using KeepyUppy.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The rule half of render interpolation. The views that use it only draw, so this is
    /// where the behaviour is actually pinned — the same boundary ShellView sits on.
    /// </summary>
    public sealed class StepInterpolatorTests
    {
        [Test]
        public void FirstSync_SnapsRatherThanTweeningFromTheOrigin()
        {
            var interp = new StepInterpolator();
            interp.Sync(0, new Vector2(16f, 12f));

            Assert.AreEqual(new Vector2(16f, 12f), interp.Sample(0f));
            Assert.AreEqual(new Vector2(16f, 12f), interp.Sample(1f));
        }

        [Test]
        public void OneStep_TweensAcrossIt()
        {
            var interp = new StepInterpolator();
            interp.Sync(0, new Vector2(10f, 10f));
            interp.Sync(1, new Vector2(10f, 11f));

            Assert.AreEqual(10f, interp.Sample(0f).y, 1e-5f);
            Assert.AreEqual(10.5f, interp.Sample(0.5f).y, 1e-5f);
            Assert.AreEqual(11f, interp.Sample(1f).y, 1e-5f);
        }

        [Test]
        public void TheTweenSpansOnlyTheLatestStep()
        {
            // Sync is called per fixed step, so each new step retires the one before it.
            // Nothing accumulates: two steps in one render frame still leave a one-step tween.
            var interp = new StepInterpolator();
            interp.Sync(0, new Vector2(0f, 10f));
            interp.Sync(1, new Vector2(0f, 9f));
            interp.Sync(2, new Vector2(0f, 8f));

            Assert.AreEqual(9f, interp.Sample(0f).y, 1e-5f);
            Assert.AreEqual(8f, interp.Sample(1f).y, 1e-5f);
        }

        [Test]
        public void SamplingTwiceWithoutAStep_IsAPureFunctionOfAlpha()
        {
            // The frame that renders twice inside one fixed step is the case the judder comes
            // from. Drawing must stay a continuous function of alpha, not of frame count.
            var interp = new StepInterpolator();
            interp.Sync(0, new Vector2(0f, 10f));
            interp.Sync(1, new Vector2(0f, 9f));

            Assert.AreEqual(9.75f, interp.Sample(0.25f).y, 1e-5f);
            Assert.AreEqual(9.5f, interp.Sample(0.5f).y, 1e-5f);
            Assert.AreEqual(9.75f, interp.Sample(0.25f).y, 1e-5f, "sampling must not advance it");
        }

        [Test]
        public void ARestart_SnapsToSpawnInsteadOfSlidingAcrossTheRoom()
        {
            // GDD 8: the retry is instant. The world's step counter running backwards is how
            // a restart is recognised.
            var interp = new StepInterpolator();
            interp.Sync(40, new Vector2(29f, 2f));
            interp.Sync(41, new Vector2(29.2f, 1.8f));

            interp.Sync(0, new Vector2(6f, 13f));   // died, respawned

            Assert.AreEqual(new Vector2(6f, 13f), interp.Sample(0f));
            Assert.AreEqual(new Vector2(6f, 13f), interp.Sample(0.5f));
        }

        [Test]
        public void Alpha_IsTheFractionIntoTheStepAndNeverExtrapolates()
        {
            float dt = 1f / 60f;

            Assert.AreEqual(0f, StepInterpolator.AlphaOf(10f, 10f, dt), 1e-5f);
            Assert.AreEqual(0.5f, StepInterpolator.AlphaOf(10f + dt * 0.5f, 10f, dt), 1e-5f);

            // A frame that overruns its step must clamp, not predict a position the sim never
            // produced.
            Assert.AreEqual(1f, StepInterpolator.AlphaOf(10f + dt * 3f, 10f, dt), 1e-5f);
            Assert.AreEqual(0f, StepInterpolator.AlphaOf(9f, 10f, dt), 1e-5f);
        }

        [Test]
        public void Alpha_SurvivesAZeroLengthStep()
        {
            Assert.AreEqual(1f, StepInterpolator.AlphaOf(10f, 10f, 0f), 1e-5f);
        }

        /// <summary>
        /// One drifting-display run. <paramref name="frameRate"/> is the render frame length
        /// as a multiple of the fixed step: above 1 the display is slower than the sim and
        /// periodically sees two steps in a frame; below 1 it is faster and periodically sees
        /// none. Both happen on real hardware and both produce the artefact.
        /// </summary>
        static void RunDescent(float frameRate, out float smoothRatio,
                               out float rawRatio, out float minDrawnGap)
        {
            const float dt = 1f / 60f;
            const float speed = -2f;   // terminal fall

            var interp = new StepInterpolator();

            float y = 10f;
            int step = 0;
            float renderTime = 0f;
            float fixedTime = 0f;

            interp.Sync(step, new Vector2(0f, y));

            float lastDrawn = interp.Sample(0f).y;
            float lastRaw = y;
            float minGap = float.MaxValue, maxGap = 0f;
            float rawMin = float.MaxValue, rawMax = 0f;

            for (int frame = 0; frame < 600; frame++)
            {
                renderTime += dt * frameRate;

                // FixedUpdate: the driver steps the world, then each view takes its sample.
                while (fixedTime + dt <= renderTime)
                {
                    fixedTime += dt;
                    step++;
                    y += speed * dt;
                    interp.Sync(step, new Vector2(0f, y));
                }

                float drawn = interp.Sample(StepInterpolator.AlphaOf(renderTime, fixedTime, dt)).y;

                if (frame > 2)
                {
                    float gap = Mathf.Abs(drawn - lastDrawn);
                    if (gap < minGap) minGap = gap;
                    if (gap > maxGap) maxGap = gap;

                    float rawGap = Mathf.Abs(y - lastRaw);
                    if (rawGap < rawMin) rawMin = rawGap;
                    if (rawGap > rawMax) rawMax = rawGap;
                }

                lastDrawn = drawn;
                lastRaw = y;
            }

            minDrawnGap = minGap;
            smoothRatio = maxGap / minGap;
            rawRatio = rawMax / Mathf.Max(rawMin, 1e-6f);
        }

        [Test]
        public void ASteadyDescent_DrawsAtAConstantRateOnASlowerDisplay()
        {
            // The reported bug, as a property. A display not phase-locked to the 60 Hz sim
            // drifts, so periodically one frame sees two fixed steps. Sampling raw then draws
            // a double-length gap on that frame, and the unevenness IS the mid-air bounce the
            // player sees on a balloon descending at 0.67 px per frame.
            RunDescent(1.013f, out float smooth, out float raw, out float minGap);

            Assert.Greater(minGap, 0f, "a frame drew no movement at all - that is the hitch");
            Assert.Less(smooth, 1.05f, $"uneven frame gaps, ratio {smooth}");

            // The same run sampled raw is the artefact being fixed, so this cannot quietly
            // start passing for the wrong reason.
            Assert.Greater(raw, 1.5f, $"raw sampling should be uneven, ratio {raw}");
        }

        [Test]
        public void ASteadyDescent_DrawsAtAConstantRateOnAFasterDisplay()
        {
            // The mirror case: a display faster than the sim periodically sees no step at all
            // and draws the same position twice - a dead frame in the middle of a fall.
            RunDescent(0.987f, out float smooth, out float raw, out float minGap);

            Assert.Greater(minGap, 0f, "a frame drew no movement at all - that is the hitch");
            Assert.Less(smooth, 1.05f, $"uneven frame gaps, ratio {smooth}");
            Assert.IsTrue(float.IsInfinity(raw) || raw > 1.5f,
                          $"raw sampling should stall on some frames, ratio {raw}");
        }
    }
}
