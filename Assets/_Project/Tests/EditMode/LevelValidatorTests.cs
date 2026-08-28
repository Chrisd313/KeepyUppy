using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Level lint (level-editor-spec 7): cheap to write, saves hours. Warnings only —
    /// a half-finished level must always be saveable.
    /// </summary>
    public sealed class LevelValidatorTests
    {
        static LevelData Valid()
        {
            var grid = new TileGrid();
            grid.FillBorder();

            return new LevelData
            {
                tiles = grid.ToRows(),
                spawn = new LevelSpawn
                {
                    player = new[] { 4f, 2f },
                    balloon = new[] { 6f, 13f },
                    balloon_inflation = "nominal",
                },
                exit = new LevelExit { pos = new[] { 29f, 4f }, shape = "ring", diameter = 1.6f },
                meta = new LevelMeta { par_touches = 7 },
            };
        }

        static void AssertWarns(LevelData level, string fragment)
        {
            List<string> warnings = LevelValidator.Validate(level);
            Assert.IsTrue(warnings.Exists(w => w.Contains(fragment)),
                          $"expected a warning containing '{fragment}', got: {string.Join(" | ", warnings)}");
        }

        [Test]
        public void AValidLevel_ProducesNoWarnings()
        {
            CollectionAssert.IsEmpty(LevelValidator.Validate(Valid()));
        }

        [Test]
        public void MissingSpawns_Warn()
        {
            LevelData level = Valid();
            level.spawn.player = null;
            AssertWarns(level, "player spawn has no position");
        }

        [Test]
        public void SpawnOutsideTheRoom_Warns()
        {
            LevelData level = Valid();
            level.spawn.player = new[] { 99f, 2f };
            AssertWarns(level, "outside the room");
        }

        [Test]
        public void BalloonSpawnInsideGeometry_Warns()
        {
            // Spawning in a wall kills the balloon before the player moves — a mistake that
            // costs a whole playtest to diagnose.
            LevelData level = Valid();
            level.spawn.balloon = new[] { 0.2f, 0.2f };
            AssertWarns(level, "overlaps solid geometry");
        }

        [Test]
        public void BalloonSpawnOnAHazard_Warns()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(6, 12, 3, 1, TileKind.SpikeUp);

            LevelData level = Valid();
            level.tiles = grid.ToRows();
            level.spawn.balloon = new[] { 7f, 12.7f };

            AssertWarns(level, "overlaps a hazard");
        }

        [Test]
        public void WrongRowCountAndWidth_Warn()
        {
            LevelData level = Valid();
            level.tiles = new[] { "###" };

            List<string> warnings = LevelValidator.Validate(level);
            Assert.IsTrue(warnings.Exists(w => w.Contains("expected 18 tile rows")));
            Assert.IsTrue(warnings.Exists(w => w.Contains("chars, expected 32")));
        }

        [Test]
        public void MissingExit_Warns()
        {
            LevelData level = Valid();
            level.exit = null;
            AssertWarns(level, "no exit");
        }

        [Test]
        public void UnknownExitShape_Warns()
        {
            LevelData level = Valid();
            level.exit.shape = "hexagon";
            AssertWarns(level, "unknown exit shape");
        }

        [Test]
        public void DuplicateIds_Warn()
        {
            LevelData level = Valid();
            level.zones = new[]
            {
                new LevelZone { id = "z01" },
                new LevelZone { id = "z01" },
            };

            AssertWarns(level, "duplicate id 'z01'");
        }

        [Test]
        public void LinkToAMissingId_Warns()
        {
            // start_state + links is the whole betrayal toolkit; a typo'd id silently
            // disarms the trap.
            LevelData level = Valid();
            level.triggers = new[] { new LevelTrigger { id = "trigger_a" } };
            level.links = new[]
            {
                new LevelLink { from = "trigger_a", to = new[] { "z_does_not_exist" } },
            };

            AssertWarns(level, "targets missing id 'z_does_not_exist'");
        }

        [Test]
        public void PathWithOneWaypoint_Warns()
        {
            LevelData level = Valid();
            level.entities = new[]
            {
                new LevelEntity { id = "e01", type = "saw", pos = new[] { 5f, 5f }, path = new[] { 5f, 5f } },
            };

            AssertWarns(level, "fewer than 2 waypoints");
        }

        [Test]
        public void OddLengthPath_Warns()
        {
            LevelData level = Valid();
            level.entities = new[]
            {
                new LevelEntity { id = "e01", type = "saw", pos = new[] { 5f, 5f }, path = new[] { 5f, 5f, 9f } },
            };

            AssertWarns(level, "not a flat list of x,y pairs");
        }

        [Test]
        public void UnsetParTouches_Warns()
        {
            LevelData level = Valid();
            level.meta.par_touches = 0;
            AssertWarns(level, "par_touches unset");
        }

        [Test]
        public void TheSeededStartingRoom_IsLintClean()
        {
            // w1_01 is written from code the first time the game runs, so a mistake in it
            // ships as the starting room. Cheaper to catch here than in the console.
            List<string> warnings = LevelValidator.Validate(KeepyUppy.Runtime.SimBootstrap.SeedLevel());
            CollectionAssert.IsEmpty(warnings, string.Join(" | ", warnings));
        }

        [Test]
        public void EntityWithoutAType_Warns()
        {
            LevelData level = Valid();
            level.entities = new[] { new LevelEntity { id = "e01", type = "", pos = new[] { 5f, 5f } } };
            AssertWarns(level, "has no type");
        }

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
    }
}
