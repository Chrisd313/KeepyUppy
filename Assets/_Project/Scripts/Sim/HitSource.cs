namespace KeepyUppy.Sim
{
    /// <summary>
    /// What struck the balloon (GDD 5.1). Adding or removing a verb is adding or removing a
    /// case here plus a row in <see cref="ControlProfiles"/> — never new branching scattered
    /// through the codebase. That is what makes the GDD 5.3 control experiment possible.
    /// </summary>
    public enum HitSource
    {
        /// <summary>Jump contact. Aimed positionally (GDD 6.5), available in every profile.</summary>
        JumpBump = 0,

        /// <summary>Light directional tap. Profile B only.</summary>
        Palm = 1,

        /// <summary>Charged strike. Profile B only.</summary>
        Spike = 2,

        /// <summary>
        /// The balloon landing on a player who is not doing anything. LOCKED GDD 4.2:
        /// this always loses energy.
        /// </summary>
        PassiveContact = 3,
    }
}
