using System.Collections.Generic;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// Where tuning profiles come from. Mirrors the ILevelSource split for the same reason:
    /// StreamingAssets supports File IO on desktop and not on web or Android, so the storage
    /// mechanism has to be swappable without the caller knowing.
    ///
    /// The interface lives here so the sim can be handed a profile; no implementation in
    /// Sim/ ever touches the file system.
    /// </summary>
    public interface ITuningSource
    {
        /// <summary>False for packed, read-only sources. The tuning panel hides Save when false.</summary>
        bool CanSave { get; }

        /// <summary>Profile names available, without extension. Sorted, for a stable UI order.</summary>
        IReadOnlyList<string> List();

        bool TryLoad(string name, out TuningProfile profile);

        /// <summary>Writes the profile. Throws if <see cref="CanSave"/> is false.</summary>
        void Save(string name, TuningProfile profile);
    }
}
