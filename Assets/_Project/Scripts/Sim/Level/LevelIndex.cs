using System;
using System.Collections.Generic;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Which levels are in the game, and in what order (spec: menus and the level list).
    ///
    /// **The manifest is authoritative for ordering.** The world and index fields inside each
    /// level file are advisory and lint-checked against this; nothing reads them to decide
    /// what comes next.
    ///
    /// A level file on disk but absent from here is a DRAFT: the editor opens it, the game
    /// does not list it. That capability is the reason this file exists rather than the game
    /// deriving its list by scanning the directory.
    ///
    /// Modelled for JsonUtility - an array of objects each holding a string[], with no nested
    /// arrays and no dictionaries, the same constraints LevelData works within.
    /// </summary>
    [Serializable]
    public sealed class LevelIndex
    {
        /// <summary>Bumped only when the shape changes in a way a reader must handle.</summary>
        public const int CurrentVersion = 1;

        /// <summary>The file this lives in, without extension. Excluded from level listings.</summary>
        public const string FileName = "index";

        public int version = CurrentVersion;
        public LevelWorld[] worlds = new LevelWorld[0];

        /// <summary>
        /// The manifest a fresh project starts with. Must name the level SimBootstrap seeds,
        /// or a new clone boots into an empty game.
        /// </summary>
        public static LevelIndex Default() => new LevelIndex
        {
            version = CurrentVersion,
            worlds = new[]
            {
                new LevelWorld
                {
                    id = "w1",
                    name = "The Living Room",
                    levels = new[] { "w1_01" },
                },
            },
        };

        /// <summary>Every listed level, in play order, across all worlds.</summary>
        public IReadOnlyList<string> AllLevels()
        {
            var all = new List<string>();
            if (worlds == null) return all;

            foreach (LevelWorld world in worlds)
            {
                if (world?.levels == null) continue;

                foreach (string id in world.levels)
                    if (!string.IsNullOrEmpty(id)) all.Add(id);
            }

            return all;
        }

        public bool Contains(string id) => WorldOf(id) != null;

        /// <summary>The world listing this level, or null. Malformed entries are skipped.</summary>
        public LevelWorld WorldOf(string id)
        {
            if (string.IsNullOrEmpty(id) || worlds == null) return null;

            foreach (LevelWorld world in worlds)
            {
                if (world?.levels == null) continue;

                foreach (string listed in world.levels)
                    if (listed == id) return world;
            }

            return null;
        }

        /// <summary>Position within its own world, 0-based. -1 when unlisted.</summary>
        public int SlotOf(string id)
        {
            LevelWorld world = WorldOf(id);
            if (world == null) return -1;

            for (int i = 0; i < world.levels.Length; i++)
                if (world.levels[i] == id) return i;

            return -1;
        }

        /// <summary>
        /// Which world this is, counting from 1 - the number a level file's `world` field is
        /// expected to match, and what the lint compares against.
        /// </summary>
        public int WorldNumberOf(string id)
        {
            LevelWorld owner = WorldOf(id);
            if (owner == null) return -1;

            for (int i = 0; i < worlds.Length; i++)
                if (ReferenceEquals(worlds[i], owner)) return i + 1;

            return -1;
        }

        /// <summary>
        /// The level after this one in the same world, or null.
        ///
        /// Deliberately does not roll into the next world at a world's end: crossing worlds
        /// is a progression question that belongs with save data (GDD 15), not with "what is
        /// the next room". An unlisted id — a draft — has no answer either.
        /// </summary>
        public string NextAfter(string id)
        {
            LevelWorld world = WorldOf(id);
            if (world == null) return null;

            int slot = SlotOf(id);
            int next = slot + 1;

            return next >= 0 && next < world.levels.Length ? world.levels[next] : null;
        }

        /// <summary>
        /// Files present on disk that the manifest does not list - drafts. Order follows the
        /// input, so a sorted listing in gives a sorted listing out.
        /// </summary>
        public List<string> DraftsAmong(IReadOnlyList<string> onDisk)
        {
            var drafts = new List<string>();
            if (onDisk == null) return drafts;

            foreach (string id in onDisk)
            {
                if (string.IsNullOrEmpty(id) || id == FileName) continue;
                if (!Contains(id)) drafts.Add(id);
            }

            return drafts;
        }
    }

    /// <summary>One world and its levels, in play order (GDD 12).</summary>
    [Serializable]
    public sealed class LevelWorld
    {
        public string id = "w1";
        public string name = "";
        public string[] levels = new string[0];
    }
}
