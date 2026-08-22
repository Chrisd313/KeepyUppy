using System.Globalization;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Formatting and parsing for the tuning panel's editable value boxes.
    ///
    /// Separated from the panel so the awkward parts — culture, integer fields, refusing
    /// NaN, keeping a typed value that sits outside its slider's range — are testable
    /// without an IMGUI context.
    /// </summary>
    public static class TuningValue
    {
        public static string Format(float value, bool integer)
            => value.ToString(integer ? "0" : "0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses a typed value. Rejects partial input ("", "-", "2.") by returning false and
        /// leaving the field alone, so typing never destroys the value mid-keystroke.
        /// Always invariant: a comma decimal separator must not depend on the machine locale.
        /// </summary>
        public static bool TryParse(string text, bool integer, out float value)
        {
            value = 0f;

            if (string.IsNullOrWhiteSpace(text)) return false;

            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return false;

            if (float.IsNaN(parsed) || float.IsInfinity(parsed)) return false;

            value = integer ? Mathf.Round(parsed) : parsed;
            return true;
        }

        /// <summary>
        /// Slider bounds widened to contain the current value.
        ///
        /// Without this a typed value outside the slider's range would be dragged back inside
        /// it on the very next frame, making the box useless for exactly the case it exists
        /// for — pushing a value past where the range was guessed.
        /// </summary>
        public static void SliderRange(float value, float min, float max, out float low, out float high)
        {
            low = Mathf.Min(min, value);
            high = Mathf.Max(max, value);

            if (high - low < 1e-6f) high = low + 1f;
        }
    }
}
