using System.Collections.Generic;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>
    /// Every panel in the room, and the tile mask they add up to.
    ///
    /// A List, never a Dictionary: iteration order decides which panel wins a tie when two
    /// switch on the same step, and unordered iteration is on CLAUDE.md's list of the things
    /// that break GDD 6.1 silently.
    ///
    /// The mask is REBUILT from the flags rather than toggled cell by cell. That is what makes
    /// overlapping panels compose for free — a cell is hidden if any panel covering it is off —
    /// and it is why a restart costs one pass over a handful of rects rather than per-cell
    /// bookkeeping (GDD 8's 200 ms budget).
    /// </summary>
    public sealed class PanelField
    {
        readonly List<SimPanel> _panels = new List<SimPanel>();

        /// <summary>Indices of panels whose tiles came back this step. SimWorld reads it to crush.</summary>
        public readonly List<int> TurnedOnThisStep = new List<int>();

        /// <summary>Whether a flag has changed since the mask was last built.</summary>
        public bool Dirty { get; private set; }

        public int Count => _panels.Count;
        public SimPanel this[int i] => _panels[i];

        public void Clear()
        {
            _panels.Clear();
            TurnedOnThisStep.Clear();
            Dirty = true;
        }

        public void Add(SimPanel panel)
        {
            if (panel == null) return;

            _panels.Add(panel);
            Dirty = true;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _panels.Count; i++)
                if (_panels[i].Id == id) return i;

            return -1;
        }

        public SimPanel Find(string id)
        {
            int index = IndexOf(id);
            return index < 0 ? null : _panels[index];
        }

        /// <summary>Clears the per-step record. Called once per tick before triggers run.</summary>
        public void BeginStep() => TurnedOnThisStep.Clear();

        /// <summary>
        /// Switches a panel. Only an off-to-on edge is recorded, because that is the one that
        /// can catch a body inside returning geometry; tiles leaving never hurt anyone.
        /// </summary>
        public void SetEnabled(string id, bool enabled)
        {
            int index = IndexOf(id);
            if (index < 0) return;

            SimPanel panel = _panels[index];
            if (panel.Enabled == enabled) return;

            if (enabled) TurnedOnThisStep.Add(index);

            panel.Enabled = enabled;
            Dirty = true;
        }

        /// <summary>GDD 8: part of the synchronous restart.</summary>
        public void Reset()
        {
            for (int i = 0; i < _panels.Count; i++) _panels[i].Reset();

            TurnedOnThisStep.Clear();
            Dirty = true;
        }

        /// <summary>Rebuilds the grid's mask from the current flags.</summary>
        public void Apply(TileGrid grid)
        {
            if (grid == null) return;

            grid.ClearMask();

            for (int i = 0; i < _panels.Count; i++)
            {
                SimPanel panel = _panels[i];
                if (!panel.Enabled) grid.SetMasked(panel.Rect, true);
            }

            Dirty = false;
        }
    }
}
