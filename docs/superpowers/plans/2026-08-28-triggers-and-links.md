# Triggers and Links Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Levels can arm dormant hazards — region and timer triggers fire links that turn entities and air zones on, off or over, and a new `spike_bar` entity gives "spikes rise from the ground" something to link to.

**Architecture:** A new plain-C# `TriggerField` lives in `SimWorld` beside `AirField` and `EntityField`, is stepped last in `SimWorld.Tick()`, and routes fired links to the two setters that already exist (`EntityField.SetActive`, `AirField.SetEnabled`). The editor gains a Trigger tool (`8`) that reuses `ZoneGeometry` wholesale and a Link tool (`6`) that draws arrows; every rule lives in `EditorSession`, every drawing in the controller.

**Tech Stack:** Unity 6000.2.6f2, C#, NUnit EditMode tests, `JsonUtility` for level JSON, IMGUI for the editor panels.

**Spec:** `docs/superpowers/specs/2026-08-28-triggers-and-links-design.md`

## Global Constraints

Copied from the spec and CLAUDE.md. Every task's requirements implicitly include these.

- **Determinism (GDD §6.1):** no `Time.deltaTime`, no `Random`, no wall-clock, no `Dictionary`/`HashSet` iteration where order affects a result, anywhere under `Assets/_Project/Scripts/Sim/`. Seconds become steps via `SimClock.StepsIn(float)` **at load**, never per step.
- **New sim state joins the replay-equality test in the same commit.** `TriggerField` adds `WasInside`, `Fired` and a pending queue; `SimEntity.Active` and `AirZone.enabled` become mutable at run time. Both are covered in Task 5.
- **Trigger effects land on the step AFTER the trigger fires.** `delay: 0` means next step. This is a fairness rule (GDD §8), not an implementation detail.
- **The editor and the game read the same JSON.** Never a second format. Unknown types load inert and are counted, never dropped.
- **Rules in tested plain C# classes; MonoBehaviours only sample input and draw.** `LevelEditorController` (1444 lines) gets grammar only.
- **No `using UnityEditor;` anywhere under `Assets/_Project/Scripts/LevelEditor/`.** Editor code is gated by `#if LEVEL_EDITOR`.
- **Reserved colours (GDD §13.1):** hazard red and balloon yellow are worn by nothing else. Triggers draw violet.
- **Do not create or edit `.unity`, `.prefab` or `.asset` files.** Everything is built in code.
- **Running tests:** `./test.sh` from the repo root (Git Bash). Unity must be CLOSED — batch mode takes an exclusive lock on `Library/`. Exit code 0 means every test passed; the script prints `total= passed= failed=`. A single fixture: `./test.sh -testFilter KeepyUppy.SimTests.TriggerFieldTests`. If a run hangs beyond ~2 minutes, stop it and report that the editor is open — do not retry in a loop and do not delete `Library/`.
- **Tests live in** `Assets/_Project/Tests/EditMode/`, namespace `KeepyUppy.SimTests`, one fixture class per file. Unity generates the `.meta` files; commit them.

---

## File Structure

| File | Responsibility |
|---|---|
| `Scripts/Sim/Triggers/SimTrigger.cs` | **New.** One trigger in the form the sim runs it: kind, rect, `Once`, `Visible`, `AtStep`, plus the `WasInside`/`Fired` run state and `Reset()`. |
| `Scripts/Sim/Triggers/SimLink.cs` | **New.** One resolved link: source index, target ids, action, delay in steps. Plus the `TriggerKind` and `LinkAction` enums. |
| `Scripts/Sim/Triggers/TriggerField.cs` | **New.** Every trigger in the room, stepped in list order: edge detection, the pending-delay queue, and applying an action to an entity or a zone. |
| `Scripts/Sim/Level/TriggerFactory.cs` | **New.** Schema strings → `SimTrigger` / `SimLink`. The only place a type string becomes behaviour, mirroring `EntityFactory`. |
| `Scripts/Sim/Level/LevelData.cs` | `LevelTrigger` gains `visible` and `seconds`. |
| `Scripts/Sim/Level/LevelLoader.cs` | `LoadTriggers`, called from `ApplyEntities`. |
| `Scripts/Sim/Level/LevelValidator.cs` | The new lints. |
| `Scripts/Sim/Level/EntityFactory.cs`, `Scripts/Sim/Entities/EntityKind.cs`, `SimEntity.cs`, `Scripts/Sim/Level/EntityFields.cs` | `spike_bar`, its defaults, its capsule and its inspector row. `SimEntity` also gains `StartActive`. |
| `Scripts/Sim/AirField.cs` | `AirZone.startEnabled` and `AirField.ResetStates()`, so a link that switched a zone does not survive a restart. |
| `Scripts/Sim/SimWorld.cs` | Holds `Triggers`, steps it last, resets it in `Restart()`. |
| `Scripts/LevelEditor/EditorSession.cs` | Trigger and link CRUD, selection exclusivity, id allocation. |
| `Scripts/LevelEditor/TriggerInspector.cs` | **New.** The IMGUI block for the selected trigger and its links. |
| `Scripts/LevelEditor/EditorTool.cs` | `Trigger = 8`. |
| `Scripts/LevelEditor/LevelEditorController.cs` | Key bindings, palette buttons, `TriggerTool`, `LinkTool`, the overlays. |
| `Scripts/Runtime/TriggerViewSync.cs` | **New.** Draws `visible: true` triggers in game, mirroring `ZoneViewSync`. |
| `Scripts/Runtime/LevelSession.cs` | Builds the new view. |
| `Tests/EditMode/TriggerFieldTests.cs` | **New.** Edge firing, `once`, timer, delay ordering, inert kinds, reset. |
| `Tests/EditMode/TriggerLoadTests.cs` | **New.** Schema → sim: kinds, actions, `seconds` → `AtStep`, unknown counted. |
| `Tests/EditMode/EditorTriggerTests.cs` | **New.** Editor trigger and link rules. |
| `Tests/EditMode/SimTestHelpers.cs` | `SimSnapshot` gains trigger and active-flag folds; a `BuildTrapRoom` helper. |
| `Tests/EditMode/DeterminismTests.cs`, `LevelFormatTests.cs`, `LevelValidatorTests.cs`, `EntityTests.cs` | Extended. |

---

## Task 1: Schema fields — `visible` and `seconds`

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelData.cs` (the `LevelTrigger` class)
- Test: `Assets/_Project/Tests/EditMode/LevelFormatTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `LevelTrigger.visible` (bool, default `false`) and `LevelTrigger.seconds` (float, default `0f`). Every later task reads these.

- [ ] **Step 1: Write the failing test**

Add to `LevelFormatTests.cs`. `FullLevel()` already builds a level using every part of the schema — find its `triggers = new[] { new LevelTrigger { ... } }` block and give the trigger the two new values, then add this test:

```csharp
        [Test]
        public void Trigger_VisibleAndSeconds_SurviveARoundTrip()
        {
            LevelData level = FullLevel();
            level.triggers = new[]
            {
                new LevelTrigger
                {
                    id = "t01", type = "player_region",
                    rect = new[] { 14f, 10f, 4f, 8f },
                    once = true, visible = true, seconds = 0f,
                },
                new LevelTrigger
                {
                    id = "t02", type = "timer",
                    rect = new[] { 0f, 0f, 1f, 1f },
                    once = true, visible = false, seconds = 2.5f,
                },
            };

            _source.Save(level.id, level);
            Assert.IsTrue(_source.TryLoad(level.id, out LevelData loaded));

            Assert.IsTrue(loaded.triggers[0].visible, "visible must round-trip");
            Assert.IsFalse(loaded.triggers[1].visible);
            Assert.AreEqual(2.5f, loaded.triggers[1].seconds, 0.0001f);
        }

        [Test]
        public void Trigger_WithNeitherNewField_LoadsWithDocumentedDefaults()
        {
            // A file written before these fields existed: JsonUtility leaves absent fields at
            // their C# default, and the default must be the old meaning — invisible, no timer.
            var trigger = JsonUtility.FromJson<LevelTrigger>(
                "{\"id\":\"t01\",\"type\":\"player_region\",\"rect\":[1,1,2,2],\"once\":true}");

            Assert.IsFalse(trigger.visible);
            Assert.AreEqual(0f, trigger.seconds);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter KeepyUppy.SimTests.LevelFormatTests`
Expected: FAIL — compile error, `LevelTrigger` has no definition for `visible` / `seconds`.

- [ ] **Step 3: Add the fields**

In `LevelData.cs`, in `LevelTrigger`, after `once`:

```csharp
        /// <summary>
        /// Drawn to the player, not only to the author. Defaults false, which is the GDD 9.3
        /// workhorse: an unmarked region that fires when the player commits.
        /// </summary>
        public bool visible;

        /// <summary>
        /// Timer triggers only: seconds from the start of the run. Converted to whole fixed
        /// steps at load (SimClock.StepsIn), so nothing multiplies seconds during a run.
        /// Ignored by the region types.
        /// </summary>
        public float seconds;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./test.sh -testFilter KeepyUppy.SimTests.LevelFormatTests`
Expected: PASS, `failed=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Level/LevelData.cs Assets/_Project/Tests/EditMode/LevelFormatTests.cs
git commit -m "Add visible and seconds to the trigger schema"
```

---

## Task 2: The `spike_bar` entity

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Entities/EntityKind.cs`, `SimEntity.cs`
- Modify: `Assets/_Project/Scripts/Sim/Level/EntityFactory.cs`, `EntityFields.cs`
- Modify: `Assets/_Project/Scripts/LevelEditor/EditorSession.cs` (the `EntityTypes` array)
- Test: `Assets/_Project/Tests/EditMode/EntityTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `EntityKind.SpikeBar`; `EntityFactory.ParseKind("spike_bar")`; `EntityDefaults.SpikeBarRadius/Length/Angle`. Task 11's demo level uses the type string `spike_bar`.

- [ ] **Step 1: Write the failing test**

Add to `EntityTests.cs`:

```csharp
        [Test]
        public void SpikeBar_IsACapsuleThatKillsBoth()
        {
            SimEntity bar = EntityFactory.Create(new LevelEntity
            {
                id = "s01", type = "spike_bar",
                pos = new[] { 10f, 1f },
                @params = new EntityParams { radius = 0.3f, length = 2f, angle = 90f },
            });

            Assert.AreEqual(EntityKind.SpikeBar, bar.Kind);
            Assert.IsTrue(bar.KillsOnContact, "a spike bar is instantly lethal");
            Assert.IsTrue(bar.KillsPlayer, "every tile spike kills the player; so does this");

            bar.Place(0);

            // The capsule runs from the entity along its angle for its length, as a lamp's does.
            Assert.AreEqual(10f, bar.KillEnd.x, 0.001f);
            Assert.AreEqual(3f, bar.KillEnd.y, 0.001f);

            Assert.IsTrue(bar.OverlapsCircle(new Vector2(10f, 2.5f), 0.2f, 0f), "tip of the bar");
            Assert.IsFalse(bar.OverlapsCircle(new Vector2(10f, 5f), 0.2f, 0f), "well clear of it");
        }

        [Test]
        public void SpikeBar_Dormant_IsHarmlessUntilArmed()
        {
            var field = new EntityField();
            var air = new AirField();

            SimEntity bar = EntityFactory.Create(new LevelEntity
            {
                id = "s01", type = "spike_bar",
                pos = new[] { 10f, 1f },
                @params = new EntityParams { radius = 0.3f, length = 2f, angle = 90f },
                start_state = "dormant",
            });
            field.Add(bar, air);
            field.Step(0, air);

            Assert.IsFalse(field.ResolveBalloon(new Vector2(10f, 2.5f), 0.2f, 0f), "dormant");

            field.SetActive("s01", true);
            Assert.IsTrue(field.ResolveBalloon(new Vector2(10f, 2.5f), 0.2f, 0f), "armed");
        }

        [Test]
        public void SpikeBar_UnsetParams_FallBackToDefaults()
        {
            SimEntity bar = EntityFactory.Create(new LevelEntity
            {
                id = "s01", type = "spike_bar", pos = new[] { 4f, 1f },
                @params = new EntityParams(),
            });

            Assert.AreEqual(EntityDefaults.SpikeBarRadius, bar.Radius, 0.0001f);
            Assert.AreEqual(EntityDefaults.SpikeBarLength, bar.Length, 0.0001f);
            Assert.AreEqual(EntityDefaults.SpikeBarAngle, bar.Angle, 0.0001f);
        }
```

If `EntityTests.cs` does not already have `using KeepyUppy.Sim.Level;`, add it.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter KeepyUppy.SimTests.EntityTests`
Expected: FAIL — `EntityKind` has no `SpikeBar`.

- [ ] **Step 3: Add the kind and its defaults**

In `EntityKind.cs`, after the `Fan` member:

```csharp
        /// <summary>
        /// A retractable spike bar (GDD 9.3). A capsule like the lamp's, but lethal on
        /// contact and to both bodies — every tile spike kills the player, and a bar that did
        /// not would be a lie told by the same sprite.
        ///
        /// Spikes stay tiles for static geometry; this exists because a trigger needs an id
        /// to link to, and a tile has none.
        /// </summary>
        SpikeBar,
```

In the same file, in `EntityDefaults`, after the fan block:

```csharp
        public const float SpikeBarRadius = 0.25f;
        public const float SpikeBarLength = 1.0f;

        /// <summary>Up out of the floor. The common case, so it is the default.</summary>
        public const float SpikeBarAngle = 90f;
