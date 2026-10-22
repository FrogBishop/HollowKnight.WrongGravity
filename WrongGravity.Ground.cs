using System;
using UnityEngine;
using Vasi;

namespace WrongGravity
{
    /// <summary>
    /// Ground-search hooks and the shared geometry helper used by the rest of the mod.
    ///
    /// Why this file exists
    /// --------------------
    /// Vanilla <see cref="HeroController.FindGroundPoint"/> and
    /// <see cref="HeroController.FindGroundPointY"/> cast a ray *downwards* and return the
    /// transform position at which the Knight would stand on that floor. Under inverted
    /// gravity the Knight stands on a ceiling, so a pure downward search can return a point
    /// on the old floor (or fail completely in rooms that have no floor below the gate).
    ///
    /// The mod therefore tries a mirrored, upward search first, then a corrected
    /// downward search, and only then the original vanilla implementation. This is
    /// intentional: some rooms are open upwards and would otherwise produce
    /// "no ground found" errors.
    /// </summary>
    public partial class WrongGravity
    {
        /// <summary>
        /// Hook for <see cref="HeroController.FindGroundPoint(Vector2, bool)"/>.
        /// Tries upward first, then a corrected downward search, then the original method.
        /// </summary>
        private Vector3 FindGroundPoint(
            On.HeroController.orig_FindGroundPoint orig,
            HeroController self,
            Vector2 startPoint,
            bool useExtended = false)
        {
            if (Config.Enabled && GroundProbe.TryFindUp(self, startPoint, useExtended, out Vector3 point))
            {
                if (Config.VerboseLog)
                    Log("[Ground] FindGroundPoint up start=" + startPoint + " result=" + point);
                return point;
            }

            // No ceiling in range: fall back to the mirrored downward search first.
            // This is the old "original logic" path, but with the collider-offset
            // correction needed by localScale.y = -1. Only if there is neither a
            // ceiling nor a floor do we call the vanilla method (which logs its own
            // error in that case).
            if (Config.Enabled && GroundProbe.TryFindDown(self, startPoint, useExtended, out point))
            {
                if (Config.VerboseLog)
                    Log("[Ground] FindGroundPoint down fallback start=" + startPoint + " result=" + point);
                return point;
            }

            return orig(self, startPoint, useExtended);
        }

        /// <summary>
        /// Hook for <see cref="HeroController.FindGroundPointY(float, float, bool)"/>.
        /// Same upward-first, corrected-downward, then-vanilla policy as <see cref="FindGroundPoint"/>.
        /// </summary>
        private float FindGroundPointY(
            On.HeroController.orig_FindGroundPointY orig,
            HeroController self,
            float x,
            float y,
            bool useExtended = false)
        {
            if (Config.Enabled && GroundProbe.TryFindUp(self, new Vector2(x, y), useExtended, out Vector3 point))
            {
                if (Config.VerboseLog)
                    Log("[Ground] FindGroundPointY up x=" + x + " y=" + y + " resultY=" + point.y);
                return point.y;
            }

            if (Config.Enabled && GroundProbe.TryFindDown(self, new Vector2(x, y), useExtended, out point))
            {
                if (Config.VerboseLog)
                    Log("[Ground] FindGroundPointY down fallback x=" + x + " y=" + y + " resultY=" + point.y);
                return point.y;
            }

            return orig(self, x, y, useExtended);
        }
    }

    /// <summary>
    /// Geometry helper shared by the ground hooks and the interaction fixer.
    ///
    /// Coordinate notes (important)
    /// ----------------------------
    /// <see cref="HeroController.Awake"/> flips the Knight's localScale.y to -1. A
    /// Collider2D offset is expressed in local space, so the world-space centre offset
    /// becomes <c>offset.y * sign(lossyScale.y)</c>. The vanilla ground formula assumes
    /// a positive scale and therefore uses <c>-offset.y</c>; the helper below uses the
    /// actual world offset so it works with either scale sign.
    /// </summary>
    internal static class GroundProbe
    {
        /// <summary>Physics layer 8 ("Terrain") in Hollow Knight 1.5.78.</summary>
        public const int TerrainLayerMask = 1 << 8;

