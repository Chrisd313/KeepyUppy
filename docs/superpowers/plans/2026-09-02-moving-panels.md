# Moving Panels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A panel with a path becomes a solid that travels — still solid, still lethal, able to carry, shove and crush — driven by new `run` / `return` link actions.

**Architecture:** A panel with `path` is lifted out of `TileGrid` at load and published each step into a new `SolidField` of world-space rects plus per-step velocities. Position is a pure function of `step - RunStep` (never accumulated), exactly as `EntityPath` already does for saws. Both sweeps gain a second cast source; shove and crush resolve before bodies step; the balloon leaves a moving face through `ApplyHit`.

**Tech Stack:** Unity 6000.2.6f2, C#, plain classes under `Assets/_Project/Scripts/Sim/` with no scene dependency. NUnit EditMode tests. Run everything with `./test.sh` (Unity must be closed).

**Spec:** `docs/superpowers/specs/2026-09-02-moving-panels-design.md`

## Global Constraints

- **60 Hz fixed step.** No `Time.deltaTime` anywhere under `Sim/`. Use `SimClock.Dt`.
- **No `Random`, no wall clock, no unordered iteration** in sim code. `List`, never `Dictionary`/`HashSet`, where order affects the result.
- **Position is derived, never accumulated.** Panel offset = pure function of the step counter, as `EntityPath.Evaluate` is.
- **`ApplyHit` sets velocity; force is never added** (GDD §5.1, §6.4).
- **Passive contact loses energy** (GDD §4.2): a stationary panel face rebounds at `restitutionWall`; only a moving one adds energy.
- **Linked fate is simultaneous** (GDD §4.1): all deaths go through `SimWorld.Kill(DeathCause)`, which pops both bodies in one step.
- **Any change that adds sim state must be in the replay-equality test in the same commit** — Task 12 is not optional and not deferrable.
- **Tuning values live in profiles**, not literals: new feel numbers go on `BalloonTuning` (GDD §6.4).
- **`LevelEditor/` must never contain `using UnityEditor;`** and is gated by the `LEVEL_EDITOR` define.
- Tests are EditMode, plain C#, no scene. Run: `./test.sh` — exit 0 means all pass.
- **Do not create or edit `.unity`, `.prefab` or `.asset` files.**

## File Structure

**Created:**
- `Assets/_Project/Scripts/Sim/Panels/PanelPath.cs` — pure position-along-path function with dwell.
- `Assets/_Project/Scripts/Sim/Panels/SolidField.cs` — the moving panels' rects + velocities for a step.
- `Assets/_Project/Tests/EditMode/PanelPathTests.cs`
- `Assets/_Project/Tests/EditMode/MovingPanelTests.cs` — load, publish, carry, shove, crush.
- `Assets/_Project/Tests/EditMode/MovingPanelContactTests.cs` — balloon and hazard behaviour.

**Modified:**
- `Sim/Panels/SimPanel.cs` — path config + `RunStep`/`Direction` run state.
- `Sim/Panels/PanelField.cs` — skip moving panels when building the mask; publish to `SolidField`.
- `Sim/Level/LevelData.cs`, `Sim/Level/LevelLoader.cs`, `Sim/Level/LevelValidator.cs` — schema, load, lint.
- `Sim/SweptAabb.cs`, `Sim/SweptCircle.cs`, `Sim/SweepResult.cs` — second cast source + `carrier`.
- `Sim/PlayerBody.cs` — sweep against solids, carry.
- `Sim/SimWorld.cs` — tick order, shove/crush, balloon-off-moving-face.
- `Sim/Hazards.cs` — lethal tiles carried by a moving panel.
- `Sim/HitSource.cs`, `Sim/BalloonTuning.cs` — `Panel` source + its power coefficient.
- `Sim/Triggers/SimLink.cs`, `Sim/Level/TriggerFactory.cs`, `Sim/Triggers/TriggerField.cs` — `run`/`return`.
- `Sim/Entities/SimEntity.cs` — `Return` rewind.
- `LevelEditor/PanelInspector.cs`, `LevelEditor/TriggerInspector.cs`, `LevelEditor/EditorSession.cs`, `LevelEditor/LevelEditorController.cs` — authoring.
- `Runtime/PanelViewSync.cs` — draw moving panels.
- `docs/level-editor-spec.md`, `docs/balloon-game-gdd.md`, `CLAUDE.md`.

---

### Task 1: `PanelPath` — position along a path, with dwell

**Files:**
- Create: `Assets/_Project/Scripts/Sim/Panels/PanelPath.cs`
- Test: `Assets/_Project/Tests/EditMode/PanelPathTests.cs`

