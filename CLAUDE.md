# CLAUDE.md

Project guidance for Claude Code. Read this before touching anything.

---

## What this is

A 2D single-screen pixel-art platformer in Unity. The player escorts a balloon across a hostile room by keeping it airborne — heading it, palming it, dodging things that pop it. *Level Devil* meets the playground game Keepy Uppy.

**Current phase: P0 — vertical slice.** See `docs/gdd.md` §20.2 for phase definitions and exit criteria.

## Read first

| Document | Contains |
|---|---|
| `docs/gdd.md` | The design. Section numbers below refer to it. |
| `docs/level-editor-spec.md` | The dev-only in-game level editor, including the level JSON schema. |

Sections in the GDD are tagged **LOCKED** / **WORKING** / **SPECULATIVE**. LOCKED items are load-bearing — see the next section.

---

## Non-negotiables

These are the rules that get silently broken and are expensive to recover from. Do not violate them without an explicit instruction, and if instructed, add a dated row to `docs/gdd.md` §22.

### Determinism (GDD §6.1)

The simulation must replay identically on attempt 40 as on attempt 1. Players learn these rooms by failing them dozens of times; non-determinism breaks the entire genre contract.

- All simulation runs in `FixedUpdate` at **60 Hz**.
- **No `Time.deltaTime` in simulation code.** Use `Time.fixedDeltaTime` or, better, a constant.
- **No `Random`, no `UnityEngine.Random`, no wall-clock time** anywhere in the sim. Balloon flutter noise is a pure function of the fixed-step counter plus a per-level seed.
- No physics behaviour that depends on frame rate, execution order, or floating-point accumulation across variable steps.

### The balloon is hand-integrated (GDD §6.1–6.2)

Unity's rigidbody gravity, drag and restitution are **all disabled**. The balloon is a circle collider on a kinematic body integrated manually. Do not "simplify" this by handing it back to the physics engine — high drag plus high restitution in Rigidbody2D feels like syrup and is not reproducible.

- Movement is **swept** (cast each step). A small collider at 16 m/s tunnels through a thin spike at 60 Hz otherwise.

### Set velocity, never add force (GDD §6.4)

`ApplyHit` **sets** the balloon's velocity. Adding force makes a bump's outcome depend on incoming velocity, so the same input yields different results — fatal for a memorisation game.

### Passive contact loses energy (GDD §4.2)

A balloon landing on a stationary player rebounds at ~0.5 restitution. Only active input (jump-bump, palm, spike) adds energy. Without this, standing still under the balloon wins every level.

### Linked fate is simultaneous (GDD §4.1)

Balloon pops → player pops **in the same frame**, and vice versa. One explosion, one sound, one restart. Never leave the player watching a doomed balloon.

### Input is abstracted (GDD §5.5)

All input flows through `IInputSource`, which emits **intents**, never raw keys. Three implementations: `KeyboardInput`, `GamepadInput`, `TouchInput`. Downstream code — `ApplyHit` especially — must never learn which device produced an intent.

**Keyboard is built and maintained first, before touch**, despite the touch-first release order. Physics cannot be tuned through an on-screen thumb pad.

### Every hit goes through one function (GDD §5.1)

```csharp
ApplyHit(HitSource source, Vector2 direction, float power, float aimOffset)
```

Jump-bump, palm and spike are the same call with different parameters. Adding or removing a verb must be configuration, not code — this is what makes the §5.3 control experiment possible.

### Asymmetric hitboxes (GDD §8)

Generous to the player, stingy to the level. The player's bump volume is **larger** than the balloon's visual; every hazard's pop hitbox is **smaller** than its visual. Do not "fix" these to match the sprites.

### Restart under 200 ms (GDD §8)

No transitions, no stingers, no delays in the death → retry loop.

---

## Unity specifics

### Folder layout

