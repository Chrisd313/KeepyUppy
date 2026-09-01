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

            // Settled by the sim rather than hand-placed: the spawn y puts the player's feet
            // exactly on the tile boundary, where float error alone decides the overlap. A few
            // steps land it on the surface the way SweptAabb actually does, a skin above.
            world.Tick(5);

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