```

- [ ] **Step 4: Extend the capsule and the kill flags**

In `SimEntity.cs`, in `Place`, replace the `KillEnd` assignment:

```csharp
            // A lamp's beam and a spike bar's shaft are both capsules with length; everything
            // else is a point. One shape keeps the GDD 8 shrink at a single call site.
            KillEnd = Kind == EntityKind.Lamp || Kind == EntityKind.SpikeBar
                ? Position + Direction() * Length
                : Position;
```

Then the two flags:

```csharp
        /// <summary>Instantly lethal on contact. A lamp is not — it needs time (GDD 9.2).</summary>
        public bool KillsOnContact => Kind == EntityKind.Saw || Kind == EntityKind.SpikeBar;

        /// <summary>GDD 4.1: the balloon dies to almost anything; the player only to blades and spikes.</summary>
        public bool KillsPlayer => Kind == EntityKind.Saw || Kind == EntityKind.SpikeBar;
```

- [ ] **Step 5: Teach the factory and the inspector about it**

In `EntityFactory.cs`, add to `ParseKind`: `case "spike_bar": return EntityKind.SpikeBar;` and to `TypeName`: `case EntityKind.SpikeBar: return "spike_bar";`. Then in `Create`, after the `Fan` case:

```csharp
                case EntityKind.SpikeBar:
                    entity.Radius = EntityDefaults.Or(p.radius, EntityDefaults.SpikeBarRadius);
                    entity.Length = EntityDefaults.Or(p.length, EntityDefaults.SpikeBarLength);
                    entity.Angle = p.angle != 0f ? p.angle : EntityDefaults.SpikeBarAngle;
                    entity.Speed = p.speed;
                    break;
```

In `EntityFields.cs`, add the field list and the switch arm:

```csharp
        static readonly EntityParamId[] SpikeBar =
        {
            EntityParamId.Radius, EntityParamId.Length, EntityParamId.Angle,
            EntityParamId.Speed,
        };
```

```csharp
                case EntityKind.SpikeBar: return SpikeBar;
```

In `EditorSession.cs`, add `"spike_bar"` to the `EntityTypes` array so the palette can place one.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `./test.sh -testFilter KeepyUppy.SimTests.EntityTests`
Expected: PASS, `failed=0`.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Scripts/LevelEditor/EditorSession.cs Assets/_Project/Tests/EditMode/EntityTests.cs
git commit -m "Add the spike_bar entity"
```

---

## Task 3: `TriggerField` — edges, `once`, timers

Links are Task 4. This task ends with triggers that fire and nothing that listens.

**Files:**
- Create: `Assets/_Project/Scripts/Sim/Triggers/SimTrigger.cs`
- Create: `Assets/_Project/Scripts/Sim/Triggers/TriggerField.cs`
- Test: `Assets/_Project/Tests/EditMode/TriggerFieldTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: namespace `KeepyUppy.Sim.Triggers`; `enum TriggerKind { Unknown = 0, PlayerRegion, BalloonRegion, Timer }`; `class SimTrigger` with fields `Id, Kind, Rect, Once, Visible, AtStep, WasInside, Fired` and `Reset()`; `class TriggerField` with `Count`, `this[int]`, `UnknownCount`, `Clear()`, `Add(SimTrigger)`, `IndexOf(string)`, `Find(string)`, `Reset()`, and `Step(int step, Vector2 playerPos, bool hasPlayer, Vector2 balloonPos, EntityField entities, AirField air)`. `FiredThisStep` (a `List<int>` of trigger indices) is exposed for the tests in this task and used by Task 4's scheduling.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/TriggerFieldTests.cs`:

```csharp
using KeepyUppy.Sim;
using KeepyUppy.Sim.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Trigger firing (GDD 9.3). Edge-triggered, never level-triggered: the shell already
    /// paid for that lesson once in ScreenStack.ObserveWin, and this is the same shape.
    /// </summary>
    public sealed class TriggerFieldTests
    {
        static SimTrigger Region(string id, bool once, TriggerKind kind = TriggerKind.PlayerRegion)
            => new SimTrigger
            {
                Id = id, Kind = kind, Once = once,
                Rect = new Rect(10f, 1f, 4f, 4f),
            };

        /// <summary>Steps the field with a player at a position and no balloon in the room.</summary>
        static void StepPlayer(TriggerField field, int step, Vector2 player)
            => field.Step(step, player, true, new Vector2(-99f, -99f), null, null);

        [Test]
        public void Region_FiresOnTheEntryEdge_NotEveryStepInside()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: false));

            StepPlayer(field, 0, new Vector2(0f, 2f));
            Assert.AreEqual(0, field.FiredThisStep.Count, "outside");

            StepPlayer(field, 1, new Vector2(12f, 2f));
            Assert.AreEqual(1, field.FiredThisStep.Count, "the entry edge fires");

            StepPlayer(field, 2, new Vector2(12.5f, 2f));
            Assert.AreEqual(0, field.FiredThisStep.Count, "still inside is not a new edge");
        }

        [Test]
        public void Region_OnceFalse_FiresAgainOnReEntry()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: false));

            StepPlayer(field, 0, new Vector2(12f, 2f));
            StepPlayer(field, 1, new Vector2(0f, 2f));
            StepPlayer(field, 2, new Vector2(12f, 2f));

            Assert.AreEqual(1, field.FiredThisStep.Count, "re-entry is a fresh edge");
        }

        [Test]
        public void Region_OnceTrue_NeverFiresTwice()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: true));

            StepPlayer(field, 0, new Vector2(12f, 2f));
            StepPlayer(field, 1, new Vector2(0f, 2f));
            StepPlayer(field, 2, new Vector2(12f, 2f));

            Assert.AreEqual(0, field.FiredThisStep.Count, "once latches for the whole run");
        }

        [Test]
        public void BalloonRegion_IgnoresThePlayer()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: true, TriggerKind.BalloonRegion));

            field.Step(0, new Vector2(12f, 2f), true, new Vector2(0f, 9f), null, null);
            Assert.AreEqual(0, field.FiredThisStep.Count, "the player is not the balloon");

            field.Step(1, new Vector2(12f, 2f), true, new Vector2(12f, 2f), null, null);
            Assert.AreEqual(1, field.FiredThisStep.Count);
        }

        [Test]
        public void PlayerRegion_WithNoPlayerInTheRoom_NeverFires()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: true));

            for (int step = 0; step < 10; step++)
                field.Step(step, new Vector2(12f, 2f), false, new Vector2(12f, 2f), null, null);

            Assert.AreEqual(0, field.FiredThisStep.Count);
        }

        [Test]
        public void Timer_FiresOnceAtItsStep()
        {
            var field = new TriggerField();
            field.Add(new SimTrigger { Id = "t01", Kind = TriggerKind.Timer, AtStep = 3, Once = true });

            for (int step = 0; step < 3; step++)
            {
                StepPlayer(field, step, Vector2.zero);
                Assert.AreEqual(0, field.FiredThisStep.Count, $"step {step} is too early");
            }

            StepPlayer(field, 3, Vector2.zero);
            Assert.AreEqual(1, field.FiredThisStep.Count, "fires on its step");

            StepPlayer(field, 4, Vector2.zero);
            Assert.AreEqual(0, field.FiredThisStep.Count, "and never again");
        }

        [Test]
        public void UnknownKind_IsCountedAndNeverFires()
        {
            var field = new TriggerField();
            field.Add(new SimTrigger
            {
                Id = "t01", Kind = TriggerKind.Unknown,
                Rect = new Rect(0f, 0f, 32f, 18f), Once = false,
            });

            StepPlayer(field, 0, new Vector2(4f, 4f));

            Assert.AreEqual(1, field.UnknownCount, "surfaced, never silently dropped");
            Assert.AreEqual(0, field.FiredThisStep.Count);
        }

        [Test]
        public void Reset_ClearsTheRunState_SoARestartReplays()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: true));

            StepPlayer(field, 0, new Vector2(12f, 2f));
            Assert.IsTrue(field[0].Fired);

            field.Reset();

            Assert.IsFalse(field[0].Fired);
            Assert.IsFalse(field[0].WasInside);

            StepPlayer(field, 0, new Vector2(12f, 2f));
            Assert.AreEqual(1, field.FiredThisStep.Count, "the room replays from the start");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./test.sh -testFilter KeepyUppy.SimTests.TriggerFieldTests`
Expected: FAIL — namespace `KeepyUppy.Sim.Triggers` does not exist.

- [ ] **Step 3: Write `SimTrigger`**

Create `Assets/_Project/Scripts/Sim/Triggers/SimTrigger.cs`:

```csharp
using UnityEngine;

namespace KeepyUppy.Sim.Triggers
{
    /// <summary>
    /// What a trigger watches. The schema carries a free-form string, because a level file
    /// outlives any one build; this is the subset with behaviour today. Anything else loads
    /// as <see cref="Unknown"/>, is counted, and never fires.
    /// </summary>
    public enum TriggerKind
    {
        Unknown = 0,
        PlayerRegion,
        BalloonRegion,
        Timer,
    }

    /// <summary>
    /// One trigger, in the form the simulation runs it in (GDD 9.3).
    ///
    /// Config is copied out of the level data at load, so nothing here reads JSON and the sim
    /// stays testable without a file.
    /// </summary>
    public sealed class SimTrigger
    {
        // --- Identity and config: set at load, never mutated by the tick ---

        public string Id;
        public TriggerKind Kind;

        /// <summary>Tile-aligned, in metres, bottom-left origin — as AirZone.</summary>
        public Rect Rect;

        /// <summary>Latches after the first fire. The GDD 9.3 betrayal workhorse.</summary>
        public bool Once = true;

        /// <summary>Drawn to the player, not only to the author.</summary>
        public bool Visible;

        /// <summary>Timer only: the step it fires on. Converted from seconds at load.</summary>
        public int AtStep;

        // --- Run state: cleared by Reset(), covered by the replay-equality test ---

        /// <summary>Whether the condition held last step. This is what makes firing an edge.</summary>
        public bool WasInside;

        public bool Fired;

        public void Reset()
        {
            WasInside = false;
            Fired = false;
        }
    }
}
```

- [ ] **Step 4: Write `TriggerField` (firing only)**

Create `Assets/_Project/Scripts/Sim/Triggers/TriggerField.cs`. The link half is added in Task 4; the `entities` and `air` parameters are accepted now so the signature does not change under `SimWorld` later.

