using UnityEngine;

namespace KeepyUppy.Shell.Save
{
    /// <summary>
    /// Holds the save in memory and is the only thing that touches an <see cref="ISaveStore"/>.
    /// Loaded once at construction; written on a clear and on a wipe, and nowhere else — a
    /// completed level is the only event that changes persisted state.
    ///
    /// Anything the reader cannot understand — malformed JSON, or a version from the future —
    /// becomes a fresh save rather than an exception. A lost record beats a boot that throws,
    /// and the version field is there so a later schema change can do better than either.
    /// </summary>
    public sealed class SaveService
    {
        readonly ISaveStore _store;

        public SaveData Data { get; private set; }

        /// <summary>
        /// Bumped on every write. The shell redraws only when something visible changed, so it
        /// needs one value that moves when the save does.
        /// </summary>
        public int Generation { get; private set; }

        public SaveService(ISaveStore store)
        {
            _store = store;
            Data = Load(store);
        }

        static SaveData Load(ISaveStore store)
        {
            string json = store?.Read();
            if (string.IsNullOrEmpty(json)) return new SaveData();

            SaveData loaded;
            try
            {
                loaded = JsonUtility.FromJson<SaveData>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[save] unreadable, starting fresh: {e.Message}");
                return new SaveData();
            }

            if (loaded == null) return new SaveData();

            if (loaded.version > SaveData.CurrentVersion)
            {
                Debug.LogWarning($"[save] version {loaded.version} is newer than this build's " +
                                 $"{SaveData.CurrentVersion}; starting fresh");
                return new SaveData();
            }

            loaded.levels ??= new LevelRecord[0];
            return loaded;
        }

        public LevelRecord RecordFor(string levelId) => SaveModel.RecordFor(Data, levelId);

        /// <summary>
        /// Folds a completed run in and writes. Returns the merge result so the caller can draw
        /// whether anything was beaten, which is only knowable here.
        /// </summary>
        public SaveResult RecordClear(RunResult run)
        {
            SaveResult result = SaveModel.Merge(Data, run);
            Data = result.Data;
            Flush();

            return result;
        }

        /// <summary>Dev-only, and only ever reached from a LEVEL_EDITOR-gated call site.</summary>
        public void Wipe()
        {
            Data = new SaveData();
            Flush();
        }

        void Flush()
        {
            Generation++;
            _store?.Write(JsonUtility.ToJson(Data));
        }
    }
}
