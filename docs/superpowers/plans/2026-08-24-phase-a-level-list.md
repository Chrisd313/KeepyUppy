# Phase A — The Level List Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the set of levels in the game a declared, editable, lintable list rather than whatever happens to be in the levels directory.

**Architecture:** A manifest file (`levels/index.json`) declares worlds and the ordered level ids in each. `ILevelSource` gains index load/save beside its existing level load/save, so the future packed web source supplies the same shape. The manifest is authoritative for ordering; the `world` and `index` fields inside each level file become advisory and are lint-checked against it. The editor maintains the manifest — new, open, reorder — because a declared list is only better than a derived one if the tool keeps it current.

**Tech Stack:** Unity 6000.2.6f2, C#, `JsonUtility`, NUnit EditMode tests. No new packages.

**Spec:** `docs/superpowers/specs/2026-08-24-menus-and-level-list-design.md`

## Global Constraints

Copied from `CLAUDE.md` and the spec. Every task's requirements implicitly include these.

- **Do not create or edit `.unity`, `.prefab` or `.asset` files.** Everything is constructed in code from the bootstrap.
- **Do not modify anything in `ProjectSettings/` without asking first.**
- **No `using UnityEditor;` outside test assemblies**, and never anywhere under `Assets/_Project/Scripts/LevelEditor/`.
- **Simulation logic stays in plain C# classes with no scene dependency.**
- **`JsonUtility` limits:** no nested arrays, no dictionaries, no polymorphism, and it silently discards unknown fields. Field names must match JSON keys literally, which is why the schema uses snake_case.
- **JSON stays readable:** `prettyPrint: true`, stable key order, never minified. The readable diff is the point.
- **Run `./test.sh` before reporting any task complete.** Exit code 0 means every test passed. If it cannot run, say so explicitly and say why.
- **The Unity Editor must be closed** for `./test.sh` to run — batch mode takes an exclusive lock on `Library/`. If a run hangs for more than about two minutes, stop and ask for the editor to be closed. Do not retry in a loop and do not delete `Library/`.
- **Single-test runs:** `./test.sh -testFilter "KeepyUppy.SimTests.ClassName"` — the wrapper forwards extra arguments to the Unity CLI.
- Phase A adds **no state to the simulation**, so the replay-equality test needs no changes. If that stops being true, the new state must join `SimSnapshot` in the same commit.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `Assets/_Project/Scripts/Sim/Level/LevelIndex.cs` | The manifest data model and every query over it |
| `Assets/_Project/Scripts/Sim/Level/LevelIndexValidator.cs` | Manifest lint: membership, duplicates, slot drift |
| `Assets/_Project/Tests/EditMode/LevelIndexTests.cs` | Model, round-trip and queries |
| `Assets/_Project/Tests/EditMode/LevelIndexValidatorTests.cs` | One test per lint |
| `Assets/StreamingAssets/levels/index.json` | The manifest itself, seeded at boot if absent |

**Modified:**

| File | Change |
|---|---|
| `Assets/_Project/Scripts/Sim/Level/ILevelSource.cs` | `TryLoadIndex` / `SaveIndex` |
| `Assets/_Project/Scripts/Runtime/FileLevelSource.cs` | Implement both; exclude the manifest from `List()` |
| `Assets/_Project/Scripts/LevelEditor/EditorSession.cs` | Hold the index; new/open/reorder mutations |
| `Assets/_Project/Scripts/LevelEditor/LevelEditorController.cs` | The level browser panel |
| `Assets/_Project/Scripts/LevelEditor/ILevelEditorHost.cs` | Nothing new — the editor already has `Levels` |
| `Assets/_Project/Scripts/Runtime/SimBootstrap.cs` | Seed the manifest; take the starting level from it |

---

## Task 1: The manifest data model

