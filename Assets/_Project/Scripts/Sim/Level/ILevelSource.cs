using System.Collections.Generic;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Where levels come from (CLAUDE.md).
    ///
    /// StreamingAssets supports File IO on desktop and not on web or Android — on web the
    /// path is a URL needing UnityWebRequest, on Android it lives inside the compressed APK.
    /// So the storage mechanism has to be swappable without the caller knowing.
    ///
    ///   FileLevelSource   — desktop/editor, read and write. The editor only ever runs here.
    ///   PackedLevelSource — web/mobile, read-only, from a build-time manifest. Not yet built.
    ///
    /// JSON on disk is the source of truth. The packed manifest is a build artefact and is
    /// never hand-edited.
    /// </summary>
    public interface ILevelSource
    {
        /// <summary>False for packed, read-only sources. The editor refuses to run against those.</summary>
        bool CanSave { get; }

        /// <summary>Level ids available, without extension. Sorted, for a stable order.</summary>
        IReadOnlyList<string> List();

        bool TryLoad(string id, out LevelData level);

        /// <summary>Writes the level. Throws if <see cref="CanSave"/> is false.</summary>
        void Save(string id, LevelData level);

        /// <summary>
        /// The manifest declaring which levels are in the game and in what order.
        ///
        /// False when there is no manifest yet, which is a normal state for a project with
        /// levels and no list, not a failure.
        /// </summary>
        bool TryLoadIndex(out LevelIndex index);

        /// <summary>Writes the manifest. Throws if <see cref="CanSave"/> is false.</summary>
        void SaveIndex(LevelIndex index);
    }
}
