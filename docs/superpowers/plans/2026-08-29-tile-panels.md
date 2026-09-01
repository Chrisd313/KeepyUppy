# Tile Panels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A trigger can take away a rectangle of floor — the tiles visibly fall, collision ends, and a body caught when they come back is crushed.

**Architecture:** A named rect (`panels[]`) over authored tiles. The loaded tile rows stay immutable; a parallel mask layer inside `TileGrid` hides cells, and every collision query already funnels through that one indexer. Links target a panel by id exactly as they target an entity or a zone. The fall is a view-only effect that the simulation cannot observe.

**Tech Stack:** Unity 6000.2.6f2, C#, NUnit EditMode tests, `JsonUtility` level JSON, IMGUI editor panels.

**Spec:** `docs/superpowers/specs/2026-08-29-tile-panels-design.md`

## Global Constraints

- **Determinism (GDD §6.1):** no `Time.deltaTime`, no `Random`, no wall-clock, no unordered-collection iteration where order affects a result — anywhere under `Assets/_Project/Scripts/Sim/`. New simulation state joins the replay-equality test in the same commit. **Views are exempt and may use render time**; nothing a view computes may be read back by the sim.
- **Linked fate (GDD §4.1, LOCKED):** `SimWorld.Kill` is the only place a death flag is set. Nothing else in the codebase may set one.
- **Restart under 200 ms (GDD §8):** synchronous reset; every counter, buffer and cooldown clears. `DeathCount` deliberately survives.
- **The loaded tile rows are immutable during a run.** A panel masks cells; it never writes `_cells`.
- **The editor and the shipping game read the same JSON.** Never a second format.
- **No `using UnityEditor;`** anywhere under `Assets/_Project/Scripts/LevelEditor/`.
- **No `.unity`, `.prefab` or `.asset` files** created or edited.
- **Reserved colours (GDD §13.1):** hazard red and balloon yellow are worn by nothing else.
- **Tests:** `Assets/_Project/Tests/EditMode/`, namespace `KeepyUppy.SimTests`, NUnit. Run with `./test.sh` from the repo root in Git Bash; exit 0 means all passed and it prints `total= passed= failed=`. A single fixture: `./test.sh -testFilter KeepyUppy.SimTests.PanelFieldTests`. **The Unity editor must be CLOSED** — batch mode takes an exclusive lock on `Library/`. If a run hangs beyond ~4 minutes, stop and report it as a blocker; never retry in a loop and never delete `Library/`.
- **Baseline at the start of this plan: `total=387 passed=387 failed=0`.**
- New files need `.meta` files; if you write one by hand, make its `guid` unique across `Assets/`.

---

## File Structure

| File | Responsibility |
|---|---|
| `Scripts/Sim/Level/LevelData.cs` | `LevelPanel` + `panels[]` on `LevelData` |
| `Scripts/Sim/TileGrid.cs` | The mask layer: `SetMasked`, `ClearMask`, `IsMasked`, masked reads in the indexer |
| `Scripts/Sim/Panels/SimPanel.cs` | **New.** One panel as the sim runs it |
| `Scripts/Sim/Panels/PanelField.cs` | **New.** Every panel, the mask rebuild, and the turned-on edge record |
| `Scripts/Sim/Level/LevelLoader.cs` | `LoadPanels`, called from `ApplyEntities` |
| `Scripts/Sim/Triggers/TriggerField.cs` | Routes a link to a panel after entity and zone |
| `Scripts/Sim/SimWorld.cs` | Holds `Panels`; re-applies the mask; the crush check; the balloon's bounds check; `Restart` |
| `Scripts/Sim/DeathCause.cs` | `Crushed = 5`, `BalloonLeftRoom = 6` |
| `Scripts/Sim/Level/LevelValidator.cs` | Three panel lints; panel ids registered |
| `Scripts/LevelEditor/EditorTool.cs` | `Panel = 9` |
| `Scripts/LevelEditor/EditorSession.cs` | Panel CRUD, selection exclusivity, link targeting |
| `Scripts/LevelEditor/PanelInspector.cs` | **New.** The IMGUI block for the selected panel |
| `Scripts/LevelEditor/LevelEditorController.cs` | Binding, palette button, tool case, inspector branch, overlay |
| `Scripts/Runtime/PanelViewSync.cs` | **New.** Falling debris on the on→off edge |
| `Scripts/Runtime/LevelSession.cs` | Builds the new view |
| `Tests/EditMode/` | `PanelFieldTests`, `PanelCrushTests`, `BalloonBoundsTests`, `EditorPanelTests`, plus extensions |

---

## Task 1: Schema — `panels[]`

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelData.cs`
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelValidator.cs` (`ValidateIds` only)
- Test: `Assets/_Project/Tests/EditMode/LevelFormatTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `LevelPanel { string id; float[] rect; string start_state; }` and `LevelData.panels`. Every later task reads these.

- [ ] **Step 1: Write the failing test**

Add to `LevelFormatTests.cs`:

```csharp
        [Test]
        public void Panels_RoundTrip()
        {
            LevelData level = FullLevel();
            level.panels = new[]
            {
                new LevelPanel { id = "p01", rect = new[] { 12f, 4f, 6f, 1f }, start_state = "on" },
                new LevelPanel { id = "p02", rect = new[] { 20f, 9f, 3f, 1f }, start_state = "off" },
            };

            _source.Save(level.id, level);
            Assert.IsTrue(_source.TryLoad(level.id, out LevelData loaded));

            Assert.AreEqual(2, loaded.panels.Length);
            Assert.AreEqual("p01", loaded.panels[0].id);
            Assert.AreEqual(new[] { 12f, 4f, 6f, 1f }, loaded.panels[0].rect);
            Assert.AreEqual("off", loaded.panels[1].start_state);
        }

        [Test]
        public void ALevelWithNoPanels_LoadsAnEmptyArray_NotNull()
        {
            var level = JsonUtility.FromJson<LevelData>("{\"id\":\"w1_01\"}");

            Assert.IsNotNull(level.panels, "a file written before panels existed must still load");
            Assert.AreEqual(0, level.panels.Length);
        }

        [Test]
        public void APanelIdColliding_WithAZoneId_IsWarned()
        {
            LevelData level = FullLevel();
            level.zones = new[] { new LevelZone { id = "x01", type = "air", rect = new[] { 2f, 2f, 2f, 2f } } };
            level.panels = new[] { new LevelPanel { id = "x01", rect = new[] { 8f, 1f, 2f, 1f } } };

            bool warned = false;
            foreach (string w in LevelValidator.Validate(level))
                if (w.Contains("duplicate id 'x01'")) warned = true;

            Assert.IsTrue(warned, "panel ids share one namespace with entities, zones and triggers");
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter KeepyUppy.SimTests.LevelFormatTests`
Expected: FAIL — `LevelPanel` does not exist.

- [ ] **Step 3: Add the schema type**

In `LevelData.cs`, add `public LevelPanel[] panels = new LevelPanel[0];` to `LevelData` beside `zones`, and the type beside `LevelZone`:

```csharp
    /// <summary>
    /// A rectangle of authored tiles a link can take away (GDD 9.3's falling floor).
    ///
    /// The tiles themselves live in <see cref="LevelData.tiles"/> like any others; a panel
    /// only says which of them can be switched. start_state "off" hides them at load, which
    /// is how an *appearing* platform is authored — the same object, inverted.
    /// </summary>
    [Serializable]
    public sealed class LevelPanel
    {
        public string id = "p01";

        /// <summary>[x, y, w, h] in tiles. Snapped to whole tiles, as a zone rect is.</summary>
        public float[] rect = { 0f, 0f, 1f, 1f };

        /// <summary>on | off — whether its tiles are present at the start of the run.</summary>
        public string start_state = "on";
    }
```

- [ ] **Step 4: Register panel ids**

In `LevelValidator.ValidateIds`, beside the trigger line:

```csharp
            if (level.panels != null) foreach (var p in level.panels) Register(p?.id, "panel");
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `./test.sh -testFilter KeepyUppy.SimTests`
Expected: PASS, `failed=0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Level Assets/_Project/Tests/EditMode/LevelFormatTests.cs
git commit -m "Add the panels array to the level schema"
```

---

## Task 2: The mask layer in `TileGrid`

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/TileGrid.cs`
- Test: `Assets/_Project/Tests/EditMode/TileGridTests.cs` (create if absent)

**Interfaces:**
- Consumes: nothing.
- Produces: `TileGrid.SetMasked(RectInt rect, bool masked)`, `TileGrid.ClearMask()`, `TileGrid.IsMasked(int x, int y)`, and masked reads from `this[x, y]`. `PanelField` (Task 3) drives all three.

- [ ] **Step 1: Write the failing test**

Create or extend `Assets/_Project/Tests/EditMode/TileGridTests.cs`:

```csharp
using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The mask layer (tile-panels design). The loaded rows stay immutable; a mask hides
    /// cells, and every collision query in the sim reads them through this one indexer.
    /// </summary>
    public sealed class TileGridMaskTests
    {
        static TileGrid Room()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 4, 6, 1, TileKind.Solid);
            return grid;
        }

        [Test]
        public void AMaskedCell_ReadsEmpty_AndStopsBlocking()
        {
            TileGrid grid = Room();
            Assert.IsTrue(grid.IsSolid(12, 4));
            Assert.IsTrue(grid.IsBlockingBalloon(12, 4));

            grid.SetMasked(new RectInt(10, 4, 6, 1), true);

            Assert.AreEqual(TileKind.Empty, grid[12, 4]);
            Assert.IsFalse(grid.IsSolid(12, 4), "collision must see the floor as gone");
            Assert.IsFalse(grid.IsBlockingBalloon(12, 4));
            Assert.IsFalse(grid.IsBlockingPlayer(12, 4));
        }

        [Test]
        public void ClearMask_BringsTheTilesBack()
        {
            TileGrid grid = Room();
            grid.SetMasked(new RectInt(10, 4, 6, 1), true);
            grid.ClearMask();

            Assert.IsTrue(grid.IsSolid(12, 4), "the rows were never edited, so nothing was lost");
        }

        [Test]
        public void Masking_BumpsVersion_SoTheViewRedraws()
        {
            TileGrid grid = Room();
            int before = grid.Version;

            grid.SetMasked(new RectInt(10, 4, 6, 1), true);
            Assert.Greater(grid.Version, before);

            int after = grid.Version;
            grid.SetMasked(new RectInt(10, 4, 6, 1), true);
            Assert.AreEqual(after, grid.Version, "masking what is already masked changes nothing");
        }

        [Test]
        public void PaintingUnderAMask_EditsTheCell_WithoutRevealingIt()
        {
            TileGrid grid = Room();
            grid.SetMasked(new RectInt(10, 4, 6, 1), true);

            grid[12, 4] = TileKind.SpikeUp;

            Assert.AreEqual(TileKind.Empty, grid[12, 4], "still hidden");
            Assert.IsTrue(grid.IsMasked(12, 4));

            grid.ClearMask();
            Assert.AreEqual(TileKind.SpikeUp, grid[12, 4], "the edit landed on the row underneath");
        }

        [Test]
        public void OutOfBounds_StillReadsSolid_EvenWithAMaskSet()
        {
            TileGrid grid = Room();
            grid.SetMasked(new RectInt(0, 0, TileGrid.Width, TileGrid.Height), true);

            Assert.AreEqual(TileKind.Solid, grid[-1, 5], "a runaway body still cannot leave the world");
            Assert.AreEqual(TileKind.Solid, grid[TileGrid.Width, 5]);
        }

        [Test]
        public void LoadRows_ClearsAnyMask()
        {
            TileGrid grid = Room();
            grid.SetMasked(new RectInt(10, 4, 6, 1), true);

            grid.LoadRows(Room().ToRows());

            Assert.IsFalse(grid.IsMasked(12, 4), "a fresh level starts unmasked");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./test.sh -testFilter KeepyUppy.SimTests.TileGridMaskTests`
Expected: FAIL — `SetMasked` does not exist.

- [ ] **Step 3: Implement the mask**

In `TileGrid.cs`, beside `_cells`:

```csharp
        /// <summary>
        /// Cells currently hidden by a panel (tile-panels design). Parallel to _cells, which
        /// is never written by a panel: the level's geometry stays exactly as it loaded, so
        /// the worst a panel bug can do is show the wrong tiles for one run.
        /// </summary>
        readonly bool[] _masked = new bool[Width * Height];
```

Replace the indexer's getter, leaving the setter alone:

```csharp
        public TileKind this[int x, int y]
        {
            get
            {
                if (!InBounds(x, y)) return TileKind.Solid;

                int i = y * Width + x;
                return _masked[i] ? TileKind.Empty : _cells[i];
            }
            set
            {
                if (!InBounds(x, y)) return;

                int i = y * Width + x;
                if (_cells[i] == value) return;

                _cells[i] = value;
                Version++;
            }
        }
```

Then the three methods, after `Fill`:

```csharp
        public bool IsMasked(int x, int y) => InBounds(x, y) && _masked[y * Width + x];

        /// <summary>
        /// Hides or reveals a rectangle of cells. Version moves only when something actually
        /// changed, so a panel re-applying its own state does not churn the tile views.
        /// </summary>
        public void SetMasked(RectInt rect, bool masked)
        {
            bool changed = false;

            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                if (!InBounds(x, y)) continue;

                int i = y * Width + x;
                if (_masked[i] == masked) continue;

                _masked[i] = masked;
                changed = true;
            }

            if (changed) Version++;
        }

        public void ClearMask()
        {
            bool changed = false;

            for (int i = 0; i < _masked.Length; i++)
            {
                if (!_masked[i]) continue;

                _masked[i] = false;
                changed = true;
            }

            if (changed) Version++;
        }
