using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// GDD 6.1: a small collider at 16 m/s tunnels through a thin obstacle at 60 Hz unless
    /// the movement is swept. The wall here is one tile thick and the balloon travels at
    /// max_speed from every approach angle.
    /// </summary>
    public sealed class TunnellingTests
    {
        const int WallX = 16;
        const int Steps = 900;   // 15 seconds of ricochet

        /// <summary>No gravity and no drag, so speed stays pinned at max_speed all run.</summary>
        static BalloonTuning BallisticTuning()
        {
            BalloonTuning t = BalloonTuning.Default;
            t.gravity = 0f;
            t.dragKx = 0f;
            t.dragKy = 0f;
            return t;
        }

        static SimWorld BuildWallRoom()
        {
            // Floor death off: these are physics tests and want to watch the balloon bounce.
            var world = new SimWorld
            {
                Tuning = BallisticTuning(),
                EnableFlutter = false,
                FloorKillsBalloon = false,
            };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Grid.Fill(WallX, 1, 1, TileGrid.Height - 2, TileKind.Solid);
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            return world;
        }

        [Test]
        public void OneTileWall_AtMaxSpeed_NeverPassed([Values(2.0f, 4.5f, 8.0f, 9.5f, 13.25f, 16.0f)] float startY)
        {
            SimWorld world = BuildWallRoom();
            float max = world.Tuning.maxSpeed;

            // Straight on, and shallow and steep diagonals, all at exactly max_speed.
            Vector2[] directions =
            {
                new Vector2(1f, 0f),
                new Vector2(0.9806f, 0.1961f),
                new Vector2(0.9806f, -0.1961f),
                new Vector2(0.7071f, 0.7071f),
                new Vector2(0.7071f, -0.7071f),
            };

            foreach (Vector2 dir in directions)
            {
                world.Reset(new Vector2(4.5f, startY), new Vector2(dir.x * max, dir.y * max), 1);

                for (int i = 0; i < Steps; i++)
                {
                    world.Tick();

                    Assert.LessOrEqual(world.Balloon.Position.x, WallX - world.Balloon.Radius + 1e-3f,
                        $"passed the wall at step {i}, startY {startY}, dir {dir}");
                    Assert.IsFalse(SimTestHelpers.CentreInsideSolid(world),
                        $"ended inside geometry at step {i}, startY {startY}, dir {dir}");
                }
            }
        }

        [Test]
        public void OneTileFloor_AtMaxSpeed_NeverPassed()
        {
            // Floor death off: these are physics tests and want to watch the balloon bounce.
            var world = new SimWorld
            {
                Tuning = BallisticTuning(),
                EnableFlutter = false,
                FloorKillsBalloon = false,
            };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Grid.Fill(1, 4, TileGrid.Width - 2, 1, TileKind.Solid);
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Shrivelled);

            world.Reset(new Vector2(12.5f, 15f), new Vector2(0f, -world.Tuning.maxSpeed), 1);

            for (int i = 0; i < Steps; i++)
            {
                world.Tick();
                Assert.GreaterOrEqual(world.Balloon.Position.y, 5f + world.Balloon.Radius - 1e-3f,
                    $"passed the floor at step {i}");
            }
        }

        [Test]
        public void SpeedIsClampedToMaxSpeed()
        {
            var world = new SimWorld { EnableFlutter = false, FloorKillsBalloon = false };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Reset(new Vector2(16f, 9f), new Vector2(400f, -400f), 1);

            world.Tick();

            Assert.LessOrEqual(world.Balloon.Velocity.magnitude, world.Tuning.maxSpeed + 1e-3f);
        }

        [Test]
        public void BelowMinBounceSpeed_DoesNotBounce()
        {
            // GDD 6.4: below min_bounce_speed there is no bounce, or a settling balloon
            // jitters against the floor forever.
            // Floor death off: these are physics tests and want to watch the balloon bounce.
            var world = new SimWorld
            {
                Tuning = BallisticTuning(),
                EnableFlutter = false,
                FloorKillsBalloon = false,
            };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);

            float creep = world.Tuning.minBounceSpeed * 0.5f;
            world.Reset(new Vector2(16f, 1.8f), new Vector2(0f, -creep), 1);

            // Long enough to close the 0.3 m gap at half the bounce threshold.
            world.Tick(240);

            Assert.AreEqual(0f, world.Balloon.Velocity.y, 1e-4f, "a sub-threshold contact bounced");
        }
    }
}
