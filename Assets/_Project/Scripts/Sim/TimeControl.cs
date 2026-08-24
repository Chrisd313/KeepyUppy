using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// How many fixed steps a render frame is allowed to run.
    ///
    /// **The simulation must never be able to observe this.** GDD 6.1 fixes the timestep at
    /// a compile-time constant, so speed is expressed as the NUMBER of 60 Hz steps taken per
    /// render frame — never a longer dt and never Unity's global timeScale. The same steps happen in
    /// the same order, so a fast-forwarded run is bit-identical to a normal one and the
    /// replay test cannot tell them apart.
    ///
    /// What changes is only how long a human waits. Every in-game clock — hazard periods,
    /// cooldowns, the balloon's descent — scales together, so GDD 4.3's "gravity is the
    /// clock" pressure is untouched. The player's own reaction time is the one real cost,
    /// which is why fast-forward is held rather than toggled.
    ///
    /// Slow motion is the same mechanism inverted, and is GDD 16's accessibility assist.
    /// </summary>
    public sealed class TimeControl
    {
        /// <summary>A guard on the tuning slider, not a design limit. See StepsFor.</summary>
        public const int MaxFactor = 4;

        /// <summary>Steps per frame while fast-forwarding. 1 is normal speed.</summary>
        public int Factor = 1;

        /// <summary>GDD 16: the 0.5x assist. Takes precedence over <see cref="Factor"/>.</summary>
        public bool SlowMotion;

        /// <summary>
        /// Steps to run on this call.
        ///
        /// <paramref name="fixedStep"/> is the driver's own count of fixed steps taken so
        /// far (SimDriver._fixedStep), not a render-frame number - FixedUpdate can run more
        /// than once per render frame, so the two are not the same counter. A pure function
        /// of it either way: slow motion drops every other step by parity rather than by an
        /// accumulator, so there is no state to drift and none to reset when the room
        /// restarts.
        /// </summary>
        public int StepsFor(int fixedStep)
        {
            // An accessibility setting must not be overridden by a convenience one.
            if (SlowMotion) return fixedStep % 2 == 0 ? 1 : 0;

            return Mathf.Clamp(Factor, 1, MaxFactor);
        }
    }
}
