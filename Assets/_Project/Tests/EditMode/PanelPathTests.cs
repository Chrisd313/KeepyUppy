using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Panels;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Where a moving panel is at a given step (moving-panels design).
    ///
    /// A pure function of the step counter, exactly as EntityPath is — that is what makes a
    /// restart free and attempt 40 replay attempt 1 (GDD 6.1).
    /// </summary>
    public sealed class PanelPathTests
    {
        static readonly Vector2[] UpThree = { Vector2.zero, new Vector2(0f, 3f) };

        [Test]
        public void WithNoHold_ItMatchesEntityPath()
        {
            for (int step = 0; step < 200; step += 7)
                Assert.AreEqual(EntityPath.Evaluate(UpThree, PathMode.Once, 6f, 0f, step),
                                PanelPath.Evaluate(UpThree, PathMode.Once, 6f, 0f, 0, step),
                                $"step {step}: with no dwell this must delegate, not reimplement");
        }

        [Test]
        public void ItIsAPureFunction_AskedOutOfOrder()
        {
            Vector2 at90 = PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, 30, 90);

            PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, 30, 5);
            PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, 30, 400);

            Assert.AreEqual(at90, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, 30, 90));
        }

        [Test]
        public void DwellHoldsTheEndpointForExactlyTheHoldSteps()
        {
            // 3 tiles at 6 tiles/s = 0.5 s = 30 steps of travel, then 30 steps of dwell.
            const int hold = 30;

            Assert.AreEqual(3f, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 30).y, 1e-4f,
                            "arrives at the far end after 30 steps");
            Assert.AreEqual(3f, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 59).y, 1e-4f,
                            "still parked one step before the dwell ends");
            Assert.Less(PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 75).y, 3f,
                        "and has started back once it has");
        }

        [Test]
        public void DwellAlsoHoldsTheStart_SoAPingPongPausesAtBothEnds()
        {
            const int hold = 30;

            // Out (30) + dwell (30) + back (30) = 90; the start dwell then runs to step 120.
            Assert.AreEqual(0f, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 90).y, 1e-4f);
            Assert.AreEqual(0f, PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 119).y, 1e-4f);
            Assert.Greater(PanelPath.Evaluate(UpThree, PathMode.PingPong, 6f, 0f, hold, 135).y, 0f);
        }

        [Test]
        public void OnceStopsAtTheEnd_AndAPathlessPanelNeverMoves()
        {
            Assert.AreEqual(3f, PanelPath.Evaluate(UpThree, PathMode.Once, 6f, 0f, 30, 10_000).y, 1e-4f);

            var single = new[] { Vector2.zero };
            Assert.AreEqual(Vector2.zero, PanelPath.Evaluate(single, PathMode.Once, 6f, 0f, 0, 500));
            Assert.AreEqual(Vector2.zero, PanelPath.Evaluate(UpThree, PathMode.Once, 0f, 0f, 0, 500),
                            "speed 0 is a parked panel, not a divide by zero");
        }
    }
}
