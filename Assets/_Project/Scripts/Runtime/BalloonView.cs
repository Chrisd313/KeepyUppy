using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Copies simulation state out to a transform. One-way: rendering never writes back
    /// into the sim.
    /// </summary>
    public sealed class BalloonView : MonoBehaviour
    {
        public BalloonBody Body;

        /// <summary>
        /// Supplies the world's step counter and the fraction into the current step.
        /// Optional: without it the view samples the sim raw, which is correct but judders.
        /// </summary>
        public SimDriver Driver;

        readonly StepInterpolator _interp = new StepInterpolator();

        /// <summary>
        /// One sample per fixed step, which is what makes the tween line up with
        /// <see cref="SimDriver.Alpha"/>. SimDriver runs earlier in the same FixedUpdate
        /// (its DefaultExecutionOrder), so this reads the position after the step ran.
        /// </summary>
        void FixedUpdate()
        {
            if (Body == null || Driver == null) return;

            _interp.Sync(Driver.World.Clock.Step, Body.Position);
        }

        void LateUpdate()
        {
            if (Body == null) return;

            // Sub-pixel on purpose (GDD 13.2). Snapping the balloon to the 20 px grid
            // produces visible judder on the one object the player is tracking.
            //
            // Interpolated between fixed steps for the same reason: at terminal fall the
            // balloon moves 0.67 px per frame, so a render frame that lands inside a step and
            // draws the previous position again is a visible hitch - it reads as the balloon
            // bouncing off nothing in mid-air.
            Vector2 p = Driver != null && _interp.Started ? _interp.Sample(Driver.Alpha) : Body.Position;

            transform.localPosition = new Vector3(p.x, p.y, transform.localPosition.z);

            float d = Body.Radius * 2f;
            transform.localScale = new Vector3(d, d, 1f);
        }
    }
}
