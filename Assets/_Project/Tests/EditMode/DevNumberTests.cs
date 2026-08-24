using System.Globalization;
using System.Threading;
using KeepyUppy.Sim;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The dev panels' value boxes. Pulled out of the panel so the parts that actually
    /// break — locale, integer fields, partial input, values outside a guessed slider range —
    /// can be tested without an IMGUI context.
    /// </summary>
    public sealed class DevNumberTests
    {
        [Test]
        public void Parses_PlainNumbers()
        {
            Assert.IsTrue(DevNumber.TryParse("2.45", integer: false, out float v));
            Assert.AreEqual(2.45f, v, 1e-6f);

            Assert.IsTrue(DevNumber.TryParse("-3", integer: false, out v));
            Assert.AreEqual(-3f, v, 1e-6f);
        }

        [Test]
        public void UnparseableInput_DoesNotApply()
        {
            // Nothing usable yet: leave the value alone and let the typist keep going.
            Assert.IsFalse(DevNumber.TryParse("", false, out _));
            Assert.IsFalse(DevNumber.TryParse("-", false, out _));
            Assert.IsFalse(DevNumber.TryParse("abc", false, out _));
            Assert.IsFalse(DevNumber.TryParse("1.2.3", false, out _));
        }

        [Test]
        public void MidTypingInput_AppliesWhatItHasSoFar()
        {
            // "2." and ".5" both parse, so the value updates as you type. Harmless, because
            // the box keeps showing exactly what was typed until focus is lost — the value
            // follows the text rather than the text snapping to the value.
            Assert.IsTrue(DevNumber.TryParse("2.", false, out float v));
            Assert.AreEqual(2f, v, 1e-6f);

            Assert.IsTrue(DevNumber.TryParse(".5", false, out v));
            Assert.AreEqual(0.5f, v, 1e-6f);
        }

        [Test]
        public void RejectsNaNAndInfinity()
        {
            // A NaN reaching the sim would poison every subsequent step.
            Assert.IsFalse(DevNumber.TryParse("NaN", false, out _));
            Assert.IsFalse(DevNumber.TryParse("Infinity", false, out _));
            Assert.IsFalse(DevNumber.TryParse("-Infinity", false, out _));
        }

        [Test]
        public void IntegerFields_Round()
        {
            Assert.IsTrue(DevNumber.TryParse("6.7", integer: true, out float v));
            Assert.AreEqual(7f, v, 1e-6f);
        }

        [Test]
        public void ParsingIsCultureInvariant()
        {
            // On a comma-decimal locale, "2.45" must still be two point four five.
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                Assert.IsTrue(DevNumber.TryParse("2.45", false, out float v));
                Assert.AreEqual(2.45f, v, 1e-6f);

                Assert.AreEqual("2.45", DevNumber.Format(2.45f, false));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void Format_MatchesFieldKind()
        {
            Assert.AreEqual("2.45", DevNumber.Format(2.45f, false));
            Assert.AreEqual("9", DevNumber.Format(9f, false));
            Assert.AreEqual("6", DevNumber.Format(6.4f, integer: true));
        }

        [Test]
        public void SliderRange_WidensToContainATypedValue()
        {
            // The whole point of the box: type past the range the slider was guessed at, and
            // keep the value instead of having it dragged back inside next frame.
            DevNumber.SliderRange(120f, 0f, 40f, out float low, out float high);
            Assert.AreEqual(0f, low, 1e-6f);
            Assert.GreaterOrEqual(high, 120f);

            DevNumber.SliderRange(-5f, 0f, 40f, out low, out high);
            Assert.LessOrEqual(low, -5f);
            Assert.AreEqual(40f, high, 1e-6f);
        }

        [Test]
        public void SliderRange_LeavesNormalValuesAlone()
        {
            DevNumber.SliderRange(9f, 0f, 20f, out float low, out float high);
            Assert.AreEqual(0f, low, 1e-6f);
            Assert.AreEqual(20f, high, 1e-6f);
        }

        [Test]
        public void SliderRange_NeverCollapses()
        {
            DevNumber.SliderRange(0f, 0f, 0f, out float low, out float high);
            Assert.Greater(high, low, "a zero-width slider range would divide by zero in IMGUI");
        }
    }
}
