using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using KeepyUppy.Sim.Panels;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Which way a panel's tiles leave (panel exit-direction design).
    ///
    /// Presentation only — the mask still switches on one step — so the value has to survive
    /// the load path without ever reaching the tick. These tests cover the parse and the
    /// load; that the debris actually flies that way is judged by a human.
    /// </summary>
    public sealed class PanelDirectionTests
    {
        static LevelData LevelWithPanel(LevelPanel panel)
        {
            var grid = new TileGrid();
            grid.FillBorder();

            return new LevelData { tiles = grid.ToRows(), panels = new[] { panel } };
        }

        [Test]
        public void Directions_ParseToTheirAxis_AndAnythingElseFallsBackToDown()
        {
            Assert.AreEqual(PanelDirection.Down, PanelDirections.Parse("down"));
            Assert.AreEqual(PanelDirection.Up, PanelDirections.Parse("up"));
            Assert.AreEqual(PanelDirection.Left, PanelDirections.Parse("left"));
            Assert.AreEqual(PanelDirection.Right, PanelDirections.Parse("right"));

            Assert.AreEqual(PanelDirection.Down, PanelDirections.Parse("sideways"));
            Assert.AreEqual(PanelDirection.Down, PanelDirections.Parse(""));
            Assert.AreEqual(PanelDirection.Down, PanelDirections.Parse(null),
                            "a level written before the field existed reads as the old behaviour");
        }

        [Test]
        public void ExitDir_ReachesTheSimPanel()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithPanel(new LevelPanel
            {
                id = "p01", rect = new[] { 4f, 1f, 3f, 1f }, exit_dir = "up",
            }), world, out _, out _);

            Assert.AreEqual(PanelDirection.Up, world.Panels[0].ExitDir);
        }

        [Test]
        public void APanelWithNoExitDir_Falls()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithPanel(new LevelPanel
            {
                id = "p01", rect = new[] { 4f, 1f, 3f, 1f },
            }), world, out _, out _);

            Assert.AreEqual(PanelDirection.Down, world.Panels[0].ExitDir);
        }

        [Test]
        public void ExitDir_IsConfig_AndSurvivesAReset()
        {
            var panel = new SimPanel { Id = "p01", ExitDir = PanelDirection.Left, StartEnabled = true };
            var field = new PanelField();
            field.Add(panel);

            field.SetEnabled("p01", false);
            field.Reset();

            Assert.AreEqual(PanelDirection.Left, panel.ExitDir);
            Assert.IsTrue(panel.Enabled, "run state resets, config does not");
        }

        [Test]
        public void UnitVector_PointsTheWayTheTilesLeave()
        {
            Assert.AreEqual(new Vector2Int(0, -1), PanelDirections.Step(PanelDirection.Down));
            Assert.AreEqual(new Vector2Int(0, 1), PanelDirections.Step(PanelDirection.Up));
            Assert.AreEqual(new Vector2Int(-1, 0), PanelDirections.Step(PanelDirection.Left));
            Assert.AreEqual(new Vector2Int(1, 0), PanelDirections.Step(PanelDirection.Right));
        }
    }
}