        /// <summary>Vanilla's small epsilon that keeps the returned point just off the surface.</summary>
        private const float SurfaceEpsilon = 0.01f;

        /// <summary>
        /// Tries to find a ceiling above <paramref name="start"/> and returns the Knight
        /// transform position at which the top of the collider would touch it.
        /// </summary>
        public static bool TryFindUp(HeroController hero, Vector2 start, bool useExtended, out Vector3 point)
        {
            point = default(Vector3);
            if (hero == null)
                return false;

            Collider2D collider = GetCollider(hero);
            if (collider == null)
                return false;

            float distance = GetSearchDistance(hero, useExtended);
            RaycastHit2D hit = FindNearest(start, Vector2.up, distance);
            if (hit.collider == null)
                return false;

            float scaleY = GetScaleSign(hero);
            float worldOffsetY = collider.offset.y * scaleY;
            float halfHeight = collider.bounds.extents.y;

            // The top of the collider must sit just below the ceiling.
            float y = hit.point.y - SurfaceEpsilon - halfHeight - worldOffsetY;
            point = new Vector3(hit.point.x, y, hero.transform.position.z);
            return true;
        }

        /// <summary>
        /// Tries to find a floor below <paramref name="start"/>. This is the mirrored
        /// counterpart of <see cref="TryFindUp"/> and is used by the HeroController
        /// ground hooks when no ceiling exists. Interaction triggers intentionally use
        /// an upward-only search and do not call this method.
        /// </summary>
        public static bool TryFindDown(HeroController hero, Vector2 start, bool useExtended, out Vector3 point)
        {
            point = default(Vector3);
            if (hero == null)
                return false;

            Collider2D collider = GetCollider(hero);
            if (collider == null)
                return false;

            float distance = GetSearchDistance(hero, useExtended);
            RaycastHit2D hit = FindNearest(start, Vector2.down, distance);
            if (hit.collider == null)
                return false;

            float scaleY = GetScaleSign(hero);
            float worldOffsetY = collider.offset.y * scaleY;
            float halfHeight = collider.bounds.extents.y;

            // The bottom of the collider must sit just above the floor.
            float y = hit.point.y + SurfaceEpsilon + halfHeight - worldOffsetY;
            point = new Vector3(hit.point.x, y, hero.transform.position.z);
            return true;
        }

        /// <summary>
        /// Finds the nearest terrain hit along a ray and skips distance-zero hits.
        ///
        /// Unity's Physics2D.queriesStartInColliders defaults to true, so a ray that
        /// starts inside a collider reports that collider at distance 0. Interaction
        /// triggers are often half-buried in a floor; the caller moves the ray origin
        /// above that floor before calling this method, but skipping distance-zero hits
        /// remains a useful guard for all callers.
        /// </summary>
        private static RaycastHit2D FindNearest(Vector2 origin, Vector2 direction, float distance)
        {
            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, distance, TerrainLayerMask);
            RaycastHit2D best = default(RaycastHit2D);
            float bestDistance = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit2D hit = hits[i];
                if (hit.collider == null || hit.distance <= 0.001f)
                    continue;

                if (hit.distance < bestDistance)
                {
                    best = hit;
                    bestDistance = hit.distance;
                }
            }

            return best;
        }

        private static Collider2D GetCollider(HeroController hero)
        {
            try
            {
                return Mirror.GetField<HeroController, Collider2D>(hero, "col2d");
            }
            catch (Exception)
            {
                return hero.GetComponent<Collider2D>();
            }
        }

        private static float GetScaleSign(HeroController hero)
        {
            float sign = Mathf.Sign(hero.transform.lossyScale.y);
            return Mathf.Approximately(sign, 0f) ? 1f : sign;
        }

        private static float GetSearchDistance(HeroController hero, bool extended)
        {
            string fieldName = extended
                ? "FIND_GROUND_POINT_DISTANCE_EXT"
                : "FIND_GROUND_POINT_DISTANCE";

            try
            {
                float distance = Mirror.GetField<HeroController, float>(hero, fieldName);
                if (distance > 0f)
                    return distance;
            }
            catch (Exception)
            {
                // Fall through to a conservative value below. The fields are private in
                // 1.5.78; if a future version renames them the mod should still work.
            }

            return extended ? 4f : 2f;
        }
    }
}