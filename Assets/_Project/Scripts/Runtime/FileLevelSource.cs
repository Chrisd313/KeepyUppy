using System;
using System.Collections.Generic;
using System.IO;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Levels as JSON files under StreamingAssets/levels/ (CLAUDE.md).
    ///
    /// Desktop and editor only — direct File IO, read and write. The level editor only ever
    /// runs against this; web and mobile get a read-only packed source.
    ///
    /// Pretty-printed, stable key order, never minified: the readable diff is the whole point
    /// of the format. You will want to see exactly what changed when a level stops being fun.
    /// </summary>
    public sealed class FileLevelSource : ILevelSource
    {
        readonly string _directory;

        public FileLevelSource(string directory = null)
        {
            _directory = directory ?? Path.Combine(Application.streamingAssetsPath, "levels");
        }

        public string Directory => _directory;

        public bool CanSave => true;

        public IReadOnlyList<string> List()
        {
            if (!System.IO.Directory.Exists(_directory)) return Array.Empty<string>();

            string[] files = System.IO.Directory.GetFiles(_directory, "*.json");
            var ids = new List<string>(files.Length);
            foreach (string file in files) ids.Add(Path.GetFileNameWithoutExtension(file));

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        public bool TryLoad(string id, out LevelData level)
        {
            level = null;

            string path = PathFor(id);
            if (!File.Exists(path)) return false;

            try
            {
                level = JsonUtility.FromJson<LevelData>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                // A corrupt level must never take the game down mid-playtest.
                Debug.LogWarning($"[level] could not read '{id}': {e.Message}");
                level = null;
                return false;
            }

            if (level == null) return false;

            level.id = id;
            return true;
        }

        public void Save(string id, LevelData level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));

            System.IO.Directory.CreateDirectory(_directory);

            level.id = id;
            File.WriteAllText(PathFor(id), JsonUtility.ToJson(level, prettyPrint: true));
        }

        /// <summary>Writes only if absent, so a seeded level is correct by construction.</summary>
        public bool SaveIfAbsent(string id, LevelData level)
        {
            if (File.Exists(PathFor(id))) return false;

            Save(id, level);
            return true;
        }

        string PathFor(string id) => Path.Combine(_directory, id + ".json");
    }
}