```

Add `ClearMask();` as the first line of `LoadRows` and of `Clear`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./test.sh`
Expected: PASS, `failed=0`. Run the whole suite — the indexer is read by every collision test in the project.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim/TileGrid.cs Assets/_Project/Tests/EditMode/TileGridTests.cs
git commit -m "Add a mask layer to TileGrid"
```

---

## Task 3: `PanelField`

**Files:**
- Create: `Assets/_Project/Scripts/Sim/Panels/SimPanel.cs`
- Create: `Assets/_Project/Scripts/Sim/Panels/PanelField.cs`
- Test: `Assets/_Project/Tests/EditMode/PanelFieldTests.cs`

**Interfaces:**
- Consumes: `TileGrid.SetMasked`, `ClearMask` (Task 2).
- Produces: namespace `KeepyUppy.Sim.Panels`; `SimPanel { string Id; RectInt Rect; bool Enabled; bool StartEnabled; }`; `PanelField` with `Count`, `this[int]`, `Clear()`, `Add(SimPanel)`, `Find(string)`, `IndexOf(string)`, `SetEnabled(string, bool)`, `Dirty`, `Apply(TileGrid)`, `BeginStep()`, `TurnedOnThisStep` (a `List<int>`), `Reset()`.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/PanelFieldTests.cs`:

```csharp
using KeepyUppy.Sim;
using KeepyUppy.Sim.Panels;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Panels: the geometry a link can take away (tile-panels design). The mask is rebuilt
    /// from the flags rather than toggled cell by cell, which is what makes overlapping
    /// panels compose without a special case.
    /// </summary>
    public sealed class PanelFieldTests
    {
        static TileGrid Room()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 4, 6, 1, TileKind.Solid);
            return grid;
        }

        static SimPanel Panel(string id, RectInt rect, bool enabled = true)
            => new SimPanel { Id = id, Rect = rect, Enabled = enabled, StartEnabled = enabled };

        [Test]
        public void ADisabledPanel_HidesItsTiles_WhenApplied()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1), enabled: false));

            panels.Apply(grid);

            Assert.AreEqual(TileKind.Empty, grid[12, 4]);
        }

        [Test]
        public void SetEnabled_TogglesTheTiles_AfterTheNextApply()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1)));
            panels.Apply(grid);

            panels.SetEnabled("p01", false);
            Assert.IsTrue(panels.Dirty, "the mask needs rebuilding");
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Empty, grid[12, 4]);
            Assert.IsFalse(panels.Dirty, "applying clears it");

            panels.SetEnabled("p01", true);
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Solid, grid[12, 4]);
        }

        [Test]
        public void OverlappingPanels_HideACellIfEitherIsOff()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1)));
            panels.Add(Panel("p02", new RectInt(12, 4, 2, 1)));

            panels.SetEnabled("p02", false);
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Empty, grid[12, 4], "hidden by p02");
            Assert.AreEqual(TileKind.Solid, grid[10, 4], "outside p02, and p01 is on");

            panels.SetEnabled("p02", true);
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Solid, grid[12, 4], "visible only when both are on");
        }

        [Test]
        public void TurnedOnThisStep_RecordsOnlyTheOffToOnEdge()
        {
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1), enabled: false));

            panels.BeginStep();
            panels.SetEnabled("p01", true);
            Assert.AreEqual(1, panels.TurnedOnThisStep.Count);

            panels.BeginStep();
            panels.SetEnabled("p01", true);
            Assert.AreEqual(0, panels.TurnedOnThisStep.Count, "already on is not an edge");

            panels.BeginStep();
            panels.SetEnabled("p01", false);
            Assert.AreEqual(0, panels.TurnedOnThisStep.Count, "turning off is not a crush");
        }

        [Test]
        public void Reset_RestoresEveryStartState()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1)));

            panels.BeginStep();
            panels.SetEnabled("p01", false);
            panels.Apply(grid);
            Assert.AreEqual(TileKind.Empty, grid[12, 4]);

            panels.Reset();
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Solid, grid[12, 4], "the retry gets the room it started with");
            Assert.AreEqual(0, panels.TurnedOnThisStep.Count);
        }

        [Test]
        public void AnUnknownId_ChangesNothing()
        {
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1)));

            panels.SetEnabled("nope", false);

            Assert.IsTrue(panels[0].Enabled);
            Assert.IsFalse(panels.Dirty);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./test.sh -testFilter KeepyUppy.SimTests.PanelFieldTests`
Expected: FAIL — namespace `KeepyUppy.Sim.Panels` does not exist.

- [ ] **Step 3: Write `SimPanel`**

Create `Assets/_Project/Scripts/Sim/Panels/SimPanel.cs`:

```csharp
using UnityEngine;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>
    /// A rectangle of authored tiles a link can take away (GDD 9.3's falling floor).
    ///
    /// The tiles belong to the level; a panel only decides whether they are currently there.
    /// Config is copied out of the level data at load, so nothing here reads JSON.
    /// </summary>
    public sealed class SimPanel
    {
        public string Id;

        /// <summary>Tile coordinates. A panel is always whole tiles — the mask indexes cells.</summary>
        public RectInt Rect;

        // --- Run state: cleared by Reset(), covered by the replay-equality test ---

        /// <summary>Whether its tiles are currently present.</summary>
        public bool Enabled = true;

        /// <summary>What Enabled returns to on a restart.</summary>
        public bool StartEnabled = true;
    }
}
```

- [ ] **Step 4: Write `PanelField`**

Create `Assets/_Project/Scripts/Sim/Panels/PanelField.cs`:

