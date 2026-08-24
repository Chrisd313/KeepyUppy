using UnityEngine;

namespace KeepyUppy.Sim.Entities
{
    /// <summary>
    /// Where a path-following entity is at a given step.
    ///
    /// **This is a pure function of the step counter — never an accumulation.** Position is
    /// evaluated from scratch every step rather than advanced by <c>+= v * dt</c>, and that
    /// single decision buys three things:
    ///
    /// - No float drift. Attempt 40 replays attempt 1 by construction (GDD 6.1), not by care.
    /// - Restart costs nothing. There is no motion state to reset, which keeps the GDD 8
    ///   200 ms budget clear.
    /// - Any step can be asked for out of order, which is what the spec 10 phase-5 time
    ///   controls (rewind, ghost trails) will need.
    ///
    /// Waypoints arrive as a flat x,y list because JsonUtility cannot serialise nested
    /// arrays (see LevelData.path).
    /// </summary>
    public static class EntityPath
    {
        /// <summary>
        /// Position along <paramref name="points"/> at <paramref name="step"/>.
        ///
        /// A path of fewer than two points is a stationary entity and returns the first
        /// point, so an entity placed without a path behaves sensibly rather than failing.
        /// </summary>
        /// <param name="phase">0..1 of a full cycle. Desyncs identical traps (schema: phase).</param>
        public static Vector2 Evaluate(Vector2[] points, PathMode mode, float speed,
                                       float phase, int step)
        {
            if (points == null || points.Length == 0) return Vector2.zero;
            if (points.Length == 1 || speed <= 0f) return points[0];

            float total = Length(points);
            if (total <= 0f) return points[0];

            // step * Dt, not an accumulated clock: one multiply, exactly reproducible.
            float travelled = speed * (step * SimClock.Dt);

            float cycle = mode == PathMode.PingPong ? total * 2f : total;
            float distance = travelled + Frac(phase) * cycle;

            switch (mode)
            {
                case PathMode.Once:
                    distance = Mathf.Min(distance, total);
                    break;

                case PathMode.Loop:
                    distance = Repeat(distance, total);
                    break;

                default:
                    // Fold the doubled cycle back on itself: out along the path, then back.
                    distance = Repeat(distance, cycle);
                    if (distance > total) distance = cycle - distance;
                    break;
            }

            return PointAt(points, distance);
        }

        /// <summary>Total length of the polyline. Open, not closed — Loop teleports home.</summary>
        public static float Length(Vector2[] points)
        {
            float total = 0f;
            for (int i = 1; i < points.Length; i++) total += (points[i] - points[i - 1]).magnitude;
            return total;
        }

        /// <summary>The point <paramref name="distance"/> metres along the polyline.</summary>
        public static Vector2 PointAt(Vector2[] points, float distance)
        {
            if (distance <= 0f) return points[0];

            for (int i = 1; i < points.Length; i++)
            {
                Vector2 from = points[i - 1];
                Vector2 to = points[i];
                float segment = (to - from).magnitude;

                if (segment <= 0f) continue;
                if (distance > segment) { distance -= segment; continue; }

                return from + (to - from) * (distance / segment);
            }

            return points[points.Length - 1];
        }

        /// <summary>
        /// Unpacks the schema's flat x,y list. The entity's own position is the implicit
        /// first waypoint, so a path of one extra point is a there-and-back.
        /// </summary>
        public static Vector2[] FromFlat(Vector2 origin, float[] flat)
        {
            int extra = flat == null ? 0 : flat.Length / 2;
            var points = new Vector2[extra + 1];
            points[0] = origin;

            for (int i = 0; i < extra; i++)
                points[i + 1] = new Vector2(flat[i * 2], flat[i * 2 + 1]);

            return points;
        }

        static float Frac(float value) => value - Mathf.Floor(value);

        /// <summary>Mathf.Repeat without the Unity dependency creeping into the maths.</summary>
        static float Repeat(float value, float length)
        {
            if (length <= 0f) return 0f;
            float wrapped = value - Mathf.Floor(value / length) * length;
            return wrapped < 0f ? 0f : wrapped;
        }
    }
}
