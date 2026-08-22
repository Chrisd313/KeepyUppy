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

        void LateUpdate()
        {
            if (Body == null) return;

            // Sub-pixel, like the balloon (GDD 13.2). Only the tile grid is snapped.
            transform.localPosition = new Vector3(Body.Position.x, Body.Position.y,
                                                  transform.localPosition.z);
            transform.localScale = new Vector3(Body.HalfWidth * 2f, Body.HalfHeight * 2f, 1f);
        }
    }
}
