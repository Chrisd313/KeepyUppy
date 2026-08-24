using KeepyUppy.Sim;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// How many fixed steps SimDriver's FixedUpdate runs in one call.
    ///
    /// GDD 6.1 forbids changing dt, so speed is expressed as the NUMBER of 60 Hz steps run
    /// per call. The simulation cannot tell the difference: the same steps happen in the
    /// same order, and only the wall-clock spent waiting for them changes.
    /// </summary>
    public sealed class TimeControlTests
    {
        [Test]
        public void NormalSpeedIsOneStepPerFrame()
        {
            var time = new TimeControl();

            for (int fixedStep = 0; fixedStep < 10; fixedStep++)
                Assert.AreEqual(1, time.StepsFor(fixedStep), $"fixed step {fixedStep}");
        }

        [Test]
        public void FastForwardRunsExtraStepsPerFrame()
        {
            var time = new TimeControl { Factor = 2 };

            for (int fixedStep = 0; fixedStep < 10; fixedStep++)
                Assert.AreEqual(2, time.StepsFor(fixedStep), $"fixed step {fixedStep}");
        }

        [Test]
        public void SlowMotionRunsAStepEveryOtherFrame()
        {
            // GDD 16's 0.5x accessibility assist is the same mechanism inverted.
            var time = new TimeControl { SlowMotion = true };

            Assert.AreEqual(1, time.StepsFor(0));
            Assert.AreEqual(0, time.StepsFor(1));
            Assert.AreEqual(1, time.StepsFor(2));
            Assert.AreEqual(0, time.StepsFor(3));
        }

        [Test]
        public void SlowMotionBeatsFastForward()
        {
            // An accessibility setting must not be overridden by a convenience one.
            var time = new TimeControl { Factor = 3, SlowMotion = true };

            Assert.AreEqual(1, time.StepsFor(0));
            Assert.AreEqual(0, time.StepsFor(1));
        }

        [Test]
        public void TheFactorIsClamped()
        {
            // A tuning slider is a text box too: a typo must not stall the game for a second
            // while it runs a thousand steps.
            var time = new TimeControl { Factor = 999 };
            Assert.AreEqual(TimeControl.MaxFactor, time.StepsFor(0));

            time.Factor = 0;
            Assert.AreEqual(1, time.StepsFor(0), "zero would stop the game, not slow it");

            time.Factor = -5;
            Assert.AreEqual(1, time.StepsFor(0));
        }

        [Test]
        public void FrameParityIsAllSlowMotionRemembers()
        {
            // Deliberately a pure function of the fixed-step count (SimDriver's own tally of
            // FixedUpdate calls, not a render-frame number - see StepsFor's remarks) rather
            // than an accumulator: no state to drift, and no state to reset when the room
            // restarts.
            var time = new TimeControl { SlowMotion = true };

            Assert.AreEqual(1, time.StepsFor(1000));
            Assert.AreEqual(0, time.StepsFor(1001));
        }
    }
}
