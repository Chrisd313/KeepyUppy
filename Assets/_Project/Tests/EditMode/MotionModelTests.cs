using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Guards the GDD 6.4 tuning table against the integrator drifting away from the values
    /// the design was reasoned about. The integrator is tested on its own — no grid, no
    /// collision — so a failure here is unambiguous.
    /// </summary>
    public sealed class MotionModelTests
    {
        static float StepToTerminal(BalloonBody body, in BalloonTuning t, int steps = 900)
        {
            for (int i = 0; i < steps; i++) body.Integrate(t, Vector2.zero, i, 0, false);
            return body.Velocity.y;
        }

        [Test]
        public void NominalTerminalFall_IsTwoMetresPerSecond()
        {
            // sqrt(gravity / drag_ky) = sqrt(9.8 / 2.45) = 2.0 — the derivation the table
            // documents. If drag_ky is retuned, the GDD table must move with it.
            BalloonTuning t = BalloonTuning.Default;
            var body = new BalloonBody { Radius = InflationBands.NominalRadius };

            Assert.AreEqual(-2.0f, StepToTerminal(body, t), 0.01f);
        }

        [Test]
        public void LargerBalloon_FallsSlower()
        {
            // Drag scales with radius (GDD 6.2), so the 7.1 band ordering falls out for
            // free rather than being authored.
            BalloonTuning t = BalloonTuning.Default;

            float shrivelled = StepToTerminal(
                new BalloonBody { Radius = InflationBands.RadiusOf(InflationBand.Shrivelled) }, t);
            float nominal = StepToTerminal(
                new BalloonBody { Radius = InflationBands.RadiusOf(InflationBand.Nominal) }, t);
            float critical = StepToTerminal(
                new BalloonBody { Radius = InflationBands.RadiusOf(InflationBand.Critical) }, t);

            Assert.Less(shrivelled, nominal, "shrivelled should fall faster than nominal");
            Assert.Less(nominal, critical, "nominal should fall faster than critical");
        }

        [Test]
        public void HorizontalDragIsMuchLowerThanVertical()
        {
            // GDD 6.3 — the deliberate lie that lets the balloon cross the room.
            BalloonTuning t = BalloonTuning.Default;
            Assert.Less(t.dragKx, t.dragKy * 0.5f);
        }

        [Test]
        public void InsideUpdraught_BalloonRises()
        {
            // v_air is the whole wind system (GDD 6.2): drag against relative velocity is
            // the only mechanism, with no per-hazard code.
            BalloonTuning t = BalloonTuning.Default;
            var body = new BalloonBody { Radius = InflationBands.NominalRadius };
            var vAir = new Vector2(0f, 6f);

            for (int i = 0; i < 600; i++) body.Integrate(t, vAir, i, 0, false);

            Assert.Greater(body.Velocity.y, 0.5f, "a 6 m/s updraught should lift the balloon");
        }

        [Test]
        public void SimClock_IsSixtyHertz()
        {
            Assert.AreEqual(60, SimClock.Hz);
            Assert.AreEqual(1f / 60f, SimClock.Dt, 0f);
        }
    }
}
