using GlobalEnums;
using Modding;
using System.Collections.Generic;
using UnityEngine;
using Vasi;

namespace WrongGravity
{
    /// <summary>
    /// Mod entry point and the HeroController wrappers.
    ///
    /// High-level model
    /// ----------------
    /// Hollow Knight is built around a world where "down" is -Y. This mod does not rotate
    /// physics; it keeps the level in place and makes the Knight behave as if gravity were
    /// positive:
    ///
    /// 1. The Knight's Rigidbody2D.gravityScale is kept negative, so Unity's downward
    ///    gravity accelerates the Knight upward.
    /// 2. Before every HeroController call the Y component of velocity is negated, so the
    ///    vanilla code (which assumes +Y is up) sees the world it expects. After the call
    ///    the velocity is negated again.
    /// 3. The Knight's localScale.y is flipped so the sprite/collider faces the camera
    ///    correctly after the camera projection is mirrored.
    /// 4. The camera projection is multiplied by Scale(1,-1,1), so world-up appears
    ///    screen-down and the player still perceives gravity as "down".
    /// 5. Raycasts and PlayMaker actions that are hard-coded for world-down are mirrored
    ///    in the dedicated partial files.
    ///
    /// The important invariant is that a mirrored call must not be mirrored twice. All
    /// wrappers therefore use <see cref="BeginMirror"/> / <see cref="EndMirror"/> and
    /// MirrorContext.IsActive.
    /// </summary>
    public partial class WrongGravity : Mod, IMenuMod, IGlobalSettings<WrongGravityConfig>
    {
        private MirrorData mirrorData;

        public override string GetVersion()
        {
            return VersionUtil.GetVersion<WrongGravity>();
        }

        public override void Initialize()
        {
            mirrorData = MirrorData.Load();

            if (!Config.Enabled)
            {
                Log("Gravity inversion is disabled in the mod settings; no hooks were installed. " +
                    "Change Invert Gravity and restart to enable it.");
                return;
            }

            // Camera
            On.tk2dCamera.UpdateCameraMatrix += UpdateCameraMatrix;
            On.CameraController.UpdateTargetDestinationDelta += UpdateCameraTargetDestinationDelta;
            On.GameCameras.StartScene += StartScene;

            // Hero lifecycle and movement
            On.HeroController.Awake += Awake;
            On.HeroController.Update += Update;
            On.HeroController.FixedUpdate += FixedUpdate;
            On.HeroController.Move += Move;
            On.HeroController.Jump += Jump;
            On.HeroController.DoubleJump += DoubleJump;
            On.HeroController.Dash += Dash;
            On.HeroController.CancelHeroJump += CancelHeroJump;
            On.HeroController.TakeDamage += TakeDamage;
            On.HeroController.BounceHigh += BounceHigh;
            On.HeroController.ShroomBounce += ShroomBounce;
            On.HeroController.RecoilLeft += RecoilLeft;
            On.HeroController.RecoilRight += RecoilRight;
            On.HeroController.RecoilRightLong += RecoilRightLong;
            On.HeroController.RecoilLeftLong += RecoilLeftLong;
            On.HeroController.RecoilDown += RecoilDown;
            On.HeroController.CanDreamNail += CanDreamNail;
            On.HeroController.AffectedByGravity += AffectedByGravity;
            On.HeroController.FallCheck += FallCheck;
            On.HeroController.JumpReleased += JumpReleased;

            // Hero collision / ground queries. These are full reimplementations, not
            // post-processing wrappers, because the vanilla methods hard-code Vector2.down.
            On.HeroController.CheckStillTouchingWall += CheckStillTouchingWall;
            On.HeroController.CheckForBump += CheckForBump;
            On.HeroController.CheckNearRoof += CheckNearRoof;
            On.HeroController.CheckTouchingGround += CheckTouchingGround;
            On.HeroController.CheckTouching += CheckTouching;
            On.HeroController.CheckTouchingAdvanced += CheckTouchingAdvanced;
            On.HeroController.FindCollisionDirection += FindCollisionDirection;
            On.HeroController.FindGroundPoint += FindGroundPoint;
            On.HeroController.FindGroundPointY += FindGroundPointY;

            // Doors and scene transitions
            On.TransitionPoint.GetGatePosition += GetGatePosition;
            On.HeroController.EnterScene += EnterSceneHook;
            On.HeroController.LeaveScene += LeaveSceneHook;
            On.GameManager.BeginSceneTransition += BeginSceneTransition;
            On.SceneManager.DrawBlackBorders += DrawBlackBorders;

            // PlayMaker actions used by hero child FSMs
            On.HutongGames.PlayMaker.Actions.CheckCollisionSide.CheckTouching += CheckCollisionSideTouching;
            On.HutongGames.PlayMaker.Actions.CheckCollisionSideEnter.CheckTouching += CheckCollisionSideEnterTouching;
            On.HutongGames.PlayMaker.Actions.RayCast2d.DoRaycast += RayCast2dHook;
            On.HutongGames.PlayMaker.Actions.SetVelocity2d.DoSetVelocity += SetVelocity2dHook;
            On.HutongGames.PlayMaker.Actions.SetVelocity2dIfFalse.DoSetVelocity += SetVelocity2dIfFalseHook;
            On.HutongGames.PlayMaker.Actions.GetVelocity2d.DoGetVelocity += GetVelocity2dHook;
            On.HutongGames.PlayMaker.Actions.SetGravity2dScale.DoSetGravityScale += SetGravity2dScaleHook;

            // Bench seating offsets and interaction-only horizontal alignment.
            On.HutongGames.PlayMaker.Actions.Vector3Add.DoVector3Add += BenchVector3AddHook;
            On.HutongGames.PlayMaker.Actions.Translate.DoTranslate += BenchTranslateHook;
            On.HutongGames.PlayMaker.Actions.GetPosition.DoGetPosition += InteractionGetPositionHook;

            // Enemy knockback: Recoil centralises HealthManager/BeginRecoil/dream-nail hits.
            On.Recoil.RecoilByDirection += RecoilByDirectionHook;

            // Wingmoulds use a fixed wound animation whose sprite needs its own mirror.
            On.HutongGames.PlayMaker.Actions.Tk2dPlayAnimation.DoPlayAnimation += PalaceFlyPlayAnimationHook;

            Log("Hooks installed; gravity inversion active.");
        }

