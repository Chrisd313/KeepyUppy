using System;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// The GDD 6.4 tuning table. Every field is a slider (GDD 18) and none of it is
    /// hardcoded at a call site. Defaults here are the starting values from the table;
    /// when a value is tuned to something better, update the GDD table to match.
    ///
    /// Serialisable so it can round-trip through the tuning profiles in
    /// StreamingAssets/levels/_tuning/. No loader yet.
    /// </summary>
    [Serializable]
    public struct BalloonTuning
    {
        // --- Motion model (GDD 6.2 / 6.3) ---

        /// <summary>m/s^2, shared with the player.</summary>
        public float gravity;

        /// <summary>Vertical quadratic drag. 2.45 gives terminal fall of 2.0 m/s at Nominal.</summary>
        public float dragKy;

        /// <summary>Horizontal quadratic drag. Deliberately much lower than dragKy — GDD 6.3.</summary>
        public float dragKx;

        // --- Collision response (GDD 6.4) ---

        public float restitutionWall;

        /// <summary>Law 4.2: passive contact loses energy. Unused until the player exists.</summary>
        public float restitutionPlayerPassive;

        /// <summary>Below this normal speed there is no bounce at all. Kills contact jitter.</summary>
        public float minBounceSpeed;

        /// <summary>Hard speed ceiling. Also the tunnelling guard the sweep is sized against.</summary>
        public float maxSpeed;

        // --- Verb parameters (GDD 6.4 / 6.5) ---
        // Carried as data so the table lives in one place. No verb reads them yet;
        // ApplyHit is a later session.

        public float bumpVelocity;
        public float bumpInheritY;
        public float bumpInheritX;
        public float palmSpeed;
        public float spikeSpeed;
        public float aimOffsetMaxAngle;

        /// <summary>GDD 5.5 aim table: left/right aims this far off vertical.</summary>
        public float aimSideAngle;

        /// <summary>GDD 5.5 aim table: aiming straight up adds this fraction of power.</summary>
        public float aimUpPowerBonus;

        /// <summary>GDD 5.5: context held under 150 ms is a palm, at or over it is a spike charge.</summary>
        public int contextHoldFrames;

        /// <summary>GDD 6.4 palm cooldown, 0.25 s.</summary>
        public int palmCooldownFrames;

        /// <summary>GDD 6.4 spike cooldown, 0.6 s.</summary>
        public int spikeCooldownFrames;

        // --- Contact and fairness (GDD 4.2 / 8) — added 2026-08-22 ---

        /// <summary>
        /// LOCKED GDD 4.2. Contact counts as active — and so may add energy — only if the
        /// player is rising faster than this, or pressed jump inside the buffer window.
        /// Below it, contact resolves at restitution_player_passive and loses height.
        /// </summary>
        public float activeContactMinPlayerVy;

        /// <summary>
        /// GDD 8: added to the balloon's radius for player contact only, so the player's bump
        /// volume is larger than the balloon's visual. Generous to the player.
        /// </summary>
        public float playerBumpRadiusBonus;

        /// <summary>
        /// GDD 8: subtracted from every hazard's kill volume, so its pop hitbox is smaller
        /// than its visual. Stingy to the level.
        /// </summary>
        public float hazardPopRadiusShrink;

        /// <summary>GDD 5.4 "within arm's reach", in metres. Gates palm and spike.</summary>
        public float armReachRadius;

        // --- Idle flutter (GDD 6.6) ---
        // Deterministic noise, not Random. Amplitude scales inversely with speed.

        public float flutterAccel;
        public float flutterFalloffSpeed;

        /// <summary>
        /// GDD 8, the generous half. The exit's accept volume is larger than its visual by
        /// this much — the mirror of hazard_pop_radius_shrink. Tuned separately, because
        /// generosity at the goal and cruelty at the hazards are different feelings.
        /// </summary>
        public float exitAcceptBonus;

        /// <summary>
        /// Steps per render frame while fast-forward is held. A slider because the right
        /// value is a feel question: too high and hazards stop being reactable while it is
        /// down. Never a change to dt — see TimeControl.
        /// </summary>
        public int fastForwardFactor;

        public static BalloonTuning Default => new BalloonTuning
        {
            gravity = 1.0f,
            dragKy = 0.25f,
            dragKx = 0.09f,

            restitutionWall = 0.75f,
            restitutionPlayerPassive = 0.5f,
            minBounceSpeed = 0.3f,
            maxSpeed = 24f,

            bumpVelocity = 20.0f,
            bumpInheritY = 0.35f,
            bumpInheritX = 0.5f,
            palmSpeed = 6.0f,
            spikeSpeed = 14.0f,
            aimOffsetMaxAngle = 40f,

            aimSideAngle = 55f,
            aimUpPowerBonus = 0.15f,

            contextHoldFrames = 9,      // 150 ms
            palmCooldownFrames = 15,    // 0.25 s
            spikeCooldownFrames = 36,   // 0.6 s

            activeContactMinPlayerVy = 1.0f,
            playerBumpRadiusBonus = 0.15f,
            hazardPopRadiusShrink = 0.15f,
            armReachRadius = 1.4f,

            flutterAccel = 1.2f,
            flutterFalloffSpeed = 3.0f,

            exitAcceptBonus = 0.15f,
            fastForwardFactor = 2,
        };
    }
}
