using HutongGames.PlayMaker;
using UnityEngine;

namespace WrongGravity
{
    /// <summary>
    /// Tracks whether a HeroController call is currently running in "mirrored" mode.
    ///
    /// The mod does not rotate the whole physics world. Instead, before a HeroController
    /// method is called, it flips the Knight's Rigidbody2D.velocity.y and remembers that
    /// the call is mirrored. Inside that call every other hook must avoid flipping again;
    /// otherwise nested calls (Update -> Move -> Jump) would cancel each other out.
    ///
    /// The counter is deliberately a process-wide static integer because Hollow Knight
    /// only has one Knight. It is reset at the start of Update and FixedUpdate so a
    /// leaked depth from an exception cannot permanently suppress mirroring.
    ///
    /// Important: <see cref="Reset"/> only clears the depth counter. It does not restore
    /// a velocity that was already negated by a wrapper which threw before its finally
    /// block. The normal Update/FixedUpdate finally blocks handle that case; direct
    /// external calls into simple wrappers are the only remaining risk.
    /// </summary>
    internal static class MirrorContext
    {
        private static int depth;

        /// <summary>True while a mirrored HeroController call is on the stack.</summary>
        public static bool IsActive
        {
            get { return depth > 0; }
        }

        /// <summary>Current nesting depth; useful for diagnostics.</summary>
        public static int Depth
        {
            get { return depth; }
        }

        /// <summary>Marks the beginning of a mirrored call.</summary>
        public static void Enter()
        {
            depth++;
        }

        /// <summary>Marks the end of a mirrored call. Never goes below zero.</summary>
        public static void Exit()
        {
            if (depth > 0)
                depth--;
        }

        /// <summary>Clears leaked depth at the start of a new frame.</summary>
        public static void Reset()
        {
            depth = 0;
        }
    }

    /// <summary>
    /// Small helpers for deciding whether a GameObject/Fsm belongs to the Knight.
    ///
    /// The mod intentionally treats FSMs on the Knight's children as hero FSMs. Many
    /// Hollow Knight abilities (Crystal Dash, Cyclone Slash, spells, ...) run on child
    /// objects, and their collision/velocity actions need the same mirroring as the
    /// root FSM.
    /// </summary>
    internal static class MirrorEntity
    {
        /// <summary>True for the Knight root or any child of the Knight.</summary>
        public static bool IsHero(GameObject go)
        {
            if (go == null || HeroController.instance == null)
                return false;

            if (go == HeroController.instance.gameObject)
                return true;

            return go.transform.IsChildOf(HeroController.instance.transform);
        }

        /// <summary>True when the FSM belongs to the Knight or one of its children.</summary>
        public static bool IsHeroFsm(Fsm fsm)
        {
            return fsm != null && IsHero(fsm.GameObject);
        }

        /// <summary>
        /// True when a PlayMaker owner-default target resolves to the Knight root or to
        /// the object that carries the HeroController component.
        /// </summary>
        public static bool IsHeroTarget(GameObject target)
        {
            if (target == null || HeroController.instance == null)
                return false;

            if (target == HeroController.instance.gameObject)
                return true;

            return target.GetComponent<HeroController>() == HeroController.instance;
        }
    }
}
