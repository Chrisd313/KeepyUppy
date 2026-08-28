using KeepyUppy.Shell.Save;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Loading, writing, and every way the stored blob can be wrong. PlayerPrefsSaveStore has
    /// no test of its own — isolating those fifteen lines is what ISaveStore is for.
    /// </summary>
    public sealed class SaveServiceTests
    {
        [Test]
        public void AFirstBootWithNothingStoredStartsEmpty()
        {
            var service = new SaveService(new MemorySaveStore());

            Assert.IsNotNull(service.Data);
            Assert.AreEqual(0, service.Data.levels.Length);
            Assert.AreEqual(SaveData.CurrentVersion, service.Data.version);
        }

        [Test]
        public void AClearIsWrittenThroughAndReadsBackOnTheNextBoot()
        {
            var store = new MemorySaveStore();

            new SaveService(store).RecordClear(new RunResult("w1_01", 6, 480, 7));

            LevelRecord record = new SaveService(store).RecordFor("w1_01");
            Assert.IsNotNull(record, "the clear survived the round trip");
            Assert.AreEqual(6, record.bestTouches);
            Assert.AreEqual(480, record.bestTimeFrames);
            Assert.AreEqual((int)Medal.Gold, record.medal);
        }

        [Test]
        public void RecordClearReturnsTheMergeResultForTheResultsScreen()
        {
            var service = new SaveService(new MemorySaveStore());
            service.RecordClear(new RunResult("w1_01", 9, 600, 7));

            SaveResult result = service.RecordClear(new RunResult("w1_01", 5, 600, 7));

            Assert.IsTrue(result.NewBestTouches);
            Assert.IsFalse(result.NewBestTime);
        }

        [Test]
        public void EveryWriteAdvancesTheGeneration()
        {
            // ShellView redraws only when something visible changed, so it needs a value that
            // moves when the save does - otherwise the level just cleared still reads as
            // unplayed on the grid behind it.
            var service = new SaveService(new MemorySaveStore());
            int before = service.Generation;

            service.RecordClear(new RunResult("w1_01", 6, 480, 7));

            Assert.AreNotEqual(before, service.Generation);
        }

        [Test]
        public void MalformedJsonYieldsAFreshSaveRatherThanThrowing()
        {
            var store = new MemorySaveStore { Contents = "{ this is not json" };

            SaveService service = null;
            Assert.DoesNotThrow(() => service = new SaveService(store));
            Assert.AreEqual(0, service.Data.levels.Length);
        }

        [Test]
        public void AVersionFromTheFutureYieldsAFreshSave()
        {
            // Better a lost record than a crash on a downgrade. The version field exists so a
            // later schema change can do better than this.
            var store = new MemorySaveStore
            {
                Contents = "{\"version\":99,\"levels\":[{\"id\":\"w1_01\",\"cleared\":true}]}",
            };

            var service = new SaveService(store);

            Assert.AreEqual(0, service.Data.levels.Length);
        }

        [Test]
        public void AWipeClearsTheStoreAndTheDataInMemory()
        {
            var store = new MemorySaveStore();
            var service = new SaveService(store);
            service.RecordClear(new RunResult("w1_01", 6, 480, 7));

            service.Wipe();

            Assert.AreEqual(0, service.Data.levels.Length);
            Assert.IsNull(new SaveService(store).RecordFor("w1_01"), "and it stayed wiped");
        }
    }
}
