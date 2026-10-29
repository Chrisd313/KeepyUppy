using KeepyUppy.Sim.Entities;
using UnityEngine;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>
    /// Where a moving panel is at a given step (moving-panels design).
    ///
    /// A pure function of the step counter, never an accumulation — the same contract, and
    /// the same three reasons, as EntityPath: no float drift, a free restart, and any step
    /// askable out of order.
    ///
    /// Its own function rather than a parameter on EntityPath. Entities have never needed
    /// dwell, and EntityPath is the function every hazard's position flows through; widening
    /// it for a panel would put a saw's placement one edit away from a panel's bug. With
    /// holdSteps == 0 this delegates, so the shared case cannot drift between the two.
    /// </summary>
    public static class PanelPath
    {
        /// <summary>
        /// Position along <paramref name="points"/> at <paramref name="step"/>, pausing
        /// <paramref name="holdSteps"/> at every waypoint including both ends.
        /// </summary>
        public static Vector2 Evaluate(Vector2[] points, PathMode mode, float speed,
                                       float phase, int holdSteps, int step)
        {
            if (holdSteps <= 0) return EntityPath.Evaluate(points, mode, speed, phase, step);

            if (points == null || points.Length == 0) return Vector2.zero;
            if (points.Length == 1 || speed <= 0f) return points[0];

            // Dwell is inserted between legs rather than folded into the arc length: the
            // travel itself must stay identical to the no-dwell case, so that raising hold
            // never changes how fast the panel moves.
            int legs = points.Length - 1;
            int[] legSteps = new int[legs];
            int travel = 0;

            for (int i = 0; i < legs; i++)
            {
                float length = (points[i + 1] - points[i]).magnitude;
                legSteps[i] = Mathf.Max(1, Mathf.RoundToInt(length / speed / SimClock.Dt));
                travel += legSteps[i];
            }

            // Each leg is followed by a dwell at the waypoint it just reached — not a dwell
            // before the leg — so a panel starts moving immediately at step 0. One dwell per
            // leg (not per waypoint): a one-way trip is legs travelled plus a hold at each
            // one's far end. PingPong walks the same list back, so a full cycle is two of
            // these, and the return dwell (parked at the start) is what closes the loop.
            int oneWay = travel + holdSteps * legs;
            int cycle = mode == PathMode.PingPong ? oneWay * 2 : oneWay;

            int at = step + Mathf.RoundToInt(Frac(phase) * cycle);

            switch (mode)
            {
                case PathMode.Once:
                    at = Mathf.Min(at, oneWay);
                    return Along(points, legSteps, holdSteps, at);

                case PathMode.Loop:
                    at = Mod(at, cycle);
                    return Along(points, legSteps, holdSteps, at);

                default:
                    at = Mod(at, cycle);
                    return at < oneWay
                        ? Along(points, legSteps, holdSteps, at)
                        : AlongReversed(points, legSteps, holdSteps, at - oneWay);
            }
        }

        /// <summary>Walks the leg/dwell list forward, from <c>points[0]</c> onward.</summary>
        static Vector2 Along(Vector2[] points, int[] legSteps, int holdSteps, int at)
        {
            for (int i = 0; i < legSteps.Length; i++)
            {
                if (at < legSteps[i])
                    return Vector2.Lerp(points[i], points[i + 1], (float)at / legSteps[i]);
                at -= legSteps[i];

                if (at < holdSteps) return points[i + 1];
                at -= holdSteps;
            }

            return points[points.Length - 1];
        }

        /// <summary>The same walk, backward from the far end toward <c>points[0]</c>.</summary>
        static Vector2 AlongReversed(Vector2[] points, int[] legSteps, int holdSteps, int at)
        {
            for (int j = legSteps.Length - 1; j >= 0; j--)
            {
                if (at < legSteps[j])
                    return Vector2.Lerp(points[j + 1], points[j], (float)at / legSteps[j]);
                at -= legSteps[j];

                if (at < holdSteps) return points[j];
                at -= holdSteps;
            }

            return points[0];
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        /// <summary>Never negative, unlike C#'s %. A phase can push a step below zero.</summary>
        static int Mod(int value, int m) => m <= 0 ? 0 : ((value % m) + m) % m;
    }
}
