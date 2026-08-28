using KeepyUppy.LevelEditing;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Rect maths for the zone tool. Pure, so the cases that are otherwise only findable by
    /// clicking — a backwards drag, a handle dragged past the far edge — are unit tests.
    /// </summary>
    public sealed class ZoneGeometryTests
    {
        [Test]
        public void Normalise_IsTheSameRectFromEveryDragDirection()
        {
            var expected = new Rect(4f, 2f, 3f, 4f);   // tiles x 4..6, y 2..5

            Assert.AreEqual(expected, ZoneGeometry.Normalise(new Vector2(4.2f, 2.7f), new Vector2(6.9f, 5.1f)));
            Assert.AreEqual(expected, ZoneGeometry.Normalise(new Vector2(6.9f, 5.1f), new Vector2(4.2f, 2.7f)));
            Assert.AreEqual(expected, ZoneGeometry.Normalise(new Vector2(4.2f, 5.1f), new Vector2(6.9f, 2.7f)));
            Assert.AreEqual(expected, ZoneGeometry.Normalise(new Vector2(6.9f, 2.7f), new Vector2(4.2f, 5.1f)));
        }

        [Test]
        public void Normalise_SnapsToWholeTiles()
        {
            Rect rect = ZoneGeometry.Normalise(new Vector2(4.9f, 2.1f), new Vector2(5.1f, 2.9f));

            Assert.AreEqual(4f, rect.xMin, 1e-5f);
            Assert.AreEqual(2f, rect.yMin, 1e-5f);
            Assert.AreEqual(2f, rect.width, 1e-5f);
            Assert.AreEqual(1f, rect.height, 1e-5f);
        }

        [Test]
        public void Normalise_NeverProducesAZeroAreaRect()
        {
            // A click without a drag. A zero-area zone is unselectable and does nothing.
            Rect rect = ZoneGeometry.Normalise(new Vector2(7.4f, 3.4f), new Vector2(7.4f, 3.4f));

            Assert.AreEqual(new Rect(7f, 3f, 1f, 1f), rect);
        }

        [Test]
        public void Normalise_ClampsToTheRoom()
        {
            Rect rect = ZoneGeometry.Normalise(new Vector2(-5f, -5f), new Vector2(99f, 99f));

            Assert.AreEqual(0f, rect.xMin, 1e-5f);
            Assert.AreEqual(0f, rect.yMin, 1e-5f);
            Assert.AreEqual(32f, rect.xMax, 1e-5f);
            Assert.AreEqual(18f, rect.yMax, 1e-5f);
        }

        [Test]
        public void HandleAt_FindsEachCornerAndNothingInTheMiddle()
        {
            var rect = new Rect(10f, 4f, 6f, 5f);   // corners (10,4) (16,4) (10,9) (16,9)

            Assert.AreEqual(ZoneCorner.BottomLeft, ZoneGeometry.HandleAt(rect, new Vector2(10.1f, 4.1f), 0.5f));
            Assert.AreEqual(ZoneCorner.BottomRight, ZoneGeometry.HandleAt(rect, new Vector2(15.9f, 4.1f), 0.5f));
            Assert.AreEqual(ZoneCorner.TopLeft, ZoneGeometry.HandleAt(rect, new Vector2(10.1f, 8.9f), 0.5f));
            Assert.AreEqual(ZoneCorner.TopRight, ZoneGeometry.HandleAt(rect, new Vector2(15.9f, 8.9f), 0.5f));
            Assert.AreEqual(ZoneCorner.None, ZoneGeometry.HandleAt(rect, new Vector2(13f, 6f), 0.5f));
        }

        [Test]
        public void HandleAt_PicksTheNearestWhenTwoCornersAreClose()
        {
            // A 1x1 zone: every corner is within a generous radius, so the nearest must win
            // and must win the same way every time.
            var rect = new Rect(3f, 3f, 1f, 1f);

            Assert.AreEqual(ZoneCorner.BottomLeft, ZoneGeometry.HandleAt(rect, new Vector2(3.1f, 3.1f), 2f));
            Assert.AreEqual(ZoneCorner.TopRight, ZoneGeometry.HandleAt(rect, new Vector2(3.9f, 3.9f), 2f));
        }

        [Test]
        public void Move_KeepsTheSizeAndSnapsToTiles()
        {
            var rect = new Rect(10f, 4f, 6f, 5f);

            Rect moved = ZoneGeometry.Move(rect, new Vector2(3.4f, 7.6f));

            Assert.AreEqual(new Rect(3f, 8f, 6f, 5f), moved);
        }

        [Test]
        public void Move_ClampsSoTheWholeRectStaysInTheRoom()
        {
            var rect = new Rect(10f, 4f, 6f, 5f);

            Assert.AreEqual(new Rect(26f, 13f, 6f, 5f), ZoneGeometry.Move(rect, new Vector2(99f, 99f)));
            Assert.AreEqual(new Rect(0f, 0f, 6f, 5f), ZoneGeometry.Move(rect, new Vector2(-99f, -99f)));
        }

        [Test]
        public void RectRoundTripsThroughTheSchemaArray()
        {
            var rect = new Rect(20f, 1f, 6f, 10f);

            float[] array = ZoneGeometry.ToArray(rect);

            Assert.AreEqual(new[] { 20f, 1f, 6f, 10f }, array);
            Assert.AreEqual(rect, ZoneGeometry.RectOf(array));
        }

        [Test]
        public void RectOf_SurvivesAMalformedArray()
        {
            // Hand-edited JSON is a supported authoring path, so a short array must not throw.
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), ZoneGeometry.RectOf(null));
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), ZoneGeometry.RectOf(new[] { 1f, 2f }));
        }

        [Test]
        public void AnchorOf_IsTheTileDiagonallyOppositeAndDoesNotMoveWithTheRect()
        {
            var rect = new Rect(10f, 4f, 6f, 5f);   // tiles x 10..15, y 4..8

            Assert.AreEqual(new Vector2Int(15, 8), ZoneGeometry.AnchorOf(rect, ZoneCorner.BottomLeft));
            Assert.AreEqual(new Vector2Int(10, 8), ZoneGeometry.AnchorOf(rect, ZoneCorner.BottomRight));
            Assert.AreEqual(new Vector2Int(15, 4), ZoneGeometry.AnchorOf(rect, ZoneCorner.TopLeft));
            Assert.AreEqual(new Vector2Int(10, 4), ZoneGeometry.AnchorOf(rect, ZoneCorner.TopRight));
        }

        [Test]
        public void ResizingPastTheAnchorAndOnwardKeepsGrowingFromTheAnchor()
        {
            // The bug this pins: deriving the anchor from the already-mutated rect each frame
            // re-anchors it to the corner under the cursor, and the rect collapses into a band
            // trailing the drag instead of growing from the corner that was pinned.
            var rect = new Rect(10f, 4f, 6f, 5f);
            Vector2Int anchor = ZoneGeometry.AnchorOf(rect, ZoneCorner.BottomLeft);

            Rect afterFlip = ZoneGeometry.Normalise(anchor, new Vector2Int(18, 10));
            Rect afterMore = ZoneGeometry.Normalise(anchor, new Vector2Int(19, 11));

            Assert.AreEqual(15f, afterFlip.xMin, 1e-5f);
            Assert.AreEqual(19f, afterFlip.xMax, 1e-5f);

            Assert.AreEqual(15f, afterMore.xMin, 1e-5f, "the anchor must not follow the cursor");
            Assert.AreEqual(20f, afterMore.xMax, 1e-5f);
        }

        [Test]
        public void Snap_ForcesTypedValuesOntoTheGridAndIntoTheRoom()
        {
            Assert.AreEqual(new Rect(2f, 3f, 4f, 5f), ZoneGeometry.Snap(new Rect(2.4f, 2.6f, 4.1f, 4.8f)));

            // Off the right-hand wall: the whole rect is pulled back inside.
            Assert.AreEqual(new Rect(26f, 13f, 6f, 5f), ZoneGeometry.Snap(new Rect(100f, 100f, 6f, 5f)));

            // Degenerate input still yields a usable zone.
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), ZoneGeometry.Snap(new Rect(-5f, -5f, 0f, 0f)));
        }

        [Test]
        public void Snap_LeavesAnAlreadyValidRectAlone()
        {
            var rect = new Rect(20f, 1f, 6f, 10f);
            Assert.AreEqual(rect, ZoneGeometry.Snap(rect));
        }
    }
}
