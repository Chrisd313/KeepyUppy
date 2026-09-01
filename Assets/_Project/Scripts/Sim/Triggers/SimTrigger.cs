using UnityEngine;

namespace KeepyUppy.Sim.Triggers
{
    /// <summary>
    /// What a trigger watches. The schema carries a free-form string, because a level file
    /// outlives any one build; this is the subset with behaviour today. Anything else loads
    /// as <see cref="Unknown"/>, is counted, and never fires.
    /// </summary>
    public enum TriggerKind
    {
        Unknown = 0,
        PlayerRegion,
        BalloonRegion,
        Timer,
    }

    /// <summary>
    /// One trigger, in the form the simulation runs it in (GDD 9.3).
    ///
    /// Config is copied out of the level data at load, so nothing here reads JSON and the sim
    /// stays testable without a file.
    /// </summary>
    public sealed class SimTrigger
    {
        // --- Identity and config: set at load, never mutated by the tick ---

        public string Id;
        public TriggerKind Kind;

        /// <summary>Tile-aligned, in metres, bottom-left origin — as AirZone.</summary>
        public Rect Rect;

        /// <summary>Latches after the first fire. The GDD 9.3 betrayal workhorse.</summary>
        public bool Once = true;

        /// <summary>Drawn to the player, not only to the author.</summary>
        public bool Visible;

        /// <summary>Timer only: the step it fires on. Converted from seconds at load.</summary>
        public int AtStep;

        // --- Run state: cleared by Reset(), covered by the replay-equality test ---

        /// <summary>Whether the condition held last step. This is what makes firing an edge.</summary>
        public bool WasInside;

        public bool Fired;

        public void Reset()
        {
            WasInside = false;
            Fired = false;
        }
    }
}