```csharp
using System.Collections.Generic;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>
    /// Every panel in the room, and the tile mask they add up to.
    ///
    /// A List, never a Dictionary: iteration order decides which panel wins a tie when two
    /// switch on the same step, and unordered iteration is on CLAUDE.md's list of the things
    /// that break GDD 6.1 silently.
    ///
    /// The mask is REBUILT from the flags rather than toggled cell by cell. That is what makes
    /// overlapping panels compose for free — a cell is hidden if any panel covering it is off —
    /// and it is why a restart costs one pass over a handful of rects rather than per-cell
    /// bookkeeping (GDD 8's 200 ms budget).
    /// </summary>
    public sealed class PanelField
    {
        readonly List<SimPanel> _panels = new List<SimPanel>();

        /// <summary>Indices of panels whose tiles came back this step. SimWorld reads it to crush.</summary>
        public readonly List<int> TurnedOnThisStep = new List<int>();

        /// <summary>Whether a flag has changed since the mask was last built.</summary>
        public bool Dirty { get; private set; }

        public int Count => _panels.Count;
        public SimPanel this[int i] => _panels[i];

        public void Clear()
        {
            _panels.Clear();
            TurnedOnThisStep.Clear();
            Dirty = true;
        }

        public void Add(SimPanel panel)
        {
            if (panel == null) return;

            _panels.Add(panel);
            Dirty = true;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _panels.Count; i++)
                if (_panels[i].Id == id) return i;

            return -1;
        }

        public SimPanel Find(string id)
        {
            int index = IndexOf(id);
            return index < 0 ? null : _panels[index];
        }

        /// <summary>Clears the per-step record. Called once per tick before triggers run.</summary>
        public void BeginStep() => TurnedOnThisStep.Clear();

        /// <summary>
        /// Switches a panel. Only an off-to-on edge is recorded, because that is the one that
        /// can catch a body inside returning geometry; tiles leaving never hurt anyone.
        /// </summary>
        public void SetEnabled(string id, bool enabled)
        {
            int index = IndexOf(id);
            if (index < 0) return;

            SimPanel panel = _panels[index];
            if (panel.Enabled == enabled) return;

            if (enabled) TurnedOnThisStep.Add(index);

            panel.Enabled = enabled;
            Dirty = true;
        }

        /// <summary>GDD 8: part of the synchronous restart.</summary>
        public void Reset()
        {
            for (int i = 0; i < _panels.Count; i++) _panels[i].Enabled = _panels[i].StartEnabled;

            TurnedOnThisStep.Clear();
            Dirty = true;
        }

        /// <summary>Rebuilds the grid's mask from the current flags.</summary>
        public void Apply(TileGrid grid)
        {
            if (grid == null) return;

            grid.ClearMask();

            for (int i = 0; i < _panels.Count; i++)
            {
                SimPanel panel = _panels[i];
                if (!panel.Enabled) grid.SetMasked(panel.Rect, true);
            }

            Dirty = false;
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `./test.sh -testFilter KeepyUppy.SimTests.PanelFieldTests`
Expected: PASS, `failed=0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Panels Assets/_Project/Tests/EditMode/PanelFieldTests.cs
git commit -m "Add PanelField and the tile mask it builds"
```

---

## Task 4: Loading panels

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelLoader.cs`
- Modify: `Assets/_Project/Scripts/Sim/SimWorld.cs` (the field only)
- Test: `Assets/_Project/Tests/EditMode/PanelFieldTests.cs`

**Interfaces:**
- Consumes: `LevelPanel` (Task 1), `PanelField` (Task 3).
- Produces: `SimWorld.Panels`; `LevelLoader.LoadPanels(LevelData, PanelField)` called from `ApplyEntities`, which also calls `world.Panels.Apply(world.Grid)`.

- [ ] **Step 1: Write the failing test**

Add to `PanelFieldTests.cs` (add `using KeepyUppy.Sim.Level;`):

```csharp
        static LevelData LevelWithPanel(string startState)
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 4, 6, 1, TileKind.Solid);

            return new LevelData
            {
                tiles = grid.ToRows(),
                panels = new[]
                {
                    new LevelPanel { id = "p01", rect = new[] { 10f, 4f, 6f, 1f }, start_state = startState },
                },
            };
        }

        [Test]
        public void APanelStartingOff_HidesItsTilesAtLoad()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithPanel("off"), world, out _, out _);

            Assert.AreEqual(1, world.Panels.Count);
            Assert.IsFalse(world.Panels[0].Enabled);
            Assert.IsFalse(world.Panels[0].StartEnabled);
            Assert.AreEqual(TileKind.Empty, world.Grid[12, 4], "an appearing platform starts hidden");
        }

        [Test]
        public void APanelStartingOn_LeavesItsTilesAlone()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithPanel("on"), world, out _, out _);

            Assert.IsTrue(world.Panels[0].Enabled);
            Assert.AreEqual(TileKind.Solid, world.Grid[12, 4]);
        }

        [Test]
        public void ReloadingALevel_DoesNotAccumulatePanels()
        {
            var world = new SimWorld();
            LevelData level = LevelWithPanel("on");

            LevelLoader.Apply(level, world, out _, out _);
            LevelLoader.ApplyEntities(level, world);
            LevelLoader.ApplyEntities(level, world);

            Assert.AreEqual(1, world.Panels.Count, "the editor reloads on every edit");
        }

        [Test]
        public void APanelRect_IsRoundedToWholeTiles()
        {
            var world = new SimWorld();
            LevelData level = LevelWithPanel("on");
            level.panels[0].rect = new[] { 9.6f, 4.4f, 6.2f, 1.1f };

            LevelLoader.Apply(level, world, out _, out _);

            Assert.AreEqual(new RectInt(10, 4, 6, 1), world.Panels[0].Rect);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter KeepyUppy.SimTests.PanelFieldTests`
Expected: FAIL — `SimWorld` has no `Panels`.

- [ ] **Step 3: Add the field to `SimWorld`**

In `SimWorld.cs`, beside `Triggers`, and add `using KeepyUppy.Sim.Panels;`:

```csharp
        /// <summary>Geometry a link can take away (GDD 9.3). Masks tiles; see PanelField.</summary>
        public readonly PanelField Panels = new PanelField();
```

- [ ] **Step 4: Load them**

In `LevelLoader.cs`, add `using KeepyUppy.Sim.Panels;`, then in `ApplyEntities` — **after** `LoadZones` and `LoadEntities`, **before** `LoadTriggers`:

```csharp
            LoadPanels(level, world.Panels);
            world.Panels.Apply(world.Grid);
```

and the method beside `LoadZones`:

```csharp
        /// <summary>
        /// Panels, in file order. A panel's rect is rounded to whole tiles: the mask indexes
        /// cells, so half a tile is not a thing that can be hidden.
        /// </summary>
        static void LoadPanels(LevelData level, PanelField panels)
        {
            panels.Clear();
            if (level.panels == null) return;

            foreach (LevelPanel data in level.panels)
            {
                if (data == null) continue;

                float[] r = data.rect;
                if (r == null || r.Length < 4) continue;

                bool on = data.start_state != "off";

                panels.Add(new SimPanel
                {
                    Id = data.id,
                    Rect = new RectInt(Mathf.RoundToInt(r[0]), Mathf.RoundToInt(r[1]),
                                       Mathf.RoundToInt(r[2]), Mathf.RoundToInt(r[3])),
                    Enabled = on,
                    StartEnabled = on,
                });
            }
        }
```

`Apply` is called from `ApplyEntities` rather than from `Apply` so the editor's live push refreshes the mask too — `ApplyEntities` is the path `SyncDynamic` takes.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode/PanelFieldTests.cs
git commit -m "Load panels and apply their mask"
```

---

## Task 5: Links switch panels, and returning tiles crush

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Triggers/TriggerField.cs`
- Modify: `Assets/_Project/Scripts/Sim/SimWorld.cs`
- Modify: `Assets/_Project/Scripts/Sim/DeathCause.cs`
- Modify: `Assets/_Project/Tests/EditMode/SimTestHelpers.cs`
- Test: `Assets/_Project/Tests/EditMode/PanelCrushTests.cs`