**Interfaces:**
- Consumes: `KeepyUppy.Sim.Entities.EntityPath.Evaluate`, `KeepyUppy.Sim.Entities.PathMode`, `SimClock.Dt`, `SimClock.StepsIn(float seconds)`.
- Produces: `PanelPath.Evaluate(Vector2[] points, PathMode mode, float speed, float phase, int holdSteps, int step) -> Vector2`.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/PanelPathTests.cs`:

```csharp
using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Panels;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Where a moving panel is at a given step (moving-panels design).
    ///
    /// A pure function of the step counter, exactly as EntityPath is — that is what makes a
    /// restart free and attempt 40 replay attempt 1 (GDD 6.1).
    /// </summary>
    public sealed class PanelPathTests
    {
        static readonly Vector2[] UpThree = { Vector2.zero, new Vector2(0f, 3f) };

        [Test]
        public void WithNoHold_ItMatchesEntityPath()
        {
            for (int step = 0; step < 200; step += 7)
                Assert.AreEqual(EntityPath.Evaluate(UpThree, PathMode.Once, 6f, 0f, step),
                                PanelPath.Evaluate(UpThree, PathMode.Once, 6f, 0f, 0, step),
                                $"step {step}: with no dwell this must delegate, not reimplement");
        }

        [Test]
        public void ItIsAPureFunction_AskedOutOfOrder()
        {
            Vector2 at90 = PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, 30, 90);

            PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, 30, 5);
            PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, 30, 400);

            Assert.AreEqual(at90, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, 30, 90));
        }

        [Test]
        public void DwellHoldsTheEndpointForExactlyTheHoldSteps()
        {
            // 3 tiles at 6 tiles/s = 0.5 s = 30 steps of travel, then 30 steps of dwell.
            const int hold = 30;

            Assert.AreEqual(3f, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 30).y, 1e-4f,
                            "arrives at the far end after 30 steps");
            Assert.AreEqual(3f, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 59).y, 1e-4f,
                            "still parked one step before the dwell ends");
            Assert.Less(PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 75).y, 3f,
                        "and has started back once it has");
        }

        [Test]
        public void DwellAlsoHoldsTheStart_SoAPingPongPausesAtBothEnds()
        {
            const int hold = 30;

            // Out (30) + dwell (30) + back (30) = 90; the start dwell then runs to step 120.
            Assert.AreEqual(0f, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 90).y, 1e-4f);
            Assert.AreEqual(0f, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 119).y, 1e-4f);
            Assert.Greater(PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 135).y, 0f);
        }

        [Test]
        public void OnceStopsAtTheEnd_AndAPathlessPanelNeverMoves()
        {
            Assert.AreEqual(3f, PanelPath.Evaluate(UpThree, PathMode.Once, 6f, 0f, 30, 10_000).y, 1e-4f);

            var single = new[] { Vector2.zero };
            Assert.AreEqual(Vector2.zero, PanelPath.Evaluate(single, PathMode.Once, 6f, 0f, 0, 500));
            Assert.AreEqual(Vector2.zero, PanelPath.Evaluate(UpThree, PathMode.Once, 0f, 0f, 0, 500),
                            "speed 0 is a parked panel, not a divide by zero");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./test.sh`
Expected: FAIL — compile error, `The name 'PanelPath' does not exist in the current context`.

- [ ] **Step 3: Write minimal implementation**

Create `Assets/_Project/Scripts/Sim/Panels/PanelPath.cs`:

```csharp
using KeepyUppy.Sim.Entities;
using UnityEngine;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>
    /// Where a moving panel is at a given step (moving-panels design).
    ///
    /// A pure function of the step counter, never an accumulation — the same contract, and
    /// the same three reasons, as EntityPath: no float drift, a free restart, and any step
    /// askable out of order.
    ///
    /// Its own function rather than a parameter on EntityPath. Entities have never needed
    /// dwell, and EntityPath is the function every hazard's position flows through; widening
    /// it for a panel would put a saw's placement one edit away from a panel's bug. With
    /// holdSteps == 0 this delegates, so the shared case cannot drift between the two.
    /// </summary>
    public static class PanelPath
    {
        /// <summary>
        /// Position along <paramref name="points"/> at <paramref name="step"/>, pausing
        /// <paramref name="holdSteps"/> at every waypoint including both ends.
        /// </summary>
        public static Vector2 Evaluate(Vector2[] points, PathMode mode, float speed,
                                       float phase, int holdSteps, int step)
        {
            if (holdSteps <= 0) return EntityPath.Evaluate(points, mode, speed, phase, step);

            if (points == null || points.Length == 0) return Vector2.zero;
            if (points.Length == 1 || speed <= 0f) return points[0];

            // Dwell is inserted between legs rather than folded into the arc length: the
            // travel itself must stay identical to the no-dwell case, so that raising hold
            // never changes how fast the panel moves.
            int legs = points.Length - 1;
            int[] legSteps = new int[legs];
            int travel = 0;

            for (int i = 0; i < legs; i++)
            {
                float length = (points[i + 1] - points[i]).magnitude;
                legSteps[i] = Mathf.Max(1, Mathf.RoundToInt(length / speed / SimClock.Dt));
                travel += legSteps[i];
            }

            // One dwell per waypoint. Out: hold, leg, hold, leg, … PingPong walks the same
            // list back, so a full cycle is two of these.
            int oneWay = travel + holdSteps * points.Length;
            int cycle = mode == PathMode.PingPong ? oneWay * 2 : oneWay;

            int at = step + Mathf.RoundToInt(Frac(phase) * cycle);

            switch (mode)
            {
                case PathMode.Once:
                    at = Mathf.Min(at, oneWay);
                    break;

                case PathMode.Loop:
                    at = Mod(at, cycle);
                    break;

                default:
                    at = Mod(at, cycle);
                    if (at >= oneWay) at = cycle - at;   // fold the return half back
                    break;
            }

            return Along(points, legSteps, holdSteps, at);
        }

        /// <summary>Walks the leg/dwell list until the step budget runs out.</summary>
        static Vector2 Along(Vector2[] points, int[] legSteps, int holdSteps, int at)
        {
            for (int i = 0; i < legSteps.Length; i++)
            {
                if (at < holdSteps) return points[i];
                at -= holdSteps;

                if (at < legSteps[i])
                    return Vector2.Lerp(points[i], points[i + 1], (float)at / legSteps[i]);

                at -= legSteps[i];
            }

            return points[points.Length - 1];
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>Never negative, unlike C#'s %. A phase can push a step below zero.</summary>
        static int Mod(int value, int m) => m <= 0 ? 0 : ((value % m) + m) % m;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `./test.sh`
Expected: PASS — `failed=0`. If `DwellAlsoHoldsTheStart…` fails, the fold in the `PingPong` case is off by the dwell; re-check `at = cycle - at`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Panels/PanelPath.cs Assets/_Project/Scripts/Sim/Panels/PanelPath.cs.meta Assets/_Project/Tests/EditMode/PanelPathTests.cs Assets/_Project/Tests/EditMode/PanelPathTests.cs.meta
git commit -m "feat: PanelPath — panel position along a path, with dwell"
```

---

### Task 2: Schema and load — a panel that has a path

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelData.cs` (class `LevelPanel`)
- Modify: `Assets/_Project/Scripts/Sim/Panels/SimPanel.cs`
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelLoader.cs` (`LoadPanels`)
- Test: `Assets/_Project/Tests/EditMode/MovingPanelTests.cs` (create)
- Test: `Assets/_Project/Tests/EditMode/LevelFormatTests.cs` (extend `Panels_RoundTrip`)

**Interfaces:**
- Consumes: `PanelPath.Evaluate` (Task 1), `EntityPath.FromFlat` (existing, converts a flat float array to `Vector2[]`), `SimClock.StepsIn`.
- Produces: `SimPanel.Points/Mode/Speed/HoldSteps/Phase/RunStep/Direction/IsMoving`, `SimPanel.Place(int step)`, `SimPanel.WorldRect`, `SimPanel.Velocity`, `SimPanel.Run(int step)`, `SimPanel.Return(int step)`; `LevelPanel.path/path_mode/speed/hold/phase`.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/MovingPanelTests.cs`:

```csharp
using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Level;
using KeepyUppy.Sim.Panels;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Panels that travel while still solid (moving-panels design).
    ///
    /// A panel with no path is the mask it has always been; one with a path leaves the grid
    /// and becomes a body. The discriminator is the presence of the field, so no shipped
    /// level changes meaning.
    /// </summary>
    public sealed class MovingPanelTests
    {
        static LevelData Room(params LevelPanel[] panels)
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 1, 4, 1, TileKind.Solid);

            return new LevelData { tiles = grid.ToRows(), panels = panels };
        }

        static LevelPanel Riser() => new LevelPanel
        {
            id = "p01",
            rect = new[] { 10f, 1f, 4f, 1f },
            path = new[] { 0f, 0f, 0f, 3f },
            path_mode = "once",
            speed = 6f,
            hold = 0f,
        };

        [Test]
        public void APanelWithAPath_LoadsAsMoving()
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(Riser()), world, out _, out _);

            SimPanel panel = world.Panels[0];

            Assert.IsTrue(panel.IsMoving);
            Assert.AreEqual(PathMode.Once, panel.Mode);
            Assert.AreEqual(6f, panel.Speed, 1e-4f);
            Assert.AreEqual(2, panel.Points.Length, "the flat pairs become waypoints");
            Assert.AreEqual(new Vector2(0f, 3f), panel.Points[1], "offsets from the authored rect");
        }

        [Test]
        public void APanelWithNoPath_IsStillAMask()
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(new LevelPanel { id = "p01", rect = new[] { 10f, 1f, 4f, 1f } }),
                              world, out _, out _);

            Assert.IsFalse(world.Panels[0].IsMoving);
        }

        [Test]
        public void HoldSecondsBecomeStepsAtLoad()
        {
            LevelPanel data = Riser();
            data.hold = 0.5f;

            var world = new SimWorld();
            LevelLoader.Apply(Room(data), world, out _, out _);

            Assert.AreEqual(30, world.Panels[0].HoldSteps, "0.5 s at 60 Hz");
        }

        [Test]
        public void AMovingPanelTravelsFromItsRunStep_AndItsRectFollows()
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(Riser()), world, out _, out _);

            SimPanel panel = world.Panels[0];
            panel.Run(0);

            panel.Place(0);
            Assert.AreEqual(new Rect(10f, 1f, 4f, 1f), panel.WorldRect, "starts at its authored rect");

            panel.Place(30);
            Assert.AreEqual(4f, panel.WorldRect.yMin, 1e-3f, "3 tiles at 6 tiles/s takes 0.5 s");
        }

        [Test]
        public void VelocityIsDerivedFromTwoEvaluations_NeverAccumulated()
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(Riser()), world, out _, out _);

            SimPanel panel = world.Panels[0];
            panel.Run(0);
            panel.Place(10);

            Assert.AreEqual(6f, panel.Velocity.y, 1e-3f, "tiles per second, up");
            Assert.AreEqual(0f, panel.Velocity.x, 1e-6f);

            panel.Place(200);
            Assert.AreEqual(0f, panel.Velocity.y, 1e-3f, "parked at the end of a once path");
        }

        [Test]
        public void Return_WalksBackFromWhereItIs_WithoutSnapping()
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(Riser()), world, out _, out _);

            SimPanel panel = world.Panels[0];
            panel.Run(0);
            panel.Place(15);

            float half = panel.WorldRect.yMin;
            Assert.AreEqual(2.5f, half, 1e-3f, "half way up after 15 steps");

            panel.Return(15);
            panel.Place(15);
            Assert.AreEqual(half, panel.WorldRect.yMin, 1e-3f, "the return starts where it stood");

            panel.Place(30);
            Assert.AreEqual(1f, panel.WorldRect.yMin, 1e-3f, "and is home 15 steps later");

            panel.Place(90);
            Assert.AreEqual(1f, panel.WorldRect.yMin, 1e-3f, "then stays home rather than running backwards");
        }

        [Test]
        public void Restart_PutsAMovingPanelBackAtItsAuthoredRect()
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(Riser()), world, out _, out _);
            world.Configure(new Vector2(6f, 13f), Vector2.zero, new Vector2(4f, 2f), null, 1);

            SimPanel panel = world.Panels[0];
            panel.Run(0);
            panel.Place(30);
            Assert.AreEqual(4f, panel.WorldRect.yMin, 1e-3f);

            world.Restart();

            Assert.AreEqual(1f, panel.WorldRect.yMin, 1e-3f);
            Assert.AreEqual(0, panel.RunStep);
            Assert.AreEqual(1, panel.Direction);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./test.sh`
Expected: FAIL — `'LevelPanel' does not contain a definition for 'path'`, `'SimPanel' does not contain a definition for 'IsMoving'`.

- [ ] **Step 3: Write minimal implementation**

In `Sim/Level/LevelData.cs`, add to `LevelPanel` after `exit_dir`:

```csharp
        /// <summary>
        /// Waypoints as a FLAT list of x,y pairs of tile OFFSETS from <see cref="rect"/>:
        /// [0,3, 0,0] is "up three, then home". Offsets rather than absolute positions so
        /// moving the panel in the editor cannot invalidate its path.
        ///
        /// A panel with fewer than two waypoints is the mask panel that has always existed.
        /// Flattened because JsonUtility cannot serialise nested arrays (see LevelEntity.path).
        /// </summary>
        public float[] path = new float[0];

        /// <summary>pingpong | loop | once</summary>
        public string path_mode = "once";

        /// <summary>Tiles per second along the path.</summary>
        public float speed;

        /// <summary>Seconds paused at EVERY waypoint, both ends included.</summary>
        public float hold;

        /// <summary>0..1 offset so two identical panels can be desynced, as an entity's is.</summary>
        public float phase;
```

In `Sim/Panels/SimPanel.cs`, replace the class body's config/run-state sections with:

```csharp
        public string Id;

        /// <summary>Tile coordinates. A panel is always whole tiles — the mask indexes cells.</summary>
        public RectInt Rect;

        /// <summary>
        /// Which way its tiles leave, and come back from, when it is switched rather than
        /// travelled. Pathless panels only: a moving panel's tiles are not debris.
        /// </summary>
        public PanelDirection ExitDir = PanelDirection.Down;

        // --- Path config: set at load, never mutated by the tick ---

        /// <summary>Offsets from <see cref="Rect"/>, the panel's own zero first.</summary>
        public Vector2[] Points = { Vector2.zero };

        public PathMode Mode = PathMode.Once;
        public float Speed;
        public int HoldSteps;
        public float Phase;

        /// <summary>
        /// A panel with waypoints is a moving solid; one without is a mask over grid cells.
        /// One field decides, so there is no mode flag to get out of sync with the level.
        /// </summary>
        public bool IsMoving => Points != null && Points.Length > 1;

        // --- Run state: cleared by Reset(), covered by the replay-equality test ---

        /// <summary>Whether its tiles are currently present.</summary>
        public bool Enabled = true;

        /// <summary>What Enabled returns to on a restart.</summary>
        public bool StartEnabled = true;

        /// <summary>
        /// The step this travel is measured from. Forward, elapsed is step - RunStep;
        /// returning, it is RunStep - step, which is why Return() rebases rather than
        /// storing a second counter.
        /// </summary>
        public int RunStep;

        /// <summary>+1 running, -1 returning.</summary>
        public int Direction = 1;

        /// <summary>Offset from the authored rect, as of the last Place().</summary>
        public Vector2 Offset;

        /// <summary>Tiles per second, as of the last Place(). Derived, never integrated.</summary>
        public Vector2 Velocity;

        /// <summary>Where the panel actually is this step, in metres.</summary>
        public Rect WorldRect => new Rect(Rect.x + Offset.x, Rect.y + Offset.y,
                                          Rect.width, Rect.height);

        public void Reset()
        {
            Enabled = StartEnabled;
            RunStep = 0;
            Direction = 1;
            Offset = Vector2.zero;
            Velocity = Vector2.zero;
        }

        /// <summary>
        /// Places the panel for this step, and derives the velocity from the next step's
        /// position rather than integrating one. Pure: safe to call for any step, in any
        /// order, exactly as SimEntity.Place is.
        /// </summary>
        public void Place(int step)
        {
            if (!IsMoving) { Offset = Vector2.zero; Velocity = Vector2.zero; return; }

            Offset = At(step);
            Velocity = (At(step + 1) - Offset) / SimClock.Dt;
        }

        Vector2 At(int step)
        {
            int elapsed = Direction >= 0 ? step - RunStep : RunStep - step;
            if (elapsed < 0) elapsed = 0;

            return PanelPath.Evaluate(Points, Mode, Speed, Phase, HoldSteps, elapsed);
        }

        /// <summary>Sends the panel out along its path from this step.</summary>
        public void Run(int step)
        {
            Direction = 1;
            RunStep = step;
        }

        /// <summary>
        /// Walks the path back from wherever the panel currently is. RunStep is rebased to
        /// step + elapsed, so At() counts down to zero from here instead of snapping the
        /// panel to an end — the same correction SimEntity.ArmedStep exists to make.
        /// </summary>
        public void Return(int step)
        {
            int elapsed = Direction >= 0 ? step - RunStep : RunStep - step;
            if (elapsed < 0) elapsed = 0;

            Direction = -1;
            RunStep = step + elapsed;
        }
```

Add `using KeepyUppy.Sim.Entities;` to the top of `SimPanel.cs`.

In `Sim/Level/LevelLoader.cs`, inside `LoadPanels`'s `panels.Add(new SimPanel { … })`, add after `ExitDir`:

```csharp
                    Points = EntityPath.FromFlat(data.path),
                    Mode = ParsePathMode(data.path_mode),
                    Speed = data.speed,
                    HoldSteps = SimClock.StepsIn(data.hold),
                    Phase = data.phase,
```

`ParsePathMode` already exists in `LevelLoader` for entities — reuse it; if it is private to the entity path, leave it exactly where it is and call it.

- [ ] **Step 4: Run test to verify it passes**

Run: `./test.sh`
Expected: PASS. `Restart_PutsAMovingPanelBackAtItsAuthoredRect` also requires `PanelField.Reset` to call `panel.Reset()` rather than assigning `Enabled` directly — change it to:

```csharp
        public void Reset()
        {
            for (int i = 0; i < _panels.Count; i++) _panels[i].Reset();

            TurnedOnThisStep.Clear();
            Dirty = true;
        }
```

- [ ] **Step 5: Extend the format round-trip**

In `Assets/_Project/Tests/EditMode/LevelFormatTests.cs`, `Panels_RoundTrip`, change the `p02` entry to carry a path and assert it survives:

```csharp
                new LevelPanel
                {
                    id = "p02", rect = new[] { 20f, 9f, 3f, 1f }, start_state = "off", exit_dir = "up",
                    path = new[] { 0f, 0f, 0f, 3f }, path_mode = "pingpong", speed = 6f, hold = 0.5f,
                },
```

and after the existing assertions:

```csharp
            Assert.AreEqual(new[] { 0f, 0f, 0f, 3f }, loaded.panels[1].path);
            Assert.AreEqual("pingpong", loaded.panels[1].path_mode);
            Assert.AreEqual(6f, loaded.panels[1].speed, 1e-4f);
            Assert.AreEqual(0.5f, loaded.panels[1].hold, 1e-4f);
            Assert.AreEqual(0, loaded.panels[0].path.Length, "a pathless panel round-trips as one");
```

- [ ] **Step 6: Run the tests**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode
git commit -m "feat: panels can carry a path, speed, dwell and phase"
```

---

### Task 3: `SolidField` — moving panels leave the mask and are published

**Files:**
- Create: `Assets/_Project/Scripts/Sim/Panels/SolidField.cs`
- Modify: `Assets/_Project/Scripts/Sim/Panels/PanelField.cs`
- Modify: `Assets/_Project/Scripts/Sim/SimWorld.cs`
- Test: `Assets/_Project/Tests/EditMode/MovingPanelTests.cs`

**Interfaces:**
- Consumes: `SimPanel.IsMoving/Place/WorldRect/Velocity/Enabled` (Task 2).
- Produces: `SolidField` with `Count`, `this[int]` returning `MovingSolid { Rect rect; Vector2 velocity; int panel; }`, `Clear()`, `Rebuild(PanelField panels)`; `PanelField.Advance(int step)`; `SimWorld.Solids`.

- [ ] **Step 1: Write the failing test**

Append to `MovingPanelTests.cs`:

```csharp
        [Test]
        public void AMovingPanelsTiles_AreNotInTheMask_AndAreNotSolidWhereTheyWereAuthored()
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(Riser()), world, out _, out _);
            world.Panels.Advance(0);
            world.Panels.Apply(world.Grid);

            Assert.IsTrue(world.Grid.IsMasked(10, 1),
                          "its tiles left the grid at load — the panel carries them now");
            Assert.AreEqual(TileKind.Solid, world.Grid.BaseAt(10, 1),
                            "but the authored rows are untouched, as with any mask");
        }

        [Test]
        public void TheSolidFieldCarriesEveryEnabledMovingPanel_InListOrder()
        {
            LevelPanel second = Riser();
            second.id = "p02";
            second.rect = new[] { 20f, 1f, 2f, 1f };

            var world = new SimWorld();
            LevelLoader.Apply(Room(Riser(), second), world, out _, out _);

            world.Panels[0].Run(0);
            world.Panels.Advance(30);
            world.Solids.Rebuild(world.Panels);

            Assert.AreEqual(2, world.Solids.Count);
            Assert.AreEqual(0, world.Solids[0].panel, "list order, never a dictionary");
            Assert.AreEqual(4f, world.Solids[0].rect.yMin, 1e-3f, "the one that ran has moved");
            Assert.AreEqual(1f, world.Solids[1].rect.yMin, 1e-3f, "the one that did not has not");
            Assert.AreEqual(6f, world.Solids[0].velocity.y, 1e-3f);
        }

        [Test]
        public void ASwitchedOffMovingPanel_IsNotASolidAnywhere()
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(Riser()), world, out _, out _);

            world.Panels.SetEnabled("p01", false);
            world.Panels.Advance(0);
            world.Solids.Rebuild(world.Panels);

            Assert.AreEqual(0, world.Solids.Count, "off means it is not a solid, not that it is parked");
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./test.sh`
Expected: FAIL — `'PanelField' does not contain a definition for 'Advance'`, `'SimWorld' does not contain a definition for 'Solids'`.

- [ ] **Step 3: Write minimal implementation**

Create `Assets/_Project/Scripts/Sim/Panels/SolidField.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>One moving panel, as the sweeps see it this step.</summary>
    public struct MovingSolid
    {
        /// <summary>Where it is now, in metres.</summary>
        public Rect rect;

        /// <summary>Tiles per second this step. Zero for a panel that is parked.</summary>
        public Vector2 velocity;

        /// <summary>Index into PanelField, so a hit can be traced back to its tiles.</summary>
        public int panel;
    }

    /// <summary>
    /// The moving panels, in the form the swept collision reads them (moving-panels design).
    ///
    /// A List, never a Dictionary: iteration order decides which solid a body resolves
    /// against first when two overlap it on the same step, and unordered iteration is on
    /// CLAUDE.md's list of the things that break GDD 6.1 silently.
    ///
    /// Rebuilt from the panels each step rather than mutated in place, for the same reason
    /// PanelField rebuilds its mask: there is then no per-solid bookkeeping that a restart
    /// can leave half-undone.
    /// </summary>
    public sealed class SolidField
    {
        readonly List<MovingSolid> _solids = new List<MovingSolid>();

        public int Count => _solids.Count;
        public MovingSolid this[int i] => _solids[i];

        public void Clear() => _solids.Clear();

        /// <summary>Republishes every enabled moving panel at its current position.</summary>
        public void Rebuild(PanelField panels)
        {
            _solids.Clear();
            if (panels == null) return;

            for (int i = 0; i < panels.Count; i++)
            {
                SimPanel panel = panels[i];

                // A switched-off panel is not a solid anywhere — "off" means its tiles are
                // absent, exactly as it does for a mask panel.
                if (!panel.IsMoving || !panel.Enabled) continue;

                _solids.Add(new MovingSolid
                {
                    rect = panel.WorldRect,
                    velocity = panel.Velocity,
                    panel = i,
                });
            }
        }
    }
}
```

In `Sim/Panels/PanelField.cs`, add:

```csharp
        /// <summary>
        /// Places every moving panel for this step. Called once per tick, before anything
        /// reads a panel's position — the sweeps, the shove pass and the view all do.
        /// </summary>
        public void Advance(int step)
        {
            for (int i = 0; i < _panels.Count; i++)
                if (_panels[i].IsMoving) _panels[i].Place(step);
        }