```csharp
using System.Collections.Generic;
using KeepyUppy.Sim.Entities;
using UnityEngine;

namespace KeepyUppy.Sim.Triggers
{
    /// <summary>
    /// Every trigger in the room, evaluated in a fixed order (GDD 9.3).
    ///
    /// A List, never a Dictionary or HashSet: iteration order decides which link applies
    /// first when two fire on the same step, and unordered iteration is on CLAUDE.md's list
    /// of the things that break GDD 6.1 silently.
    ///
    /// Firing is edge-triggered. A trigger fires on the false-to-true edge of its condition,
    /// never for every step the condition holds — the same shape as ScreenStack.ObserveWin,
    /// which is where this project already learned that a level-triggered check ships broken.
    /// </summary>
    public sealed class TriggerField
    {
        readonly List<SimTrigger> _triggers = new List<SimTrigger>();

        /// <summary>Indices of the triggers that fired on the most recent step, in list order.</summary>
        public readonly List<int> FiredThisStep = new List<int>();

        /// <summary>Types this build has no behaviour for. Surfaced, never swallowed.</summary>
        public int UnknownCount { get; private set; }

        public int Count => _triggers.Count;
        public SimTrigger this[int i] => _triggers[i];

        public void Clear()
        {
            _triggers.Clear();
            FiredThisStep.Clear();
            UnknownCount = 0;
        }

        public void Add(SimTrigger trigger)
        {
            if (trigger == null) return;

            if (trigger.Kind == TriggerKind.Unknown) UnknownCount++;

            trigger.Reset();
            _triggers.Add(trigger);
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _triggers.Count; i++)
                if (_triggers[i].Id == id) return i;

            return -1;
        }

        public SimTrigger Find(string id)
        {
            int index = IndexOf(id);
            return index < 0 ? null : _triggers[index];
        }

        /// <summary>GDD 8: part of the synchronous restart. The room must replay from the start.</summary>
        public void Reset()
        {
            for (int i = 0; i < _triggers.Count; i++) _triggers[i].Reset();
            FiredThisStep.Clear();
        }

        /// <summary>
        /// Evaluates every trigger for this step. Called last in SimWorld.Tick, after contact
        /// and the exit check: a trap armed by the player's own movement must not be able to
        /// materialise inside the body that armed it, in the frame it armed it.
        /// </summary>
        public void Step(int step, Vector2 playerPos, bool hasPlayer, Vector2 balloonPos,
                         EntityField entities, AirField air)
        {
            FiredThisStep.Clear();

            for (int i = 0; i < _triggers.Count; i++)
            {
                SimTrigger trigger = _triggers[i];

                bool inside = Holds(trigger, step, playerPos, hasPlayer, balloonPos);
                bool edge = inside && !trigger.WasInside;
                trigger.WasInside = inside;

                if (!edge) continue;
                if (trigger.Once && trigger.Fired) continue;

                trigger.Fired = true;
                FiredThisStep.Add(i);
            }
        }

        static bool Holds(SimTrigger trigger, int step, Vector2 playerPos, bool hasPlayer,
                          Vector2 balloonPos)
        {
            switch (trigger.Kind)
            {
                case TriggerKind.PlayerRegion:
                    return hasPlayer && trigger.Rect.Contains(playerPos);

                case TriggerKind.BalloonRegion:
                    return trigger.Rect.Contains(balloonPos);

                // Held true from its step onwards rather than tested for equality, so the edge
                // rule above does the "fires once" work instead of a second mechanism.
                case TriggerKind.Timer:
                    return step >= trigger.AtStep;

                default:
                    return false;
            }
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `./test.sh -testFilter KeepyUppy.SimTests.TriggerFieldTests`
Expected: PASS, `failed=0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Triggers Assets/_Project/Tests/EditMode/TriggerFieldTests.cs
git commit -m "Add TriggerField: edge-triggered regions and timers"
```

---

## Task 4: Links — actions, delays, and applying them

**Files:**
- Create: `Assets/_Project/Scripts/Sim/Triggers/SimLink.cs`
- Modify: `Assets/_Project/Scripts/Sim/Triggers/TriggerField.cs`
- Test: `Assets/_Project/Tests/EditMode/TriggerFieldTests.cs`

**Interfaces:**
- Consumes: `TriggerField`, `SimTrigger`, `TriggerKind` from Task 3.
- Produces: `enum LinkAction { Inert = 0, On, Off, Toggle }`; `class SimLink { int FromIndex; string[] ToIds; LinkAction Action; int DelaySteps; }`; `TriggerField.AddLink(SimLink)`, `TriggerField.LinkCount`, `TriggerField.PendingCount`. Task 5 relies on `Step(...)` now applying links to the `entities` and `air` it is handed.

- [ ] **Step 1: Write the failing test**

Append to `TriggerFieldTests.cs` (inside the class), and add `using KeepyUppy.Sim.Entities;` at the top:

```csharp
        static EntityField OneDormantSaw(out AirField air)
        {
            air = new AirField();
            var field = new EntityField();

            field.Add(new SimEntity
            {
                Id = "e01", Kind = EntityKind.Saw, Radius = 0.6f, Speed = 0f,
                Points = new[] { new Vector2(20f, 8f) },
                Active = false,
            }, air);

            return field;
        }

        [Test]
        public void Link_On_ArmsADormantEntity_OnTheStepAfterTheTriggerFires()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.On, DelaySteps = 0,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.IsFalse(entities.Find("e01").Active, "effects land on the NEXT step");

            field.Step(1, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.IsTrue(entities.Find("e01").Active);
        }

        [Test]
        public void Link_Delay_AppliesExactlyOnItsStep()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.On, DelaySteps = 24,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, entities, air);

            // Fired on step 0, so it is queued for step 0 + 1 + 24 = 25. The +1 is the
            // "effects land on the next step" rule, not an off-by-one.
            for (int step = 1; step <= 26; step++)
            {
                field.Step(step, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
                bool expected = step >= 25;
                Assert.AreEqual(expected, entities.Find("e01").Active, $"step {step}");
            }
        }

        [Test]
        public void Link_Toggle_FlipsWhateverStateTheTargetIsIn()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: false));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.Toggle, DelaySteps = 0,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            field.Step(1, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.IsTrue(entities.Find("e01").Active, "first entry armed it");

            field.Step(2, new Vector2(0f, 2f), true, Vector2.zero, entities, air);
            field.Step(3, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            field.Step(4, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.IsFalse(entities.Find("e01").Active, "re-entry toggled it back off");
        }

        [Test]
        public void Link_SwitchesAnAirZone_ById()
        {
            var air = new AirField();
            air.Add(new AirZone("z01", new Rect(0f, 0f, 4f, 4f), new Vector2(0f, 5f), false));

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "z01" }, Action = LinkAction.On, DelaySteps = 0,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, null, air);
            field.Step(1, new Vector2(12f, 2f), true, Vector2.zero, null, air);

            Assert.IsTrue(air[0].enabled);
        }

        [Test]
        public void Link_Inert_ChangesNothing()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.Inert, DelaySteps = 0,
            });

            for (int step = 0; step < 10; step++)
                field.Step(step, new Vector2(12f, 2f), true, Vector2.zero, entities, air);

            Assert.IsFalse(entities.Find("e01").Active, "arm and fire are parsed, not simulated");
        }

        [Test]
        public void Reset_DropsPendingDelays()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.On, DelaySteps = 30,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.AreEqual(1, field.PendingCount, "queued but not yet due");

            field.Reset();

            Assert.AreEqual(0, field.PendingCount, "a restart drops a delay in flight");
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter KeepyUppy.SimTests.TriggerFieldTests`
Expected: FAIL — `SimLink` does not exist.

- [ ] **Step 3: Write `SimLink`**

Create `Assets/_Project/Scripts/Sim/Triggers/SimLink.cs`:

```csharp
namespace KeepyUppy.Sim.Triggers
{
    /// <summary>
    /// What a link does to its targets. The schema also declares arm and fire; they parse as
    /// <see cref="Inert"/> until something needs them, rather than being dropped.
    /// </summary>
    public enum LinkAction
    {
        Inert = 0,
        On,
        Off,
        Toggle,
    }

    /// <summary>
    /// One resolved link (level-editor-spec 3).
    ///
    /// The source is an index rather than an id: it is resolved once at load, so the tick
    /// never does a string comparison to find out who fired.
    /// </summary>
    public sealed class SimLink
    {
        /// <summary>Index into the trigger list. -1 means the source id matched nothing.</summary>
        public int FromIndex = -1;

        /// <summary>Entity or zone ids. Applied in order, so two links agree on precedence.</summary>
        public string[] ToIds = new string[0];

        public LinkAction Action = LinkAction.Toggle;

        /// <summary>
        /// Whole fixed steps, converted from the schema's seconds at load. Nothing multiplies
        /// seconds by a timestep during a run (GDD 6.1).
        /// </summary>
        public int DelaySteps;
    }
}
```

- [ ] **Step 4: Add the queue and the application to `TriggerField`**

In `TriggerField.cs`, add the fields beside `_triggers`:

```csharp
        readonly List<SimLink> _links = new List<SimLink>();

        /// <summary>Links waiting out a delay. Drained in insertion order.</summary>
        readonly List<Pending> _pending = new List<Pending>();

        struct Pending
        {
            public int step;
            public int link;
        }

        public int LinkCount => _links.Count;
        public int PendingCount => _pending.Count;

        public void AddLink(SimLink link)
        {
            if (link != null) _links.Add(link);
        }
```

Extend `Clear()` with `_links.Clear();` and `_pending.Clear();`, and `Reset()` with `_pending.Clear();`.

At the end of `Step`, after the firing loop:

```csharp
            // Queue every link the fires above own, then drain whatever is due. Queue first,
            // so a zero-delay link fires on the NEXT step rather than this one.
            for (int i = 0; i < FiredThisStep.Count; i++)
            {
                int trigger = FiredThisStep[i];

                for (int l = 0; l < _links.Count; l++)
                {
                    if (_links[l].FromIndex != trigger) continue;
                    _pending.Add(new Pending { step = step + 1 + _links[l].DelaySteps, link = l });
                }
            }

            Drain(step, entities, air);
