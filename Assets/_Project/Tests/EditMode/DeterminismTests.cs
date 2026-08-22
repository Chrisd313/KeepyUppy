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