**Interfaces:**
- Consumes: `PanelField` (Task 3), `SimWorld.Panels` (Task 4).
- Produces: `DeathCause.Crushed = 5`; `TriggerField.Step(int, Vector2, bool, Vector2, EntityField, AirField, PanelField)` — the signature gains a trailing `panels` parameter; `SimSnapshot` folds panel flags.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/PanelCrushTests.cs`:

```csharp
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// A panel whose tiles come back around a body kills it (tile-panels design). The kill
    /// goes through SimWorld.Kill, because GDD 4.1 is LOCKED: nothing else sets a death flag.
    /// </summary>
    public sealed class PanelCrushTests
    {
        /// <summary>A floor with a switchable slab, the player standing on top of it.</summary>
        static SimWorld Room(float playerX, string startState)
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 1, 6, 1, TileKind.Solid);

            var level = new LevelData
            {
                tiles = grid.ToRows(),
                panels = new[]
                {
                    new LevelPanel { id = "p01", rect = new[] { 10f, 1f, 6f, 2f }, start_state = startState },
                },
            };

            var world = new SimWorld { EnableFlutter = false, FloorKillsBalloon = false };
            LevelLoader.Apply(level, world, out _, out _);
            world.Configure(new Vector2(4f, 12f), Vector2.zero, new Vector2(playerX, 2.8f), null, 1);
            return world;
        }

        [Test]
        public void APanelReturning_UnderThePlayer_KillsBoth()
        {
            SimWorld world = Room(playerX: 12f, startState: "off");
            world.Tick(30);   // the player falls into the space the panel would occupy

            Assert.IsFalse(world.IsDead, "nothing has switched yet");

            world.Panels.BeginStep();
            world.Panels.SetEnabled("p01", true);
            world.Panels.Apply(world.Grid);
            world.CrushBodiesInReturningPanels();

            Assert.IsTrue(world.IsDead);
            Assert.AreEqual(DeathCause.Crushed, world.LastDeath);
        }

        [Test]
        public void APanelReturning_WithTheRoomClear_KillsNobody()
        {
            SimWorld world = Room(playerX: 4f, startState: "off");
            world.Tick(30);

            world.Panels.BeginStep();
            world.Panels.SetEnabled("p01", true);
            world.Panels.Apply(world.Grid);
            world.CrushBodiesInReturningPanels();

            Assert.IsFalse(world.IsDead, "the player is nowhere near it");
        }

        [Test]
        public void APanelReturning_OverEmptyCells_KillsNobody()
        {
            // The panel's rect is two tiles tall but only its bottom row holds tiles, so a
            // player standing in the empty upper row is not inside anything solid.
            SimWorld world = Room(playerX: 12f, startState: "on");

            world.Panels.BeginStep();
            world.Panels.SetEnabled("p01", false);
            world.Panels.Apply(world.Grid);
            world.Panels.BeginStep();
            world.Panels.SetEnabled("p01", true);
            world.Panels.Apply(world.Grid);
            world.CrushBodiesInReturningPanels();

            Assert.IsFalse(world.IsDead, "only a cell that actually holds a tile can crush");
        }

        [Test]
        public void ATriggerLinkedToAPanel_TakesTheFloorAway()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 1, 6, 1, TileKind.Solid);

            var level = new LevelData
            {
                tiles = grid.ToRows(),
                panels = new[]
                {
                    new LevelPanel { id = "p01", rect = new[] { 10f, 1f, 6f, 1f }, start_state = "on" },
                },
                triggers = new[]
                {
                    new LevelTrigger { id = "t01", type = "balloon_region", rect = new[] { 0f, 0f, 32f, 18f }, once = true },
                },
                links = new[]
                {
                    new LevelLink { from = "t01", to = new[] { "p01" }, action = "off", delay = 0f },
                },
            };

            var world = new SimWorld { EnableFlutter = false, FloorKillsBalloon = false };
            LevelLoader.Apply(level, world, out _, out _);
            world.Configure(new Vector2(12f, 8f), Vector2.zero, new Vector2(4f, 1.8f), null, 1);

            Assert.AreEqual(TileKind.Solid, world.Grid[12, 1]);

            world.Tick(3);   // fire, apply on the next step, mask rebuilt

            Assert.AreEqual(TileKind.Empty, world.Grid[12, 1], "the link took the floor away");
        }

        [Test]
        public void Restart_PutsTheFloorBack()
        {
            SimWorld world = Room(playerX: 4f, startState: "on");

            world.Panels.BeginStep();
            world.Panels.SetEnabled("p01", false);
            world.Panels.Apply(world.Grid);
            Assert.AreEqual(TileKind.Empty, world.Grid[12, 1]);

            world.Restart();

            Assert.AreEqual(TileKind.Solid, world.Grid[12, 1], "the retry is the room they died in");
            Assert.IsTrue(world.Panels[0].Enabled);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./test.sh -testFilter KeepyUppy.SimTests.PanelCrushTests`
Expected: FAIL — `DeathCause.Crushed` and `CrushBodiesInReturningPanels` do not exist.

- [ ] **Step 3: Add the death cause**

In `DeathCause.cs`, after `PlayerFellOut = 4`:

```csharp
        /// <summary>A panel's tiles came back around a body (tile-panels design).</summary>
        Crushed = 5,
```

- [ ] **Step 4: Route links to panels**

In `TriggerField.cs`, add `using KeepyUppy.Sim.Panels;`. Change the two signatures and the one call:

```csharp
        public void Step(int step, Vector2 playerPos, bool hasPlayer, Vector2 balloonPos,
                         EntityField entities, AirField air, PanelField panels)
```

```csharp
                Apply(_links[item.link], entities, air, panels, step);
```

```csharp
        void Drain(int step, EntityField entities, AirField air, PanelField panels)
```
(and its call site inside `Step`: `Drain(step, entities, air, panels);`)

```csharp
        static void Apply(SimLink link, EntityField entities, AirField air, PanelField panels, int step)
```

Then, in `Apply`, after the air-zone loop and before the end of the `foreach`, add the panel branch:

```csharp
                // Panels last: a panel is geometry, and an id naming one is not naming an
                // entity or a zone. On shows its tiles, Off takes them away.
                if (panels != null && panels.IndexOf(id) >= 0)
                    panels.SetEnabled(id, Next(link.Action, panels.Find(id).Enabled));
```

- [ ] **Step 5: Wire it into `SimWorld`**

In `Tick()`, replace the `Triggers.Step(...)` line with:

```csharp
            Panels.BeginStep();
            Triggers.Step(step, Player.Position, HasPlayer, Balloon.Position, Entities, Air, Panels);

            if (Panels.Dirty) Panels.Apply(Grid);
            CrushBodiesInReturningPanels();
```

Add the check itself, beside `CheckExit`:

```csharp
        /// <summary>
        /// A body caught inside tiles that just came back dies (tile-panels design).
        ///
        /// Only the panels that switched this step are tested, and only their cells that
        /// actually hold a tile — a panel whose rect covers empty space crushes nothing. The
        /// GDD 8 fairness shrink is deliberately NOT applied: the hazard is the tile arriving,
        /// not a hitbox the player was asked to read, and an inset would let a body sit
        /// visibly inside solid geometry and live.
        ///
        /// Internal rather than private so the crush tests can drive it without a trigger.
        /// </summary>
        internal void CrushBodiesInReturningPanels()
        {
            if (IsDead) return;

            for (int i = 0; i < Panels.TurnedOnThisStep.Count; i++)
            {
                RectInt rect = Panels[Panels.TurnedOnThisStep[i]].Rect;

                for (int y = rect.yMin; y < rect.yMax; y++)
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    if (!TileGrid.InBounds(x, y) || Grid[x, y] == TileKind.Empty) continue;

                    if (HasPlayer && BoxOverlapsTile(Player.Position, Player.HalfWidth, Player.HalfHeight, x, y))
                    {
                        Kill(DeathCause.Crushed);
                        return;
                    }

                    if (CircleOverlapsTile(Balloon.Position, Balloon.Radius, x, y))
                    {
                        Kill(DeathCause.Crushed);
                        return;
                    }
                }
            }
        }

        static bool BoxOverlapsTile(Vector2 centre, float halfWidth, float halfHeight, int x, int y)
            => centre.x + halfWidth > x && centre.x - halfWidth < x + 1 &&
               centre.y + halfHeight > y && centre.y - halfHeight < y + 1;

        static bool CircleOverlapsTile(Vector2 centre, float radius, int x, int y)
        {
            float dx = centre.x - Mathf.Clamp(centre.x, x, x + 1);
            float dy = centre.y - Mathf.Clamp(centre.y, y, y + 1);
            return dx * dx + dy * dy < radius * radius;
        }
```

In `Restart()`, beside the other resets and **before** `Entities.Step(0, Air)`:

```csharp
            Panels.Reset();
            Panels.Apply(Grid);
```

Add `[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("KeepyUppy.Sim.Tests")]` to `SimWorld.cs`'s top **only if** the test assembly cannot see `internal`; if the compiler objects, make `CrushBodiesInReturningPanels` `public` instead and note the choice in your report.

- [ ] **Step 6: Fold the flags into the snapshot**

In `SimTestHelpers.cs`, add to `FoldActive` (which already folds entity and zone state), after the air loop:

```csharp
                for (int i = 0; i < panels.Count; i++)
                    hash = (hash ^ (panels[i].Enabled ? 1 : 0)) * 16777619;
```

Change its signature to `static int FoldActive(EntityField entities, AirField air, PanelField panels)` and its call to `FoldActive(w.Entities, w.Air, w.Panels)`. Add `using KeepyUppy.Sim.Panels;`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `./test.sh`
Expected: PASS, `failed=0`. The whole suite — this changes `Tick` and `Restart`.

- [ ] **Step 8: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode
git commit -m "Switch panels from links, and crush what returning tiles catch"
```

---