        // -------------------------------------------------------------------------
        // Camera
        // -------------------------------------------------------------------------

        /// <summary>
        /// Mirrors the final projection of the tk2d camera after it has rebuilt its
        /// matrix. This is the single source of truth for the main gameplay camera.
        /// </summary>
        private void UpdateCameraMatrix(On.tk2dCamera.orig_UpdateCameraMatrix orig, tk2dCamera self)
        {
            orig(self);

            if (!Config.Enabled || GameManager.instance == null || !GameManager.instance.IsGameplayScene())
                return;
            if (GameCameras.instance == null || self == null)
                return;

            Camera camera = self.GetComponent<Camera>();
            if (camera == null)
                return;

            // Multiplying on the right flips the view-space Y axis. For an orthographic
            // projection this negates the vertical screen axis; tk2d uses an orthographic
            // matrix, so this is the intended mirror.
            camera.projectionMatrix *= Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
        }

        /// <summary>
        /// Mirrors the vertical look distance before the camera consumes it. Vanilla
        /// LateUpdate sets lookOffset to heroY - targetY +/- 6, then calculates the
        /// destination, applies lock/scene bounds and smooths the movement. Only the
        /// +/- 6 look distance changes sign; the hero-to-target correction stays in
        /// world space. Recomputing it also avoids double flips when scene positioning
        /// calls UpdateTargetDestinationDelta again.
        /// </summary>
        private void UpdateCameraTargetDestinationDelta(
            On.CameraController.orig_UpdateTargetDestinationDelta orig,
            CameraController self)
        {
            if (Config.Enabled && self != null && self.camTarget != null)
            {
                HeroController hero = Mirror.GetField<CameraController, HeroController>(self, "hero_ctrl");
                if (hero != null && hero.cState != null &&
                    (hero.cState.lookingUp || hero.cState.lookingDown))
                {
                    // Screen-up is world-down under the mirrored projection.
                    float lookDistance = hero.cState.lookingUp ? -6f : 6f;
                    self.lookOffset = hero.transform.position.y - self.camTarget.transform.position.y + lookDistance;
                }
            }

            orig(self);
        }

