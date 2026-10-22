using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WrongGravity
{
    /// <summary>
    /// Mirrors the PlayMaker velocity/gravity actions used by the Knight's FSMs.
    ///
    /// These hooks run *after* the original action so they can read the value the action
    /// actually wrote. They must not run while a HeroController wrapper is active:
    /// <see cref="MirrorContext.IsActive"/> means the wrapper already flipped the
    /// Rigidbody2D velocity and a second flip would cancel it.
    /// </summary>
    public partial class WrongGravity
    {
        private static bool loggedHeroVelocityMirror;

        private void SetVelocity2dHook(
            On.HutongGames.PlayMaker.Actions.SetVelocity2d.orig_DoSetVelocity orig,
            HutongGames.PlayMaker.Actions.SetVelocity2d self)
        {
            orig(self);
            if (!Config.Enabled)
                return;
            MirrorSetVelocity(self.Fsm, self.gameObject, self.vector, self.y);
        }

        private void SetVelocity2dIfFalseHook(
            On.HutongGames.PlayMaker.Actions.SetVelocity2dIfFalse.orig_DoSetVelocity orig,
            HutongGames.PlayMaker.Actions.SetVelocity2dIfFalse self)
        {
            orig(self);
            if (!Config.Enabled)
                return;

            // The action only writes when the bool is false; when the bool is true the
            // original value is left alone and there is nothing to mirror.
            if (self.checkBool != null && !self.checkBool.IsNone && self.checkBool.Value)
                return;

            MirrorSetVelocity(self.Fsm, self.gameObject, self.vector, self.y);
        }

        private void GetVelocity2dHook(
            On.HutongGames.PlayMaker.Actions.GetVelocity2d.orig_DoGetVelocity orig,
            HutongGames.PlayMaker.Actions.GetVelocity2d self)
        {
            orig(self);
            if (!Config.Enabled || !Config.MirrorFsmVelocity || MirrorContext.IsActive ||
                self.space != Space.World || IsWorldDownSpell(self.Fsm))
                return;

            if (self.gameObject == null)
                return;

            GameObject target = self.Fsm.GetOwnerDefaultTarget(self.gameObject);
            if (!IsHeroTarget(target))
                return;

            LogFsmVelocityOnce();

            if (self.vector != null && !self.vector.IsNone)
            {
                Vector2 value = self.vector.Value;
                self.vector.Value = new Vector2(value.x, -value.y);
            }

            if (self.y != null && !self.y.IsNone)
                self.y.Value = -self.y.Value;
        }

        private void SetGravity2dScaleHook(
            On.HutongGames.PlayMaker.Actions.SetGravity2dScale.orig_DoSetGravityScale orig,
            HutongGames.PlayMaker.Actions.SetGravity2dScale self)
        {
            orig(self);
            if (!Config.Enabled || !Config.MirrorFsmVelocity || MirrorContext.IsActive ||
                self.gravityScale == null || self.gravityScale.IsNone || IsWorldDownSpell(self.Fsm))
                return;

            if (self.gameObject == null)
                return;

            GameObject target = self.Fsm.GetOwnerDefaultTarget(self.gameObject);
            if (!IsHeroTarget(target))
                return;

            Rigidbody2D body = target.GetComponent<Rigidbody2D>();
            if (body == null)
                return;

            LogFsmVelocityOnce();

            float value = self.gravityScale.Value;
            body.gravityScale = value == 0f ? 0f : -Mathf.Abs(value);
        }

        /// <summary>
        /// Flips the Knight's world-space Y velocity after a SetVelocity2d action wrote it.
        /// </summary>
        private void MirrorSetVelocity(Fsm fsm, FsmOwnerDefault owner, FsmVector2 vector, FsmFloat y)
        {
            if (!Config.Enabled || !Config.MirrorFsmVelocity || fsm == null || owner == null ||
                MirrorContext.IsActive || IsWorldDownSpell(fsm))
                return;

            // A vector or explicit Y means the action changed vertical velocity. If it
            // only changed X, the existing Y must be left untouched.
            bool changesY = (y != null && !y.IsNone) || (vector != null && !vector.IsNone);
            if (!changesY)
                return;

            GameObject target = fsm.GetOwnerDefaultTarget(owner);
            if (!IsHeroTarget(target))
                return;

            Rigidbody2D body = target.GetComponent<Rigidbody2D>();
            if (body == null)
                return;

            LogFsmVelocityOnce();

            Vector2 velocity = body.velocity;
            body.velocity = new Vector2(velocity.x, -velocity.y);
        }

        /// <summary>
        /// When DownSpellMode is World Down (0), spell FSMs keep vanilla velocity so the
        /// down-spell can still hit the floor. The check is deliberately name-based; the
        /// FSM names are stable in 1.5.78.
        /// </summary>
        private static bool IsWorldDownSpell(Fsm fsm)
        {
            if (Config.DownSpellMode != 0 || fsm == null || string.IsNullOrEmpty(fsm.Name))
                return false;
            return fsm.Name.IndexOf("Spell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHeroTarget(GameObject target)
        {
            return MirrorEntity.IsHeroTarget(target);
        }

        private void LogFsmVelocityOnce()
        {
            if (loggedHeroVelocityMirror)
                return;

            loggedHeroVelocityMirror = true;
            Log("[FSM] Mirroring PlayMaker velocity/gravity actions for the Knight.");
        }
    }
}