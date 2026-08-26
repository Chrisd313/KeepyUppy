using KeepyUppy.Sim.Level;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// "What comes next" is answerable only because the manifest declares an order
    /// (spec: menus and the level list). This is the query the results screen's Next uses.
    /// </summary>
    public sealed class LevelOrderTests
    {
        static LevelIndex TwoWorlds() => new LevelIndex
        {
            version = LevelIndex.CurrentVersion,
            worlds = new[]
            {
                new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_01", "w1_02" } },
                new LevelWorld { id = "w2", name = "Two", levels = new[] { "w2_01" } },
            },
        };

        [Test]
        public void NextAfterFollowsTheManifestOrder()
        {
            Assert.AreEqual("w1_02", TwoWorlds().NextAfter("w1_01"));
        }

        [Test]
        public void TheLastLevelOfAWorldHasNoNext()
        {
            // Deliberately does NOT roll into world 2. Crossing worlds is a progression
            // question that belongs with save data, not with "what is the next room".
            Assert.IsNull(TwoWorlds().NextAfter("w1_02"));
            Assert.IsNull(TwoWorlds().NextAfter("w2_01"));
        }

        [Test]
        public void AnUnlistedLevelHasNoNext()
        {
            // Drafts are openable in the editor and absent from the game; asking what
            // follows one is a question with no answer, not an error.
            Assert.IsNull(TwoWorlds().NextAfter("scratch"));
            Assert.IsNull(TwoWorlds().NextAfter(null));
        }

        [Test]
        public void AMalformedManifestAnswersNothing()
        {
            Assert.IsNull(new LevelIndex().NextAfter("w1_01"));
            Assert.IsNull(new LevelIndex { worlds = new LevelWorld[] { null } }.NextAfter("w1_01"));
        }
    }
}
