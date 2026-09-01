using UnityEngine;

namespace KeepyUppy.Sim.Entities
{
    /// <summary>
    /// One hazard, in the form the simulation runs it in.
    ///
    /// Deliberately one class with a kind switch rather than a subclass per hazard. The kill
    /// volume of every type is a capsule — a segment plus a radius — so a saw is a capsule of
    /// zero length and a lamp beam is a long one. That keeps the fairness shrink (GDD 8) at a
    /// single call site, which is the whole reason the tile version lives in one place too.
    ///
    /// Config is copied out of the level data at load. Nothing here reads JSON, so the sim
    /// stays testable without a file (CLAUDE.md working agreements).
    /// </summary>
    public sealed class SimEntity
    {
        // --- Identity and config: set at load, never mutated by the tick ---

        public string Id;
        public EntityKind Kind;

        /// <summary>Waypoints, the entity's own position first. See EntityPath.FromFlat.</summary>
        public Vector2[] Points = { Vector2.zero };

        public PathMode Mode = PathMode.PingPong;
        public float Speed;

        /// <summary>0..1 of a cycle, so two identical traps can be desynced.</summary>
        public float Phase;

        /// <summary>Kill radius for a saw; beam half-width for a lamp; half-width for a fan.</summary>
        public float Radius;

        /// <summary>Beam or blast length, along <see cref="Angle"/>. Unused by the saw.</summary>
        public float Length;

        /// <summary>Degrees, 0 = +x, 90 = up. Lamps point anywhere; fans are snapped to an axis.</summary>
        public float Angle;

        /// <summary>Lamp: seconds of unbroken exposure that pops the balloon.</summary>
        public float Duration;

        /// <summary>Fan: v_air magnitude it contributes.</summary>
        public float Power;

        /// <summary>
        /// GDD 9.3 start_state. A dormant entity does not move, kill or blow — it is waiting
        /// to be armed by a trigger. Triggers do not exist yet, so nothing arms it; the flag
        /// is honoured now so a level authored with one behaves rather than misbehaves.
        ///
        /// Flip it through <see cref="SetActive"/> rather than directly whenever the step is
        /// known: that is what records <see cref="ArmedStep"/>.
        /// </summary>
        public bool Active = true;

        /// <summary>
        /// What Active returns to on a restart. A link can flip Active mid-run (GDD 9.3), and
        /// without this the room the player retries would not be the room they died in.
        /// </summary>
        public bool StartActive = true;

        // --- Run state: everything here must be reset by Reset() and covered by the
        //     replay-equality test (CLAUDE.md). Position is derived, not accumulated. ---

        public Vector2 Position;

        /// <summary>Consecutive frames the balloon has been inside a lamp beam.</summary>
        public int ExposureFrames;

        /// <summary>
        /// The step this entity was armed on. A path is measured from here, not from zero.
        ///
        /// EntityPath is a pure function of the step counter, so a saw armed ten seconds into
        /// a room would otherwise be placed where it would have been had it run the whole
        /// time — the better part of a lap in a single step, which reads as the saw
        /// teleporting to the far end of its route and then resuming (GDD 9.3).
        ///
        /// Zero for anything that starts active, so a level with no triggers is unchanged.
        /// </summary>
        public int ArmedStep;

        /// <summary>Where the kill capsule ends. Equals Position for anything but a lamp.</summary>
        public Vector2 KillEnd;

        public void Reset()
        {
            Active = StartActive;
            ArmedStep = 0;
            ExposureFrames = 0;
            Position = Points != null && Points.Length > 0 ? Points[0] : Vector2.zero;
            KillEnd = Position;
        }