        /// <summary>
        /// Marks a camera that already received the one-time StartScene flip. The main
        /// tk2d camera is deliberately skipped here because UpdateCameraMatrix flips it
        /// every frame; flipping both would produce a double (non-mirrored) image.
        /// </summary>
        private void StartScene(On.GameCameras.orig_StartScene orig, GameCameras self)
        {
            orig(self);

            if (!Config.Enabled || self == null || self.tk2dCam == null)
                return;

            Camera mainCamera = self.tk2dCam.GetComponent<Camera>();
            Camera[] cameras = self.tk2dCam.GetComponentsInChildren<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || camera == mainCamera)
                    continue;

                // A nested tk2dCamera flips itself in UpdateCameraMatrix; skip it here
                // as well so it never receives two flips.
                if (camera.GetComponent<tk2dCamera>() != null)
                    continue;

                CameraProjectionMarker marker = camera.gameObject.GetComponent<CameraProjectionMarker>();
                if (marker == null)
                    marker = camera.gameObject.AddComponent<CameraProjectionMarker>();

                if (marker.Flipped)
                    continue;

                camera.projectionMatrix *= Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
                marker.Flipped = true;
            }

            ResetFsmDiagnostics();
            ScheduleInteractionRefresh(self);
        }

        // -------------------------------------------------------------------------
        // Mirror primitives
        // -------------------------------------------------------------------------

        /// <summary>
        /// Enters mirrored mode for one HeroController call. Returns false when the call
        /// is already nested inside a mirrored wrapper (or the mod is disabled).
        /// </summary>
        private bool BeginMirror(HeroController self)
        {
            if (!Config.Enabled || self == null || MirrorContext.IsActive)
                return false;

            MirrorContext.Enter();
            try
            {
                NegateVelocityY(self);
            }
            catch
            {
                MirrorContext.Exit();
                throw;
            }
            return true;
        }

        /// <summary>Leaves mirrored mode and restores the velocity sign.</summary>
        private void EndMirror(HeroController self, bool entered)
        {
            if (!entered)
                return;

            try
            {
                NegateVelocityY(self);
            }
            finally
            {
                MirrorContext.Exit();
            }
        }

        /// <summary>Flips only the Y component; X and angular velocity are untouched.</summary>
        private static void NegateVelocityY(HeroController self)
        {
            if (self == null)
                return;

            Rigidbody2D body = Mirror.GetField<HeroController, Rigidbody2D>(self, "rb2d");
            if (body == null)
                return;

            Vector2 velocity = body.velocity;
            body.velocity = new Vector2(velocity.x, -velocity.y);
        }

        /// <summary>
        /// Flips the hero's localScale so the sprite and collider line up with the
        /// mirrored camera. This is intentionally done once in Awake; the scene-entry
        /// speed is also adjusted because the Knight now enters "down" scenes with a
        /// positive velocity.
        /// </summary>
        private void Awake(On.HeroController.orig_Awake orig, HeroController self)
        {
            orig(self);

            Vector3 localScale = self.transform.localScale;
            localScale.y *= -1f;
            self.transform.localScale = localScale;

            if (self.SPEED_TO_ENTER_SCENE_DOWN < 0f)
                self.SPEED_TO_ENTER_SCENE_DOWN *= -1f;
        }

        // -------------------------------------------------------------------------
        // Frame drivers
        // -------------------------------------------------------------------------

        private void Update(On.HeroController.orig_Update orig, HeroController self)
        {
            if (!Config.Enabled)
            {
                MirrorContext.Reset();
                TickTopKillPlane();
                orig(self);
                return;
            }

            // Clear leaked nesting before doing anything; the finally below will only
            // close the mirror it opened here.
            MirrorContext.Reset();
            EnsureSuperdashPatched(self);
            TickTopKillPlane();

            bool entered = BeginMirror(self);
            try
            {
                orig(self);
            }
            finally
            {
                EndMirror(self, entered);
                TickTransition(self);
                TickSuperdash(self);
            }
        }

        private void FixedUpdate(On.HeroController.orig_FixedUpdate orig, HeroController self)
        {
            if (!Config.Enabled)
            {
                MirrorContext.Reset();
                EnsureGravityScalePositive(self);
                orig(self);
                return;
            }

            MirrorContext.Reset();
            bool entered = BeginMirror(self);
            try
            {
                orig(self);
            }
            finally
            {
                EndMirror(self, entered);
                EnsureGravityScaleNegative(self);
            }
        }

        private static void EnsureGravityScaleNegative(HeroController self)
        {
            if (self == null)
                return;

            Rigidbody2D body = Mirror.GetField<HeroController, Rigidbody2D>(self, "rb2d");
            if (body != null && body.gravityScale > 0f)
                body.gravityScale *= -1f;
        }

        private static void EnsureGravityScalePositive(HeroController self)
        {
            if (self == null)
                return;

            Rigidbody2D body = Mirror.GetField<HeroController, Rigidbody2D>(self, "rb2d");
            if (body != null && body.gravityScale < 0f)
                body.gravityScale *= -1f;
        }

        // -------------------------------------------------------------------------
        // Simple HeroController wrappers
        //
        // Every wrapper uses try/finally so an exception in the original method cannot
        // leave the Knight's velocity mirrored. This is the main structural change from
        // the old "call orig then Flip" pattern.
        // -------------------------------------------------------------------------

        private void Move(On.HeroController.orig_Move orig, HeroController self, float moveDirection)
        {
            bool entered = BeginMirror(self);
            try { orig(self, moveDirection); }
            finally { EndMirror(self, entered); }
        }

        private void Jump(On.HeroController.orig_Jump orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void DoubleJump(On.HeroController.orig_DoubleJump orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void Dash(On.HeroController.orig_Dash orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void CancelHeroJump(On.HeroController.orig_CancelHeroJump orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        public void TakeDamage(On.HeroController.orig_TakeDamage orig, HeroController self,
            GameObject go, CollisionSide damageSide, int damageAmount, int hazardType)
        {
            bool entered = BeginMirror(self);
            try { orig(self, go, damageSide, damageAmount, hazardType); }
            finally { EndMirror(self, entered); }
        }

        private void BounceHigh(On.HeroController.orig_BounceHigh orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void ShroomBounce(On.HeroController.orig_ShroomBounce orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void RecoilLeft(On.HeroController.orig_RecoilLeft orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void RecoilRight(On.HeroController.orig_RecoilRight orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void RecoilRightLong(On.HeroController.orig_RecoilRightLong orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void RecoilLeftLong(On.HeroController.orig_RecoilLeftLong orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void RecoilDown(On.HeroController.orig_RecoilDown orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private bool CanDreamNail(On.HeroController.orig_CanDreamNail orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { return orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void FallCheck(On.HeroController.orig_FallCheck orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        private void JumpReleased(On.HeroController.orig_JumpReleased orig, HeroController self)
        {
            bool entered = BeginMirror(self);
            try { orig(self); }
            finally { EndMirror(self, entered); }
        }

        /// <summary>
        /// AffectedByGravity is not a simple velocity flip: it toggles gravityScale. The
        /// vanilla implementation only acts when gravityScale is positive, so the wrapper
        /// temporarily makes it positive, lets vanilla run, then restores the negative
        /// sign. A zero scale is preserved as zero.
        /// </summary>
        private void AffectedByGravity(On.HeroController.orig_AffectedByGravity orig,
            HeroController self, bool gravityApplies)
        {
            if (!Config.Enabled)
            {
                orig(self, gravityApplies);
                return;
            }

            Rigidbody2D body = Mirror.GetField<HeroController, Rigidbody2D>(self, "rb2d");
            if (body == null)
            {
                orig(self, gravityApplies);
                return;
            }

            if (body.gravityScale < 0f)
                body.gravityScale *= -1f;

            try
            {
                orig(self, gravityApplies);
            }
            finally
            {
                if (body.gravityScale > 0f)
                    body.gravityScale *= -1f;
            }
        }

        // -------------------------------------------------------------------------
        // Reimplemented HeroController collision / ground queries
        // -------------------------------------------------------------------------

        /// <summary>
        /// Reimplementation of CheckStillTouchingWall. Walls stay vertical, so the
        /// horizontal rays are unchanged; only the optional "top" ray is moved to the
        /// world bottom (the Knight's head under inverted gravity).
        /// </summary>
        private bool CheckStillTouchingWall(On.HeroController.orig_CheckStillTouchingWall orig,
            HeroController self, CollisionSide side, bool checkTop = false)
        {
            if (!Config.Enabled)
                return orig(self, side, checkTop);

            Collider2D collider = Mirror.GetField<HeroController, Collider2D>(self, "col2d");
            if (collider == null)
                return false;

            Vector2 originTop = new Vector2(collider.bounds.min.x, collider.bounds.min.y);
            Vector2 originMiddle = new Vector2(collider.bounds.min.x, collider.bounds.center.y);
            Vector2 originBottom = new Vector2(collider.bounds.min.x, collider.bounds.max.y);
            Vector2 originTopRight = new Vector2(collider.bounds.max.x, collider.bounds.min.y);
            Vector2 originMiddleRight = new Vector2(collider.bounds.max.x, collider.bounds.center.y);
            Vector2 originBottomRight = new Vector2(collider.bounds.max.x, collider.bounds.max.y);

            const float distance = 0.1f;
            RaycastHit2D topHit = default(RaycastHit2D);
            RaycastHit2D middleHit = default(RaycastHit2D);
            RaycastHit2D bottomHit = default(RaycastHit2D);

            if (side == CollisionSide.left)
            {
                if (checkTop)
                    topHit = Physics2D.Raycast(originTop, Vector2.left, distance, 256);
                middleHit = Physics2D.Raycast(originMiddle, Vector2.left, distance, 256);
                bottomHit = Physics2D.Raycast(originBottom, Vector2.left, distance, 256);
            }
            else if (side == CollisionSide.right)
            {
                if (checkTop)
                    topHit = Physics2D.Raycast(originTopRight, Vector2.right, distance, 256);
                middleHit = Physics2D.Raycast(originMiddleRight, Vector2.right, distance, 256);
                bottomHit = Physics2D.Raycast(originBottomRight, Vector2.right, distance, 256);
            }
            else
            {
                LogError("Invalid CollisionSide specified: " + side);
                return false;
            }

            if (IsSolidWallHit(middleHit) || IsSolidWallHit(bottomHit))
                return true;

            return checkTop && IsSolidWallHit(topHit);
        }

        private static bool IsSolidWallHit(RaycastHit2D hit)
        {
            if (hit.collider == null || hit.collider.isTrigger)
                return false;
            if (hit.collider.GetComponent<SteepSlope>() != null)
                return false;
            if (hit.collider.GetComponent<NonSlider>() != null)
                return false;
            return true;
        }

        /// <summary>
        /// Reimplementation of CheckForBump for the mirrored feet.
        ///
        /// Vanilla probes two heights near the bottom edge, casts horizontally, then casts
        /// downwards from a point one unit above the hit point to find the step surface.
        /// Under inverted gravity the probes are near the top edge and the vertical cast
        /// must go upwards from one unit *below* the hit point. The old code changed the
        /// direction but kept +1, which started the ray on the wrong side of the step.
        /// </summary>
        private bool CheckForBump(On.HeroController.orig_CheckForBump orig, HeroController self,
            CollisionSide side)
        {
            if (!Config.Enabled)
                return orig(self, side);

            Collider2D collider = Mirror.GetField<HeroController, Collider2D>(self, "col2d");
            if (collider == null)
                return false;

            const float inset = 0.2f;
            const float edge = 0.025f;
            const float castLength = 0.52f;

            Vector2 innerLeft = new Vector2(collider.bounds.min.x + inset, collider.bounds.max.y - 0.2f);
            Vector2 outerLeft = new Vector2(collider.bounds.min.x + inset, collider.bounds.max.y + edge);
            Vector2 innerRight = new Vector2(collider.bounds.max.x - inset, collider.bounds.max.y - 0.2f);
            Vector2 outerRight = new Vector2(collider.bounds.max.x - inset, collider.bounds.max.y + edge);

            RaycastHit2D outerHit = default(RaycastHit2D);
            RaycastHit2D innerHit = default(RaycastHit2D);

            if (side == CollisionSide.left)
            {
                if (Config.DebugDraw)
                {
                    Debug.DrawLine(outerLeft, outerLeft + Vector2.left * castLength, Color.cyan, 0.15f);
                    Debug.DrawLine(innerLeft, innerLeft + Vector2.left * castLength, Color.cyan, 0.15f);
                }
                outerHit = Physics2D.Raycast(outerLeft, Vector2.left, castLength, 256);
                innerHit = Physics2D.Raycast(innerLeft, Vector2.left, castLength, 256);
            }
            else if (side == CollisionSide.right)
            {
                if (Config.DebugDraw)
                {
                    Debug.DrawLine(outerRight, outerRight + Vector2.right * castLength, Color.cyan, 0.15f);
                    Debug.DrawLine(innerRight, innerRight + Vector2.right * castLength, Color.cyan, 0.15f);
                }
                outerHit = Physics2D.Raycast(outerRight, Vector2.right, castLength, 256);
                innerHit = Physics2D.Raycast(innerRight, Vector2.right, castLength, 256);
            }
            else
            {
                LogError("Invalid CollisionSide specified: " + side);
                return false;
            }

            if (outerHit.collider == null || innerHit.collider != null)
                return false;

            Vector2 firstOrigin = outerHit.point + new Vector2(
                side == CollisionSide.right ? 0.1f : -0.1f, -1f);
            RaycastHit2D first = Physics2D.Raycast(firstOrigin, Vector2.up, 1.5f, 256);

            Vector2 secondOrigin = outerHit.point + new Vector2(
                side == CollisionSide.right ? -0.1f : 0.1f, -1f);
            RaycastHit2D second = Physics2D.Raycast(secondOrigin, Vector2.up, 1.5f, 256);

            if (first.collider == null)
                return false;

            if (Config.DebugDraw)
                Debug.DrawLine(firstOrigin, first.point, Color.cyan, 0.15f);

            if (second.collider == null)
                return true;

            if (Config.DebugDraw)
                Debug.DrawLine(secondOrigin, second.point, Color.cyan, 0.15f);

            // Screen-space "higher" is world-space lower because the camera is mirrored.
            // Vanilla returns true when hit4.y - hit3.y > 0; the mirrored comparison is
            // the opposite sign.
            float height = first.point.y - second.point.y;
            if (height > 0f)
            {
                if (Config.VerboseLog)
                    Log("[Bump] height=" + height + " side=" + side);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reimplementation of CheckNearRoof: the roof is now world-down, so the diagonal
        /// and vertical probes originate at bounds.min.y and point downwards/upwards as
        /// the mirror of vanilla.
        /// </summary>
        private bool CheckNearRoof(On.HeroController.orig_CheckNearRoof orig, HeroController self)
        {
            if (!Config.Enabled)
                return orig(self);

            Collider2D collider = Mirror.GetField<HeroController, Collider2D>(self, "col2d");
            if (collider == null)
                return false;

            Vector2 left = new Vector2(collider.bounds.min.x, collider.bounds.min.y);
            Vector2 right = new Vector2(collider.bounds.max.x, collider.bounds.min.y);
            Vector2 quarterRight = new Vector2(collider.bounds.center.x + collider.bounds.size.x / 4f,
                collider.bounds.min.y);
            Vector2 quarterLeft = new Vector2(collider.bounds.center.x - collider.bounds.size.x / 4f,
                collider.bounds.min.y);

            RaycastHit2D leftDiagonal = Physics2D.Raycast(left, new Vector2(-0.5f, -1f), 2f, 256);
            RaycastHit2D rightDiagonal = Physics2D.Raycast(right, new Vector2(0.5f, -1f), 2f, 256);
            RaycastHit2D quarterRightHit = Physics2D.Raycast(quarterRight, Vector2.down, 1f, 256);
            RaycastHit2D quarterLeftHit = Physics2D.Raycast(quarterLeft, Vector2.down, 1f, 256);

            return leftDiagonal.collider != null || rightDiagonal.collider != null ||
                quarterRightHit.collider != null || quarterLeftHit.collider != null;
        }

        /// <summary>
        /// Reimplementation of CheckTouchingGround: the ground is now above the Knight,
        /// so the three rays point up from the old feet positions.
        /// </summary>
        private bool CheckTouchingGround(On.HeroController.orig_CheckTouchingGround orig, HeroController self)
        {
            if (!Config.Enabled)
                return orig(self);

            Collider2D collider = Mirror.GetField<HeroController, Collider2D>(self, "col2d");
            if (collider == null)
                return false;

            Vector2 left = new Vector2(collider.bounds.min.x, collider.bounds.center.y);
            Vector2 center = collider.bounds.center;
            Vector2 right = new Vector2(collider.bounds.max.x, collider.bounds.center.y);
            float distance = collider.bounds.extents.y + 0.16f;

            if (Config.DebugDraw)
            {
                Debug.DrawRay(left, Vector2.up, Color.yellow);
                Debug.DrawRay(center, Vector2.up, Color.yellow);
                Debug.DrawRay(right, Vector2.up, Color.yellow);
            }

            RaycastHit2D leftHit = Physics2D.Raycast(left, Vector2.up, distance, 256);
            RaycastHit2D centerHit = Physics2D.Raycast(center, Vector2.up, distance, 256);
            RaycastHit2D rightHit = Physics2D.Raycast(right, Vector2.up, distance, 256);

            return leftHit.collider != null || centerHit.collider != null || rightHit.collider != null;
        }

        /// <summary>
        /// Reimplementation of CheckTouching. Vertical hit labels are swapped because a
        /// downward ray now represents the Knight's "top" side and an upward ray the
        /// "bottom" side. Horizontal labels are unchanged.
        /// </summary>
        private List<CollisionSide> CheckTouching(On.HeroController.orig_CheckTouching orig,
            HeroController self, PhysLayers layer)
        {
            if (!Config.Enabled)
                return orig(self, layer);

            List<CollisionSide> sides = new List<CollisionSide>(4);
            Collider2D collider = Mirror.GetField<HeroController, Collider2D>(self, "col2d");
            if (collider == null)
                return sides;

            Vector3 center = collider.bounds.center;
            float distanceX = collider.bounds.extents.x + 0.16f;
            float distanceY = collider.bounds.extents.y + 0.16f;
            int mask = 1 << (int)layer;

            RaycastHit2D down = Physics2D.Raycast(center, Vector2.down, distanceY, mask);
            RaycastHit2D right = Physics2D.Raycast(center, Vector2.right, distanceX, mask);
            RaycastHit2D up = Physics2D.Raycast(center, Vector2.up, distanceY, mask);
            RaycastHit2D left = Physics2D.Raycast(center, Vector2.left, distanceX, mask);

            if (down.collider != null)
                sides.Add(CollisionSide.top);
            if (right.collider != null)
                sides.Add(CollisionSide.right);
            if (up.collider != null)
                sides.Add(CollisionSide.bottom);
            if (left.collider != null)
                sides.Add(CollisionSide.left);

            return sides;
        }

        /// <summary>
        /// Reimplementation of CheckTouchingAdvanced. Same label swap as CheckTouching,
        /// but with three probes per side at the collider edges.
        /// </summary>
        private List<CollisionSide> CheckTouchingAdvanced(
            On.HeroController.orig_CheckTouchingAdvanced orig,
            HeroController self,
            PhysLayers layer)
        {
            if (!Config.Enabled)
                return orig(self, layer);

            List<CollisionSide> sides = new List<CollisionSide>();
            Collider2D collider = Mirror.GetField<HeroController, Collider2D>(self, "col2d");
            if (collider == null)
                return sides;

            Bounds bounds = collider.bounds;
            Vector2 topLeft = new Vector2(bounds.min.x, bounds.max.y);
            Vector2 topCenter = new Vector2(bounds.center.x, bounds.max.y);
            Vector2 topRight = new Vector2(bounds.max.x, bounds.max.y);
            Vector2 middleLeft = new Vector2(bounds.min.x, bounds.center.y);
            Vector2 middleRight = new Vector2(bounds.max.x, bounds.center.y);
            Vector2 bottomLeft = new Vector2(bounds.min.x, bounds.min.y);
            Vector2 bottomCenter = new Vector2(bounds.center.x, bounds.min.y);
            Vector2 bottomRight = new Vector2(bounds.max.x, bounds.min.y);

            int mask = 1 << (int)layer;
            RaycastHit2D upLeft = Physics2D.Raycast(topLeft, Vector2.up, 0.16f, mask);
            RaycastHit2D upCenter = Physics2D.Raycast(topCenter, Vector2.up, 0.16f, mask);
            RaycastHit2D upRight = Physics2D.Raycast(topRight, Vector2.up, 0.16f, mask);

            RaycastHit2D rightTop = Physics2D.Raycast(topRight, Vector2.right, 0.16f, mask);
            RaycastHit2D rightMiddle = Physics2D.Raycast(middleRight, Vector2.right, 0.16f, mask);
            RaycastHit2D rightBottom = Physics2D.Raycast(bottomRight, Vector2.right, 0.16f, mask);

            RaycastHit2D downRight = Physics2D.Raycast(bottomRight, Vector2.down, 0.16f, mask);
            RaycastHit2D downCenter = Physics2D.Raycast(bottomCenter, Vector2.down, 0.16f, mask);
            RaycastHit2D downLeft = Physics2D.Raycast(bottomLeft, Vector2.down, 0.16f, mask);

            RaycastHit2D leftBottom = Physics2D.Raycast(bottomLeft, Vector2.left, 0.16f, mask);
            RaycastHit2D leftMiddle = Physics2D.Raycast(middleLeft, Vector2.left, 0.16f, mask);
            RaycastHit2D leftTop = Physics2D.Raycast(topLeft, Vector2.left, 0.16f, mask);

            if (downRight.collider != null || downCenter.collider != null || downLeft.collider != null)
                sides.Add(CollisionSide.top);
            if (rightTop.collider != null || rightMiddle.collider != null || rightBottom.collider != null)
                sides.Add(CollisionSide.right);
            if (upLeft.collider != null || upCenter.collider != null || upRight.collider != null)
                sides.Add(CollisionSide.bottom);
            if (leftBottom.collider != null || leftMiddle.collider != null || leftTop.collider != null)
                sides.Add(CollisionSide.left);

            return sides;
        }

        /// <summary>
        /// The collision direction from Unity is world-space. Swap top/bottom so the
        /// rest of the game receives the mirrored semantic side.
        /// </summary>
        private CollisionSide FindCollisionDirection(
            On.HeroController.orig_FindCollisionDirection orig,
            HeroController self,
            Collision2D collision)
        {
            if (!Config.Enabled)
                return orig(self, collision);

            CollisionSide side = orig(self, collision);
            if (side == CollisionSide.top)
                return CollisionSide.bottom;
            if (side == CollisionSide.bottom)
                return CollisionSide.top;
            return side;
        }

        // -------------------------------------------------------------------------
        // Door side
        // -------------------------------------------------------------------------

        /// <summary>
        /// Gate positions are consumed by EnterScene, LeaveScene and
        /// TransitionPoint.OnTriggerEnter2D. The mod swaps top/bottom for all of them:
        /// the game then thinks the Knight enters from the mirrored side, while
        /// LeaveSceneHook swaps the value back before vanilla computes transition_vel.
        /// </summary>
        private GatePosition GetGatePosition(On.TransitionPoint.orig_GetGatePosition orig, TransitionPoint self)
        {
            GatePosition worldSide = orig(self);
            if (!Config.Enabled)
                return worldSide;

            GatePosition effectiveSide = worldSide;
            if (effectiveSide == GatePosition.top)
                effectiveSide = GatePosition.bottom;
            else if (effectiveSide == GatePosition.bottom)
                effectiveSide = GatePosition.top;

            LogGateSide(self, worldSide, effectiveSide);
            return effectiveSide;
        }
    }

    /// <summary>
    /// Marker for camera objects that already received the one-time StartScene flip.
    /// Runtime-only; never serialized into a scene.
    /// </summary>
    public sealed class CameraProjectionMarker : MonoBehaviour
    {
        public bool Flipped;
    }
}