```

and change `Apply` so a moving panel's tiles are always masked out of the grid, whether it is on or off — the panel carries them now:

```csharp
        public void Apply(TileGrid grid)
        {
            if (grid == null) return;

            grid.ClearMask();

            for (int i = 0; i < _panels.Count; i++)
            {
                SimPanel panel = _panels[i];

                // A moving panel's tiles left the grid at load: they travel with the panel,
                // and a copy of them sitting at the authored rect would be a second, static
                // solid nobody authored.
                if (panel.IsMoving || !panel.Enabled) grid.SetMasked(panel.Rect, true);
            }

            Dirty = false;
        }
```

In `Sim/SimWorld.cs`, beside `Panels`:

```csharp
        /// <summary>Moving panels, as the sweeps read them (moving-panels design).</summary>
        public readonly SolidField Solids = new SolidField();
```

and in `Restart()`, after `Panels.Reset();`:

```csharp
            Panels.Advance(0);
            Solids.Rebuild(Panels);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `./test.sh`
Expected: PASS, `failed=0`. Existing panel tests must stay green — pathless panels take an unchanged route through `Apply`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode
git commit -m "feat: SolidField publishes moving panels for collision"
```

---

### Task 4: The player sweeps against moving panels, and rides them

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/SweepResult.cs`
- Modify: `Assets/_Project/Scripts/Sim/SweptAabb.cs`
- Modify: `Assets/_Project/Scripts/Sim/PlayerBody.cs`
- Modify: `Assets/_Project/Scripts/Sim/SimWorld.cs` (`StepPlayer`)
- Test: `Assets/_Project/Tests/EditMode/MovingPanelTests.cs`

**Interfaces:**
- Consumes: `SolidField`, `MovingSolid` (Task 3).
- Produces: `SweepResult.carrier` (int, −1 for none); `SweptAabb.Move(TileGrid grid, SolidField solids, float halfWidth, float halfHeight, ref Vector2 pos, ref Vector2 vel, float dt)`; `PlayerBody.Step(in PlayerTuning t, IInputSource input, TileGrid grid, SolidField solids)`.

- [ ] **Step 1: Write the failing test**

Append to `MovingPanelTests.cs`:

```csharp
        static SimWorld PlayableRoom(LevelPanel panel, Vector2 playerSpawn)
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(panel), world, out _, out _);
            world.FloorKillsBalloon = false;
            world.Configure(new Vector2(2f, 14f), Vector2.zero, playerSpawn, null, 1);
            return world;
        }

        [Test]
        public void AMovingPanelBlocksThePlayer_LikeAnyOtherFloor()
        {
            // Panel spans x 10..14 at y 1..2; the player is dropped onto it from above.
            SimWorld world = PlayableRoom(Riser(), new Vector2(12f, 6f));

            world.Tick(60);

            Assert.IsTrue(world.Player.Grounded, "landed on the panel, not fallen through it");
            Assert.AreEqual(2f + world.Player.HalfHeight, world.Player.Position.y, 0.05f);
        }

        [Test]
        public void APlayerStandingOnARisingPanel_IsCarriedUp()
        {
            SimWorld world = PlayableRoom(Riser(), new Vector2(12f, 6f));
            world.Tick(60);

            float before = world.Player.Position.y;
            world.Panels[0].Run(world.Clock.Step);

            world.Tick(30);

            Assert.AreEqual(before + 3f, world.Player.Position.y, 0.1f,
                            "carried the panel's full three tiles");
            Assert.IsTrue(world.Player.Grounded, "and is still standing on it");
        }

        [Test]
        public void CarryIsDisplacement_NotVelocity()
        {
            SimWorld world = PlayableRoom(Riser(), new Vector2(12f, 6f));
            world.Tick(60);
            world.Panels[0].Run(world.Clock.Step);
            world.Tick(10);

            Assert.AreEqual(0f, world.Player.Velocity.y, 0.01f,
                            "a rising floor must not put velocity into the player, or a jump "
                            + "taken from it would go higher than one taken from the world");
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./test.sh`
Expected: FAIL — the player falls through the panel: `Grounded` is false and `Position.y` is far below.

- [ ] **Step 3: Write minimal implementation**

In `Sim/SweepResult.cs`, add:

```csharp
        /// <summary>
        /// Index into SolidField of the moving panel this sweep ended against, or -1. This is
        /// what lets the step after the sweep tell "stood on a moving floor" from "stood on
        /// the world" without re-testing every solid.
        /// </summary>
        public int carrier;
```

