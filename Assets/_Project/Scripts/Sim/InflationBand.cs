namespace KeepyUppy.Sim
{
    /// <summary>GDD 7.1. Simulation is continuous; the bands are the readable art snap.</summary>
    public enum InflationBand
    {
        Shrivelled = 0,
        Slack = 1,
        Nominal = 2,
        Full = 3,
        Critical = 4,
    }

    public static class InflationBands
    {
        /// <summary>Levels are authored at Nominal. Drag scales relative to this radius.</summary>
        public const float NominalRadius = 0.50f;

        /// <summary>Radii from the GDD 7.1 table, indexed by band.</summary>
        public static float RadiusOf(InflationBand band)
        {
            switch (band)
            {
                case InflationBand.Shrivelled: return 0.25f;
                case InflationBand.Slack:      return 0.38f;
                case InflationBand.Full:       return 0.63f;
                case InflationBand.Critical:   return 0.75f;
                default:                       return NominalRadius;
            }
        }
    }
}
