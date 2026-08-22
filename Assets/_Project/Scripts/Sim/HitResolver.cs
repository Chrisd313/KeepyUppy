using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>Record of the most recent hit. Feeds the debug overlay and the contact tests.</summary>
    public struct HitRecord
    {
        public HitSource source;
        public float incomingSpeed;
        public float outgoingSpeed;
        public float aimOffset;
        public int step;
        public bool valid;

        public bool WasActive => valid && source != HitSource.PassiveContact;
    }

    /// <summary>
    /// LOCKED GDD 5.1 — every balloon contact in the game resolves through
    /// <see cref="ApplyHit"/>. Jump-bump, palm, spike and passive contact are the same call
    /// with different parameters.
    ///
    /// It holds the player because GDD 6.4's bump inheritance is a function of the *player's*
    /// velocity. It never reads the balloon's incoming velocity, which is what makes
    /// "set, never add" structural rather than a convention.
    /// </summary>
    public sealed class HitResolver
    {
        readonly BalloonBody _balloon;
        readonly PlayerBody _player;

        public BalloonTuning Tuning;

        /// <summary>
        /// GDD 15's touch count. Incremented here, on active hits only, so no call site can
        /// forget to count one or count a passive contact by mistake.
        /// </summary>
        public int TouchCount { get; private set; }

        public HitRecord LastHit { get; private set; }

        public HitResolver(BalloonBody balloon, PlayerBody player, in BalloonTuning tuning)
        {
            _balloon = balloon;
            _player = player;
            Tuning = tuning;
        }

        public void Reset()
        {
            TouchCount = 0;
            LastHit = default;
        }

        /// <summary>
        /// LOCKED signature (GDD 5.1). SETS the balloon's velocity.
        ///
        /// Adding force would make a bump's outcome depend on the balloon's incoming
        /// velocity, so the same input would produce different results — poison for a game
        /// learned by repetition.
        /// </summary>
        /// <param name="source">Which verb struck the balloon.</param>
        /// <param name="direction">Unit direction before aim offset. Need not be normalised.</param>
        /// <param name="power">Multiplier on the source's tuned speed. For PassiveContact this
        /// is the outgoing speed in m/s directly.</param>
        /// <param name="aimOffset">-1 .. +1. Tilts the outgoing angle up to
        /// aim_offset_max_angle off <paramref name="direction"/> (GDD 6.5).</param>
        public void ApplyHit(HitSource source, Vector2 direction, float power, float aimOffset)
        {
            float incoming = _balloon.Velocity.magnitude;   // recorded only, never used below

            Vector2 dir = Normalise(direction, Vector2.up);
            float offset = Mathf.Clamp(aimOffset, -1f, 1f);
            dir = Rotate(dir, offset * Tuning.aimOffsetMaxAngle * Mathf.Deg2Rad);

            float speed = power * BaseSpeed(source);
            Vector2 outgoing = new Vector2(dir.x * speed, dir.y * speed);

            // GDD 6.4 inheritance: a running bump travels. Read from the player, so an
            // identical hit on a differently-moving balloon still produces an identical result.
            if (source == HitSource.JumpBump && _player != null)
            {
                outgoing.x += _player.Velocity.x * Tuning.bumpInheritX;
                outgoing.y += _player.Velocity.y * Tuning.bumpInheritY;
            }

            _balloon.Velocity = outgoing;
            _balloon.ClampSpeed(Tuning.maxSpeed);

            if (source != HitSource.PassiveContact) TouchCount++;

            LastHit = new HitRecord
            {
                source = source,
                incomingSpeed = incoming,
                outgoingSpeed = _balloon.Velocity.magnitude,
                aimOffset = offset,
                valid = true,
            };
        }

        /// <summary>Per-verb speed from the GDD 6.4 table. Passive carries its speed in `power`.</summary>
        public float BaseSpeed(HitSource source)
        {
            switch (source)
            {
                case HitSource.Palm: return Tuning.palmSpeed;
                case HitSource.Spike: return Tuning.spikeSpeed;
                case HitSource.PassiveContact: return 1f;
                default: return Tuning.bumpVelocity;
            }
        }

        /// <summary>Clockwise rotation, so a positive offset sends the balloon to the right.</summary>
        public static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians);
            float s = Mathf.Sin(radians);
            return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
        }

        static Vector2 Normalise(Vector2 v, Vector2 fallback)
        {
            float len = Mathf.Sqrt(v.x * v.x + v.y * v.y);
            return len < 1e-6f ? fallback : new Vector2(v.x / len, v.y / len);
        }
    }
}
