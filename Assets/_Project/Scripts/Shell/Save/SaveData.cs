using System;

namespace KeepyUppy.Shell.Save
{
    /// <summary>
    /// Everything that survives closing the game. One blob, written as JSON into a single
    /// PlayerPrefs key (spec: progression and save data, 3).
    ///
    /// Modelled for JsonUtility under the same constraints LevelData and LevelIndex work
    /// within: a flat array of objects, no nested arrays, no dictionaries.
    ///
    /// Death counts are deliberately absent. GDD 15 wants them displayed and never hidden,
    /// but says nothing about keeping them, and a per-session count is the more useful number
    /// while levels are being tuned.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>Bumped only when the shape changes in a way a reader must handle.</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public LevelRecord[] levels = new LevelRecord[0];
    }

    /// <summary>
    /// One level's best. Times are FRAMES, not seconds: integers, exactly reproducible, and
    /// it keeps SimClock the only measure of time in the project.
    /// </summary>
    [Serializable]
    public sealed class LevelRecord
    {
        public string id = "";
        public bool cleared;
        public int bestTouches;
        public int bestTimeFrames;

        /// <summary>
        /// The medal earned by the run that set <see cref="bestTouches"/>, graded at win time
        /// when the level's par was in hand. Stored rather than re-derived because par lives
        /// inside each level file, and grading in the menu would mean parsing every level in
        /// the game to draw it. The accepted cost: re-tuning a par does not re-grade records
        /// until the level is played again.
        /// </summary>
        public int medal;
    }

    /// <summary>The touch-count medal (GDD 15). Ordered, so a later tier is a better one.</summary>
    public enum Medal
    {
        None = 0,
        Bronze = 1,
        Silver = 2,
        Gold = 3,
    }
}
