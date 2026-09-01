using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The balloon's (and player's) out-of-room death.
    ///
    /// TileGrid's out-of-bounds indexer used to read Solid on every side, so the room was
    /// already a sealed box: a balloon with no floor tile of its own just rested on that
    /// implicit floor and died to BalloonHitFloor, never actually leaving. That made a
    /// Mario-style fall off the bottom of an open room impossible. The indexer now opens
    /// only below y = 0 (Empty there); the sides and ceiling stay Solid, preserving the
    /// safety net every other direction still relies on. See DeathCause.BalloonLeftRoom
    /// and SimWorld.StepBalloon/StepPlayer.
    /// </summary>
    public sealed class BalloonBoundsTests
    {
        /// <summary>
        /// Ticks until IsDead flips true, up to a generous bound. GDD 8's death hold and
        /// restart mean a fixed tick count is fragile here — tick a little too far past the
        /// first death and Restart() has already put the world back in a live, falling state,
        /// masking the very moment under test. Stopping at the first death sidesteps that.
        /// </summary>
        static void TickUntilDead(SimWorld world, int maxSteps = 600)
        {
            for (int i = 0; i < maxSteps && !world.IsDead; i++) world.Tick();
        }

        /// <summary>
        /// A room with no geometry at all and no player, so nothing but the open floor and
        /// the bounds check can end it. No player: Configure would spawn one at the origin,
        /// which falls through the same open floor and reaches the margin on its own —
        /// masking the balloon's death under PlayerFellOut instead. Reset leaves HasPlayer
        /// false.
        /// </summary>
        static SimWorld OpenRoom(Vector2 balloonPos, Vector2 velocity)
        {
            var world = new SimWorld { EnableFlutter = false, FloorKillsBalloon = true };
            world.Grid.Clear();
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Reset(balloonPos, velocity, 1);
            return world;
        }

        [Test]
        public void ABalloonFallingOutTheBottom_Dies()
        {
            SimWorld world = OpenRoom(new Vector2(16f, 1f), new Vector2(0f, -6f));

            TickUntilDead(world);

            Assert.IsTrue(world.IsDead, "a level with no floor of its own should end, not stall");
            Assert.AreEqual(DeathCause.BalloonLeftRoom, world.LastDeath);
        }

        [Test]
        public void ABalloonLandingOnTheLevelsOwnFloor_DiesToTheFloorNotTheBounds()
        {
            // A properly walled room: the level's own floor tile is the specific cause, and
            // it should win over the open-bottom safety net that only applies when there is
            // no floor tile at all.
            var world = new SimWorld { EnableFlutter = false, FloorKillsBalloon = true };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Reset(new Vector2(16f, 3f), new Vector2(0f, -6f), 1);

            TickUntilDead(world);

            Assert.IsTrue(world.IsDead);
            Assert.AreEqual(DeathCause.BalloonHitFloor, world.LastDeath);
        }

        [Test]
        public void ABalloonJustBelowTheRoom_Survives()
        {
            // One metre below the open floor, inside the 2 m margin: the boundary itself is
            // not lethal, only 2 m past it.
            SimWorld world = OpenRoom(new Vector2(16f, -1f), Vector2.zero);

            world.Tick(1);

            Assert.IsFalse(world.IsDead);
        }

        [Test]
        public void ThePlayerFallingOutTheBottom_DiesToPlayerFellOut()
        {
            // Mirrors the balloon case for the player: an open-bottomed room with no floor
            // tile should end the level for the player too, and with the player's own cause.
            // The balloon needs somewhere plausible to sit too: Configure always spawns one.
            // Its gravity (1.0) is far weaker than the player's (40, GDD 5.7's platformer
            // fall) and it starts at rest well clear of either bound, so it never reaches its
            // own bounds check first.
            var world = new SimWorld { EnableFlutter = false };
            world.Grid.Clear();
            world.Configure(new Vector2(4f, 12f), Vector2.zero, new Vector2(16f, 9f), null, 1);

            TickUntilDead(world);

            Assert.IsTrue(world.IsDead, "a floorless room should drop the player out, not hold them up");
            Assert.AreEqual(DeathCause.PlayerFellOut, world.LastDeath);
        }
    }
}
