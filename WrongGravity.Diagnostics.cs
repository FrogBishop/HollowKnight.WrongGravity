using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GlobalEnums;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WrongGravity
{
    /// <summary>
    /// Door handling, the top kill plane, and the Crystal Dash FSM patch.
    ///
    /// This file intentionally keeps the three runtime "repair" systems together:
    /// - doors need side swaps because the camera/world is vertically mirrored;
    /// - the top kill plane replaces the vanilla "fall off the bottom" punishment;
    /// - Crystal Dash needs a targeted FSM patch because its charge state has a
    ///   FINISHED transition that would otherwise auto-release the dash.
    /// </summary>
    public partial class WrongGravity
    {
        // -------------------------------------------------------------------------
        // Door / transition state
        // -------------------------------------------------------------------------

        private float droppingStartedAt = -1f;
        private static MethodInfo finishedEnteringSceneMethod;

        /// <summary>
        /// Logs every scene transition. sourceGate comes from TransitionPoint.lastEntered;
        /// it is reliable for normal gates but can be stale for isADoor gates because
        /// that static field is only assigned on the non-door path of OnTriggerEnter2D.
        /// </summary>
        private void BeginSceneTransition(
            On.GameManager.orig_BeginSceneTransition orig,
            GameManager self,
            GameManager.SceneLoadInfo info)
        {
            ResetInteractionCache();

            if (Config.LogDoors && info != null)
            {
                string sourceScene = self != null ? self.sceneName : "?";
                string sourceGate = TransitionPoint.lastEntered ?? "";
                string destScene = info.SceneName ?? "?";
                string destGate = info.EntryGateName ?? "?";
                string leave = info.HeroLeaveDirection.HasValue
                    ? info.HeroLeaveDirection.Value.ToString()
                    : "?";

                Log("[Door] " + sourceScene + " | " + sourceGate + " -> " + destScene + " | " +
                    destGate + " | leave=" + leave);
            }

            orig(self, info);
        }

        private void LeaveSceneHook(
            On.HeroController.orig_LeaveScene orig,
            HeroController self,
            GatePosition? gate)
        {
            if (!Config.Enabled)
            {
                orig(self, gate);
                return;
            }

            GatePosition? worldSide = gate;
            if (gate == GatePosition.top)
                worldSide = GatePosition.bottom;
            else if (gate == GatePosition.bottom)
                worldSide = GatePosition.top;

            if (Config.LogDoors)
                Log("[Door] LeaveScene effective=" + gate + " world=" + worldSide);

            orig(self, worldSide);
        }

        /// <summary>
        /// Applies a custom entryOffset before the vanilla EnterScene coroutine runs and
        /// restores the shared TransitionPoint field afterwards.
        ///
        /// The restore is also performed when the original method throws, so a failed
        /// scene entry cannot leave a modified gate offset in the scene.
        /// </summary>
        private IEnumerator EnterSceneHook(
            On.HeroController.orig_EnterScene orig,
            HeroController self,
            TransitionPoint gate,
            float delayBeforeEnter)
        {
            if (!Config.Enabled || gate == null)
                return orig(self, gate, delayBeforeEnter);

            Vector2 original = gate.entryOffset;
            IEnumerator routine;
            try
            {
                ApplyEntryOffset(gate);
                routine = orig(self, gate, delayBeforeEnter);
            }
            catch
            {
                gate.entryOffset = original;
                throw;
            }

            return RestoreEntryOffset(routine, gate, original);
        }

        private IEnumerator RestoreEntryOffset(IEnumerator routine, TransitionPoint gate, Vector2 original)
        {
            try
            {
                if (routine != null)
                {
                    while (routine.MoveNext())
                        yield return routine.Current;
                }
            }
            finally
            {
                if (gate != null)
                    gate.entryOffset = original;
            }
        }

        /// <summary>
        /// Chooses the entry offset. A JSON profile wins when applyEntryOffset is true.
        /// Otherwise the gate-name convention is used as a pragmatic default: most gates
        /// are named top1/bot2/left1/right1, so the name is a reliable world-side hint.
        /// Unusual gates can be fixed with a DoorProfile instead of growing more special
        /// cases here.
        /// </summary>
        private void ApplyEntryOffset(TransitionPoint gate)
        {
            if (gate == null)
                return;

            string scene = gate.gameObject.scene.name;
            DoorProfile profile = mirrorData != null ? mirrorData.GetDoor(scene, gate.name) : null;

            if (profile != null && profile.applyEntryOffset)
            {
                gate.entryOffset = new Vector2(profile.entryOffsetX, profile.entryOffsetY);
                if (Config.LogDoors)
                    Log("[Door] profile offset scene='" + scene + "' gate='" + gate.name +
                        "' offset=" + gate.entryOffset);
                return;
            }

            // This deliberately uses the *world* side from the gate name, not the
            // mirrored GetGatePosition result. The values mirror vanilla's top/bottom
            // offsets; the actual EnterScene branch uses the effective side elsewhere.
            GatePosition side = GetVanillaGateSide(gate);
            if (side == GatePosition.top)
                gate.entryOffset = new Vector2(gate.entryOffset.x, -6f);
            else if (side == GatePosition.bottom)
                gate.entryOffset = new Vector2(gate.entryOffset.x, -1.5f);
        }

        /// <summary>
        /// Local copy of vanilla TransitionPoint.GetGatePosition's naming convention.
        /// It intentionally does not call the hooked GetGatePosition because we need the
        /// uncompressed world side here.
        /// </summary>
        private static GatePosition GetVanillaGateSide(TransitionPoint gate)
        {
            string text = gate.name;
            if (text.Contains("top"))
                return GatePosition.top;
            if (text.Contains("right"))
                return GatePosition.right;
            if (text.Contains("left"))
                return GatePosition.left;
            if (text.Contains("bot"))
                return GatePosition.bottom;
            if (text.Contains("door") || gate.isADoor)
                return GatePosition.door;
            return GatePosition.unknown;
        }

        /// <summary>
        /// Watchdog for a hero that never receives control after DROPPING_DOWN.
        /// It only acts on that specific transition state; other transition states are
        /// left to the game.
        /// </summary>
        private void TickTransition(HeroController self)
        {
            if (!Config.Enabled || self == null || self.cState == null)
                return;

            if (!self.cState.transitioning || self.transitionState != HeroTransitionState.DROPPING_DOWN)
            {
                droppingStartedAt = -1f;
                return;
            }

            if (droppingStartedAt < 0f)
            {
                droppingStartedAt = Time.realtimeSinceStartup;
                return;
            }

            float timeout = GetTransitionTimeout(self);
            if (timeout <= 0f)
                timeout = 3f;

            if (Time.realtimeSinceStartup - droppingStartedAt < timeout)
                return;

            ForceFinishEnteringScene(self, timeout);
        }

        private float GetTransitionTimeout(HeroController self)
        {
            string scene = "";
            string gate = "";

            if (self.sceneEntryGate != null)
            {
                scene = self.sceneEntryGate.gameObject.scene.name;
                gate = self.sceneEntryGate.name;
            }
            else if (GameManager.instance != null)
            {
                scene = GameManager.instance.sceneName;
            }

            float timeout = mirrorData != null ? mirrorData.GetDoorTimeout(scene, gate) : 0f;
            return timeout > 0f ? timeout : Config.TransitionTimeout;
        }

        private void ForceFinishEnteringScene(HeroController self, float timeout)
        {
            droppingStartedAt = -1f;

            if (finishedEnteringSceneMethod == null)
            {
                finishedEnteringSceneMethod = typeof(HeroController).GetMethod(
                    "FinishedEnteringScene",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            string scene = GameManager.instance != null
                ? GameManager.instance.sceneName
                : self.gameObject.scene.name;
            string gate = self.sceneEntryGate != null ? self.sceneEntryGate.name : "?";
            Rigidbody2D body = self.GetComponent<Rigidbody2D>();

            LogWarn("[Door] Forced FinishedEnteringScene after " + timeout.ToString("0.0") +
                "s scene='" + scene + "' gate='" + gate + "' position=" + self.transform.position +
                " velocity=" + (body != null ? body.velocity.ToString() : "?"));

            if (finishedEnteringSceneMethod == null)
            {
                LogError("[Door] FinishedEnteringScene not found by reflection.");
                return;
            }

            try
            {
                finishedEnteringSceneMethod.Invoke(self, new object[] { true, false });
            }
            catch (Exception ex)
            {
                LogError("[Door] Failed to force FinishedEnteringScene: " + ex);
            }
        }

        private void LogGateSide(TransitionPoint gate, GatePosition worldSide, GatePosition effectiveSide)
        {
            if (!Config.LogDoors || gate == null)
                return;

            string scene = gate.gameObject.scene.name;
            Log("[Door] side scene='" + scene + "' gate='" + gate.name + "' world=" + worldSide +
                " effective=" + effectiveSide + " target='" + gate.targetScene + "'");
        }

        // -------------------------------------------------------------------------
        // Top kill plane
        // -------------------------------------------------------------------------

        private readonly List<BoxCollider2D> topKillColliders = new List<BoxCollider2D>();
        private bool loggedTopKillPlane;
        private float nextTopKillCheck;

        /// <summary>
        /// Called once per frame. Re-scans at most once per second, and only when the
        /// cached top colliders are missing or disabled.
        /// </summary>
        private void TickTopKillPlane()
        {
            if (!Config.Enabled || !Config.MirrorTopKillPlane)
            {
                DisableTopKillPlane();
                return;
            }

            if (Time.realtimeSinceStartup < nextTopKillCheck)
                return;

            nextTopKillCheck = Time.realtimeSinceStartup + 1f;

            bool needsRefresh = topKillColliders.Count == 0;
            if (!needsRefresh)
            {
                for (int i = 0; i < topKillColliders.Count; i++)
                {
                    if (topKillColliders[i] == null || !topKillColliders[i].enabled)
                    {
                        needsRefresh = true;
                        break;
                    }
                }
            }

            if (needsRefresh)
                ApplyTopKillPlane();
        }

        private void DisableTopKillPlane()
        {
            for (int i = 0; i < topKillColliders.Count; i++)
            {
                if (topKillColliders[i] != null)
                    topKillColliders[i].enabled = false;
            }
            topKillColliders.Clear();
        }

        /// <summary>
        /// Finds every active Bounds Cage and enables the matching top collider.
        ///
        /// Why not use bounds: a disabled Collider2D reports zero bounds in Unity, so the
        /// top collider must be matched by its local size/offset. The bottom collider is
        /// the widest enabled box on the cage; the top collider is the box with a larger
        /// local offset.y and a similar size.
        /// </summary>
        private void ApplyTopKillPlane()
        {
            if (!Config.Enabled || !Config.MirrorTopKillPlane)
            {
                DisableTopKillPlane();
                return;
            }

            try
            {
                KillOnContact[] kills = UnityEngine.Object.FindObjectsOfType<KillOnContact>();
                DisableTopKillPlane();
                int cages = 0;

                for (int i = 0; i < kills.Length; i++)
                {
                    KillOnContact cage = kills[i];
                    if (cage == null || !cage.isActiveAndEnabled)
                        continue;
                    if (cage.gameObject.name.IndexOf("Bounds Cage", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    cages++;
                    BoxCollider2D[] boxes = cage.GetComponents<BoxCollider2D>();
                    BoxCollider2D bottom = FindBottomCollider(boxes);
                    if (bottom == null)
                    {
                        LogWarn("[KillPlane] '" + cage.name + "' has no enabled BoxCollider2D; skipped.");
                        continue;
                    }

                    BoxCollider2D top = FindTopCollider(boxes, bottom);
                    if (top == null)
                    {
                        LogWarn("[KillPlane] '" + cage.name + "' has no matching top collider; candidates:");
                        LogTopCandidates(boxes);
                        continue;
                    }

                    bool changed = !top.enabled;
                    top.enabled = true;
                    if (!topKillColliders.Contains(top))
                        topKillColliders.Add(top);

                    if (changed || !loggedTopKillPlane)
                    {
                        loggedTopKillPlane = true;
                        Log("[KillPlane] Enabled top collider for '" + cage.name +
                            "' bottom=" + bottom.bounds + " top=" + top.bounds);
                    }
                }

                if (cages == 0)
                    LogWarn("[KillPlane] No active 'Bounds Cage' found; top kill plane not changed.");
            }
            catch (Exception ex)
            {
                LogError("[KillPlane] Failed to enable Bounds Cage top colliders: " + ex);
            }
        }

        private static BoxCollider2D FindBottomCollider(BoxCollider2D[] boxes)
        {
            BoxCollider2D bottom = null;
            for (int i = 0; i < boxes.Length; i++)
            {
                BoxCollider2D box = boxes[i];
                if (box == null || !box.enabled)
                    continue;
                if (bottom == null || box.bounds.size.x > bottom.bounds.size.x)
                    bottom = box;
            }
            return bottom;
        }

        private static BoxCollider2D FindTopCollider(BoxCollider2D[] boxes, BoxCollider2D bottom)
        {
            for (int i = 0; i < boxes.Length; i++)
            {
                BoxCollider2D box = boxes[i];
                if (box == null || box == bottom)
                    continue;
                if (box.offset.y <= bottom.offset.y)
                    continue;
                if (Mathf.Abs(box.size.x - bottom.size.x) > 5f ||
                    Mathf.Abs(box.size.y - bottom.size.y) > 5f)
                    continue;
                return box;
            }
            return null;
        }

        /// <summary>
        /// SceneManager draws black borders as part of a scene transition. Hooking it
        /// lets the top kill plane be refreshed after the new scene's Bounds Cage exists,
        /// without waiting for the next Update tick.
        /// </summary>
        private void DrawBlackBorders(On.SceneManager.orig_DrawBlackBorders orig, global::SceneManager self)
        {
            orig(self);

            if (Config.Enabled && Config.MirrorTopKillPlane)
                ApplyTopKillPlane();
            else
                DisableTopKillPlane();
        }

        private void LogTopCandidates(BoxCollider2D[] boxes)
        {
            for (int i = 0; i < boxes.Length; i++)
            {
                BoxCollider2D box = boxes[i];
                if (box == null)
                    continue;
                LogWarn("[KillPlane]   enabled=" + box.enabled + " size=" + box.size +
                    " offset=" + box.offset + " bounds=" + box.bounds);
            }
        }

        // -------------------------------------------------------------------------
        // Crystal Dash / Superdash FSM patch
        // -------------------------------------------------------------------------

        private Fsm patchedSuperdashFsm;
        private string lastSuperdashChangedState = "";
        private string lastSuperDashState = "";
        private string lastSuperDashTransition = "";
        private bool lastSuperDashing;
        private int superdashReleaseFrames;
        private FsmEvent superdashButtonUp;
        private string lastSuperdashPatchWarning;

        private static readonly string[] ChargedStates = { "Ground Charged", "Wall Charged" };
        private static readonly string[] ChargeStates = { "Ground Charge", "Wall Charge" };

        /// <summary>
        /// Per-frame Crystal Dash hold logic. The FSM patch removes the automatic
        /// BUTTON UP events; the mod sends its own BUTTON UP after the player has really
        /// released the button for two frames.
        /// </summary>
        private void TickSuperdash(HeroController self)
        {
            if (!Config.Enabled || self == null || self.superDash == null || self.superDash.Fsm == null)
                return;

            EnsureSuperdashPatched(self);

            Fsm fsm = self.superDash.Fsm;
            if (fsm != patchedSuperdashFsm)
                return;

            FsmTransition transition = fsm.LastTransition;
            string state = fsm.ActiveStateName ?? "";
            string transitionName = transition != null
                ? (transition.EventName + " -> " + transition.ToState)
                : "";
            bool active = self.cState != null && self.cState.superDashing;

            InputHandler input = GameManager.instance != null
                ? GameManager.instance.GetComponent<InputHandler>()
                : null;

            if (state != lastSuperDashState || transitionName != lastSuperDashTransition || active != lastSuperDashing)
            {
                lastSuperDashState = state;
                lastSuperDashTransition = transitionName;
                lastSuperDashing = active;

                string inputState = "";
                if (input != null)
                {
                    var button = input.inputActions.superDash;
                    inputState = " superDash(pressed=" + button.IsPressed + " wasPressed=" + button.WasPressed +
                        " wasReleased=" + button.WasReleased + ")";
                }

                Log("[Cdash] t=" + Time.realtimeSinceStartup.ToString("0.00") + " active=" + active +
                    " state='" + state + "' transition='" + transitionName +
                    "' onGround=" + (self.cState != null && self.cState.onGround) +
                    " pos=" + self.transform.position + " vel=" + self.GetComponent<Rigidbody2D>().velocity +
                    inputState);
            }

            if (input == null)
                return;

            bool charged = state == "Ground Charged" || state == "Wall Charged";
            if (!charged)
            {
                superdashReleaseFrames = 0;
                return;
            }

            var held = input.inputActions.superDash;
            if (Config.VerboseLog && Time.frameCount % 30 == 0)
            {
                Log("[Cdash] hold t=" + Time.realtimeSinceStartup.ToString("0.00") +
                    " state='" + state + "' pressed=" + held.IsPressed +
                    " wasReleased=" + held.WasReleased);
            }

            if (held.IsPressed)
            {
                superdashReleaseFrames = 0;
                return;
            }

            superdashReleaseFrames++;
            if (superdashReleaseFrames < 2)
                return;

            superdashReleaseFrames = 0;
            if (superdashButtonUp == null)
            {
                LogWarn("[Cdash] BUTTON UP event is missing; cannot release charged state.");
                return;
            }

            Log("[Cdash] Release charged state '" + state + "' after two unpressed frames.");
            fsm.Event(superdashButtonUp);
        }

        /// <summary>
        /// Applies the Crystal Dash patch. All prerequisites are validated before any
        /// state is mutated, so a missing state/action does not leave the FSM half-patched.
        /// The patch is intentionally idempotent: patchedSuperdashFsm is checked first.
        /// </summary>
        private void EnsureSuperdashPatched(HeroController self)
        {
            if (!Config.Enabled || self == null || self.superDash == null || self.superDash.Fsm == null)
                return;

            Fsm fsm = self.superDash.Fsm;
            if (fsm == patchedSuperdashFsm)
                return;

            FsmEvent buttonUp = FindFsmEvent(fsm, "BUTTON UP");
            FsmEvent finished = FindFsmEvent(fsm, "FINISHED");
            if (buttonUp == null || finished == null)
            {
                WarnSuperdashPatchOnce("[Cdash] FSM events BUTTON UP/FINISHED missing; Crystal Dash patch skipped.");
                return;
            }

            for (int i = 0; i < ChargedStates.Length; i++)
            {
                if (!HasListenForSuperdash(fsm, ChargedStates[i]))
                {
                    WarnSuperdashPatchOnce("[Cdash] State '" + ChargedStates[i] +
                        "' has no ListenForSuperdash; Crystal Dash patch skipped.");
                    return;
                }
            }

            for (int i = 0; i < ChargeStates.Length; i++)
            {
                if (fsm.GetState(ChargeStates[i]) == null)
                {
                    WarnSuperdashPatchOnce("[Cdash] State '" + ChargeStates[i] + "' not found; patch skipped.");
                    return;
                }
            }

            if (!HasState(fsm, "Ground Charged") || !HasState(fsm, "Wall Charged"))
            {
                WarnSuperdashPatchOnce("[Cdash] Charged target states not found; patch skipped.");
                return;
            }

            bool ground = PatchChargedState(fsm, "Ground Charged");
            bool wall = PatchChargedState(fsm, "Wall Charged");
            if (!ground || !wall)
            {
                LogWarn("[Cdash] Charged state patch failed; FSM left partially modified.");
                return;
            }

            ClearButtonUpEvents(fsm, "Ground Charge", false);
            ClearButtonUpEvents(fsm, "Ground Charged", true);
            ClearButtonUpEvents(fsm, "Wall Charge", false);
            ClearButtonUpEvents(fsm, "Wall Charged", true);

            if (!AddFinishedHoldTransition(fsm, "Ground Charge", "Ground Charged") ||
                !AddFinishedHoldTransition(fsm, "Ground Charged", "Ground Charged") ||
                !AddFinishedHoldTransition(fsm, "Wall Charge", "Wall Charged") ||
                !AddFinishedHoldTransition(fsm, "Wall Charged", "Wall Charged"))
            {
                LogWarn("[Cdash] Failed to install one or more hold transitions.");
                return;
            }

            PatchGlobalDashTransitions(fsm);

            if (patchedSuperdashFsm != null)
                patchedSuperdashFsm.StateChanged -= OnSuperdashStateChanged;

            patchedSuperdashFsm = fsm;
            lastSuperdashPatchWarning = null;
            superdashButtonUp = buttonUp;
            superdashReleaseFrames = 0;
            lastSuperdashChangedState = fsm.ActiveStateName ?? "";
            fsm.StateChanged += OnSuperdashStateChanged;

            Log("[Cdash] Charged states patched: hold until the button is released.");
        }

        private void WarnSuperdashPatchOnce(string message)
        {
            if (lastSuperdashPatchWarning == message)
                return;

            lastSuperdashPatchWarning = message;
            LogWarn(message);
        }

        private static bool HasState(Fsm fsm, string stateName)
        {
            return fsm.GetState(stateName) != null;
        }

        private static bool HasListenForSuperdash(Fsm fsm, string stateName)
        {
            FsmState state = fsm.GetState(stateName);
            if (state == null || state.Actions == null)
                return false;

            for (int i = 0; i < state.Actions.Length; i++)
            {
                if (state.Actions[i] is ListenForSuperdash)
                    return true;
            }
            return false;
        }

        private static FsmEvent FindFsmEvent(Fsm fsm, string eventName)
        {
            if (fsm.Events == null)
                return null;

            for (int i = 0; i < fsm.Events.Length; i++)
            {
                FsmEvent candidate = fsm.Events[i];
                if (candidate != null && candidate.Name == eventName)
                    return candidate;
            }
            return null;
        }

        private void OnSuperdashStateChanged(FsmState state)
        {
            Fsm fsm = patchedSuperdashFsm;
            if (fsm == null || state == null)
                return;

            string previousState = lastSuperdashChangedState;
            string stateName = state.Name ?? "";
            lastSuperdashChangedState = stateName;
            if (previousState == stateName)
                return;

            superdashReleaseFrames = 0;

            FsmTransition transition = fsm.LastTransition;
            string transitionName = transition != null
                ? (transition.EventName + " -> " + transition.ToState)
                : "";
            InputHandler input = GameManager.instance != null
                ? GameManager.instance.GetComponent<InputHandler>()
                : null;
            string inputState = input != null
                ? " pressed=" + input.inputActions.superDash.IsPressed +
                    " wasReleased=" + input.inputActions.superDash.WasReleased
                : " input=unavailable";

            Log("[Cdash] state t=" + Time.realtimeSinceStartup.ToString("0.00") +
                " frame=" + Time.frameCount + " '" + previousState + "' -> '" + stateName +
                "' transition='" + transitionName + "'" + inputState);
        }

        /// <summary>
        /// Clears ListenForSuperdash.wasReleased and disables SendEventByName actions that
        /// would send BUTTON UP on their own. Only called for the two charged states.
        /// </summary>
        private bool PatchChargedState(Fsm fsm, string stateName)
        {
            FsmState state = fsm.GetState(stateName);
            if (state == null || state.Actions == null || state.Actions.Length == 0)
                return false;

            bool found = false;

            for (int i = 0; i < state.Actions.Length; i++)
            {
                FsmStateAction action = state.Actions[i];
                if (action == null)
                    continue;

                ListenForSuperdash listen = action as ListenForSuperdash;
                if (listen != null)
                {
                    found = true;
                    if (Config.VerboseLog)
                    {
                        string released = listen.wasReleased != null ? listen.wasReleased.Name : "null";
                        Log("[Cdash] " + stateName + " ListenForSuperdash wasReleased='" + released + "'");
                    }
                    listen.wasReleased = null;
                }

                SendEventByName send = action as SendEventByName;
                if (send == null || send.sendEvent == null)
                    continue;

                string eventName = send.sendEvent.Value;
                if (string.IsNullOrEmpty(eventName))
                    continue;

                if (Config.VerboseLog)
                {
                    float delay = send.delay != null ? send.delay.Value : 0f;
                    Log("[Cdash] " + stateName + " SendEventByName '" + eventName + "' delay=" + delay);
                }

                if (eventName == "BUTTON UP")
                {
                    send.Enabled = false;
                    if (Config.VerboseLog)
                        Log("[Cdash] Disabled automatic BUTTON UP in " + stateName + ".");
                }
            }

            if (!found)
                LogWarn("[Cdash] " + stateName + " has no ListenForSuperdash action.");
            return found;
        }

        /// <summary>
        /// Clears public FsmEvent fields named BUTTON UP. For the charge (not charged)
        /// states, ListenForSuperdash.wasReleased is deliberately kept so a real release
        /// still cancels the charge.
        /// </summary>
        private void ClearButtonUpEvents(Fsm fsm, string stateName, bool includeListenRelease)
        {
            FsmState state = fsm.GetState(stateName);
            if (state == null || state.Actions == null)
                return;

            for (int i = 0; i < state.Actions.Length; i++)
            {
                FsmStateAction action = state.Actions[i];
                if (action == null)
                    continue;

                FieldInfo[] fields = action.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
                for (int j = 0; j < fields.Length; j++)
                {
                    FieldInfo field = fields[j];
                    if (field.FieldType != typeof(FsmEvent))
                        continue;

                    FsmEvent ev = field.GetValue(action) as FsmEvent;
                    if (ev == null || ev.Name != "BUTTON UP")
                        continue;

                    if (!includeListenRelease && action is ListenForSuperdash && field.Name == "wasReleased")
                        continue;

                    field.SetValue(action, null);
                    if (Config.VerboseLog)
                        Log("[Cdash] Cleared " + stateName + " " + action.GetType().Name + "." +
                            field.Name + " = BUTTON UP.");
                }
            }
        }

        /// <summary>Removes every FINISHED transition from a state before the hold one is added.</summary>
        private void RemoveAutoDashTransitions(Fsm fsm, string stateName)
        {
            FsmState state = fsm.GetState(stateName);
            if (state == null || state.Transitions == null)
                return;

            state.Transitions = Array.FindAll(state.Transitions, transition =>
            {
                if (transition == null || transition.EventName != "FINISHED")
                    return true;

                if (Config.VerboseLog)
                    Log("[Cdash] Removed " + stateName + " transition '" + transition.EventName +
                        "' -> '" + transition.ToState + "'.");
                return false;
            });
        }

        /// <summary>
        /// Prepends FINISHED -> targetState. The transition is placed at index 0 because
        /// PlayMaker evaluates transitions in array order and an old FINISHED transition
        /// would otherwise win before the hold state is reached.
        /// </summary>
        private bool AddFinishedHoldTransition(Fsm fsm, string stateName, string targetState)
        {
            FsmState state = fsm.GetState(stateName);
            if (state == null)
                return false;

            FsmEvent finished = FindFsmEvent(fsm, "FINISHED");
            if (finished == null)
            {
                LogWarn("[Cdash] FINISHED event not found; cannot add hold transition to " + stateName + ".");
                return false;
            }

            FsmState target = fsm.GetState(targetState);
            if (target == null)
            {
                LogWarn("[Cdash] Target state '" + targetState + "' not found; hold transition not added.");
                return false;
            }

            RemoveAutoDashTransitions(fsm, stateName);

            FsmTransition[] previous = state.Transitions ?? new FsmTransition[0];
            FsmTransition hold = new FsmTransition();
            hold.FsmEvent = finished;
            hold.ToState = targetState;
            hold.ToFsmState = target;

            FsmTransition[] expanded = new FsmTransition[previous.Length + 1];
            expanded[0] = hold;
            Array.Copy(previous, 0, expanded, 1, previous.Length);
            state.Transitions = expanded;

            Log("[Cdash] Prepended " + stateName + " FINISHED -> " + targetState +
                " hold transition at index 0 (exactly one FINISHED).");
            return true;
        }

        /// <summary>
        /// Global FINISHED transitions to Dashing/Dash Start/Direction are disabled when
        /// they exist. The vanilla FSM usually has none, but older/custom configurations
        /// can; this keeps the hold behavior deterministic.
        /// </summary>
        private void PatchGlobalDashTransitions(Fsm fsm)
        {
            if (fsm.GlobalTransitions == null || fsm.GlobalTransitions.Length == 0)
            {
                if (Config.VerboseLog)
                    Log("[Cdash] No global transitions on Superdash FSM.");
                return;
            }

            for (int i = 0; i < fsm.GlobalTransitions.Length; i++)
            {
                FsmTransition transition = fsm.GlobalTransitions[i];
                if (transition == null)
                    continue;

                string eventName = transition.EventName;
                string toState = transition.ToState;

                if (Config.VerboseLog)
                    Log("[Cdash] Global transition '" + eventName + "' -> '" + toState + "'");

                if (eventName == "FINISHED" &&
                    (toState == "Dashing" || toState == "Dash Start" || toState == "Direction"))
                {
                    transition.FsmEvent = null;
                    Log("[Cdash] Disabled global FINISHED -> " + toState);
                }
            }
        }
    }
}