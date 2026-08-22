using System;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// A named set of tuning values (level-editor-spec 6, GDD 18).
    ///
    /// A class rather than a struct because JsonUtility will not serialise a struct at the
    /// top level. Field order is the JSON key order, so profiles produce readable diffs.
    ///
    /// JSON on disk is the source of truth; <see cref="Default"/> is the seed used when no
    /// profile exists yet.
    /// </summary>
    [Serializable]
    public sealed class TuningProfile
    {
        /// <summary>Profile name, matching the file stem. Informational — the file wins.</summary>
        public string name = "default";

        public BalloonTuning balloon = BalloonTuning.Default;
        public PlayerTuning player = PlayerTuning.Default;

        public static TuningProfile Default => new TuningProfile
        {
            name = "default",
            balloon = BalloonTuning.Default,
            player = PlayerTuning.Default,
        };

        public TuningProfile Clone() => new TuningProfile
        {
            name = name,
            balloon = balloon,
            player = player,
        };
    }
}