In `Sim/SweptAabb.cs`:

1. Add the overload and thread `solids` through `Move` and `Cast`:

```csharp
        /// <summary>Grid only. The rooms with no moving panels, and every existing caller.</summary>
        public static SweepResult Move(TileGrid grid, float halfWidth, float halfHeight,
                                       ref Vector2 pos, ref Vector2 vel, float dt)
            => Move(grid, null, halfWidth, halfHeight, ref pos, ref vel, dt);

        public static SweepResult Move(TileGrid grid, SolidField solids,
                                       float halfWidth, float halfHeight,
                                       ref Vector2 pos, ref Vector2 vel, float dt)
```

2. Initialise `result.carrier = -1;` where `result` is created, and set it on each contact
   (`result.carrier = solidIndex;`, `-1` when the hit was a tile).

3. After the tile loop in `Cast`, add the moving-panel pass. A solid is cast in its own
   frame — the body's velocity minus the panel's — and the earliest hit across both sources
   wins:

```csharp
            if (solids != null)
            {
                for (int i = 0; i < solids.Count; i++)
                {
                    MovingSolid solid = solids[i];

                    // Relative velocity: resolving in the panel's frame is what makes a
                    // panel moving INTO the body a contact rather than a miss.
                    Vector2 rel = new Vector2(d.x - solid.velocity.x * dtScale,
                                              d.y - solid.velocity.y * dtScale);

                    if (!SweepRect(p, rel, hx, hy, solid.rect, out float ht, out Vector2 hn)) continue;
                    if (ht >= t) continue;

                    t = ht;
                    normal = hn;
                    kind = TileKind.Solid;
                    solidIndex = i;
                    found = true;
                }
            }
```

   where `dtScale` is the fraction of the step this cast covers (pass `timeLeft / dt` in from
   `Move`; it is 1 on the first iteration), and `SweepRect` is `SweepTile` generalised from a
   unit cell to an arbitrary `Rect` — extract the existing body of `SweepTile` to take
   `float ex0, ey0, ex1, ey1` and have both call it.

4. In `Move`, after resolving a contact against a solid, add the panel's motion back so the
   body ends the step in the world frame:

```csharp
                if (result.carrier >= 0)
                {
                    Vector2 carry = solids[result.carrier].velocity;
                    pos = new Vector2(pos.x + carry.x * timeLeft, pos.y + carry.y * timeLeft);
                }
```

In `Sim/PlayerBody.cs`, change `Step` to take the field and pass it through, keeping the old
signature as an overload so existing callers and tests compile:

```csharp
        public SweepResult Step(in PlayerTuning t, IInputSource input, TileGrid grid)
            => Step(t, input, grid, null);

        public SweepResult Step(in PlayerTuning t, IInputSource input, TileGrid grid, SolidField solids)
```

and inside, replace the sweep call with:

```csharp
            SweepResult result = SweptAabb.Move(grid, solids, HalfWidth, HalfHeight,
                                                ref pos, ref vel, SimClock.Dt);
```

In `Sim/SimWorld.StepPlayer`, pass the field:

```csharp
            Player.Step(PlayerTuning, Input, Grid, Solids);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `./test.sh`
Expected: PASS. If `CarryIsDisplacement_NotVelocity` fails, something is writing the carry into `vel` — the carry must only ever touch `pos`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode
git commit -m "feat: the player collides with and rides moving panels"
```

---

### Task 5: Shove and crush

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/SimWorld.cs`
- Test: `Assets/_Project/Tests/EditMode/MovingPanelTests.cs`

**Interfaces:**
- Consumes: `SolidField` (Task 3), `SimWorld.Kill(DeathCause)`, `DeathCause.Crushed` (both existing).
- Produces: `SimWorld.ResolveMovingPanels()` — public, so the tests can drive it without a trigger, exactly as `CrushBodiesInReturningPanels` is.

- [ ] **Step 1: Write the failing test**

Append to `MovingPanelTests.cs`:

```csharp
        /// <summary>A panel that travels sideways into whatever is standing next to it.</summary>
        static LevelPanel Pusher() => new LevelPanel
        {
            id = "p01",
            rect = new[] { 10f, 1f, 1f, 2f },
            path = new[] { 0f, 0f, 6f, 0f },
            path_mode = "once",
            speed = 4f,
            hold = 0f,
        };

        [Test]
        public void APanelMovingIntoThePlayer_ShovesThem()
        {
            SimWorld world = PlayableRoom(Pusher(), new Vector2(12f, 2f));
            world.Tick(30);

            float before = world.Player.Position.x;
            world.Panels[0].Run(world.Clock.Step);
            world.Tick(30);

            Assert.Greater(world.Player.Position.x, before + 1f, "pushed along, not passed through");
        }

        [Test]
        public void BeingShoved_DoesNotChangeThePlayersVelocity()
        {
            SimWorld world = PlayableRoom(Pusher(), new Vector2(12f, 2f));
            world.Tick(30);
            world.Panels[0].Run(world.Clock.Step);
            world.Tick(15);

            Assert.AreEqual(0f, world.Player.Velocity.x, 0.01f,
                            "a shove is a change of position; letting it inject velocity turns "
                            + "a push into a launch");
        }

        [Test]
        public void APlayerPinnedBetweenAPanelAndTheWall_IsCrushed()
        {
            // The room's right wall is at x = 31; the panel runs six tiles into it.
            LevelPanel pusher = Pusher();
            pusher.rect = new[] { 24f, 1f, 1f, 2f };

            SimWorld world = PlayableRoom(pusher, new Vector2(30.5f, 2f));
            world.Tick(30);

            world.Panels[0].Run(world.Clock.Step);
            world.Tick(60);

            Assert.IsTrue(world.IsDead);
            Assert.AreEqual(DeathCause.Crushed, world.LastDeath);
        }

        [Test]
        public void ACrushKillsBothBodiesInTheSameStep()
        {
            LevelPanel pusher = Pusher();
            pusher.rect = new[] { 24f, 1f, 1f, 2f };

            SimWorld world = PlayableRoom(pusher, new Vector2(30.5f, 2f));
            world.Tick(30);
            world.Panels[0].Run(world.Clock.Step);
            world.Tick(60);

            Assert.IsTrue(world.Player.Popped);
            Assert.IsTrue(world.Balloon.Popped, "GDD 4.1: linked fate, same step, no exceptions");
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./test.sh`
Expected: FAIL — the panel passes through the player; `Position.x` is unchanged and nothing dies.

- [ ] **Step 3: Write minimal implementation**

In `Sim/SimWorld.cs`, add:

```csharp
        /// <summary>
        /// Displaces bodies that a moving panel has moved into, and kills the ones it cannot
        /// place clear (moving-panels design).
        ///
        /// Runs after the panels are placed and before either body steps, so a body is never
        /// left inside geometry that arrived in the same step — the same rule that puts
        /// trigger effects on the following step.
        ///
        /// The shove moves position only. Letting a panel write velocity into the player
        /// turns every push into a launch, and makes the outcome depend on how long contact
        /// happened to last.
        ///
        /// Public so the tests can drive it without a trigger, as CrushBodiesInReturningPanels is.
        /// </summary>
        public void ResolveMovingPanels()
        {
            if (IsDead) return;

            for (int i = 0; i < Solids.Count; i++)
            {
                MovingSolid solid = Solids[i];
                if (solid.velocity.sqrMagnitude <= 0f) continue;

                if (HasPlayer && ShoveBox(solid, ref Player.Position,
                                          Player.HalfWidth, Player.HalfHeight))
                {
                    Kill(DeathCause.Crushed);
                    return;
                }

                if (ShoveCircle(solid, ref Balloon.Position, Balloon.Radius))
                {
                    Kill(DeathCause.Crushed);
                    return;
                }
            }
        }

        /// <summary>
        /// Pushes a box clear along the panel's motion axis. True if it cannot be placed —
        /// static geometry behind it — which is a crush.
        /// </summary>
        bool ShoveBox(in MovingSolid solid, ref Vector2 centre, float hx, float hy)
        {
            if (!Overlaps(solid.rect, centre, hx, hy)) return false;

            Vector2 pushed = PushOut(solid, centre, hx, hy);
            if (BoxHitsGeometry(pushed, hx, hy)) return true;

            centre = pushed;
            return false;
        }

        bool ShoveCircle(in MovingSolid solid, ref Vector2 centre, float radius)
        {
            if (!Overlaps(solid.rect, centre, radius, radius)) return false;

            Vector2 pushed = PushOut(solid, centre, radius, radius);
            if (BoxHitsGeometry(pushed, radius, radius)) return true;

            centre = pushed;
            return false;
        }

        /// <summary>Clear of the panel's leading face, along the axis it is travelling.</summary>
        static Vector2 PushOut(in MovingSolid solid, Vector2 centre, float hx, float hy)
        {
            bool horizontal = Mathf.Abs(solid.velocity.x) >= Mathf.Abs(solid.velocity.y);

            if (horizontal)
            {
                float x = solid.velocity.x > 0f
                    ? solid.rect.xMax + hx
                    : solid.rect.xMin - hx;

                return new Vector2(x, centre.y);
            }

            float y = solid.velocity.y > 0f
                ? solid.rect.yMax + hy
                : solid.rect.yMin - hy;

            return new Vector2(centre.x, y);
        }

        static bool Overlaps(Rect rect, Vector2 centre, float hx, float hy)
            => centre.x + hx > rect.xMin && centre.x - hx < rect.xMax &&
               centre.y + hy > rect.yMin && centre.y - hy < rect.yMax;

        /// <summary>
        /// Whether a body placed here would be inside static geometry. Tiles only: a body
        /// pushed into a second moving panel is left to the next step's shove, which is the
        /// same panel-versus-panel case the validator warns about rather than resolving.
        /// </summary>
        bool BoxHitsGeometry(Vector2 centre, float hx, float hy)
        {
            int x0 = Mathf.FloorToInt(centre.x - hx);
            int x1 = Mathf.FloorToInt(centre.x + hx);
            int y0 = Mathf.FloorToInt(centre.y - hy);
            int y1 = Mathf.FloorToInt(centre.y + hy);

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!TileGrid.InBounds(x, y)) return true;      // the room bound counts as a wall
                if (Grid[x, y] == TileKind.Empty) continue;
                if (BoxOverlapsTile(centre, hx, hy, x, y)) return true;
            }

            return false;
        }
```

In `Tick()`, place the panel work before the entities step, replacing the line
`Entities.Step(step, Air);` with:

```csharp
            // Panels move before anything else, and what their motion did to a body is
            // resolved before that body gets a step of its own.
            Panels.Advance(step);
            Solids.Rebuild(Panels);
            ResolveMovingPanels();

            if (IsDead) { Clock.Advance(); return; }

            // Hazards move before either body does, so a saw and the balloon it catches are
            // contemporaries rather than a frame apart.
            Entities.Step(step, Air);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode
git commit -m "feat: moving panels shove bodies and crush what they cannot place"
```

---

### Task 6: The balloon — blocked, batted, and popped by carried spikes

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/HitSource.cs`
- Modify: `Assets/_Project/Scripts/Sim/BalloonTuning.cs`
- Modify: `Assets/_Project/Scripts/Sim/SweptCircle.cs`
- Modify: `Assets/_Project/Scripts/Sim/Hazards.cs`
- Modify: `Assets/_Project/Scripts/Sim/SimWorld.cs` (`StepBalloon`)
- Test: `Assets/_Project/Tests/EditMode/MovingPanelContactTests.cs` (create)

