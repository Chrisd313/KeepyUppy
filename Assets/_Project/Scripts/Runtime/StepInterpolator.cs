using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Renders a body between the two most recent fixed steps instead of snapping to the
    /// latest one.
    ///
    /// The simulation advances only in FixedUpdate at exactly 60 Hz (GDD 6.1); rendering
    /// happens on a display frame that is not phase-locked to it. Sampling the sim position
    /// raw on a render frame therefore draws the same step twice on one frame and skips a
    /// step on the next, and the apparent velocity fluctuates. On a balloon descending at
    /// terminal fall — 2 m/s, or 0.67 px per frame at 20 px/m — that reads as the balloon
    /// lightly bouncing off nothing in mid-air. It is the same complaint CLAUDE.md's "do not
    /// pixel-snap moving entities" note is about, arriving through the clock instead of
    /// through the grid.
    ///
    /// This is presentation only. Nothing here is read by the simulation, the interpolated
    /// value is never written back, and the sim cannot observe that it exists — the same rule
    /// TimeControl follows. Determinism is untouched.
    ///
    /// The cost is that the picture lags the simulation by up to one step (16.7 ms). That is
    /// the standard trade for a fixed-timestep renderer and it is not negotiable away: the
    /// alternative, extrapolating forward, invents positions the sim never had and overshoots
    /// through walls on the frame a bounce lands.
    ///
    /// <b><see cref="Sync"/> must be called once per fixed step, not once per render frame.</b>
    /// The two are not the same count, and that is the entire point — pairing a render-frame
    /// sample with a fixed-step <see cref="AlphaOf"/> puts a two-step jump against a one-step
    /// fraction on exactly the frames where the clocks drift past each other, which
    /// reintroduces the hitch on the frames that need it most.
    /// </summary>
    public sealed class StepInterpolator
    {
        Vector2 _prev;
        Vector2 _curr;
        int _lastClockStep;
        bool _started;

        /// <summary>Where the tween currently ends: the latest simulated position.</summary>
        public Vector2 Current => _curr;

        /// <summary>
        /// Record one fixed step. <paramref name="clockStep"/> is the world's own step
        /// counter, which runs backwards on a restart — the body teleports to spawn there,
        /// and tweening across that would drag it over the room on the exact 200 ms path
        /// GDD 8 wants to feel instant.
        /// </summary>
        public void Sync(int clockStep, Vector2 position)
        {
            if (!_started || clockStep < _lastClockStep)
            {
                _prev = position;
                _curr = position;
                _started = true;
            }
            else
            {
                _prev = _curr;
                _curr = position;
            }

            _lastClockStep = clockStep;
        }

        /// <summary>Where to draw the body, <paramref name="alpha"/> of the way into the step.</summary>
        public Vector2 Sample(float alpha)
        {
            float a = alpha < 0f ? 0f : (alpha > 1f ? 1f : alpha);
            return new Vector2(_prev.x + (_curr.x - _prev.x) * a,
                               _prev.y + (_curr.y - _prev.y) * a);
        }

        /// <summary>
        /// How far the current render frame sits into the step that has not run yet.
        ///
        /// Unity has no built-in accessor for this; render time minus the time of the most
        /// recent fixed step, over the step length, is the standard construction. Clamped,
        /// because a frame that overruns its step is normal and must not extrapolate.
        /// </summary>
        public static float AlphaOf(float renderTime, float fixedTime, float fixedDelta)
        {
            if (fixedDelta <= 0f) return 1f;

            float a = (renderTime - fixedTime) / fixedDelta;
            return a < 0f ? 0f : (a > 1f ? 1f : a);
        }
    }
}
