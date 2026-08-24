using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Draws strings with <see cref="BitmapFont"/> as quads, the same way <see cref="QuadView"/>
    /// draws every other piece of placeholder geometry (GDD 13.3) — no UI package, no font
    /// asset pipeline.
    ///
    /// One quad per lit pixel is deliberate: at 5x7 even a menu-length string is only a few
    /// hundred quads, they all share <see cref="QuadView"/>'s one material, and reading straight
    /// from <see cref="BitmapFont.Rows"/> avoids inventing a mesh builder before anything needs
    /// per-instance UVs (the atlas exists for a future textured renderer, not this one).
    /// </summary>
    public static class TextView
    {
        /// <summary>World size of one font pixel at scale 1: 20 px per tile/unit (CLAUDE.md).</summary>
        const float PixelSize = 1f / 20f;

        /// <summary>
        /// Builds <paramref name="text"/> under a fresh child of <paramref name="parent"/>, one
        /// quad per lit pixel, and returns that child so <see cref="Clear"/> can tear it down.
        ///
        /// <paramref name="origin"/> is the top-left corner of the string's bounding box, rounded
        /// to the pixel grid: CLAUDE.md bans pixel-snapping moving entities because it judders
        /// the object the player is tracking, but text here is static and unsnapped text at this
        /// resolution shimmers instead.
        ///
        /// Row-to-y mapping: <see cref="BitmapFont.Rows"/> packs row 0 at the top and Unity
        /// world space has +y up, so row 0 must land at the *highest* y under the origin and
        /// increasing row must move *down* (decreasing y) — the opposite direction from how row
        /// increases in the packed bits' iteration order used to read them out. Column 0 is the
        /// left column in both the font's encoding and world space, so it maps directly to
        /// increasing x with no flip. Hand-check with 'F' (BitmapFontTests.
        /// RowsCatchesAHorizontalMirrorUsingF), whose rows are 11111/10000/10000/11110/10000/
        /// 10000/10000: with this mapping row 0 (11111) draws the top bar, row 3 (11110) draws
        /// the middle bar one column short of the right edge, and there is no bottom bar — an
        /// upright F, not a mirrored or upside-down one.
        /// </summary>
        public static GameObject Draw(Transform parent, string text, Vector2 origin, Color colour, int scale = 1)
        {
            var root = new GameObject("text");
            root.transform.SetParent(parent, false);

            if (string.IsNullOrEmpty(text))
                return root;

            float pixel = PixelSize * scale;
            Vector2 snappedOrigin = new Vector2(
                Mathf.Round(origin.x / PixelSize) * PixelSize,
                Mathf.Round(origin.y / PixelSize) * PixelSize);

            for (int charIndex = 0; charIndex < text.Length; charIndex++)
            {
                // Rows() already renders an unknown character as a filled box rather than
                // silently skipping it, so there is nothing to special-case here.
                char c = text[charIndex];
                ulong bits = BitmapFont.Rows(c);
                float charLeft = snappedOrigin.x + charIndex * BitmapFont.Advance * pixel;

                for (int row = 0; row < BitmapFont.GlyphHeight; row++)
                {
                    for (int col = 0; col < BitmapFont.GlyphWidth; col++)
                    {
                        int bitIndex = (BitmapFont.GlyphHeight - 1 - row) * BitmapFont.GlyphWidth
                                       + (BitmapFont.GlyphWidth - 1 - col);
                        if ((bits & (1UL << bitIndex)) == 0) continue;

                        float x = charLeft + (col + 0.5f) * pixel;
                        float y = snappedOrigin.y - (row + 0.5f) * pixel;

                        QuadView.Create(root.transform, $"px_{charIndex}_{row}_{col}", colour,
                                         new Vector2(x, y), new Vector2(pixel, pixel), 0f);
                    }
                }
            }

            return root;
        }

        /// <summary>Tears down everything <see cref="Draw"/> built.</summary>
        public static void Clear(GameObject drawn)
        {
            if (drawn == null) return;
            Object.Destroy(drawn);
        }
    }
}
