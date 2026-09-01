using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// GDD 6.1. Attempt 40 must replay attempt 1 exactly. This is the kind of property that
    /// regresses silently, so it is asserted on every step rather than just the last one.
    ///
    /// The room carries a player driven by a ScriptedInputSource, so the guarantee covers
    /// input, movement, contact and the hit system — not just the balloon.
    /// </summary>
    public sealed class DeterminismTests
    {
        const int Steps = 900;   // 15 seconds at 60 Hz
        const int Seed = 1337;

        [Test]
        public void SameSeed_900Steps_BitIdenticalEveryStep()
        {
            SimWorld a = SimTestHelpers.BuildBusyRoom(Seed);
            SimWorld b = SimTestHelpers.BuildBusyRoom(Seed);

            for (int i = 0; i < Steps; i++)
            {
                a.Tick();
                b.Tick();

                SimSnapshot.Capture(a).AssertMatches(SimSnapshot.Capture(b), $"step {i}");
            }
        }

        [Test]
        public void SameWorld_ResetAndReplayed_BitIdentical()
        {
            SimWorld world = SimTestHelpers.BuildBusyRoom(Seed);

            world.Tick(Steps);
            SimSnapshot first = SimSnapshot.Capture(world);

            // The restart path (GDD 8): same object, reset, run again.
            world.Restart();
            world.Tick(Steps);

            first.AssertMatches(SimSnapshot.Capture(world), "after replay");
        }

        [Test]
        public void SameSeed_WonRoom_WinningFramesMatchAndAreNonZero()
        {
            // BuildBusyRoom never wins (it has no exit), so every comparison built from it
            // asserts WinningFrames as 0 == 0 - structurally covered, never behaviourally
            // exercised. This room actually completes, so the field means something here.
            SimWorld a = SimTestHelpers.BuildWinnableRoom(Seed);
            SimWorld b = SimTestHelpers.BuildWinnableRoom(Seed);

            while (!a.IsWon) a.Tick();
            while (!b.IsWon) b.Tick();

            Assert.Greater(a.WinningFrames, 0, "the room should have been won");
            SimSnapshot.Capture(a).AssertMatches(SimSnapshot.Capture(b), "won room");

            // WinningFrames must keep matching through the win-hold too, where it is the only
            // thing still changing frame to frame - and after the hold expires and Restart()
            // fires, it must still read the same on both runs (it deliberately survives).
            a.Tick(SimWorld.WinHoldFrames);
            b.Tick(SimWorld.WinHoldFrames);

            SimSnapshot.Capture(a).AssertMatches(SimSnapshot.Capture(b), "after win hold");
        }

        [Test]
        public void TrapRoom_SameSeed_BitIdenticalEveryStep()
        {
            // Triggers are the second system to carry real per-step state, after the lamp's
            // exposure. Without this the guarantee rots silently (CLAUDE.md).
            SimWorld a = SimTestHelpers.BuildTrapRoom(Seed);
            SimWorld b = SimTestHelpers.BuildTrapRoom(Seed);

            for (int i = 0; i < Steps; i++)
            {
                a.Tick();
                b.Tick();

                SimSnapshot.Capture(a).AssertMatches(SimSnapshot.Capture(b), $"trap step {i}");
            }
        }

        [Test]
        public void TrapRoom_ResetAndReplayed_BitIdentical()
        {
            SimWorld world = SimTestHelpers.BuildTrapRoom(Seed);

            world.Tick(Steps);
            SimSnapshot first = SimSnapshot.Capture(world);

            world.Restart();
            world.Tick(Steps);

            first.AssertMatches(SimSnapshot.Capture(world), "trap room after replay");
        }

        [Test]
        public void DifferentSeed_ProducesDifferentPath()
        {
            // Guards the opposite failure: a seed that is plumbed but ignored would make
            // every determinism test above pass for the wrong reason. Hashed over the whole
            // run, so two paths that happen to coincide at step 900 still count as different.
            int a = SimTestHelpers.RunHash(SimTestHelpers.BuildBusyRoom(Seed), Steps);
            int b = SimTestHelpers.RunHash(SimTestHelpers.BuildBusyRoom(Seed + 1), Steps);

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void SameSeed_RunHashesMatch()
        {
            int a = SimTestHelpers.RunHash(SimTestHelpers.BuildBusyRoom(Seed), Steps);
            int b = SimTestHelpers.RunHash(SimTestHelpers.BuildBusyRoom(Seed), Steps);

            Assert.AreEqual(a, b);
        }

        [Test]
        public void ScriptedInput_ReplaysIdentically()
        {
            // The scripted source is the reason the tests above mean anything: a keyboard
            // cannot press the same keys on the same frames twice.
            var a = SimTestHelpers.BusyScript();
            var b = SimTestHelpers.BusyScript();

            for (int step = 0; step < 400; step++)
            {
                a.Sample(step);
                b.Sample(step);

                Assert.AreEqual(a.MoveAxis, b.MoveAxis, $"moveAxis at {step}");
                Assert.AreEqual(a.JumpPressed, b.JumpPressed, $"jumpPressed at {step}");
                Assert.AreEqual(a.JumpHeld, b.JumpHeld, $"jumpHeld at {step}");
                Assert.AreEqual(a.ContextPressed, b.ContextPressed, $"contextPressed at {step}");
                Assert.AreEqual(a.ContextHeld, b.ContextHeld, $"contextHeld at {step}");
                Assert.AreEqual(a.AimHint, b.AimHint, $"aimHint at {step}");
            }
        }

        [Test]
        public void Noise_IsPureFunctionOfStepAndSeed()
        {
            for (int step = 0; step < 64; step++)
            {
                float first = DeterministicNoise.Signed(step, Seed, 1);
                float second = DeterministicNoise.Signed(step, Seed, 1);
                Assert.AreEqual(SimTestHelpers.Bits(first), SimTestHelpers.Bits(second));
                Assert.That(first, Is.InRange(-1f, 1f));
            }
        }
    }
}
