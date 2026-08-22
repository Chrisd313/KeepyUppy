namespace KeepyUppy.Sim
{
    /// <summary>
    /// Noise as a pure function of (step, seed, channel) — GDD 6.1.
    ///
    /// There is no state, no Random and no wall clock. Querying the same step twice
    /// returns the same bits, so a replay from step 0 reproduces the original run
    /// exactly even if it is queried in a different order or not at all.
    /// </summary>
    public static class DeterministicNoise
    {
        /// <summary>Uniform in [0, 1]. Integer hash only — no floating-point accumulation.</summary>
        public static float Unit(int step, int seed, int channel = 0)
        {
            uint h = (uint)step * 2654435761u;
            h ^= (uint)seed * 2246822519u;
            h ^= (uint)channel * 3266489917u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return h / 4294967295f;
        }

        /// <summary>Uniform in [-1, 1].</summary>
        public static float Signed(int step, int seed, int channel = 0)
            => Unit(step, seed, channel) * 2f - 1f;
    }
}