**Interfaces:**
- Consumes: `HitResolver.ApplyHit(HitSource, Vector2, float, float)`, `SolidField`, `SimPanel.Rect/Offset`.
- Produces: `HitSource.Panel`; `BalloonTuning.panelHitPower`; `SweptCircle.Move(TileGrid, SolidField, in BalloonTuning, float, ref Vector2, ref Vector2, float)`; `Hazards.MovingPanelKills(TileGrid grid, PanelField panels, SolidField solids, Vector2 centre, float radius, float shrink, bool playerOnly)`.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/MovingPanelContactTests.cs`:

```csharp
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// What a moving panel does to the balloon (moving-panels design).
    ///
    /// A moving face adds energy through ApplyHit, because that is the one door every hit
    /// goes through (GDD 5.1). A stationary one is passive contact and loses energy, which is
    /// what stops a parked panel being farmed (GDD 4.2).
    /// </summary>
    public sealed class MovingPanelContactTests
    {
        static LevelData Room(LevelPanel panel, string[] extraRows = null)
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 4, 4, 1, TileKind.Solid);

            return new LevelData { tiles = grid.ToRows(), panels = new[] { panel } };
        }

        static LevelPanel Riser() => new LevelPanel
        {
            id = "p01",
            rect = new[] { 10f, 4f, 4f, 1f },
            path = new[] { 0f, 0f, 0f, 6f },
            path_mode = "once",
            speed = 8f,
            hold = 0f,
        };

        static SimWorld World(LevelPanel panel, Vector2 balloonAt)
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(panel), world, out _, out _);
            world.FloorKillsBalloon = false;
            world.Configure(balloonAt, Vector2.zero, new Vector2(2f, 2f), null, 1);
            return world;
        }

        [Test]
        public void AStationaryPanelIsAWall_AndTheBalloonRestsOnIt()
        {
            SimWorld world = World(Riser(), new Vector2(12f, 8f));

            world.Tick(120);

            Assert.Greater(world.Balloon.Position.y, 5f, "came to rest on the panel, not through it");
        }

        [Test]
        public void APanelRisingIntoTheBalloon_BatsIt()
        {
            SimWorld world = World(Riser(), new Vector2(12f, 8f));
            world.Tick(120);

            float restingY = world.Balloon.Position.y;
            world.Panels[0].Run(world.Clock.Step);
            world.Tick(20);

            Assert.Greater(world.Balloon.Velocity.y, 1f,
                           "a moving face adds energy — that is the launcher this feature is for");
            Assert.Greater(world.Balloon.Position.y, restingY + 1f);
        }

        [Test]
        public void AStationaryPanelNeverAddsEnergy()
        {
            SimWorld world = World(Riser(), new Vector2(12f, 8f));
            world.Tick(120);

            float first = world.Balloon.Position.y;
            world.Tick(120);

            Assert.LessOrEqual(world.Balloon.Position.y, first + 0.05f,
                               "GDD 4.2: passive contact loses energy, so this must settle, "
                               + "never climb");
        }

        [Test]
        public void ASpikeCarriedByAMovingPanel_PopsTheBalloonMidTravel()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 4, 4, 1, TileKind.SpikeUp);

            LevelPanel panel = Riser();

            var world = new SimWorld();
            LevelLoader.Apply(new LevelData { tiles = grid.ToRows(), panels = new[] { panel } },
                              world, out _, out _);
            world.FloorKillsBalloon = false;
            world.Configure(new Vector2(12f, 9f), Vector2.zero, new Vector2(2f, 2f), null, 1);

            world.Panels[0].Run(0);
            world.Tick(40);

            Assert.IsTrue(world.IsDead);
            Assert.AreEqual(DeathCause.BalloonHitHazard, world.LastDeath,
                            "a spike on a retracting wall is lethal for the whole retraction");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./test.sh`
Expected: FAIL — the balloon falls straight through the panel; `AStationaryPanelIsAWall…` reports a `Position.y` near the floor.

- [ ] **Step 3: Write minimal implementation**

In `Sim/HitSource.cs`, add:

```csharp
        /// <summary>
        /// A panel face that is moving toward the balloon (moving-panels design). Passive
        /// contact with a parked one is PassiveContact, so a stationary panel can never be
        /// farmed for energy (GDD 4.2).
        /// </summary>
        Panel = 4,
```

In `Sim/BalloonTuning.cs`, add beside the other hit speeds, and set it in `Default`:

```csharp
        /// <summary>
        /// Metres per second the balloon leaves a moving panel face with, per tile/second of
        /// panel speed along the normal. A slider like every other GDD 6.4 number.
        /// </summary>
        public float panelHitPower;
```

In `HitResolver.BaseSpeed`, return `1f` for `HitSource.Panel` — the power argument already
carries the speed, so the source's base must not scale it a second time.

In `Sim/SweptCircle.cs`, mirror Task 4's structure: add a `SolidField solids` parameter with a
grid-only overload delegating with `null`, cast each solid in its own frame with the relative
velocity, and report `carrier`. The balloon's tile path is not otherwise touched.

In `Sim/Hazards.cs`, add:

```csharp
        /// <summary>
        /// Lethal tiles carried by a moving panel (moving-panels design).
        ///
        /// The panel's tiles are read from the grid's authored rows through BaseAt — they are
        /// masked out of the live grid, because the panel carries them — and tested at their
        /// offset positions.
        /// </summary>
        public static bool MovingPanelKills(TileGrid grid, PanelField panels, SolidField solids,
                                            Vector2 centre, float radius, float shrink,
                                            bool playerOnly)
        {
            for (int i = 0; i < solids.Count; i++)
            {
                MovingSolid solid = solids[i];
                RectInt home = panels[solid.panel].Rect;
                Vector2 offset = new Vector2(solid.rect.xMin - home.x, solid.rect.yMin - home.y);

                for (int y = home.yMin; y < home.yMax; y++)
                for (int x = home.xMin; x < home.xMax; x++)
                {
                    TileKind kind = grid.BaseAt(x, y);
                    if (kind == TileKind.Empty) continue;
                    if (playerOnly ? !KillsPlayer(kind) : !KillsBalloon(kind)) continue;

                    var tile = new Rect(x + offset.x, y + offset.y, 1f, 1f);

                    float dx = centre.x - Mathf.Clamp(centre.x, tile.xMin + shrink, tile.xMax - shrink);
                    float dy = centre.y - Mathf.Clamp(centre.y, tile.yMin + shrink, tile.yMax - shrink);

                    if (dx * dx + dy * dy < radius * radius) return true;
                }
            }

            return false;
        }
```

In `Sim/SimWorld.StepBalloon`, sweep against the solids and turn a moving face into a hit:

```csharp
            SweepResult result = SweptCircle.Move(Grid, Solids, Tuning, Balloon.Radius,
                                                  ref pos, ref vel, SimClock.Dt);
            Balloon.Position = pos;
            Balloon.Velocity = vel;
            LastContacts = result.contacts;

            // A face travelling toward the balloon adds energy; a parked one has already
            // rebounded passively inside the sweep (GDD 4.2). ApplyHit is the only door
            // (GDD 5.1), and it SETS velocity rather than adding to it.
            if (result.carrier >= 0)
            {
                Vector2 face = Solids[result.carrier].velocity;
                float closing = Vector2.Dot(face, result.lastNormal);

                if (closing > 0f)
                    Hits.ApplyHit(HitSource.Panel, result.lastNormal,
                                  closing * Tuning.panelHitPower, 0f);
            }
```

and after the existing tile-hazard check:

```csharp
            if (Hazards.MovingPanelKills(Grid, Panels, Solids, Balloon.Position, Balloon.Radius,
                                         Tuning.hazardPopRadiusShrink, playerOnly: false))
            {
                Kill(DeathCause.BalloonHitHazard);
                return;
            }
```

Add the player's equivalent in `StepPlayer`, after its tile-hazard check, using the player's
half-extents as the radius pair and `playerOnly: true` — a saw blade and a spike kill the
player, a heat tile does not.

- [ ] **Step 4: Run test to verify it passes**

Run: `./test.sh`
Expected: PASS. If `APanelRisingIntoTheBalloon_BatsIt` gives a tiny velocity, `panelHitPower` in
`BalloonTuning.Default` is too low — start at `0.8` and tune by playtest, not by test.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode
git commit -m "feat: the balloon collides with, is batted by and dies to moving panels"
```

---

### Task 7: `run` and `return` link actions

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Triggers/SimLink.cs` (enum `LinkAction`)
- Modify: `Assets/_Project/Scripts/Sim/Level/TriggerFactory.cs` (`ParseAction`)
- Modify: `Assets/_Project/Scripts/Sim/Triggers/TriggerField.cs` (`Apply`)
- Modify: `Assets/_Project/Scripts/Sim/Entities/SimEntity.cs`
- Test: `Assets/_Project/Tests/EditMode/TriggerLoadTests.cs`, `Assets/_Project/Tests/EditMode/MovingPanelTests.cs`

**Interfaces:**
- Consumes: `SimPanel.Run/Return` (Task 2), `SimEntity.SetActive` (existing).
- Produces: `LinkAction.Run`, `LinkAction.Return`; `SimEntity.Return(int step)`.

- [ ] **Step 1: Write the failing test**

In `TriggerLoadTests.cs`, extend the actions test:

```csharp
        [Test]
        public void RunAndReturn_ParseToTheirBehaviour()
        {
            Assert.AreEqual(LinkAction.Run, TriggerFactory.ParseAction("run"));
            Assert.AreEqual(LinkAction.Return, TriggerFactory.ParseAction("return"));
            Assert.AreEqual(LinkAction.Inert, TriggerFactory.ParseAction("arm"), "still unbuilt");
        }
```

Append to `MovingPanelTests.cs`:

```csharp
        [Test]
        public void ATriggerWithARunLink_SendsThePanelOut()
        {
            LevelData level = Room(Riser());
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "timer", seconds = 0.25f, once = true },
            };
            level.links = new[]
            {
                new LevelLink { from = "t01", to = new[] { "p01" }, action = "run" },
            };

            var world = new SimWorld();
            LevelLoader.Apply(level, world, out _, out _);
            world.FloorKillsBalloon = false;
            world.Configure(new Vector2(2f, 14f), Vector2.zero, new Vector2(2f, 2f), null, 1);

            world.Tick(15);
            Assert.AreEqual(1f, world.Panels[0].WorldRect.yMin, 1e-3f, "not yet fired");

            world.Tick(45);
            Assert.Greater(world.Panels[0].WorldRect.yMin, 1.5f, "fired and travelling");
        }

        [Test]
        public void OffStillMeansAbsent_ForAMovingPanelToo()
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(Riser()), world, out _, out _);

            world.Panels[0].Run(0);
            world.Panels.Advance(20);
            world.Panels.SetEnabled("p01", false);
            world.Solids.Rebuild(world.Panels);

            Assert.AreEqual(0, world.Solids.Count, "off removes it from collision entirely");

            world.Panels.SetEnabled("p01", true);
            world.Panels.Advance(20);
            world.Solids.Rebuild(world.Panels);

            Assert.AreEqual(1f, world.Solids[0].rect.yMin, 1e-3f,
                            "and on restores it at its authored home with the travel cleared");
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./test.sh`
Expected: FAIL — `'LinkAction' does not contain a definition for 'Run'`.

- [ ] **Step 3: Write minimal implementation**

In `Sim/Triggers/SimLink.cs`:

```csharp
    public enum LinkAction
    {
        Inert = 0,
        On,
        Off,
        Toggle,

        /// <summary>
        /// Send the target out along its path (moving-panels design). Distinct from On,
        /// which switches a target's tiles present or absent: switching and travelling are
        /// different verbs, and overloading On would silently change what every existing
        /// link to a panel means the day that panel gains a path.
        /// </summary>
        Run,

        /// <summary>Walk the target back along its path from wherever it currently is.</summary>
        Return,
    }