```
Assets/_Project/Scripts/
├─ Balloon/       integrator, ApplyHit, inflation state
├─ Player/        movement, carry
├─ Input/         IInputSource + Keyboard/Gamepad/Touch
├─ Level/         schema, ILevelSource, loader
├─ LevelEditor/   the in-game editor  ⚠ see below
└─ Tuning/        tuning profiles, the live slider panel
```

### ⚠ The `LevelEditor` folder must never be renamed `Editor`

Unity treats any folder named `Editor` as UnityEditor-only and **strips it from all builds**. The in-game level editor is *runtime* code that is merely dev-only — it must survive into development builds or it cannot be used for playtesting.

- Gate it with the `LEVEL_EDITOR` scripting define, not with folder placement.
- `LevelEditor/` must have **no `using UnityEditor;`** anywhere.

### Level loading

Levels are JSON in `Assets/StreamingAssets/levels/`. **StreamingAssets does not support File IO on WebGL or Android** — on web the path is a URL requiring `UnityWebRequest`; on Android it lives inside the compressed APK.

Abstract behind `ILevelSource`:

- `FileLevelSource` — desktop/editor. Direct file IO, **read and write**. The level editor only ever runs here.
- `PackedLevelSource` — web/mobile. Reads a single TextAsset manifest generated at build time. Read-only.

JSON on disk is the source of truth. The packed manifest is a build artefact and is never hand-edited.

Which levels are *in* the game is declared in `Assets/StreamingAssets/levels/index.json`, not
derived from the directory. A level file absent from the manifest is a draft: the editor opens
it, the game does not list it. The editor maintains the manifest — a declared list is only
better than a derived one while the tooling keeps it current.

### The shipping game and the editor read the same JSON

Never introduce a second level format. The moment they diverge, the editor stops being trustworthy.

### Keybind collisions

Gameplay reserves `Space` `W` `A` `D` `J` `R` `Esc` and the arrow keys. Editor bindings must avoid that set. `R` is intentionally shared — it means "restart" in both contexts.

### Rendering

640 × 360 base resolution, **integer scaling only** (×2 → 720p, ×3 → 1080p). 20 px tiles, point filtering, no mipmaps.

**Do not pixel-snap moving entities.** At 20 px/metre a Nominal balloon falls 0.67 px per frame; snapping produces visible judder on the exact object the player is tracking. Snap the tile grid only.

### WebGL build size

The primary shipping risk (GDD §17.2). IL2CPP with aggressive stripping, Brotli compression, no unused packages, one texture atlas per world. **Check the build size at the end of P0**, not later.

### Code over content — scoped

For the simulation, levels, tuning and gameplay views: do not create or edit `.unity`, `.prefab` or `.asset` files. Everything there is constructed in code from the bootstrap. That is absolute, not a style preference — it is why the sim is testable without a scene, why determinism holds without a second place for state to hide, and why there is no prefab to drift out of sync with the JSON that is the actual source of truth.

Shell *presentation* is the one carve-out (§22, 2026-08-25): a screen may be authored as a prefab under `Resources/Shell/<AppScreen>.prefab`, with `ScreenLayout` anchors placed visually in the editor instead of as hardcoded literals in `ShellView`. Any anchor left unassigned — including every anchor, when no prefab exists at all — falls back to the code position, so "no prefab yet" and "prefab with a gap" are the same ordinary path, not a special case. This costs something real: prefab YAML merges badly across branches, and menu layout stops being reviewable in a diff the way everything else in this project is. That cost is accepted for eight items and a grid; it is not a precedent for pulling anything else out of code. If this ever looks like a mistake, the fix is discipline about who touches the prefab and when — not moving the shell back into code, which would just re-litigate the point of the carve-out.

### The menu shell