## Task 6: The balloon dies when it leaves the room

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/DeathCause.cs`
- Modify: `Assets/_Project/Scripts/Sim/SimWorld.cs` (`StepBalloon`)
- Test: `Assets/_Project/Tests/EditMode/BalloonBoundsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `DeathCause.BalloonLeftRoom = 6`.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/BalloonBoundsTests.cs`:

```csharp
using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The balloon's out-of-room death.
    ///
    /// Before panels, the room was always walled and the balloon always died to a floor. With
    /// the floor switchable, a balloon that falls out of an open room would otherwise fall for
    /// ever and the level would never end.
    /// </summary>
    public sealed class BalloonBoundsTests
    {
        /// <summary>A room with no geometry at all, so nothing but the bounds can end it.</summary>
        static SimWorld OpenRoom(Vector2 balloonPos, Vector2 velocity)
        {
            var world = new SimWorld { EnableFlutter = false, FloorKillsBalloon = true };
            world.Grid.Clear();
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Configure(balloonPos, velocity, Vector2.zero, null, 1);
            return world;
        }

        [Test]
        public void ABalloonFallingOutTheBottom_Dies()
        {
            SimWorld world = OpenRoom(new Vector2(16f, 1f), new Vector2(0f, -6f));

            world.Tick(120);

            Assert.IsTrue(world.IsDead, "otherwise it falls for ever and the level never ends");
            Assert.AreEqual(DeathCause.BalloonLeftRoom, world.LastDeath);
        }

        [Test]
        public void ABalloonEscapingUpwards_Dies()
        {
            SimWorld world = OpenRoom(new Vector2(16f, 17f), new Vector2(0f, 12f));

            world.Tick(120);

            Assert.IsTrue(world.IsDead, "a balloon that gets away is the premise of losing");
            Assert.AreEqual(DeathCause.BalloonLeftRoom, world.LastDeath);
        }

        [Test]
        public void ABalloonJustOutsideAWall_Survives()
        {
            // One metre past the edge, inside the 2 m margin: a bump can carry a body a little
            // past a wall and back, and killing at the boundary would make walls feel lethal.
            SimWorld world = OpenRoom(new Vector2(-1f, 9f), Vector2.zero);

            world.Tick(1);

            Assert.IsFalse(world.IsDead);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./test.sh -testFilter KeepyUppy.SimTests.BalloonBoundsTests`
Expected: FAIL — `DeathCause.BalloonLeftRoom` does not exist.

- [ ] **Step 3: Add the cause and the check**

In `DeathCause.cs`, after `Crushed = 5`:

```csharp
        /// <summary>
        /// The balloon left the room. Reachable once a panel can take away a wall or a floor;
        /// escaping upward counts, and reads as the balloon getting away.
        /// </summary>
        BalloonLeftRoom = 6,
```

In `SimWorld.StepBalloon`, as the last check in the method (after the `FloorKillsBalloon` line):

```csharp
            // The same 2 m margin the player gets. Outside the room rather than at its edge:
            // a bump can carry a body a little past a wall and back, and killing at the
            // boundary would make the walls feel like hazards.
            if (Balloon.Position.y < -2f || Balloon.Position.y > TileGrid.Height + 2f ||
                Balloon.Position.x < -2f || Balloon.Position.x > TileGrid.Width + 2f)
            {
                Kill(DeathCause.BalloonLeftRoom);
            }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode/BalloonBoundsTests.cs
git commit -m "Kill the balloon when it leaves the room"
```

---

## Task 7: Determinism coverage

**Files:**
- Modify: `Assets/_Project/Tests/EditMode/SimTestHelpers.cs`
- Modify: `Assets/_Project/Tests/EditMode/DeterminismTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–6.
- Produces: `SimTestHelpers.BuildFallingFloorRoom(int seed)`.

- [ ] **Step 1: Write the failing test**

Add to `SimTestHelpers.cs` beside `BuildTrapRoom`:

```csharp
        /// <summary>
        /// A room whose floor is taken away by a once:true region the scripted player walks
        /// into. Panels add run state — the enabled flags and the mask built from them — and
        /// state the replay test cannot see is state that rots silently (CLAUDE.md).
        /// </summary>
        public static SimWorld BuildFallingFloorRoom(int seed)
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(8, 1, 12, 1, TileKind.Solid);

            var level = new LevelData
            {
                tiles = grid.ToRows(),
                panels = new[]
                {
                    new LevelPanel { id = "p01", rect = new[] { 12f, 1f, 6f, 1f }, start_state = "on" },
                },
                triggers = new[]
                {
                    new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 6f, 1f, 6f, 4f }, once = true },
                },
                links = new[]
                {
                    new LevelLink { from = "t01", to = new[] { "p01" }, action = "off", delay = 0.25f },
                },
            };

            var world = new SimWorld { EnableFlutter = true, FloorKillsBalloon = false };
            LevelLoader.Apply(level, world, out Vector2 balloonSpawn, out Vector2 playerSpawn);
            world.Configure(balloonSpawn, new Vector2(1.5f, 0f), playerSpawn, BusyScript(), seed);
            return world;
        }
```

`LevelData`'s spawns default to `player [4, 2]` and `balloon [6, 13]`, both inside this room, so no spawn block is needed. Add `using KeepyUppy.Sim.Level;` if the file lacks it.

Add to `DeterminismTests.cs`:

```csharp
        [Test]
        public void FallingFloorRoom_SameSeed_BitIdenticalEveryStep()
        {
            SimWorld a = SimTestHelpers.BuildFallingFloorRoom(Seed);
            SimWorld b = SimTestHelpers.BuildFallingFloorRoom(Seed);

            for (int i = 0; i < Steps; i++)
            {
                a.Tick();
                b.Tick();

                SimSnapshot.Capture(a).AssertMatches(SimSnapshot.Capture(b), $"falling floor step {i}");
            }
        }

        [Test]
        public void FallingFloorRoom_ResetAndReplayed_BitIdentical()
        {
            SimWorld world = SimTestHelpers.BuildFallingFloorRoom(Seed);

            world.Tick(Steps);
            SimSnapshot first = SimSnapshot.Capture(world);

            world.Restart();
            world.Tick(Steps);

            first.AssertMatches(SimSnapshot.Capture(world), "falling floor after replay");
        }

        [Test]
        public void FallingFloorRoom_ActuallyTakesTheFloorAway()
        {
            // Guards the tests above from passing for the wrong reason: a room where the
            // trigger never fires would replay identically while proving nothing.
            SimWorld world = SimTestHelpers.BuildFallingFloorRoom(Seed);

            bool everMasked = false;
            for (int i = 0; i < Steps && !everMasked; i++)
            {
                world.Tick();
                if (world.Grid[14, 1] == TileKind.Empty) everMasked = true;
            }

            Assert.IsTrue(everMasked, "the scripted run should walk into the trigger");
        }
```

- [ ] **Step 2: Run the tests**

Run: `./test.sh -testFilter KeepyUppy.SimTests.DeterminismTests`
Expected: the two replay tests PASS immediately (the fold from Task 5 already covers panels). `FallingFloorRoom_ActuallyTakesTheFloorAway` may FAIL if the scripted player never enters the trigger rect.

- [ ] **Step 3: If the guard test fails, move the trigger, not the assertion**

Widen or reposition the trigger rect in `BuildFallingFloorRoom` until the scripted run genuinely fires it — `BusyScript()` moves right, jumps, moves left, and loops. Do **not** weaken the assertion to make it pass; a determinism room that never fires its trigger proves nothing. Record what you changed and why in your report.

- [ ] **Step 4: Run the whole suite**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Tests/EditMode
git commit -m "Cover panels in the replay-equality test"
```

---

## Task 8: Validation

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelValidator.cs`
- Test: `Assets/_Project/Tests/EditMode/LevelValidatorTests.cs`

**Interfaces:**
- Consumes: `LevelPanel` (Task 1).
- Produces: no new API — `Validate` returns more strings.

- [ ] **Step 1: Write the failing test**

Add to `LevelValidatorTests.cs` (the file already has `static LevelData Valid()`, `static void AssertWarns(LevelData, string)`, and a `Mentions(List<string>, string)` helper from the trigger work):

```csharp
        [Test]
        public void APanelNothingLinksFrom_IsWarned()
        {
            LevelData level = Valid();
            level.panels = new[] { new LevelPanel { id = "p01", rect = new[] { 4f, 1f, 4f, 1f } } };

            AssertWarns(level, "nothing links to panel 'p01'");
        }

        [Test]
        public void APanelWithAZeroAreaRect_IsWarned()
        {
            LevelData level = Valid();
            level.panels = new[] { new LevelPanel { id = "p01", rect = new[] { 4f, 1f, 0f, 1f } } };
            level.links = new[] { new LevelLink { from = "t01", to = new[] { "p01" }, action = "off" } };
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 2f, 1f, 2f, 2f } },
            };

            AssertWarns(level, "zero-area");
        }

        [Test]
        public void APanelCoveringNoTiles_IsWarned()
        {
            // The lint that earns its keep: a rect placed a few tiles off the floor it meant
            // to take away masks nothing at all, and nothing else would ever tell the author.
            LevelData level = Valid();
            level.panels = new[] { new LevelPanel { id = "p01", rect = new[] { 4f, 9f, 4f, 1f } } };
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 2f, 1f, 2f, 2f } },
            };
            level.links = new[] { new LevelLink { from = "t01", to = new[] { "p01" }, action = "off" } };

            AssertWarns(level, "covers no tiles");
        }

        [Test]
        public void AWellFormedPanel_ProducesNoPanelWarnings()
        {
            LevelData level = Valid();

            // Valid() is a bordered room, so row 0 is solid across its whole width.
            level.panels = new[] { new LevelPanel { id = "p01", rect = new[] { 4f, 0f, 4f, 1f } } };
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 2f, 1f, 2f, 2f } },
            };
            level.links = new[] { new LevelLink { from = "t01", to = new[] { "p01" }, action = "off" } };

            Assert.IsFalse(Mentions(LevelValidator.Validate(level), "panel"),
                           string.Join(" | ", LevelValidator.Validate(level)));
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter KeepyUppy.SimTests.LevelValidatorTests`
Expected: FAIL — none of these warnings exist.

- [ ] **Step 3: Add the lints**

In `LevelValidator.cs`, call `ValidatePanels(level, warnings);` from `Validate` after `ValidateTriggers`, and add:

```csharp
        /// <summary>
        /// Panels (tile-panels design). Duplicate ids and links naming a missing panel are
        /// already covered by ValidateIds; this is everything else.
        /// </summary>
        static void ValidatePanels(LevelData level, List<string> warnings)
        {
            if (level.panels == null || level.panels.Length == 0) return;

            var grid = new TileGrid();
            grid.LoadRows(level.tiles ?? new string[0]);

            foreach (LevelPanel panel in level.panels)
            {
                if (panel == null) continue;

                float[] r = panel.rect;

                if (r == null || r.Length < 4 || r[2] <= 0f || r[3] <= 0f)
                {
                    warnings.Add($"panel '{panel.id}' has a zero-area rect");
                    continue;
                }

                if (!LinkedTo(level, panel.id))
                    warnings.Add($"nothing links to panel '{panel.id}'");

                if (!CoversATile(grid, r))
                    warnings.Add($"panel '{panel.id}' covers no tiles — it would hide nothing");
            }
        }

        static bool CoversATile(TileGrid grid, float[] rect)
        {
            int x0 = Mathf.RoundToInt(rect[0]);
            int y0 = Mathf.RoundToInt(rect[1]);
            int x1 = x0 + Mathf.RoundToInt(rect[2]);
            int y1 = y0 + Mathf.RoundToInt(rect[3]);

            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (TileGrid.InBounds(x, y) && grid[x, y] != TileKind.Empty) return true;

            return false;
        }

        /// <summary>Whether any link names this id as a target. A panel is never a source.</summary>
        static bool LinkedTo(LevelData level, string id)
        {
            if (level.links == null || string.IsNullOrEmpty(id)) return false;

            foreach (LevelLink link in level.links)
            {
                if (link?.to == null) continue;

                foreach (string target in link.to)
                    if (target == id) return true;
            }

            return false;
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Level/LevelValidator.cs Assets/_Project/Tests/EditMode/LevelValidatorTests.cs
git commit -m "Lint panels"
```

---

## Task 9: Editor rules — panel CRUD in `EditorSession`

**Files:**
- Modify: `Assets/_Project/Scripts/LevelEditor/EditorSession.cs`
- Test: `Assets/_Project/Tests/EditMode/EditorPanelTests.cs`

**Interfaces:**
- Consumes: `ZoneGeometry` (existing), `LevelPanel` (Task 1).
- Produces: `SelectedPanelId`, `FindPanel`, `PanelAt`, `AddPanel`, `SelectPanel`, `SetPanelRect`, `SetPanelEnabled`, `DeletePanel`; `TargetCentre` and `TargetIdAt` resolve panels. Tasks 10 and 11 call these.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/EditorPanelTests.cs`:

```csharp
using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Authoring panels (tile-panels design). Mirrors the zone and trigger sections, which is
    /// the point — an author who knows one rect tool knows all of them.
    /// </summary>
    public sealed class EditorPanelTests
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
                triggers = new[]
                {
                    new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 2f, 1f, 4f, 4f } },
                },
            });
            return session;
        }

        [Test]
        public void AddPanel_AllocatesTheLowestFreeId_AndSelectsIt()
        {
            EditorSession session = Session();

            LevelPanel first = session.AddPanel(new Rect(10f, 1f, 6f, 1f));
            LevelPanel second = session.AddPanel(new Rect(20f, 1f, 2f, 1f));

            Assert.AreEqual("p01", first.id);
            Assert.AreEqual("p02", second.id);
            Assert.AreEqual("p02", session.SelectedPanelId);
            Assert.AreEqual("on", first.start_state);
            Assert.IsTrue(session.Dirty);
        }

        [Test]
        public void AddPanel_AfterADelete_ReusesTheGap()
        {
            EditorSession session = Session();
            session.AddPanel(new Rect(10f, 1f, 6f, 1f));
            session.AddPanel(new Rect(20f, 1f, 2f, 1f));

            session.DeletePanel("p01");

            Assert.AreEqual("p01", session.AddPanel(new Rect(4f, 1f, 2f, 1f)).id);
        }

        [Test]
        public void PanelAt_PicksTheSmallestOverlapping()
        {
            EditorSession session = Session();
            session.AddPanel(new Rect(0f, 0f, 20f, 12f));
            LevelPanel small = session.AddPanel(new Rect(4f, 4f, 2f, 2f));

            Assert.AreEqual(small.id, session.PanelAt(new Vector2(5f, 5f)).id);
            Assert.IsNull(session.PanelAt(new Vector2(30f, 17f)));
        }

        [Test]
        public void SelectingAPanel_ClearsEveryOtherSelection()
        {
            EditorSession session = Session();
            session.SelectEntity("e01");
            session.AddPanel(new Rect(10f, 1f, 6f, 1f));

            Assert.IsNull(session.SelectedEntityId);
            Assert.IsNull(session.SelectedZoneId);
            Assert.IsNull(session.SelectedTriggerId);
            Assert.IsFalse(session.ExitSelected);

            session.SelectTrigger("t01");
            Assert.IsNull(session.SelectedPanelId, "and the exclusion runs both ways");
        }

        [Test]
        public void SetPanelRect_SnapsToTheTileGrid()
        {
            EditorSession session = Session();
            LevelPanel panel = session.AddPanel(new Rect(10f, 1f, 6f, 1f));

            session.SetPanelRect(panel.id, new Rect(9.4f, 1.6f, 6.2f, 1.4f));

            Assert.AreEqual(new[] { 9f, 2f, 6f, 1f }, panel.rect);
        }

        [Test]
        public void SetPanelEnabled_WritesTheStartState()
        {
            EditorSession session = Session();
            LevelPanel panel = session.AddPanel(new Rect(10f, 1f, 6f, 1f));

            session.SetPanelEnabled(panel.id, false);
            Assert.AreEqual("off", panel.start_state);

            session.SetPanelEnabled(panel.id, true);
            Assert.AreEqual("on", panel.start_state);
        }

        [Test]
        public void APanelCanBeALinkTarget_AndDeletingItCleansTheLinkUp()
        {
            EditorSession session = Session();
            session.AddPanel(new Rect(10f, 1f, 6f, 1f));
            session.AddLink("t01", "p01");

            Assert.AreEqual(new[] { "p01" }, session.Level.links[0].to);
            Assert.IsTrue(session.TargetCentre("p01", out Vector2 centre));
            Assert.AreEqual(new Vector2(13f, 1.5f), centre);

            session.DeletePanel("p01");

            Assert.AreEqual(0, session.Level.links.Length, "an orphaned link is a validator error");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./test.sh -testFilter KeepyUppy.SimTests.EditorPanelTests`
Expected: FAIL — `EditorSession` has no `AddPanel`.

- [ ] **Step 3: Add the panel section**

In `EditorSession.cs`, add the field beside `SelectedTriggerId`:

```csharp
        /// <summary>
        /// The selected panel. Mutually exclusive with the entity, zone, trigger and exit
        /// selections: Delete tests each independently, so two live selections means one
        /// keypress destroying two objects.
        /// </summary>
        public string SelectedPanelId;
```

Add `SelectedPanelId = null;` to `SelectZone`, `SelectEntity`, `SelectTrigger`, `SelectExit` and `Load`, and add to the method that clears stale selections after a level swap (the one containing `if (FindZone(SelectedZoneId) == null) SelectedZoneId = null;`):

```csharp
            if (FindPanel(SelectedPanelId) == null) SelectedPanelId = null;
```

Then the section itself, after the trigger section:

```csharp
        // ------------------------------------------------------------------ panels

        public LevelPanel FindPanel(string id)
        {
            if (string.IsNullOrEmpty(id) || Level.panels == null) return null;

            foreach (LevelPanel panel in Level.panels)
                if (panel != null && panel.id == id) return panel;

            return null;
        }

        /// <summary>Smallest first, as ZoneAt and TriggerAt are — panels nest by design.</summary>
        public LevelPanel PanelAt(Vector2 world)
        {
            if (Level.panels == null) return null;

            LevelPanel best = null;
            float bestArea = float.MaxValue;

            foreach (LevelPanel panel in Level.panels)
            {
                if (panel?.rect == null || panel.rect.Length < 4) continue;

                Rect rect = ZoneGeometry.RectOf(panel.rect);
                if (!rect.Contains(world)) continue;

                float area = rect.width * rect.height;
                if (area >= bestArea) continue;

                bestArea = area;
                best = panel;
            }

            return best;
        }

        public LevelPanel AddPanel(Rect rect)
        {
            var panel = new LevelPanel
            {
                id = NextPanelId(),
                rect = ZoneGeometry.ToArray(ZoneGeometry.Snap(rect)),
                start_state = "on",
            };

            var panels = new List<LevelPanel>(Level.panels ?? new LevelPanel[0]) { panel };
            Level.panels = panels.ToArray();

            SelectPanel(panel.id);
            Dirty = true;
            return panel;
        }

        public void SelectPanel(string id)
        {
            SelectedPanelId = id;
            SelectedEntityId = null;
            SelectedZoneId = null;
            SelectedTriggerId = null;
            ExitSelected = false;
        }

        public void SetPanelRect(string id, Rect rect)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return;

            panel.rect = ZoneGeometry.ToArray(ZoneGeometry.Snap(rect));
            Dirty = true;
        }

        public void SetPanelEnabled(string id, bool enabled)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return;

            string state = enabled ? "on" : "off";
            if (panel.start_state == state) return;

            panel.start_state = state;
            Dirty = true;
        }

        public bool DeletePanel(string id)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return false;

            var panels = new List<LevelPanel>(Level.panels);
            panels.Remove(panel);
            Level.panels = panels.ToArray();

            RemoveLinksReferencing(id);

            if (SelectedPanelId == id) SelectedPanelId = null;
            Dirty = true;
            return true;
        }

        /// <summary>Lowest unused pNN id, matching how entity, zone and trigger ids are allocated.</summary>
        string NextPanelId()
        {
            for (int i = 1; i < 1000; i++)
            {
                string candidate = "p" + i.ToString("00");
                if (FindPanel(candidate) == null) return candidate;
            }

            return "p" + (Level.panels?.Length ?? 0);
        }