```

In `TriggerFactory.ParseAction`, add `case "run": return LinkAction.Run;` and
`case "return": return LinkAction.Return;`.

In `SimEntity`, add:

```csharp
        /// <summary>
        /// Walks the path back from where the entity currently is. ArmedStep is rebased so
        /// Place() counts down to zero rather than snapping the entity to its start — the
        /// rewind Place()'s own comment anticipates.
        /// </summary>
        public void Return(int step)
        {
            int elapsed = step - ArmedStep;
            if (elapsed < 0) elapsed = 0;

            Rewinding = true;
            ArmedStep = step + elapsed;
            Active = true;
        }

        /// <summary>True while a return is walking the path backwards.</summary>
        public bool Rewinding;
```

and in `Place`, replace the `elapsed` computation with:

```csharp
            int elapsed = Rewinding ? ArmedStep - step : step - ArmedStep;
            if (elapsed < 0) elapsed = 0;
```

Add `Rewinding = false;` to `SimEntity.Reset()` and set it false in `SetActive` on a
false-to-true edge.

In `TriggerField.Apply`, handle the two new actions before the existing `Next` switch — they
are not on/off states, so they must not go through it:

```csharp
                SimEntity entity = entities?.Find(id);
                if (entity != null)
                {
                    if (link.Action == LinkAction.Run) { entity.SetActive(true, step + 1); }
                    else if (link.Action == LinkAction.Return) { entity.Return(step + 1); }
                    else entity.SetActive(Next(link.Action, entity.Active), step + 1);

                    continue;
                }
```

and for panels, at the end of the same loop:

```csharp
                if (panels != null && panels.IndexOf(id) >= 0)
                {
                    SimPanel panel = panels.Find(id);

                    // step + 1 for the same reason an entity is armed on the next step: this
                    // runs at the END of the step, so the first step the panel is actually
                    // placed on is the following one.
                    if (link.Action == LinkAction.Run) panel.Run(step + 1);
                    else if (link.Action == LinkAction.Return) panel.Return(step + 1);
                    else panels.SetEnabled(id, Next(link.Action, panel.Enabled));
                }
```

In `PanelField.SetEnabled`, clear the travel when a panel is switched back on, so `on` means
"present at its authored home":

```csharp
            if (enabled)
            {
                TurnedOnThisStep.Add(index);
                panel.RunStep = 0;
                panel.Direction = 1;
                panel.Offset = Vector2.zero;
                panel.Velocity = Vector2.zero;
            }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim Assets/_Project/Tests/EditMode
git commit -m "feat: run and return link actions drive panel and entity travel"
```

---

### Task 8: Tunnelling guarantee

**Files:**
- Test: `Assets/_Project/Tests/EditMode/TunnellingTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3–6. Adds no production code unless the test fails.

- [ ] **Step 1: Write the failing test**

Append to `TunnellingTests.cs`:

```csharp
        [Test]
        public void AFastBalloon_CannotCrossAMovingPanel()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(16, 2, 1, 14, TileKind.Solid);

            var level = new LevelData
            {
                tiles = grid.ToRows(),
                panels = new[]
                {
                    new LevelPanel
                    {
                        id = "p01", rect = new[] { 16f, 2f, 1f, 14f },
                        path = new[] { 0f, 0f, 0f, 2f }, path_mode = "pingpong",
                        speed = 8f, hold = 0f,
                    },
                },
            };

            var world = new SimWorld();
            LevelLoader.Apply(level, world, out _, out _);
            world.FloorKillsBalloon = false;
            world.Configure(new Vector2(8f, 8f), new Vector2(16f, 0f), new Vector2(2f, 2f), null, 1);

            world.Panels[0].Run(0);
            world.Tick(120);

            Assert.Less(world.Balloon.Position.x, 16f,
                        "a one-tile-thick panel is never crossed: nothing moves more than "
                        + "~0.35 m in a fixed step, so the sweep cannot miss it");
        }
```

- [ ] **Step 2: Run test to verify it fails or passes**

Run: `./test.sh`
Expected: PASS if Tasks 4 and 6 swept correctly. **If it fails, the sweep is not testing solids
on every iteration of the resolve loop** — fix `SweptCircle`, not the test.

- [ ] **Step 3: Commit**

```bash
git add Assets/_Project/Tests/EditMode/TunnellingTests.cs
git commit -m "test: a moving panel cannot be tunnelled through"
```

---

