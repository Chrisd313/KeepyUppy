using System.Collections.Generic;
using UnityEngine;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>One moving panel, as the sweeps see it this step.</summary>
    public struct MovingSolid
    {
        /// <summary>Where it is now, in metres.</summary>
        public Rect rect;

        /// <summary>Tiles per second this step. Zero for a panel that is parked.</summary>
        public Vector2 velocity;

        /// <summary>Index into PanelField, so a hit can be traced back to its tiles.</summary>
        public int panel;
    }

    /// <summary>
    /// The moving panels, in the form the swept collision reads them (moving-panels design).
    ///
    /// A List, never a Dictionary: iteration order decides which solid a body resolves
    /// against first when two overlap it on the same step, and unordered iteration is on
    /// CLAUDE.md's list of the things that break GDD 6.1 silently.
    ///
    /// Rebuilt from the panels each step rather than mutated in place, for the same reason
    /// PanelField rebuilds its mask: there is then no per-solid bookkeeping that a restart
    /// can leave half-undone.
    /// </summary>
    public sealed class SolidField
    {
        readonly List<MovingSolid> _solids = new List<MovingSolid>();

        public int Count => _solids.Count;
        public MovingSolid this[int i] => _solids[i];

        public void Clear() => _solids.Clear();

        /// <summary>Republishes every enabled moving panel at its current position.</summary>
        public void Rebuild(PanelField panels)
        {
            _solids.Clear();
            if (panels == null) return;

            for (int i = 0; i < panels.Count; i++)
            {
                SimPanel panel = panels[i];

                // A switched-off panel is not a solid anywhere — "off" means its tiles are
                // absent, exactly as it does for a mask panel.
                if (!panel.IsMoving || !panel.Enabled) continue;

                _solids.Add(new MovingSolid
                {
                    rect = panel.WorldRect,
                    velocity = panel.Velocity,
                    panel = i,
                });
            }
        }
    }
}
