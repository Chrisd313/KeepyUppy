using KeepyUppy.Runtime;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The 5x7 font the menus and the HUD draw with.
    ///
    /// Built in code rather than imported, like everything else in this project. The tests
    /// pin the format and the metrics, not the artwork - a glyph's shape is judged by
    /// looking at it.
    /// </summary>
    public sealed class BitmapFontTests
    {
        [Test]
        public void EveryCharacterInTheSetHasAGlyph()
        {
            const string set = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,:!?-/'()[]*+%";

            foreach (char c in set)
                Assert.IsTrue(BitmapFont.Has(c), $"no glyph for '{c}'");
        }

        [Test]
        public void LowercaseMapsToUppercase()
        {
            // A 5x7 lowercase set with descenders is not worth it at this resolution.
            Assert.AreEqual(BitmapFont.Rows('A'), BitmapFont.Rows('a'));
        }

        [Test]
        public void AnUnknownCharacterIsVisible()
        {
            // A missing glyph must be obvious on screen, not silently absent.
            Assert.AreNotEqual(0UL, BitmapFont.Rows('¥'));
        }

        [Test]
        public void SpaceIsBlankButAdvances()
        {
            Assert.AreEqual(0UL, BitmapFont.Rows(' '));
            Assert.AreEqual(BitmapFont.Advance, BitmapFont.Width(" "));
        }

        [Test]
        public void WidthIsOneAdvancePerCharacter()
        {
            // Fixed width: proportional spacing at 5px is not worth the layout code.
            Assert.AreEqual(BitmapFont.Advance * 5, BitmapFont.Width("HELLO"));
            Assert.AreEqual(0, BitmapFont.Width(""));
            Assert.AreEqual(0, BitmapFont.Width(null));
        }

        [Test]
        public void RowsMatchesTheBriefsWorkedExampleForA()
        {
            // GlyphsFitTheirCell cannot fail by construction - this is the assertion that
            // would actually catch a transposed or mirrored font. Pinned against the brief's
            // own worked example: row 0 at the top, bit 4 at the left.
            Assert.AreEqual(0b01110_10001_10001_11111_10001_10001_10001UL, BitmapFont.Rows('A'));
        }

        [Test]
        public void GlyphsFitTheirCell()
        {
            // Every glyph must fit in 5x7, or the atlas and the renderer disagree.
            const string set = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,:!?-/'()[]*+%";
            ulong limit = 1UL << (BitmapFont.GlyphWidth * BitmapFont.GlyphHeight);

            foreach (char c in set)
                Assert.Less(BitmapFont.Rows(c), limit, $"'{c}' has bits outside its cell");
        }

        [Test]
        public void TheAtlasIsBuiltOnceAndIsPointFiltered()
        {
            // Point filtering and no mipmaps: CLAUDE.md's rendering rules apply to text too.
            var first = BitmapFont.Atlas();

            Assert.IsNotNull(first);
            Assert.AreEqual(UnityEngine.FilterMode.Point, first.filterMode);
            Assert.AreSame(first, BitmapFont.Atlas(), "the atlas should be cached");
        }
    }
}
