using System;
using System.Runtime.CompilerServices;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace WrongGravity
{
    public partial class WrongGravity
    {
        private sealed class PalaceFlySpriteOrientation
        {
            public readonly bool OriginalFlipY;

            public PalaceFlySpriteOrientation(bool originalFlipY)
            {
                OriginalFlipY = originalFlipY;
            }
        }

        private static readonly ConditionalWeakTable<tk2dBaseSprite, PalaceFlySpriteOrientation>
            palaceFlySpriteOrientations = new ConditionalWeakTable<tk2dBaseSprite, PalaceFlySpriteOrientation>();

        /// <summary>
        /// Wingmoulds play fixed Fly/Wound clips instead of choosing a directional
        /// hit animation. Counter the camera's vertical mirror for their sprite,
        /// retaining the same orientation throughout the wound/return/idle cycle.
        /// The 1.5.78 sprite definitions use Unset colliders with Physics3D, so this
        /// does not change the enemy's separately authored BoxCollider2D.
        /// </summary>
        private void PalaceFlyPlayAnimationHook(
            On.HutongGames.PlayMaker.Actions.Tk2dPlayAnimation.orig_DoPlayAnimation orig,
            Tk2dPlayAnimation self)
        {
            orig(self);

            if (!Config.Enabled || self.gameObject == null || !IsPalaceFlyControl(self.Fsm))
                return;

            GameObject target = self.Fsm.GetOwnerDefaultTarget(self.gameObject);
            if (target == null || target != self.Fsm.GameObject)
                return;

            tk2dBaseSprite sprite = target.GetComponent<tk2dBaseSprite>();
            if (sprite == null)
                return;

            PalaceFlySpriteOrientation orientation;
            if (!palaceFlySpriteOrientations.TryGetValue(sprite, out orientation))
            {
                orientation = new PalaceFlySpriteOrientation(sprite.FlipY);
                palaceFlySpriteOrientations.Add(sprite, orientation);
            }

            // Use the original orientation, not the current flag: repeated hits,
            // stationary variants and pooled instances must not flip back and forth.
            // Sprite scale leaves the object transform, particles and motion alone.
            sprite.FlipY = !orientation.OriginalFlipY;
        }

        private static bool IsPalaceFlyControl(Fsm fsm)
        {
            if (fsm == null || !string.Equals(fsm.Name, "Control", StringComparison.Ordinal))
                return false;

            // The journal identifier is shared by normal and stationary Wingmoulds,
            // including numbered scene objects and clones. Other enemies also use
            // Control/Fly/Wound names, so those names alone are not an identity check.
            foreach (FsmString variable in fsm.Variables.StringVariables)
            {
                if (variable != null && variable.Name == "PlayerData Name" && variable.Value == "PalaceFly")
                    return true;
            }

            return false;
        }
    }
}
