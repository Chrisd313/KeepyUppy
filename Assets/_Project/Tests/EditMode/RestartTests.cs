using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// GDD 8: restart under 200 ms, with no transition and no delay — and, more importantly,
    /// a restart that is genuinely a reset. A counter that survives one makes attempt 2
    /// differ from attempt 1, which is the determinism contract broken by the back door.
    /// </summary>
    public sealed class RestartTests
    {
        const int Steps = 300;
        const int Seed = 4242;

        [Test]
        public void RestartIsPure_MatchesAFreshRun()
        {
            SimWorld reused = SimTestHelpers.BuildBusyRoom(Seed);
            reused.Tick(Steps);
            reused.Restart();
            reused.Tick(Steps);

            SimWorld fresh = SimTestHelpers.BuildBusyRoom(Seed);
            fresh.Tick(Steps);

            SimSnapshot.Capture(fresh)
                       .AssertMatches(SimSnapshot.Capture(reused), "restarted vs fresh");
        }

        [Test]
        public void Restart_ClearsEveryCounter()
        {
            SimWorld world = SimTestHelpers.BuildBusyRoom(Seed);
            world.Tick(Steps);

            world.Restart();

            Assert.AreEqual(0, world.Clock.Step, "step counter");
            Assert.AreEqual(0, world.TouchCount, "touch count");
            Assert.AreEqual(0, world.Player.JumpBufferFrames, "jump buffer");
            Assert.AreEqual(0, world.Player.CoyoteFrames, "coyote frames");
            Assert.IsFalse(world.IsDead, "death flag");
            Assert.IsFalse(world.Balloon.Popped, "balloon popped");
            Assert.IsFalse(world.Player.Popped, "player popped");
            Assert.AreEqual(DeathCause.None, world.LastDeath, "death cause");
        }

        [Test]
        public void Restart_PreservesDeathCount()
        {
            // GDD 15: the per-level death counter is displayed and never hidden, so it is the
            // one piece of state a restart must not clear.
            SimWorld world = SimTestHelpers.BuildContactRoom();
            world.FloorKillsBalloon = true;

            // Balloon well clear of the player, so it reaches the floor rather than resting
            // on a head.
            world.Configure(new Vector2(6f, 8f), Vector2.zero, new Vector2(24f, 2f), null, 1);

            world.Tick(600);
            int deaths = world.DeathCount;

            Assert.Greater(deaths, 0, "the balloon should have hit the floor by now");

            world.Restart();
            Assert.AreEqual(deaths, world.DeathCount);
        }

        [Test]
        public void BalloonHittingFloor_KillsBoth_InTheSameStep()
        {
            // LOCKED GDD 4.1: one explosion, one restart. The player is never left watching
            // a doomed balloon.
            SimWorld world = SimTestHelpers.BuildContactRoom();
            world.FloorKillsBalloon = true;
            world.PlayerTuning = WithoutAutoRestart(world.PlayerTuning);

            // Well away from the player, so this is the floor and nothing else.
            world.Configure(new Vector2(6f, 4f), Vector2.zero, new Vector2(24f, 2f), null, 1);

            for (int i = 0; i < 600 && !world.IsDead; i++) world.Tick();

            Assert.IsTrue(world.IsDead, "the balloon never reached the floor");
            Assert.AreEqual(DeathCause.BalloonHitFloor, world.LastDeath);
            Assert.IsTrue(world.Balloon.Popped, "balloon");
            Assert.IsTrue(world.Player.Popped, "player — linked fate is simultaneous");
        }

        [Test]
        public void SpikeKillsBalloon_OnItsPointedFaceOnly()
        {
            SimWorld world = SimTestHelpers.BuildContactRoom();
            world.PlayerTuning = WithoutAutoRestart(world.PlayerTuning);
            world.Grid.Fill(16, 5, 1, 1, TileKind.SpikeUp);

            // Directly above the point.
            world.Configure(new Vector2(16.5f, 8f), Vector2.zero, new Vector2(24f, 2f), null, 1);
            for (int i = 0; i < 300 && !world.IsDead; i++) world.Tick();

            Assert.IsTrue(world.IsDead, "a balloon landing on a spike should pop");
            Assert.AreEqual(DeathCause.BalloonHitHazard, world.LastDeath);
        }

        [Test]
        public void PlayerDiesToSpikes_AndTakesTheBalloonWithThem()
        {
            SimWorld world = SimTestHelpers.BuildContactRoom();
            world.PlayerTuning = WithoutAutoRestart(world.PlayerTuning);
            world.Grid.Fill(10, 1, 4, 1, TileKind.SpikeUp);

            // Player spawned above the spikes; the balloon is parked far away and healthy.
            world.Configure(new Vector2(28f, 8f), Vector2.zero, new Vector2(12f, 4f), null, 1);
            for (int i = 0; i < 300 && !world.IsDead; i++) world.Tick();

            Assert.IsTrue(world.IsDead, "the player never landed on the spikes");
            Assert.AreEqual(DeathCause.PlayerHitHazard, world.LastDeath);
            Assert.IsTrue(world.Balloon.Popped, "the balloon pops with the player");
        }

        /// <summary>Holds the death so a test can inspect it rather than racing the auto-restart.</summary>
        static PlayerTuning WithoutAutoRestart(PlayerTuning t)
        {
            t.deathRestartFrames = int.MaxValue;
            return t;
        }
    }
}
