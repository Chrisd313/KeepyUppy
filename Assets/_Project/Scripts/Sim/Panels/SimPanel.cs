using UnityEngine;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>
    /// A rectangle of authored tiles a link can take away (GDD 9.3's falling floor).
    ///
    /// The tiles belong to the level; a panel only decides whether they are currently there.
    /// Config is copied out of the level data at load, so nothing here reads JSON.
    /// </summary>
    public sealed class SimPanel
    {
        public string Id;

        /// <summary>Tile coordinates. A panel is always whole tiles — the mask indexes cells.</summary>
        public RectInt Rect;

        // --- Run state: cleared by Reset(), covered by the replay-equality test ---

        /// <summary>Whether its tiles are currently present.</summary>
        public bool Enabled = true;

        /// <summary>What Enabled returns to on a restart.</summary>
        public bool StartEnabled = true;
    }
}