### Task 9: Validation

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelValidator.cs` (`ValidatePanels`, and link checks)
- Test: `Assets/_Project/Tests/EditMode/LevelValidatorTests.cs`

**Interfaces:**
- Consumes: `LevelPanel.path/speed/path_mode/exit_dir`, `LevelLink.action/to`.
- Produces: warning strings only. Warnings never block a save.

- [ ] **Step 1: Write the failing test**

Append to `LevelValidatorTests.cs`:

```csharp
        static LevelData WithMovingPanel(out LevelPanel panel)
        {
            LevelData level = Valid();

            panel = new LevelPanel
            {
                id = "p01", rect = new[] { 4f, 0f, 4f, 1f },
                path = new[] { 0f, 0f, 0f, 3f }, path_mode = "once", speed = 6f,
            };

            level.panels = new[] { panel };
            level.triggers = new[]
            {
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 2f, 1f, 2f, 2f } },
            };
            level.links = new[] { new LevelLink { from = "t01", to = new[] { "p01" }, action = "run" } };

            return level;
        }

        [Test]
        public void AMovingPanelWithNoSpeed_Warns()
        {
            LevelData level = WithMovingPanel(out LevelPanel panel);
            panel.speed = 0f;

            AssertWarns(level, "never move");
        }

        [Test]
        public void ExitDirOnAPathedPanel_Warns()
        {
            LevelData level = WithMovingPanel(out LevelPanel panel);
            panel.exit_dir = "up";

            AssertWarns(level, "exit_dir is ignored");
        }

        [Test]
        public void RunTargetingSomethingWithNoPath_Warns()
        {
            LevelData level = WithMovingPanel(out LevelPanel panel);
            panel.path = new float[0];

            AssertWarns(level, "cannot travel");
        }

        [Test]
        public void TwoMovingPanelsThatWillFight_Warn()
        {
            LevelData level = WithMovingPanel(out LevelPanel panel);

            level.panels = new[]
            {
                panel,
                new LevelPanel
                {
                    id = "p02", rect = new[] { 4f, 2f, 4f, 1f },
                    path = new[] { 0f, 0f, 0f, -2f }, path_mode = "once", speed = 6f,
                },
            };
            level.links = new[]
            {
                new LevelLink { from = "t01", to = new[] { "p01", "p02" }, action = "run" },
            };

            AssertWarns(level, "swept rects overlap");
        }

        [Test]
        public void AWellFormedMovingPanel_ProducesNoPanelWarnings()
        {
            LevelData level = WithMovingPanel(out _);

            Assert.IsFalse(Mentions(LevelValidator.Validate(level), "panel"),
                           string.Join(" | ", LevelValidator.Validate(level)));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./test.sh`
Expected: FAIL — `expected a warning containing 'never move'`.

- [ ] **Step 3: Write minimal implementation**

In `ValidatePanels`, after the existing checks, add:

```csharp
                bool moving = panel.path != null && panel.path.Length >= 4;

                if (moving && panel.speed <= 0f)
                    warnings.Add($"panel '{panel.id}' has a path but speed 0 — it will never move");

                if (moving && PanelDirections.IsKnown(panel.exit_dir) && panel.exit_dir != "down")
                    warnings.Add($"panel '{panel.id}' has a path, so its exit_dir is ignored");

                if (!moving) continue;

                Rect swept = SweptRectOf(panel);

                foreach (LevelPanel other in level.panels)
                {
                    if (other == null || other == panel) continue;
                    if (other.path == null || other.path.Length < 4) continue;

                    // Once per pair, not twice: the id comparison keeps the warning list
                    // stable regardless of file order.
                    if (string.CompareOrdinal(panel.id, other.id) >= 0) continue;

                    if (swept.Overlaps(SweptRectOf(other)))
                        warnings.Add($"panels '{panel.id}' and '{other.id}' have swept rects that "
                                     + "overlap — two moving solids that will fight");
                }
```

Implement the two helpers in the same file. `SweptRectOf` is the union of the panel's rect at
every waypoint:

```csharp
        /// <summary>The whole area a moving panel passes through — its rect at every waypoint.</summary>
        static Rect SweptRectOf(LevelPanel panel)
        {
            Rect swept = ZoneGeometry.RectOf(panel.rect);
            Rect home = swept;

            for (int i = 0; i + 1 < panel.path.Length; i += 2)
            {
                var at = new Rect(home.xMin + panel.path[i], home.yMin + panel.path[i + 1],
                                  home.width, home.height);

                swept = Rect.MinMaxRect(Mathf.Min(swept.xMin, at.xMin), Mathf.Min(swept.yMin, at.yMin),
                                        Mathf.Max(swept.xMax, at.xMax), Mathf.Max(swept.yMax, at.yMax));
            }

            return swept;
        }
```

`ZoneGeometry` lives in the editor assembly, so inline the two-line rect construction here
instead of referencing it — `LevelValidator` is sim-side and must not depend on `LevelEditor`.

Add the link check in the link-validation pass:

```csharp
                if (action == "run" || action == "return")
                    foreach (string target in link.to)
                        if (!HasPath(level, target))
                            warnings.Add($"link '{link.from}' → '{target}' is '{action}', but "
                                         + $"'{target}' has no path and cannot travel");
```

with `HasPath` checking both `level.panels` and `level.entities` for a `path` of four or more
floats.

- [ ] **Step 4: Run test to verify it passes**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Level/LevelValidator.cs Assets/_Project/Tests/EditMode/LevelValidatorTests.cs
git commit -m "feat: lint moving panels and travel links"
```

---

### Task 10: Editor authoring

**Files:**
- Modify: `Assets/_Project/Scripts/LevelEditor/EditorSession.cs`
- Modify: `Assets/_Project/Scripts/LevelEditor/PanelInspector.cs`
- Modify: `Assets/_Project/Scripts/LevelEditor/TriggerInspector.cs`
- Modify: `Assets/_Project/Scripts/LevelEditor/LevelEditorController.cs`
- Test: `Assets/_Project/Tests/EditMode/EditorPanelTests.cs`

**Interfaces:**
- Consumes: `LevelPanel.path/path_mode/speed/hold` (Task 2).
- Produces: `EditorSession.SetPanelSpeed(string id, float speed)`, `SetPanelHold(string id, float hold)`, `SetPanelPathMode(string id, string mode)`, `AddPanelWaypoint(string id, Vector2 worldPoint)`, `MovePanelWaypoint(string id, int index, Vector2 worldPoint)`, `RemovePanelWaypoint(string id, int index)`, `PanelWaypoints(string id) -> Vector2[]`.

- [ ] **Step 1: Write the failing test**

Append to `EditorPanelTests.cs`:

```csharp
        [Test]
        public void PanelWaypoints_AreStoredAsOffsetsFromTheRect()
        {
            EditorSession session = Session();
            LevelPanel panel = session.AddPanel(new Rect(10f, 1f, 4f, 1f));

            session.AddPanelWaypoint(panel.id, new Vector2(10f, 4f));

            Assert.AreEqual(new[] { 0f, 0f, 0f, 3f }, panel.path,
                            "the panel's own zero first, then the offset to the clicked tile");
            Assert.AreEqual(new Vector2(10f, 4f), session.PanelWaypoints(panel.id)[1],
                            "and reads back in world space, where the author put it");
        }

        [Test]
        public void MovingThePanel_LeavesItsPathAlone()
        {
            EditorSession session = Session();
            LevelPanel panel = session.AddPanel(new Rect(10f, 1f, 4f, 1f));
            session.AddPanelWaypoint(panel.id, new Vector2(10f, 4f));

            session.SetPanelRect(panel.id, new Rect(20f, 1f, 4f, 1f));

            Assert.AreEqual(new[] { 0f, 0f, 0f, 3f }, panel.path, "offsets, so the path travels with it");
            Assert.AreEqual(new Vector2(20f, 4f), session.PanelWaypoints(panel.id)[1]);
        }

        [Test]
        public void SetPanelSpeedAndHold_WriteTheFields()
        {
            EditorSession session = Session();
            LevelPanel panel = session.AddPanel(new Rect(10f, 1f, 4f, 1f));

            session.SetPanelSpeed(panel.id, 6f);
            session.SetPanelHold(panel.id, 0.5f);
            session.SetPanelPathMode(panel.id, "pingpong");

            Assert.AreEqual(6f, panel.speed, 1e-4f);
            Assert.AreEqual(0.5f, panel.hold, 1e-4f);
            Assert.AreEqual("pingpong", panel.path_mode);

            session.SetPanelSpeed(panel.id, -2f);
            Assert.AreEqual(0f, panel.speed, 1e-4f, "a negative speed is a backwards panel, not a feature");
        }

        [Test]
        public void RemovingTheLastWaypoint_MakesItAMaskPanelAgain()
        {
            EditorSession session = Session();
            LevelPanel panel = session.AddPanel(new Rect(10f, 1f, 4f, 1f));
            session.AddPanelWaypoint(panel.id, new Vector2(10f, 4f));

            session.RemovePanelWaypoint(panel.id, 1);

            Assert.AreEqual(0, panel.path.Length, "an empty path, not a one-point path");
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `./test.sh`
Expected: FAIL — `'EditorSession' does not contain a definition for 'AddPanelWaypoint'`.

- [ ] **Step 3: Write minimal implementation**

In `EditorSession.cs`, beside the other panel operations:

```csharp
        /// <summary>
        /// The panel's waypoints in world space. Stored as offsets from the rect — moving a
        /// panel must not invalidate its path — and converted here, where the author is
        /// clicking in world coordinates.
        /// </summary>
        public Vector2[] PanelWaypoints(string id)
        {
            LevelPanel panel = FindPanel(id);
            if (panel?.path == null || panel.path.Length < 2) return new Vector2[0];

            Rect rect = ZoneGeometry.RectOf(panel.rect);
            var points = new Vector2[panel.path.Length / 2];

            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2(rect.xMin + panel.path[i * 2], rect.yMin + panel.path[i * 2 + 1]);

            return points;
        }

        /// <summary>
        /// Appends a waypoint. The first call seeds the panel's own zero as waypoint 0, so a
        /// path always starts where the panel was authored.
        /// </summary>
        public void AddPanelWaypoint(string id, Vector2 world)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return;

            Rect rect = ZoneGeometry.RectOf(panel.rect);
            Vector2 tile = GridCursor.ToTile(world);
            var offset = new Vector2(tile.x - rect.xMin, tile.y - rect.yMin);

            var path = new List<float>(panel.path ?? new float[0]);
            if (path.Count == 0) { path.Add(0f); path.Add(0f); }

            path.Add(offset.x);
            path.Add(offset.y);

            panel.path = path.ToArray();
            Dirty = true;
        }

        public void MovePanelWaypoint(string id, int index, Vector2 world)
        {
            LevelPanel panel = FindPanel(id);
            if (panel?.path == null || index < 0 || index * 2 + 1 >= panel.path.Length) return;

            Rect rect = ZoneGeometry.RectOf(panel.rect);
            Vector2 tile = GridCursor.ToTile(world);

            panel.path[index * 2] = tile.x - rect.xMin;
            panel.path[index * 2 + 1] = tile.y - rect.yMin;
            Dirty = true;
        }

        /// <summary>
        /// Removes a waypoint. Dropping to a single point empties the path outright: a
        /// one-waypoint path is a moving panel that cannot move, which is a trap for the
        /// author rather than a state worth keeping.
        /// </summary>
        public void RemovePanelWaypoint(string id, int index)
        {
            LevelPanel panel = FindPanel(id);
            if (panel?.path == null || index < 0 || index * 2 + 1 >= panel.path.Length) return;

            var path = new List<float>(panel.path);
            path.RemoveRange(index * 2, 2);

            panel.path = path.Count <= 2 ? new float[0] : path.ToArray();
            Dirty = true;
        }

        public void SetPanelSpeed(string id, float speed)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return;

            panel.speed = Mathf.Max(speed, 0f);
            Dirty = true;
        }

        public void SetPanelHold(string id, float hold)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return;

            panel.hold = Mathf.Max(hold, 0f);
            Dirty = true;
        }

        public void SetPanelPathMode(string id, string mode)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null || (mode != "once" && mode != "loop" && mode != "pingpong")) return;

            panel.path_mode = mode;
            Dirty = true;
        }
```

In `PanelInspector.Draw`, after the `exit_dir` block, add speed/hold/mode rows and the waypoint
list, and show `exit_dir` **only** when `panel.path.Length < 4`:

```csharp
            bool moving = panel.path != null && panel.path.Length >= 4;

            if (!moving) changed |= DrawExitDir(session, panel);
            else GUILayout.Label("moving panel — solid the whole way; exit_dir does not apply");

            float speed = Row("speed", $"{panel.id}.speed", panel.speed, fieldText);
            if (!Mathf.Approximately(speed, panel.speed))
            {
                session.BeginEdit();
                session.SetPanelSpeed(panel.id, speed);
                changed = true;
            }

            float hold = Row("hold s", $"{panel.id}.hold", panel.hold, fieldText);
            if (!Mathf.Approximately(hold, panel.hold))
            {
                session.BeginEdit();
                session.SetPanelHold(panel.id, hold);
                changed = true;
            }

            GUILayout.BeginHorizontal();
            foreach (string mode in Modes)
            {
                bool active = panel.path_mode == mode;
                if (GUILayout.Toggle(active, mode, "Button", GUILayout.Width(60f)) == active) continue;

                session.BeginEdit();
                session.SetPanelPathMode(panel.id, mode);
                changed = true;
            }
            GUILayout.EndHorizontal();

            Vector2[] waypoints = session.PanelWaypoints(panel.id);
            for (int i = 1; i < waypoints.Length; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"→ {DevNumber.Format(waypoints[i].x, false)}, "
                                + $"{DevNumber.Format(waypoints[i].y, false)}", GUILayout.Width(120f));

                if (GUILayout.Button("x", GUILayout.Width(24f)))
                {
                    session.BeginEdit();
                    session.RemovePanelWaypoint(panel.id, i);
                    changed = true;
                    GUILayout.EndHorizontal();
                    break;
                }

                GUILayout.EndHorizontal();
            }
```

with `static readonly string[] Modes = { "once", "loop", "pingpong" };`.

In `TriggerInspector`, extend `Actions`:

```csharp
        static readonly string[] Actions = { "toggle", "on", "off", "run", "return", "arm", "fire" };
```

In `LevelEditorController.PanelTool`, add waypoint authoring. Insert this as the first branch
of the left-button press, before the corner-handle test, so a ctrl+click never also starts a
resize:

```csharp
            if (mouse.leftButton.wasPressedThisFrame
                && Keyboard.current != null && Keyboard.current.ctrlKey.isPressed
                && Session.SelectedPanelId != null)
            {
                Session.BeginEdit();
                Session.AddPanelWaypoint(Session.SelectedPanelId, world);
                SyncDynamic();
                _status = "added waypoint";
                return;
            }
```

Dragging an existing waypoint reuses the entity tool's `_draggingWaypoint` field. Add to the
same press handler, after the ctrl branch:

```csharp
            if (mouse.leftButton.wasPressedThisFrame && Session.SelectedPanelId != null)
            {
                Vector2[] points = Session.PanelWaypoints(Session.SelectedPanelId);

                // Waypoint 0 is the panel's own home and is not draggable: moving it would
                // mean moving the panel, which is what dragging the panel itself does.
                for (int i = 1; i < points.Length; i++)
                {
                    if (Vector2.Distance(points[i], world) > ZoneHandleRadius) continue;

                    Session.BeginEdit();
                    _draggingWaypoint = i;
                    _dragging = true;
                    break;
                }
            }

            if (_draggingWaypoint >= 0 && Session.SelectedPanelId != null)
            {
                Session.MovePanelWaypoint(Session.SelectedPanelId, _draggingWaypoint, world);
                SyncDynamic();

                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    _draggingWaypoint = -1;
                    _dragging = false;
                    _status = "moved waypoint";
                }

                return;
            }
```

Draw the path in `DrawPanels`, inside the existing per-panel loop:

```csharp
                Vector2[] points = Session.PanelWaypoints(panel.id);

                for (int i = 1; i < points.Length; i++)
                {
                    DrawWorldLine(points[i - 1], points[i], PanelEdge);
                    DrawHandle(points[i]);
                }
