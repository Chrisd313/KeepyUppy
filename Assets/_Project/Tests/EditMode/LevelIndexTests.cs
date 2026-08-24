using System.Collections.Generic;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The level manifest (spec: 2026-08-24 menus and the level list).
    ///
    /// The manifest is authoritative for ordering. These tests pin that, because the
    /// alternative - ordering implied by filenames - is what the manifest exists to replace.
    /// </summary>
    public sealed class LevelIndexTests
    {
        [Test]
        public void RoundTripsThroughJson()
        {
            var index = new LevelIndex
            {
                version = LevelIndex.CurrentVersion,
                worlds = new[]
                {
                    new LevelWorld { id = "w1", name = "The Living Room",
                                     levels = new[] { "w1_01", "w1_02" } },
                },
            };

            string json = JsonUtility.ToJson(index, prettyPrint: true);
            LevelIndex parsed = JsonUtility.FromJson<LevelIndex>(json);

            Assert.AreEqual(1, parsed.worlds.Length);
            Assert.AreEqual("The Living Room", parsed.worlds[0].name);
            CollectionAssert.AreEqual(new[] { "w1_01", "w1_02" }, parsed.worlds[0].levels);
        }

        [Test]
        public void TheDefaultManifestNamesTheSeededRoom()
        {
            // SimBootstrap seeds w1_01 when the levels directory is empty. The manifest it
            // writes alongside has to agree, or a fresh clone boots to an empty game.
            LevelIndex index = LevelIndex.Default();

            Assert.AreEqual(LevelIndex.CurrentVersion, index.version);
            CollectionAssert.Contains(index.worlds[0].levels, "w1_01");
        }

        static LevelIndex TwoWorlds() => new LevelIndex
        {
            version = LevelIndex.CurrentVersion,
            worlds = new[]
            {
                new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_02", "w1_01" } },
                new LevelWorld { id = "w2", name = "Two", levels = new[] { "w2_01" } },
            },
        };

        [Test]
        public void OrderComesFromTheManifest_NotTheFilenames()
        {
            // The whole point. w1_02 is listed first, so it IS first, however it sorts.
            LevelIndex index = TwoWorlds();

            CollectionAssert.AreEqual(new[] { "w1_02", "w1_01", "w2_01" }, index.AllLevels());
            Assert.AreEqual(0, index.SlotOf("w1_02"));
            Assert.AreEqual(1, index.SlotOf("w1_01"));
        }

        [Test]
        public void WorldLookupsFindTheOwningWorld()
        {
            LevelIndex index = TwoWorlds();

            Assert.AreEqual("w2", index.WorldOf("w2_01").id);
            Assert.AreEqual(2, index.WorldNumberOf("w2_01"), "world numbers are 1-based");
            Assert.AreEqual(1, index.WorldNumberOf("w1_01"));
        }

        [Test]
        public void AnUnlistedLevelIsAbsentEverywhere()
        {
            LevelIndex index = TwoWorlds();

            Assert.IsFalse(index.Contains("w9_99"));
            Assert.IsNull(index.WorldOf("w9_99"));
            Assert.AreEqual(-1, index.SlotOf("w9_99"));
            Assert.AreEqual(-1, index.WorldNumberOf("w9_99"));
        }

        [Test]
        public void DraftsAreTheFilesTheManifestDoesNotList()
        {
            // A level being authored while the previous one is still under test. The editor
            // opens it; the game does not list it.
            LevelIndex index = TwoWorlds();

            List<string> drafts = index.DraftsAmong(new[] { "w1_01", "w1_02", "w2_01", "scratch" });

            CollectionAssert.AreEqual(new[] { "scratch" }, drafts);
        }

        [Test]
        public void QueriesSurviveAMalformedManifest()
        {
            // A hand-edited file must never take the editor down.
            var index = new LevelIndex { worlds = new[] { null, new LevelWorld { levels = null } } };

            Assert.DoesNotThrow(() => index.AllLevels());
            Assert.IsEmpty(index.AllLevels());
            Assert.IsFalse(index.Contains("anything"));

            // worlds itself can be null too - JsonUtility leaves it that way for a manifest
            // with no "worlds" key at all, not just an empty array.
            var noWorlds = new LevelIndex { worlds = null };

            Assert.DoesNotThrow(() => noWorlds.AllLevels());
            Assert.IsEmpty(noWorlds.AllLevels());
            Assert.IsFalse(noWorlds.Contains("anything"));
            Assert.IsNull(noWorlds.WorldOf("anything"));
            Assert.AreEqual(-1, noWorlds.SlotOf("anything"));
            Assert.AreEqual(-1, noWorlds.WorldNumberOf("anything"));
        }
    }
}