        /// <summary>
        /// Places the entity for this step. A pure function of the step counter, so this is
        /// safe to call for any step in any order (EntityPath).
        /// </summary>
        public void Place(int step)
        {
            // Steps since arming, never the raw world step: see ArmedStep. Clamped at zero so
            // asking for a step before the entity was armed parks it at its start rather than
            // running the path backwards - the phase-5 rewind will ask for exactly that.
            int elapsed = step - ArmedStep;
            if (elapsed < 0) elapsed = 0;

            Position = Active
                ? EntityPath.Evaluate(Points, Mode, Speed, Phase, elapsed)
                : Points[0];

            // A lamp's beam and a spike bar's shaft are both capsules with length; everything
            // else is a point. One shape keeps the GDD 8 shrink at a single call site.
            KillEnd = Kind == EntityKind.Lamp || Kind == EntityKind.SpikeBar
                ? Position + Direction() * Length
                : Position;
        }

        /// <summary>
        /// Arms or disarms the entity, recording when. Only a false-to-true edge moves
        /// <see cref="ArmedStep"/>, so a link that fires On twice does not restart the path.
        /// </summary>
        public void SetActive(bool active, int step)
        {
            if (active && !Active) ArmedStep = step;
            Active = active;
        }

        public Vector2 Direction()
        {
            float radians = Angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        /// <summary>Instantly lethal on contact. A lamp is not — it needs time (GDD 9.2).</summary>
        public bool KillsOnContact => Kind == EntityKind.Saw || Kind == EntityKind.SpikeBar;

        /// <summary>GDD 4.1: the balloon dies to almost anything; the player only to blades and spikes.</summary>
        public bool KillsPlayer => Kind == EntityKind.Saw || Kind == EntityKind.SpikeBar;

        /// <summary>
        /// The capsule against a circle, inset by the GDD 8 fairness shrink.
        /// Stingy to the level: the kill volume is always smaller than the visual.
        /// </summary>
        public bool OverlapsCircle(Vector2 centre, float circleRadius, float shrink)
        {
            float r = KillRadius(shrink) + circleRadius;
            return SegmentDistanceSq(centre, Position, KillEnd) < r * r;
        }

        /// <summary>The capsule against the player's box. Only the saw needs this.</summary>
        public bool OverlapsBox(Vector2 centre, float halfWidth, float halfHeight, float shrink)
        {
            float r = KillRadius(shrink);

            // Nearest point on the capsule's spine to the box, then the nearest point on the
            // box to that. Exact for a saw, whose spine is a single point.
            Vector2 near = ClosestOnSegment(centre, Position, KillEnd);

            float dx = near.x - Mathf.Clamp(near.x, centre.x - halfWidth, centre.x + halfWidth);
            float dy = near.y - Mathf.Clamp(near.y, centre.y - halfHeight, centre.y + halfHeight);

            return dx * dx + dy * dy < r * r;
        }

        /// <summary>Never let the shrink erase a hitbox entirely, as with tile hazards.</summary>
        public float KillRadius(float shrink) => Mathf.Max(Radius - shrink, 0.02f);

        /// <summary>The air zone a fan contributes. Axis-aligned; see EntityField.</summary>
        public Rect FanRect()
        {
            Vector2 dir = Direction();
            bool horizontal = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y);

            if (horizontal)
            {
                float x = dir.x >= 0f ? Position.x : Position.x - Length;
                return new Rect(x, Position.y - Radius, Length, Radius * 2f);
            }

            float y = dir.y >= 0f ? Position.y : Position.y - Length;
            return new Rect(Position.x - Radius, y, Radius * 2f, Length);
        }

        /// <summary>Fan v_air, snapped to an axis so it composes with the rect above.</summary>
        public Vector2 FanVelocity()
        {
            Vector2 dir = Direction();
            return Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
                ? new Vector2(Mathf.Sign(dir.x) * Power, 0f)
                : new Vector2(0f, Mathf.Sign(dir.y) * Power);
        }

        static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq <= 0f) return a;

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            return a + ab * t;
        }

        static float SegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
            => (p - ClosestOnSegment(p, a, b)).sqrMagnitude;
    }
}
