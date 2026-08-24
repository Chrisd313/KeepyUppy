using System.Collections.Generic;
using KeepyUppy.Sim.Level;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Manifest lint. A declared list is only better than a derived one if the tooling keeps
    /// it current, and these are what catch it going stale.
    ///
    /// Warnings, never errors: a half-finished level set must always be saveable, exactly as
    /// LevelValidator treats a half-finished level.
    /// </summary>
    public sealed class LevelIndexValidatorTests
    {
        static LevelIndex Index(params string[] levels) => new LevelIndex
        {
            version = LevelIndex.CurrentVersion,
            worlds = new[] { new LevelWorld { id = "w1", name = "One", levels = levels } },
        };

        [Test]
        public void ACleanManifestProducesNoWarnings()
        {
            List<string> warnings = LevelIndexValidator.Validate(
                Index("w1_01", "w1_02"), new[] { "w1_01", "w1_02" });

            CollectionAssert.IsEmpty(warnings);
        }

        [Test]
        public void AListedLevelMissingFromDiskIsAWarning()
        {
            List<string> warnings = LevelIndexValidator.Validate(
                Index("w1_01", "w1_02"), new[] { "w1_01" });

            Assert.IsTrue(warnings.Exists(w => w.Contains("w1_02")),
                          "the game would show a level that cannot be loaded");
        }

        [Test]
        public void ADraftIsReportedButNotAsAProblem()
        {
            // A file on disk that is not listed is deliberate, so it is mentioned and marked
            // as a draft rather than reported as an error.
            List<string> warnings = LevelIndexValidator.Validate(
                Index("w1_01"), new[] { "w1_01", "scratch" });

            Assert.IsTrue(warnings.Exists(w => w.Contains("scratch") && w.Contains("draft")));
        }

        [Test]
        public void ALevelListedTwiceIsAWarning()
        {
            List<string> warnings = LevelIndexValidator.Validate(
                Index("w1_01", "w1_01"), new[] { "w1_01" });

            Assert.IsTrue(warnings.Exists(w => w.Contains("more than once")));
        }

        [Test]
        public void ALevelListedInTwoDifferentWorldsIsAWarning()
        {
            // The easy regression: moving the `seen` HashSet inside the per-world loop would
            // reset it for every world and miss exactly this case, while still catching a
            // same-world duplicate.
            var index = new LevelIndex
            {
                version = LevelIndex.CurrentVersion,
                worlds = new[]
                {
                    new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_01" } },
                    new LevelWorld { id = "w2", name = "Two", levels = new[] { "w1_01" } },
                },
            };

            List<string> warnings = LevelIndexValidator.Validate(index, new[] { "w1_01" });

            Assert.IsTrue(warnings.Exists(w => w.Contains("w1_01") && w.Contains("more than once")));
        }

        [Test]
        public void AnEmptyWorldIsAWarning()
        {
            var index = new LevelIndex
            {
                worlds = new[] { new LevelWorld { id = "w1", name = "One", levels = new string[0] } },
            };

            Assert.IsTrue(LevelIndexValidator.Validate(index, new string[0])
                              .Exists(w => w.Contains("no levels")));
        }

        [Test]
        public void AManifestWithNoWorldsArrayIsAWarning()
        {
            // Not just an empty array - JsonUtility leaves worlds null for a manifest with no
            // "worlds" key at all, and that must not throw either.
            var index = new LevelIndex { worlds = null };

            List<string> warnings = LevelIndexValidator.Validate(index, new string[0]);

            Assert.IsTrue(warnings.Exists(w => w.Contains("no worlds")));
        }

        [Test]
        public void AVersionFromTheFutureIsAWarning()
        {
            var index = LevelIndex.Default();
            index.version = LevelIndex.CurrentVersion + 1;

            Assert.IsTrue(LevelIndexValidator.Validate(index, new[] { "w1_01" })
                              .Exists(w => w.Contains("version")));
        }

        [Test]
        public void SlotDriftIsAWarning()
        {
            // The manifest is authoritative; the fields inside the level are advisory. When
            // they disagree the file is stale, and the lint is what says so.
            LevelIndex index = Index("w1_01", "w1_02");
            var level = new LevelData { id = "w1_02", world = 1, index = 1 };

            List<string> warnings = LevelIndexValidator.ValidateSlot(level, index);

            Assert.IsTrue(warnings.Exists(w => w.Contains("index")),
                          "w1_02 sits in slot 2 but the file claims index 1");
        }

        [Test]
        public void MatchingSlotFieldsProduceNoWarnings()
        {
            LevelIndex index = Index("w1_01", "w1_02");
            var level = new LevelData { id = "w1_02", world = 1, index = 2 };

            CollectionAssert.IsEmpty(LevelIndexValidator.ValidateSlot(level, index));
        }

        [Test]
        public void AnUnlistedLevelHasNoSlotToDriftFrom()
        {
            // A draft is not stale, it is just not in the game yet.
            LevelIndex index = Index("w1_01");
            var level = new LevelData { id = "scratch", world = 9, index = 9 };

            CollectionAssert.IsEmpty(LevelIndexValidator.ValidateSlot(level, index));
        }
    }
}
