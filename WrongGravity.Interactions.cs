using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
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
    /// Important: interaction triggers are NOT identified by layer 19. That was an
    /// earlier assumption and moved unrelated interactive-object colliders. The real
    /// targets are:
    /// - RestBench components (benches);
    /// - TransitionPoint components with isADoor == true (door-type transitions);
    /// - PlayMaker FSMs that call HeroController.CanTalk / CanInspect / CanInteract
    ///   through CallMethod / CallMethodProper (NPC dialogue, inspectables, shops, ...).
    ///
    /// The collider itself is usually a trigger on the same GameObject or a child, so
    /// each target group scans its children and only moves trigger BoxCollider2Ds.
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
        /// Discovers interaction targets without relying on a physics layer.
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

            // Benches have a dedicated component and a trigger collider.
            RestBench[] benches = UnityEngine.Object.FindObjectsOfType<RestBench>();
            for (int i = 0; i < benches.Length; i++)
            {
                if (benches[i] != null)
                    ApplyTriggerGroup(benches[i].gameObject, scene, "bench");
            }

            // Door-type transitions (isADoor) use the same kind of invisible trigger as
            // NPC dialogue, so they need the same ground placement.
            TransitionPoint[] gates = UnityEngine.Object.FindObjectsOfType<TransitionPoint>();
            for (int i = 0; i < gates.Length; i++)
            {
                TransitionPoint gate = gates[i];
                if (gate != null && IsDoorTransition(gate))
                    ApplyTriggerGroup(gate.gameObject, scene, "door");
            }

            // NPC dialogue / inspectables / shops use PlayMaker CallMethod to ask the
            // HeroController whether interaction is allowed. That is the reliable signal.
            PlayMakerFSM[] fsms = UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>();
            for (int i = 0; i < fsms.Length; i++)
            {
                PlayMakerFSM fsm = fsms[i];
                if (fsm == null || fsm.Fsm == null || !CallsInteractionCheck(fsm.Fsm))
                    continue;

                // Never move the Knight's own trigger colliders.
                if (MirrorEntity.IsHero(fsm.gameObject))
                    continue;

                ApplyTriggerGroup(fsm.gameObject, scene, "fsm:" + fsm.FsmName);
            }

            // JSON profiles are also a discovery mechanism: a rare interaction that the
            // component/FSM heuristics cannot recognise can be named directly by path.
            ApplyProfileTargets(scene);
        }

        /// <summary>
        /// Applies every interaction profile for the current scene by resolving its path
        /// to a GameObject. This allows JSON to hard-code a target whose component/FSM
        /// signature is unusual.
        /// </summary>
        private void ApplyProfileTargets(string scene)
        {
            if (mirrorData == null)
                return;

            for (int i = 0; i < mirrorData.Interactions.Count; i++)
            {
                InteractionProfile profile = mirrorData.Interactions[i];
                if (profile == null ||
                    !string.Equals(profile.scene, scene, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                GameObject target = FindSceneObjectByPath(profile.path);
                if (target == null)
                {
                    if (Config.LogInteractions)
                        LogWarn("[Interaction] profile target not found scene='" + scene + "' path='" + profile.path + "'");
                    continue;
                }

                ApplyTriggerGroup(target, scene, "profile");
            }
        }

        /// <summary>
        /// Finds an object by exact path or path suffix inside the active scene.
        /// </summary>
        private static GameObject FindSceneObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            UnityEngine.SceneManagement.Scene scene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform found = FindTransformByPath(roots[i].transform, path);
                if (found != null)
                    return found.gameObject;
            }
            return null;
        }

        private static Transform FindTransformByPath(Transform transform, string path)
        {
            if (transform == null)
                return null;

            string current = GetTransformPath(transform);
            if (string.Equals(current, path, System.StringComparison.OrdinalIgnoreCase) ||
                current.EndsWith("/" + path, System.StringComparison.OrdinalIgnoreCase))
            {
                return transform;
            }

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform found = FindTransformByPath(transform.GetChild(i), path);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>
        /// Moves every trigger BoxCollider2D under <paramref name="owner"/> onto the
        /// ground/ceiling. Non-trigger colliders are left alone, and colliders that deal
        /// damage are skipped so hazards are not moved by accident.
        /// </summary>
        private void ApplyTriggerGroup(GameObject owner, string scene, string kind)
        {
            if (owner == null)
                return;

            // Stay conservative: the interaction trigger is normally on the component
            // object itself or on a direct child ("Interaction", "door", ...). Scanning
            // every descendant could move unrelated triggers in a shared room FSM.
            List<Collider2D> colliders = new List<Collider2D>();
            colliders.AddRange(owner.GetComponents<Collider2D>());
            Transform ownerTransform = owner.transform;
            for (int c = 0; c < ownerTransform.childCount; c++)
                colliders.AddRange(ownerTransform.GetChild(c).GetComponents<Collider2D>());

            string ownerPath = GetTransformPath(ownerTransform);

            for (int i = 0; i < colliders.Count; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !collider.isTrigger)
                    continue;
                if (collider.GetComponent<DamageHero>() != null)
                    continue;

                BoxCollider2D box = collider as BoxCollider2D;
                if (box == null)
                    continue;

                int id = box.GetInstanceID();
                if (!tunedInteractionIds.Add(id))
                    continue;

                // A marker on the collider GameObject prevents compounding offsets when
                // a persistent object survives a scene transition and is scanned again.
                InteractionAdjustedMarker marker = box.GetComponent<InteractionAdjustedMarker>();
                if (marker == null)
                    marker = box.gameObject.AddComponent<InteractionAdjustedMarker>();
                if (marker.AdjustedIds.Contains(id))
                    continue;
                marker.AdjustedIds.Add(id);

                string colliderPath = GetTransformPath(box.transform);
                InteractionProfile profile = mirrorData.FindInteraction(scene, ownerPath);
                if (profile == null && colliderPath != ownerPath)
                    profile = mirrorData.FindInteraction(scene, colliderPath);

                if (!PlaceTriggerOnGround(box, profile))
                {
                    marker.AdjustedIds.Remove(id);
                    if (Config.LogInteractions)
                        LogWarn("[Interaction] No ground for scene='" + scene + "' path='" + colliderPath + "' kind=" + kind);
                    continue;
                }

                if (Config.LogInteractions)
                {
                    Log("[Interaction] scene='" + scene + "' path='" + colliderPath + "' kind=" + kind +
                        " offsetY=" + (profile != null && profile.applyOffset ? profile.offsetY : 0f) +
                        (profile != null ? " profile=true" : " profile=false"));
                }
            }
        }

        /// <summary>
        /// Finds a ground point for a trigger and moves the BoxCollider2D centre onto it.
        /// </summary>
        private bool PlaceTriggerOnGround(BoxCollider2D box, InteractionProfile profile)
        {
            HeroController hero = HeroController.instance;
            if (box == null || hero == null)
                return false;

            // Search from the trigger centre. A ceiling trigger may already touch the
            // ceiling, so starting at bounds.max.y could start inside the collider.
            Vector2 probe = new Vector2(box.bounds.center.x, box.bounds.center.y);
            if (!GroundProbe.TryFindUp(hero, probe, true, out Vector3 ground) &&
                !GroundProbe.TryFindDown(hero, probe, true, out ground))
            {
                return false;
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

            return true;
        }

        private static bool IsDoorTransition(TransitionPoint gate)
        {
            if (gate == null)
                return false;

            return gate.isADoor ||
                gate.name.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// True when any state action calls HeroController.CanTalk / CanInspect /
        /// CanInteract. These are the gates used by NPC dialogue and inspectable objects.
        /// </summary>
        private static bool CallsInteractionCheck(Fsm fsm)
        {
            if (fsm == null || fsm.States == null)
                return false;

            for (int s = 0; s < fsm.States.Length; s++)
            {
                FsmState state = fsm.States[s];
                if (state == null || state.Actions == null)
                    continue;

                for (int a = 0; a < state.Actions.Length; a++)
                {
                    FsmStateAction action = state.Actions[a];
                    if (action == null)
                        continue;

                    CallMethod call = action as CallMethod;
                    if (call != null && IsInteractionMethod(call.methodName))
                        return true;

                    CallMethodProper proper = action as CallMethodProper;
                    if (proper != null && IsInteractionMethod(proper.methodName))
                        return true;
                }
            }

            return false;
        }

        private static bool IsInteractionMethod(FsmString methodName)
        {
            if (methodName == null || methodName.IsNone)
                return false;

            string value = methodName.Value != null ? methodName.Value.Trim() : "";
            return string.Equals(value, "CanTalk", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "CanInspect", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "CanInteract", System.StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Runtime marker placed on a GameObject whose interaction colliders have already
    /// been adjusted. Runtime-only; never serialized into a scene.
    /// </summary>
    public sealed class InteractionAdjustedMarker : MonoBehaviour
    {
        public readonly HashSet<int> AdjustedIds = new HashSet<int>();
    }
}