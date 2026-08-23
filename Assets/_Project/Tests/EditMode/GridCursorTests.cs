using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Cursor mapping and the snapping rules from level-editor-spec 3. Cheap to test here,
    /// and otherwise only findable by clicking around and squinting.
    /// </summary>
    public sealed class GridCursorTests
    {
        [Test]
        public void ToTile_FloorsTowardTheBottomLeft()
        {
            Assert.AreEqual(new Vector2Int(4, 9), GridCursor.ToTile(new Vector2(4.9f, 9.01f)));
            Assert.AreEqual(new Vector2Int(0, 0), GridCursor.ToTile(new Vector2(0.0f, 0.0f)));
            Assert.AreEqual(new Vector2Int(-1, -1), GridCursor.ToTile(new Vector2(-0.2f, -0.2f)),
                            "outside the room is still a coordinate, just an invalid one");
        }

        [Test]
        public void InBounds_MatchesTheRoom()
        {
            Assert.IsTrue(GridCursor.InBounds(new Vector2Int(0, 0)));
            Assert.IsTrue(GridCursor.InBounds(new Vector2Int(TileGrid.Width - 1, TileGrid.Height - 1)));
            Assert.IsFalse(GridCursor.InBounds(new Vector2Int(TileGrid.Width, 0)));
            Assert.IsFalse(GridCursor.InBounds(new Vector2Int(-1, 0)));
        }

        [Test]
        public void SnapHalfTile_RoundsToTheNearestHalf()
        {
            Assert.AreEqual(new Vector2(6.5f, 9f), GridCursor.SnapHalfTile(new Vector2(6.3f, 9.1f)));
            Assert.AreEqual(new Vector2(6.5f, 9.5f), GridCursor.SnapHalfTile(new Vector2(6.7f, 9.4f)));
            Assert.AreEqual(new Vector2(7f, 9f), GridCursor.SnapHalfTile(new Vector2(6.9f, 8.9f)));
        }

        [Test]
        public void TileRect_IsInclusiveAndDirectionAgnostic()
        {
            RectInt forward = GridCursor.TileRect(new Vector2Int(4, 4), new Vector2Int(7, 6));
            RectInt backward = GridCursor.TileRect(new Vector2Int(7, 6), new Vector2Int(4, 4));

            Assert.AreEqual(forward, backward);
            Assert.AreEqual(4, forward.width, "4..7 inclusive is four tiles");
            Assert.AreEqual(3, forward.height);
        }

        [Test]
        public void TileRect_ClampsToTheRoom()
        {
            RectInt rect = GridCursor.TileRect(new Vector2Int(-10, -10),
                                               new Vector2Int(TileGrid.Width + 10, TileGrid.Height + 10));

            Assert.AreEqual(0, rect.xMin);
            Assert.AreEqual(0, rect.yMin);
            Assert.AreEqual(TileGrid.Width, rect.width);
            Assert.AreEqual(TileGrid.Height, rect.height);
        }
    }
}
