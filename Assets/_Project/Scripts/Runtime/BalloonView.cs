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

        void LateUpdate()
        {
            if (Body == null) return;

            // Sub-pixel on purpose (GDD 13.2). Snapping the balloon to the 20 px grid
            // produces visible judder on the one object the player is tracking.
            transform.localPosition = new Vector3(Body.Position.x, Body.Position.y,
                                                  transform.localPosition.z);

            float d = Body.Radius * 2f;
            transform.localScale = new Vector3(d, d, 1f);
        }
    }
}
