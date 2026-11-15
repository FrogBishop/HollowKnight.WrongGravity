using System.Collections.Generic;
using GlobalEnums;
using HutongGames.PlayMaker;
using UnityEngine;
using Vasi;

namespace WrongGravity
{
    public partial class WrongGravity
    {
        private bool ShouldKeepOriginalHazardRespawn(HeroController hero, Vector2 original, Vector3 proposed)
        {
            // HazardRespawn sets this flag before FindGroundPoint is called. Bench
            // respawns, door placement and interaction-trigger searches are separate.
            if (!Config.Enabled || hero == null || hero.cState == null || !hero.cState.hazardRespawning ||
                !HazardRespawnProbe.IsUnsafe(hero, proposed))
            {
                return false;
            }

            if (Config.VerboseLog)
            {
                Log("[Ground] Unsafe hazard-respawn ceiling at " + proposed +
                    "; keeping original respawn position " + original);
            }
            return true;
        }
    }

    /// <summary>
    /// Checks the space the Knight will occupy without moving him or changing his
    /// colliders. The query includes trigger hazards and uses the post-respawn layers:
    /// DieFromHazard temporarily puts the body on Ignore Raycast until after placement.
    /// </summary>
    internal static class HazardRespawnProbe
    {
        private const float ContactPadding = 0.02f;

        public static bool IsUnsafe(HeroController hero, Vector3 destination)
        {
            Collider2D body = Mirror.GetField<HeroController, Collider2D>(hero, "col2d");
            if (body == null)
                return true; // Do not relocate a respawn whose footprint cannot be checked.

            Physics2D.SyncTransforms();
            Vector3 displacement = destination - hero.transform.position;
            List<Collider2D> overlaps = new List<Collider2D>();
            if (OverlapsHazard(hero, body, displacement, (int)PhysLayers.PLAYER, overlaps))
                return true;

            // HeroBox applies DamageHero/legacy damages_hero damage. Its trigger may
            // extend beyond the solid body, so testing only the body/centre is unsafe.
            // Include temporarily disabled boxes; their local geometry is still valid.
            foreach (HeroBox heroBox in hero.GetComponentsInChildren<HeroBox>(true))
            {
                foreach (Collider2D hurtbox in heroBox.GetComponents<Collider2D>())
                {
                    if (OverlapsHazard(hero, hurtbox, displacement, (int)PhysLayers.HERO_BOX, overlaps))
                        return true;
                }
            }
            return false;
        }

        private static bool OverlapsHazard(HeroController hero, Collider2D source, Vector3 displacement,
            int collisionLayer, List<Collider2D> overlaps)
        {
            Vector2 center;
            Vector2 size;
            float angle;
            BoxCollider2D box = source as BoxCollider2D;
            if (box != null)
            {
                // bounds is empty for disabled colliders. Derive the box from local
                // geometry so death/respawn visibility and negative scale cannot hide it.
                center = box.transform.TransformPoint(box.offset) + displacement;
                Vector3 scale = box.transform.lossyScale;
                size = new Vector2(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.y * scale.y));
                angle = box.transform.eulerAngles.z;
            }
            else
            {
                // Conservative bounds fallback for a collider supplied by another mod.
                Bounds bounds = source.bounds;
                center = bounds.center + displacement;
                size = bounds.size;
                angle = 0f;
            }

            if (size.x <= 0f || size.y <= 0f)
                return true;

            size += Vector2.one * (2f * ContactPadding);
            ContactFilter2D filter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = true,
                layerMask = Physics2D.GetLayerCollisionMask(collisionLayer)
            };
            overlaps.Clear();
            Physics2D.OverlapBox(center, size, angle, filter, overlaps);

            foreach (Collider2D other in overlaps)
            {
                if (other == null || !other.enabled || !other.gameObject.activeInHierarchy ||
                    other.transform == hero.transform || other.transform.IsChildOf(hero.transform))
                {
                    continue;
                }

                if (CausesHazardDamage(other.gameObject))
                    return true;

                // Bounds Cage bypasses DamageHero and starts HazardRespawn directly.
                // KillOnContact uses collision callbacks, not trigger callbacks.
                if (!source.isTrigger && !other.isTrigger &&
                    (other.GetComponent<KillOnContact>() != null ||
                    (other.attachedRigidbody != null && other.attachedRigidbody.GetComponent<KillOnContact>() != null)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CausesHazardDamage(GameObject target)
        {
            // Match HeroBox's priority: the legacy FSM wins over DamageHero when both
            // exist. DamageHero is a data component; HeroBox does not test its enabled flag.
            PlayMakerFSM damageFsm = FSMUtility.LocateFSM(target, "damages_hero");
            if (damageFsm != null)
            {
                FsmInt damage = damageFsm.FsmVariables.FindFsmInt("damageDealt");
                FsmInt hazard = damageFsm.FsmVariables.FindFsmInt("hazardType");
                return damage != null && hazard != null && IsHazardDamage(damage.Value, hazard.Value);
            }

            DamageHero damageHero = target.GetComponent<DamageHero>();
            return damageHero != null && IsHazardDamage(damageHero.damageDealt, damageHero.hazardType);
        }

        private static bool IsHazardDamage(int damage, int hazardType)
        {
            // DamageHero/TakeDamage numbers differ from the zero-based HazardType enum:
            // 1 = ordinary damage; 2 = spikes, 3 = acid, 4 = lava, 5 = pit.
            return damage > 0 && hazardType >= 2 && hazardType <= 5;
        }
    }
}
