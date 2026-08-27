using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;

namespace KeepyUppy.Shell.Save
{
    /// <summary>One completed run, as handed to <see cref="SaveModel.Merge"/>.</summary>
    public readonly struct RunResult
    {
        public readonly string LevelId;
        public readonly int Touches;
        public readonly int TimeFrames;

        /// <summary>The level's meta.par_touches. Zero or less means unset.</summary>
        public readonly int ParTouches;

        public RunResult(string levelId, int touches, int timeFrames, int parTouches)
        {
            LevelId = levelId;
            Touches = touches;
            TimeFrames = timeFrames;
            ParTouches = parTouches;
        }
    }

    /// <summary>
    /// The outcome of a merge: the new save, the record that changed, and whether this run
    /// beat anything — which is what the Results screen draws a BEST marker from.
    /// </summary>
    public readonly struct SaveResult
    {
        public readonly SaveData Data;
        public readonly LevelRecord Record;
        public readonly bool NewBestTouches;
        public readonly bool NewBestTime;

        public SaveResult(SaveData data, LevelRecord record, bool newBestTouches, bool newBestTime)
        {
            Data = data;
            Record = record;
            NewBestTouches = newBestTouches;
            NewBestTime = newBestTime;
        }
    }

    /// <summary>
    /// Every progression rule, as pure functions. No Unity types, no IO, no state — so all of
    /// it is reachable from an EditMode test without a scene, which is the whole reason the
    /// rules are not in ShellView.
    /// </summary>
    public static class SaveModel
    {
        /// <summary>How far over par still earns silver.</summary>
        public const int SilverSlack = 3;

        /// <summary>
        /// The touch-count medal (GDD 15). Three tiers rather than one because the gradient is
        /// the point: GDD 4.2 makes the fewest-touches line the most dangerous line in the
        /// game, and a player who missed gold by one touch needs something to aim at.
        ///
        /// A par of zero or less means UNSET — the normal state while a level is authored,
        /// since par comes from playtest metrics. An unset par can never award gold.
        /// </summary>
        public static Medal Grade(int touches, int parTouches)
        {
            if (parTouches <= 0) return Medal.Bronze;
            if (touches <= parTouches) return Medal.Gold;
            if (touches <= parTouches + SilverSlack) return Medal.Silver;

            return Medal.Bronze;
        }

        /// <summary>The stored record for a level, or null if it has never been cleared.</summary>
        public static LevelRecord RecordFor(SaveData data, string levelId)
        {
            if (data?.levels == null || string.IsNullOrEmpty(levelId)) return null;

            foreach (LevelRecord record in data.levels)
                if (record != null && record.id == levelId) return record;

            return null;
        }

        /// <summary>
        /// Folds a completed run into the save and returns the result.
        ///
        /// Each field takes its own best independently: a slow gold run and a fast bronze run
        /// both leave their mark, and the medal follows bestTouches. A first clear sets both
        /// and flags neither as a new best — there was nothing to beat.
        ///
        /// Never mutates its input: the caller's SaveData is the one already on screen, and a
        /// merge that edited it in place would change what is drawn before anything decided to
        /// keep it. Records for levels no longer in the manifest are copied through rather than
        /// pruned — an unlisted level is a draft (menus spec 1) and may well come back.
        /// </summary>
        public static SaveResult Merge(SaveData data, RunResult run)
        {
            var next = new SaveData { version = SaveData.CurrentVersion };
            LevelRecord existing = RecordFor(data, run.LevelId);

            var updated = new LevelRecord
            {
                id = run.LevelId,
                cleared = true,
                bestTouches = existing != null ? Min(existing.bestTouches, run.Touches) : run.Touches,
                bestTimeFrames = existing != null ? Min(existing.bestTimeFrames, run.TimeFrames) : run.TimeFrames,
            };

            bool newBestTouches = existing != null && run.Touches < existing.bestTouches;
            bool newBestTime = existing != null && run.TimeFrames < existing.bestTimeFrames;

            // The medal describes the best touch run, so it is only re-graded when that run
            // changes - a first clear included.
            updated.medal = existing == null || newBestTouches
                ? (int)Grade(updated.bestTouches, run.ParTouches)
                : existing.medal;

            var levels = new System.Collections.Generic.List<LevelRecord>();
            if (data?.levels != null)
            {
                foreach (LevelRecord record in data.levels)
                {
                    if (record == null || record.id == run.LevelId) continue;

                    levels.Add(new LevelRecord
                    {
                        id = record.id,
                        cleared = record.cleared,
                        bestTouches = record.bestTouches,
                        bestTimeFrames = record.bestTimeFrames,
                        medal = record.medal,
                    });
                }
            }

            levels.Add(updated);
            next.levels = levels.ToArray();

            return new SaveResult(next, updated, newBestTouches, newBestTime);
        }

        static int Min(int a, int b) => a < b ? a : b;

        /// <summary>
        /// Frames as M:SS.CC. Frames are the stored unit (SimClock is the only measure of time
        /// in this project); this is the only place they become something to read.
        /// </summary>
        public static string FormatTime(int frames)
        {
            if (frames < 0) frames = 0;

            int totalHundredths = frames * 100 / SimClock.Hz;
            int hundredths = totalHundredths % 100;
            int totalSeconds = totalHundredths / 100;
            int seconds = totalSeconds % 60;
            int minutes = totalSeconds / 60;

            return $"{minutes}:{seconds:00}.{hundredths:00}";
        }

        /// <summary>
        /// World 1 is always open; world N opens when every level of world N-1 is cleared
        /// (menus spec 2). Derived, never stored — a stored unlock is a second source of truth
        /// that can disagree with the records it was derived from.
        ///
        /// NOTHING CALLS THIS YET. Gating is deliberately out of scope for phase C (spec 6):
        /// there is one world to gate today. It is written here because this is where a reader
        /// looks for it and because its skipped-level behaviour is worth pinning down while
        /// the rest is fresh. If gating never arrives, deleting this costs nothing.
        /// </summary>
        public static bool WorldUnlocked(LevelIndex index, SaveData data, int worldNumber)
        {
            if (index?.worlds == null) return false;
            if (worldNumber < 1 || worldNumber > index.worlds.Length) return false;
            if (worldNumber == 1) return true;

            LevelWorld previous = index.worlds[worldNumber - 2];
            if (previous?.levels == null) return true;   // nothing to clear, nothing to block

            foreach (string id in previous.levels)
            {
                LevelRecord record = RecordFor(data, id);
                if (record == null || !record.cleared) return false;
            }

            return true;
        }
    }
}
