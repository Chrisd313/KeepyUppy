using KeepyUppy.Sim;
using KeepyUppy.Sim.Panels;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Panels: the geometry a link can take away (tile-panels design). The mask is rebuilt
    /// from the flags rather than toggled cell by cell, which is what makes overlapping
    /// panels compose without a special case.
    /// </summary>
    public sealed class PanelFieldTests
    {
        static TileGrid Room()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(10, 4, 6, 1, TileKind.Solid);
            return grid;
        }

        static SimPanel Panel(string id, RectInt rect, bool enabled = true)
            => new SimPanel { Id = id, Rect = rect, Enabled = enabled, StartEnabled = enabled };

        [Test]
        public void ADisabledPanel_HidesItsTiles_WhenApplied()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1), enabled: false));

            panels.Apply(grid);

            Assert.AreEqual(TileKind.Empty, grid[12, 4]);
        }

        [Test]
        public void SetEnabled_TogglesTheTiles_AfterTheNextApply()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1)));
            panels.Apply(grid);

            panels.SetEnabled("p01", false);
            Assert.IsTrue(panels.Dirty, "the mask needs rebuilding");
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Empty, grid[12, 4]);
            Assert.IsFalse(panels.Dirty, "applying clears it");

            panels.SetEnabled("p01", true);
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Solid, grid[12, 4]);
        }

        [Test]
        public void OverlappingPanels_HideACellIfEitherIsOff()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1)));
            panels.Add(Panel("p02", new RectInt(12, 4, 2, 1)));

            panels.SetEnabled("p02", false);
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Empty, grid[12, 4], "hidden by p02");
            Assert.AreEqual(TileKind.Solid, grid[10, 4], "outside p02, and p01 is on");

            panels.SetEnabled("p02", true);
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Solid, grid[12, 4], "visible only when both are on");
        }

        [Test]
        public void TurnedOnThisStep_RecordsOnlyTheOffToOnEdge()
        {
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1), enabled: false));

            panels.BeginStep();
            panels.SetEnabled("p01", true);
            Assert.AreEqual(1, panels.TurnedOnThisStep.Count);

            panels.BeginStep();
            panels.SetEnabled("p01", true);
            Assert.AreEqual(0, panels.TurnedOnThisStep.Count, "already on is not an edge");

            panels.BeginStep();
            panels.SetEnabled("p01", false);
            Assert.AreEqual(0, panels.TurnedOnThisStep.Count, "turning off is not a crush");
        }

        [Test]
        public void Reset_RestoresEveryStartState()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1)));

            panels.BeginStep();
            panels.SetEnabled("p01", false);
            panels.Apply(grid);
            Assert.AreEqual(TileKind.Empty, grid[12, 4]);

            panels.Reset();
            panels.Apply(grid);

            Assert.AreEqual(TileKind.Solid, grid[12, 4], "the retry gets the room it started with");
            Assert.AreEqual(0, panels.TurnedOnThisStep.Count);
        }

        [Test]
        public void AnUnknownId_ChangesNothing()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1)));
            panels.Apply(grid);

            panels.SetEnabled("nope", false);

            Assert.IsTrue(panels[0].Enabled);
            Assert.IsFalse(panels.Dirty);
        }

        [Test]
        public void SetEnabled_ToItsCurrentValue_ChangesNothing()
        {
            TileGrid grid = Room();
            var panels = new PanelField();
            panels.Add(Panel("p01", new RectInt(10, 4, 6, 1)));
            panels.Apply(grid);

            panels.SetEnabled("p01", true);

            Assert.IsFalse(panels.Dirty, "already on, setting on again is not a change");
            Assert.AreEqual(0, panels.TurnedOnThisStep.Count);
        }
    }
}
