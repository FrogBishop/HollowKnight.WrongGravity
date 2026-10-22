using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WrongGravity
{
    /// <summary>
    /// Runtime fixer for interaction triggers.
    ///
    /// Why this is needed
    /// -----------------
    /// In Vanilla, NPC/bench/tablet interaction triggers are placed around the floor.
    /// With inverted gravity the Knight stands on the ceiling, so those triggers are on
    /// the wrong side of the room. Instead of moving every NPC, this class moves the
    /// *trigger collider* onto the ground point found by <see cref="GroundProbe"/>.
    ///
    /// The search policy is the same as the ground hooks: try the ceiling first, then
    /// fall back to the floor. A room without a ceiling still gets a usable trigger.
    ///
    /// Data overrides
    /// --------------
    /// Most triggers are handled automatically. The few unusual ones can be described in
    /// <c>Data/WrongGravityData.json</c> with an <c>interactions</c> entry:
    ///
    /// <code>
    /// {
    ///   "scene": "Town",
    ///   "path": "Sly/Interaction",
    ///   "offsetY": 5.0,
    ///   "sizeY": 2.0,
    ///   "applyOffset": true,
    ///   "applySize": true
    /// }
    /// </code>
    ///
    /// <c>path</c> is matched as an exact path or as a suffix of the transform path
    /// (for example <c>Sly/Interaction</c> matches <c>_SceneManager/Sly/Interaction</c>).
    /// </summary>
    public partial class WrongGravity
    {
        /// <summary>Hollow Knight uses layer 19 for interaction triggers in 1.5.78.</summary>
        private const int InteractionLayer = 19;

        private readonly HashSet<int> tunedInteractionIds = new HashSet<int>();
        private string tunedInteractionScene;

        /// <summary>
        /// Schedules one deferred scan after a scene has started. A frame is yielded so
        /// that scene objects have finished their Awake/Start pass.
        /// </summary>
        private void ScheduleInteractionRefresh(MonoBehaviour host)
        {
            if (!Config.Enabled || !Config.FixInteractions || host == null)
                return;

            host.StartCoroutine(RefreshInteractionsDeferred());
        }

        /// <summary>
        /// Clears the per-scene instance cache before a transition. Scene objects are
        /// destroyed/recreated, so stale IDs must not suppress the new scene's scan.
        /// </summary>
        private void ResetInteractionCache()
        {
            tunedInteractionIds.Clear();
            tunedInteractionScene = null;
        }

        private IEnumerator RefreshInteractionsDeferred()
        {
            // Let the newly loaded scene finish constructing its objects.
            yield return null;

            for (int i = 0; i < 10 && HeroController.instance == null; i++)
                yield return null;

            if (HeroController.instance == null)
            {
                if (Config.LogInteractions)
                    LogWarn("[Interaction] HeroController was not ready; interaction scan skipped.");
                yield break;
            }

            ApplyInteractionFixes();
        }

        /// <summary>
        /// Scans layer-19 trigger colliders and moves each BoxCollider2D onto the ground
        /// position. The original collider offset is not restored on scene unload because
        /// the scene objects are destroyed with the scene.
        /// </summary>
        private void ApplyInteractionFixes()
        {
            if (!Config.Enabled || !Config.FixInteractions || mirrorData == null)
                return;

            HeroController hero = HeroController.instance;
            if (hero == null)
                return;

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!string.Equals(scene, tunedInteractionScene, System.StringComparison.Ordinal))
            {
                tunedInteractionScene = scene;
                tunedInteractionIds.Clear();
            }

            Collider2D[] colliders = UnityEngine.Object.FindObjectsOfType<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.isTrigger || collider.gameObject.layer != InteractionLayer)
                    continue;

                BoxCollider2D box = collider as BoxCollider2D;
                if (box == null)
                    continue;

                int id = box.GetInstanceID();
                if (!tunedInteractionIds.Add(id))
                    continue;

                string path = GetTransformPath(box.transform);
                InteractionProfile profile = mirrorData.FindInteraction(scene, path);

                // Search from the trigger centre, not its top edge: a ceiling trigger
                // may already touch the ceiling and an upward ray from the top edge
                // could start inside the collider.
                Vector2 probe = new Vector2(box.bounds.center.x, box.bounds.center.y);
                if (!GroundProbe.TryFindUp(hero, probe, true, out Vector3 ground) &&
                    !GroundProbe.TryFindDown(hero, probe, true, out ground))
                {
                    if (Config.LogInteractions)
                        LogWarn("[Interaction] No ground for scene='" + scene + "' path='" + path + "' pos=" + box.bounds.center);
                    continue;
                }

                float offsetY = profile != null && profile.applyOffset ? profile.offsetY : 0f;
                float targetCenterY = ground.y + offsetY;
                float scaleY = box.transform.lossyScale.y;
                if (Mathf.Approximately(scaleY, 0f))
                    scaleY = 1f;

                float deltaY = targetCenterY - box.bounds.center.y;
                box.offset = new Vector2(box.offset.x, box.offset.y + deltaY / scaleY);

                if (profile != null && profile.applySize && profile.sizeY > 0f)
                    box.size = new Vector2(box.size.x, profile.sizeY);

                if (Config.LogInteractions)
                {
                    Log("[Interaction] scene='" + scene + "' path='" + path + "'" +
                        " ground=" + ground + " offsetY=" + offsetY +
                        (profile != null ? " profile=true" : " profile=false"));
                }
            }
        }

        /// <summary>
        /// Builds a slash-separated path from the scene root to <paramref name="transform"/>.
        /// </summary>
        private static string GetTransformPath(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
    }
}