```

And the two helpers:

```csharp
        /// <summary>
        /// Applies everything due, in insertion order, and compacts what is left in place.
        /// Forward iteration is load-bearing: two links landing on the same step must apply in
        /// the order they were queued, on every run.
        /// </summary>
        void Drain(int step, EntityField entities, AirField air)
        {
            int keep = 0;

            for (int i = 0; i < _pending.Count; i++)
            {
                Pending item = _pending[i];

                if (item.step > step)
                {
                    _pending[keep++] = item;
                    continue;
                }

                Apply(_links[item.link], entities, air);
            }

            _pending.RemoveRange(keep, _pending.Count - keep);
        }

        static void Apply(SimLink link, EntityField entities, AirField air)
        {
            if (link.Action == LinkAction.Inert || link.ToIds == null) return;

            foreach (string id in link.ToIds)
            {
                // An entity first: a fan is both an entity and a zone sharing one id, and its
                // AirZone is republished from Active every step, so setting the entity is
                // enough and there is no fan special case.
                SimEntity entity = entities?.Find(id);
                if (entity != null)
                {
                    entity.Active = Next(link.Action, entity.Active);
                    continue;
                }

                if (air == null) continue;

                for (int i = 0; i < air.Count; i++)
                {
                    if (air[i].id != id) continue;

                    AirZone zone = air[i];
                    zone.enabled = Next(link.Action, zone.enabled);
                    air[i] = zone;
                }
            }
        }

        static bool Next(LinkAction action, bool current)
        {
            switch (action)
            {
                case LinkAction.On:     return true;
                case LinkAction.Off:    return false;
                case LinkAction.Toggle: return !current;
                default:                return current;
            }
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `./test.sh -testFilter KeepyUppy.SimTests.TriggerFieldTests`
Expected: PASS, `failed=0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Triggers Assets/_Project/Tests/EditMode/TriggerFieldTests.cs
git commit -m "Add links: on, off, toggle, and delays in fixed steps"
```

---

## Task 5: Loading — schema strings into the field

**Files:**
- Create: `Assets/_Project/Scripts/Sim/Level/TriggerFactory.cs`
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelLoader.cs`
- Test: `Assets/_Project/Tests/EditMode/TriggerLoadTests.cs`

**Interfaces:**
- Consumes: `SimTrigger`, `SimLink`, `TriggerField` (Tasks 3–4); `LevelTrigger.visible`/`seconds` (Task 1).
- Produces: `TriggerFactory.ParseKind(string)`, `TriggerFactory.KindName(TriggerKind)`, `TriggerFactory.ParseAction(string)`, `TriggerFactory.Create(LevelTrigger)`, `TriggerFactory.CreateLink(LevelLink, TriggerField)`; `LevelLoader.LoadTriggers(LevelData, TriggerField)` called from `ApplyEntities`.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/TriggerLoadTests.cs`:

```csharp
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using KeepyUppy.Sim.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Schema to simulation for triggers and links. The editor and the shipping game take
    /// this same path, so a type that loads wrong here is a level that plays wrong in both.
    /// </summary>
    public sealed class TriggerLoadTests
    {
        static LevelData LevelWithTrigger(LevelTrigger trigger, LevelLink link = null)
        {
            var grid = new TileGrid();
            grid.FillBorder();

            return new LevelData
            {
                tiles = grid.ToRows(),
                triggers = new[] { trigger },
                links = link == null ? new LevelLink[0] : new[] { link },
            };
        }

        [Test]
        public void Kinds_ParseToTheirBehaviour_AndAnythingElseIsUnknown()
        {
            Assert.AreEqual(TriggerKind.PlayerRegion, TriggerFactory.ParseKind("player_region"));
            Assert.AreEqual(TriggerKind.BalloonRegion, TriggerFactory.ParseKind("balloon_region"));
            Assert.AreEqual(TriggerKind.Timer, TriggerFactory.ParseKind("timer"));
            Assert.AreEqual(TriggerKind.Unknown, TriggerFactory.ParseKind("lever"));
            Assert.AreEqual(TriggerKind.Unknown, TriggerFactory.ParseKind("on_first_entry"));
        }

        [Test]
        public void Actions_ParseToTheirBehaviour_AndArmAndFireAreInert()
        {
            Assert.AreEqual(LinkAction.On, TriggerFactory.ParseAction("on"));
            Assert.AreEqual(LinkAction.Off, TriggerFactory.ParseAction("off"));
            Assert.AreEqual(LinkAction.Toggle, TriggerFactory.ParseAction("toggle"));
            Assert.AreEqual(LinkAction.Inert, TriggerFactory.ParseAction("arm"));
            Assert.AreEqual(LinkAction.Inert, TriggerFactory.ParseAction("fire"));
        }

        [Test]
        public void Seconds_BecomeStepsAtLoad()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(new LevelTrigger
            {
                id = "t01", type = "timer", seconds = 2.5f, once = true,
            }), world, out _, out _);

            Assert.AreEqual(150, world.Triggers[0].AtStep, "2.5 s at 60 Hz");
        }

        [Test]
        public void RectAndFlags_ComeStraightFromTheFile()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(new LevelTrigger
            {
                id = "t01", type = "player_region",
                rect = new[] { 14f, 10f, 4f, 8f },
                once = false, visible = true,
            }), world, out _, out _);

            SimTrigger trigger = world.Triggers.Find("t01");

            Assert.AreEqual(new Rect(14f, 10f, 4f, 8f), trigger.Rect);
            Assert.IsFalse(trigger.Once);
            Assert.IsTrue(trigger.Visible);
        }

        [Test]
        public void UnknownType_LoadsInertAndIsCounted()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(new LevelTrigger { id = "t01", type = "lever" }),
                              world, out _, out _);

            Assert.AreEqual(1, world.Triggers.Count, "never dropped");
            Assert.AreEqual(1, world.Triggers.UnknownCount);
        }

        [Test]
        public void Link_ResolvesItsSourceToAnIndex_AndItsDelayToSteps()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 1f, 1f, 2f, 2f } },
                new LevelLink { from = "t01", to = new[] { "e01" }, action = "on", delay = 0.4f }),
                world, out _, out _);

            Assert.AreEqual(1, world.Triggers.LinkCount);
        }

        [Test]
        public void Link_WithAMissingSource_IsDropped_NotCrashed()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 1f, 1f, 2f, 2f } },
                new LevelLink { from = "nope", to = new[] { "e01" }, action = "on" }),
                world, out _, out _);

            Assert.AreEqual(0, world.Triggers.LinkCount, "the validator warns; the sim ignores it");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./test.sh -testFilter KeepyUppy.SimTests.TriggerLoadTests`
Expected: FAIL — `TriggerFactory` does not exist and `SimWorld` has no `Triggers`.

- [ ] **Step 3: Write `TriggerFactory`**

Create `Assets/_Project/Scripts/Sim/Level/TriggerFactory.cs`:

```csharp
using KeepyUppy.Sim.Triggers;
using UnityEngine;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Turns schema triggers and links into runtime ones, mirroring <see cref="EntityFactory"/>.
    ///
    /// The only place a trigger type string becomes behaviour. An unrecognised type still
    /// produces a SimTrigger — an inert one — so the level round-trips intact and the editor
    /// can tell the author their file is newer than their build.
    /// </summary>
    public static class TriggerFactory
    {
        public static TriggerKind ParseKind(string type)
        {
            switch (type)
            {
                case "player_region":  return TriggerKind.PlayerRegion;
                case "balloon_region": return TriggerKind.BalloonRegion;
                case "timer":          return TriggerKind.Timer;
                default:               return TriggerKind.Unknown;
            }
        }

        public static string KindName(TriggerKind kind)
        {
            switch (kind)
            {
                case TriggerKind.PlayerRegion:  return "player_region";
                case TriggerKind.BalloonRegion: return "balloon_region";
                case TriggerKind.Timer:         return "timer";
                default:                        return "";
            }
        }

        /// <summary>arm and fire parse as Inert: declared by the schema, not simulated yet.</summary>
        public static LinkAction ParseAction(string action)
        {
            switch (action)
            {
                case "on":     return LinkAction.On;
                case "off":    return LinkAction.Off;
                case "toggle": return LinkAction.Toggle;
                default:       return LinkAction.Inert;
            }
        }

        public static SimTrigger Create(LevelTrigger data)
        {
            if (data == null) return null;

            float[] r = data.rect;

            return new SimTrigger
            {
                Id = data.id,
                Kind = ParseKind(data.type),
                Rect = r == null || r.Length < 4
                    ? new Rect(0f, 0f, 1f, 1f)
                    : new Rect(r[0], r[1], r[2], r[3]),
                Once = data.once,
                Visible = data.visible,
                AtStep = SimClock.StepsIn(data.seconds),
            };
        }

        /// <summary>
        /// Resolves a link against the triggers already loaded. A link whose source names no
        /// trigger returns null: the validator is where the author is told, and the sim is not
        /// the place to argue with a file.
        /// </summary>
        public static SimLink CreateLink(LevelLink data, TriggerField triggers)
        {
            if (data == null || triggers == null) return null;

            int from = triggers.IndexOf(data.from);
            if (from < 0) return null;

            return new SimLink
            {
                FromIndex = from,
                ToIds = data.to ?? new string[0],
                Action = ParseAction(data.action),
                DelaySteps = SimClock.StepsIn(data.delay),
            };
        }
    }
}
```

- [ ] **Step 4: Load them in `LevelLoader`**

In `LevelLoader.cs`, add to `ApplyEntities` after `LoadEntities(...)`:

```csharp
            LoadTriggers(level, world.Triggers);
```

and the method itself, beside `LoadZones`:

```csharp
        /// <summary>
        /// Triggers and the links that fan out from them (GDD 9.3).
        ///
        /// Triggers first, in file order, so a link can resolve its source to an index rather
        /// than carrying an id into the tick. File order fixes the firing order, so a
        /// re-ordered file is a different — but still deterministic — level, exactly as it is
        /// for entities.
        /// </summary>
        static void LoadTriggers(LevelData level, TriggerField triggers)
        {
            triggers.Clear();
            if (level.triggers != null)
            {
                foreach (LevelTrigger data in level.triggers)
                    triggers.Add(TriggerFactory.Create(data));
            }

            if (level.links == null) return;

            foreach (LevelLink data in level.links)
                triggers.AddLink(TriggerFactory.CreateLink(data, triggers));
        }
```

Add `using KeepyUppy.Sim.Triggers;` to the file's usings.

- [ ] **Step 5: Give `SimWorld` the field**

In `SimWorld.cs`, beside `Entities`:

```csharp
        /// <summary>Triggers and links (GDD 9.3). Stepped last; see Tick.</summary>
        public readonly TriggerField Triggers = new TriggerField();
```

Add `using KeepyUppy.Sim.Triggers;`. This is enough to compile the test; the tick wiring is Task 6.

- [ ] **Step 6: Run the test to verify it passes**

Run: `./test.sh -testFilter KeepyUppy.SimTests.TriggerLoadTests`
Expected: PASS, `failed=0`.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode/TriggerLoadTests.cs
git commit -m "Load triggers and links from the level schema"
```

---

## Task 6: Wire into `SimWorld` — the tick, the restart, and determinism

The subtle half of the feature: without start-state restoration, a fired link survives a
restart and the room the player retries is not the room they died in.

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/SimWorld.cs`
- Modify: `Assets/_Project/Scripts/Sim/AirField.cs` (`AirZone.startEnabled`, `AirField.ResetStates`)
- Modify: `Assets/_Project/Scripts/Sim/Entities/SimEntity.cs` (`StartActive`), `Entities/EntityField.cs` (`Add` records it)
- Modify: `Assets/_Project/Scripts/Sim/Level/EntityFactory.cs` (sets `StartActive`)
- Modify: `Assets/_Project/Tests/EditMode/SimTestHelpers.cs`, `DeterminismTests.cs`
- Test: `Assets/_Project/Tests/EditMode/TriggerFieldTests.cs` (the restart case)

**Interfaces:**
- Consumes: `TriggerField.Step`, `TriggerField.Reset` (Tasks 3–4); `LevelLoader.LoadTriggers` (Task 5).
- Produces: `SimEntity.StartActive`; `AirZone.startEnabled` (constructor argument order unchanged — it is set from `enabled`); `AirField.ResetStates()`; `SimTestHelpers.BuildTrapRoom(int seed)`; `SimSnapshot.triggerState` and `SimSnapshot.activeStates`.

- [ ] **Step 1: Write the failing tests**

Add to `TriggerFieldTests.cs`:

```csharp
        [Test]
        public void Restart_PutsAnArmedTrapBackToDormant()
        {
            SimWorld world = SimTestHelpers.BuildTrapRoom(1337);

            Assert.IsFalse(world.Entities.Find("bar").Active, "starts dormant");

            // The scripted run walks into the trigger region and arms it.
            world.Tick(600);
            Assert.IsTrue(world.Entities.Find("bar").Active, "the run should have armed it");

            world.Restart();

            Assert.IsFalse(world.Entities.Find("bar").Active,
                           "the room the player retries must be the room they died in");
            Assert.IsFalse(world.Triggers[0].Fired);
        }

        [Test]
        public void Restart_PutsASwitchedZoneBackToItsStartState()
        {
            SimWorld world = SimTestHelpers.BuildTrapRoom(1337);

            world.Tick(600);
            world.Restart();

            for (int i = 0; i < world.Air.Count; i++)
            {
                if (world.Air[i].id != "z_gust") continue;
                Assert.IsFalse(world.Air[i].enabled, "the gust is off again");
            }
        }
```

Add to `DeterminismTests.cs`:

```csharp
        [Test]
        public void TrapRoom_SameSeed_BitIdenticalEveryStep()
        {
            // Triggers are the second system to carry real per-step state, after the lamp's
            // exposure. Without this the guarantee rots silently (CLAUDE.md).
            SimWorld a = SimTestHelpers.BuildTrapRoom(Seed);
            SimWorld b = SimTestHelpers.BuildTrapRoom(Seed);

            for (int i = 0; i < Steps; i++)
            {
                a.Tick();
                b.Tick();

                SimSnapshot.Capture(a).AssertMatches(SimSnapshot.Capture(b), $"trap step {i}");
            }
        }

        [Test]
        public void TrapRoom_ResetAndReplayed_BitIdentical()
        {
            SimWorld world = SimTestHelpers.BuildTrapRoom(Seed);

            world.Tick(Steps);
            SimSnapshot first = SimSnapshot.Capture(world);

            world.Restart();
            world.Tick(Steps);

            first.AssertMatches(SimSnapshot.Capture(world), "trap room after replay");
        }
```

- [ ] **Step 2: Add the test room and the snapshot fields**

In `SimTestHelpers.cs`, add to `SimSnapshot`:

```csharp
        /// <summary>
        /// Trigger run state and the arm/disarm flags a link writes, folded to one value each
        /// so the snapshot does not have to know how many of either a room has.
        /// </summary>
        public int triggerState;
        public int activeStates;
```

In `SimSnapshot.Capture`, add:

```csharp
            triggerState = FoldTriggers(w.Triggers),
            activeStates = FoldActive(w.Entities, w.Air),
```

and the folds beside `FoldExposure`:

```csharp
        static int FoldTriggers(TriggerField triggers)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < triggers.Count; i++)
                {
                    hash = (hash ^ (triggers[i].Fired ? 1 : 0)) * 16777619;
                    hash = (hash ^ (triggers[i].WasInside ? 2 : 0)) * 16777619;
                }

                // A delay in flight is state too: two runs must have the same links pending.
                return (hash ^ triggers.PendingCount) * 16777619;
            }
        }

        static int FoldActive(EntityField entities, AirField air)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < entities.Count; i++)
                    hash = (hash ^ (entities[i].Active ? 1 : 0)) * 16777619;

                for (int i = 0; i < air.Count; i++)
                    hash = (hash ^ (air[i].enabled ? 1 : 0)) * 16777619;

                return hash;
            }
        }
```

In `AssertMatches`, beside the entity assertions:

```csharp
            Assert.AreEqual(triggerState, other.triggerState, $"{context}: trigger state");
            Assert.AreEqual(activeStates, other.activeStates, $"{context}: active states");
```

Add `using KeepyUppy.Sim.Triggers;` to the file. Then the room itself, beside `BuildBusyRoom`:

```csharp
        /// <summary>
        /// A GDD 9.3 betrayal trap in miniature: a dormant spike bar and an off gust, both
        /// armed by a once:true region the scripted player walks into, one after a delay.
        /// The state triggers add is only observable in a room that actually fires them.
        /// </summary>
        public static SimWorld BuildTrapRoom(int seed)
        {
            var world = new SimWorld { EnableFlutter = true };

            world.Grid.Clear();
            world.Grid.FillBorder();

            world.Air.Clear();
            world.Air.Add(new AirZone("z_gust", new Rect(8f, 1f, 6f, 10f), new Vector2(0f, 6f), false));

            world.Entities.Clear();
            world.Entities.Add(new SimEntity
            {
                Id = "bar", Kind = EntityKind.SpikeBar, Radius = 0.25f, Length = 1f,
                Angle = 90f, Active = false, StartActive = false,
                Points = new[] { new Vector2(14f, 1f) },
            }, world.Air);

            world.Triggers.Clear();
            world.Triggers.Add(new SimTrigger
            {
                Id = "t01", Kind = TriggerKind.PlayerRegion, Once = true,
                Rect = new Rect(9f, 1f, 6f, 4f),
            });
            world.Triggers.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "bar" }, Action = LinkAction.On, DelaySteps = 0,
            });
            world.Triggers.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "z_gust" }, Action = LinkAction.Toggle, DelaySteps = 24,
            });

            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Configure(new Vector2(6f, 12f), new Vector2(1.5f, 0f),
                            new Vector2(4f, 1.5f), BusyScript(), seed);

            return world;
        }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `./test.sh -testFilter KeepyUppy.SimTests`
Expected: FAIL — `SimEntity` has no `StartActive`, `SimWorld` never steps `Triggers`.

- [ ] **Step 4: Record and restore start states**

In `SimEntity.cs`, beside `Active`:

```csharp
        /// <summary>
        /// What Active returns to on a restart. A link can flip Active mid-run (GDD 9.3), and
        /// without this the room the player retries would not be the room they died in.
        /// </summary>
        public bool StartActive = true;
```

and in `Reset()`, as the first line:

```csharp
            Active = StartActive;
```

In `EntityFactory.Create`, set both — the object initialiser already sets `Active`, so add:

```csharp
            entity.StartActive = entity.Active;
```

immediately before the existing `entity.Reset();` at the end of the method.

In `AirField.cs`, add to `AirZone`:

```csharp
        /// <summary>What enabled returns to on a restart, for the same reason as SimEntity.StartActive.</summary>
        public bool startEnabled;
```

and set it in the constructor, after `this.enabled = enabled;`:

```csharp
            this.startEnabled = enabled;
```

Then, on `AirField`:

```csharp
        /// <summary>
        /// Puts every zone back to the state its level declared. Part of the synchronous
        /// restart (GDD 8): a link that switched a zone must not outlive the attempt.
        /// </summary>
        public void ResetStates()
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                AirZone zone = _zones[i];
                if (zone.enabled == zone.startEnabled) continue;

                zone.enabled = zone.startEnabled;
                _zones[i] = zone;
            }
        }
```

- [ ] **Step 5: Step and reset the field in `SimWorld`**

In `Tick()`, immediately before the final `Clock.Advance();`:

```csharp
            // Last, after contact and the exit check. A trap armed by the player's own
            // movement must not materialise inside the body that armed it in the frame it
            // armed it — that is a death with no frame in which it was avoidable (GDD 8).
            // Effects therefore land on the next step; delay 0 means next step, not this one.
            Triggers.Step(step, Player.Position, HasPlayer, Balloon.Position, Entities, Air);
