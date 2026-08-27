using KeepyUppy.Shell.Save;
using KeepyUppy.Sim.Level;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Every progression rule. These are decisions, so they live in a tested class rather
    /// than in ShellView — see ScreenStack.ObserveWin for what happened the one time a rule
    /// leaked into the view.
    /// </summary>
    public sealed class SaveModelTests
    {
        [Test]
        public void AtOrUnderParIsGold()
        {
            Assert.AreEqual(Medal.Gold, SaveModel.Grade(5, 7));
            Assert.AreEqual(Medal.Gold, SaveModel.Grade(7, 7), "par itself is gold");
        }

        [Test]
        public void WithinThreeOverParIsSilver()
        {
            Assert.AreEqual(Medal.Silver, SaveModel.Grade(8, 7));
            Assert.AreEqual(Medal.Silver, SaveModel.Grade(10, 7), "par + 3 is the last silver");
        }

        [Test]
        public void FurtherOverParIsBronze()
        {
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(11, 7));
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(400, 7));
        }

        [Test]
        public void AnUnsetParNeverAwardsGold()
        {
            // par_touches defaults to 0 and is only filled in from playtest metrics (GDD 15),
            // so "unset" is the normal state while a level is being authored. A level that
            // hands out gold because nobody measured it yet makes the medal meaningless
            // exactly when it is being tuned.
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(0, 0));
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(3, 0));
        }

        [Test]
        public void ANegativeParIsTreatedAsUnset()
        {
            // Nothing writes a negative par, but the level file is hand-editable JSON.
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(1, -5));
        }

        // ------------------------------------------------------------------ merging

        static SaveData Empty() => new SaveData();

        [Test]
        public void AFirstClearCreatesTheRecordAndFlagsNoBest()
        {
            SaveResult result = SaveModel.Merge(Empty(), new RunResult("w1_01", 9, 600, 7));

            Assert.AreEqual(1, result.Data.levels.Length);
            Assert.AreEqual("w1_01", result.Record.id);
            Assert.IsTrue(result.Record.cleared);
            Assert.AreEqual(9, result.Record.bestTouches);
            Assert.AreEqual(600, result.Record.bestTimeFrames);
            Assert.AreEqual((int)Medal.Silver, result.Record.medal);

            // Nothing was beaten - there was nothing there to beat.
            Assert.IsFalse(result.NewBestTouches);
            Assert.IsFalse(result.NewBestTime);
        }

        [Test]
        public void FewerTouchesBeatsTheRecordAndRegradesTheMedal()
        {
            SaveData data = SaveModel.Merge(Empty(), new RunResult("w1_01", 9, 600, 7)).Data;

            SaveResult result = SaveModel.Merge(data, new RunResult("w1_01", 6, 900, 7));

            Assert.AreEqual(6, result.Record.bestTouches);
            Assert.AreEqual((int)Medal.Gold, result.Record.medal, "the medal follows bestTouches");
            Assert.IsTrue(result.NewBestTouches);
            Assert.IsFalse(result.NewBestTime, "a slower run does not set a time record");
            Assert.AreEqual(600, result.Record.bestTimeFrames, "and does not overwrite one");
        }

        [Test]
        public void AFasterRunSetsTheTimeWithoutTouchingTheMedal()
        {
            SaveData data = SaveModel.Merge(Empty(), new RunResult("w1_01", 6, 900, 7)).Data;

            SaveResult result = SaveModel.Merge(data, new RunResult("w1_01", 20, 500, 7));

            Assert.AreEqual(500, result.Record.bestTimeFrames);
            Assert.IsTrue(result.NewBestTime);
            Assert.AreEqual(6, result.Record.bestTouches, "a worse touch count is not a best");
            Assert.AreEqual((int)Medal.Gold, result.Record.medal, "so the medal is untouched");
            Assert.IsFalse(result.NewBestTouches);
        }

        [Test]
        public void EqualIsNotBetter()
        {
            SaveData data = SaveModel.Merge(Empty(), new RunResult("w1_01", 6, 600, 7)).Data;

            SaveResult result = SaveModel.Merge(data, new RunResult("w1_01", 6, 600, 7));

            Assert.IsFalse(result.NewBestTouches);
            Assert.IsFalse(result.NewBestTime);
        }

        [Test]
        public void MergeDoesNotMutateTheDataItWasGiven()
        {
            SaveData original = SaveModel.Merge(Empty(), new RunResult("w1_01", 9, 600, 7)).Data;

            SaveModel.Merge(original, new RunResult("w1_01", 2, 100, 7));

            Assert.AreEqual(9, original.levels[0].bestTouches,
                            "the caller's copy is the one that was already on screen");
        }

        [Test]
        public void ADifferentLevelIsAppendedAndOthersAreLeftAlone()
        {
            SaveData data = SaveModel.Merge(Empty(), new RunResult("w1_01", 9, 600, 7)).Data;

            SaveData after = SaveModel.Merge(data, new RunResult("w1_02", 4, 300, 5)).Data;

            Assert.AreEqual(2, after.levels.Length);
            Assert.AreEqual(9, SaveModel.RecordFor(after, "w1_01").bestTouches);
            Assert.AreEqual(4, SaveModel.RecordFor(after, "w1_02").bestTouches);
        }

        [Test]
        public void RecordForIsNullWhenTheLevelHasNeverBeenCleared()
        {
            Assert.IsNull(SaveModel.RecordFor(Empty(), "w1_01"));
            Assert.IsNull(SaveModel.RecordFor(new SaveData { levels = null }, "w1_01"));
        }

        // ------------------------------------------------------------------ formatting

        [Test]
        public void TimeFormatsAsMinutesSecondsAndHundredths()
        {
            Assert.AreEqual("0:00.00", SaveModel.FormatTime(0));
            Assert.AreEqual("0:01.00", SaveModel.FormatTime(60));
            Assert.AreEqual("0:00.50", SaveModel.FormatTime(30), "half a second at 60 Hz");
            Assert.AreEqual("1:00.00", SaveModel.FormatTime(3600));
            Assert.AreEqual("10:00.00", SaveModel.FormatTime(36000), "past ten minutes it just gets wider");
        }

        [Test]
        public void ANegativeTimeReadsAsZero()
        {
            // Nothing writes one; the save file is editable and the menu must not draw junk.
            Assert.AreEqual("0:00.00", SaveModel.FormatTime(-1));
        }

        // ------------------------------------------------------------------ unlock

        static LevelIndex TwoWorlds() => new LevelIndex
        {
            worlds = new[]
            {
                new LevelWorld { id = "w1", name = "One", levels = new[] { "w1_01", "w1_02" } },
                new LevelWorld { id = "w2", name = "Two", levels = new[] { "w2_01" } },
            },
        };

        static SaveData Cleared(params string[] ids)
        {
            SaveData data = new SaveData();
            foreach (string id in ids) data = SaveModel.Merge(data, new RunResult(id, 5, 300, 7)).Data;

            return data;
        }

        [Test]
        public void TheFirstWorldIsAlwaysOpen()
        {
            Assert.IsTrue(SaveModel.WorldUnlocked(TwoWorlds(), new SaveData(), 1));
        }

        [Test]
        public void ALaterWorldNeedsEveryLevelOfThePreviousOne()
        {
            Assert.IsFalse(SaveModel.WorldUnlocked(TwoWorlds(), Cleared("w1_01"), 2),
                           "one of two is not the whole world");
            Assert.IsTrue(SaveModel.WorldUnlocked(TwoWorlds(), Cleared("w1_01", "w1_02"), 2));
        }

        [Test]
        public void SkippingALevelStillBlocksTheNextWorld()
        {
            // GDD 15: progression tolerates skipped levels WITHIN a world, and the world gate
            // is the one place that intentionally does not.
            Assert.IsFalse(SaveModel.WorldUnlocked(TwoWorlds(), Cleared("w1_02"), 2));
        }

        [Test]
        public void AnOutOfRangeOrMalformedWorldIsLocked()
        {
            Assert.IsFalse(SaveModel.WorldUnlocked(TwoWorlds(), Cleared("w1_01", "w1_02"), 3));
            Assert.IsFalse(SaveModel.WorldUnlocked(null, new SaveData(), 1));
        }
    }
}
