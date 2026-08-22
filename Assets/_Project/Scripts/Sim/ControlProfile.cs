namespace KeepyUppy.Sim
{
    /// <summary>GDD 5.2. Hot-swappable mid-run so the GDD 5.3 experiment can cross-play levels.</summary>
    public enum ControlProfile
    {
        /// <summary>A — jump only. Purest, most Level Devil. Props auto-activate or are absent.</summary>
        JumpOnly = 0,

        /// <summary>B — jump plus context: bump, palm, spike, full manual props. The default.</summary>
        JumpAndContext = 1,

        /// <summary>
        /// C — jump for the balloon, context for props only. The body is the only thing that
        /// ever strikes the balloon; all aiming is positional. Watch this one.
        /// </summary>
        PropsOnly = 2,
    }

    /// <summary>What a profile actually permits.</summary>
    public struct ControlProfileConfig
    {
        public bool palmEnabled;
        public bool spikeEnabled;
        public bool contextDrivesProps;
    }

    public static class ControlProfiles
    {
        /// <summary>
        /// The one and only switch on <see cref="ControlProfile"/> in the codebase. Every
        /// other site asks this table rather than testing the enum, so a profile change is a
        /// config change.
        /// </summary>
        public static ControlProfileConfig Of(ControlProfile profile)
        {
            switch (profile)
            {
                case ControlProfile.JumpOnly:
                    return new ControlProfileConfig
                    {
                        palmEnabled = false,
                        spikeEnabled = false,
                        contextDrivesProps = false,
                    };

                case ControlProfile.PropsOnly:
                    return new ControlProfileConfig
                    {
                        palmEnabled = false,
                        spikeEnabled = false,
                        contextDrivesProps = true,
                    };

                default:
                    return new ControlProfileConfig
                    {
                        palmEnabled = true,
                        spikeEnabled = true,
                        contextDrivesProps = true,
                    };
            }
        }
    }
}