```

In `Restart()`, replace the entity block with:

```csharp
            // Entity motion is derived from the step counter, so this only clears the state
            // that genuinely accumulates — a lamp's exposure — plus the arm/disarm flags a
            // link may have written (GDD 9.3).
            Triggers.Reset();
            Air.ResetStates();
            Entities.Reset();
            Entities.Step(0, Air);
```

`Triggers.Reset()` comes first so nothing re-applies while the entities are being reset.

- [ ] **Step 6: Run the whole suite**

Run: `./test.sh`
Expected: PASS, `failed=0`. The whole suite, not one fixture — this task changes `Restart`, which every determinism and contact test exercises.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode
git commit -m "Step triggers in SimWorld, and restore start states on restart"
```

---

## Task 7: Validation

`LevelValidator` already reports duplicate ids and links naming a missing id — verify those
first and do not duplicate them.

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelValidator.cs`
- Test: `Assets/_Project/Tests/EditMode/LevelValidatorTests.cs`

**Interfaces:**
- Consumes: `TriggerFactory.ParseKind`, `TriggerFactory.ParseAction` (Task 5).
- Produces: no new API — `LevelValidator.Validate(LevelData)` returns more strings.

- [ ] **Step 1: Write the failing test**

Add to `LevelValidatorTests.cs`. The file already has `static LevelData Valid()` (a lint-clean
level) and `static void AssertWarns(LevelData level, string fragment)` — use both rather than
adding new helpers. `Mentions` is the one addition, for the tests that assert the ABSENCE of a
warning:

```csharp
        static bool Mentions(System.Collections.Generic.List<string> warnings, string fragment)
        {
            foreach (string warning in warnings)
                if (warning.Contains(fragment)) return true;

            return false;
        }

        [Test]
        public void Timer_WithNoSeconds_IsAnError()
        {
            LevelData level = Valid();
            level.triggers = new[] { new LevelTrigger { id = "t01", type = "timer", seconds = 0f } };

            Assert.IsTrue(Mentions(LevelValidator.Validate(level), "timer"));
        }

        [Test]
        public void Region_WithAZeroAreaRect_IsAnError()
        {
            LevelData level = Valid();
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 4f, 4f, 0f, 3f } },
            };

            Assert.IsTrue(Mentions(LevelValidator.Validate(level), "zero-area"));
        }

        [Test]
        public void Trigger_NothingLinksFrom_IsWarned()
        {
            LevelData level = Valid();
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 4f, 4f, 2f, 2f } },
            };
            level.links = new LevelLink[0];

            Assert.IsTrue(Mentions(LevelValidator.Validate(level), "nothing links from"));
        }

        [Test]
        public void TwoOnceTriggers_WithIdenticalRects_AreWarned()
        {
            LevelData level = Valid();
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 4f, 4f, 2f, 2f }, once = true },
                new LevelTrigger { id = "t02", type = "player_region", rect = new[] { 4f, 4f, 2f, 2f }, once = true },
            };

            Assert.IsTrue(Mentions(LevelValidator.Validate(level), "identical rect"));
        }

        [Test]
        public void InertTypesAndActions_AreCalledOut()
        {
            LevelData level = Valid();
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "lever", rect = new[] { 4f, 4f, 2f, 2f } },
            };
            level.links = new[]
            {
                new LevelLink { from = "t01", to = new string[0], action = "fire" },
            };

            System.Collections.Generic.List<string> warnings = LevelValidator.Validate(level);

            Assert.IsTrue(Mentions(warnings, "not simulated"), "the inert type");
            Assert.IsTrue(Mentions(warnings, "'fire'"), "the inert action");
        }

        [Test]
        public void AWellFormedTrap_ProducesNoTriggerWarnings()
        {
            LevelData level = Valid();
            level.entities = new[]
            {
                new LevelEntity { id = "e01", type = "spike_bar", pos = new[] { 14f, 1f }, start_state = "dormant" },
            };
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 9f, 1f, 6f, 4f }, once = true },
            };
            level.links = new[]
            {
                new LevelLink { from = "t01", to = new[] { "e01" }, action = "on", delay = 0.2f },
            };

            System.Collections.Generic.List<string> warnings = LevelValidator.Validate(level);

            Assert.IsFalse(Mentions(warnings, "trigger"), string.Join(" | ", warnings));
            Assert.IsFalse(Mentions(warnings, "link"), string.Join(" | ", warnings));
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter KeepyUppy.SimTests.LevelValidatorTests`
Expected: FAIL — none of these warnings are emitted.

- [ ] **Step 3: Add the lints**

In `LevelValidator.cs`, add the call in `Validate` after `ValidateEntities(level, warnings);`:

```csharp
            ValidateTriggers(level, warnings);
```

and the method:

```csharp
        /// <summary>
        /// Triggers and links (GDD 9.3). Duplicate ids and links naming a missing id are
        /// already covered by ValidateIds — this is everything else.
        /// </summary>
        static void ValidateTriggers(LevelData level, List<string> warnings)
        {
            if (level.triggers != null)
            {
                foreach (LevelTrigger trigger in level.triggers)
                {
                    if (trigger == null) continue;

                    TriggerKind kind = TriggerFactory.ParseKind(trigger.type);

                    if (kind == TriggerKind.Unknown)
                        warnings.Add($"trigger '{trigger.id}' type '{trigger.type}' is parsed but not simulated");

                    if (kind == TriggerKind.Timer && trigger.seconds <= 0f)
                        warnings.Add($"timer trigger '{trigger.id}' has no seconds — it can never fire");

                    bool region = kind == TriggerKind.PlayerRegion || kind == TriggerKind.BalloonRegion;
                    float[] r = trigger.rect;

                    if (region && (r == null || r.Length < 4 || r[2] <= 0f || r[3] <= 0f))
                        warnings.Add($"trigger '{trigger.id}' has a zero-area rect");

                    if (!LinkedFrom(level, trigger.id))
                        warnings.Add($"nothing links from trigger '{trigger.id}'");
                }

                ReportDuplicateRects(level, warnings);
            }

            if (level.links == null) return;

            foreach (LevelLink link in level.links)
            {
                if (link == null) continue;

                if (TriggerFactory.ParseAction(link.action) == LinkAction.Inert)
                    warnings.Add($"link action '{link.action}' is parsed but not simulated");
            }
        }

        static bool LinkedFrom(LevelData level, string id)
        {
            if (level.links == null || string.IsNullOrEmpty(id)) return false;

            foreach (LevelLink link in level.links)
                if (link != null && link.from == id) return true;

            return false;
        }

        /// <summary>
        /// Two once:true triggers on the same rect: almost always a duplication mistake
        /// (level-editor-spec 7). Compared pairwise — a room has a handful of triggers, and a
        /// dictionary keyed on a float rect would be worse in every way.
        /// </summary>
        static void ReportDuplicateRects(LevelData level, List<string> warnings)
        {
            LevelTrigger[] triggers = level.triggers;

            for (int i = 0; i < triggers.Length; i++)
            for (int j = i + 1; j < triggers.Length; j++)
            {
                LevelTrigger a = triggers[i];
                LevelTrigger b = triggers[j];

                if (a == null || b == null || !a.once || !b.once) continue;
                if (a.rect == null || b.rect == null || a.rect.Length < 4 || b.rect.Length < 4) continue;

                bool same = true;
                for (int k = 0; k < 4; k++)
                    if (!Mathf.Approximately(a.rect[k], b.rect[k])) { same = false; break; }

                if (same)
                    warnings.Add($"triggers '{a.id}' and '{b.id}' have identical rects");
            }
        }
```

Add `using KeepyUppy.Sim.Triggers;` to the file's usings.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./test.sh -testFilter KeepyUppy.SimTests.LevelValidatorTests`
Expected: PASS, `failed=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Level/LevelValidator.cs Assets/_Project/Tests/EditMode/LevelValidatorTests.cs
git commit -m "Lint triggers and links"
```

---

## Task 8: Editor rules — trigger and link CRUD in `EditorSession`

**Files:**
- Modify: `Assets/_Project/Scripts/LevelEditor/EditorSession.cs`
- Test: `Assets/_Project/Tests/EditMode/EditorTriggerTests.cs`

**Interfaces:**
- Consumes: `ZoneGeometry.Normalise/Move/Snap/RectOf/ToArray` (existing).
- Produces: `EditorSession.SelectedTriggerId`, `FindTrigger(string)`, `TriggerAt(Vector2)`, `AddTrigger(Rect, string type = "player_region")`, `SelectTrigger(string)`, `SetTriggerRect(string, Rect)`, `SetTriggerType(string, string)`, `SetTriggerOnce(string, bool)`, `SetTriggerVisible(string, bool)`, `SetTriggerSeconds(string, float)`, `DeleteTrigger(string)`, `AddLink(string from, string to)`, `RemoveLink(string from, string to)`, `LinksFrom(string)`, `TargetCentre(string id, out Vector2 centre)`, `LinkAt(Vector2 world, out string from, out string to)`. Task 9 and Task 10 call all of these.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/EditorTriggerTests.cs`:

```csharp
using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The editor's trigger and link rules (level-editor-spec 4). The controller on top is
    /// input plumbing and untested by design, which is exactly why none of this lives there.
    /// </summary>
    public sealed class EditorTriggerTests
    {
        static EditorSession Session()
        {
            var grid = new TileGrid();
            grid.FillBorder();

            var session = new EditorSession();
            session.Load(new LevelData
            {
                tiles = grid.ToRows(),
                entities = new[] { new LevelEntity { id = "e01", type = "saw", pos = new[] { 20f, 8f } } },
                zones = new[] { new LevelZone { id = "z01", type = "air", rect = new[] { 2f, 2f, 4f, 4f } } },
            });
            return session;
        }

        [Test]
        public void AddTrigger_AllocatesTheLowestFreeId_AndSelectsIt()
        {
            EditorSession session = Session();

            LevelTrigger first = session.AddTrigger(new Rect(9f, 1f, 6f, 4f));
            LevelTrigger second = session.AddTrigger(new Rect(20f, 1f, 2f, 2f));

            Assert.AreEqual("t01", first.id);
            Assert.AreEqual("t02", second.id);
            Assert.AreEqual("t02", session.SelectedTriggerId);
            Assert.IsTrue(session.Dirty);
        }

        [Test]
        public void AddTrigger_AfterADelete_ReusesTheGap()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));
            session.AddTrigger(new Rect(20f, 1f, 2f, 2f));

            session.DeleteTrigger("t01");

            Assert.AreEqual("t01", session.AddTrigger(new Rect(4f, 4f, 2f, 2f)).id);
        }

        [Test]
        public void AddTrigger_Defaults_ToAnInvisibleOnceRegion()
        {
            LevelTrigger trigger = Session().AddTrigger(new Rect(9f, 1f, 6f, 4f));

            Assert.AreEqual("player_region", trigger.type);
            Assert.IsTrue(trigger.once, "the GDD 9.3 workhorse is once:true");
            Assert.IsFalse(trigger.visible);
        }

        [Test]
        public void TriggerAt_PicksTheSmallestOverlapping()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(0f, 0f, 20f, 12f));
            LevelTrigger small = session.AddTrigger(new Rect(4f, 4f, 2f, 2f));

            Assert.AreEqual(small.id, session.TriggerAt(new Vector2(5f, 5f)).id,
                            "a small trigger inside a big one must stay selectable");
            Assert.IsNull(session.TriggerAt(new Vector2(30f, 17f)));
        }

        [Test]
        public void SelectingATrigger_ClearsEveryOtherSelection()
        {
            EditorSession session = Session();
            session.SelectEntity("e01");
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));

            Assert.IsNull(session.SelectedEntityId);
            Assert.IsNull(session.SelectedZoneId);
            Assert.IsFalse(session.ExitSelected);

            session.SelectZone("z01");
            Assert.IsNull(session.SelectedTriggerId, "and the exclusion runs both ways");
        }

        [Test]
        public void SetTriggerRect_SnapsToTheTileGrid()
        {
            EditorSession session = Session();
            LevelTrigger trigger = session.AddTrigger(new Rect(9f, 1f, 6f, 4f));

            session.SetTriggerRect(trigger.id, new Rect(9.4f, 1.6f, 6.2f, 4.4f));

            Assert.AreEqual(new[] { 9f, 2f, 6f, 4f }, trigger.rect);
        }

        [Test]
        public void AddLink_AppendsToTheSameSource_RatherThanMakingASecond()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));

            session.AddLink("t01", "e01");
            session.AddLink("t01", "z01");

            Assert.AreEqual(1, session.Level.links.Length, "one link, two targets");
            Assert.AreEqual(new[] { "e01", "z01" }, session.Level.links[0].to);
        }

        [Test]
        public void AddLink_IsIdempotent_ForATargetAlreadyWired()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));

            session.AddLink("t01", "e01");
            session.AddLink("t01", "e01");

            Assert.AreEqual(1, session.Level.links[0].to.Length);
        }

        [Test]
        public void RemoveLink_DropsTheTarget_AndTheLinkWhenItsLastTargetGoes()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));
            session.AddLink("t01", "e01");
            session.AddLink("t01", "z01");

            session.RemoveLink("t01", "e01");
            Assert.AreEqual(new[] { "z01" }, session.Level.links[0].to);

            session.RemoveLink("t01", "z01");
            Assert.AreEqual(0, session.Level.links.Length, "an empty link is not a link");
        }

        [Test]
        public void DeleteTrigger_CascadesToItsLinks()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));
            session.AddLink("t01", "e01");

            Assert.IsTrue(session.DeleteTrigger("t01"));

            Assert.AreEqual(0, session.Level.links.Length, "an orphaned link is a validator error");
            Assert.IsNull(session.SelectedTriggerId);
        }

        [Test]
        public void TargetCentre_FindsEntitiesZonesAndTriggers()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(10f, 2f, 4f, 4f));

            Assert.IsTrue(session.TargetCentre("e01", out Vector2 entity));
            Assert.AreEqual(new Vector2(20f, 8f), entity);

            Assert.IsTrue(session.TargetCentre("z01", out Vector2 zone));
            Assert.AreEqual(new Vector2(4f, 4f), zone);

            Assert.IsTrue(session.TargetCentre("t01", out Vector2 trigger));
            Assert.AreEqual(new Vector2(12f, 4f), trigger);

            Assert.IsFalse(session.TargetCentre("nope", out _));
        }

        [Test]
        public void LinkAt_HitsTheArrowBetweenSourceAndTarget()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(10f, 2f, 4f, 4f));   // centre 12, 4
            session.AddLink("t01", "e01");                    // saw at 20, 8

            Assert.IsTrue(session.LinkAt(new Vector2(16f, 6f), out string from, out string to),
                          "the midpoint of the arrow");
            Assert.AreEqual("t01", from);
            Assert.AreEqual("e01", to);

            Assert.IsFalse(session.LinkAt(new Vector2(2f, 16f), out _, out _));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./test.sh -testFilter KeepyUppy.SimTests.EditorTriggerTests`
Expected: FAIL — `EditorSession` has no `AddTrigger`.

- [ ] **Step 3: Add the trigger section to `EditorSession`**

In `EditorSession.cs`, add the selection field beside `SelectedZoneId`:

```csharp
        /// <summary>
        /// The selected trigger. Mutually exclusive with the entity, zone and exit selections
        /// for the same reason they are with each other: Delete tests each independently, so
        /// two live selections means one keypress destroying two objects.
        /// </summary>
        public string SelectedTriggerId;
