namespace KeepyUppy.Sim
{
    /// <summary>
    /// The simulation's only clock (GDD 6.1).
    ///
    /// Nothing under Sim/ may read UnityEngine.Time. The timestep is a compile-time
    /// constant so that no package, editor tweak or future call site can change the
    /// rate the simulation integrates at without a code change.
    /// </summary>
    public sealed class SimClock
    {
        /// <summary>Simulation rate in hertz. Fixed. See GDD 6.1.</summary>
        public const int Hz = 60;

        /// <summary>Seconds per simulation step. The only dt the sim ever uses.</summary>
        public const float Dt = 1f / Hz;

        /// <summary>Steps elapsed since Reset. Every deterministic value derives from this.</summary>
        public int Step { get; private set; }

        public void Advance() => Step++;

        public void Reset() => Step = 0;

        /// <summary>Whole steps in the given number of seconds, rounded down.</summary>
        public static int StepsIn(float seconds) => (int)(seconds * Hz);
    }
}
