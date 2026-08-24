using System.Collections.Generic;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Manifest lint (spec: menus and the level list).
    ///
    /// A declared list beats a derived one only while the tooling keeps it current. These are
    /// the checks that catch it going stale - a level listed but deleted, a file listed twice,
    /// a level file whose own fields contradict where the manifest puts it.
    ///
    /// **Warnings, never errors.** A half-finished level set must stay saveable, exactly as
    /// LevelValidator treats a half-finished level.
    /// </summary>
    public static class LevelIndexValidator
    {
        /// <summary>
        /// Checks the manifest against what is actually on disk.
        /// </summary>
        /// <param name="onDisk">Level ids from ILevelSource.List(), which excludes the manifest.</param>
        public static List<string> Validate(LevelIndex index, IReadOnlyList<string> onDisk)
        {
            var warnings = new List<string>();

            if (index == null) { warnings.Add("no manifest"); return warnings; }

            if (index.version > LevelIndex.CurrentVersion)
            {
                warnings.Add($"manifest version {index.version} is newer than this build " +
                             $"understands ({LevelIndex.CurrentVersion})");
            }

            if (index.worlds == null || index.worlds.Length == 0)
            {
                warnings.Add("manifest lists no worlds");
                return warnings;
            }

            var seen = new HashSet<string>();
            var duplicates = new List<string>();

            foreach (LevelWorld world in index.worlds)
            {
                if (world == null) { warnings.Add("manifest has an empty world entry"); continue; }

                if (string.IsNullOrEmpty(world.id)) warnings.Add("a world has no id");

                if (world.levels == null || world.levels.Length == 0)
                {
                    warnings.Add($"world '{world.id}' has no levels");
                    continue;
                }

                foreach (string id in world.levels)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        warnings.Add($"world '{world.id}' has an empty level id");
                        continue;
                    }

                    if (!seen.Add(id)) duplicates.Add(id);

                    if (onDisk != null && !Contains(onDisk, id))
                        warnings.Add($"'{id}' is listed in world '{world.id}' but no such level file exists");
                }
            }

            // Sorted so the warning list is stable between runs, as LevelValidator does.
            duplicates.Sort(System.StringComparer.Ordinal);
            foreach (string id in duplicates) warnings.Add($"'{id}' is listed more than once");

            foreach (string draft in index.DraftsAmong(onDisk))
                warnings.Add($"'{draft}' is on disk but not in the manifest (draft)");

            return warnings;
        }

        /// <summary>
        /// Checks one level file's advisory world and index fields against where the manifest
        /// actually puts it. An unlisted level is a draft and has no slot to drift from.
        /// </summary>
        public static List<string> ValidateSlot(LevelData level, LevelIndex index)
        {
            var warnings = new List<string>();
            if (level == null || index == null) return warnings;

            int slot = index.SlotOf(level.id);
            if (slot < 0) return warnings;

            int world = index.WorldNumberOf(level.id);

            if (level.world != world)
                warnings.Add($"'{level.id}' says world {level.world} but the manifest puts it in world {world}");

            // Slots are 0-based; the schema's index field is 1-based, as the filenames imply.
            if (level.index != slot + 1)
                warnings.Add($"'{level.id}' says index {level.index} but the manifest puts it at {slot + 1}");

            return warnings;
        }

        static bool Contains(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == id) return true;

            return false;
        }
    }
}