Title, level select, pause and results draw with the game's own quads — there is no UI package. uGUI was already dropped alongside URP during the WebGL cut (§22, 2026-08-22), and these screens are eight items and a grid, not a form worth pulling a UI framework back in for. `BitmapFont` (`Assets/_Project/Scripts/Runtime/BitmapFont.cs`) is a 5×7 glyph table authored in code; `TextView` (`TextView.cs`) turns a string into one quad per lit pixel through `QuadView`, the same primitive every other piece of placeholder geometry uses.

**The sim does not run behind the menus.** A level exists only while it is being played (§22, 2026-08-25). `SimBootstrap` builds just the camera and the shell at boot — no `SimDriver`, no world, no views. Choosing a level calls `SimBootstrap.StartLevel(id)`, which builds one `LevelSession` root holding the driver, the world it ticks and every view that reads it; quitting a level calls `EndLevel()`, which is a single `Destroy` on that root and nothing else to remember. Pause freezes the session in place — `ShellView` still owns `Bootstrap.Session` while paused — but there is no "hidden and frozen behind the title screen" state at any point: on the menus, `Session` is null, full stop. Every read through `Bootstrap.Session` in `ShellView` has to tolerate that null as the normal case, not an error, because it is what the screen looks like most of the time.

**Every screen's rules live in a tested plain C# class; the view only draws.** `ScreenStack` and `LevelSelectModel`, both in `Assets/_Project/Scripts/Shell/` (its own assembly, `KeepyUppy.Shell` — an asmdef only covers its own folder tree, and a folder without one falls into `Assembly-CSharp`), decide which screen is current, what Back means, and where the level-select cursor can move. `ShellView.cs` is a MonoBehaviour with no automated tests, by design — it samples input, calls into the tested classes, and redraws text when their state changes. This is the same boundary the level editor already sits on.

That boundary earned itself once already. The one piece of real decision logic that leaked into `ShellView` was the one that shipped broken: win detection was level-triggered (`IsWon && Current == Playing`) rather than edge-triggered. `SimWorld.IsWon` only clears via `Restart()`, and `Restart()` only fires after the win-hold's fixed steps run — steps a paused Results screen prevents from ever running. So pressing Back from Results returned to Playing with `IsWon` still true, the level-triggered check fired again immediately, and the sim re-paused before a single step could land — Back bounced straight back into Results, forever. The fix was moving the edge into `ScreenStack.ObserveWin(bool)`, which tracks the previous value and only transitions on the false-to-true edge; that is now a unit test (`AppScreenTests`), not a bug someone finds by hand. Rules belong in the tested classes for exactly this reason.

`IShellInput` (`Assets/_Project/Scripts/Input/IShellInput.cs`) is deliberately separate from `IInputSource`, not just for tidiness. `IInputSource.Sample(int step)` is called *inside* `SimWorld.Tick()`, so an intent bound there can't be read while the world isn't ticking — a pause bound that way could be entered and never left (§22, 2026-08-24 reverses the 2026-08-22 prediction that `PausePressed` would join `IInputSource`). Everything shell-level — navigation, confirm, back, fast-forward — is sampled once per render frame instead, outside the sim's tick. `KeyboardShellInput` implements it but has no automated tests: the test assembly cannot reference `KeepyUppy.Input.Devices`, so only the tested `ScriptedShellInput` stands in for it in tests (`ShellInputTests`).

Fast-forward is *more fixed steps per render frame*, never a longer `dt` and never `Time.timeScale`. `TimeControl` (`Assets/_Project/Scripts/Sim/TimeControl.cs`) exposes `StepsFor(renderFrame)`; the simulation has no way to observe it, so a fast-forwarded run is bit-identical to a normal one and the determinism test cannot tell them apart. Slow motion is the same mechanism inverted — GDD §16's accessibility assist, dropping every other frame by frame parity rather than by an accumulator. Held on `F`, not Shift, which is already the context verb in `KeyboardInput`.

---

## Verifying changes

**Do not report a task as complete without running the tests.** If the tests could not be run, say so explicitly and say why — never imply verification that did not happen.

