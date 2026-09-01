using KeepyUppy.Sim;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The mask layer (tile-panels design). The loaded rows stay immutable; a mask hides
    /// cells, and every collision query in the sim reads them through this one indexer.
    /// </summary>
    public sealed class TileGridMaskTests
    {
        static TileGrid Room()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 4, 6, 1, TileKind.Solid);
            return grid;
        }

        [Test]
        public void AMaskedCell_ReadsEmpty_AndStopsBlocking()
        {
            TileGrid grid = Room();
            Assert.IsTrue(grid.IsSolid(12, 4));
            Assert.IsTrue(grid.IsBlockingBalloon(12, 4));

            grid.SetMasked(new RectInt(10, 4, 6, 1), true);

            Assert.AreEqual(TileKind.Empty, grid[12, 4]);
            Assert.IsFalse(grid.IsSolid(12, 4), "collision must see the floor as gone");
            Assert.IsFalse(grid.IsBlockingBalloon(12, 4));
            Assert.IsFalse(grid.IsBlockingPlayer(12, 4));
        }

        [Test]
        public void ClearMask_BringsTheTilesBack()
        {
            TileGrid grid = Room();
            grid.SetMasked(new RectInt(10, 4, 6, 1), true);
            grid.ClearMask();

            Assert.IsTrue(grid.IsSolid(12, 4), "the rows were never edited, so nothing was lost");
        }

        [Test]
        public void Masking_BumpsVersion_SoTheViewRedraws()
        {
            TileGrid grid = Room();
            int before = grid.Version;

            grid.SetMasked(new RectInt(10, 4, 6, 1), true);
            Assert.Greater(grid.Version, before);

            int after = grid.Version;
            grid.SetMasked(new RectInt(10, 4, 6, 1), true);
            Assert.AreEqual(after, grid.Version, "masking what is already masked changes nothing");
        }

        [Test]
        public void PaintingUnderAMask_EditsTheCell_WithoutRevealingIt()
        {
            TileGrid grid = Room();
            grid.SetMasked(new RectInt(10, 4, 6, 1), true);

            grid[12, 4] = TileKind.SpikeUp;

            Assert.AreEqual(TileKind.Empty, grid[12, 4], "still hidden");
            Assert.IsTrue(grid.IsMasked(12, 4));

            grid.ClearMask();
            Assert.AreEqual(TileKind.SpikeUp, grid[12, 4], "the edit landed on the row underneath");
        }

        [Test]
        public void OutOfBounds_StillReadsSolid_EvenWithAMaskSet()
        {
            TileGrid grid = Room();
            grid.SetMasked(new RectInt(0, 0, TileGrid.Width, TileGrid.Height), true);

            Assert.AreEqual(TileKind.Solid, grid[-1, 5], "a runaway body still cannot leave the world");
            Assert.AreEqual(TileKind.Solid, grid[TileGrid.Width, 5]);
        }

        [Test]
        public void LoadRows_ClearsAnyMask()
        {
            TileGrid grid = Room();
            grid.SetMasked(new RectInt(10, 4, 6, 1), true);

            grid.LoadRows(Room().ToRows());

            Assert.IsFalse(grid.IsMasked(12, 4), "a fresh level starts unmasked");
        }
    }
}
