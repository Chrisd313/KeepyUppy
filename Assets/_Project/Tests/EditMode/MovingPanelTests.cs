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