```

Add `SelectedTriggerId = null;` to the bodies of `SelectZone`, `SelectEntity` and `SelectExit`, and to `Load`. In `Rebind` (the method that clears selections whose object has gone — the one containing `if (FindZone(SelectedZoneId) == null) SelectedZoneId = null;`), add the matching line:

```csharp
            if (FindTrigger(SelectedTriggerId) == null) SelectedTriggerId = null;
```

Then, after the zone section, add the trigger section:

```csharp
        // ----------------------------------------------------------------- triggers

        public LevelTrigger FindTrigger(string id)
        {
            if (string.IsNullOrEmpty(id) || Level.triggers == null) return null;

            foreach (LevelTrigger trigger in Level.triggers)
                if (trigger != null && trigger.id == id) return trigger;

            return null;
        }

        /// <summary>
        /// The trigger under a world position, smallest first — the same rule as ZoneAt, and
        /// for the same reason: a small trigger drawn inside a room-wide one must stay
        /// selectable.
        /// </summary>
        public LevelTrigger TriggerAt(Vector2 world)
        {
            if (Level.triggers == null) return null;

            LevelTrigger best = null;
            float bestArea = float.MaxValue;

            foreach (LevelTrigger trigger in Level.triggers)
            {
                if (trigger?.rect == null || trigger.rect.Length < 4) continue;

                Rect rect = ZoneGeometry.RectOf(trigger.rect);
                if (!rect.Contains(world)) continue;

                float area = rect.width * rect.height;
                if (area >= bestArea) continue;

                bestArea = area;
                best = trigger;
            }

            return best;
        }

        /// <summary>
        /// A new trigger. Defaults to an invisible once:true player region — GDD 9.3's
        /// workhorse, and the thing an author reaches for nine times in ten.
        /// </summary>
        public LevelTrigger AddTrigger(Rect rect, string type = "player_region")
        {
            var trigger = new LevelTrigger
            {
                id = NextTriggerId(),
                type = type,
                rect = ZoneGeometry.ToArray(ZoneGeometry.Snap(rect)),
                once = true,
                visible = false,
            };

            var triggers = new List<LevelTrigger>(Level.triggers ?? new LevelTrigger[0]) { trigger };
            Level.triggers = triggers.ToArray();

            SelectTrigger(trigger.id);
            Dirty = true;
            return trigger;
        }

        public void SelectTrigger(string id)
        {
            SelectedTriggerId = id;
            SelectedEntityId = null;
            SelectedZoneId = null;
            ExitSelected = false;
        }

        public void SetTriggerRect(string id, Rect rect)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null) return;

            trigger.rect = ZoneGeometry.ToArray(ZoneGeometry.Snap(rect));
            Dirty = true;
        }

        public void SetTriggerType(string id, string type)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null || trigger.type == type) return;

            trigger.type = type;
            Dirty = true;
        }

        public void SetTriggerOnce(string id, bool once)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null || trigger.once == once) return;

            trigger.once = once;
            Dirty = true;
        }

        public void SetTriggerVisible(string id, bool visible)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null || trigger.visible == visible) return;

            trigger.visible = visible;
            Dirty = true;
        }

        public void SetTriggerSeconds(string id, float seconds)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null || Mathf.Approximately(trigger.seconds, seconds)) return;

            trigger.seconds = seconds;
            Dirty = true;
        }

        /// <summary>
        /// Deletes a trigger and every link that fired from it. An orphaned link is a
        /// validator error, and leaving an author to clean up after their own delete is how a
        /// level file rots.
        /// </summary>
        public bool DeleteTrigger(string id)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null) return false;

            var triggers = new List<LevelTrigger>(Level.triggers);
            triggers.Remove(trigger);
            Level.triggers = triggers.ToArray();

            var links = new List<LevelLink>(Level.links ?? new LevelLink[0]);
            links.RemoveAll(link => link != null && link.from == id);
            Level.links = links.ToArray();

            if (SelectedTriggerId == id) SelectedTriggerId = null;
            Dirty = true;
            return true;
        }

        /// <summary>Lowest unused tNN id, matching how entity and zone ids are allocated.</summary>
        string NextTriggerId()
        {
            for (int i = 1; i < 1000; i++)
            {
                string candidate = "t" + i.ToString("00");
                if (FindTrigger(candidate) == null) return candidate;
            }

            return "t" + (Level.triggers?.Length ?? 0);
        }
```

- [ ] **Step 4: Add the link section**

Straight after, in the same file:

```csharp
        // -------------------------------------------------------------------- links

        /// <summary>The link fired by a trigger, or null. One link per source, many targets.</summary>
        public LevelLink LinksFrom(string from)
        {
            if (string.IsNullOrEmpty(from) || Level.links == null) return null;

            foreach (LevelLink link in Level.links)
                if (link != null && link.from == from) return link;

            return null;
        }

        /// <summary>
        /// Wires a trigger to a target. A second target on the same trigger is appended to the
        /// existing link rather than making another one: two links from one source with the
        /// same action would be a distinction the author cannot see on screen.
        /// </summary>
        public LevelLink AddLink(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return null;
            if (FindTrigger(from) == null) return null;

            LevelLink link = LinksFrom(from);

            if (link == null)
            {
                link = new LevelLink { from = from, to = new[] { to }, action = "toggle" };
                var links = new List<LevelLink>(Level.links ?? new LevelLink[0]) { link };
                Level.links = links.ToArray();
                Dirty = true;
                return link;
            }

            var targets = new List<string>(link.to ?? new string[0]);
            if (targets.Contains(to)) return link;

            targets.Add(to);
            link.to = targets.ToArray();
            Dirty = true;
            return link;
        }

        public void SetLinkAction(string from, string action)
        {
            LevelLink link = LinksFrom(from);
            if (link == null || link.action == action) return;

            link.action = action;
            Dirty = true;
        }

        public void SetLinkDelay(string from, float delay)
        {
            LevelLink link = LinksFrom(from);
            if (link == null || Mathf.Approximately(link.delay, delay)) return;

            link.delay = Mathf.Max(delay, 0f);
            Dirty = true;
        }

        /// <summary>Unwires one target. The link goes when its last target does.</summary>
        public bool RemoveLink(string from, string to)
        {
            LevelLink link = LinksFrom(from);
            if (link == null) return false;

            var targets = new List<string>(link.to ?? new string[0]);
            if (!targets.Remove(to)) return false;

            if (targets.Count > 0)
            {
                link.to = targets.ToArray();
                Dirty = true;
                return true;
            }

            var links = new List<LevelLink>(Level.links);
            links.Remove(link);
            Level.links = links.ToArray();
            Dirty = true;
            return true;
        }

        /// <summary>
        /// Where an id sits in the room, for drawing an arrow to it. Entities, zones and
        /// triggers can all be link targets, so all three answer here.
        /// </summary>
        public bool TargetCentre(string id, out Vector2 centre)
        {
            centre = Vector2.zero;

            LevelEntity entity = FindEntity(id);
            if (entity?.pos != null && entity.pos.Length >= 2)
            {
                centre = new Vector2(entity.pos[0], entity.pos[1]);
                return true;
            }

            LevelZone zone = FindZone(id);
            if (zone != null)
            {
                centre = ZoneGeometry.RectOf(zone.rect).center;
                return true;
            }

            LevelTrigger trigger = FindTrigger(id);
            if (trigger != null)
            {
                centre = ZoneGeometry.RectOf(trigger.rect).center;
                return true;
            }

            return false;
        }

        /// <summary>Hit radius for clicking an arrow, in metres. About 6 px at 20 px/metre.</summary>
        public const float LinkHitRadius = 0.3f;

        /// <summary>
        /// The link arrow under a world position, in file order. Distance to the segment
        /// between the two centres — the arrow the controller draws is that segment, so the
        /// click target and the drawing cannot drift apart.
        /// </summary>
        public bool LinkAt(Vector2 world, out string from, out string to)
        {
            from = null;
            to = null;

            if (Level.links == null) return false;

            foreach (LevelLink link in Level.links)
            {
                if (link?.to == null) continue;
                if (!TargetCentre(link.from, out Vector2 source)) continue;

                foreach (string target in link.to)
                {
                    if (!TargetCentre(target, out Vector2 centre)) continue;
                    if (DistanceToSegment(world, source, centre) > LinkHitRadius) continue;

                    from = link.from;
                    to = target;
                    return true;
                }
            }

            return false;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq <= 0f) return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            return Vector2.Distance(p, a + ab * t);
        }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `./test.sh -testFilter KeepyUppy.SimTests.EditorTriggerTests`
Expected: PASS, `failed=0`.

- [ ] **Step 6: Run the whole suite**

Run: `./test.sh`
Expected: PASS — `EditorSessionTests` exercises the selection exclusivity this task changed.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Scripts/LevelEditor/EditorSession.cs Assets/_Project/Tests/EditMode/EditorTriggerTests.cs
git commit -m "Add trigger and link editing rules to EditorSession"
```

---

## Task 9: The Trigger tool

**Files:**
- Modify: `Assets/_Project/Scripts/LevelEditor/EditorTool.cs`
- Modify: `Assets/_Project/Scripts/LevelEditor/LevelEditorController.cs`

**Interfaces:**
- Consumes: everything Task 8 produced.
- Produces: `EditorTool.Trigger = 8`; the controller's `TriggerTool(Mouse, Vector2)` and `DrawTriggers()`.

No new automated tests: `LevelEditorController` is a MonoBehaviour with no test coverage by
design (CLAUDE.md), and the rules it drives are already covered by Task 8. Verification is the
full suite plus the manual check in Step 6.

- [ ] **Step 1: Add the tool value**

In `EditorTool.cs`, after `Place = 7`:

```csharp
        /// <summary>
        /// 8 — drag a trigger rect (GDD 9.3). Past Place because the spec's digit bindings
        /// stop at 6 and shuffling them under the author's fingers is the one thing this enum
        /// exists to prevent.
        /// </summary>
        Trigger = 8,
```

- [ ] **Step 2: Bind the key and the palette button**

In `LevelEditorController.cs`, beside the other digit bindings:

```csharp
            if (kb.digit8Key.wasPressedThisFrame) Session.Tool = EditorTool.Trigger;
```

In `DrawPalette`, after the `5 Path` button:

```csharp
            if (GUILayout.Button("8 Trigger", GUILayout.Width(70f))) Session.Tool = EditorTool.Trigger;
```

Extend the `Delete` handler so it deletes a selected trigger too — beside the existing zone branch:

```csharp
            if (kb.deleteKey.wasPressedThisFrame && Session.Tool == EditorTool.Trigger
                && Session.SelectedTriggerId != null)
            {
                Session.BeginEdit();
                Session.DeleteTrigger(Session.SelectedTriggerId);
                SyncDynamic();
            }
```

- [ ] **Step 3: Add the tool itself**

Add the dispatch case beside the others:

```csharp
                case EditorTool.Trigger: TriggerTool(mouse, world); break;
```

and the method. It is `ZoneTool` with the nouns changed, including its `_zoneCreating` flag —
a create drag is a distinct mode, not something to infer from what is under the cursor on
release. Add the drag fields beside `_zoneCorner` / `_zoneAnchor`:

```csharp
        ZoneCorner _triggerCorner;
        Vector2Int _triggerAnchor;
        Vector2 _triggerDragOffset;
        bool _triggerCreating;
