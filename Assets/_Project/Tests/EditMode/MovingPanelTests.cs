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
            world.Panels.Advance(15);
            world.Solids.Rebuild(world.Panels);

            Assert.AreEqual(2, world.Solids.Count);
            Assert.AreEqual(0, world.Solids[0].panel, "list order, never a dictionary");
            Assert.AreEqual(2.5f, world.Solids[0].rect.yMin, 1e-3f, "the one that ran has moved");
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
        [Test]
        public void ARoomWithAMovingPanel_ReplaysIdentically()
        {
            // GDD 6.1. Panels only began advancing inside Tick with the player collision, so
            // the replay guarantee has to cover a ridden panel or it covers nothing new.
            SimWorld a = PlayableRoom(Riser(), new Vector2(12f, 6f));
            SimWorld b = PlayableRoom(Riser(), new Vector2(12f, 6f));

            a.Panels[0].Run(0);
            b.Panels[0].Run(0);

            for (int i = 0; i < 300; i++)
            {
                a.Tick();
                b.Tick();

                Assert.AreEqual(a.Player.Position, b.Player.Position, $"player at step {i}");
                Assert.AreEqual(a.Player.Velocity, b.Player.Velocity, $"velocity at step {i}");
                Assert.AreEqual(a.Panels[0].WorldRect, b.Panels[0].WorldRect, $"panel at step {i}");
                Assert.AreEqual(a.Balloon.Position, b.Balloon.Position, $"balloon at step {i}");
            }

            Assert.AreEqual(4f, a.Panels[0].WorldRect.yMin, 1e-3f,
                            "and it passed for the right reason: the panel really travelled");
        }
        /// <summary>
        /// A body riding a panel that also brushes a wall in the same step is still riding
        /// the panel. The wall contact resolves second, and must not erase the carrier the
        /// panel contact recorded — the shove and crush passes read that field to decide
        /// which panel owns the body.
        /// </summary>
        [Test]
        public void ATileContactDoesNotErase_ThePanelTheBodyIsRiding()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(14, 2, 1, 5, TileKind.Solid);   // a wall along the panel's right edge

            var panels = new PanelField();
            panels.Add(new SimPanel
            {
                Id = "p01",
                Rect = new RectInt(10, 1, 4, 1),
                Points = new[] { Vector2.zero, new Vector2(0f, 3f) },
                Mode = PathMode.Once,
                Speed = 6f,
            });
            panels[0].Run(0);
            panels.Advance(0);

            var solids = new SolidField();
            solids.Rebuild(panels);
            Assert.AreEqual(6f, solids[0].velocity.y, 1e-3f, "the panel is rising this step");

            const float hw = 0.40f;
            const float hh = 0.80f;

            // Standing on the panel's top (y = 2) and running right, close enough that the
            // wall is reached part way through the step rather than at its very start.
            var pos = new Vector2(13.55f, 2f + hh + 1e-4f);
            var vel = new Vector2(6f, -40f * SimClock.Dt);

            SweepResult result = SweptAabb.Move(grid, solids, hw, hh, ref pos, ref vel, SimClock.Dt);

            Assert.IsTrue(result.hitFloor, "it landed on the panel");
            Assert.AreEqual(-1f, result.lastNormal.x, 1e-3f,
                            "and the LAST contact of the step was the wall, not the panel");
            Assert.AreEqual(0, result.carrier,
                            "yet the carrier is still the panel: a tile contact must not erase it");
            Assert.AreEqual(14f - hw, pos.x, 0.01f, "stopped against the wall");
        }

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

        /// <summary>
        /// Ticks until the room kills something, so a crush test asserts on the step the
        /// crush happened rather than on a hand-counted tick total. Six frames after a death
        /// the room restarts itself (PlayerTuning.deathRestartFrames), which would clear
        /// every flag these tests are about.
        /// </summary>
        static void TickUntilDead(SimWorld world, int cap = 300)
        {
            for (int i = 0; i < cap && !world.IsDead; i++) world.Tick();
        }

        /// <summary>A panel that climbs to the right, so neither velocity axis is zero.</summary>
        static LevelPanel Diagonal() => new LevelPanel
        {
            id = "p01",
            rect = new[] { 10f, 1f, 4f, 1f },
            path = new[] { 0f, 0f, 6f, 6f },
            path_mode = "once",
            speed = 6f,
            hold = 0f,
        };

        /// <summary>A room with one moving panel, mid-travel, with the bodies parked.</summary>
        static SimWorld RunningRoom(LevelPanel panel, int steps)
        {
            var world = new SimWorld();
            LevelLoader.Apply(Room(panel), world, out _, out _);
            world.FloorKillsBalloon = false;
            world.Configure(new Vector2(2f, 14f), Vector2.zero, new Vector2(12.5f, 2.8f), null, 1);

            world.Panels[0].Run(0);
            world.Panels.Advance(steps);
            world.Solids.Rebuild(world.Panels);
            return world;
        }

        [Test]
        public void ABalloonWhoseSquareClipsAPanelCornerButWhoseCircleDoesNot_IsLeftAlone()
        {
            SimWorld world = RunningRoom(Pusher(), 15);

            Rect rect = world.Solids[0].rect;
            float r = world.Balloon.Radius;

            // Diagonally off the panel's top-right corner by 0.875 r on each axis: the
            // bounding SQUARE overlaps (0.875 r < r) while the circle is clear
            // (dist = 1.237 r > r). GDD 8 forbids a pop hitbox larger than the visual.
            var parked = new Vector2(rect.xMax + r * 0.875f, rect.yMax + r * 0.875f);
            world.Balloon.Position = parked;

            world.ResolveMovingPanels();

            Assert.IsFalse(world.IsDead, "the circle never touched the panel");
            Assert.AreEqual(parked, world.Balloon.Position, "and so it is not shoved either");
        }

        [Test]
        public void ABodyOnADiagonallyRisingPanel_IsLiftedNotEjectedSideways()
        {
            SimWorld world = RunningRoom(Diagonal(), 15);

            MovingSolid solid = world.Solids[0];
            Assert.Greater(solid.velocity.x, 0f);
            Assert.Greater(solid.velocity.y, 0f, "travelling up AND right, so neither axis wins");

            // Standing on top, overlapping only by the sliver the panel rose into this step.
            float hh = world.Player.HalfHeight;
            world.Player.Position = new Vector2(solid.rect.center.x, solid.rect.yMax + hh - 0.05f);
            float x = world.Player.Position.x;

            world.ResolveMovingPanels();

            Assert.IsFalse(world.IsDead);
            Assert.AreEqual(x, world.Player.Position.x, 1e-4f,
                            "not ejected off the front: the axis is chosen by penetration "
                            + "depth, not by which velocity component is larger");
            Assert.AreEqual(solid.rect.yMax + hh, world.Player.Position.y, 1e-4f,
                            "lifted clear of the surface it was standing on");
        }

        [Test]
        public void APanelMovingIntoThePlayer_ShovesThem()
        {
            // Standing on the ledge at x 10..14, clear of the panel's authored rect (x 10..11).
            SimWorld world = PlayableRoom(Pusher(), new Vector2(12.5f, 2.8f));
            world.Tick(30);

            float before = world.Player.Position.x;
            world.Panels[0].Run(world.Clock.Step);
            world.Tick(60);

            Assert.Greater(world.Player.Position.x, before + 1f, "pushed along, not passed through");
        }

        [Test]
        public void BeingShoved_DoesNotChangeThePlayersVelocity()
        {
            SimWorld world = PlayableRoom(Pusher(), new Vector2(12.5f, 2.8f));
            world.Tick(30);
            world.Panels[0].Run(world.Clock.Step);
            world.Tick(30);

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
            TickUntilDead(world);

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
            TickUntilDead(world);

            Assert.IsTrue(world.Player.Popped);
            Assert.IsTrue(world.Balloon.Popped, "GDD 4.1: linked fate, same step, no exceptions");
        }
    }
}
