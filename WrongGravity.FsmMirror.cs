using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WrongGravity
{
    /// <summary>
    /// Mirrors the two PlayMaker collision actions used by the Knight and the Superdash
    /// RayCast2d action.
    ///
    /// CheckCollisionSide is important for Crystal Dash: its rays point up/down and
    /// decide whether the Knight is touching a wall/ceiling. Under inverted gravity the
    /// ceiling is the new floor, so the top/bottom rays must be swapped. The action is
    /// reimplemented instead of calling the original because the original unconditionally
    /// uses world-down.
    ///
    /// Notes on fidelity (verified against Assembly-CSharp 1.5.78):
    /// - The action's OnEnter computes checkUp/checkDown/checkLeft/checkRight from
    ///   "FsmBool is not None OR FsmEvent is set". <see cref="ShouldCheck"/> reproduces
    ///   exactly that rule.
    /// - The game passes a LayerMask whose value is a layer *index* (normally 8,
    ///   Terrain). Vanilla evaluates <c>1 &lt;&lt; (layer &amp; 31)</c>; we do the same.
    /// - otherLayer/otherLayerNumber are resolved by the game before CheckTouching is
    ///   called, so the incoming mask is already correct.
    /// </summary>
    public partial class WrongGravity
    {
        private static bool loggedHeroCollisionMirror;

        /// <summary>
        /// Instance IDs of RayCast2d actions already logged. IDs are used instead of
        /// object references so a scene unload can release the old FSM actions.
        /// </summary>
        private static readonly HashSet<int> loggedRaycasts = new HashSet<int>();

        private const float CheckCollisionRayLength = 0.08f;

        /// <summary>Clears per-scene diagnostic state after a scene starts.</summary>
        private static void ResetFsmDiagnostics()
        {
            loggedRaycasts.Clear();
        }

        private void CheckCollisionSideTouching(
            On.HutongGames.PlayMaker.Actions.CheckCollisionSide.orig_CheckTouching orig,
            HutongGames.PlayMaker.Actions.CheckCollisionSide self,
            LayerMask layer)
        {
            if (!Config.Enabled || !Config.MirrorFsmCollision || !IsHeroFsm(self.Fsm))
            {
                orig(self, layer);
                return;
            }

            if (!loggedHeroCollisionMirror)
            {
                loggedHeroCollisionMirror = true;
                Log("[FSM] Mirroring CheckCollisionSide rays for the Knight (Crystal Dash / FSM ground checks).");
            }

            Collider2D collider = HeroController.instance != null
                ? HeroController.instance.GetComponent<Collider2D>()
                : null;
            if (collider == null)
                collider = self.Fsm.GameObject.GetComponent<Collider2D>();

            MirrorTouch(self.Fsm, collider, layer,
                self.topHit, self.topHitEvent,
                self.rightHit, self.rightHitEvent,
                self.bottomHit, self.bottomHitEvent,
                self.leftHit, self.leftHitEvent,
                self.ignoreTriggers);
        }

        private void CheckCollisionSideEnterTouching(
            On.HutongGames.PlayMaker.Actions.CheckCollisionSideEnter.orig_CheckTouching orig,
            HutongGames.PlayMaker.Actions.CheckCollisionSideEnter self,
            LayerMask layer)
        {
            if (!Config.Enabled || !Config.MirrorFsmCollision || !IsHeroFsm(self.Fsm))
            {
                orig(self, layer);
                return;
            }

            Collider2D collider = HeroController.instance != null
                ? HeroController.instance.GetComponent<Collider2D>()
                : null;
            if (collider == null)
                collider = self.Fsm.GameObject.GetComponent<Collider2D>();

            MirrorTouch(self.Fsm, collider, layer,
                self.topHit, self.topHitEvent,
                self.rightHit, self.rightHitEvent,
                self.bottomHit, self.bottomHitEvent,
                self.leftHit, self.leftHitEvent,
                self.ignoreTriggers);
        }

        /// <summary>
        /// Mirrors RayCast2d only for the Superdash FSM while a super dash is active.
        /// The local fromPosition is mirrored because it is expressed in the FSM owner's
        /// local space; world-space fromPosition is intentionally left alone (that case
        /// has not been observed in the Superdash FSM).
        /// </summary>
        private void RayCast2dHook(
            On.HutongGames.PlayMaker.Actions.RayCast2d.orig_DoRaycast orig,
            HutongGames.PlayMaker.Actions.RayCast2d self)
        {
            HeroController hero = HeroController.instance;
            if (!Config.Enabled || !Config.MirrorFsmCollision || !IsHeroFsm(self.Fsm) ||
                !IsSuperdashFsm(self.Fsm) || hero == null || hero.cState == null ||
                !hero.cState.superDashing)
            {
                orig(self);
                return;
            }

            GameObject owner = self.fromGameObject != null
                ? self.Fsm.GetOwnerDefaultTarget(self.fromGameObject)
                : null;
            bool fromLocal = owner != null;

            Vector2 oldFrom = self.fromPosition != null ? self.fromPosition.Value : Vector2.zero;
            Vector2 oldDirection = self.direction != null ? self.direction.Value : Vector2.zero;
            bool changedFrom = false;
            bool changedDirection = false;

            if (loggedRaycasts.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(self)))
            {
                Log("[FSM] RayCast2d hero fsm='" + self.Fsm.Name + "' state='" + self.Fsm.ActiveStateName +
                    "' fromLocal=" + fromLocal + " from=" + oldFrom + " dir=" + oldDirection +
                    " space=" + self.space + " distance=" + (self.distance != null ? self.distance.Value : 0f));
            }

            try
            {
                if (fromLocal && self.fromPosition != null && !self.fromPosition.IsNone)
                {
                    self.fromPosition.Value = new Vector2(oldFrom.x, -oldFrom.y);
                    changedFrom = true;
                }

                if (self.direction != null && !self.direction.IsNone)
                {
                    self.direction.Value = new Vector2(oldDirection.x, -oldDirection.y);
                    changedDirection = true;
                }

                orig(self);
            }
            finally
            {
                if (changedFrom)
                    self.fromPosition.Value = oldFrom;
                if (changedDirection)
                    self.direction.Value = oldDirection;
            }
        }

        private static bool IsSuperdashFsm(Fsm fsm)
        {
            return fsm != null && !string.IsNullOrEmpty(fsm.Name) &&
                fsm.Name.IndexOf("Superdash", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHeroFsm(Fsm fsm)
        {
            return MirrorEntity.IsHeroFsm(fsm);
        }

        /// <summary>
        /// Reimplementation of CheckCollisionSide.CheckTouching. The vertical rays are
        /// swapped (top from the bottom edge downwards, bottom from the top edge upwards);
        /// horizontal rays are unchanged.
        /// </summary>
        private static void MirrorTouch(Fsm fsm, Collider2D collider, LayerMask layer,
            FsmBool topHit, FsmEvent topEvent,
            FsmBool rightHit, FsmEvent rightEvent,
            FsmBool bottomHit, FsmEvent bottomEvent,
            FsmBool leftHit, FsmEvent leftEvent,
            FsmBool ignoreTriggers)
        {
            if (fsm == null || collider == null)
                return;

            Bounds bounds = collider.bounds;
            int mask = 1 << (layer.value & 31);
            bool ignore = ignoreTriggers != null && !ignoreTriggers.IsNone && ignoreTriggers.Value;

            if (ShouldCheck(topHit, topEvent))
            {
                bool hit = Hit(new Vector2(bounds.min.x, bounds.min.y), Vector2.down, mask, ignore)
                    || Hit(new Vector2(bounds.center.x, bounds.min.y), Vector2.down, mask, ignore)
                    || Hit(new Vector2(bounds.max.x, bounds.min.y), Vector2.down, mask, ignore);
                Apply(fsm, topHit, topEvent, hit);
            }

            if (ShouldCheck(rightHit, rightEvent))
            {
                bool hit = Hit(new Vector2(bounds.max.x, bounds.max.y), Vector2.right, mask, ignore)
                    || Hit(new Vector2(bounds.max.x, bounds.center.y), Vector2.right, mask, ignore)
                    || Hit(new Vector2(bounds.max.x, bounds.min.y), Vector2.right, mask, ignore);
                Apply(fsm, rightHit, rightEvent, hit);
            }

            if (ShouldCheck(bottomHit, bottomEvent))
            {
                bool hit = Hit(new Vector2(bounds.max.x, bounds.max.y), Vector2.up, mask, ignore)
                    || Hit(new Vector2(bounds.center.x, bounds.max.y), Vector2.up, mask, ignore)
                    || Hit(new Vector2(bounds.min.x, bounds.max.y), Vector2.up, mask, ignore);
                Apply(fsm, bottomHit, bottomEvent, hit);
            }

            if (ShouldCheck(leftHit, leftEvent))
            {
                bool hit = Hit(new Vector2(bounds.min.x, bounds.max.y), Vector2.left, mask, ignore)
                    || Hit(new Vector2(bounds.min.x, bounds.center.y), Vector2.left, mask, ignore)
                    || Hit(new Vector2(bounds.min.x, bounds.min.y), Vector2.left, mask, ignore);
                Apply(fsm, leftHit, leftEvent, hit);
            }
        }

        private static bool ShouldCheck(FsmBool hit, FsmEvent ev)
        {
            return (hit != null && !hit.IsNone) || ev != null;
        }

        private static void Apply(Fsm fsm, FsmBool hit, FsmEvent ev, bool value)
        {
            if (hit != null && !hit.IsNone)
                hit.Value = value;

            if (value && ev != null)
                fsm.Event(ev);
        }

        private static bool Hit(Vector2 origin, Vector2 direction, int mask, bool ignoreTriggers)
        {
            RaycastHit2D hit = Physics2D.Raycast(origin, direction, CheckCollisionRayLength, mask);
            return hit.collider != null && (!ignoreTriggers || !hit.collider.isTrigger);
        }
    }
}
