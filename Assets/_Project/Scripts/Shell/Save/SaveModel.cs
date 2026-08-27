namespace KeepyUppy.Shell.Save
{
    /// <summary>
    /// Every progression rule, as pure functions. No Unity types, no IO, no state — so all of
    /// it is reachable from an EditMode test without a scene, which is the whole reason the
    /// rules are not in ShellView.
    /// </summary>
    public static class SaveModel
    {
        /// <summary>How far over par still earns silver.</summary>
        public const int SilverSlack = 3;

        /// <summary>
        /// The touch-count medal (GDD 15). Three tiers rather than one because the gradient is
        /// the point: GDD 4.2 makes the fewest-touches line the most dangerous line in the
        /// game, and a player who missed gold by one touch needs something to aim at.
        ///
        /// A par of zero or less means UNSET — the normal state while a level is authored,
        /// since par comes from playtest metrics. An unset par can never award gold.
        /// </summary>
        public static Medal Grade(int touches, int parTouches)
        {
            if (parTouches <= 0) return Medal.Bronze;
            if (touches <= parTouches) return Medal.Gold;
            if (touches <= parTouches + SilverSlack) return Medal.Silver;

            return Medal.Bronze;
        }
    }
}
