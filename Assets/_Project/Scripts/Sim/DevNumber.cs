using System.Globalization;
using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// Formatting and parsing for the dev panels' editable value boxes.
    ///
    /// Separated from the panels so the awkward parts — culture, integer fields, refusing
    /// NaN, keeping a typed value that sits outside its slider's range — are testable
    /// without an IMGUI context.
    ///
    /// It lives in Sim, which is neither its home by rights nor an accident: the tuning panel
    /// is in Runtime and the level editor's inspector is in LevelEditor, and Runtime depends
    /// on LevelEditor rather than the reverse. Sim is the only assembly both can see. Two
    /// copies of "what does typing 2. mean" is the outcome worth avoiding; a plain string
    /// parser sitting beside the simulation is the price.
    /// </summary>
    public static class DevNumber
    {
        public static string Format(float value, bool integer)
            => value.ToString(integer ? "0" : "0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses a typed value. Input that means nothing yet ("", "-", "abc") returns false
        /// and leaves the value alone, so typing never destroys it mid-keystroke. Input that
        /// does parse applies immediately, including partial forms like "2." and ".5" — the
        /// box keeps showing what was typed, so the value follows the text rather than the
        /// text snapping to the value.
        ///
        /// Always invariant: the decimal separator must not depend on the machine locale.
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
