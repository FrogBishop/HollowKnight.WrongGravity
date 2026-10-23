using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WrongGravity
{
    /// <summary>
    /// Adapts the vanilla interaction FSMs after a trigger has been activated.
    /// These fixes run when the actions execute, including on newly enabled objects
    /// and during bench respawn before the deferred trigger scan has run.
    /// </summary>
    public partial class WrongGravity
    {
        // Weak keys let unloaded scenes release their FSMs and variables. The bench
        // offset is shared by sitting, respawning and the inverse get-off offset;
        // mirror that variable once instead of negating it on every interaction.
        private static readonly ConditionalWeakTable<FsmVector3, object> mirroredBenchOffsets =
            new ConditionalWeakTable<FsmVector3, object>();

        private static readonly ConditionalWeakTable<FsmState, object> patchedInteractionChecks =
            new ConditionalWeakTable<FsmState, object>();

        private void BenchVector3AddHook(
            On.HutongGames.PlayMaker.Actions.Vector3Add.orig_DoVector3Add orig,
            Vector3Add self)
        {
            // Bench Control / Start Rest reads the current hero position into
            // Sit Vector, adds Adjust Vector, then starts an iTweenMoveTo.
            if (MirrorBenchOffset(self.Fsm, self.addVector) && Config.LogInteractions)
                Log("[Interaction] Mirrored bench seating offset: " + self.addVector.Value);

            orig(self);
        }

        private void BenchTranslateHook(
            On.HutongGames.PlayMaker.Actions.Translate.orig_DoTranslate orig,
            Translate self)
        {
            // Bench Control / Kinemetise applies the same adjustment directly when
            // loading a save on a bench. It can run before the first manual sit.
            if (Config.Enabled && self.Fsm != null && self.Fsm.Name == "Bench Control" &&
                self.gameObject != null &&
                IsHeroTarget(self.Fsm.GetOwnerDefaultTarget(self.gameObject)) &&
                MirrorBenchOffset(self.Fsm, self.vector) && Config.LogInteractions)
            {
                Log("[Interaction] Mirrored bench respawn offset: " + self.vector.Value);
            }

            orig(self);
        }

        private static bool MirrorBenchOffset(Fsm fsm, FsmVector3 offset)
        {
            if (!Config.Enabled || fsm == null ||
                !string.Equals(fsm.Name, "Bench Control", StringComparison.Ordinal) ||
                offset == null || offset.IsNone ||
                !string.Equals(offset.Name, "Adjust Vector", StringComparison.Ordinal))
            {
                return false;
            }

            object marker;
            if (mirroredBenchOffsets.TryGetValue(offset, out marker))
                return false;

            // MirrorContext only changes velocity, so position offsets must still
            // be mirrored when an interaction runs inside a HeroController wrapper.
            Vector3 value = offset.Value;
            offset.Value = new Vector3(value.x, -value.y, value.z);
            mirroredBenchOffsets.Add(offset, new object());
            return true;
        }

        private void InteractionGetPositionHook(
            On.HutongGames.PlayMaker.Actions.GetPosition.orig_DoGetPosition orig,
            GetPosition self)
        {
            orig(self);

            // All vanilla NPC/inspect/shop alignment checks read Hero X before
            // evaluating MOVE LEFT/RIGHT. Patch at that point so even a late-enabled
            // FSM is fixed before it can start walking. Keep the real coordinates:
            // the remaining actions still need them to choose which way to face.
            if (!Config.Enabled || self.Fsm == null || self.State == null ||
                self.State.Name != "Check Proximity" || self.space != Space.World ||
                self.x == null || self.x.IsNone || self.gameObject == null ||
                !IsHeroTarget(self.Fsm.GetOwnerDefaultTarget(self.gameObject)))
            {
                return;
            }

            int removed = SuppressInteractionWalkEvents(self.Fsm, self.State);
            if (removed > 0 && Config.LogInteractions)
            {
                Log("[Interaction] Removed " + removed + " forced walk branches from '" +
                    self.Fsm.Name + "' on '" + self.Fsm.GameObject.name + "'.");
            }
        }

        /// <summary>
        /// Removes only events that send an interaction's proximity check into the
        /// vanilla Move Hero Left/Right states. The normal FINISHED and TURN events,
        /// input checks, dialogue/shop events and control restoration remain in place.
        /// Suppressing the branch at its source also avoids ever setting walk velocity
        /// or waiting for an unreachable X coordinate in the movement state.
        /// </summary>
        private static int SuppressInteractionWalkEvents(Fsm fsm, FsmState state)
        {
            object marker;
            if (patchedInteractionChecks.TryGetValue(state, out marker) ||
                state.Transitions == null || state.Actions == null)
            {
                return 0;
            }

            // Only patch the verified interaction graph, with a valid non-walking
            // FINISHED exit. Never change unrelated FloatCompare/FloatInRange actions.
            bool canFinish = false;
            HashSet<string> walkEvents = new HashSet<string>(StringComparer.Ordinal);
            foreach (FsmTransition transition in state.Transitions)
            {
                if (transition == null || string.IsNullOrEmpty(transition.ToState) ||
                    fsm.GetState(transition.ToState) == null)
                {
                    continue;
                }

                bool walksLeft = transition.ToState == "Move Hero Left";
                bool walksRight = transition.ToState == "Move Hero Right";
                if (transition.EventName == "FINISHED" && !walksLeft && !walksRight)
                    canFinish = true;
                else if ((transition.EventName == "MOVE LEFT" && walksLeft) ||
                    (transition.EventName == "MOVE RIGHT" && walksRight))
                    walkEvents.Add(transition.EventName);
            }

            if (!canFinish || walkEvents.Count == 0)
                return 0;

            int removed = 0;
            foreach (FsmStateAction action in state.Actions)
            {
                if (action == null)
                    continue;

                // NPCs use BoolAllTrue + FloatInRange; shop regions use BoolTest.
                // Their public FsmEvent fields share the same routing semantics.
                foreach (FieldInfo field in action.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.FieldType != typeof(FsmEvent) || field.IsInitOnly)
                        continue;

                    FsmEvent ev = field.GetValue(action) as FsmEvent;
                    if (ev == null || !walkEvents.Contains(ev.Name))
                        continue;

                    field.SetValue(action, null);
                    removed++;
                }
            }

            patchedInteractionChecks.Add(state, new object());
            return removed;
        }
    }
}
