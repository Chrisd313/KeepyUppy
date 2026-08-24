using System.Collections.Generic;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// The 5x7 pixel font the menus and the HUD draw with.
    ///
    /// Built in code rather than imported: 640x360 with integer scaling and point filtering
    /// (CLAUDE.md) has no room for a font asset pipeline, and this project draws everything
    /// else with its own quads too. <see cref="Runtime.TextView"/> turns strings into quads
    /// with this table, reading straight from <see cref="Rows"/> - there is no atlas texture,
    /// this class only owns the glyph shapes.
    ///
    /// Encoding: one glyph is 7 rows of 5 bits packed into a ulong, row 0 at the top,
    /// bit 4 (the highest bit of each row) at the left. Rows are written below as literal
    /// "01110"-style strings so a glyph can be corrected by eye without decoding hex.
    /// </summary>
    public static class BitmapFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        /// <summary>Horizontal pixels from the start of one glyph cell to the next. Fixed-width.</summary>
        public const int Advance = 6;

        /// <summary>Unknown characters render as this — a filled cell, impossible to miss.</summary>
        private const ulong MissingGlyph = (1UL << (GlyphWidth * GlyphHeight)) - 1;

        private static readonly Dictionary<char, ulong> Glyphs = new Dictionary<char, ulong>();

        // One entry per character. Each string is GlyphHeight rows of GlyphWidth chars,
        // '1' = pixel on, '0' = pixel off, row 0 first (top). Fix a letterform here.
        private static readonly (char Ch, string[] Rows)[] Source =
        {
            ('A', new[] {
                "01110",
                "10001",
                "10001",
                "11111",
                "10001",
                "10001",
                "10001",
            }),
            ('B', new[] {
                "11110",
                "10001",
                "10001",
                "11110",
                "10001",
                "10001",
                "11110",
            }),
            ('C', new[] {
                "01111",
                "10000",
                "10000",
                "10000",
                "10000",
                "10000",
                "01111",
            }),
            ('D', new[] {
                "11100",
                "10010",
                "10001",
                "10001",
                "10001",
                "10010",
                "11100",
            }),
            ('E', new[] {
                "11111",
                "10000",
                "10000",
                "11110",
                "10000",
                "10000",
                "11111",
            }),
            ('F', new[] {
                "11111",
                "10000",
                "10000",
                "11110",
                "10000",
                "10000",
                "10000",
            }),
            ('G', new[] {
                "01111",
                "10000",
                "10000",
                "10011",
                "10001",
                "10001",
                "01111",
            }),
            ('H', new[] {
                "10001",
                "10001",
                "10001",
                "11111",
                "10001",
                "10001",
                "10001",
            }),
            ('I', new[] {
                "11111",
                "00100",
                "00100",
                "00100",
                "00100",
                "00100",
                "11111",
            }),
            ('J', new[] {
                "00111",
                "00010",
                "00010",
                "00010",
                "00010",
                "10010",
                "01100",
            }),
            ('K', new[] {
                "10001",
                "10010",
                "10100",
                "11000",
                "10100",
                "10010",
                "10001",
            }),
            ('L', new[] {
                "10000",
                "10000",
                "10000",
                "10000",
                "10000",
                "10000",
                "11111",
            }),
            ('M', new[] {
                "10001",
                "11011",
                "10101",
                "10101",
                "10001",
                "10001",
                "10001",
            }),
            ('N', new[] {
                "10001",
                "11001",
                "10101",
                "10101",
                "10011",
                "10001",
                "10001",
            }),
            ('O', new[] {
                "01110",
                "10001",
                "10001",
                "10001",
                "10001",
                "10001",
                "01110",
            }),
            ('P', new[] {
                "11110",
                "10001",
                "10001",
                "11110",
                "10000",
                "10000",
                "10000",
            }),
            ('Q', new[] {
                "01110",
                "10001",
                "10001",
                "10001",
                "10101",
                "10010",
                "01101",
            }),
            ('R', new[] {
                "11110",
                "10001",
                "10001",
                "11110",
                "10100",
                "10010",
                "10001",
            }),
            ('S', new[] {
                "01111",
                "10000",
                "10000",
                "01110",
                "00001",
                "00001",
                "11110",
            }),
            ('T', new[] {
                "11111",
                "00100",
                "00100",
                "00100",
                "00100",
                "00100",
                "00100",
            }),
            ('U', new[] {
                "10001",
                "10001",
                "10001",
                "10001",
                "10001",
                "10001",
                "01110",
            }),
            ('V', new[] {
                "10001",
                "10001",
                "10001",
                "10001",
                "10001",
                "01010",
                "00100",
            }),
            ('W', new[] {
                "10001",
                "10001",
                "10001",
                "10101",
                "10101",
                "10101",
                "01010",
            }),
            ('X', new[] {
                "10001",
                "10001",
                "01010",
                "00100",
                "01010",
                "10001",
                "10001",
            }),
            ('Y', new[] {
                "10001",
                "10001",
                "01010",
                "00100",
                "00100",
                "00100",
                "00100",
            }),
            ('Z', new[] {
                "11111",
                "00001",
                "00010",
                "00100",
                "01000",
                "10000",
                "11111",
            }),
            ('0', new[] {
                "01110",
                "10011",
                "10101",
                "10101",
                "10101",
                "11001",
                "01110",
            }),
            ('1', new[] {
                "00100",
                "01100",
                "00100",
                "00100",
                "00100",
                "00100",
                "01110",
            }),
            ('2', new[] {
                "01110",
                "10001",
                "00001",
                "00010",
                "00100",
                "01000",
                "11111",
            }),
            ('3', new[] {
                "11110",
                "00001",
                "00001",
                "01110",
                "00001",
                "00001",
                "11110",
            }),
            ('4', new[] {
                "00010",
                "00110",
                "01010",
                "10010",
                "11111",
                "00010",
                "00010",
            }),
            ('5', new[] {
                "11111",
                "10000",
                "11110",
                "00001",
                "00001",
                "10001",
                "01110",
            }),
            ('6', new[] {
                "00111",
                "01000",
                "10000",
                "11110",
                "10001",
                "10001",
                "01110",
            }),
            ('7', new[] {
                "11111",
                "00001",
                "00010",
                "00100",
                "01000",
                "01000",
                "01000",
            }),
            ('8', new[] {
                "01110",
                "10001",
                "10001",
                "01110",
                "10001",
                "10001",
                "01110",
            }),
            ('9', new[] {
                "01110",
                "10001",
                "10001",
                "01111",
                "00001",
                "00010",
                "11100",
            }),
            (' ', new[] {
                "00000",
                "00000",
                "00000",
                "00000",
                "00000",
                "00000",
                "00000",
            }),
            ('.', new[] {
                "00000",
                "00000",
                "00000",
                "00000",
                "00000",
                "01100",
                "01100",
            }),
            (',', new[] {
                "00000",
                "00000",
                "00000",
                "00000",
                "00000",
                "01100",
                "01000",
            }),
            (':', new[] {
                "00000",
                "01100",
                "01100",
                "00000",
                "01100",
                "01100",
                "00000",
            }),
            ('!', new[] {
                "00100",
                "00100",
                "00100",
                "00100",
                "00100",
                "00000",
                "00100",
            }),
            ('?', new[] {
                "01110",
                "10001",
                "00001",
                "00010",
                "00100",
                "00000",
                "00100",
            }),
            ('-', new[] {
                "00000",
                "00000",
                "00000",
                "11111",
                "00000",
                "00000",
                "00000",
            }),
            ('/', new[] {
                "00001",
                "00010",
                "00010",
                "00100",
                "01000",
                "01000",
                "10000",
            }),
            ('\'', new[] {
                "01100",
                "01100",
                "00100",
                "01000",
                "00000",
                "00000",
                "00000",
            }),
            ('(', new[] {
                "00010",
                "00100",
                "01000",
                "01000",
                "01000",
                "00100",
                "00010",
            }),
            (')', new[] {
                "01000",
                "00100",
                "00010",
                "00010",
                "00010",
                "00100",
                "01000",
            }),
            ('[', new[] {
                "01110",
                "01000",
                "01000",
                "01000",
                "01000",
                "01000",
                "01110",
            }),
            (']', new[] {
                "01110",
                "00010",
                "00010",
                "00010",
                "00010",
                "00010",
                "01110",
            }),
            ('*', new[] {
                "00000",
                "10101",
                "01110",
                "11111",
                "01110",
                "10101",
                "00000",
            }),
            ('+', new[] {
                "00000",
                "00100",
                "00100",
                "11111",
                "00100",
                "00100",
                "00000",
            }),
            ('%', new[] {
                "11001",
                "11010",
                "00010",
                "00100",
                "01000",
                "01011",
                "10011",
            }),
            ('_', new[] {
                "00000",
                "00000",
                "00000",
                "00000",
                "00000",
                "00000",
                "11111",
            }),
            ('<', new[] {
                "00001",
                "00010",
                "00100",
                "01000",
                "00100",
                "00010",
                "00001",
            }),
            ('>', new[] {
                "10000",
                "01000",
                "00100",
                "00010",
                "00100",
                "01000",
                "10000",
            }),
        };

        static BitmapFont()
        {
            for (int i = 0; i < Source.Length; i++)
            {
                var (ch, rows) = Source[i];
                Glyphs[ch] = Pack(rows);
            }
        }

        /// <summary>
        /// The bit position of one glyph pixel within its packed ulong: row 0 (top) occupies
        /// the highest bits, row GlyphHeight-1 (bottom) the lowest; within a row, column 0
        /// (left) is the row's high bit. Public so <see cref="Runtime.TextView"/> can read a
        /// glyph's bits out the same way <see cref="Pack"/> wrote them in - there is only one
        /// place to get this right.
        /// </summary>
        public static int BitIndex(int row, int col)
        {
            return (GlyphHeight - 1 - row) * GlyphWidth + (GlyphWidth - 1 - col);
        }

        private static ulong Pack(string[] rows)
        {
            ulong bits = 0;

            for (int row = 0; row < GlyphHeight; row++)
            {
                string line = rows[row];

                for (int col = 0; col < GlyphWidth; col++)
                {
                    if (line[col] == '1')
                        bits |= 1UL << BitIndex(row, col);
                }
            }

            return bits;
        }

        /// <summary>Uppercases and normalises a character to whatever the table actually stores.</summary>
        private static char Normalize(char c)
        {
            return char.ToUpperInvariant(c);
        }

        /// <summary>True if this character (after uppercasing) has a real glyph.</summary>
        public static bool Has(char c) => Glyphs.ContainsKey(Normalize(c));

        /// <summary>
        /// The packed bitmap for a character. Lowercase maps to uppercase. A character not in
        /// the table returns a filled box, so a missing glyph is visible rather than invisible.
        /// </summary>
        public static ulong Rows(char c)
        {
            char normalized = Normalize(c);
            return Glyphs.TryGetValue(normalized, out ulong bits) ? bits : MissingGlyph;
        }

        /// <summary>Pixel width of a string rendered at one Advance per character.</summary>
        public static int Width(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : text.Length * Advance;
        }

    }
}
