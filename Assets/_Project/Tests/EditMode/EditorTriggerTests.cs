using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The editor's trigger and link rules (level-editor-spec 4). The controller on top is
    /// input plumbing and untested by design, which is exactly why none of this lives there.
    /// </summary>
    public sealed class EditorTriggerTests
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
                zones = new[] { new LevelZone { id = "z01", type = "air", rect = new[] { 2f, 2f, 4f, 4f } } },
            });
            return session;
        }

        [Test]
        public void AddTrigger_AllocatesTheLowestFreeId_AndSelectsIt()
        {
            EditorSession session = Session();

            LevelTrigger first = session.AddTrigger(new Rect(9f, 1f, 6f, 4f));
            LevelTrigger second = session.AddTrigger(new Rect(20f, 1f, 2f, 2f));

            Assert.AreEqual("t01", first.id);
            Assert.AreEqual("t02", second.id);
            Assert.AreEqual("t02", session.SelectedTriggerId);
            Assert.IsTrue(session.Dirty);
        }

        [Test]
        public void AddTrigger_AfterADelete_ReusesTheGap()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));
            session.AddTrigger(new Rect(20f, 1f, 2f, 2f));

            session.DeleteTrigger("t01");

            Assert.AreEqual("t01", session.AddTrigger(new Rect(4f, 4f, 2f, 2f)).id);
        }

        [Test]
        public void AddTrigger_Defaults_ToAnInvisibleOnceRegion()
        {
            LevelTrigger trigger = Session().AddTrigger(new Rect(9f, 1f, 6f, 4f));

            Assert.AreEqual("player_region", trigger.type);
            Assert.IsTrue(trigger.once, "the GDD 9.3 workhorse is once:true");
            Assert.IsFalse(trigger.visible);
        }

        [Test]
        public void TriggerAt_PicksTheSmallestOverlapping()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(0f, 0f, 20f, 12f));
            LevelTrigger small = session.AddTrigger(new Rect(4f, 4f, 2f, 2f));

            Assert.AreEqual(small.id, session.TriggerAt(new Vector2(5f, 5f)).id,
                            "a small trigger inside a big one must stay selectable");
            Assert.IsNull(session.TriggerAt(new Vector2(30f, 17f)));
        }

        [Test]
        public void SelectingATrigger_ClearsEveryOtherSelection()
        {
            EditorSession session = Session();
            session.SelectEntity("e01");
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));

            Assert.IsNull(session.SelectedEntityId);
            Assert.IsNull(session.SelectedZoneId);
            Assert.IsFalse(session.ExitSelected);

            session.SelectZone("z01");
            Assert.IsNull(session.SelectedTriggerId, "and the exclusion runs both ways");
        }

        [Test]
        public void SetTriggerRect_SnapsToTheTileGrid()
        {
            EditorSession session = Session();
            LevelTrigger trigger = session.AddTrigger(new Rect(9f, 1f, 6f, 4f));

            session.SetTriggerRect(trigger.id, new Rect(9.4f, 1.6f, 6.2f, 4.4f));

            Assert.AreEqual(new[] { 9f, 2f, 6f, 4f }, trigger.rect);
        }

        [Test]
        public void AddLink_AppendsToTheSameSource_RatherThanMakingASecond()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));

            session.AddLink("t01", "e01");
            session.AddLink("t01", "z01");

            Assert.AreEqual(1, session.Level.links.Length, "one link, two targets");
            Assert.AreEqual(new[] { "e01", "z01" }, session.Level.links[0].to);
        }

        [Test]
        public void AddLink_IsIdempotent_ForATargetAlreadyWired()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));

            session.AddLink("t01", "e01");
            session.AddLink("t01", "e01");

            Assert.AreEqual(1, session.Level.links[0].to.Length);
        }

        [Test]
        public void RemoveLink_DropsTheTarget_AndTheLinkWhenItsLastTargetGoes()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));
            session.AddLink("t01", "e01");
            session.AddLink("t01", "z01");

            session.RemoveLink("t01", "e01");
            Assert.AreEqual(new[] { "z01" }, session.Level.links[0].to);

            session.RemoveLink("t01", "z01");
            Assert.AreEqual(0, session.Level.links.Length, "an empty link is not a link");
        }

        [Test]
        public void DeleteTrigger_CascadesToItsLinks()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));
            session.AddLink("t01", "e01");

            Assert.IsTrue(session.DeleteTrigger("t01"));

            Assert.AreEqual(0, session.Level.links.Length, "an orphaned link is a validator error");
            Assert.IsNull(session.SelectedTriggerId);
        }

        [Test]
        public void DeleteTrigger_StripsItFromOtherLinksTargetLists()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));   // t01
            session.AddTrigger(new Rect(20f, 1f, 2f, 2f));  // t02, also a valid link target

            session.AddLink("t01", "e01");
            session.AddLink("t01", "t02");

            session.DeleteTrigger("t02");

            Assert.AreEqual(new[] { "e01" }, session.Level.links[0].to,
                            "t02 is gone as a target, but the link to e01 survives");
        }

        [Test]
        public void DeleteTrigger_RemovesALinkLeftWithNoTargets_WhenItsOnlyTargetIsDeleted()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));   // t01
            session.AddTrigger(new Rect(20f, 1f, 2f, 2f));  // t02

            session.AddLink("t01", "t02");

            session.DeleteTrigger("t02");

            Assert.AreEqual(0, session.Level.links.Length, "an empty link is not a link");
        }

        [Test]
        public void DeleteEntity_StripsItFromAnyLinksTargetList()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));   // t01

            session.AddLink("t01", "e01");
            session.AddLink("t01", "z01");

            session.DeleteEntity("e01");

            Assert.AreEqual(new[] { "z01" }, session.Level.links[0].to,
                            "e01 is gone as a target, but the link to z01 survives");
        }

        [Test]
        public void DeleteZone_RemovesALinkLeftWithNoTargets_WhenItsOnlyTargetIsDeleted()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(9f, 1f, 6f, 4f));   // t01

            session.AddLink("t01", "z01");

            session.DeleteZone("z01");

            Assert.AreEqual(0, session.Level.links.Length, "an empty link is not a link");
        }

        [Test]
        public void TargetCentre_FindsEntitiesZonesAndTriggers()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(10f, 2f, 4f, 4f));

            Assert.IsTrue(session.TargetCentre("e01", out Vector2 entity));
            Assert.AreEqual(new Vector2(20f, 8f), entity);

            Assert.IsTrue(session.TargetCentre("z01", out Vector2 zone));
            Assert.AreEqual(new Vector2(4f, 4f), zone);

            Assert.IsTrue(session.TargetCentre("t01", out Vector2 trigger));
            Assert.AreEqual(new Vector2(12f, 4f), trigger);

            Assert.IsFalse(session.TargetCentre("nope", out _));
        }

        [Test]
        public void LinkAt_HitsTheArrowBetweenSourceAndTarget()
        {
            EditorSession session = Session();
            session.AddTrigger(new Rect(10f, 2f, 4f, 4f));   // centre 12, 4
            session.AddLink("t01", "e01");                    // saw at 20, 8

            Assert.IsTrue(session.LinkAt(new Vector2(16f, 6f), out string from, out string to),
                          "the midpoint of the arrow");
            Assert.AreEqual("t01", from);
            Assert.AreEqual("e01", to);

            Assert.IsFalse(session.LinkAt(new Vector2(2f, 16f), out _, out _));
        }
    }
}
