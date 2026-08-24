using System;

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
