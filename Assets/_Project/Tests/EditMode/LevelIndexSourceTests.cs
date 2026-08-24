using System.Collections.Generic;
using System.IO;
using KeepyUppy.Runtime;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Reading and writing the manifest through ILevelSource.
    ///
    /// Uses a real temporary directory rather than a mock: the thing most likely to break is
    /// the file layout - what is named what, and what List() does or does not return.
    /// </summary>
    public sealed class LevelIndexSourceTests
    {
        string _directory;
        FileLevelSource _source;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "keepyuppy_levels_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_directory);
            _source = new FileLevelSource(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void AnAbsentManifestIsNotAnError()
        {
            // A project with levels and no manifest yet is a normal state, not a failure.
            Assert.IsFalse(_source.TryLoadIndex(out LevelIndex index));
            Assert.IsNull(index);
        }

        [Test]
        public void TheManifestRoundTripsThroughDisk()
        {
            var written = new LevelIndex
            {
                version = LevelIndex.CurrentVersion,
                worlds = new[]
                {
                    new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_02", "w1_01" } },
                },
            };

            _source.SaveIndex(written);

            Assert.IsTrue(_source.TryLoadIndex(out LevelIndex read));
            CollectionAssert.AreEqual(new[] { "w1_02", "w1_01" }, read.worlds[0].levels,
                                      "the declared order must survive the round trip");
        }

        [Test]
        public void TheManifestIsWrittenReadable()
        {
            // The readable diff is the point of the format (CLAUDE.md commit conventions).
            _source.SaveIndex(LevelIndex.Default());

            string json = File.ReadAllText(Path.Combine(_directory, "index.json"));
            StringAssert.Contains("\n", json, "the manifest must not be minified");
        }

        [Test]
        public void TheManifestIsNotListedAsALevel()
        {
            // index.json lives in the levels directory and is not a level. Without this,
            // every level list in the game and the editor grows a phantom entry.
            _source.SaveIndex(LevelIndex.Default());
            _source.Save("w1_01", new LevelData());

            IReadOnlyList<string> ids = _source.List();

            CollectionAssert.Contains(ids, "w1_01");
            CollectionAssert.DoesNotContain(ids, LevelIndex.FileName);
        }

        [Test]
        public void ACorruptManifestFailsSoftly()
        {
            // Hand-edited files exist. A broken one must not take the editor down.
            File.WriteAllText(Path.Combine(_directory, "index.json"), "{ not json");

            Assert.DoesNotThrow(() => _source.TryLoadIndex(out _));
            Assert.IsFalse(_source.TryLoadIndex(out _));
        }
    }
}
