using System;
using UnityEngine;

namespace KeepyUppy.InputLayer
{
    /// <summary>
    /// One fixed step's worth of intents. The unit a scripted run is written in, and the
    /// unit a recorded playtest would replay.
    /// </summary>
    [Serializable]
    public struct InputIntents
    {
        public float moveAxis;
        public bool jumpPressed;
        public bool jumpHeld;
        public bool contextPressed;
        public bool contextHeld;
        public Vector2 aimHint;
        public bool restartPressed;

        public static InputIntents Idle => default;
    }
}
