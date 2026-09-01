using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The rubber tile. Solid geometry already bounces the balloon without popping it, so
    /// what makes rubber a distinct kind is the pair of things it does that Solid cannot:
    /// an upward-facing rubber surface does not end the run, and it launches at a fixed
    /// speed rather than at restitution_wall times whatever came in.
    ///
    /// Fixed launch is not a flourish. At restitution_wall the bounces shrink, min_bounce_speed
    /// eventually zeroes one, and the balloon comes to rest on a tile that is by design not
    /// allowed to kill it - a level that is neither lost nor winnable. Setting the outgoing
    /// speed removes that state instead of tuning around it.
    /// </summary>
    public sealed class RubberTileTests
    {
        const float Radius = 0.4f;
        const float Dt = 1f / 60f;
        const float Launch = 12f;

        /// <summary>Slab of the given kind across y = 4, its top face at y = 5.</summary>
        static TileGrid SlabRoom(TileKind kind)
        {
            var grid = new TileGrid();
            grid.Clear();
            grid.FillBorder();
            grid.Fill(4, 4, 10, 1, kind);
            return grid;
        }

        static BalloonTuning Tuning()
        {
            BalloonTuning t = BalloonTuning.Default;
            t.rubberLaunchSpeed = Launch;
            return t;
        }

        /// <summary>Drops a circle onto the slab from just above it and returns the sweep.</summary>
        static SweepResult DropOnto(TileKind kind, Vector2 velocity, out Vector2 outVel)
        {
            TileGrid grid = SlabRoom(kind);
            BalloonTuning tuning = Tuning();
            var pos = new Vector2(8f, 5f + Radius + 0.001f);
            outVel = velocity;
            return SweptCircle.Move(grid, tuning, Radius, ref pos, ref outVel, Dt);
        }

        // --- the safe floor ---------------------------------------------------------

        [Test]
        public void RubberFloor_IsNotReportedAsFloor()
        {
            SweepResult result = DropOnto(TileKind.Rubber, new Vector2(0f, -8f), out _);

            Assert.IsTrue(result.Any, "the drop should have contacted the slab");
            Assert.IsFalse(result.hitFloor, "rubber must not end the balloon's run");
        }

        [Test]
        public void SolidFloor_IsStillReportedAsFloor()
        {
            SweepResult result = DropOnto(TileKind.Solid, new Vector2(0f, -8f), out _);

            Assert.IsTrue(result.hitFloor, "ordinary geometry must still end the run");
        }

        [Test]
        public void BalloonLandingOnRubber_SurvivesAndIsSentBackUp()
        {
            var world = new SimWorld { EnableFlutter = false };
            world.Tuning = Tuning();
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Grid.Fill(4, 1, 10, 1, TileKind.Rubber);
            world.Balloon.Radius = Radius;
            world.Configure(new Vector2(8f, 12f), Vector2.zero, new Vector2(20f, 2f), null, 1);

            // Gravity is 1 m/s^2, so the six-metre fall takes several seconds. Ten is long
            // enough to land, launch, and fall back for a second contact.
            bool launched = false;
            for (int i = 0; i < 600 && !world.IsDead; i++)
            {
                world.Tick();
                if (world.Balloon.Velocity.y > 0f) launched = true;
            }

            Assert.IsFalse(world.IsDead, $"killed by {world.LastDeath}");
            Assert.IsTrue(launched, "the rubber floor should have sent the balloon back up");
        }

        // --- fixed launch -----------------------------------------------------------

        [Test]
        public void RubberLaunch_IgnoresIncomingSpeed([Values(2f, 8f, 18f)] float speed)
        {
            DropOnto(TileKind.Rubber, new Vector2(0f, -speed), out Vector2 vel);

            Assert.AreEqual(Launch, vel.y, 1e-4f);
        }

        [Test]
        public void SolidBounce_StillScalesWithIncomingSpeed()
        {
            BalloonTuning tuning = Tuning();
            DropOnto(TileKind.Solid, new Vector2(0f, -8f), out Vector2 vel);

            Assert.AreEqual(8f * tuning.restitutionWall, vel.y, 1e-4f);
        }

        [Test]
        public void RubberLaunch_IgnoresMinBounceSpeed()
        {
            // Below min_bounce_speed a solid surface absorbs the bounce entirely, which is
            // exactly the case that would otherwise leave a balloon parked on rubber.
            DropOnto(TileKind.Solid, new Vector2(0f, -0.1f), out Vector2 onSolid);
            Assert.AreEqual(0f, onSolid.y, 1e-4f);

            DropOnto(TileKind.Rubber, new Vector2(0f, -0.1f), out Vector2 onRubber);
            Assert.AreEqual(Launch, onRubber.y, 1e-4f);
        }

        [Test]
        public void RubberLaunch_KeepsTheTangentialComponent()
        {
            DropOnto(TileKind.Rubber, new Vector2(5f, -8f), out Vector2 vel);

            Assert.AreEqual(5f, vel.x, 1e-4f, "sideways travel should survive the launch");
            Assert.AreEqual(Launch, vel.y, 1e-4f);
        }

        [Test]
        public void SideFacingRubber_LaunchesAlongItsOwnNormal()
        {
            TileGrid grid = SlabRoom(TileKind.Empty);
            grid.Fill(20, 2, 1, 10, TileKind.Rubber);
            BalloonTuning tuning = Tuning();

            var pos = new Vector2(20f - Radius - 0.001f, 6f);
            var vel = new Vector2(9f, 0f);
            SweptCircle.Move(grid, tuning, Radius, ref pos, ref vel, Dt);

            Assert.AreEqual(-Launch, vel.x, 1e-4f, "a rubber wall should throw it back");
        }

        // --- what rubber is not -----------------------------------------------------

        [Test]
        public void Rubber_BlocksTheBalloon_ButNeverPopsIt()
        {
            Assert.IsTrue(TileGrid.BlocksBalloon(TileKind.Rubber));
            Assert.IsFalse(Hazards.KillsBalloon(TileKind.Rubber));
        }

        [Test]
        public void Rubber_IsOrdinaryGroundForThePlayer()
        {
            Assert.IsTrue(TileGrid.BlocksPlayer(TileKind.Rubber));
            Assert.IsFalse(Hazards.KillsPlayer(TileKind.Rubber));
        }

        [Test]
        public void Rubber_RoundTripsThroughItsLevelChar()
        {
            Assert.AreEqual(TileKind.Rubber, TileGrid.FromChar('o'));
            Assert.AreEqual('o', TileGrid.ToChar(TileKind.Rubber));
        }
    }
}
