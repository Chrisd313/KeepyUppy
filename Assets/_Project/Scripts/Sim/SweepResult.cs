using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// What a swept move ran into. The caller needs more than "did it hit" — a wall bounces,
    /// a floor ends the balloon's run, and a spike kills whatever touched it.
    /// </summary>
    public struct SweepResult
    {
        public int contacts;

        /// <summary>True if any contact this step was with an upward-facing surface.</summary>
        public bool hitFloor;

        public Vector2 lastNormal;
        public TileKind lastKind;
        public int lastTileX;
        public int lastTileY;

        /// <summary>
        /// Index into SolidField of the moving panel this sweep ended against, or -1. This is
        /// what lets the step after the sweep tell "stood on a moving floor" from "stood on
        /// the world" without re-testing every solid.
        /// </summary>
        public int carrier;

        public bool Any => contacts > 0;
    }
}
