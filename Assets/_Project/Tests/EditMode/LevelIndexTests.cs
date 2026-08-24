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
    }
}
