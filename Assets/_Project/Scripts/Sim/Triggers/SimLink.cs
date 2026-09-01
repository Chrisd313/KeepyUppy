namespace KeepyUppy.Sim.Triggers
{
    /// <summary>
    /// What a link does to its targets. The schema also declares arm and fire; they parse as
    /// <see cref="Inert"/> until something needs them, rather than being dropped.
    /// </summary>
    public enum LinkAction
    {
        Inert = 0,
        On,
        Off,
        Toggle,
    }

    /// <summary>
    /// One resolved link (level-editor-spec 3).
    ///
    /// The source is an index rather than an id: it is resolved once at load, so the tick
    /// never does a string comparison to find out who fired.
    /// </summary>
    public sealed class SimLink
    {
        /// <summary>Index into the trigger list. -1 means the source id matched nothing.</summary>
        public int FromIndex = -1;

        /// <summary>Entity or zone ids. Applied in order, so two links agree on precedence.</summary>
        public string[] ToIds = new string[0];

        public LinkAction Action = LinkAction.Toggle;

        /// <summary>
        /// Whole fixed steps, converted from the schema's seconds at load. Nothing multiplies
        /// seconds by a timestep during a run (GDD 6.1).
        /// </summary>
        public int DelaySteps;
    }
}