```

`RemoveLinksReferencing(string id)` already exists — it was added when `DeleteTrigger`, `DeleteEntity` and `DeleteZone` were made to cascade.

Finally, teach linking about panels. In `TargetCentre`, after the trigger branch:

```csharp
            LevelPanel panel = FindPanel(id);
            if (panel != null)
            {
                centre = ZoneGeometry.RectOf(panel.rect).center;
                return true;
            }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./test.sh`
Expected: PASS, `failed=0` — run the whole suite, since selection exclusivity is shared.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/LevelEditor/EditorSession.cs Assets/_Project/Tests/EditMode/EditorPanelTests.cs
git commit -m "Add panel editing rules to EditorSession"
```

---

## Task 10: The Panel tool and its inspector

**Files:**
- Modify: `Assets/_Project/Scripts/LevelEditor/EditorTool.cs`
- Create: `Assets/_Project/Scripts/LevelEditor/PanelInspector.cs`
- Modify: `Assets/_Project/Scripts/LevelEditor/LevelEditorController.cs`

**Interfaces:**
- Consumes: everything from Task 9.
- Produces: `EditorTool.Panel = 9`; `PanelInspector.Draw(EditorSession, Dictionary<string, string>) → bool`.

`LevelEditorController` and the inspectors have no automated coverage by design — the rules they drive are tested in `EditorSession`. **Do not add a test that fakes a `Mouse`, a `Keyboard` or an IMGUI layout.** Verification is: compiles with no errors and no new warnings, `./test.sh` unchanged, and a report saying what only a human at the keyboard can check.

- [ ] **Step 1: Add the tool value**

In `EditorTool.cs`, after `Trigger = 8`:

```csharp
        /// <summary>9 — drag a rect of tiles a link can take away (tile-panels design).</summary>
        Panel = 9,
```

- [ ] **Step 2: Write the inspector**

Create `Assets/_Project/Scripts/LevelEditor/PanelInspector.cs`, modelled on `TriggerInspector.cs`:

