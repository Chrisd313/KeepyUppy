namespace KeepyUppy.Sim.Entities
{
    /// <summary>
    /// The entity types the simulation can actually run (GDD 9.2).
    ///
    /// The level schema carries a free-form type string, because a level file outlives any
    /// one build and must survive round-tripping through an editor that does not understand
    /// every type in it. This enum is the subset that has behaviour today; anything else
    /// loads as <see cref="Unknown"/>, is counted, and is left alone.
    /// </summary>
    public enum EntityKind
    {
        /// <summary>Parsed, preserved, not simulated. Never silently dropped.</summary>
        Unknown = 0,

        /// <summary>Path-following blade. Lethal to both (GDD 9.1 generic tier).</summary>
        Saw,

        /// <summary>Heat lamp / sunbeam. Sustained exposure pops the balloon (GDD 9.2).</summary>
        Lamp,

        /// <summary>Owns an air zone. Hazard when on, tool when switchable (GDD 9.2).</summary>
        Fan,
    }

    /// <summary>How an entity walks its path. Maps to the schema's path_mode.</summary>
    public enum PathMode
    {
        PingPong = 0,
        Loop,
        Once,
    }

    /// <summary>
    /// Per-type fallbacks for schema params.
    ///
    /// JsonUtility writes every field of <c>EntityParams</c>, so an unset param arrives as 0
    /// rather than absent. A zero radius would be an invisible hazard and a zero speed a saw
    /// that never moves, so 0 means "not set" and these apply. The editor shows the same
    /// numbers, so what you place is what runs.
    /// </summary>
    public static class EntityDefaults
    {
        public const float SawRadius = 0.6f;
        public const float SawSpeed = 3.0f;

        public const float LampRadius = 0.5f;
        public const float LampLength = 5.0f;
        public const float LampAngle = 270f;

        /// <summary>Seconds of unbroken exposure before the balloon pops. GDD 9.2 says ~3 s.</summary>
        public const float LampDuration = 3.0f;

        public const float FanRadius = 1.0f;
        public const float FanLength = 6.0f;
        public const float FanPower = 8.0f;
        public const float FanAngle = 90f;

        public static float Or(float value, float fallback) => value > 0f ? value : fallback;
    }
}
