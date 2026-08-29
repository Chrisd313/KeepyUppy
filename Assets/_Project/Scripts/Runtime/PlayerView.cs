using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Copies player state out to a transform. One-way: rendering never writes back into
    /// the sim.
    /// </summary>
    public sealed class PlayerView : MonoBehaviour
    {
        public PlayerBody Body;

        /// <summary>Optional, as on <see cref="BalloonView"/>: absent means no interpolation.</summary>
        public SimDriver Driver;

        readonly StepInterpolator _interp = new StepInterpolator();

        /// <summary>One sample per fixed step - see <see cref="BalloonView"/>.</summary>
        void FixedUpdate()
        {
            if (Body == null || Driver == null) return;

            _interp.Sync(Driver.World.Clock.Step, Body.Position);
        }

        void LateUpdate()
        {
            if (Body == null) return;

            // Sub-pixel, like the balloon (GDD 13.2). Only the tile grid is snapped, and
            // interpolated between fixed steps for the same reason the balloon is.
            Vector2 p = Driver != null && _interp.Started ? _interp.Sample(Driver.Alpha) : Body.Position;

            transform.localPosition = new Vector3(p.x, p.y, transform.localPosition.z);
            transform.localScale = new Vector3(Body.HalfWidth * 2f, Body.HalfHeight * 2f, 1f);
        }
    }
}