```csharp
#if LEVEL_EDITOR
using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// The selected panel's fields (tile-panels design).
    ///
    /// Its own file because LevelEditorController is already long. IMGUI and untested, the
    /// same boundary ZoneInspector and TriggerInspector sit on.
    /// </summary>
    public static class PanelInspector
    {
        public static bool Draw(EditorSession session, Dictionary<string, string> fieldText)
        {
            LevelPanel panel = session.FindPanel(session.SelectedPanelId);
            if (panel == null) return false;

            bool changed = false;

            GUILayout.Label($"panel {panel.id}");
            GUILayout.Label("tiles inside this rect can be switched by a link");

            Rect rect = ZoneGeometry.RectOf(panel.rect);
            float x = Row("x", $"{panel.id}.x", rect.xMin, fieldText);
            float y = Row("y", $"{panel.id}.y", rect.yMin, fieldText);
            float w = Row("w", $"{panel.id}.w", rect.width, fieldText);
            float h = Row("h", $"{panel.id}.h", rect.height, fieldText);

            var typed = ZoneGeometry.Snap(new Rect(x, y, Mathf.Max(w, 1f), Mathf.Max(h, 1f)));
            if (typed != rect)
            {
                session.BeginEdit();
                session.SetPanelRect(panel.id, typed);
                changed = true;
            }

            bool on = panel.start_state != "off";
            bool nowOn = GUILayout.Toggle(on, on ? "start: tiles present" : "start: tiles hidden");
            if (nowOn != on)
            {
                session.BeginEdit();
                session.SetPanelEnabled(panel.id, nowOn);
                changed = true;
            }

            if (!nowOn)
                GUILayout.Label("   (hidden at load — an appearing platform)");

            LevelLink link = null;
            if (session.Level.links != null)
            {
                foreach (LevelLink candidate in session.Level.links)
                {
                    if (candidate?.to == null) continue;

                    foreach (string target in candidate.to)
                        if (target == panel.id) link = candidate;
                }
            }

            GUILayout.Label(link == null
                ? "no link — use tool 6 from a trigger"
                : $"switched by {link.from} ({link.action})");

            if (GUILayout.Button("Delete panel", GUILayout.Width(110f)))
            {
                session.BeginEdit();
                session.DeletePanel(panel.id);
                changed = true;
            }

            return changed;
        }

        /// <summary>A labelled number box, through DevNumber as every other panel field is.</summary>
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

- [ ] **Step 3: Wire the tool into the controller**

Read `TriggerTool` in `LevelEditorController.cs` first — `PanelTool` is the same shape with the nouns changed, and any place they differ is a place to check you have not improvised. Add the drag fields beside the trigger ones:

```csharp
        ZoneCorner _panelCorner;
        Vector2Int _panelAnchor;
        Vector2 _panelDragOffset;
        bool _panelCreating;
```

The binding, beside `digit8Key`:

```csharp
            if (kb.digit9Key.wasPressedThisFrame) Session.Tool = EditorTool.Panel;
```

The tool-change reset, beside the trigger one:

```csharp
            if (Session.Tool != EditorTool.Panel) { _panelCorner = ZoneCorner.None; _panelCreating = false; }
```

The `Delete` branch:

```csharp
            if (kb.deleteKey.wasPressedThisFrame && Session.Tool == EditorTool.Panel
                && Session.SelectedPanelId != null)
            {
                Session.BeginEdit();
                Session.DeletePanel(Session.SelectedPanelId);
                SyncDynamic();
                _status = "deleted panel";
            }
```

The palette button, after the `8 Trigger` one:

```csharp
            if (GUILayout.Button("9 Panel", GUILayout.Width(70f))) Session.Tool = EditorTool.Panel;
```

The dispatch case:

```csharp
                case EditorTool.Panel: PanelTool(mouse, world); break;
```

And the tool, mirroring `TriggerTool` exactly:

```csharp
        /// <summary>
        /// Panel rects (tile-panels design). Identical in shape to TriggerTool and ZoneTool:
        /// all three are tile rects on the same grid, so all three route through ZoneGeometry.
        /// </summary>
        void PanelTool(Mouse mouse, Vector2 world)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                LevelPanel hit = Session.PanelAt(world);
                if (hit != null)
                {
                    Session.BeginEdit();
                    Session.DeletePanel(hit.id);
                    SyncDynamic();
                    _status = "deleted panel";
                }

                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                LevelPanel selected = Session.FindPanel(Session.SelectedPanelId);

                _panelCorner = selected?.rect != null
                    ? ZoneGeometry.HandleAt(ZoneGeometry.RectOf(selected.rect), world, ZoneHandleRadius)
                    : ZoneCorner.None;

                LevelPanel hit = Session.PanelAt(world);

                if (_panelCorner != ZoneCorner.None)
                {
                    _panelAnchor = ZoneGeometry.AnchorOf(ZoneGeometry.RectOf(selected.rect), _panelCorner);
                    Session.BeginEdit();
                    _panelCreating = false;
                    _dragging = true;
                }
                else if (hit != null)
                {
                    Session.SelectPanel(hit.id);
                    Rect rect = ZoneGeometry.RectOf(hit.rect);
                    _panelDragOffset = new Vector2(rect.xMin, rect.yMin) - world;
                    Session.BeginEdit();
                    _panelCreating = false;
                    _dragging = true;
                }
                else
                {
                    _dragStart = GridCursor.ToTile(world);
                    _panelCreating = true;
                    _dragging = true;
                }
            }

            if (!_dragging) return;

            if (_panelCreating)
            {
                if (!mouse.leftButton.wasReleasedThisFrame) return;

                Session.BeginEdit();
                LevelPanel created = Session.AddPanel(ZoneGeometry.Normalise(_dragStart, GridCursor.ToTile(world)));
                SyncDynamic();

                _dragging = false;
                _panelCreating = false;
                _status = $"added {created.id}";
                return;
            }

            LevelPanel dragged = Session.FindPanel(Session.SelectedPanelId);
            if (dragged?.rect == null) { _dragging = false; return; }

            Rect current = ZoneGeometry.RectOf(dragged.rect);

            Rect updated = _panelCorner != ZoneCorner.None
                ? ZoneGeometry.Normalise(_panelAnchor, GridCursor.ToTile(world))
                : ZoneGeometry.Move(current, world + _panelDragOffset);

            Session.SetPanelRect(dragged.id, updated);
            SyncDynamic();

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _status = _panelCorner != ZoneCorner.None ? "resized panel" : "moved panel";
                _dragging = false;
                _panelCorner = ZoneCorner.None;
            }
        }
```

- [ ] **Step 4: Draw them, and show the inspector**

In `DrawOverlays`, after `DrawTriggers();`:

```csharp
            DrawPanels();
```

```csharp
        /// <summary>
        /// Every panel, outlined; one that starts hidden is also hatched, so the author can
        /// see geometry the player will not. Amber — not the trigger violet, not hazard red,
        /// not balloon yellow (GDD 13.1).
        /// </summary>
        void DrawPanels()
        {
            if (Session.Level.panels == null) return;

            foreach (LevelPanel panel in Session.Level.panels)
            {
                if (panel?.rect == null || panel.rect.Length < 4) continue;

                Rect rect = ZoneGeometry.RectOf(panel.rect);
                bool selected = panel.id == Session.SelectedPanelId;

                DrawWorldRect(new Rect(rect.xMin, rect.yMax - 0.08f, rect.width, 0.08f), PanelEdge);
                DrawWorldRect(new Rect(rect.xMin, rect.yMin, rect.width, 0.08f), PanelEdge);
                DrawWorldRect(new Rect(rect.xMin, rect.yMin, 0.08f, rect.height), PanelEdge);
                DrawWorldRect(new Rect(rect.xMax - 0.08f, rect.yMin, 0.08f, rect.height), PanelEdge);

                // Hidden at load: hatch it so the author sees tiles the player will not.
                if (panel.start_state == "off")
                {
                    for (float x = rect.xMin; x < rect.xMax; x += 0.5f)
                        DrawWorldRect(new Rect(x, rect.yMin, 0.12f, rect.height), PanelHatch);
                }

                if (!selected) continue;

                DrawHandle(new Vector2(rect.xMin, rect.yMin));
                DrawHandle(new Vector2(rect.xMax, rect.yMin));
                DrawHandle(new Vector2(rect.xMin, rect.yMax));
                DrawHandle(new Vector2(rect.xMax, rect.yMax));
            }
        }

        static readonly Color PanelEdge = new Color(0.95f, 0.65f, 0.25f, 0.85f);
        static readonly Color PanelHatch = new Color(0.95f, 0.65f, 0.25f, 0.25f);
```

In `DrawInspector`, before the trigger branch:

```csharp
            if (Session.Tool == EditorTool.Panel && Session.SelectedPanelId != null)
            {
                DrawPanelInspector();
                return;
            }
```

```csharp
        void DrawPanelInspector()
        {
            _inspectorRect = InspectorRect(320f);
            GUILayout.BeginArea(_inspectorRect, GUI.skin.box);

            bool changed = PanelInspector.Draw(Session, _fieldText);

            GUILayout.EndArea();

            if (changed) SyncDynamic();
        }
```

Finally, in `TargetIdAt` (used by the Link tool), add panels last so a panel can be wired:

```csharp
            LevelPanel panel = Session.PanelAt(world);
            return panel?.id;
```
replacing the current `return trigger?.id;` tail with a trigger check followed by this.

- [ ] **Step 5: Verify**

Run: `./test.sh`
Expected: PASS, `failed=0`, no new compiler warnings.

- [ ] **Step 6: Manual check**

In Unity with `LEVEL_EDITOR` defined: press `9`, drag a rect over a floor, wire it from a trigger with `6`, set the link action to `off`, then `Tab` into play and walk into the trigger. The floor should go. Report what you could not verify.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Scripts/LevelEditor
git commit -m "Add the panel tool and its inspector"
```

---

## Task 11: Falling debris

**Files:**
- Create: `Assets/_Project/Scripts/Runtime/PanelViewSync.cs`
- Modify: `Assets/_Project/Scripts/Runtime/LevelSession.cs`

**Interfaces:**
- Consumes: `SimWorld.Panels`, `SimPanel.Enabled/Rect` (Tasks 3–4); `QuadView.Create(Transform, string, Color, Vector2 centre, Vector2 size, float z)`; `SimPalette.Solid`, `SimPalette.Hazard`.
- Produces: `PanelViewSync` with public `SimWorld World` and `SimDriver Driver`, built by `LevelSession.BuildPanelViews`.

Views have no EditMode coverage (they need rendering). **Do not add a test that instantiates a MonoBehaviour or GameObject.** Verification is a green suite, no new warnings, and an honest report of what needs a human.

- [ ] **Step 1: Write the view**

Create `Assets/_Project/Scripts/Runtime/PanelViewSync.cs`:

