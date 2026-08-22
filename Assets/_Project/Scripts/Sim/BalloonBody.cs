using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// The balloon, hand-integrated (GDD 6.1 / 6.2).
    ///
    /// A plain C# class on purpose: no MonoBehaviour, no Rigidbody2D, no engine gravity,
    /// drag or restitution. Rendering copies Position out; nothing writes back in.
    ///
    ///     a = -gravity * gravityMult
    ///       + (-k * |v - v_air| * (v - v_air))     per axis, k split (GDD 6.3)
    /// </summary>
    public sealed class BalloonBody
    {
        public Vector2 Position;
        public Vector2 Velocity;

        /// <summary>Metres. See GDD 7.1 bands.</summary>
        public float Radius = InflationBands.NominalRadius;

        /// <summary>Inflation's effect on weight. 1.0 until inflation is implemented.</summary>
        public float GravityMult = 1f;

        /// <summary>Set by SimWorld.Kill only. Linked fate (GDD 4.1) means the player pops too.</summary>
        public bool Popped;

        /// <summary>
        /// One fixed step of acceleration and velocity. Position is moved separately by
        /// the swept collision pass so that the sweep sees the post-drag velocity.
        /// </summary>
        /// <param name="vAir">Air velocity sampled at this balloon's position (GDD 6.2).</param>
        /// <param name="step">Fixed-step counter — the only time source in the sim.</param>
        public void Integrate(in BalloonTuning t, Vector2 vAir, int step, int seed, bool flutter)
        {
            // Drag scales with radius, so a large balloon is dramatically more
            // wind-affected and a shrivelled one shrugs gusts off (GDD 6.2).
            float k = Radius / InflationBands.NominalRadius;
            float kx = t.dragKx * k;
            float ky = t.dragKy * k;

            Vector2 vRel = Velocity - vAir;

            float ax = -kx * Abs(vRel.x) * vRel.x;
            float ay = -t.gravity * GravityMult - ky * Abs(vRel.y) * vRel.y;

            if (flutter)
            {
                // Falling-leaf wobble (GDD 6.6). Amplitude scales inversely with speed,
                // and the noise is a pure function of the step counter plus the seed.
                float speed = Magnitude(Velocity);
                float falloff = t.flutterFalloffSpeed / (t.flutterFalloffSpeed + speed);
                ax += t.flutterAccel * falloff * DeterministicNoise.Signed(step, seed, 1);
            }

            Velocity = new Vector2(
                Velocity.x + ax * SimClock.Dt,
                Velocity.y + ay * SimClock.Dt);

            ClampSpeed(t.maxSpeed);
        }

        /// <summary>GDD 6.4 max_speed. Also what keeps the sweep's step length bounded.</summary>
        public void ClampSpeed(float maxSpeed)
        {
            float sqr = Velocity.x * Velocity.x + Velocity.y * Velocity.y;
            float max2 = maxSpeed * maxSpeed;
            if (sqr <= max2 || sqr <= 0f) return;

            float scale = maxSpeed / Mathf.Sqrt(sqr);
            Velocity = new Vector2(Velocity.x * scale, Velocity.y * scale);
        }

        static float Abs(float v) => v < 0f ? -v : v;

        static float Magnitude(Vector2 v) => Mathf.Sqrt(v.x * v.x + v.y * v.y);
    }
}