```

```csharp
        /// <summary>
        /// Trigger rects (GDD 9.3). Identical in shape to ZoneTool and deliberately so: both
        /// are tile rects on the same grid, so both route through ZoneGeometry and an author
        /// who knows one tool knows the other.
        /// </summary>
        void TriggerTool(Mouse mouse, Vector2 world)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                LevelTrigger hit = Session.TriggerAt(world);
                if (hit != null)
                {
                    Session.BeginEdit();
                    Session.DeleteTrigger(hit.id);
                    SyncDynamic();
                    _status = "deleted trigger";
                }

                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                LevelTrigger selected = Session.FindTrigger(Session.SelectedTriggerId);

                _triggerCorner = selected != null
                    ? ZoneGeometry.HandleAt(ZoneGeometry.RectOf(selected.rect), world, ZoneHandleRadius)
                    : ZoneCorner.None;

                LevelTrigger hit = Session.TriggerAt(world);

                if (_triggerCorner != ZoneCorner.None)
                {
                    _triggerAnchor = ZoneGeometry.AnchorOf(ZoneGeometry.RectOf(selected.rect), _triggerCorner);
                    Session.BeginEdit();
                    _triggerCreating = false;
                    _dragging = true;
                }
                else if (hit != null)
                {
                    Session.SelectTrigger(hit.id);
                    Rect rect = ZoneGeometry.RectOf(hit.rect);
                    _triggerDragOffset = new Vector2(rect.xMin, rect.yMin) - world;
                    Session.BeginEdit();
                    _triggerCreating = false;
                    _dragging = true;
                }
                else
                {
                    _dragStart = GridCursor.ToTile(world);
                    _triggerCreating = true;
                    _dragging = true;
                }
            }

            if (!_dragging) return;

            // A create drag resolves on release, so the author sees the rect they dragged
            // rather than one that grew out of the first tile.
            if (_triggerCreating)
            {
                if (!mouse.leftButton.wasReleasedThisFrame) return;

                Session.BeginEdit();
                LevelTrigger created =
                    Session.AddTrigger(ZoneGeometry.Normalise(_dragStart, GridCursor.ToTile(world)));
                SyncDynamic();

                _dragging = false;
                _triggerCreating = false;
                _status = $"added {created.id}";
                return;
            }

            LevelTrigger dragged = Session.FindTrigger(Session.SelectedTriggerId);
            if (dragged?.rect == null) { _dragging = false; return; }

            Rect current = ZoneGeometry.RectOf(dragged.rect);

            Rect updated = _triggerCorner != ZoneCorner.None
                ? ZoneGeometry.Normalise(_triggerAnchor, GridCursor.ToTile(world))
                : ZoneGeometry.Move(current, world + _triggerDragOffset);

            Session.SetTriggerRect(dragged.id, updated);
            SyncDynamic();

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _status = _triggerCorner != ZoneCorner.None ? "resized trigger" : "moved trigger";
                _dragging = false;
                _triggerCorner = ZoneCorner.None;
            }
        }
```

Read `ZoneTool` beside this while writing it — the two should read the same, and any place they
differ is a place to check you have not improvised. There is deliberately no `N`-style forced
create: triggers nest far less than zones do, and `N` is already the zone tool's.

Also clear the drag state when the tool changes, where `_zoneForceNew` is cleared:

```csharp
            if (Session.Tool != EditorTool.Trigger) { _triggerCorner = ZoneCorner.None; _triggerCreating = false; }
```

- [ ] **Step 4: Draw them**

In `DrawOverlays`, after `DrawZones();`:

```csharp
            DrawTriggers();
```

and the method beside `DrawZones`:

```csharp
        /// <summary>
        /// Every trigger, whatever its type — the author must see what the player cannot. A
        /// visible:true trigger is filled, an invisible one outlined, so the two are told
        /// apart at a glance. Violet: never the reserved hazard hue (GDD 13.1).
        /// </summary>
        void DrawTriggers()
        {
            if (Session.Level.triggers == null) return;

            foreach (LevelTrigger trigger in Session.Level.triggers)
            {
                if (trigger?.rect == null || trigger.rect.Length < 4) continue;

                Rect rect = ZoneGeometry.RectOf(trigger.rect);
                bool selected = trigger.id == Session.SelectedTriggerId;

                if (trigger.visible)
                {
                    DrawWorldRect(rect, TriggerFill);
                }
                else
                {
                    Color edge = TriggerFill;
                    edge.a = 0.55f;
                    DrawWorldRect(new Rect(rect.xMin, rect.yMax - 0.08f, rect.width, 0.08f), edge);
                    DrawWorldRect(new Rect(rect.xMin, rect.yMin, rect.width, 0.08f), edge);
                    DrawWorldRect(new Rect(rect.xMin, rect.yMin, 0.08f, rect.height), edge);
                    DrawWorldRect(new Rect(rect.xMax - 0.08f, rect.yMin, 0.08f, rect.height), edge);
                }

                if (!selected) continue;

                DrawHandle(new Vector2(rect.xMin, rect.yMin));
                DrawHandle(new Vector2(rect.xMax, rect.yMin));
                DrawHandle(new Vector2(rect.xMin, rect.yMax));
                DrawHandle(new Vector2(rect.xMax, rect.yMax));
            }
        }

        /// <summary>Violet. Not hazard red, not balloon yellow (GDD 13.1).</summary>
        static readonly Color TriggerFill = new Color(0.62f, 0.45f, 0.85f, 0.25f);
```

- [ ] **Step 5: Run the whole suite**

Run: `./test.sh`
Expected: PASS, `failed=0`. This task must not change any existing behaviour.

- [ ] **Step 6: Manual check (there is no automated cover for a MonoBehaviour)**

Open the project in Unity, enter play mode with `LEVEL_EDITOR` defined, open a level, press
`8`, and confirm: a drag on empty space creates a trigger; a drag inside one moves it; a corner
handle resizes it; `Delete` and right-click remove it; the rect is outlined, not filled. Note in
the commit message what was checked.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Scripts/LevelEditor
git commit -m "Add the trigger tool"
```

---

## Task 10: The Link tool and the trigger inspector

**Files:**
- Create: `Assets/_Project/Scripts/LevelEditor/TriggerInspector.cs`
- Modify: `Assets/_Project/Scripts/LevelEditor/LevelEditorController.cs`

**Interfaces:**
- Consumes: `EditorSession.AddLink/RemoveLink/LinksFrom/LinkAt/TargetCentre/SetLinkAction/SetLinkDelay` and the trigger setters (Task 8).
- Produces: `TriggerInspector.Draw(EditorSession, Dictionary<string, string>) → bool`; the controller's `LinkTool` and `DrawLinks`.

- [ ] **Step 1: Write the inspector**

Create `Assets/_Project/Scripts/LevelEditor/TriggerInspector.cs`:

```csharp
#if LEVEL_EDITOR
using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using KeepyUppy.Sim.Triggers;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// The selected trigger's fields and its outgoing links (level-editor-spec 4).
    ///
    /// Its own file because LevelEditorController is already long. IMGUI and untested, the
    /// same boundary ZoneInspector sits on — the rules it edits live in EditorSession, which
    /// is tested.
    /// </summary>
    public static class TriggerInspector
    {
        /// <summary>Every type the schema declares. Three of the seven are simulated.</summary>
        static readonly string[] Types =
        {
            "player_region", "balloon_region", "timer",
            "lever", "balloon_touches", "player_grounded", "on_first_entry",
        };

        static readonly string[] Actions = { "toggle", "on", "off", "arm", "fire" };

        /// <summary>Draws the panel. True if anything changed, so the caller can push the edit.</summary>
        public static bool Draw(EditorSession session, Dictionary<string, string> fieldText)
        {
            LevelTrigger trigger = session.FindTrigger(session.SelectedTriggerId);
            if (trigger == null) return false;

            bool changed = false;

            GUILayout.Label($"trigger {trigger.id}");

            foreach (string type in Types)
            {
                bool active = trigger.type == type;
                if (GUILayout.Toggle(active, type, "Button") == active) continue;

                session.BeginEdit();
                session.SetTriggerType(trigger.id, type);
                changed = true;
            }

            // Only three types are read by TriggerFactory. Saying so here is the whole
            // mitigation for authoring ahead of the sim, as it is for the inert zone types.
            if (TriggerFactory.ParseKind(trigger.type) == TriggerKind.Unknown)
                GUILayout.Label($"   ({trigger.type} is inert — parsed and saved, no sim behaviour yet)");

            Rect rect = ZoneGeometry.RectOf(trigger.rect);
            float x = Row("x", $"{trigger.id}.x", rect.xMin, fieldText);
            float y = Row("y", $"{trigger.id}.y", rect.yMin, fieldText);
            float w = Row("w", $"{trigger.id}.w", rect.width, fieldText);
            float h = Row("h", $"{trigger.id}.h", rect.height, fieldText);

            var typed = ZoneGeometry.Snap(new Rect(x, y, Mathf.Max(w, 1f), Mathf.Max(h, 1f)));
            if (typed != rect)
            {
                session.BeginEdit();
                session.SetTriggerRect(trigger.id, typed);
                changed = true;
            }

            if (trigger.type == "timer")
            {
                float seconds = Row("seconds", $"{trigger.id}.sec", trigger.seconds, fieldText);
                if (!Mathf.Approximately(seconds, trigger.seconds))
                {
                    session.BeginEdit();
                    session.SetTriggerSeconds(trigger.id, seconds);
                    changed = true;
                }
            }

            bool once = GUILayout.Toggle(trigger.once, trigger.once ? "once: yes" : "once: no");
            if (once != trigger.once)
            {
                session.BeginEdit();
                session.SetTriggerOnce(trigger.id, once);
                changed = true;
            }

            bool visible = GUILayout.Toggle(trigger.visible, trigger.visible ? "visible" : "invisible");
            if (visible != trigger.visible)
            {
                session.BeginEdit();
                session.SetTriggerVisible(trigger.id, visible);
                changed = true;
            }

            changed |= DrawLinks(session, trigger, fieldText);

            if (GUILayout.Button("Delete trigger", GUILayout.Width(110f)))
            {
                session.BeginEdit();
                session.DeleteTrigger(trigger.id);
                changed = true;
            }

            return changed;
        }

        static bool DrawLinks(EditorSession session, LevelTrigger trigger,
                              Dictionary<string, string> fieldText)
        {
            LevelLink link = session.LinksFrom(trigger.id);

            if (link == null)
            {
                GUILayout.Label("no links — use tool 6 to wire one");
                return false;
            }

            bool changed = false;

            GUILayout.Label("action");
            GUILayout.BeginHorizontal();

            foreach (string action in Actions)
            {
                bool active = link.action == action;
                if (GUILayout.Toggle(active, action, "Button", GUILayout.Width(46f)) == active) continue;

                session.BeginEdit();
                session.SetLinkAction(trigger.id, action);
                changed = true;
            }

            GUILayout.EndHorizontal();

            if (TriggerFactory.ParseAction(link.action) == LinkAction.Inert)
                GUILayout.Label($"   ({link.action} is inert — parsed and saved, no sim behaviour yet)");

            float delay = Row("delay s", $"{trigger.id}.delay", link.delay, fieldText);
            if (!Mathf.Approximately(delay, link.delay))
            {
                session.BeginEdit();
                session.SetLinkDelay(trigger.id, delay);
                changed = true;
            }

            foreach (string target in link.to ?? new string[0])
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"→ {target}", GUILayout.Width(120f));

                if (GUILayout.Button("x", GUILayout.Width(24f)))
                {
                    session.BeginEdit();
                    session.RemoveLink(trigger.id, target);
                    changed = true;
                    GUILayout.EndHorizontal();
                    break;
                }

                GUILayout.EndHorizontal();
            }

            return changed;
        }

        /// <summary>
        /// A labelled number box. The typed text is held per field so a half-finished number
        /// is never snapped back mid-keystroke — DevNumber, shared with the tuning panel.
        /// </summary>
        static float Row(string label, string key, float value, Dictionary<string, string> fieldText)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(70f));

            string text = fieldText.TryGetValue(key, out string held) ? held : DevNumber.Format(value, false);
            string typed = GUILayout.TextField(text, GUILayout.Width(70f));

            if (typed != text) fieldText[key] = typed;

            GUILayout.EndHorizontal();

            return DevNumber.TryParse(typed, false, out float parsed) ? parsed : value;
        }
    }
}
#endif
```

- [ ] **Step 2: Show it**

In `LevelEditorController.DrawInspector`, before the zone branch:

```csharp
            if ((Session.Tool == EditorTool.Trigger || Session.Tool == EditorTool.Link)
                && Session.SelectedTriggerId != null)
            {
                DrawTriggerInspector();
                return;
            }
```

and the method beside `DrawZoneInspector`:

```csharp
        void DrawTriggerInspector()
        {
            _inspectorRect = InspectorRect(460f);
            GUILayout.BeginArea(_inspectorRect, GUI.skin.box);

            bool changed = TriggerInspector.Draw(Session, _fieldText);

            GUILayout.EndArea();

            if (changed) SyncDynamic();
        }
```

- [ ] **Step 3: Add the Link tool**

Bind it beside the other digits and add the palette button:

```csharp
            if (kb.digit6Key.wasPressedThisFrame) Session.Tool = EditorTool.Link;
```

