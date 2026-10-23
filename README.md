# WrongGravity

A Hollow Knight 1.5.78.11833 mod that turns the Knight's gravity upside down while
keeping the screen readable: the Knight falls upward in world space, the camera
projection is mirrored, and the level appears upside down on screen.

Version 1.0.0.9.

This repository is a source mod, not a standalone game. Build it against the game's
assembly and install the resulting `WrongGravity.dll` with a Modding API loader.

## What it actually does

- Keeps the Knight's `Rigidbody2D.gravityScale` negative, so Unity's downward gravity
  accelerates the Knight upward.
- Negates `Rigidbody2D.velocity.y` around HeroController calls. Vanilla code sees the
  world it expects; the real world sees the mirrored velocity.
- Flips the Knight's `localScale.y` so the sprite/collider line up with the mirrored
  camera.
- Mirrors the tk2d camera projection with `Matrix4x4.Scale(1, -1, 1)`.
- Reimplements the HeroController ray/ground checks that hard-code `Vector2.down`.
- Uses the vanilla downward ground search (with the mod's collider-offset correction)
  while `EnterScene` places the Knight at a door, so a low ceiling above the doorway
  cannot wedge his head at the gate. Normal in-room ground searches (bench/hazard
  respawn) stay mirrored.
- Mirrors the PlayMaker collision, velocity and gravity actions used by hero FSMs.
- Swaps up/down enemy knockback so a nail hit still knocks the enemy away on screen.
- Patches the Crystal Dash (Superdash) FSM so holding the button no longer auto-dashes.
- Enables the top `Bounds Cage` kill collider so falling upward out of a room respawns
  the Knight like falling downward does in vanilla.
- Moves interaction triggers (benches, door-type transitions, and NPC/inspectable
  PlayMaker triggers) onto the ceiling above them. If a trigger is half-buried, the
  search origin is raised above the buried floor first, so it reaches the real ceiling
  instead of hitting the floor at distance zero; if no ceiling exists it is left in
  place. Unusual triggers can be overridden in `Data/WrongGravityData.json`.
- Mirrors the bench's seating-height adjustment, including the adjustment used when
  loading a save on a bench. Sitting moves the Knight away from the ceiling instead
  of into it; repeated rests keep the same direction.
- Removes forced horizontal alignment from the standard NPC, shop, inspect and
  station-bell interaction FSMs. Once activated, the interaction continues at the
  Knight's current X position, with the normal facing and dialogue/menu logic.

## Requirements

- Hollow Knight 1.5.78.11833 (Steam/GOG Windows build).
- .NET Framework 4.7.2 targeting pack for building.
- The Modding API / Modding API loader used by the Hollow Knight modding community.
- `References/Assembly-CSharp.dll`, `MMHOOK_Assembly-CSharp.dll`, `UnityEngine*.dll`,
  `Vasi.dll` and `PlayMaker.dll` in the `References/` folder.

Only 1.5.78 is supported. The hooks use private field names and PlayMaker state names
from that version.

## Build

```powershell
cd D:\HKProjects\HollowKnight.WrongGravity
dotnet build WrongGravity.csproj -c Release --nologo
```

The Release target creates `WrongGravity.zip` in the project directory containing:

- `WrongGravity.dll`
- `WrongGravity.pdb`
- `README.md`

The JSON data file is embedded into the DLL; do not copy it next to the DLL.

## Settings

Settings are exposed through the Modding API menu and saved as
`WrongGravity.GlobalSettings.json`.

| Setting | Default | Meaning |
|---|---:|---|
| Invert Gravity | On | Master switch. A restart is required; when off at startup no hooks are installed. |
| Door Log | On | Write `[Door]` transition/leave/entry logs. |
| Top Kill Plane | On | Enable every active `Bounds Cage` top kill collider. |
| Mirror FSM Collision | On | Mirror `CheckCollisionSide` / `RayCast2d` for hero FSMs. |
| Mirror FSM Velocity | On | Mirror `SetVelocity2d`, `GetVelocity2d`, `SetGravity2dScale`. |
| Enemy Recoil | On | Swap up/down enemy knockback so it matches the mirrored camera. |
| Fix Interactions | On | Move detected NPC/bench/door-type interaction triggers to the ceiling above them. |
| Interaction Log | Off | Log every trigger adjusted by the fixer. |
| Debug Draw | Off | Draw collision/ground probe rays. Noisy. |
| Verbose Log | Off | Extra `[Ground]`, `[Bump]`, `[Cdash]` details. |
| Down Spell | World Down | 0 = World Down, 1 = Screen Down (experimental). |

`Invert Gravity` is intentionally startup-only: the mod cannot safely uninstall hooks
mid-scene. Change it and restart the game.

Bench seating and interaction alignment fixes are part of gravity inversion itself.
`Fix Interactions` controls trigger relocation; it does not disable these two fixes.

## Data overrides

`Data/WrongGravityData.json` is embedded at build time. It has two arrays:

```json
{
  "doors": [
    {
      "scene": "Tutorial_01",
      "gate": "top1",
      "forceControlAfter": 1.0,
      "entryOffsetX": 0.0,
      "entryOffsetY": -3.0,
      "applyEntryOffset": true
    }
  ],
  "interactions": [
    {
      "scene": "Town",
      "path": "Sly/Interaction",
      "offsetY": 5.0,
      "sizeY": 2.0,
      "applyOffset": true,
      "applySize": true
    }
  ]
}
```

### Door fields

- `scene` / `gate`: lookup key (case-insensitive). `gate` is the `TransitionPoint`
  GameObject name, for example `top1`.
- `forceControlAfter`: watchdog timeout for `DROPPING_DOWN`; 0 means use the global
  `TransitionTimeout`.
- `entryOffsetX/Y` + `applyEntryOffset`: replace the name-based entry offset before
  `HeroController.EnterScene`.
- `entryVelocityX/Y` + `applyEntryVelocity`: parsed for forward compatibility but not
  applied yet.

### Interaction fields

- `scene`: scene name.
- `path`: transform path. Matched exactly or as a suffix, so
  `Sly/Interaction` also matches `_SceneManager/Sly/Interaction`. Profiles are also
  used as a discovery fallback: a target that the component/FSM scan cannot recognise
  can still be named directly here.
- `offsetY`: Y offset from the ceiling point found by the fixer.
- `sizeY`: optional replacement height for a `BoxCollider2D`.
- `applyOffset` / `applySize`: enable the two fields above.

## Ground search policy

**Scene-entry exception.** While `HeroController.EnterScene` is positioning the Knight
at a door, `FindGroundPoint` and `FindGroundPointY` use the vanilla downward search
instead of the mirrored upward search. It goes through the mod's corrected downward
helper, so the flipped collider offset is still accounted for. Some rooms have a
ceiling immediately above the doorway; the mirrored upward search would place the
Knight's head against it and block the transition. Outside that window the mirrored
policy below applies.

The mod tries the mirrored search first:

1. Cast upward on the Terrain layer.
2. If a ceiling is found, return the transform position that puts the Knight's collider
   top just under it.
3. If no ceiling exists, fall back to the corrected downward search. This avoids
   "no ground found" errors in rooms that are open upwards.
4. Only if there is neither a ceiling nor a floor does it call the vanilla method, which
   then logs its usual "could not find ground" error.

The interaction fixer searches **upward only**. That is intentional: interaction
points belong on the inverted-gravity floor (the ceiling), and a downward fallback would
move a trigger that is already on a ceiling, or a trigger in a room without a ceiling,
to the wrong side. For half-buried triggers the ray origin is raised above the buried
floor before the upward search; if there is no ceiling, the trigger is left in place.

## Debugging

All messages go to the Modding API log:

- `[Door]`: transition, LeaveScene side swaps, entry offsets, watchdog.
- `[KillPlane]`: Bounds Cage top collider selection.
- `[FSM]`: CheckCollisionSide / RayCast2d / velocity mirroring.
- `[Cdash]`: Superdash state and hold/release logic.
- `[Ground]`: upward/fallback ground searches and the scene-entry downward search
  (Verbose Log).
- `[Bump]`: bump height details (Verbose Log).
- `[Interaction]`: interaction/door trigger adjustments.

Useful sequence for a stuck door:

```text
[Door] source | gate -> dest | gate | leave=...
[Door] LeaveScene effective=... world=...
[Door] side scene='...' gate='...' world=... effective=...
[Door] Forced FinishedEnteringScene ...
```

`sourceGate` comes from `TransitionPoint.lastEntered`; it can be stale for `isADoor`
gates. The destination fields come from `SceneLoadInfo` and are reliable.

## Known issues / limitations

- Interaction triggers are detected by component/FSM behavior, not by a physics
  layer. Some custom NPCs, shops or door setups may still need a JSON interaction
  profile.
- The horizontal alignment fix recognises the vanilla `Check Proximity` states and
  their `Move Hero Left` / `Move Hero Right` branches. Custom interaction FSMs with
  different state layouts may need an additional adapter.
- Water/acid surfaces are not mirrored yet.
- Enemy vertical knockback is mirrored by swapping Recoil up/down. Hit-effect
  rotations and some Quake/spell effects may still use unmirrored directions.
- Up-slash/down-slash object orientation and some particle effects are not mirrored.
- Outside scene entry, `FindGroundPoint` uses upward-first, corrected-downward, then
  vanilla fallback; a room with neither a ceiling nor a floor can still produce the
  vanilla error. Scene entry itself uses the downward search (via the corrected
  helper).
- `RayCast2d` is mirrored only for the Superdash FSM while `cState.superDashing` is true.
- `GetVelocity2d` is mirrored only for `Space.World`.
- The current HeroController wrappers use `try/finally` where they touch
  velocity/gravity; new wrappers must follow the same `BeginMirror`/`EndMirror` rule.

## Repository layout

See [DEVELOPMENT.md](DEVELOPMENT.md) for a detailed architecture and per-file guide.

## License

GPLv3. See [LICENSE](LICENSE).
