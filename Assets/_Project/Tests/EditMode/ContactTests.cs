using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The LOCKED rules that make the game a game: GDD 4.2 (passive contact loses energy,
    /// only active input adds it), GDD 5.1 (set velocity, never add force) and GDD 6.5
    /// (contact-offset aiming).
    ///
    /// Break any of these and the optimal strategy becomes standing still, or the same input
    /// stops producing the same result. Both are fatal.
    /// </summary>
    public sealed class ContactTests
    {
        /// <summary>Balloon resting just above the player's head, falling at a known speed.</summary>
        static SimWorld Contact(float balloonVy, float playerVy)
        {
            SimWorld world = SimTestHelpers.BuildContactRoom();

            // Player standing on the floor; balloon one contact-width above the head.
            float headY = 1.8f + world.Player.HalfHeight;
            world.Configure(new Vector2(16f, headY + world.Balloon.Radius + 0.05f),
                            new Vector2(0f, balloonVy),
                            new Vector2(16f, 1.8f), null, 1);

            world.Player.Velocity = new Vector2(0f, playerVy);
            return world;
        }

        [Test]
        public void PassiveContact_LosesEnergy()
        {
            // LOCKED GDD 4.2. Without this, standing under the balloon wins every level.
            SimWorld world = Contact(balloonVy: -4f, playerVy: 0f);
            float incoming = world.Balloon.Velocity.magnitude;

            for (int i = 0; i < 60 && !world.Hits.LastHit.valid; i++) world.Tick();

            Assert.IsTrue(world.Hits.LastHit.valid, "no contact occurred");
            Assert.AreEqual(HitSource.PassiveContact, world.Hits.LastHit.source);
            Assert.Less(world.Hits.LastHit.outgoingSpeed, world.Hits.LastHit.incomingSpeed,
                        "a passive contact must lose energy");
            Assert.LessOrEqual(world.Hits.LastHit.outgoingSpeed, incoming);
        }

        [Test]
        public void PassiveContact_DoesNotCountAsATouch()
        {
            // GDD 15: the touch-count medal measures deliberate hits, not the balloon
            // resting on someone's head.
            SimWorld world = Contact(balloonVy: -4f, playerVy: 0f);

            for (int i = 0; i < 60 && !world.Hits.LastHit.valid; i++) world.Tick();

            Assert.AreEqual(HitSource.PassiveContact, world.Hits.LastHit.source);
            Assert.AreEqual(0, world.TouchCount);
        }

        [Test]
        public void StandingStill_NeverGainsHeight()
        {
            // The rule stated as its consequence (LOCKED GDD 4.2): every bounce off a
            // stationary player must peak lower than the one before. If this ever fails, the
            // optimal strategy in every level becomes standing under the balloon and waiting.
            SimWorld world = SimTestHelpers.BuildContactRoom();
            world.Configure(new Vector2(16f, 8f), Vector2.zero, new Vector2(16f, 1.8f), null, 1);

            var peaks = new System.Collections.Generic.List<float> { world.Balloon.Position.y };
            float previous = world.Balloon.Position.y;
            bool rising = false;

            for (int i = 0; i < 1800; i++)
            {
                world.Tick();
                float y = world.Balloon.Position.y;

                if (y > previous) rising = true;
                else if (rising) { peaks.Add(previous); rising = false; }

                previous = y;
            }

            Assert.GreaterOrEqual(peaks.Count, 3, "the balloon should have bounced a few times");
            Assert.AreEqual(0, world.TouchCount, "none of those contacts were active");

            for (int i = 1; i < peaks.Count; i++)
            {
                Assert.Less(peaks[i], peaks[i - 1],
                    $"bounce {i} peaked at {peaks[i]:F3}, higher than the previous {peaks[i - 1]:F3}");
            }
        }

        [Test]
        public void ActiveBump_AddsEnergy()
        {
            SimWorld world = Contact(balloonVy: -4f, playerVy: 6f);
            float incoming = world.Balloon.Velocity.magnitude;

            for (int i = 0; i < 60 && !world.Hits.LastHit.valid; i++) world.Tick();

            Assert.IsTrue(world.Hits.LastHit.valid, "no contact occurred");
            Assert.AreEqual(HitSource.JumpBump, world.Hits.LastHit.source);
            Assert.Greater(world.Hits.LastHit.outgoingSpeed, incoming,
                           "an active bump must add energy");
            Assert.AreEqual(1, world.TouchCount, "an active hit counts as a touch");
        }

        [Test]
        public void ContactIsActive_WhenJumpWasPressedInsideTheBufferWindow()
        {
            // GDD 4.2's second clause: a jump pressed within the input buffer counts as
            // active even if the player is not yet rising.
            SimWorld world = Contact(balloonVy: -4f, playerVy: 0f);
            world.Player.JumpBufferFrames = 3;

            world.Tick();

            Assert.IsTrue(world.Hits.LastHit.valid, "no contact occurred");
            Assert.AreEqual(HitSource.JumpBump, world.Hits.LastHit.source);
        }

        [Test]
        public void ApplyHit_SetsRatherThanAdds()
        {
            // LOCKED GDD 6.4. Identical parameters must produce an identical outcome no
            // matter what the balloon was doing beforehand.
            SimWorld slow = SimTestHelpers.BuildContactRoom();
            SimWorld fast = SimTestHelpers.BuildContactRoom();

            slow.Balloon.Velocity = new Vector2(-0.4f, -1.2f);
            fast.Balloon.Velocity = new Vector2(11f, 9.5f);

            var direction = new Vector2(0.3f, 1f);
            slow.Hits.ApplyHit(HitSource.JumpBump, direction, 1f, 0.25f);
            fast.Hits.ApplyHit(HitSource.JumpBump, direction, 1f, 0.25f);

            Assert.AreEqual(SimTestHelpers.Bits(slow.Balloon.Velocity.x),
                            SimTestHelpers.Bits(fast.Balloon.Velocity.x), "vx");
            Assert.AreEqual(SimTestHelpers.Bits(slow.Balloon.Velocity.y),
                            SimTestHelpers.Bits(fast.Balloon.Velocity.y), "vy");
        }

        [Test]
        public void MaxContactOffset_AimsAtFortyDegrees()
        {
            // GDD 6.5: the offset between head centre and balloon centre tilts the outgoing
            // angle up to aim_offset_max_angle off vertical. Breakout paddle physics.
            SimWorld world = SimTestHelpers.BuildContactRoom();
            world.Player.Velocity = Vector2.zero;   // no inheritance to skew the angle

            world.Hits.ApplyHit(HitSource.JumpBump, Vector2.up, 1f, 1f);
            float right = Vector2.SignedAngle(Vector2.up, world.Balloon.Velocity);

            world.Hits.ApplyHit(HitSource.JumpBump, Vector2.up, 1f, -1f);
            float left = Vector2.SignedAngle(Vector2.up, world.Balloon.Velocity);

            Assert.AreEqual(-world.Tuning.aimOffsetMaxAngle, right, 0.5f,
                            "a full right offset should aim 40 degrees to the right");
            Assert.AreEqual(world.Tuning.aimOffsetMaxAngle, left, 0.5f,
                            "a full left offset should aim 40 degrees to the left");
        }

        [Test]
        public void ZeroContactOffset_AimsStraightUp()
        {
            SimWorld world = SimTestHelpers.BuildContactRoom();
            world.Player.Velocity = Vector2.zero;

            world.Hits.ApplyHit(HitSource.JumpBump, Vector2.up, 1f, 0f);

            Assert.AreEqual(0f, world.Balloon.Velocity.x, 1e-4f);
            Assert.AreEqual(world.Tuning.bumpVelocity, world.Balloon.Velocity.y, 1e-3f);
        }

        [Test]
        public void RunningBump_InheritsHorizontalVelocity()
        {
            // GDD 6.4 bump_inherit_x: running bumps travel.
            SimWorld world = SimTestHelpers.BuildContactRoom();
            world.Player.Velocity = new Vector2(6f, 0f);

            world.Hits.ApplyHit(HitSource.JumpBump, Vector2.up, 1f, 0f);

            Assert.AreEqual(6f * world.Tuning.bumpInheritX, world.Balloon.Velocity.x, 1e-3f);
        }

        [Test]
        public void ProfileA_HasNoPalmOrSpike()
        {
            // GDD 5.2/5.3: switching profile is a config change, not a code path.
            ControlProfileConfig a = ControlProfiles.Of(ControlProfile.JumpOnly);
            ControlProfileConfig b = ControlProfiles.Of(ControlProfile.JumpAndContext);
            ControlProfileConfig c = ControlProfiles.Of(ControlProfile.PropsOnly);

            Assert.IsFalse(a.palmEnabled); Assert.IsFalse(a.spikeEnabled);
            Assert.IsTrue(b.palmEnabled);  Assert.IsTrue(b.spikeEnabled);
            Assert.IsFalse(c.palmEnabled); Assert.IsFalse(c.spikeEnabled);
            Assert.IsTrue(c.contextDrivesProps, "C reserves context for props");
        }

        [Test]
        public void AimTable_MatchesTheGdd()
        {
            BalloonTuning t = BalloonTuning.Default;

            PlayerVerbs.ResolveAim(Vector2.zero, t, out Vector2 dir, out float power);
            Assert.AreEqual(0f, dir.x, 1e-4f, "nothing held aims straight up");
            Assert.AreEqual(1f, power, 1e-4f);

            PlayerVerbs.ResolveAim(new Vector2(0f, 1f), t, out dir, out power);
            Assert.AreEqual(1f + t.aimUpPowerBonus, power, 1e-4f, "up adds power");

            PlayerVerbs.ResolveAim(new Vector2(0f, -1f), t, out dir, out power);
            Assert.Less(dir.y, 0f, "down is a downward smash");

            PlayerVerbs.ResolveAim(new Vector2(1f, 0f), t, out dir, out power);
            Assert.AreEqual(-t.aimSideAngle, Vector2.SignedAngle(Vector2.up, dir), 0.5f);
        }

        [Test]
        public void JumpVelocityIsDerivedFromHeight()
        {
            // v = sqrt(2 g h). The height is the tunable; the velocity is never typed.
            PlayerTuning t = PlayerTuning.Default;
            Assert.AreEqual(Mathf.Sqrt(2f * t.gravity * t.jumpHeightTiles), t.JumpVelocity, 1e-4f);

            t.jumpHeightTiles = 4.0f;
            Assert.AreEqual(Mathf.Sqrt(2f * 9.8f * 4.0f), t.JumpVelocity, 1e-4f);
        }

        [Test]
        public void PlayerBumpVolumeIsLargerThanTheBalloon_AndHazardsSmaller()
        {
            // GDD 8: generous to the player, stingy to the level.
            BalloonTuning t = BalloonTuning.Default;
            Assert.Greater(t.playerBumpRadiusBonus, 0f);
            Assert.Greater(t.hazardPopRadiusShrink, 0f);
        }
    }
}
