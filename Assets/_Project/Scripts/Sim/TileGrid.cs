using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// Static geometry chars from the level schema (level-editor-spec 3).
    /// Only Empty and Solid participate in collision this far; the rest parse and sit inert
    /// until hazards exist.
    /// </summary>
    public enum TileKind : byte
    {
        Empty = 0,        // '.'
        Solid = 1,        // '#'
        OneWay = 2,       // '='
        Rough = 3,        // '~'  pops a Full or Critical balloon
        SpikeUp = 4,      // '^'
        SpikeDown = 5,    // 'v'
        SpikeLeft = 6,    // '<'
        SpikeRight = 7,   // '>'
        PlayerLethal = 8, // 'x'
    }

    /// <summary>
    /// The room's static geometry. 32 x 18 tiles, 1 tile = 1 metre (level-editor-spec 3).
    /// Tile (x, y) occupies the world box [x, x+1] x [y, y+1], y up.
    /// </summary>
    public sealed class TileGrid
    {
        public const int Width = 32;
        public const int Height = 18;

        readonly TileKind[] _cells = new TileKind[Width * Height];

        /// <summary>
        /// Cells currently hidden by a panel (tile-panels design). Parallel to _cells, which
        /// is never written by a panel: the level's geometry stays exactly as it loaded, so
        /// the worst a panel bug can do is show the wrong tiles for one run.
        /// </summary>
        readonly bool[] _masked = new bool[Width * Height];

        /// <summary>
        /// Incremented on every change. Rendering watches this to know when to rebuild,
        /// which is what lets the editor paint tiles and see them immediately.
        /// </summary>
        public int Version { get; private set; }

        public TileKind this[int x, int y]
        {
            get
            {
                if (!InBounds(x, y))
                {
                    // The closing shell stays on three sides — sideways and above, a runaway
                    // body still cannot fly off, which is the safety net GDD 4.1 relies on.
                    // Below the room it opens: a level with no floor tile of its own should
                    // let a body fall through and out (Mario-style pit death) rather than land
                    // on an implicit floor no one placed. SweptCircle needs no change for this
                    // — it only ever reads this indexer.
                    return y < 0 ? TileKind.Empty : TileKind.Solid;
                }

                int i = y * Width + x;
                return _masked[i] ? TileKind.Empty : _cells[i];
            }
            set
            {
                if (!InBounds(x, y)) return;

                int i = y * Width + x;
                if (_cells[i] == value) return;

                _cells[i] = value;
                Version++;
            }
        }

        public static bool InBounds(int x, int y)
            => x >= 0 && x < Width && y >= 0 && y < Height;

        /// <summary>
        /// Out-of-bounds reads as solid. The room is always walled in anyway, and this
        /// guarantees a runaway body can never leave the world even if a level forgets
        /// its border.
        /// </summary>
        public bool IsSolid(int x, int y) => this[x, y] == TileKind.Solid;

        /// <summary>
        /// Blocking is per-entity, and it has to be: a tile cannot both stop a body and pop
        /// it. Collision would hold the body outside the tile, and the kill volume — which
        /// GDD 8 requires to be *smaller* than the visual — would never be reached.
        ///
        /// So a hazard is transparent to whatever it kills, and solid to whatever it does not.
        /// The balloon flies into a spike and pops; the player lands on the same spike, is
        /// held up by it, and dies to the kill volume bleeding through its surface.
        /// </summary>
        public bool IsBlockingBalloon(int x, int y) => BlocksBalloon(this[x, y]);

        public bool IsBlockingPlayer(int x, int y) => BlocksPlayer(this[x, y]);

        /// <summary>Only genuine geometry stops the balloon. Everything lethal lets it in.</summary>
        public static bool BlocksBalloon(TileKind kind) => kind == TileKind.Solid;

        /// <summary>
        /// Everything except empty space and one-ways stops the player, including spikes —
        /// you land on a bed of spikes, you do not fall through it.
        /// </summary>
        public static bool BlocksPlayer(TileKind kind)
            => kind != TileKind.Empty && kind != TileKind.OneWay;

        public static bool IsSpike(TileKind kind)
            => kind == TileKind.SpikeUp || kind == TileKind.SpikeDown
            || kind == TileKind.SpikeLeft || kind == TileKind.SpikeRight;

        public void Clear()
        {
            ClearMask();
            for (int i = 0; i < _cells.Length; i++) _cells[i] = TileKind.Empty;
            Version++;
        }

        public void Fill(int x, int y, int w, int h, TileKind kind)
        {
            for (int ty = y; ty < y + h; ty++)
            for (int tx = x; tx < x + w; tx++)
                this[tx, ty] = kind;
        }

        public bool IsMasked(int x, int y) => InBounds(x, y) && _masked[y * Width + x];

        /// <summary>
        /// The tile a cell holds regardless of any mask. For the debris view, which draws the
        /// tiles a panel has just hidden — the indexer reports those as Empty by design.
        /// Out-of-range mirrors the indexer exactly: Empty below the room, Solid elsewhere.
        /// </summary>
        public TileKind BaseAt(int x, int y)
        {
            if (InBounds(x, y)) return _cells[y * Width + x];
            return y < 0 ? TileKind.Empty : TileKind.Solid;
        }

        /// <summary>
        /// Hides or reveals a rectangle of cells. Version moves only when something actually
        /// changed, so a panel re-applying its own state does not churn the tile views.
        /// </summary>
        public void SetMasked(RectInt rect, bool masked)
        {
            bool changed = false;

            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                if (!InBounds(x, y)) continue;

                int i = y * Width + x;
                if (_masked[i] == masked) continue;

                _masked[i] = masked;
                changed = true;
            }

            if (changed) Version++;
        }

        public void ClearMask()
        {
            bool changed = false;

            for (int i = 0; i < _masked.Length; i++)
            {
                if (!_masked[i]) continue;

                _masked[i] = false;
                changed = true;
            }

            if (changed) Version++;
        }

        /// <summary>Solid border one tile thick around the whole room.</summary>
        public void FillBorder(TileKind kind = TileKind.Solid)
        {
            Fill(0, 0, Width, 1, kind);
            Fill(0, Height - 1, Width, 1, kind);
            Fill(0, 0, 1, Height, kind);
            Fill(Width - 1, 0, 1, Height, kind);
        }

        public static TileKind FromChar(char c)
        {
            switch (c)
            {
                case '#': return TileKind.Solid;
                case '=': return TileKind.OneWay;
                case '~': return TileKind.Rough;
                case '^': return TileKind.SpikeUp;
                case 'v': return TileKind.SpikeDown;
                case '<': return TileKind.SpikeLeft;
                case '>': return TileKind.SpikeRight;
                case 'x': return TileKind.PlayerLethal;
                default:  return TileKind.Empty;
            }
        }

        public static char ToChar(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Solid:        return '#';
                case TileKind.OneWay:       return '=';
                case TileKind.Rough:        return '~';
                case TileKind.SpikeUp:      return '^';
                case TileKind.SpikeDown:    return 'v';
                case TileKind.SpikeLeft:    return '<';
                case TileKind.SpikeRight:   return '>';
                case TileKind.PlayerLethal: return 'x';
                default:                    return '.';
            }
        }

        /// <summary>
        /// The grid as level-JSON rows, row 0 being the top of the room. Inverse of
        /// <see cref="LoadRows"/>.
        ///
        /// Reads <see cref="BaseAt"/>, never the masked indexer: this is the authored level,
        /// not whatever a panel happens to be hiding this frame. A panel hides tiles, it does
        /// not delete them, and the editor shares the world's live grid — sharing that lets
        /// live editing work, but it also means whatever ToRows serialises here can be flushed
        /// to LevelData mid-edit. Serialising the masked view would bake a currently-hidden
        /// panel's hole into the level permanently.
        /// </summary>
        public string[] ToRows()
        {
            var rows = new string[Height];
            var chars = new char[Width];

            for (int r = 0; r < Height; r++)
            {
                int y = Height - 1 - r;
                for (int x = 0; x < Width; x++) chars[x] = ToChar(BaseAt(x, y));
                rows[r] = new string(chars);
            }

            return rows;
        }

        /// <summary>
        /// Rows as written in the level JSON: row 0 is the top of the room, so it maps to
        /// grid y = Height - 1.
        /// </summary>
        public void LoadRows(string[] rows)
        {
            ClearMask();
            Clear();
            int count = rows.Length < Height ? rows.Length : Height;
            for (int r = 0; r < count; r++)
            {
                string row = rows[r];
                int y = Height - 1 - r;
                int cols = row.Length < Width ? row.Length : Width;
                for (int x = 0; x < cols; x++) this[x, y] = FromChar(row[x]);
            }
        }
    }
}
