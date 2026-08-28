using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>Which corner of a zone rect a drag has hold of.</summary>
    public enum ZoneCorner
    {
        None = 0,
        BottomLeft = 1,
        BottomRight = 2,
        TopLeft = 3,
        TopRight = 4,
    }

    /// <summary>
    /// Rect maths for the zone tool (level-editor-spec 3: zone rects snap to tile).
    ///
    /// Pure statics, no camera and no scene, for the same reason GridCursor is: a backwards
    /// drag and a handle dragged past the far edge are otherwise only findable by clicking.
    ///
    /// Everything routes through <see cref="GridCursor.TileRect"/>, which already normalises
    /// a drag in any direction and clamps it to the room. Because it is inclusive it can
    /// never return a zero-area rect, so the 1x1 minimum falls out rather than being
    /// enforced separately.
    /// </summary>
    public static class ZoneGeometry
    {
        /// <summary>Inclusive tile rect between two tiles, as a world-space Rect.</summary>
        public static Rect Normalise(Vector2Int a, Vector2Int b)
        {
            RectInt tiles = GridCursor.TileRect(a, b);
            return new Rect(tiles.x, tiles.y, tiles.width, tiles.height);
        }

        public static Rect Normalise(Vector2 a, Vector2 b)
            => Normalise(GridCursor.ToTile(a), GridCursor.ToTile(b));

        /// <summary>
        /// The corner handle under a world position, or None. Nearest wins, so a 1x1 zone
        /// whose corners all fall inside the radius still resolves to one of them — and to
        /// the same one every time, since the comparison is a strict improvement over a fixed
        /// iteration order.
        /// </summary>
        public static ZoneCorner HandleAt(Rect rect, Vector2 world, float radius)
        {
            ZoneCorner best = ZoneCorner.None;
            float bestSqr = radius * radius;

            best = Closer(best, ref bestSqr, world, new Vector2(rect.xMin, rect.yMin), ZoneCorner.BottomLeft);
            best = Closer(best, ref bestSqr, world, new Vector2(rect.xMax, rect.yMin), ZoneCorner.BottomRight);
            best = Closer(best, ref bestSqr, world, new Vector2(rect.xMin, rect.yMax), ZoneCorner.TopLeft);
            best = Closer(best, ref bestSqr, world, new Vector2(rect.xMax, rect.yMax), ZoneCorner.TopRight);

            return best;
        }

        static ZoneCorner Closer(ZoneCorner best, ref float bestSqr, Vector2 world,
                                 Vector2 corner, ZoneCorner candidate)
        {
            float dx = corner.x - world.x;
            float dy = corner.y - world.y;
            float sqr = dx * dx + dy * dy;

            if (sqr > bestSqr) return best;

            bestSqr = sqr;
            return candidate;
        }

        /// <summary>
        /// The tile diagonally opposite a corner — the one a resize pivots around. Tile
        /// coordinates, not rect edges: a rect of x 10 width 6 covers tiles 10..15, so its far
        /// tile is 15 and not 16.
        ///
        /// Public because a resize drag must capture this ONCE at mouse-down: recomputing it
        /// from the live rect each frame moves the anchor as soon as the drag crosses it.
        /// </summary>
        public static Vector2Int AnchorOf(Rect rect, ZoneCorner corner)
        {
            int minX = Mathf.RoundToInt(rect.xMin);
            int minY = Mathf.RoundToInt(rect.yMin);
            int maxX = Mathf.RoundToInt(rect.xMax) - 1;
            int maxY = Mathf.RoundToInt(rect.yMax) - 1;

            switch (corner)
            {
                case ZoneCorner.BottomLeft:  return new Vector2Int(maxX, maxY);
                case ZoneCorner.BottomRight: return new Vector2Int(minX, maxY);
                case ZoneCorner.TopLeft:     return new Vector2Int(maxX, minY);
                default:                     return new Vector2Int(minX, minY);
            }
        }

        /// <summary>
        /// Moves a rect to a new bottom-left, keeping its size and holding the whole rect
        /// inside the room — a zone half outside the wall is not something the author can
        /// mean.
        /// </summary>
        public static Rect Move(Rect rect, Vector2 bottomLeft)
        {
            int w = Mathf.RoundToInt(rect.width);
            int h = Mathf.RoundToInt(rect.height);

            int x = Mathf.Clamp(Mathf.RoundToInt(bottomLeft.x), 0, TileGrid.Width - w);
            int y = Mathf.Clamp(Mathf.RoundToInt(bottomLeft.y), 0, TileGrid.Height - h);

            return new Rect(x, y, w, h);
        }

        /// <summary>
        /// Forces a rect onto the tile grid and inside the room, for values arriving from
        /// somewhere other than a drag — typed inspector fields, or hand-edited JSON.
        ///
        /// The drag paths get this for free from GridCursor.TileRect; typed input has to be
        /// held to the same invariant or the two halves of the tool disagree about what a
        /// zone is.
        /// </summary>
        public static Rect Snap(Rect rect)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(rect.width), 1, TileGrid.Width);
            int h = Mathf.Clamp(Mathf.RoundToInt(rect.height), 1, TileGrid.Height);
            int x = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, TileGrid.Width - w);
            int y = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, TileGrid.Height - h);

            return new Rect(x, y, w, h);
        }

        /// <summary>The schema's [x, y, w, h]. Malformed input reads as a 1x1 at the origin.</summary>
        public static Rect RectOf(float[] rect)
            => rect == null || rect.Length < 4
                ? new Rect(0f, 0f, 1f, 1f)
                : new Rect(rect[0], rect[1], rect[2], rect[3]);

        public static float[] ToArray(Rect rect)
            => new[] { rect.xMin, rect.yMin, rect.width, rect.height };
    }
}