**Files:**
- Create: `Assets/_Project/Scripts/Sim/Level/LevelIndex.cs`
- Test: `Assets/_Project/Tests/EditMode/LevelIndexTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `KeepyUppy.Sim.Level.LevelIndex` with `public int version`, `public LevelWorld[] worlds`; `KeepyUppy.Sim.Level.LevelWorld` with `public string id`, `public string name`, `public string[] levels`. Also `LevelIndex.CurrentVersion` (const int, value 1) and `LevelIndex.Default()` returning a one-world manifest containing `"w1_01"`.

- [ ] **Step 1: Write the failing test**

Create `Assets/_Project/Tests/EditMode/LevelIndexTests.cs`:

```csharp
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The level manifest (spec: 2026-08-24 menus and the level list).
    ///
    /// The manifest is authoritative for ordering. These tests pin that, because the
    /// alternative - ordering implied by filenames - is what the manifest exists to replace.
    /// </summary>
    public sealed class LevelIndexTests
    {
        [Test]
        public void RoundTripsThroughJson()
        {
            var index = new LevelIndex
            {
                version = LevelIndex.CurrentVersion,
                worlds = new[]
                {
                    new LevelWorld { id = "w1", name = "The Living Room",
                                     levels = new[] { "w1_01", "w1_02" } },
                },
            };

            string json = JsonUtility.ToJson(index, prettyPrint: true);
            LevelIndex parsed = JsonUtility.FromJson<LevelIndex>(json);

            Assert.AreEqual(1, parsed.worlds.Length);
            Assert.AreEqual("The Living Room", parsed.worlds[0].name);
            CollectionAssert.AreEqual(new[] { "w1_01", "w1_02" }, parsed.worlds[0].levels);
        }

        [Test]
        public void TheDefaultManifestNamesTheSeededRoom()
        {
            // SimBootstrap seeds w1_01 when the levels directory is empty. The manifest it
            // writes alongside has to agree, or a fresh clone boots to an empty game.
            LevelIndex index = LevelIndex.Default();

            Assert.AreEqual(LevelIndex.CurrentVersion, index.version);
            CollectionAssert.Contains(index.worlds[0].levels, "w1_01");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.LevelIndexTests"`
Expected: FAIL — compile error, `LevelIndex` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/_Project/Scripts/Sim/Level/LevelIndex.cs`:

```csharp
using System;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Which levels are in the game, and in what order (spec: menus and the level list).
    ///
    /// **The manifest is authoritative for ordering.** The world and index fields inside each
    /// level file are advisory and lint-checked against this; nothing reads them to decide
    /// what comes next.
    ///
    /// A level file on disk but absent from here is a DRAFT: the editor opens it, the game
    /// does not list it. That capability is the reason this file exists rather than the game
    /// deriving its list by scanning the directory.
    ///
    /// Modelled for JsonUtility - an array of objects each holding a string[], with no nested
    /// arrays and no dictionaries, the same constraints LevelData works within.
    /// </summary>
    [Serializable]
    public sealed class LevelIndex
    {
        /// <summary>Bumped only when the shape changes in a way a reader must handle.</summary>
        public const int CurrentVersion = 1;

        /// <summary>The file this lives in, without extension. Excluded from level listings.</summary>
        public const string FileName = "index";

        public int version = CurrentVersion;
        public LevelWorld[] worlds = new LevelWorld[0];

        /// <summary>
        /// The manifest a fresh project starts with. Must name the level SimBootstrap seeds,
        /// or a new clone boots into an empty game.
        /// </summary>
        public static LevelIndex Default() => new LevelIndex
        {
            version = CurrentVersion,
            worlds = new[]
            {
                new LevelWorld
                {
                    id = "w1",
                    name = "The Living Room",
                    levels = new[] { "w1_01" },
                },
            },
        };
    }

    /// <summary>One world and its levels, in play order (GDD 12).</summary>
    [Serializable]
    public sealed class LevelWorld
    {
        public string id = "w1";
        public string name = "";
        public string[] levels = new string[0];
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.LevelIndexTests"`
Expected: PASS, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Level/LevelIndex.cs Assets/_Project/Scripts/Sim/Level/LevelIndex.cs.meta Assets/_Project/Tests/EditMode/LevelIndexTests.cs Assets/_Project/Tests/EditMode/LevelIndexTests.cs.meta
git commit -m "Add the level manifest data model"
```

---

## Task 2: Queries over the manifest

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Level/LevelIndex.cs`
- Test: `Assets/_Project/Tests/EditMode/LevelIndexTests.cs`

**Interfaces:**
- Consumes: `LevelIndex`, `LevelWorld` from Task 1
- Produces: on `LevelIndex` — `IReadOnlyList<string> AllLevels()`, `bool Contains(string id)`, `LevelWorld WorldOf(string id)`, `int SlotOf(string id)` (0-based within its world, −1 if absent), `int WorldNumberOf(string id)` (1-based world position, −1 if absent), `List<string> DraftsAmong(IReadOnlyList<string> onDisk)`

- [ ] **Step 1: Write the failing tests**

Append to `LevelIndexTests.cs`, inside the class:

```csharp
        static LevelIndex TwoWorlds() => new LevelIndex
        {
            version = LevelIndex.CurrentVersion,
            worlds = new[]
            {
                new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_02", "w1_01" } },
                new LevelWorld { id = "w2", name = "Two", levels = new[] { "w2_01" } },
            },
        };

        [Test]
        public void OrderComesFromTheManifest_NotTheFilenames()
        {
            // The whole point. w1_02 is listed first, so it IS first, however it sorts.
            LevelIndex index = TwoWorlds();

            CollectionAssert.AreEqual(new[] { "w1_02", "w1_01", "w2_01" }, index.AllLevels());
            Assert.AreEqual(0, index.SlotOf("w1_02"));
            Assert.AreEqual(1, index.SlotOf("w1_01"));
        }

        [Test]
        public void WorldLookupsFindTheOwningWorld()
        {
            LevelIndex index = TwoWorlds();

            Assert.AreEqual("w2", index.WorldOf("w2_01").id);
            Assert.AreEqual(2, index.WorldNumberOf("w2_01"), "world numbers are 1-based");
            Assert.AreEqual(1, index.WorldNumberOf("w1_01"));
        }

        [Test]
        public void AnUnlistedLevelIsAbsentEverywhere()
        {
            LevelIndex index = TwoWorlds();

            Assert.IsFalse(index.Contains("w9_99"));
            Assert.IsNull(index.WorldOf("w9_99"));
            Assert.AreEqual(-1, index.SlotOf("w9_99"));
            Assert.AreEqual(-1, index.WorldNumberOf("w9_99"));
        }

        [Test]
        public void DraftsAreTheFilesTheManifestDoesNotList()
        {
            // A level being authored while the previous one is still under test. The editor
            // opens it; the game does not list it.
            LevelIndex index = TwoWorlds();

            List<string> drafts = index.DraftsAmong(new[] { "w1_01", "w1_02", "w2_01", "scratch" });

            CollectionAssert.AreEqual(new[] { "scratch" }, drafts);
        }

        [Test]
        public void QueriesSurviveAMalformedManifest()
        {
            // A hand-edited file must never take the editor down.
            var index = new LevelIndex { worlds = new[] { null, new LevelWorld { levels = null } } };

            Assert.DoesNotThrow(() => index.AllLevels());
            Assert.IsEmpty(index.AllLevels());
            Assert.IsFalse(index.Contains("anything"));
        }
```

Add `using System.Collections.Generic;` to the file's usings.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.LevelIndexTests"`
Expected: FAIL — compile error, `AllLevels` does not exist.

- [ ] **Step 3: Write the implementation**

Add `using System.Collections.Generic;` to `LevelIndex.cs` and these members to the `LevelIndex` class:

```csharp
        /// <summary>Every listed level, in play order, across all worlds.</summary>
        public IReadOnlyList<string> AllLevels()
        {
            var all = new List<string>();
            if (worlds == null) return all;

            foreach (LevelWorld world in worlds)
            {
                if (world?.levels == null) continue;

                foreach (string id in world.levels)
                    if (!string.IsNullOrEmpty(id)) all.Add(id);
            }

            return all;
        }

        public bool Contains(string id) => WorldOf(id) != null;

        /// <summary>The world listing this level, or null. Malformed entries are skipped.</summary>
        public LevelWorld WorldOf(string id)
        {
            if (string.IsNullOrEmpty(id) || worlds == null) return null;

            foreach (LevelWorld world in worlds)
            {
                if (world?.levels == null) continue;

                foreach (string listed in world.levels)
                    if (listed == id) return world;
            }

            return null;
        }

        /// <summary>Position within its own world, 0-based. -1 when unlisted.</summary>
        public int SlotOf(string id)
        {
            LevelWorld world = WorldOf(id);
            if (world == null) return -1;

            for (int i = 0; i < world.levels.Length; i++)
                if (world.levels[i] == id) return i;

            return -1;
        }

        /// <summary>
        /// Which world this is, counting from 1 - the number a level file's `world` field is
        /// expected to match, and what the lint compares against.
        /// </summary>
        public int WorldNumberOf(string id)
        {
            LevelWorld owner = WorldOf(id);
            if (owner == null) return -1;

            for (int i = 0; i < worlds.Length; i++)
                if (ReferenceEquals(worlds[i], owner)) return i + 1;

            return -1;
        }

        /// <summary>
        /// Files present on disk that the manifest does not list - drafts. Order follows the
        /// input, so a sorted listing in gives a sorted listing out.
        /// </summary>
        public List<string> DraftsAmong(IReadOnlyList<string> onDisk)
        {
            var drafts = new List<string>();
            if (onDisk == null) return drafts;

            foreach (string id in onDisk)
            {
                if (string.IsNullOrEmpty(id) || id == FileName) continue;
                if (!Contains(id)) drafts.Add(id);
            }

            return drafts;
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.LevelIndexTests"`
Expected: PASS, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Level/LevelIndex.cs Assets/_Project/Tests/EditMode/LevelIndexTests.cs
git commit -m "Add manifest queries: order, world lookup, drafts"
```

---

## Task 3: Loading and saving the manifest

**Files:**
- Modify: `Assets/_Project/Scripts/Sim/Level/ILevelSource.cs`
- Modify: `Assets/_Project/Scripts/Runtime/FileLevelSource.cs`
- Test: `Assets/_Project/Tests/EditMode/LevelIndexSourceTests.cs` (create)

**Interfaces:**
- Consumes: `LevelIndex` (Tasks 1–2), the existing `ILevelSource`
- Produces: on `ILevelSource` — `bool TryLoadIndex(out LevelIndex index)` and `void SaveIndex(LevelIndex index)`. `FileLevelSource.List()` no longer returns `"index"`.

**Note on this task's test:** it writes real files, so it uses a temporary directory and deletes it in `[TearDown]`. `FileLevelSource` already takes a directory in its constructor for exactly this reason.

- [ ] **Step 1: Write the failing tests**

Create `Assets/_Project/Tests/EditMode/LevelIndexSourceTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using KeepyUppy.Runtime;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Reading and writing the manifest through ILevelSource.
    ///
    /// Uses a real temporary directory rather than a mock: the thing most likely to break is
    /// the file layout - what is named what, and what List() does or does not return.
    /// </summary>
    public sealed class LevelIndexSourceTests
    {
        string _directory;
        FileLevelSource _source;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "keepyuppy_levels_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_directory);
            _source = new FileLevelSource(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void AnAbsentManifestIsNotAnError()
        {
            // A project with levels and no manifest yet is a normal state, not a failure.
            Assert.IsFalse(_source.TryLoadIndex(out LevelIndex index));
            Assert.IsNull(index);
        }

        [Test]
        public void TheManifestRoundTripsThroughDisk()
        {
            var written = new LevelIndex
            {
                version = LevelIndex.CurrentVersion,
                worlds = new[]
                {
                    new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_02", "w1_01" } },
                },
            };

            _source.SaveIndex(written);

            Assert.IsTrue(_source.TryLoadIndex(out LevelIndex read));
            CollectionAssert.AreEqual(new[] { "w1_02", "w1_01" }, read.worlds[0].levels,
                                      "the declared order must survive the round trip");
        }

        [Test]
        public void TheManifestIsWrittenReadable()
        {
            // The readable diff is the point of the format (CLAUDE.md commit conventions).
            _source.SaveIndex(LevelIndex.Default());

            string json = File.ReadAllText(Path.Combine(_directory, "index.json"));
            StringAssert.Contains("\n", json, "the manifest must not be minified");
        }

        [Test]
        public void TheManifestIsNotListedAsALevel()
        {
            // index.json lives in the levels directory and is not a level. Without this,
            // every level list in the game and the editor grows a phantom entry.
            _source.SaveIndex(LevelIndex.Default());
            _source.Save("w1_01", new LevelData());

            IReadOnlyList<string> ids = _source.List();

            CollectionAssert.Contains(ids, "w1_01");
            CollectionAssert.DoesNotContain(ids, LevelIndex.FileName);
        }

        [Test]
        public void ACorruptManifestFailsSoftly()
        {
            // Hand-edited files exist. A broken one must not take the editor down.
            File.WriteAllText(Path.Combine(_directory, "index.json"), "{ not json");

            Assert.DoesNotThrow(() => _source.TryLoadIndex(out _));
            Assert.IsFalse(_source.TryLoadIndex(out _));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.LevelIndexSourceTests"`
Expected: FAIL — compile error, `TryLoadIndex` does not exist.

- [ ] **Step 3: Add the interface members**

In `Assets/_Project/Scripts/Sim/Level/ILevelSource.cs`, add inside the interface, after `Save`:

```csharp
        /// <summary>
        /// The manifest declaring which levels are in the game and in what order.
        ///
        /// False when there is no manifest yet, which is a normal state for a project with
        /// levels and no list, not a failure.
        /// </summary>
        bool TryLoadIndex(out LevelIndex index);

        /// <summary>Writes the manifest. Throws if <see cref="CanSave"/> is false.</summary>
        void SaveIndex(LevelIndex index);
```

- [ ] **Step 4: Implement in FileLevelSource**

In `Assets/_Project/Scripts/Runtime/FileLevelSource.cs`, add these members before `PathFor`:

```csharp
        public bool TryLoadIndex(out LevelIndex index)
        {
            index = null;

            string path = PathFor(LevelIndex.FileName);
            if (!File.Exists(path)) return false;

            try
            {
                index = JsonUtility.FromJson<LevelIndex>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                // Hand-edited files exist. A broken manifest must not take the editor down.
                Debug.LogWarning($"[level] could not read the manifest: {e.Message}");
                index = null;
                return false;
            }

            return index != null;
        }

        public void SaveIndex(LevelIndex index)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));

            System.IO.Directory.CreateDirectory(_directory);
            File.WriteAllText(PathFor(LevelIndex.FileName), JsonUtility.ToJson(index, prettyPrint: true));
        }
```

And in `List()`, skip the manifest. Replace the loop body:

```csharp
            foreach (string file in files)
            {
                string id = Path.GetFileNameWithoutExtension(file);

                // The manifest shares the directory with the levels and is not one of them.
                if (id == LevelIndex.FileName) continue;

                ids.Add(id);
            }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.LevelIndexSourceTests"`
Expected: PASS, exit code 0.

- [ ] **Step 6: Run the whole suite**

Run: `./test.sh`
Expected: PASS, exit code 0. Every existing test still passes — `List()` changed, and `LevelFormatTests` exercises it.

- [ ] **Step 7: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Level/ILevelSource.cs Assets/_Project/Scripts/Runtime/FileLevelSource.cs Assets/_Project/Tests/EditMode/LevelIndexSourceTests.cs Assets/_Project/Tests/EditMode/LevelIndexSourceTests.cs.meta
git commit -m "Load and save the level manifest through ILevelSource"
```

---

## Task 4: Manifest lint

**Files:**
- Create: `Assets/_Project/Scripts/Sim/Level/LevelIndexValidator.cs`
- Test: `Assets/_Project/Tests/EditMode/LevelIndexValidatorTests.cs` (create)

**Interfaces:**
- Consumes: `LevelIndex` (Tasks 1–2), `LevelData`
- Produces: `LevelIndexValidator.Validate(LevelIndex index, IReadOnlyList<string> onDisk)` returning `List<string>`; `LevelIndexValidator.ValidateSlot(LevelData level, LevelIndex index)` returning `List<string>`

- [ ] **Step 1: Write the failing tests**

Create `Assets/_Project/Tests/EditMode/LevelIndexValidatorTests.cs`:

```csharp
using System.Collections.Generic;
using KeepyUppy.Sim.Level;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Manifest lint. A declared list is only better than a derived one if the tooling keeps
    /// it current, and these are what catch it going stale.
    ///
    /// Warnings, never errors: a half-finished level set must always be saveable, exactly as
    /// LevelValidator treats a half-finished level.
    /// </summary>
    public sealed class LevelIndexValidatorTests
    {
        static LevelIndex Index(params string[] levels) => new LevelIndex
        {
            version = LevelIndex.CurrentVersion,
            worlds = new[] { new LevelWorld { id = "w1", name = "One", levels = levels } },
        };

        [Test]
        public void ACleanManifestProducesNoWarnings()
        {
            List<string> warnings = LevelIndexValidator.Validate(
                Index("w1_01", "w1_02"), new[] { "w1_01", "w1_02" });

            CollectionAssert.IsEmpty(warnings);
        }

        [Test]
        public void AListedLevelMissingFromDiskIsAWarning()
        {
            List<string> warnings = LevelIndexValidator.Validate(
                Index("w1_01", "w1_02"), new[] { "w1_01" });

            Assert.IsTrue(warnings.Exists(w => w.Contains("w1_02")),
                          "the game would show a level that cannot be loaded");
        }

        [Test]
        public void ADraftIsReportedButNotAsAProblem()
        {
            // A file on disk that is not listed is deliberate, so it is mentioned and marked
            // as a draft rather than reported as an error.
            List<string> warnings = LevelIndexValidator.Validate(
                Index("w1_01"), new[] { "w1_01", "scratch" });

            Assert.IsTrue(warnings.Exists(w => w.Contains("scratch") && w.Contains("draft")));
        }

        [Test]
        public void ALevelListedTwiceIsAWarning()
        {
            List<string> warnings = LevelIndexValidator.Validate(
                Index("w1_01", "w1_01"), new[] { "w1_01" });

            Assert.IsTrue(warnings.Exists(w => w.Contains("more than once")));
        }

        [Test]
        public void AnEmptyWorldIsAWarning()
        {
            var index = new LevelIndex
            {
                worlds = new[] { new LevelWorld { id = "w1", name = "One", levels = new string[0] } },
            };

            Assert.IsTrue(LevelIndexValidator.Validate(index, new string[0])
                              .Exists(w => w.Contains("no levels")));
        }

        [Test]
        public void AVersionFromTheFutureIsAWarning()
        {
            var index = LevelIndex.Default();
            index.version = LevelIndex.CurrentVersion + 1;

            Assert.IsTrue(LevelIndexValidator.Validate(index, new[] { "w1_01" })
                              .Exists(w => w.Contains("version")));
        }

        [Test]
        public void SlotDriftIsAWarning()
        {
            // The manifest is authoritative; the fields inside the level are advisory. When
            // they disagree the file is stale, and the lint is what says so.
            LevelIndex index = Index("w1_01", "w1_02");
            var level = new LevelData { id = "w1_02", world = 1, index = 1 };

            List<string> warnings = LevelIndexValidator.ValidateSlot(level, index);

            Assert.IsTrue(warnings.Exists(w => w.Contains("index")),
                          "w1_02 sits in slot 2 but the file claims index 1");
        }

        [Test]
        public void MatchingSlotFieldsProduceNoWarnings()
        {
            LevelIndex index = Index("w1_01", "w1_02");
            var level = new LevelData { id = "w1_02", world = 1, index = 2 };

            CollectionAssert.IsEmpty(LevelIndexValidator.ValidateSlot(level, index));
        }

        [Test]
        public void AnUnlistedLevelHasNoSlotToDriftFrom()
        {
            // A draft is not stale, it is just not in the game yet.
            LevelIndex index = Index("w1_01");
            var level = new LevelData { id = "scratch", world = 9, index = 9 };

            CollectionAssert.IsEmpty(LevelIndexValidator.ValidateSlot(level, index));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.LevelIndexValidatorTests"`
Expected: FAIL — compile error, `LevelIndexValidator` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/_Project/Scripts/Sim/Level/LevelIndexValidator.cs`:

```csharp
using System.Collections.Generic;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Manifest lint (spec: menus and the level list).
    ///
    /// A declared list beats a derived one only while the tooling keeps it current. These are
    /// the checks that catch it going stale - a level listed but deleted, a file listed twice,
    /// a level file whose own fields contradict where the manifest puts it.
    ///
    /// **Warnings, never errors.** A half-finished level set must stay saveable, exactly as
    /// LevelValidator treats a half-finished level.
    /// </summary>
    public static class LevelIndexValidator
    {
        /// <summary>
        /// Checks the manifest against what is actually on disk.
        /// </summary>
        /// <param name="onDisk">Level ids from ILevelSource.List(), which excludes the manifest.</param>
        public static List<string> Validate(LevelIndex index, IReadOnlyList<string> onDisk)
        {
            var warnings = new List<string>();

            if (index == null) { warnings.Add("no manifest"); return warnings; }

            if (index.version > LevelIndex.CurrentVersion)
            {
                warnings.Add($"manifest version {index.version} is newer than this build " +
                             $"understands ({LevelIndex.CurrentVersion})");
            }

            if (index.worlds == null || index.worlds.Length == 0)
            {
                warnings.Add("manifest lists no worlds");
                return warnings;
            }

            var seen = new HashSet<string>();
            var duplicates = new List<string>();

            foreach (LevelWorld world in index.worlds)
            {
                if (world == null) { warnings.Add("manifest has an empty world entry"); continue; }

                if (string.IsNullOrEmpty(world.id)) warnings.Add("a world has no id");

                if (world.levels == null || world.levels.Length == 0)
                {
                    warnings.Add($"world '{world.id}' has no levels");
                    continue;
                }

                foreach (string id in world.levels)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        warnings.Add($"world '{world.id}' has an empty level id");
                        continue;
                    }

                    if (!seen.Add(id)) duplicates.Add(id);

                    if (onDisk != null && !Contains(onDisk, id))
                        warnings.Add($"'{id}' is listed in world '{world.id}' but no such level file exists");
                }
            }

            // Sorted so the warning list is stable between runs, as LevelValidator does.
            duplicates.Sort(System.StringComparer.Ordinal);
            foreach (string id in duplicates) warnings.Add($"'{id}' is listed more than once");

            foreach (string draft in index.DraftsAmong(onDisk))
                warnings.Add($"'{draft}' is on disk but not in the manifest (draft)");

            return warnings;
        }

        /// <summary>
        /// Checks one level file's advisory world and index fields against where the manifest
        /// actually puts it. An unlisted level is a draft and has no slot to drift from.
        /// </summary>
        public static List<string> ValidateSlot(LevelData level, LevelIndex index)
        {
            var warnings = new List<string>();
            if (level == null || index == null) return warnings;

            int slot = index.SlotOf(level.id);
            if (slot < 0) return warnings;

            int world = index.WorldNumberOf(level.id);

            if (level.world != world)
                warnings.Add($"'{level.id}' says world {level.world} but the manifest puts it in world {world}");

            // Slots are 0-based; the schema's index field is 1-based, as the filenames imply.
            if (level.index != slot + 1)
                warnings.Add($"'{level.id}' says index {level.index} but the manifest puts it at {slot + 1}");

            return warnings;
        }

        static bool Contains(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == id) return true;

            return false;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.LevelIndexValidatorTests"`
Expected: PASS, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/Sim/Level/LevelIndexValidator.cs Assets/_Project/Scripts/Sim/Level/LevelIndexValidator.cs.meta Assets/_Project/Tests/EditMode/LevelIndexValidatorTests.cs Assets/_Project/Tests/EditMode/LevelIndexValidatorTests.cs.meta
git commit -m "Add manifest lint: membership, duplicates, slot drift"
```

---

## Task 5: The editor holds the manifest

**Files:**
- Modify: `Assets/_Project/Scripts/LevelEditor/EditorSession.cs`
- Test: `Assets/_Project/Tests/EditMode/EditorIndexTests.cs` (create)

**Interfaces:**
- Consumes: `LevelIndex` (Tasks 1–2)
- Produces: on `EditorSession` — `LevelIndex Index { get; }`, `void LoadIndex(LevelIndex index)`, `bool IndexDirty { get; }`, `void MarkIndexSaved()`, `LevelWorld AddLevel(string id, string worldId)`, `bool RemoveLevel(string id)`, `bool MoveLevel(string id, int direction)`

**Why the index is separate from `Dirty`:** the existing `Dirty` flag tracks the *level* being edited and is cleared by `MarkSaved()` after `Levels.Save`. The manifest is a different file with a different save, so conflating them would make saving a level appear to save the manifest.

- [ ] **Step 1: Write the failing tests**

Create `Assets/_Project/Tests/EditMode/EditorIndexTests.cs`:

```csharp
using KeepyUppy.LevelEditing;
using KeepyUppy.Sim.Level;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The editor maintaining the manifest.
    ///
    /// This is what makes a declared level list safe: without the tool keeping it current, a
    /// manifest is just a file that goes stale.
    /// </summary>
    public sealed class EditorIndexTests
    {
        static EditorSession Session()
        {
            var session = new EditorSession();
            session.LoadIndex(new LevelIndex
            {
                version = LevelIndex.CurrentVersion,
                worlds = new[]
                {
                    new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_01", "w1_02" } },
                    new LevelWorld { id = "w2", name = "Two", levels = new[] { "w2_01" } },
                },
            });
            return session;
        }

        [Test]
        public void AddingALevelAppendsItToItsWorld()
        {
            EditorSession session = Session();

            session.AddLevel("w1_03", "w1");

            CollectionAssert.AreEqual(new[] { "w1_01", "w1_02", "w1_03" },
                                      session.Index.WorldOf("w1_03").levels);
            Assert.IsTrue(session.IndexDirty);
        }

        [Test]
        public void AddingToAnUnknownWorldCreatesIt()
        {
            // Authoring world 3 before world 3 exists is normal; refusing would just mean
            // hand-editing the manifest, which is what this exists to avoid.
            EditorSession session = Session();

            LevelWorld world = session.AddLevel("w3_01", "w3");

            Assert.AreEqual("w3", world.id);
            CollectionAssert.AreEqual(new[] { "w3_01" }, world.levels);
            Assert.AreEqual(3, session.Index.worlds.Length);
        }

        [Test]
        public void AddingALevelTwiceIsHarmless()
        {
            EditorSession session = Session();

            session.AddLevel("w1_01", "w1");

            CollectionAssert.AreEqual(new[] { "w1_01", "w1_02" }, session.Index.WorldOf("w1_01").levels);
        }

        [Test]
        public void RemovingALevelUnlistsItWithoutDeletingTheFile()
        {
            // Unlisting turns a level into a draft. The file is left alone on purpose: losing
            // work to a mis-click is worse than an unlisted file.
            EditorSession session = Session();

            Assert.IsTrue(session.RemoveLevel("w1_01"));
            Assert.IsFalse(session.Index.Contains("w1_01"));
            Assert.IsFalse(session.RemoveLevel("w1_01"), "removing twice is harmless");
        }

        [Test]
        public void MovingALevelReordersItWithinItsWorld()
        {
            EditorSession session = Session();

            Assert.IsTrue(session.MoveLevel("w1_02", -1));
            CollectionAssert.AreEqual(new[] { "w1_02", "w1_01" }, session.Index.WorldOf("w1_02").levels);
            Assert.IsTrue(session.IndexDirty);
        }

        [Test]
        public void MovingPastTheEndOfAWorldDoesNothing()
        {
            // Deliberately does NOT move the level into the next world. Reordering and
            // re-homing are different intentions, and the second needs its own gesture.
            EditorSession session = Session();

            Assert.IsFalse(session.MoveLevel("w1_01", -1), "already first");
            Assert.IsFalse(session.MoveLevel("w2_01", 1), "only level in its world");
            CollectionAssert.AreEqual(new[] { "w1_01", "w1_02" }, session.Index.WorldOf("w1_01").levels);
        }

        [Test]
        public void MarkIndexSavedClearsTheFlag()
        {
            EditorSession session = Session();
            session.AddLevel("w1_03", "w1");

            session.MarkIndexSaved();

            Assert.IsFalse(session.IndexDirty);
        }

        [Test]
        public void TheLevelDirtyFlagAndTheIndexDirtyFlagAreSeparate()
        {
            // They are different files with different saves. Conflating them would make
            // saving a level look like it saved the manifest.
            EditorSession session = Session();
            session.Load(new LevelData { tiles = new string[0] });

            session.AddLevel("w1_03", "w1");

            Assert.IsTrue(session.IndexDirty);
            Assert.IsFalse(session.Dirty, "adding to the manifest does not edit the level");
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.EditorIndexTests"`
Expected: FAIL — compile error, `LoadIndex` does not exist.

- [ ] **Step 3: Write the implementation**

In `Assets/_Project/Scripts/LevelEditor/EditorSession.cs`, add these members after the `ExitSelected` field:

```csharp
        /// <summary>
        /// The manifest (spec: menus and the level list). Held here rather than in the
        /// controller so every mutation is testable without a scene, as with everything else
        /// in this class.
        /// </summary>
        public LevelIndex Index { get; private set; } = new LevelIndex();

        /// <summary>
        /// Unsaved manifest changes. Deliberately separate from <see cref="Dirty"/>: the
        /// manifest is a different file with a different save, and conflating them would make
        /// saving a level appear to save the list.
        /// </summary>
        public bool IndexDirty { get; private set; }

        public void LoadIndex(LevelIndex index)
        {
            Index = index ?? new LevelIndex();
            IndexDirty = false;
        }

        public void MarkIndexSaved() => IndexDirty = false;

        /// <summary>
        /// Lists a level at the end of a world, creating the world if it does not exist yet -
        /// authoring world 3 before world 3 exists is normal, and refusing would only mean
        /// hand-editing the manifest, which is what this exists to avoid.
        /// </summary>
        public LevelWorld AddLevel(string id, string worldId)
        {
            if (string.IsNullOrEmpty(id)) return null;

            LevelWorld existing = Index.WorldOf(id);
            if (existing != null) return existing;

            LevelWorld world = FindWorld(worldId);

            if (world == null)
            {
                world = new LevelWorld { id = worldId, name = worldId, levels = new string[0] };

                var worlds = new List<LevelWorld>(Index.worlds ?? new LevelWorld[0]) { world };
                Index.worlds = worlds.ToArray();
            }

            var levels = new List<string>(world.levels ?? new string[0]) { id };
            world.levels = levels.ToArray();

            IndexDirty = true;
            return world;
        }

        /// <summary>
        /// Unlists a level, turning it into a draft. The file is deliberately left on disk:
        /// losing authored work to a mis-click is worse than an unlisted file.
        /// </summary>
        public bool RemoveLevel(string id)
        {
            LevelWorld world = Index.WorldOf(id);
            if (world == null) return false;

            var levels = new List<string>(world.levels);
            levels.Remove(id);
            world.levels = levels.ToArray();

            IndexDirty = true;
            return true;
        }

        /// <summary>
        /// Moves a level one slot within its own world. Does not carry it into the next
        /// world: reordering and re-homing are different intentions.
        /// </summary>
        public bool MoveLevel(string id, int direction)
        {
            LevelWorld world = Index.WorldOf(id);
            if (world == null || direction == 0) return false;

            int from = Index.SlotOf(id);
            int to = from + (direction < 0 ? -1 : 1);

            if (to < 0 || to >= world.levels.Length) return false;

            world.levels[from] = world.levels[to];
            world.levels[to] = id;

            IndexDirty = true;
            return true;
        }

        LevelWorld FindWorld(string worldId)
        {
            if (Index.worlds == null) return null;

            foreach (LevelWorld world in Index.worlds)
                if (world != null && world.id == worldId) return world;

            return null;
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.EditorIndexTests"`
Expected: PASS, exit code 0.

- [ ] **Step 5: Commit**

```bash
git add Assets/_Project/Scripts/LevelEditor/EditorSession.cs Assets/_Project/Tests/EditMode/EditorIndexTests.cs Assets/_Project/Tests/EditMode/EditorIndexTests.cs.meta
git commit -m "Editor session holds and edits the level manifest"
```

---

## Task 6: The level browser panel

**Files:**
- Modify: `Assets/_Project/Scripts/LevelEditor/LevelEditorController.cs`
- Modify: `Assets/_Project/Scripts/LevelEditor/EditorTool.cs` — no change needed; listed here so the implementer does not go looking for one

**Interfaces:**
- Consumes: `EditorSession.Index`, `AddLevel`, `MoveLevel`, `RemoveLevel`, `IndexDirty`, `MarkIndexSaved` (Task 5); `ILevelSource.TryLoadIndex`, `SaveIndex`, `List` (Task 3); `LevelIndexValidator.Validate` (Task 4)
- Produces: nothing consumed by later tasks

**This task has no EditMode test.** It is IMGUI plumbing on a MonoBehaviour, which the existing loop does not cover — the same reason `LevelEditorController` has no tests today while `EditorSession` has many. All the logic it calls is already tested in Task 5. Verify by running the editor and using the panel.

- [ ] **Step 1: Add the browser state**

In `LevelEditorController.cs`, add beside the other overlay fields (near `_showPanel`):

```csharp
        bool _showBrowser;
        Rect _browserRect;
        string _newLevelId = "w1_02";
        string _newWorldId = "w1";
```

- [ ] **Step 2: Load the manifest when the editor starts**

Replace the body of `Begin`:

```csharp
        public void Begin(LevelData level)
        {
            // Share the world's grid, so edits are visible the moment they are made rather
            // than at the next Tab.
            Session = new EditorSession(Host.World.Grid);
            Session.Load(level);

            // The manifest decides what is in the game; without it the browser can only offer
            // whatever files happen to be present, which is the state this replaces.
            Session.LoadIndex(Host.Levels.TryLoadIndex(out LevelIndex index) ? index : LevelIndex.Default());

            EnterEdit();
        }
```

- [ ] **Step 3: Bind the browser to a key**

In `HandleEditorKeys`, beside the other overlay toggles:

```csharp
            if (kb.lKey.wasPressedThisFrame) _showBrowser = !_showBrowser;
```

`L` is free: gameplay reserves `Space W A D J R Esc` and the arrows, and the editor already uses `U G H T P`, digits and `Ctrl+S/Z/Y`.

- [ ] **Step 4: Keep clicks off the room**

In `PointerOverUi`, extend the final return so a click in the browser does not paint through it:

```csharp
            if (_toggleRect.Contains(point)) return true;
            if (_showBrowser && _browserRect.Contains(point)) return true;
            if (!_showPanel) return false;

            return _paletteRect.Contains(point) || _inspectorRect.Contains(point);
```

- [ ] **Step 5: Draw the browser**

Add these methods before `DrawStatus`:

```csharp
        /// <summary>
        /// The manifest, as a list you can open, reorder and add to (spec: menus and the
        /// level list). IMGUI, like every other dev panel - spec 2 is explicit that polish is
        /// not built here.
        /// </summary>
        void DrawBrowser()
        {
            _browserRect = new Rect(8f, 60f, 300f, 420f);
            GUILayout.BeginArea(_browserRect, GUI.skin.box);

            GUILayout.Label($"levels{(Session.IndexDirty ? " *" : "")}");

            if (Session.Index.worlds != null)
            {
                foreach (LevelWorld world in Session.Index.worlds)
                {
                    if (world?.levels == null) continue;

                    GUILayout.Space(4f);
                    GUILayout.Label($"{world.id}  {world.name}");

                    foreach (string id in new List<string>(world.levels)) DrawBrowserRow(id);
                }
            }

            DrawDrafts();
            GUILayout.Space(6f);
            DrawNewLevelRow();

            if (GUILayout.Button("save list"))
            {
                Host.Levels.SaveIndex(Session.Index);
                Session.MarkIndexSaved();
                _warnings = LevelIndexValidator.Validate(Session.Index, Host.Levels.List());
                _status = "saved the level list";
            }

            GUILayout.EndArea();
        }

        void DrawBrowserRow(string id)
        {
            GUILayout.BeginHorizontal();

            bool current = id == LevelId;
            if (GUILayout.Button(current ? $"[{id}]" : id, GUILayout.Width(130f))) Open(id);

            if (GUILayout.Button("^", GUILayout.Width(24f))) Session.MoveLevel(id, -1);
            if (GUILayout.Button("v", GUILayout.Width(24f))) Session.MoveLevel(id, 1);
            if (GUILayout.Button("unlist", GUILayout.Width(56f))) Session.RemoveLevel(id);

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Files on disk the manifest does not list. Shown so a draft is discoverable rather
        /// than invisible, with one click to put it in the game.
        /// </summary>
        void DrawDrafts()
        {
            List<string> drafts = Session.Index.DraftsAmong(Host.Levels.List());
            if (drafts.Count == 0) return;

            GUILayout.Space(4f);
            GUILayout.Label("drafts");

            foreach (string id in drafts)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(id, GUILayout.Width(130f))) Open(id);
                if (GUILayout.Button("add to list", GUILayout.Width(90f)))
                    Session.AddLevel(id, _newWorldId);
                GUILayout.EndHorizontal();
            }
        }

        void DrawNewLevelRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("new", GUILayout.Width(32f));
            _newLevelId = GUILayout.TextField(_newLevelId, GUILayout.Width(90f));
            _newWorldId = GUILayout.TextField(_newWorldId, GUILayout.Width(40f));

            if (GUILayout.Button("create", GUILayout.Width(60f))) CreateLevel();

            GUILayout.EndHorizontal();
        }
```

- [ ] **Step 6: Open and create**

Add these methods after `Save`:

```csharp
        /// <summary>
        /// Switches the level being edited. Unsaved work in the current one is kept by saving
        /// it first: losing a room to a mis-click in a list is not a trade worth making.
        /// </summary>
        void Open(string id)
        {
            if (id == LevelId) return;

            if (Session.Dirty) Save();

            if (!Host.Levels.TryLoad(id, out LevelData level))
            {
                _status = $"could not load '{id}'";
                return;
            }

            LevelId = id;
            Session.Load(level);
            Host.ApplyLevel(Session.Flush());
            EnterEdit();

            _status = $"opened {id}";
        }

        /// <summary>
        /// Creates an empty room, writes it, lists it, and opens it. All three: a new level
        /// that is not in the manifest is a draft nobody asked for.
        /// </summary>
        void CreateLevel()
        {
            string id = _newLevelId;

            if (string.IsNullOrEmpty(id)) { _status = "a new level needs an id"; return; }

            if (Host.Levels.TryLoad(id, out _)) { _status = $"'{id}' already exists"; return; }

            var grid = new TileGrid();
            grid.FillBorder();

            var level = new LevelData
            {
                id = id,
                name = id,
                tiles = grid.ToRows(),
            };

            Host.Levels.Save(id, level);
            Session.AddLevel(id, _newWorldId);
            Host.Levels.SaveIndex(Session.Index);
            Session.MarkIndexSaved();

            Open(id);
            _status = $"created {id}";
        }
```

- [ ] **Step 7: Draw it and mention the key**

In `OnGUI`, after `DrawPanelToggle();`:

```csharp
            if (_showBrowser) DrawBrowser();
```

And in `DrawStatus`, extend the key list so the binding is discoverable:

```csharp
                      $"EDIT {LevelId}{dirty}   Tab play   L levels   U tools   G grid   H hitboxes   T safe area   Ctrl+S save   {_status}");
```

- [ ] **Step 8: Run the whole suite**

Run: `./test.sh`
Expected: PASS, exit code 0. No test covers this panel, but it must still compile with no new warnings.

- [ ] **Step 9: Verify by hand**

Open the project, press Play, press `L`. Check: the manifest's levels are listed in manifest order; `^`/`v` reorder them; `create` makes a new room, lists it and opens it; a file unlisted with `unlist` reappears under "drafts"; `save list` writes `index.json` readably.

- [ ] **Step 10: Commit**

```bash
git add Assets/_Project/Scripts/LevelEditor/LevelEditorController.cs
git commit -m "Add the editor level browser: open, create, reorder, unlist"
```

---

## Task 7: Boot from the manifest

**Files:**
- Modify: `Assets/_Project/Scripts/Runtime/SimBootstrap.cs:120-132`
- Test: `Assets/_Project/Tests/EditMode/LevelIndexSourceTests.cs` (extend)

**Interfaces:**
- Consumes: everything from Tasks 1–4
- Produces: nothing consumed by later tasks

- [ ] **Step 1: Write the failing test**

Append to `LevelIndexSourceTests.cs`, inside the class:

```csharp
        [Test]
        public void TheSeededManifestAndTheSeededLevelAgree()
        {
            // SimBootstrap seeds w1_01 and the default manifest when the directory is empty.
            // If those two disagree, a fresh clone boots into an empty game - which is a
            // miserable first five minutes and entirely preventable here.
            _source.SaveIndex(LevelIndex.Default());
            _source.Save(SimBootstrap.StartingLevel, new LevelData());

            Assert.IsTrue(_source.TryLoadIndex(out LevelIndex index));
            CollectionAssert.Contains(index.AllLevels(), SimBootstrap.StartingLevel);

            CollectionAssert.IsEmpty(LevelIndexValidator.Validate(index, _source.List()));
        }
```

- [ ] **Step 2: Run the test to verify it fails or passes**

Run: `./test.sh -testFilter "KeepyUppy.SimTests.LevelIndexSourceTests"`
Expected: PASS already — `LevelIndex.Default()` names `w1_01` and `SimBootstrap.StartingLevel` is `"w1_01"`. This test exists to *keep* them in agreement; if it fails, one of the two was changed without the other and that is the bug it is for.

- [ ] **Step 3: Seed the manifest at boot**

In `SimBootstrap.cs`, immediately after the existing `_levelSource.SaveIfAbsent(StartingLevel, SeedLevel());` line:

```csharp
            // The manifest decides what is in the game. Seeded alongside the level so a fresh
            // clone has a coherent level list rather than a room nothing lists.
            if (!_levelSource.TryLoadIndex(out LevelIndex index))
            {
                index = LevelIndex.Default();
                _levelSource.SaveIndex(index);
            }

            // The starting level is the manifest's first, so adding a level before w1_01 in
            // the editor changes what the game boots into - which is what a level list is for.
            IReadOnlyList<string> listed = index.AllLevels();
            if (listed.Count > 0) StartingLevel = listed[0];
```

Add `using System.Collections.Generic;` to the file if it is not already there.

- [ ] **Step 4: Run the whole suite**

Run: `./test.sh`
Expected: PASS, exit code 0.

- [ ] **Step 5: Verify by hand**

Delete `Assets/StreamingAssets/levels/index.json`, press Play, and confirm it is recreated and the game still boots into `w1_01`.

- [ ] **Step 6: Commit**

```bash
git add Assets/_Project/Scripts/Runtime/SimBootstrap.cs Assets/_Project/Tests/EditMode/LevelIndexSourceTests.cs
git commit -m "Seed the manifest at boot and take the starting level from it"
```

---

## Task 8: Documentation

**Files:**
- Modify: `docs/level-editor-spec.md` — §3 (the data model) and §4 (editing keys)
- Modify: `CLAUDE.md` — the level-loading section

- [ ] **Step 1: Document the manifest in the editor spec**

In `docs/level-editor-spec.md`, add to §3 after the schema, and add `L` to the §4 editing-keys table:

```markdown
### The manifest — 2026-08-24, as built

`levels/index.json` declares which levels are in the game and in what order:

```json
{
    "version": 1,
    "worlds": [
        { "id": "w1", "name": "The Living Room", "levels": ["w1_01"] }
    ]
}
```

**The manifest is authoritative for ordering.** The `world` and `index` fields inside each
level file are advisory and lint-checked against it; nothing reads them to decide what comes
next.

**A level on disk but absent from the manifest is a draft** — the editor opens it, the game
does not list it. That is the capability a derived list cannot offer, and it is why this file
exists rather than the game scanning the directory.

`L` opens the level browser: open, create, reorder, unlist, and save the list. Unlisting
never deletes the file.
```

- [ ] **Step 2: Document it in CLAUDE.md**

In `CLAUDE.md`, under "Level loading", after the `ILevelSource` bullets:

```markdown
Which levels are *in* the game is declared in `Assets/StreamingAssets/levels/index.json`, not
derived from the directory. A level file absent from the manifest is a draft: the editor opens
it, the game does not list it. The editor maintains the manifest — a declared list is only
better than a derived one while the tooling keeps it current.
```

- [ ] **Step 3: Run the whole suite one final time**

Run: `./test.sh`
Expected: PASS, exit code 0, with no new compile warnings. Confirm with `grep -c "warning CS" TestResults/test-run.log` returning `0`.

- [ ] **Step 4: Commit**

```bash
git add docs/level-editor-spec.md CLAUDE.md
git commit -m "Document the level manifest"
```

---

## Self-review notes

**Spec coverage.** §1 the manifest → Tasks 1–3. §5 editor integration → Tasks 5–6. §5 validator lints → Task 4. §6 phase A boundary → all tasks; §2 save data and §3 screens are phase B/C and deliberately absent. The spec's `PausePressed` and `IMenuInput` belong to phase B and appear nowhere here, which is correct.

**Not covered, and deliberately so:** `PackedLevelSource` is still unbuilt, so `TryLoadIndex` has exactly one implementation. The interface is what phase B and the web build need; a second implementation without a build step to feed it would be speculative.

**Known gap to watch:** Task 6 is untested IMGUI plumbing. Every branch of it calls a method tested in Task 5, so the risk is confined to wiring, and Step 9 is a manual check for exactly that.
