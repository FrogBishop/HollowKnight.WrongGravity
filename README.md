# WrongGravity

A Hollow Knight 1.5.78.11833 mod that turns the Knight's gravity upside down while
keeping the screen readable: the Knight falls upward in world space, the camera
projection is mirrored, and the level appears upside down on screen.

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
- Mirrors the PlayMaker collision, velocity and gravity actions used by hero FSMs.
- Patches the Crystal Dash (Superdash) FSM so holding the button no longer auto-dashes.
- Enables the top `Bounds Cage` kill collider so falling upward out of a room respawns
  the Knight like falling downward does in vanilla.
- Moves layer-19 interaction triggers onto the found floor/ceiling. Unusual triggers can
  be overridden in `Data/WrongGravityData.json`.

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
cd D:\HKProjects\WrongGravity\HollowKnight.WrongGravity
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
| Fix Interactions | On | Move layer-19 interaction triggers to the found floor/ceiling. |
| Interaction Log | Off | Log every trigger adjusted by the fixer. |
| Debug Draw | Off | Draw collision/ground probe rays. Noisy. |
| Verbose Log | Off | Extra `[Ground]`, `[Bump]`, `[Cdash]` details. |
| Down Spell | World Down | 0 = World Down, 1 = Screen Down (experimental). |

`Invert Gravity` is intentionally startup-only: the mod cannot safely uninstall hooks
mid-scene. Change it and restart the game.

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
  `Sly/Interaction` also matches `_SceneManager/Sly/Interaction`.
- `offsetY`: Y offset from the ground point found by the fixer.
- `sizeY`: optional replacement height for a `BoxCollider2D`.
- `applyOffset` / `applySize`: enable the two fields above.

## Ground search policy

The mod tries the mirrored search first:

1. Cast upward on the Terrain layer.
2. If a ceiling is found, return the transform position that puts the Knight's collider
   top just under it.
3. If no ceiling exists, fall back to the corrected downward search. This avoids
   "no ground found" errors in rooms that are open upwards.
4. Only if there is neither a ceiling nor a floor does it call the vanilla method, which
   then logs its usual "could not find ground" error.

The same policy is used by the interaction fixer, with a downward fallback when a room
has no usable ceiling.

## Debugging

All messages go to the Modding API log:

- `[Door]`: transition, LeaveScene side swaps, entry offsets, watchdog.
- `[KillPlane]`: Bounds Cage top collider selection.
- `[FSM]`: CheckCollisionSide / RayCast2d / velocity mirroring.
- `[Cdash]`: Superdash state and hold/release logic.
- `[Ground]`: upward/fallback ground searches (Verbose Log).
- `[Bump]`: bump height details (Verbose Log).
- `[Interaction]`: layer-19 trigger adjustments.

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

- Interaction triggers are adjusted heuristically. Some NPCs/benches may still need a
  JSON interaction profile.
- Water/acid surfaces are not mirrored yet.
- Enemy recoil and some Quake/spell effects still use unmirrored directions.
- Up-slash/down-slash object orientation and some particle effects are not mirrored.
- `FindGroundPoint` uses an upward-first/vanilla-fallback search; a room with neither a
  ceiling nor a floor can still produce the vanilla error.
- `RayCast2d` is mirrored only for the Superdash FSM while `cState.superDashing` is true.
- `GetVelocity2d` is mirrored only for `Space.World`.
- Not every HeroController wrapper is guaranteed to be exception-safe forever; the
  current wrappers use `try/finally` where they touch velocity/gravity.

## Repository layout

See [DEVELOPMENT.md](DEVELOPMENT.md) for a detailed architecture and per-file guide.

## License

GPLv3. See [LICENSE](LICENSE).
