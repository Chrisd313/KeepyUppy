using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// World position to grid coordinates, and the snapping rules from level-editor-spec 3:
    /// static geometry snaps to the tile grid, entities snap to half-tile, zone rects snap to
    /// tile, path waypoints are free.
    ///
    /// Pure maths, no camera and no scene, so the mapping is testable — including the edge
    /// cases that are otherwise only findable by clicking.
    /// </summary>
    public static class GridCursor
    {
        /// <summary>Tile containing a world position. May be outside the room.</summary>
        public static Vector2Int ToTile(Vector2 world)
            => new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        public static bool InBounds(Vector2Int tile)
            => TileGrid.InBounds(tile.x, tile.y);

        public static Vector2Int Clamp(Vector2Int tile)
            => new Vector2Int(Mathf.Clamp(tile.x, 0, TileGrid.Width - 1),
                              Mathf.Clamp(tile.y, 0, TileGrid.Height - 1));

        /// <summary>Entity positions snap to half-tile.</summary>
        public static Vector2 SnapHalfTile(Vector2 world)
            => new Vector2(Mathf.Round(world.x * 2f) * 0.5f, Mathf.Round(world.y * 2f) * 0.5f);

        /// <summary>
        /// Normalised inclusive tile rectangle between two corners, clamped to the room.
        /// Handles drags in any direction — dragging up-left must fill the same rect as
        /// dragging down-right.
        /// </summary>
        public static RectInt TileRect(Vector2Int a, Vector2Int b)
        {
            Vector2Int min = Clamp(new Vector2Int(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y)));
            Vector2Int max = Clamp(new Vector2Int(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));

            return new RectInt(min.x, min.y, max.x - min.x + 1, max.y - min.y + 1);
        }
    }
}