```

`PanelEdge`, `DrawWorldLine`, `DrawHandle` and `ZoneHandleRadius` all already exist in this
file — this is the same treatment `DrawEntities` gives an entity path.

- [ ] **Step 4: Run test to verify it passes**

Run: `./test.sh`
Expected: PASS, `failed=0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/LevelEditor Assets/_Project/Tests/EditMode/EditorPanelTests.cs
git commit -m "feat: author panel paths, speed and dwell in the editor"
```

---

### Task 11: Drawing a moving panel

**Files:**
- Modify: `Assets/_Project/Scripts/Runtime/PanelViewSync.cs`
- Modify: `Assets/_Project/Scripts/Runtime/StepInterpolator.cs` (read only — confirm the API)

**Interfaces:**
- Consumes: `SimWorld.Panels`, `SimPanel.IsMoving/WorldRect/Rect`, `SimPalette.ForTile`, `QuadView.Create`, `StepInterpolator`.
- Produces: no new public API. This is the view; there are no automated tests, by the same boundary `ShellView` and the editor sit on.

- [ ] **Step 1: Add the moving-panel quads**

`PanelViewSync` currently has only a `LateUpdate`. Moving panels need a `FixedUpdate` too,
because `StepInterpolator.Sync` must be called once per fixed step, never once per render frame
(its own doc comment is explicit, and `SimDriver` carries `[DefaultExecutionOrder(-100)]` so the
step has already run). Follow `BalloonView` exactly: `Sync` in `FixedUpdate`, `Sample(Alpha)` in
`LateUpdate`. Add a `public SimDriver Driver;` field beside `World`, assigned where
`LevelSession` builds the view.

```csharp
        /// <summary>
        /// One quad per tile of every moving panel. These are NOT debris: they are the panel,
        /// which is solid the whole way, so they are positioned from the simulation's own
        /// offset rather than given a velocity of their own.
        ///
        /// Interpolated between fixed steps exactly as the balloon is (CLAUDE.md, GDD 13.2):
        /// the sim ticks at 60 Hz, the display is not phase-locked to it, and sampling a
        /// moving solid raw on a render frame judders — on a panel a player is standing on,
        /// that reads as their feet slipping. The interpolated value is never written back and
        /// the sim cannot observe it.
        /// </summary>
        sealed class PanelView
        {
            public int panel;
            public readonly List<GameObject> tiles = new List<GameObject>();
            public readonly StepInterpolator interp = new StepInterpolator();
        }

        readonly List<PanelView> _movers = new List<PanelView>();
        int _seenPanels = -1;
```

```csharp
        void FixedUpdate()
        {
            if (World == null) return;

            for (int i = 0; i < _movers.Count; i++)
                _movers[i].interp.Sync(World.Clock.Step, World.Panels[_movers[i].panel].Offset);
        }
```

In `LateUpdate`, before the existing calls, rebuild when the panel count changes and place the
quads:

```csharp
            if (World.Panels.Count != _seenPanels) RebuildMovers();

            for (int i = 0; i < _movers.Count; i++)
            {
                PanelView view = _movers[i];
                SimPanel panel = World.Panels[view.panel];

                Vector2 offset = Driver != null && view.interp.Started
                    ? view.interp.Sample(Driver.Alpha)
                    : panel.Offset;

                int t = 0;

                for (int y = panel.Rect.yMin; y < panel.Rect.yMax; y++)
                for (int x = panel.Rect.xMin; x < panel.Rect.xMax; x++)
                {
                    // BaseAt, not the indexer: a moving panel's cells are permanently masked
                    // out of the live grid, because the panel carries them now.
                    if (World.Grid.BaseAt(x, y) == TileKind.Empty) continue;
                    if (t >= view.tiles.Count) break;

                    GameObject quad = view.tiles[t++];
                    quad.SetActive(panel.Enabled);
                    quad.transform.localPosition =
                        new Vector3(x + 0.5f + offset.x, y + 0.5f + offset.y, 0f);
                }
            }
```

`RebuildMovers` destroys the old quads, then for each `panel.IsMoving` creates one
`QuadView.Create(transform, $"panel_{panel.Id}_{x}_{y}", SimPalette.ForTile(World.Grid.BaseAt(x, y)), …)`
per non-empty cell, and sets `_seenPanels = World.Panels.Count`.

- [ ] **Step 2: Keep moving panels out of the debris watcher**

A moving panel's cells are masked for the whole run, so the first seeded frame would otherwise
drop one debris quad per cell. In `WatchMask`, skip any cell inside a moving panel's authored
rect:

```csharp
                if (_seeded && masked != _wasMasked[i] && !InAMovingPanel(x, y))
```

with:

```csharp
        /// <summary>
        /// Whether this cell belongs to a moving panel. Those tiles never fall or arrive —
        /// they travel — so the per-cell debris edge must not see them at all.
        /// </summary>
        bool InAMovingPanel(int x, int y)
        {
            for (int i = 0; i < World.Panels.Count; i++)
            {
                SimPanel panel = World.Panels[i];
                if (panel.IsMoving && panel.Rect.Contains(new Vector2Int(x, y))) return true;
            }

            return false;
        }
```

- [ ] **Step 3: Verify by running the game**

Run the level editor on a room with a moving panel (`E` from the level select, tool `9`), press
Tab to play, and fire the trigger. Confirm the panel is drawn where it collides — stand on it
and check your feet stay on its surface at every speed.

Note in the commit message that this is unverified by automated tests, because it is view code.

- [ ] **Step 4: Commit**

```bash
git add Assets/_Project/Scripts/Runtime/PanelViewSync.cs
git commit -m "feat: draw moving panels, interpolated between fixed steps"
```

---

### Task 12: Determinism — the load-bearing test

**Files:**
- Modify: `Assets/_Project/Tests/EditMode/DeterminismTests.cs`

**Interfaces:**
- Consumes: everything above. `SimPanel.RunStep/Direction/Offset`, `SolidField`.

- [ ] **Step 1: Write the failing test**

Append to `DeterminismTests.cs`:

```csharp
        static LevelData MovingPanelRoom()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 1, 6, 1, TileKind.Solid);

            return new LevelData
            {
                tiles = grid.ToRows(),
                panels = new[]
                {
                    new LevelPanel
                    {
                        id = "p01", rect = new[] { 10f, 1f, 6f, 1f },
                        path = new[] { 0f, 0f, 0f, 4f, 3f, 4f },
                        path_mode = "pingpong", speed = 5f, hold = 0.25f,
                    },
                },
                triggers = new[]
                {
                    new LevelTrigger { id = "t01", type = "timer", seconds = 0.5f, once = true },
                },
                links = new[]
                {
                    new LevelLink { from = "t01", to = new[] { "p01" }, action = "run" },
                },
            };
        }

        static string PanelSignature(SimWorld world)
        {
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < world.Panels.Count; i++)
            {
                SimPanel p = world.Panels[i];
                sb.Append($"{p.Id}|{p.Enabled}|{p.RunStep}|{p.Direction}|"
                          + $"{p.Offset.x:R},{p.Offset.y:R}|{p.Velocity.x:R},{p.Velocity.y:R};");
            }

            sb.Append($"solids={world.Solids.Count};");
            sb.Append($"player={world.Player.Position.x:R},{world.Player.Position.y:R};");
            sb.Append($"balloon={world.Balloon.Position.x:R},{world.Balloon.Position.y:R}");

            return sb.ToString();
        }

        static SimWorld RunMovingPanelRoom(int steps)
        {
            var world = new SimWorld();
            LevelLoader.Apply(MovingPanelRoom(), world, out _, out _);
            world.FloorKillsBalloon = false;
            world.Configure(new Vector2(12f, 8f), Vector2.zero, new Vector2(12f, 3f), null, 4242);
            world.Tick(steps);
            return world;
        }

        [Test]
        public void AMovingPanelRoom_ReplaysIdentically()
        {
            Assert.AreEqual(PanelSignature(RunMovingPanelRoom(600)),
                            PanelSignature(RunMovingPanelRoom(600)),
                            "GDD 6.1: attempt 40 must replay attempt 1, panels included");
        }

        [Test]
        public void AMovingPanelRoom_ReplaysIdenticallyAcrossARestart()
        {
            SimWorld first = RunMovingPanelRoom(600);
            string once = PanelSignature(first);

            first.Restart();
            first.Tick(600);

            Assert.AreEqual(once, PanelSignature(first),
                            "a restart must leave no travel state behind");
        }

        [Test]
        public void FastForwardingAMovingPanelRoom_ChangesNothing()
        {
            // TimeControl gives more fixed steps per render frame; the sim cannot observe it,
            // so 600 steps taken in bursts must equal 600 taken evenly.
            var burst = new SimWorld();
            LevelLoader.Apply(MovingPanelRoom(), burst, out _, out _);
            burst.FloorKillsBalloon = false;
            burst.Configure(new Vector2(12f, 8f), Vector2.zero, new Vector2(12f, 3f), null, 4242);

            for (int i = 0; i < 200; i++) burst.Tick(3);

            Assert.AreEqual(PanelSignature(RunMovingPanelRoom(600)), PanelSignature(burst));
        }
```

- [ ] **Step 2: Run the tests**

Run: `./test.sh`
Expected: PASS. **A failure here is the whole point of the test** — the likely causes, in order:
a velocity that was integrated rather than derived, `Direction`/`RunStep` not cleared by
`Reset()`, or iteration over an unordered collection in `SolidField`.

- [ ] **Step 3: Commit**

```bash
git add Assets/_Project/Tests/EditMode/DeterminismTests.cs
git commit -m "test: moving panels are in the replay-equality comparison"
```

---

### Task 13: Documentation

**Files:**
- Modify: `docs/level-editor-spec.md`
- Modify: `docs/balloon-game-gdd.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update the level-editor spec**

In §3's `panels` block, add `path`, `path_mode`, `speed`, `hold`, `phase` with the comments from
the design doc. In §4's tool table, note ctrl+click waypoint authoring on tool `9` and the new
`run`/`return` action buttons. In §7, list the five new warnings from Task 9. In the tile-panels
"as built" section, scope its moving-solid deferral to pathless panels and link the new design
doc.

- [ ] **Step 2: Update the GDD**

Add a §22 row:

```markdown
| 2026-09-02 | Moving solids exist. A panel with a path is solid, lethal and able to carry, shove and crush for the whole of its travel; both sweeps gained a second cast source. "The world is static geometry plus hazards" was an assumption `SweptAabb` and `SweptCircle` were written against, and it no longer holds. Pathless panels are unchanged. | `docs/superpowers/specs/2026-09-02-moving-panels-design.md` |
```

and extend §9.3's trap vocabulary with the pop-up-and-shove and retracting-wall traps this makes
authorable.

- [ ] **Step 3: Update CLAUDE.md**

Under the tile-panel notes, correct the claim that every collision query funnels through
`TileGrid`: it now funnels through the grid **and** `SolidField`, and a new hazard or body must
consult both.

- [ ] **Step 4: Run the tests one final time**

Run: `./test.sh`
Expected: `failed=0`, and confirm `DeterminismTests` shows a `result="Passed"` for every case in
`TestResults/results.xml`.

- [ ] **Step 5: Commit**

```bash
git add docs CLAUDE.md
git commit -m "docs: moving panels, and the deferral they reverse"
```

---

## Playtesting notes (not automatable)

Tests cover correctness, not feel. After Task 11, a human must judge:

- **`panelHitPower`.** Whether a rising panel bats the balloon a satisfying distance, or launches
  it into the ceiling. This is a `BalloonTuning` slider in the live tuning panel — tune it there,
  then write the value into the profile under `Assets/StreamingAssets/levels/_tuning/`.
- **Shove readability.** Whether being pushed by a panel reads as being pushed, or as the player
  sliding for no reason. If it reads badly, the fix is telegraphing in the level (a `visible`
  trigger, a link `delay`), not a change to the shove.
- **Carry at speed.** Whether a fast-moving panel drags the player smoothly or leaves them
  stuttering a pixel behind. Stutter means the view is sampling per render frame instead of per
  fixed step — a Task 11 bug, not a physics one.
- **Crush fairness.** Whether deaths to a closing panel feel earned. The fairness shrink is
  deliberately not applied to a crush; if it reads as unfair, the answer is slower panels or more
  warning, not a smaller hitbox.
