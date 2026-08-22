using System;
using KeepyUppy.InputLayer;
using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Everything the sim carries from one step to the next that a test can observe.
    ///
    /// One shared field set, so determinism and restart purity are checked against exactly
    /// the same definition of "the state". A new counter added to the sim gets covered by
    /// both the moment it is added here.
    ///
    /// DeathCount is deliberately absent: GDD 15 wants it to survive a restart, so it is not
    /// part of the state a restart is expected to reproduce.
    /// </summary>
    internal struct SimSnapshot
    {
        public int step;
        public float balloonX, balloonY, balloonVx, balloonVy;
        public float playerX, playerY, playerVx, playerVy;
        public int touchCount;
        public int coyoteFrames;
        public int jumpBufferFrames;
        public bool grounded;
        public bool isDead;

        public static SimSnapshot Capture(SimWorld w) => new SimSnapshot
        {
            step = w.Clock.Step,
            balloonX = w.Balloon.Position.x,
            balloonY = w.Balloon.Position.y,
            balloonVx = w.Balloon.Velocity.x,
            balloonVy = w.Balloon.Velocity.y,
            playerX = w.Player.Position.x,
            playerY = w.Player.Position.y,
            playerVx = w.Player.Velocity.x,
            playerVy = w.Player.Velocity.y,
            touchCount = w.TouchCount,
            coyoteFrames = w.Player.CoyoteFrames,
            jumpBufferFrames = w.Player.JumpBufferFrames,
            grounded = w.Player.Grounded,
            isDead = w.IsDead,
        };

        public void AssertMatches(in SimSnapshot other, string context)
        {
            Assert.AreEqual(step, other.step, $"{context}: step");

            AssertBits(balloonX, other.balloonX, $"{context}: balloon.x");
            AssertBits(balloonY, other.balloonY, $"{context}: balloon.y");
            AssertBits(balloonVx, other.balloonVx, $"{context}: balloon.vx");
            AssertBits(balloonVy, other.balloonVy, $"{context}: balloon.vy");

            AssertBits(playerX, other.playerX, $"{context}: player.x");
            AssertBits(playerY, other.playerY, $"{context}: player.y");
            AssertBits(playerVx, other.playerVx, $"{context}: player.vx");
            AssertBits(playerVy, other.playerVy, $"{context}: player.vy");

            Assert.AreEqual(touchCount, other.touchCount, $"{context}: touchCount");
            Assert.AreEqual(coyoteFrames, other.coyoteFrames, $"{context}: coyoteFrames");
            Assert.AreEqual(jumpBufferFrames, other.jumpBufferFrames, $"{context}: jumpBufferFrames");
            Assert.AreEqual(grounded, other.grounded, $"{context}: grounded");
            Assert.AreEqual(isDead, other.isDead, $"{context}: isDead");
        }

        static void AssertBits(float a, float b, string message)
            => Assert.AreEqual(SimTestHelpers.Bits(a), SimTestHelpers.Bits(b), message);
    }

    internal static class SimTestHelpers
    {
        /// <summary>Exact bit pattern of a float. Equality here means equality, not "close".</summary>
        public static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        /// <summary>
        /// A room with enough going on that a determinism bug has somewhere to show up:
        /// walls, two ledges, hazards, two overlapping air zones, idle flutter, a player and
        /// a scripted run that bumps the balloon, dies and restarts.
        /// </summary>
        public static SimWorld BuildBusyRoom(int seed)
        {
            var world = new SimWorld { EnableFlutter = true };

            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Grid.Fill(4, 6, 10, 1, TileKind.Solid);
            world.Grid.Fill(18, 11, 10, 1, TileKind.Solid);
            world.Grid.Fill(24, 1, 3, 1, TileKind.SpikeUp);

            world.Air.Clear();
            world.Air.Add(new AirZone("z_up", new Rect(6f, 1f, 8f, 12f), new Vector2(1.5f, 5f)));
            world.Air.Add(new AirZone("z_cross", new Rect(10f, 4f, 12f, 6f), new Vector2(-3f, 1f)));
            world.Air.Add(new AirZone("z_off", new Rect(0f, 0f, 32f, 18f), new Vector2(99f, 99f), false));

            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Configure(new Vector2(8.3f, 12.1f), new Vector2(2.7f, -1.3f),
                            new Vector2(8.0f, 2.0f), BusyScript(), seed);

            return world;
        }

        /// <summary>
        /// A run that exercises the whole surface: movement both ways, jumps into the
        /// balloon, palms and spikes, and the idle frames in between.
        /// </summary>
        public static ScriptedInputSource BusyScript()
        {
            return new ScriptedInputSource.Builder()
                .Wait(20)
                .Move(1f).Wait(30)
                .Jump(10)
                .Wait(15)
                .Move(-1f).Wait(25)
                .Jump(4)
                .Aim(new Vector2(1f, 0f)).Context(3)
                .Wait(20)
                .Move(0f).Aim(new Vector2(0f, 1f)).Context(20)
                .Wait(30)
                .Aim(Vector2.zero).Move(1f).Jump(12)
                .Wait(40)
                .LoopTo(1200)
                .Build();
        }

        /// <summary>
        /// A bare room for contact tests: floor, walls, no hazards, no wind, no flutter, and
        /// the floor made non-lethal so a test can watch a balloon bounce.
        /// </summary>
        public static SimWorld BuildContactRoom(IInputSource input = null)
        {
            var world = new SimWorld { EnableFlutter = false, FloorKillsBalloon = false };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Configure(new Vector2(16f, 8f), Vector2.zero, new Vector2(16f, 1.8f), input, 1);
            return world;
        }

        /// <summary>True when the balloon's centre is inside a solid tile.</summary>
        public static bool CentreInsideSolid(SimWorld world)
        {
            int tx = Mathf.FloorToInt(world.Balloon.Position.x);
            int ty = Mathf.FloorToInt(world.Balloon.Position.y);
            return world.Grid.IsSolid(tx, ty);
        }

        /// <summary>
        /// Runs the world and folds every step's state into one hash. Catches divergence
        /// anywhere in the run, including divergence that later converges.
        /// </summary>
        public static int RunHash(SimWorld world, int steps)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < steps; i++)
                {
                    world.Tick();
                    SimSnapshot s = SimSnapshot.Capture(world);
                    hash = Fold(hash, Bits(s.balloonX));
                    hash = Fold(hash, Bits(s.balloonY));
                    hash = Fold(hash, Bits(s.balloonVx));
                    hash = Fold(hash, Bits(s.balloonVy));
                    hash = Fold(hash, Bits(s.playerX));
                    hash = Fold(hash, Bits(s.playerY));
                    hash = Fold(hash, s.touchCount);
                }
                return hash;
            }
        }

        static int Fold(int hash, int value) => unchecked((hash ^ value) * 16777619);
    }
}
