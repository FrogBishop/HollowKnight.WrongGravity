using UnityEngine;

namespace WrongGravity
{
    /// <summary>
    /// Mirrors the vertical knockback applied to enemies.
    ///
    /// Why this is needed
    /// -----------------
    /// Enemies are not gravity-inverted; they still live in the original world where +Y
    /// is up. The camera, however, is mirrored: world-up is drawn as screen-down. A nail
    /// hit from below therefore gives the enemy a world-up velocity, which looks like a
    /// downward knockback to the player.
    ///
    /// Recoil is centralised in <see cref="Recoil.RecoilByDirection"/>. It receives a
    /// cardinal direction from DirectionUtils:
    ///   0 = right, 1 = up, 2 = left, 3 = down.
    /// Swapping 1 and 3 for non-hero Recoil components makes the enemy move away from the
    /// attack on screen again. Horizontal knockback is unchanged.
    ///
    /// This also covers HealthManager damage, EnemyDreamnailReaction and PlayMaker's
    /// BeginRecoil action, because all of them call RecoilByDirection.
    /// </summary>
    public partial class WrongGravity
    {
        /// <summary>
        /// Hook for <see cref="Recoil.RecoilByDirection(int, float)"/>.
        /// </summary>
        private void RecoilByDirectionHook(
            On.Recoil.orig_RecoilByDirection orig,
            Recoil self,
            int direction,
            float magnitude)
        {
            if (!Config.Enabled || !Config.MirrorEnemyRecoil || self == null ||
                MirrorEntity.IsHero(self.gameObject))
            {
                orig(self, direction, magnitude);
                return;
            }

            int mirroredDirection = direction;
            if (direction == DirectionUtils.Up)
                mirroredDirection = DirectionUtils.Down;
            else if (direction == DirectionUtils.Down)
                mirroredDirection = DirectionUtils.Up;

            if (mirroredDirection != direction && Config.VerboseLog)
            {
                Log("[EnemyRecoil] " + self.gameObject.name + " direction=" + direction +
                    " -> " + mirroredDirection + " magnitude=" + magnitude);
            }

            orig(self, mirroredDirection, magnitude);
        }
    }
}
