using System.IO;
using KeepyUppy.LevelEditing;
using KeepyUppy.Runtime;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The level format (level-editor-spec 3). JSON on disk is the source of truth and the
    /// shipping game and the editor read the same file, so a field that does not survive a
    /// round trip is author data the editor will silently destroy.
    /// </summary>
    public sealed class LevelFormatTests
    {
        string _directory;
        FileLevelSource _source;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "keepyuppy-levels-" + Path.GetRandomFileName());
            _source = new FileLevelSource(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        /// <summary>A level using every part of the schema, including parts nothing reads yet.</summary>
        static LevelData FullLevel()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(18, 11, 10, 1, TileKind.Solid);
            grid.Fill(13, 1, 6, 1, TileKind.SpikeUp);

            return new LevelData
            {
                id = "w1_03",
                name = "The Radiator",
                world = 1,
                index = 3,
                tiles = grid.ToRows(),
                spawn = new LevelSpawn
                {
                    player = new[] { 3f, 14f },
                    balloon = new[] { 4.5f, 6f },
                    balloon_inflation = "nominal",
                },
                exit = new LevelExit
                {
                    pos = new[] { 29f, 4f },
                    shape = "ring",
                    diameter = 1.6f,
                    required_band = "full",
                },
                entities = new[]
                {
                    new LevelEntity
                    {
                        id = "e01",
                        type = "saw",
                        pos = new[] { 12f, 9f },
                        @params = new EntityParams { radius = 0.8f, speed = 3f },
                        path = new[] { 12f, 9f, 20f, 9f },
                        path_mode = "pingpong",
                        phase = 0.25f,
                        start_state = "active",
                    },
                    new LevelEntity
                    {
                        id = "e02",
                        type = "cat",
                        pos = new[] { 24f, 15f },
                        @params = new EntityParams { aggro_radius = 4f, patience = 1.5f },
                    },
                },
                zones = new[]
                {
                    new LevelZone
                    {
                        id = "z01",
                        type = "air",
                        rect = new[] { 10f, 2f, 6f, 8f },
                        @params = new ZoneParams { vy = -6f },
                        start_state = "off",
                    },
                },
                triggers = new[]
                {
                    new LevelTrigger { id = "trigger_a", type = "player_region", rect = new[] { 14f, 10f, 4f, 8f }, once = true, visible = true, seconds = 0f },
                },
                links = new[]
                {
                    new LevelLink { from = "trigger_a", to = new[] { "z01", "e01" }, action = "toggle", delay = 0.4f },
                },
                meta = new LevelMeta { par_touches = 7, author_note = "note", tuning_profile = "default" },
            };
        }

        [Test]
        public void FullSchema_RoundTripsByteIdentical()
        {
            // The test that protects the editor: every field, including entities, triggers
            // and links that no system reads yet.
            LevelData written = FullLevel();
            _source.Save("w1_03", written);

            string first = File.ReadAllText(Path.Combine(_directory, "w1_03.json"));

            Assert.IsTrue(_source.TryLoad("w1_03", out LevelData read));
            _source.Save("w1_03", read);

            string second = File.ReadAllText(Path.Combine(_directory, "w1_03.json"));
            Assert.AreEqual(first, second, "a load/save cycle changed the file");
        }

        [Test]
        public void EntityDetail_SurvivesTheRoundTrip()
        {
            _source.Save("w1_03", FullLevel());
            Assert.IsTrue(_source.TryLoad("w1_03", out LevelData read));

            Assert.AreEqual(2, read.entities.Length);
            Assert.AreEqual("saw", read.entities[0].type);
            Assert.AreEqual(0.8f, read.entities[0].@params.radius, 1e-6f);
            Assert.AreEqual(0.25f, read.entities[0].phase, 1e-6f, "phase is easy to forget and essential");
            CollectionAssert.AreEqual(new[] { 12f, 9f, 20f, 9f }, read.entities[0].path);
            Assert.AreEqual(1.5f, read.entities[1].@params.patience, 1e-6f);

            Assert.AreEqual("trigger_a", read.links[0].from);
            CollectionAssert.AreEqual(new[] { "z01", "e01" }, read.links[0].to);
            Assert.AreEqual("off", read.zones[0].start_state);
        }

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

        [Test]
        public void Panels_RoundTrip()
        {
            LevelData level = FullLevel();
            level.panels = new[]
            {
                new LevelPanel { id = "p01", rect = new[] { 12f, 4f, 6f, 1f }, start_state = "on" },
                new LevelPanel
                {
                    id = "p02", rect = new[] { 20f, 9f, 3f, 1f }, start_state = "off", exit_dir = "up",
                },
            };

            _source.Save(level.id, level);
            Assert.IsTrue(_source.TryLoad(level.id, out LevelData loaded));

            Assert.AreEqual(2, loaded.panels.Length);
            Assert.AreEqual("p01", loaded.panels[0].id);
            Assert.AreEqual(new[] { 12f, 4f, 6f, 1f }, loaded.panels[0].rect);
            Assert.AreEqual("off", loaded.panels[1].start_state);
            Assert.AreEqual("down", loaded.panels[0].exit_dir, "the default is written, not left blank");
            Assert.AreEqual("up", loaded.panels[1].exit_dir);
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

        [Test]
        public void SavedLevel_IsReadableJson()
        {
            _source.Save("w1_01", FullLevel());
            string json = File.ReadAllText(Path.Combine(_directory, "w1_01.json"));

            StringAssert.Contains("\n", json, "levels must be pretty-printed, never minified");
            StringAssert.Contains("\"tiles\"", json);
        }

        [Test]
        public void Rows_RoundTripThroughTheGrid_WithRowZeroAsCeiling()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(8, 16, 4, 1, TileKind.SpikeDown);

            string[] rows = grid.ToRows();

            Assert.AreEqual(TileGrid.Height, rows.Length);
            Assert.AreEqual(TileGrid.Width, rows[0].Length);
            Assert.AreEqual('v', rows[1][8], "row 1 should be y=16, the ceiling spikes");

            var reloaded = new TileGrid();
            reloaded.LoadRows(rows);

            for (int y = 0; y < TileGrid.Height; y++)
            for (int x = 0; x < TileGrid.Width; x++)
                Assert.AreEqual(grid[x, y], reloaded[x, y], $"tile {x},{y}");
        }

        [Test]
        public void UnknownTileChars_BecomeEmpty_WithoutThrowing()
        {
            var grid = new TileGrid();
            grid.LoadRows(new[] { "?????" });

            Assert.AreEqual(TileKind.Empty, grid[0, TileGrid.Height - 1]);
        }

        [Test]
        public void MissingLevel_FailsCleanly()
        {
            Assert.IsFalse(_source.TryLoad("nope", out LevelData level));
            Assert.IsNull(level);
        }

        [Test]
        public void CorruptLevel_FallsBackRatherThanThrowing()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "broken.json"), "{ not json");

            LogAssert.ignoreFailingMessages = true;
            bool loaded = _source.TryLoad("broken", out LevelData level);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(loaded);
            Assert.IsNull(level);
        }

        [Test]
        public void List_IsSortedAndExtensionless()
        {
            _source.Save("w1_02", FullLevel());
            _source.Save("w1_01", FullLevel());

            CollectionAssert.AreEqual(new[] { "w1_01", "w1_02" }, _source.List());
        }

        [Test]
        public void Loader_AppliesGeometryZonesAndSpawns()
        {
            LevelData level = FullLevel();
            var world = new SimWorld();

            LevelLoader.Apply(level, world, out Vector2 balloon, out Vector2 player);

            Assert.AreEqual(new Vector2(4.5f, 6f), balloon);
            Assert.AreEqual(new Vector2(3f, 14f), player);
            Assert.AreEqual(TileKind.Solid, world.Grid[0, 0], "border did not load");
            Assert.AreEqual(TileKind.SpikeUp, world.Grid[13, 1], "hazards did not load");
            Assert.AreEqual(InflationBands.RadiusOf(InflationBand.Nominal), world.Balloon.Radius, 1e-6f);
        }

        [Test]
        public void Loader_AirZonesBehaveLikeHandBuiltOnes()
        {
            // Fans, vents and thermals are one system (GDD 6.2) whether authored in JSON or
            // in code, and a zone marked off must contribute nothing.
            LevelData level = FullLevel();
            var world = new SimWorld();
            LevelLoader.Apply(level, world, out _, out _);

            Assert.AreEqual(Vector2.zero, world.Air.Sample(new Vector2(12f, 5f)), "start_state off");

            world.Air.SetEnabled("z01", true);
            Assert.AreEqual(new Vector2(0f, -6f), world.Air.Sample(new Vector2(12f, 5f)));
            Assert.AreEqual(Vector2.zero, world.Air.Sample(new Vector2(2f, 2f)), "outside the rect");
        }

        [Test]
        public void LoadedLevel_StillSimulatesDeterministically()
        {
            // The loading path must not become a source of divergence: same file, same seed,
            // bit-identical run (GDD 6.1).
            LevelData level = FullLevel();

            SimWorld Build()
            {
                var world = new SimWorld { EnableFlutter = true };
                LevelLoader.Apply(level, world, out Vector2 balloon, out Vector2 player);
                world.Configure(balloon, Vector2.zero, player, SimTestHelpers.BusyScript(), 99);
                return world;
            }

            SimWorld a = Build();
            SimWorld b = Build();

            for (int i = 0; i < 600; i++)
            {
                a.Tick();
                b.Tick();
                SimSnapshot.Capture(a).AssertMatches(SimSnapshot.Capture(b), $"step {i}");
            }
        }

        [Test]
        public void AZoneAuthoredInTheEditorSurvivesASaveAndReload()
        {
            var session = new EditorSession();
            LevelZone authored = session.AddZone(new Rect(20f, 1f, 6f, 10f));
            authored.@params.vx = 1.5f;
            authored.@params.vy = 4.5f;
            authored.start_state = "off";

            string json = JsonUtility.ToJson(session.Flush(), true);
            LevelData reloaded = JsonUtility.FromJson<LevelData>(json);

            Assert.AreEqual(1, reloaded.zones.Length);

            LevelZone zone = reloaded.zones[0];
            Assert.AreEqual("z01", zone.id);
            Assert.AreEqual("air", zone.type);
            Assert.AreEqual(new[] { 20f, 1f, 6f, 10f }, zone.rect);
            Assert.AreEqual(1.5f, zone.@params.vx, 1e-5f);
            Assert.AreEqual(4.5f, zone.@params.vy, 1e-5f);
            Assert.AreEqual("off", zone.start_state);
        }

        [Test]
        public void FlushDoesNotDropZonesWhileWritingTiles()
        {
            // Flush exists to write the working grid back into the level. Nothing else it
            // touches may be lost on the way through.
            var session = new EditorSession();
            session.AddZone(new Rect(4f, 4f, 2f, 2f));

            session.Grid[3, 3] = TileKind.Solid;
            LevelData flushed = session.Flush();

            Assert.AreEqual(1, flushed.zones.Length, "flushing tiles must not disturb zones");
        }
    }
}
