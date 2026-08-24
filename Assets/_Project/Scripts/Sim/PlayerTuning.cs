using System;
using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// GDD 5.7. The player is deliberately simple: every point of movement complexity is
    /// attention taken away from the balloon.
    ///
    /// Jump velocity is never stored — it is derived from <see cref="jumpHeightTiles"/> so
    /// the height stays the number a designer reasons about.
    ///
    /// Coyote time and input buffer are frame counts, not seconds. At a fixed 60 Hz they are
    /// exact; expressed in seconds they would be a float comparison that drifts.
    /// </summary>
    [Serializable]
    public struct PlayerTuning
    {
        // --- GDD 5.7 ---

        public float runSpeed;

        /// <summary>Metres, and tiles, since 1 tile = 1 metre.</summary>
        public float jumpHeightTiles;

        public float gravity;

        /// <summary>GDD 6.4: ~18 m/s. The contrast with the balloon's 2.0 m/s *is* the feel.</summary>
        public float terminalFallSpeed;

        public int coyoteFrames;
        public int jumpBufferFrames;

        // --- Body ---

        public float halfWidth;
        public float halfHeight;

        // --- Added 2026-08-22; see GDD 5.7 notes ---

        /// <summary>Frames to reach full run speed on the ground. 0 = instant.</summary>
        public int groundAccelFrames;

        /// <summary>Frames to reach full run speed in the air. Deliberately slower than ground.</summary>
        public int airAccelFrames;

        /// <summary>
        /// Rising velocity is multiplied by this when jump is released early, giving a short
        /// hop. This is what JumpHeld in the GDD 5.5 contract is for.
        /// </summary>
        public float jumpCutMultiplier;

        /// <summary>
        /// Frames a death is held before the level resets. GDD 8 caps the whole loop at
        /// 200 ms; 6 frames is 100 ms. 0 restarts on the very next step.
        /// </summary>
        public int deathRestartFrames;

        public static PlayerTuning Default => new PlayerTuning
        {
            runSpeed = 6.0f,
            jumpHeightTiles = 3.2f,
            gravity = 40f,
            terminalFallSpeed = 18f,
            coyoteFrames = 6,
            jumpBufferFrames = 10,

            halfWidth = 0.40f,
            halfHeight = 0.80f,

            groundAccelFrames = 4,
            airAccelFrames = 8,
            jumpCutMultiplier = 0.39f,
            deathRestartFrames = 6,
        };

        /// <summary>v = sqrt(2 * g * h). The height is the tunable; this is never hardcoded.</summary>
        public float JumpVelocity => Mathf.Sqrt(2f * gravity * jumpHeightTiles);
    }
}
