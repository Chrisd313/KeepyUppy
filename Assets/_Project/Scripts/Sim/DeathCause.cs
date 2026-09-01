namespace KeepyUppy.Sim
{
    /// <summary>
    /// Why a run ended. Recorded rather than branched on: GDD 4.1's linked fate means every
    /// cause produces the same outcome — one explosion, one sound, one restart.
    /// </summary>
    public enum DeathCause
    {
        None = 0,

        /// <summary>Gravity won. The balloon touched an upward-facing surface.</summary>
        BalloonHitFloor = 1,

        /// <summary>Spikes, rough geometry — anything with a pop volume.</summary>
        BalloonHitHazard = 2,

        /// <summary>The player touched explicitly lethal geometry (GDD 4.1 asymmetric fragility).</summary>
        PlayerHitHazard = 3,

        /// <summary>The player left the room. Should be impossible in a walled level; a safety net.</summary>
        PlayerFellOut = 4,

        /// <summary>A panel's tiles came back around a body (tile-panels design).</summary>
        Crushed = 5,
    }
}
