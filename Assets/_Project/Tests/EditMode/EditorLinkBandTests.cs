using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Wiring a whole row at once (panel exit-direction design).
    ///
    /// One trigger could already name many targets — a click each. What was missing is the
    /// band: drag a rect over a row of saws and wire all of them. The band answers with ids
    /// in the same precedence order a single click uses, so what a drag wires is what the
    /// same clicks would have wired.
    /// </summary>
    public sealed class EditorLinkBandTests
    {
        static EditorSession Session()
        {
            var grid = new TileGrid();
            grid.FillBorder();

            var session = new EditorSession();
            session.Load(new LevelData
            {
                tiles = grid.ToRows(),
                entities = new[]
                {
                    new LevelEntity { id = "e01", type = "saw", pos = new[] { 10f, 8f } },
                    new LevelEntity { id = "e02", type = "saw", pos = new[] { 12f, 8f } },
                    new LevelEntity { id = "e03", type = "saw", pos = new[] { 26f, 8f } },
                },
                zones = new[]
                {
                    new LevelZone { id = "z01", type = "air", rect = new[] { 10f, 6f, 2f, 2f } },
                },
                triggers = new[]
                {
                    new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 2f, 1f, 4f, 4f } },
                },
                panels = new[]
                {
                    new LevelPanel { id = "p01", rect = new[] { 9f, 7f, 2f, 1f } },
                },
            });
            return session;
        }

        [Test]
        public void TargetIdsIn_ReturnsEverythingTheBandTouches_EntitiesZonesTriggersThenPanels()
        {
            EditorSession session = Session();

            string[] ids = session.TargetIdsIn(new Rect(8f, 5f, 6f, 5f));

            Assert.AreEqual(new[] { "e01", "e02", "z01", "p01" }, ids);
        }

        [Test]
        public void TargetIdsIn_SkipsWhatFallsOutsideTheBand()
        {
            EditorSession session = Session();

            Assert.AreEqual(new string[0], session.TargetIdsIn(new Rect(0f, 14f, 2f, 2f)));
            Assert.AreEqual(new[] { "e03" }, session.TargetIdsIn(new Rect(25f, 7f, 2f, 2f)));
        }

        [Test]
        public void TargetIdsIn_IncludesATriggerTheBandCovers()
        {
            EditorSession session = Session();

            Assert.AreEqual(new[] { "t01" }, session.TargetIdsIn(new Rect(1f, 0f, 6f, 6f)));
        }

        [Test]
        public void TargetIdsIn_TakesAZoneTheBandOnlyClips()
        {
            EditorSession session = Session();

            Assert.AreEqual(new[] { "z01" }, session.TargetIdsIn(new Rect(11.4f, 7.4f, 0.4f, 0.4f)),
                            "a band that overlaps at all counts — a partial drag over a row is the normal case");
        }

        [Test]
        public void WiringABand_AppendsEveryTargetToTheOneLink()
        {
            EditorSession session = Session();

            foreach (string id in session.TargetIdsIn(new Rect(8f, 5f, 6f, 5f)))
                session.AddLink("t01", id);

            Assert.AreEqual(1, session.Level.links.Length);
            Assert.AreEqual(new[] { "e01", "e02", "z01", "p01" }, session.Level.links[0].to);
        }
    }
}