```csharp
using System.Collections.Generic;
using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Debris for a floor a link has taken away (tile-panels design).
    ///
    /// A floor that blinks out of existence reads as a rendering glitch; a floor that falls
    /// reads as a trap. But a tile that falls *as a solid* is the moving-solid problem the
    /// project has deferred on design grounds, so this is presentation only: collision ended
    /// on the step the panel switched, and these quads are scenery that nothing can touch.
    ///
    /// Nothing here is readable by the simulation, so determinism is untouched — the same
    /// rule StepInterpolator and TimeControl already follow. That is also why this may use
    /// render time when no code under Sim/ ever may.
    /// </summary>
    public sealed class PanelViewSync : MonoBehaviour
    {
        public SimWorld World;

        /// <summary>Used only to notice a restart, never to drive the fall.</summary>
        public SimDriver Driver;

        struct Debris
        {
            public GameObject view;
            public float velocity;
        }

        readonly List<Debris> _debris = new List<Debris>();
        readonly List<bool> _wasEnabled = new List<bool>();
        int _lastStep;

        /// <summary>Metres per second squared. The player's own gravity, so it looks of a piece.</summary>
        const float Gravity = 40f;

        void LateUpdate()
        {
            if (World == null) return;

            WatchForRestart();
            WatchPanels();
            FallAndFade();
        }

        /// <summary>
        /// A restart clears the debris. Clock.Step running backwards is the signal, the same
        /// one StepInterpolator uses — tiles still falling through a floor that is back would
        /// be the room half-reset (GDD 8).
        /// </summary>
        void WatchForRestart()
        {
            int step = World.Clock.Step;

            if (step < _lastStep)
            {
                foreach (Debris d in _debris) if (d.view != null) Destroy(d.view);
                _debris.Clear();
            }

            _lastStep = step;
        }

        void WatchPanels()
        {
            while (_wasEnabled.Count < World.Panels.Count) _wasEnabled.Add(true);

            for (int i = 0; i < World.Panels.Count; i++)
            {
                bool enabled = World.Panels[i].Enabled;

                // The true-to-false edge is the moment the floor gives way.
                if (_wasEnabled[i] && !enabled) Spawn(World.Panels[i].Rect);

                _wasEnabled[i] = enabled;
            }
        }

        /// <summary>
        /// One quad per tile the panel just hid. Read from the level's rows rather than the
        /// live grid: the mask is already applied by the time this runs, so the live grid
        /// reports Empty for exactly the tiles we want to draw falling.
        /// </summary>
        void Spawn(RectInt rect)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                if (!TileGrid.InBounds(x, y)) continue;
                if (!World.Grid.IsMasked(x, y)) continue;

                TileKind kind = World.Grid.BaseAt(x, y);
                if (kind == TileKind.Empty) continue;

                Color colour = kind == TileKind.Solid ? SimPalette.Solid : SimPalette.Hazard;

                _debris.Add(new Debris
                {
                    view = QuadView.Create(transform, $"debris_{x}_{y}", colour,
                                           new Vector2(x + 0.5f, y + 0.5f), Vector2.one, -0.1f),
                    velocity = 0f,
                });
            }
        }

        void FallAndFade()
        {
            float dt = Time.deltaTime;

            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                Debris d = _debris[i];

                if (d.view == null) { _debris.RemoveAt(i); continue; }

                d.velocity -= Gravity * dt;

                Vector3 p = d.view.transform.localPosition;
                p.y += d.velocity * dt;
                d.view.transform.localPosition = p;

                if (p.y < -2f)
                {
                    Destroy(d.view);
                    _debris.RemoveAt(i);
                    continue;
                }

                _debris[i] = d;
            }
        }
    }
}
```

- [ ] **Step 2: Add the one grid accessor it needs**

`Spawn` needs the tile *underneath* the mask, which the indexer deliberately hides. In `TileGrid.cs`, beside `IsMasked`:

```csharp
        /// <summary>
        /// The tile a cell holds regardless of any mask. For the debris view, which draws the
        /// tiles a panel has just hidden — the indexer reports those as Empty by design.
        /// </summary>
        public TileKind BaseAt(int x, int y)
            => InBounds(x, y) ? _cells[y * Width + x] : TileKind.Solid;
```

- [ ] **Step 3: Build it with the session**

In `LevelSession.Create`, after `session.BuildTriggerViews(world);`:

```csharp
            session.BuildPanelViews(world, driver);
```

and the builder beside `BuildTriggerViews` — **match the shape of the existing builders** (check whether they add the component to `gameObject` or to a child root, and follow that):

```csharp
        void BuildPanelViews(SimWorld world, SimDriver driver)
        {
            var root = new GameObject("Panels");
            root.transform.SetParent(transform, false);

            PanelViewSync view = root.AddComponent<PanelViewSync>();
            view.World = world;
            view.Driver = driver;
        }
```

- [ ] **Step 4: Verify**

Run: `./test.sh`
Expected: PASS, `failed=0`, no new warnings.

- [ ] **Step 5: Manual check**

Play a level with a panel wired to `off`. The tiles should fall, accelerate, and disappear below the room; the collision should end the instant they start falling, not when they land; and pressing `R` mid-fall should clear the debris and restore the floor in one frame.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Scripts/Runtime Assets/_Project/Scripts/Sim/TileGrid.cs
git commit -m "Drop debris when a panel takes its tiles away"
```

---

## Task 12: Documentation and a level to play

**Files:**
- Modify: `docs/level-editor-spec.md`, `docs/balloon-game-gdd.md`
- Modify: `docs/superpowers/specs/2026-08-29-tile-panels-design.md` (status line)
- Modify: `docs/superpowers/specs/2026-08-28-triggers-and-links-design.md` ("Not in this pass")
- Create: `Assets/StreamingAssets/levels/w1_floor.json` — a draft, NOT added to `index.json`

- [ ] **Step 1: Verify every claim before writing it**

Check each against the code and report anything that differs: which link actions switch a panel (`TriggerField.Apply`); that a panel masks rather than edits (`TileGrid`); that `start_state: "off"` hides at load (`LevelLoader.LoadPanels`); that returning tiles kill (`SimWorld.CrushBodiesInReturningPanels`); that the balloon now dies outside the room (`SimWorld.StepBalloon`); the editor key (`EditorTool.cs`).

- [ ] **Step 2: Update `docs/level-editor-spec.md`**

Add the `panels` array to the §3 schema example, a "Tile panels — 2026-08-29, as built" block beside the trigger one (at the end of the file, where the zone and trigger blocks actually live — the file has no §3 subsection for them), the Panel row to §4's tool table (`| Panel | `9` | Drag a rect of tiles a link can take away. Hatched when it starts hidden. |`), and the three new lints to §7.

- [ ] **Step 3: Update `docs/balloon-game-gdd.md`**

In §9.1, record that disappearing floors are built as tile panels, and that crushers, closing doors and moving platforms remain deferred because they need moving-solid collision (§22, 2026-08-24) — a vanishing tile does not. In §4.3, record that the balloon now also dies on leaving the room.

No §22 row: nothing LOCKED changed, and the crush routes through `SimWorld.Kill` precisely so §4.1 still holds.

- [ ] **Step 4: Correct the trigger spec**

In `2026-08-28-triggers-and-links-design.md`, under "Not in this pass", mark tile-rect toggling as built on 2026-08-29 and note that grouping it with moving-solid collision was wrong — a vanishing tile never pushes a body.

- [ ] **Step 5: Author the draft level**

Create `Assets/StreamingAssets/levels/w1_floor.json` with the same key order as `w1_02.json`: a bordered room, a floor the player must cross, a panel over the middle of it, a `once: true` `player_region` trigger covering the approach, and a link `{ "from": "t01", "to": ["p01"], "action": "off", "delay": 0.15 }`. Spawns clear of geometry, a reachable exit, `meta.par_touches` set.

**Do not add it to `index.json`** — a level on disk but absent from the manifest is a draft the editor opens and the game does not list.

To check it lints clean, write a temporary EditMode test that loads the file and asserts `LevelValidator.Validate` returns an empty list, run it, then **delete the test before committing** and say in your report what it reported.

- [ ] **Step 6: Verify**

Run: `./test.sh`
Expected: PASS at the plan's baseline plus the tests added by Tasks 1–9, with the temporary test removed.

- [ ] **Step 7: Commit**

```bash
git add docs Assets/StreamingAssets/levels/w1_floor.json Assets/StreamingAssets/levels/w1_floor.json.meta
git commit -m "Document tile panels, and add a falling-floor draft to play"
```

---

## Self-Review

Checked against the spec:

- Panels as named rects with ids → Task 1. Mask layer, rows immutable → Task 2. `PanelField`, rebuild-not-toggle, overlapping panels AND → Task 3. `start_state: "off"` as an appearing platform → Task 4. Links switch panels; crush through `SimWorld.Kill`; `DeathCause.Crushed` → Task 5. Balloon leaves the room → Task 6. Determinism fold and a room that exercises it → Tasks 5 and 7. Three lints → Task 8. Editor rules, tool `9`, inspector, hatching → Tasks 9–10. Falling debris, restart clears it, render time → Task 11. Docs and a draft → Task 12.
- **One addition the spec did not name:** `TileGrid.BaseAt(x, y)`, in Task 11. The debris view has to draw the tiles a panel has just hidden, and the indexer reports those as `Empty` by design. Without it there is nothing to draw.
- **One thing to watch in Task 5:** the crush test drives `CrushBodiesInReturningPanels` directly, which means it must be reachable from the test assembly. The task says to use `internal` and fall back to `public` if the assembly boundary objects, and to report which was used.
- **Task 7 Step 3 is a real fork:** if `BusyScript()` never walks into the trigger, the room must move, not the assertion. Called out explicitly because the equivalent guess in the trigger plan was wrong and cost a fix round.
- Names used consistently: `SetMasked` / `ClearMask` / `IsMasked` / `BaseAt`; `PanelField.Apply/SetEnabled/BeginStep/Reset/Dirty/TurnedOnThisStep`; `EditorSession.AddPanel/PanelAt/FindPanel/SelectPanel/SetPanelRect/SetPanelEnabled/DeletePanel`; `CrushBodiesInReturningPanels`.
