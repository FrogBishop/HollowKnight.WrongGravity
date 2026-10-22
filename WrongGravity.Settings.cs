using System;
using System.Collections.Generic;
using Modding;

namespace WrongGravity
{
    /// <summary>
    /// Persisted global settings.
    ///
    /// Hollow Knight's Modding API serializes this class to
    /// <c>WrongGravity.GlobalSettings.json</c> next to the mod DLL. Field names are part
    /// of that on-disk format: rename a field only with a migration plan.
    /// </summary>
    [Serializable]
    public class WrongGravityConfig
    {
        /// <summary>
        /// Master switch. It is read in <c>Initialize</c> to decide whether any hooks are
        /// installed. The menu warns that a restart is required. Most hooks also check
        /// this flag at runtime, but already-installed hooks cannot be removed and the
        /// Knight's localScale/SPEED_TO_ENTER_SCENE_DOWN plus the Crystal Dash FSM patch
        /// are not restored, so a full reset still requires a restart.
        /// </summary>
        public bool Enabled = true;

        /// <summary>Write [Door] transition/leave/entry logs.</summary>
        public bool LogDoors = true;

        /// <summary>Enable the Bounds Cage's existing top kill collider.</summary>
        public bool MirrorTopKillPlane = true;

        /// <summary>Mirror PlayMaker CheckCollisionSide / RayCast2d actions for hero FSMs.</summary>
        public bool MirrorFsmCollision = true;

        /// <summary>Mirror PlayMaker SetVelocity2d / GetVelocity2d / SetGravity2dScale.</summary>
        public bool MirrorFsmVelocity = true;

        /// <summary>
        /// Swap up/down enemy knockback so an upward nail hit visibly knocks the enemy
        /// upward on the mirrored screen instead of downward in world space.
        /// </summary>
        public bool MirrorEnemyRecoil = true;

        /// <summary>
        /// Move layer-19 interaction triggers onto the found ground/ceiling. A few
        /// unusual triggers can be overridden by Data/WrongGravityData.json.
        /// </summary>
        public bool FixInteractions = true;

        /// <summary>Write [Interaction] logs for every adjusted trigger.</summary>
        public bool LogInteractions = false;

        /// <summary>
        /// Draw the diagnostic rays used by the rewritten HeroController collision
        /// methods. This is noisy and should stay off in normal play.
        /// </summary>
        public bool DebugDraw = false;

        /// <summary>Write extra [Ground]/[Bump] details.</summary>
        public bool VerboseLog = false;

        /// <summary>
        /// Fallback watchdog timeout for a hero that is stuck in DROPPING_DOWN.
        /// A per-door <c>forceControlAfter</c> value takes priority.
        /// </summary>
        public float TransitionTimeout = 3f;

        /// <summary>
        /// 0 = World Down (preserve vanilla down-spell progression),
        /// 1 = Screen Down (experimental).
        /// </summary>
        public int DownSpellMode = 0;
    }

    public partial class WrongGravity
    {
        internal static WrongGravityConfig Config = new WrongGravityConfig();

        /// <summary>
        /// Called by the Modding API after loading the global settings file.
        /// Keeps the two numeric values inside their documented ranges.
        /// </summary>
        public void OnLoadGlobal(WrongGravityConfig settings)
        {
            if (settings != null)
                Config = settings;

            if (Config.TransitionTimeout < 0.5f)
                Config.TransitionTimeout = 3f;

            if (Config.DownSpellMode < 0 || Config.DownSpellMode > 1)
                Config.DownSpellMode = 0;
        }

        /// <summary>Called by the Modding API before saving the global settings file.</summary>
        public WrongGravityConfig OnSaveGlobal()
        {
            return Config;
        }

        public bool ToggleButtonInsideMenu
        {
            get { return false; }
        }

        /// <summary>
        /// Menu entries. The mod exposes only the values that are safe to change while
        /// the game is running; Enabled explicitly says a restart is required.
        /// </summary>
        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry)
        {
            return new List<IMenuMod.MenuEntry>
            {
                new IMenuMod.MenuEntry(
                    "Invert Gravity",
                    new string[] { "Off", "On" },
                    "Master switch. A restart is required; when off at startup no gameplay hooks are installed.",
                    delegate(int i)
                    {
                        Config.Enabled = i == 1;
                        Log("Invert Gravity set to " + (Config.Enabled ? "On" : "Off") + " (restart required)");
                    },
                    delegate { return Config.Enabled ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Door Log",
                    new string[] { "Off", "On" },
                    "Log gate names, LeaveScene side swaps and forced transition finishes.",
                    delegate(int i) { Config.LogDoors = i == 1; },
                    delegate { return Config.LogDoors ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Top Kill Plane",
                    new string[] { "Off", "On" },
                    "Enable every active Bounds Cage's existing top kill collider.",
                    delegate(int i) { Config.MirrorTopKillPlane = i == 1; },
                    delegate { return Config.MirrorTopKillPlane ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Mirror FSM Collision",
                    new string[] { "Off", "On" },
                    "Mirror CheckCollisionSide and RayCast2d for the Knight's PlayMaker FSMs.",
                    delegate(int i) { Config.MirrorFsmCollision = i == 1; },
                    delegate { return Config.MirrorFsmCollision ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Mirror FSM Velocity",
                    new string[] { "Off", "On" },
                    "Mirror SetVelocity2d, GetVelocity2d and SetGravity2dScale for the Knight.",
                    delegate(int i) { Config.MirrorFsmVelocity = i == 1; },
                    delegate { return Config.MirrorFsmVelocity ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Enemy Recoil",
                    new string[] { "Off", "On" },
                    "Swap up/down enemy knockback so it matches the mirrored camera.",
                    delegate(int i) { Config.MirrorEnemyRecoil = i == 1; },
                    delegate { return Config.MirrorEnemyRecoil ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Fix Interactions",
                    new string[] { "Off", "On" },
                    "Move layer-19 interaction triggers to the found floor/ceiling. JSON overrides are supported.",
                    delegate(int i) { Config.FixInteractions = i == 1; },
                    delegate { return Config.FixInteractions ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Interaction Log",
                    new string[] { "Off", "On" },
                    "Log every interaction trigger adjusted by the fixer.",
                    delegate(int i) { Config.LogInteractions = i == 1; },
                    delegate { return Config.LogInteractions ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Debug Draw",
                    new string[] { "Off", "On" },
                    "Draw collision/ground probe rays. Very noisy; use only while debugging.",
                    delegate(int i) { Config.DebugDraw = i == 1; },
                    delegate { return Config.DebugDraw ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Verbose Log",
                    new string[] { "Off", "On" },
                    "Log ground searches, bump geometry and other per-frame details.",
                    delegate(int i) { Config.VerboseLog = i == 1; },
                    delegate { return Config.VerboseLog ? 1 : 0; }),

                new IMenuMod.MenuEntry(
                    "Down Spell",
                    new string[] { "World Down", "Screen Down" },
                    "World Down preserves vanilla floor progression; Screen Down is experimental.",
                    delegate(int i) { Config.DownSpellMode = i; },
                    delegate { return Config.DownSpellMode; })
            };
        }
    }
}