```csharp
            if (GUILayout.Button("6 Link", GUILayout.Width(70f))) Session.Tool = EditorTool.Link;
```

Add the dispatch case and the tool:

```csharp
                case EditorTool.Link: LinkTool(mouse, world); break;
```

```csharp
        /// <summary>
        /// Wiring (level-editor-spec 4, tool 6). Click a trigger, then click an entity, zone
        /// or trigger to wire it. Right-click an arrow to unwire that one target.
        ///
        /// The selected trigger IS the pending source, so there is no extra mode state to get
        /// out of sync with what the author can see highlighted.
        /// </summary>
        void LinkTool(Mouse mouse, Vector2 world)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (!Session.LinkAt(world, out string from, out string to)) return;

                Session.BeginEdit();
                Session.RemoveLink(from, to);
                SyncDynamic();
                _status = $"unlinked {from} → {to}";
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame) return;

            LevelTrigger trigger = Session.TriggerAt(world);

            if (trigger != null && Session.SelectedTriggerId == null)
            {
                Session.SelectTrigger(trigger.id);
                _status = $"linking from {trigger.id} — click a target";
                return;
            }

            if (Session.SelectedTriggerId == null)
            {
                _status = "click a trigger first";
                return;
            }

            string target = TargetIdAt(world);

            if (target == null || target == Session.SelectedTriggerId)
            {
                // Clicking the source again, or empty space, drops the pending wire rather
                // than leaving the author in a mode they cannot see they are in.
                Session.SelectTrigger(null);
                _status = "link cancelled";
                return;
            }

            Session.BeginEdit();
            Session.AddLink(Session.SelectedTriggerId, target);
            SyncDynamic();
            _status = $"linked {Session.SelectedTriggerId} → {target}";
        }

        /// <summary>An entity, zone or trigger under the cursor, in that order of precedence.</summary>
        string TargetIdAt(Vector2 world)
        {
            LevelEntity entity = Session.EntityAt(world);
            if (entity != null) return entity.id;

            LevelZone zone = Session.ZoneAt(world);
            if (zone != null) return zone.id;

            LevelTrigger trigger = Session.TriggerAt(world);
            return trigger?.id;
        }
```

- [ ] **Step 4: Draw the arrows**

In `DrawOverlays`, after `DrawTriggers();`:

```csharp
            DrawLinks();
```

```csharp
        /// <summary>
        /// One line per wired target, from the trigger's centre to the target's. The same
        /// segment EditorSession.LinkAt hit-tests, so what is clickable is what is drawn.
        /// </summary>
        void DrawLinks()
        {
            if (Session.Level.links == null) return;

            foreach (LevelLink link in Session.Level.links)
            {
                if (link?.to == null) continue;
                if (!Session.TargetCentre(link.from, out Vector2 source)) continue;

                bool selected = link.from == Session.SelectedTriggerId;
                Color colour = TriggerFill;
                colour.a = selected ? 0.9f : 0.5f;

                foreach (string target in link.to)
                {
                    if (!Session.TargetCentre(target, out Vector2 centre)) continue;

                    DrawWorldLine(source, centre, colour);

                    // A blob at the target end: an arrowhead needs a rotated triangle, and
                    // spec 2 is explicit that ugly is correct here.
                    Vector2 tip = Vector2.MoveTowards(centre, source, 0.35f);
                    DrawWorldRect(new Rect(tip.x - 0.15f, tip.y - 0.15f, 0.3f, 0.3f), colour);
                }
            }
        }
```

- [ ] **Step 5: Run the whole suite**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 6: Manual check**

In Unity: press `6`, click a trigger then a saw — an arrow appears; the inspector lists the
target with an `x`; changing action and delay updates the JSON on save; right-clicking the arrow
removes it; deleting the trigger removes the arrow too.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Scripts/LevelEditor
git commit -m "Add the link tool and the trigger inspector"
```

---

## Task 11: Drawing visible triggers in game

**Files:**
- Create: `Assets/_Project/Scripts/Runtime/TriggerViewSync.cs`
- Modify: `Assets/_Project/Scripts/Runtime/LevelSession.cs`

**Interfaces:**
- Consumes: `SimWorld.Triggers`, `SimTrigger.Visible`, `SimTrigger.Rect` (Tasks 3–6); `QuadView.Create(Transform, string, Color, Vector2 centre, Vector2 size, float z)` (existing).
- Produces: `TriggerViewSync` with a public `SimWorld World`, built by `LevelSession.BuildTriggerViews`.

- [ ] **Step 1: Write the view**

Create `Assets/_Project/Scripts/Runtime/TriggerViewSync.cs`:

```csharp
using System.Collections.Generic;
using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Placeholder rendering for visible triggers (GDD 9.3).
    ///
    /// Only triggers with visible:true are drawn. The invisible ones are the point of the
    /// betrayal trap — they are drawn in the editor, and nowhere else — so this deliberately
    /// shows the author less than the editor does.
    ///
    /// Violet: not the reserved hazard hue and not balloon yellow (GDD 13.1). A trigger is
    /// not lethal, and wearing the colour that means "this hurts" would be a lie.
    /// </summary>
    public sealed class TriggerViewSync : MonoBehaviour
    {
        public SimWorld World;

        readonly List<GameObject> _views = new List<GameObject>();
        int _seenCount = -1;

        void LateUpdate()
        {
            if (World == null) return;

            if (World.Triggers.Count != _seenCount) Rebuild();

            for (int i = 0; i < World.Triggers.Count && i < _views.Count; i++)
            {
                GameObject view = _views[i];
                if (view == null) continue;   // an invisible trigger: no quad was made for it

                Rect rect = World.Triggers[i].Rect;

                // Followed every frame rather than only on a rebuild, because the editor moves
                // and resizes triggers while the level is loaded — the same reason ZoneViewSync
                // does it.
                view.transform.localPosition = new Vector3(rect.center.x, rect.center.y, -0.24f);
                view.transform.localScale = new Vector3(rect.width, rect.height, 1f);
            }
        }

        void Rebuild()
        {
            foreach (GameObject view in _views)
                if (view != null) Destroy(view);

            _views.Clear();

            for (int i = 0; i < World.Triggers.Count; i++)
            {
                if (!World.Triggers[i].Visible)
                {
                    // Hold the slot so the list stays index-parallel with the TriggerField.
                    _views.Add(null);
                    continue;
                }

                Rect rect = World.Triggers[i].Rect;

                _views.Add(QuadView.Create(transform, $"trigger_{World.Triggers[i].Id}", Marked,
                                           rect.center, new Vector2(rect.width, rect.height), -0.24f));
            }

            _seenCount = World.Triggers.Count;
        }

        /// <summary>
        /// Faint, like a zone: it sits behind everything the player is tracking, and the
        /// balloon is the highest-contrast thing on screen, always (GDD 13.1).
        /// </summary>
        static readonly Color Marked = new Color(0.62f, 0.45f, 0.85f, 0.20f);
    }
}
```

Check `QuadView.Create`'s exact signature in `Assets/_Project/Scripts/Runtime/QuadView.cs` and
match it — `ZoneViewSync.Rebuild` is the reference call.

- [ ] **Step 2: Build it with the session**

In `LevelSession.Create`, after `session.BuildZoneViews(world);`:

```csharp
            session.BuildTriggerViews(world);
```

and the builder beside `BuildZoneViews`:

```csharp
        void BuildTriggerViews(SimWorld world)
            => gameObject.AddComponent<TriggerViewSync>().World = world;
```

Match the shape of the existing `BuildZoneViews` — if it takes a child root rather than adding
to `gameObject`, do the same here.

- [ ] **Step 3: Run the whole suite**

Run: `./test.sh`
Expected: PASS, `failed=0`. Views have no EditMode coverage; this confirms nothing else broke.

- [ ] **Step 4: Manual check**

Play a level with one `visible: true` trigger and one without. Exactly one violet rect appears,
in the right place, behind the balloon and the player.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Runtime
git commit -m "Draw visible triggers in game"
```

---

## Task 12: Documentation and a trap to play

**Files:**
- Modify: `docs/level-editor-spec.md` (§3, §4, §7)
- Modify: `docs/balloon-game-gdd.md` (§9.2a)
- Modify: `docs/superpowers/specs/2026-08-28-triggers-and-links-design.md` (status line)
- Create: `Assets/StreamingAssets/levels/w1_trap.json` — a draft, deliberately NOT added to `index.json`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing code-facing.

- [ ] **Step 1: Update the level-editor spec**

In §3, after the zone "as built" block, add a **Triggers and links — 2026-08-28, as built**
section stating: the two new fields (`visible` defaults false, `seconds` is timer-only and
becomes steps at load); that `player_region`, `balloon_region` and `timer` are simulated and the
other four load inert and are counted; that `on`, `off` and `toggle` are simulated and `arm` and
`fire` load inert; that firing is edge-triggered, `once:true` latches, and `once:false` re-fires
on re-entry; and that **effects land on the step after the trigger fires, so `delay: 0` means
next step** — a fairness rule, so a trap cannot materialise inside the body that armed it.

In §4's tool table, add the row `| Trigger | `8` | Drag empty space to create, drag a trigger to
move, drag a corner handle to resize. `Delete` or right-click removes one. Type, rect, `once`,
`visible`, `seconds` and the outgoing link in the inspector. |` and fill in the Link row's
behaviour: click a trigger then a target to wire it, right-click an arrow to unwire it, and
targets append to one link per source.

In §7, add the new lints to the list and change the phase-4 row from deferred to built.

- [ ] **Step 2: Update the GDD**

In §9.2a, add the spike bar to the list of simulated types, and replace the paragraph beginning
"`start_state: dormant` is honoured now" with one saying triggers and links now arm it: region
and timer triggers, `on`/`off`/`toggle` links with a delay in fixed steps, and that a fired link
is undone by `Restart()` so the room the player retries is the room they died in.

No §22 row: nothing LOCKED changed.

- [ ] **Step 3: Mark the spec built**

In the design doc's header, change `**Status:** designed` to `**Status:** built 2026-08-28`.

- [ ] **Step 4: Author the trap level**

Create `Assets/StreamingAssets/levels/w1_trap.json` by hand, or build it in the editor and save.
It must contain: a dormant `spike_bar` on the floor past the middle of the room, a `once: true`
`player_region` trigger covering the approach to it, and a link `{ "from": "t01", "to": ["s01"],
"action": "on", "delay": 0.0 }`. Keep the key order the other level files use, no minifying,
`meta.par_touches` set to a plausible number.

Do **not** add it to `index.json`: a level on disk but absent from the manifest is a draft — the
editor opens it, the game does not list it. That is what this level is.

- [ ] **Step 5: Verify and play it**

Run: `./test.sh`
Expected: PASS, `failed=0`.

Then open `w1_trap` in the editor and play it. This is the part the tests cannot cover
(CLAUDE.md): look at whether the arming moment is legible in the frame it happens; whether the
one-step effect delay is felt at all; and whether the trap reads as cruel-but-learnable rather
than unfair (GDD §8 — never demand precise platforming and precise balloon-tracking in the same
two seconds). Report what it felt like rather than describing it as working.

- [ ] **Step 6: Commit**

```bash
git add docs Assets/StreamingAssets/levels/w1_trap.json Assets/StreamingAssets/levels/w1_trap.json.meta
git commit -m "Document triggers and links, and add a trap draft to play"
```

---

## Self-Review

Checked against the spec:

- Spike bar rather than toggled tiles → Task 2. Visible flag rather than a physical object → Tasks 1, 11. Three trigger kinds → Task 3. Three link actions → Task 4. Trigger on `8`, Link on `6` → Tasks 9, 10. Triggers evaluate last → Task 6.
- Schema `visible` + `seconds` → Task 1. Ids `tNN` → Task 8. `links[]` unchanged → no task, correct.
- Every listed lint → Task 7; the two the validator already had (duplicate ids, links naming a missing id) are deliberately not re-implemented, and Task 7 Step 1 verifies them via `AWellFormedTrap_ProducesNoTriggerWarnings`.
- Editor: `ZoneGeometry` reuse, `EditorSession` rules, cascade-on-delete, selection exclusivity, `TriggerInspector` in its own file, live push through `SyncDynamic`, inert types marked on screen → Tasks 8–10.
- Rendering: editor draws all, game draws `visible` only, violet, no interpolation → Tasks 9, 11.
- Testing table: `TriggerFieldTests` (3, 4, 6), `DeterminismTests` (6), `EditorTriggerTests` (8), `LevelValidatorTests` (7), `LevelFormatTests` (1). `TriggerLoadTests` (5) is an addition to the spec's table, covering the schema→sim boundary the table implied but did not name.
- One thing the spec did not name and this plan adds: `SimEntity.StartActive` and `AirField.ResetStates()`. Without them a fired link outlives `Restart()`, which would break GDD §8's "every counter, buffer and cooldown clears". Covered by Task 6's two restart tests.
- Names used consistently throughout: `TriggerField.Step/Reset/Add/AddLink/IndexOf/Find/FiredThisStep/PendingCount/LinkCount/UnknownCount`; `TriggerFactory.ParseKind/KindName/ParseAction/Create/CreateLink`; `EditorSession.AddTrigger/TriggerAt/FindTrigger/SelectTrigger/SetTrigger*/DeleteTrigger/AddLink/RemoveLink/LinksFrom/SetLinkAction/SetLinkDelay/TargetCentre/LinkAt`; `SyncDynamic` (never the old `SyncEntities`).
