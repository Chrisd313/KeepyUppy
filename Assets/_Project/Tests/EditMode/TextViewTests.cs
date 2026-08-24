using KeepyUppy.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Pins <see cref="TextView.PixelCentre"/>'s orientation: <see cref="BitmapFont"/> packs
    /// row 0 at the top, but Unity world space has +y up, so row and y must move in opposite
    /// directions or every string this renderer draws comes out upside down with no compile
    /// error and no other test noticing. Column and x agree in direction, but that's pinned too
    /// so a future edit can't quietly break it.
    /// </summary>
    public sealed class TextViewTests
    {
        // 'F' (0b11111_10000_10000_11110_10000_10000_10000, BitmapFontTests.
        // RowsCatchesAHorizontalMirrorUsingF) has column 0 lit on every row, so its top-left
        // lit pixel is (row 0, col 0) and its bottom-left lit pixel is (row 6, col 0) - the two
        // ends of the same column, which isolates the row/y direction from the column/x one.
        const int TopRow = 0;
        const int BottomRow = 6;
        const int LeftCol = 0;
        const int RightCol = 4;

        [Test]
        public void TopOfGlyphIsHigherYThanBottom()
        {
            // origin (0,0), pixel 1/20 (scale 1). Centres are (row + 0.5) pixels below origin.y,
            // stated here rather than derived from the formula under test:
            //   row 0 centre y = 0 - 0.5 * 0.05 = -0.025
            //   row 6 centre y = 0 - 6.5 * 0.05 = -0.325
            // -0.025 > -0.325, so the top row must land at the higher y. If the sign in
            // PixelCentre's y expression were flipped, row 0 would compute to +0.025 and row 6
            // to +0.325, making the top row the *lower* y - this assertion would then fail.
            Vector2 top = TextView.PixelCentre(Vector2.zero, charIndex: 0, row: TopRow, col: LeftCol, pixel: 0.05f);
            Vector2 bottom = TextView.PixelCentre(Vector2.zero, charIndex: 0, row: BottomRow, col: LeftCol, pixel: 0.05f);

            Assert.AreEqual(-0.025f, top.y, 1e-6f);
            Assert.AreEqual(-0.325f, bottom.y, 1e-6f);
            Assert.Greater(top.y, bottom.y);
        }

        [Test]
        public void LeftOfGlyphIsLowerXThanRight()
        {
            // Same origin/pixel; row 0 has every column lit, so col 0 and col 4 are both real
            // pixels of 'F'. Centres are (col + 0.5) pixels right of origin.x:
            //   col 0 centre x = 0 + 0.5 * 0.05 = 0.025
            //   col 4 centre x = 0 + 4.5 * 0.05 = 0.225
            Vector2 left = TextView.PixelCentre(Vector2.zero, charIndex: 0, row: TopRow, col: LeftCol, pixel: 0.05f);
            Vector2 right = TextView.PixelCentre(Vector2.zero, charIndex: 0, row: TopRow, col: RightCol, pixel: 0.05f);

            Assert.AreEqual(0.025f, left.x, 1e-6f);
            Assert.AreEqual(0.225f, right.x, 1e-6f);
            Assert.Less(left.x, right.x);
        }
    }
}
