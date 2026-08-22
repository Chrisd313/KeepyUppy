using System;
using System.Collections.Generic;
using System.IO;
using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Tuning profiles as JSON files under StreamingAssets/levels/_tuning/ (CLAUDE.md).
    ///
    /// Desktop and editor only — direct File IO, read and write. StreamingAssets is a URL on
    /// WebGL and lives inside the compressed APK on Android, so a packed read-only source
    /// takes over there. Which is why callers hold an ITuningSource, not this.
    ///
    /// Pretty-printed with stable key order, because these files are committed and the diffs
    /// are the point.
    /// </summary>
    public sealed class FileTuningSource : ITuningSource
    {
        public const string DefaultProfileName = "default";

        readonly string _directory;

        public FileTuningSource(string directory = null)
        {
            _directory = directory ?? Path.Combine(Application.streamingAssetsPath, "levels", "_tuning");
        }

        public string Directory => _directory;

        public bool CanSave => true;

        public IReadOnlyList<string> List()
        {
            if (!System.IO.Directory.Exists(_directory)) return Array.Empty<string>();

            string[] files = System.IO.Directory.GetFiles(_directory, "*.json");
            var names = new List<string>(files.Length);
            foreach (string file in files) names.Add(Path.GetFileNameWithoutExtension(file));

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        public bool TryLoad(string name, out TuningProfile profile)
        {
            profile = null;

            string path = PathFor(name);
            if (!File.Exists(path)) return false;

            try
            {
                string json = File.ReadAllText(path);
                profile = JsonUtility.FromJson<TuningProfile>(json);
            }
            catch (Exception e)
            {
                // A corrupt profile must never take the game down mid-playtest.
                Debug.LogWarning($"[tuning] could not read '{name}': {e.Message}");
                profile = null;
                return false;
            }

            if (profile == null) return false;

            profile.name = name;
            return true;
        }

        public void Save(string name, TuningProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            System.IO.Directory.CreateDirectory(_directory);

            profile.name = name;
            File.WriteAllText(PathFor(name), JsonUtility.ToJson(profile, prettyPrint: true));
        }

        /// <summary>
        /// Writes the profile only if it does not already exist. Used to seed a correct
        /// default.json from the code defaults rather than hand-writing one that could drift.
        /// </summary>
        public bool SaveIfAbsent(string name, TuningProfile profile)
        {
            if (File.Exists(PathFor(name))) return false;

            Save(name, profile);
            return true;
        }

        string PathFor(string name) => Path.Combine(_directory, Sanitise(name) + ".json");

        /// <summary>Profile names become file names, so keep them boring.</summary>
        public static string Sanitise(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return DefaultProfileName;

            // Anything not allowed in a file name becomes a separator rather than vanishing,
            // so "chris/2026-08-22" reads as "chris-2026-08-22" and not "chris2026-08-22".
            var chars = new List<char>(name.Length);
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') chars.Add(c);
                else if (chars.Count > 0 && chars[chars.Count - 1] != '-') chars.Add('-');
            }

            while (chars.Count > 0 && chars[chars.Count - 1] == '-') chars.RemoveAt(chars.Count - 1);

            return chars.Count == 0 ? DefaultProfileName : new string(chars.ToArray());
        }
    }
}
