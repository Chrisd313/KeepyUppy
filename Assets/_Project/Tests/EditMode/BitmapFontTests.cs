using System.Collections.Generic;
using System.IO;
using KeepyUppy.Runtime;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

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
            const string set = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .,:!?-/'()[]*+%_<>";

            foreach (char c in set)
                Assert.IsTrue(BitmapFont.Has(c), $"no glyph for '{c}'");
        }

        [Test]
        public void EveryCharacterShellViewActuallyDrawsHasAGlyph()
        {
            // EveryCharacterInTheSetHasAGlyph above pins a hardcoded charset nobody checked
            // against real usage - which is exactly how '_', '<' and '>' went missing while
            // ShellView drew "w1_02" and "< WORLD >" as solid missing-glyph blocks. This test
            // crosses the font/screen boundary that one does not: every literal ShellView
            // draws, plus every level id in the shipped manifest (level ids are not
            // uppercased before drawing - see ShellView.DrawLevelSelect).
            string[] literals =
            {
                "KEEPY UPPY", "PRESS SPACE",
                "NO LEVELS",
                "> ", "  ", "< WORLD >",
                "PAUSED", "SPACE RESUME", "ESC RESUME",
                "ROOM CLEARED", " TOUCHES",
            };

            var missing = new List<char>();

            foreach (string literal in literals)
                foreach (char c in literal)
                    if (!BitmapFont.Has(c) && !missing.Contains(c))
                        missing.Add(c);

            string indexPath = Path.Combine(Application.dataPath, "StreamingAssets", "levels", "index.json");
            Assert.IsTrue(File.Exists(indexPath), $"expected a manifest at {indexPath}");

            LevelIndex index = JsonUtility.FromJson<LevelIndex>(File.ReadAllText(indexPath));
            foreach (string id in index.AllLevels())
                foreach (char c in id)
                    if (!BitmapFont.Has(c) && !missing.Contains(c))
                        missing.Add(c);

            CollectionAssert.IsEmpty(missing, "no glyph for: " + string.Join(", ", missing));
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
            // Pins the row order against the brief's own worked example: row 0 at the top,
            // bit 4 at the left. Every row of 'A' is a palindrome, though, so this alone would
            // not catch a horizontal mirror - see RowsCatchesAHorizontalMirrorUsingF below.
            Assert.AreEqual(0b01110_10001_10001_11111_10001_10001_10001UL, BitmapFont.Rows('A'));
        }

        [Test]
        public void RowsCatchesAHorizontalMirrorUsingF()
        {
            // 'A's rows are all palindromes, so reversing bit 4 and bit 0 columnwise would
            // leave BitmapFont.Rows('A') unchanged and a mirrored font would still pass the
            // test above. 'F' is not row-palindromic ("10000" != "00001"), so this value would
            // change under a horizontal mirror and actually exercises the column direction.
            Assert.AreEqual(0b11111_10000_10000_11110_10000_10000_10000UL, BitmapFont.Rows('F'));
        }

    }
}
