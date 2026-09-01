using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Authoring panels (tile-panels design). Mirrors the zone and trigger sections, which is
    /// the point — an author who knows one rect tool knows all of them.
    /// </summary>
    public sealed class EditorPanelTests
    {
        static EditorSession Session()
        {
            var grid = new TileGrid();
            grid.FillBorder();

            var session = new EditorSession();
            session.Load(new LevelData
            {
                tiles = grid.ToRows(),
                entities = new[] { new LevelEntity { id = "e01", type = "saw", pos = new[] { 20f, 8f } } },
                triggers = new[]
                {
                    new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 2f, 1f, 4f, 4f } },
                },
            });
            return session;
        }

        [Test]
        public void AddPanel_AllocatesTheLowestFreeId_AndSelectsIt()
        {
            EditorSession session = Session();

            LevelPanel first = session.AddPanel(new Rect(10f, 1f, 6f, 1f));
            LevelPanel second = session.AddPanel(new Rect(20f, 1f, 2f, 1f));

            Assert.AreEqual("p01", first.id);
            Assert.AreEqual("p02", second.id);
            Assert.AreEqual("p02", session.SelectedPanelId);
            Assert.AreEqual("on", first.start_state);
            Assert.IsTrue(session.Dirty);
        }

        [Test]
        public void AddPanel_AfterADelete_ReusesTheGap()
        {
            EditorSession session = Session();
            session.AddPanel(new Rect(10f, 1f, 6f, 1f));
            session.AddPanel(new Rect(20f, 1f, 2f, 1f));

            session.DeletePanel("p01");

            Assert.AreEqual("p01", session.AddPanel(new Rect(4f, 1f, 2f, 1f)).id);
        }

        [Test]
        public void PanelAt_PicksTheSmallestOverlapping()
        {
            EditorSession session = Session();
            session.AddPanel(new Rect(0f, 0f, 20f, 12f));
            LevelPanel small = session.AddPanel(new Rect(4f, 4f, 2f, 2f));

            Assert.AreEqual(small.id, session.PanelAt(new Vector2(5f, 5f)).id);
            Assert.IsNull(session.PanelAt(new Vector2(30f, 17f)));
        }

        [Test]
        public void SelectingAPanel_ClearsEveryOtherSelection()
        {
            EditorSession session = Session();
            session.SelectEntity("e01");
            session.AddPanel(new Rect(10f, 1f, 6f, 1f));

            Assert.IsNull(session.SelectedEntityId);
            Assert.IsNull(session.SelectedZoneId);
            Assert.IsNull(session.SelectedTriggerId);
            Assert.IsFalse(session.ExitSelected);

            session.SelectTrigger("t01");
            Assert.IsNull(session.SelectedPanelId, "and the exclusion runs both ways");
        }

        [Test]
        public void SetPanelRect_SnapsToTheTileGrid()
        {
            EditorSession session = Session();
            LevelPanel panel = session.AddPanel(new Rect(10f, 1f, 6f, 1f));

            session.SetPanelRect(panel.id, new Rect(9.4f, 1.6f, 6.2f, 1.4f));

            Assert.AreEqual(new[] { 9f, 2f, 6f, 1f }, panel.rect);
        }

        [Test]
        public void SetPanelEnabled_WritesTheStartState()
        {
            EditorSession session = Session();
            LevelPanel panel = session.AddPanel(new Rect(10f, 1f, 6f, 1f));

            session.SetPanelEnabled(panel.id, false);
            Assert.AreEqual("off", panel.start_state);

            session.SetPanelEnabled(panel.id, true);
            Assert.AreEqual("on", panel.start_state);
        }

        [Test]
        public void APanelCanBeALinkTarget_AndDeletingItCleansTheLinkUp()
        {
            EditorSession session = Session();
            session.AddPanel(new Rect(10f, 1f, 6f, 1f));
            session.AddLink("t01", "p01");

            Assert.AreEqual(new[] { "p01" }, session.Level.links[0].to);
            Assert.IsTrue(session.TargetCentre("p01", out Vector2 centre));
            Assert.AreEqual(new Vector2(13f, 1.5f), centre);

            session.DeletePanel("p01");

            Assert.AreEqual(0, session.Level.links.Length, "an orphaned link is a validator error");
        }
    }
}
