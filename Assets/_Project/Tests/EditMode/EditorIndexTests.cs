using KeepyUppy.LevelEditing;
using KeepyUppy.Sim.Level;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The editor maintaining the manifest.
    ///
    /// This is what makes a declared level list safe: without the tool keeping it current, a
    /// manifest is just a file that goes stale.
    /// </summary>
    public sealed class EditorIndexTests
    {
        static EditorSession Session()
        {
            var session = new EditorSession();
            session.LoadIndex(new LevelIndex
            {
                version = LevelIndex.CurrentVersion,
                worlds = new[]
                {
                    new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_01", "w1_02" } },
                    new LevelWorld { id = "w2", name = "Two", levels = new[] { "w2_01" } },
                },
            });
            return session;
        }

        [Test]
        public void AddingALevelAppendsItToItsWorld()
        {
            EditorSession session = Session();

            session.AddLevel("w1_03", "w1");

            CollectionAssert.AreEqual(new[] { "w1_01", "w1_02", "w1_03" },
                                      session.Index.WorldOf("w1_03").levels);
            Assert.IsTrue(session.IndexDirty);
        }

        [Test]
        public void AddingToAnUnknownWorldCreatesIt()
        {
            // Authoring world 3 before world 3 exists is normal; refusing would just mean
            // hand-editing the manifest, which is what this exists to avoid.
            EditorSession session = Session();

            LevelWorld world = session.AddLevel("w3_01", "w3");

            Assert.AreEqual("w3", world.id);
            CollectionAssert.AreEqual(new[] { "w3_01" }, world.levels);
            Assert.AreEqual(3, session.Index.worlds.Length);
        }

        [Test]
        public void AddingALevelTwiceIsHarmless()
        {
            EditorSession session = Session();

            session.AddLevel("w1_01", "w1");

            CollectionAssert.AreEqual(new[] { "w1_01", "w1_02" }, session.Index.WorldOf("w1_01").levels);
        }

        [Test]
        public void RemovingALevelUnlistsItWithoutDeletingTheFile()
        {
            // Unlisting turns a level into a draft. The file is left alone on purpose: losing
            // work to a mis-click is worse than an unlisted file.
            EditorSession session = Session();

            Assert.IsTrue(session.RemoveLevel("w1_01"));
            Assert.IsFalse(session.Index.Contains("w1_01"));
            Assert.IsFalse(session.RemoveLevel("w1_01"), "removing twice is harmless");
        }

        [Test]
        public void MovingALevelReordersItWithinItsWorld()
        {
            EditorSession session = Session();

            Assert.IsTrue(session.MoveLevel("w1_02", -1));
            CollectionAssert.AreEqual(new[] { "w1_02", "w1_01" }, session.Index.WorldOf("w1_02").levels);
            Assert.IsTrue(session.IndexDirty);
        }

        [Test]
        public void MovingPastTheEndOfAWorldDoesNothing()
        {
            // Deliberately does NOT move the level into the next world. Reordering and
            // re-homing are different intentions, and the second needs its own gesture.
            EditorSession session = Session();

            Assert.IsFalse(session.MoveLevel("w1_01", -1), "already first");
            Assert.IsFalse(session.MoveLevel("w2_01", 1), "only level in its world");
            CollectionAssert.AreEqual(new[] { "w1_01", "w1_02" }, session.Index.WorldOf("w1_01").levels);
        }

        [Test]
        public void MarkIndexSavedClearsTheFlag()
        {
            EditorSession session = Session();
            session.AddLevel("w1_03", "w1");

            session.MarkIndexSaved();

            Assert.IsFalse(session.IndexDirty);
        }

        [Test]
        public void TheLevelDirtyFlagAndTheIndexDirtyFlagAreSeparate()
        {
            // They are different files with different saves. Conflating them would make
            // saving a level look like it saved the manifest.
            EditorSession session = Session();
            session.Load(new LevelData { tiles = new string[0] });

            session.AddLevel("w1_03", "w1");

            Assert.IsTrue(session.IndexDirty);
            Assert.IsFalse(session.Dirty, "adding to the manifest does not edit the level");
        }
    }
}
