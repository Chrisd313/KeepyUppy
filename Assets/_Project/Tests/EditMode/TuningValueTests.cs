using System.Globalization;
using System.Threading;
using KeepyUppy.Runtime;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The tuning panel's value boxes. Pulled out of the panel so the parts that actually
    /// break — locale, integer fields, partial input, values outside a guessed slider range —
    /// can be tested without an IMGUI context.
    /// </summary>
    public sealed class TuningValueTests
    {
        [Test]
        public void Parses_PlainNumbers()
        {
            Assert.IsTrue(TuningValue.TryParse("2.45", integer: false, out float v));
            Assert.AreEqual(2.45f, v, 1e-6f);

            Assert.IsTrue(TuningValue.TryParse("-3", integer: false, out v));
            Assert.AreEqual(-3f, v, 1e-6f);
        }

        [Test]
        public void PartialInput_DoesNotApply()
        {
            // Typing "2." must not blank the value or snap the box back mid-keystroke.
            Assert.IsFalse(TuningValue.TryParse("", false, out _));
            Assert.IsFalse(TuningValue.TryParse("-", false, out _));
            Assert.IsFalse(TuningValue.TryParse("2.", false, out _), "trailing separator");
            Assert.IsFalse(TuningValue.TryParse("abc", false, out _));
        }

        [Test]
        public void RejectsNaNAndInfinity()
        {
            // A NaN reaching the sim would poison every subsequent step.
            Assert.IsFalse(TuningValue.TryParse("NaN", false, out _));
            Assert.IsFalse(TuningValue.TryParse("Infinity", false, out _));
            Assert.IsFalse(TuningValue.TryParse("-Infinity", false, out _));
        }

        [Test]
        public void IntegerFields_Round()
        {
            Assert.IsTrue(TuningValue.TryParse("6.7", integer: true, out float v));
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

                Assert.IsTrue(TuningValue.TryParse("2.45", false, out float v));
                Assert.AreEqual(2.45f, v, 1e-6f);

                Assert.AreEqual("2.45", TuningValue.Format(2.45f, false));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void Format_MatchesFieldKind()
        {
            Assert.AreEqual("2.45", TuningValue.Format(2.45f, false));
            Assert.AreEqual("9", TuningValue.Format(9f, false));
            Assert.AreEqual("6", TuningValue.Format(6.4f, integer: true));
        }

        [Test]
        public void SliderRange_WidensToContainATypedValue()
        {
            // The whole point of the box: type past the range the slider was guessed at, and
            // keep the value instead of having it dragged back inside next frame.
            TuningValue.SliderRange(120f, 0f, 40f, out float low, out float high);
            Assert.AreEqual(0f, low, 1e-6f);
            Assert.GreaterOrEqual(high, 120f);

            TuningValue.SliderRange(-5f, 0f, 40f, out low, out high);
            Assert.LessOrEqual(low, -5f);
            Assert.AreEqual(40f, high, 1e-6f);
        }

        [Test]
        public void SliderRange_LeavesNormalValuesAlone()
        {
            TuningValue.SliderRange(9f, 0f, 20f, out float low, out float high);
            Assert.AreEqual(0f, low, 1e-6f);
            Assert.AreEqual(20f, high, 1e-6f);
        }

        [Test]
        public void SliderRange_NeverCollapses()
        {
            TuningValue.SliderRange(0f, 0f, 0f, out float low, out float high);
            Assert.Greater(high, low, "a zero-width slider range would divide by zero in IMGUI");
        }
    }
}