### Running the tests

Use the wrapper script at the repo root:

```bash
./test.sh            # macOS / Linux
test.bat             # Windows
```

It wraps the Unity Test Runner CLI:

```bash
"$UNITY" -batchmode -runTests \
  -projectPath . \
  -testPlatform EditMode \
  -testResults ./Logs/test-results.xml \
  -logFile -
```

If the wrapper is missing, invoke the editor binary directly:

| OS | Path |
|---|---|
| Windows | `C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe` |
| macOS | `/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents/MacOS/Unity` |
| Linux | `~/Unity/Hub/Editor/<version>/Editor/Unity` |

Exit code `0` means every test passed. Non-zero means failure — read `Logs/test-results.xml` for the failing cases rather than guessing from the console log, which is extremely noisy in batch mode.

### ⚠ Unity must not be open

Batch mode takes an exclusive lock on `Library/`. If the project is open in the Unity editor, the run will fail or hang indefinitely.

If a test run hangs for more than about two minutes, stop it and tell me to close the editor — **do not retry in a loop, and do not delete `Library/` to break the lock.**

### What must pass before a task is done

1. The project compiles with no errors and no new warnings.
2. All EditMode tests pass.
3. The determinism test specifically passes — see below.

### The determinism test is load-bearing

`docs/gdd.md` §6.1 is the rule most likely to regress silently, because breaking it produces no error, no warning, and no visible symptom until someone tries to learn a level.

**Any change that adds state to the simulation must be covered by the replay-equality test in the same commit.** A new field on the balloon, a new hazard with internal state, a new zone type — if it isn't included in the two-run comparison, the test will keep passing while the guarantee quietly rots.

The test itself: run N fixed steps twice from the same seed and identical inputs, assert bit-identical output. If it ever fails, the cause is almost always one of:

- `Time.deltaTime` used somewhere in sim code
- `Random` / `UnityEngine.Random` in the sim
- Iteration over an unordered collection (`Dictionary`, `HashSet`) where order affects the result
- State surviving between runs that should have been reset on restart
- Physics driven from `Update` instead of `FixedUpdate`

### What this cannot verify

The tests cover correctness, not game feel. Balloon handling, hit response, difficulty and readability are all judged by a human playing it.

When a change affects feel — anything in GDD §6.4, §6.6 or §7 — say plainly that it needs playtesting and describe what to look for. Do not describe untested feel changes as working.

PlayMode tests and anything requiring rendering are also out of scope for this loop. Prefer testing simulation logic as plain C# classes with no scene dependency (see Working agreements below) — that is what makes it testable in the first place.

---

## Working agreements

- **Do not author level content past level 10** until the control-scheme experiment (GDD §5.3, open question #1) is settled. It changes every level.
- **Do not do an art pass** before the same decision closes. Placeholder colour blocks only, on the final grid, with the reserved-colour rule enforced (GDD §13.1).
- **When a LOCKED item changes**, add a dated row to `docs/gdd.md` §22 with the rationale in the same commit.
- **When a WORKING value is tuned to something better**, update the table in the GDD. The tuning tables should reflect reality, not the original guess.
- Tuning values live in serialised profiles under `Assets/StreamingAssets/levels/_tuning/`, **not** hardcoded in scripts. Everything in GDD §6.4 and §7.1 is a slider.
- Prefer plain C# classes over MonoBehaviours for simulation logic. The sim should be testable without a scene.
- Write edit-mode tests for the balloon integrator. Determinism is exactly the kind of property that regresses silently and is trivially unit-testable: run N fixed steps twice from the same seed, assert identical output.

## Commit conventions

- Levels are JSON and produce readable diffs — keep them that way. No minifying, stable key order.
- Force Text serialization and Visible Meta Files must stay enabled (Project Settings → Editor).
- Don't commit `Library/`, `Temp/`, build output, or playtest metrics CSVs.