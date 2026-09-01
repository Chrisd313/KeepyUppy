using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// Swept circle vs static tile grid (GDD 6.1).
    ///
    /// The balloon's movement is cast, never applied and then corrected. A small collider
    /// at 16 m/s tunnels through a thin spike at 60 Hz if you integrate first and test after.
    ///
    /// Pure static maths: no engine physics, no allocation, no ordering dependence on
    /// anything outside its arguments.
    /// </summary>
    public static class SweptCircle
    {
        /// <summary>Gap left between the circle and a surface after a resolved hit.</summary>
        const float Skin = 1e-4f;

        /// <summary>Bounces resolved within one fixed step before the remaining motion is dropped.</summary>
        const int MaxIterations = 4;

        const float Epsilon = 1e-12f;

        /// <summary>Normal.y at or above which a surface counts as floor rather than wall.</summary>
        public const float FloorNormalThreshold = 0.5f;

        /// <summary>
        /// Advance <paramref name="pos"/> by <paramref name="vel"/> for one step, resolving
        /// bounces against solid tiles. <paramref name="vel"/> is rewritten with the
        /// post-bounce velocity.
        /// </summary>
        public static SweepResult Move(TileGrid grid, in BalloonTuning tuning, float radius,
                                       ref Vector2 pos, ref Vector2 vel, float dt)
        {
            Depenetrate(grid, radius, ref pos);

            var result = default(SweepResult);
            int contacts = 0;
            float timeLeft = dt;

            for (int i = 0; i < MaxIterations && timeLeft > 0f; i++)
            {
                Vector2 delta = new Vector2(vel.x * timeLeft, vel.y * timeLeft);

                if (!Cast(grid, pos, delta, radius, out float tHit, out Vector2 normal,
                          out TileKind kind, out int tileX, out int tileY))
                {
                    pos = new Vector2(pos.x + delta.x, pos.y + delta.y);
                    result.contacts = contacts;
                    return result;
                }

                contacts++;
                result.lastNormal = normal;
                result.lastKind = kind;
                result.lastTileX = tileX;
                result.lastTileY = tileY;

                // A floor is a surface pushing upward. Walls and ceilings bounce; the floor
                // is where a balloon's run ends (GDD 4.1) - unless it is rubber, which is
                // the one upward-facing surface a balloon is allowed to land on.
                if (normal.y >= FloorNormalThreshold && kind != TileKind.Rubber)
                    result.hitFloor = true;
                pos = new Vector2(
                    pos.x + delta.x * tHit + normal.x * Skin,
                    pos.y + delta.y * tHit + normal.y * Skin);

                float vn = vel.x * normal.x + vel.y * normal.y;
                if (vn < 0f)
                {
                    float approach = -vn;

                    // Rubber SETS the outgoing speed, for the same reason ApplyHit does
                    // (GDD 5.1): a bounce that scaled with what came in would shrink, and
                    // min_bounce_speed would eventually zero one - parking the balloon on a
                    // tile that by design cannot kill it. min_bounce_speed is skipped here
                    // for that reason, not by oversight.
                    //
                    // Only the normal component is replaced; the tangential term below is
                    // untouched, so a balloon crossing a rubber floor keeps its travel and a
                    // rubber wall throws it straight back.
                    float outward;
                    if (kind == TileKind.Rubber)
                    {
                        outward = tuning.rubberLaunchSpeed;
                    }
                    else
                    {
                        // GDD 6.4 min_bounce_speed: below this there is no bounce at all,
                        // otherwise a resting balloon jitters forever on ever-smaller hops.
                        outward = approach < tuning.minBounceSpeed
                            ? 0f
                            : approach * tuning.restitutionWall;
                    }

                    vel = new Vector2(
                        vel.x - normal.x * vn + normal.x * outward,
                        vel.y - normal.y * vn + normal.y * outward);
                }

                timeLeft *= 1f - tHit;
            }

            // Iterations exhausted in a corner. Drop the remainder rather than apply it
            // unchecked — losing a fraction of a millimetre of travel is always preferable
            // to passing through geometry.
            result.contacts = contacts;
            return result;
        }

        /// <summary>
        /// Earliest solid tile struck by the circle sweeping from <paramref name="p"/> along
        /// <paramref name="d"/>. <paramref name="t"/> is a fraction of d in [0, 1].
        /// </summary>
        public static bool Cast(TileGrid grid, Vector2 p, Vector2 d, float r,
                                out float t, out Vector2 normal,
                                out TileKind kind, out int tileX, out int tileY)
        {
            t = float.MaxValue;
            normal = Vector2.zero;
            kind = TileKind.Empty;
            tileX = 0;
            tileY = 0;
            bool found = false;

            float minX = Mathf.Min(p.x, p.x + d.x) - r;
            float maxX = Mathf.Max(p.x, p.x + d.x) + r;
            float minY = Mathf.Min(p.y, p.y + d.y) - r;
            float maxY = Mathf.Max(p.y, p.y + d.y) + r;

            // Clamped to one tile outside the room: out-of-bounds reads as solid, so this
            // range always contains a closing shell.
            int x0 = Mathf.Clamp(Mathf.FloorToInt(minX), -1, TileGrid.Width);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(maxX), -1, TileGrid.Width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minY), -1, TileGrid.Height);
            int y1 = Mathf.Clamp(Mathf.FloorToInt(maxY), -1, TileGrid.Height);

            // Fixed y-then-x iteration with a strict less-than: two tiles hit at exactly
            // the same time always resolve to the same one.
            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
                if (!grid.IsBlockingBalloon(tx, ty)) continue;

                if (SweepBox(p, d, r, tx, ty, tx + 1, ty + 1, out float ht, out Vector2 hn)
                    && ht < t)
                {
                    t = ht;
                    normal = hn;
                    kind = grid[tx, ty];
                    tileX = tx;
                    tileY = ty;
                    found = true;
                }
            }

            if (!found) t = 0f;
            return found;
        }

        /// <summary>
        /// Circle sweep against one axis-aligned box, solved as a ray against the box
        /// expanded by r with radius-r rounded corners.
        /// </summary>
        static bool SweepBox(Vector2 p, Vector2 d, float r,
                             float bx0, float by0, float bx1, float by1,
                             out float t, out Vector2 normal)
        {
            t = 0f;
            normal = Vector2.zero;

            float ex0 = bx0 - r, ey0 = by0 - r;
            float ex1 = bx1 + r, ey1 = by1 + r;

            // Started genuinely overlapping — tested against the box itself, not the
            // expanded one, so a circle sitting diagonally off a corner is not a false
            // positive. Depenetrate normally handles this; if a skin push has nudged us
            // into a neighbour mid-step, resolve it here rather than sweep past.
            if (Overlaps(p, r, bx0, by0, bx1, by1))
            {
                normal = ContactNormal(p, bx0, by0, bx1, by1);
                t = 0f;
                return true;
            }

            float tEnter = 0f;
            float tExit = 1f;
            Vector2 n = Vector2.zero;

            if (Mathf.Abs(d.x) < Epsilon)
            {
                if (p.x <= ex0 || p.x >= ex1) return false;
            }
            else
            {
                float inv = 1f / d.x;
                float t0 = (ex0 - p.x) * inv;
                float t1 = (ex1 - p.x) * inv;
                Vector2 n0 = new Vector2(-1f, 0f);
                Vector2 n1 = new Vector2(1f, 0f);
                if (t0 > t1) { (t0, t1) = (t1, t0); (n0, n1) = (n1, n0); }
                if (t0 > tEnter) { tEnter = t0; n = n0; }
                if (t1 < tExit) tExit = t1;
            }

            if (Mathf.Abs(d.y) < Epsilon)
            {
                if (p.y <= ey0 || p.y >= ey1) return false;
            }
            else
            {
                float inv = 1f / d.y;
                float t0 = (ey0 - p.y) * inv;
                float t1 = (ey1 - p.y) * inv;
                Vector2 n0 = new Vector2(0f, -1f);
                Vector2 n1 = new Vector2(0f, 1f);
                if (t0 > t1) { (t0, t1) = (t1, t0); (n0, n1) = (n1, n0); }
                if (t0 > tEnter) { tEnter = t0; n = n0; }
                if (t1 < tExit) tExit = t1;
            }

            if (tEnter > tExit || tEnter > 1f || tExit < 0f) return false;
            if (n.x == 0f && n.y == 0f) return false;

            Vector2 contact = new Vector2(p.x + d.x * tEnter, p.y + d.y * tEnter);

            // A face hit is only real if the contact point lies within that face's extent.
            // Otherwise the true first contact is the rounded corner.
            bool faceHit = n.x != 0f
                ? contact.y >= by0 && contact.y <= by1
                : contact.x >= bx0 && contact.x <= bx1;

            if (faceHit)
            {
                t = tEnter;
                normal = n;
                return true;
            }

            float cornerX, cornerY;
            if (n.x != 0f)
            {
                cornerX = n.x < 0f ? bx0 : bx1;
                cornerY = contact.y < by0 ? by0 : by1;
            }
            else
            {
                cornerY = n.y < 0f ? by0 : by1;
                cornerX = contact.x < bx0 ? bx0 : bx1;
            }

            return SweepCorner(p, d, r, cornerX, cornerY, out t, out normal);
        }

        /// <summary>Ray vs the radius-r circle sitting on a box corner.</summary>
        static bool SweepCorner(Vector2 p, Vector2 d, float r, float cx, float cy,
                                out float t, out Vector2 normal)
        {
            t = 0f;
            normal = Vector2.zero;

            float mx = p.x - cx;
            float my = p.y - cy;

            float a = d.x * d.x + d.y * d.y;
            if (a < Epsilon) return false;

            float b = 2f * (mx * d.x + my * d.y);
            float c = mx * mx + my * my - r * r;

            float disc = b * b - 4f * a * c;
            if (disc < 0f) return false;

            float root = Mathf.Sqrt(disc);
            float s = (-b - root) / (2f * a);
            if (s < 0f) s = (-b + root) / (2f * a);
            if (s < 0f || s > 1f) return false;

            float hx = p.x + d.x * s - cx;
            float hy = p.y + d.y * s - cy;
            float len = Mathf.Sqrt(hx * hx + hy * hy);
            if (len < Epsilon) return false;

            t = s;
            normal = new Vector2(hx / len, hy / len);
            return true;
        }

        /// <summary>
        /// Pushes the circle out of any solid tile it is overlapping. Normally a no-op —
        /// it exists so that a body teleported or spawned inside geometry recovers
        /// predictably instead of falling through the world.
        /// </summary>
        public static void Depenetrate(TileGrid grid, float r, ref Vector2 pos)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(pos.x - r), -1, TileGrid.Width);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(pos.x + r), -1, TileGrid.Width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(pos.y - r), -1, TileGrid.Height);
            int y1 = Mathf.Clamp(Mathf.FloorToInt(pos.y + r), -1, TileGrid.Height);

            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
                if (!grid.IsBlockingBalloon(tx, ty)) continue;

                float qx = Mathf.Clamp(pos.x, tx, tx + 1);
                float qy = Mathf.Clamp(pos.y, ty, ty + 1);

                float dx = pos.x - qx;
                float dy = pos.y - qy;
                float dist2 = dx * dx + dy * dy;
                if (dist2 >= r * r) continue;

                Vector2 n;
                float push;
                if (dist2 > Epsilon)
                {
                    float dist = Mathf.Sqrt(dist2);
                    n = new Vector2(dx / dist, dy / dist);
                    push = r - dist;
                }
                else
                {
                    // Centre is inside the tile: leave by the nearest face.
                    n = NearestFaceNormal(pos, tx, ty, tx + 1, ty + 1);
                    push = r + FaceDistance(pos, n, tx, ty, tx + 1, ty + 1);
                }

                pos = new Vector2(pos.x + n.x * (push + Skin), pos.y + n.y * (push + Skin));
            }
        }

        /// <summary>True when the circle actually intersects the box.</summary>
        static bool Overlaps(Vector2 p, float r, float bx0, float by0, float bx1, float by1)
        {
            float qx = Mathf.Clamp(p.x, bx0, bx1);
            float qy = Mathf.Clamp(p.y, by0, by1);
            float dx = p.x - qx;
            float dy = p.y - qy;
            return dx * dx + dy * dy < r * r;
        }

        /// <summary>
        /// Outward normal for an overlapping circle: away from the closest point on the box,
        /// falling back to the nearest face when the centre is inside.
        /// </summary>
        static Vector2 ContactNormal(Vector2 p, float bx0, float by0, float bx1, float by1)
        {
            float qx = Mathf.Clamp(p.x, bx0, bx1);
            float qy = Mathf.Clamp(p.y, by0, by1);
            float dx = p.x - qx;
            float dy = p.y - qy;
            float dist2 = dx * dx + dy * dy;

            if (dist2 <= Epsilon) return NearestFaceNormal(p, bx0, by0, bx1, by1);

            float dist = Mathf.Sqrt(dist2);
            return new Vector2(dx / dist, dy / dist);
        }

        /// <summary>Distance from p to the face named by an axis-aligned normal.</summary>
        static float FaceDistance(Vector2 p, Vector2 n, float bx0, float by0, float bx1, float by1)
        {
            if (n.x < 0f) return p.x - bx0;
            if (n.x > 0f) return bx1 - p.x;
            if (n.y < 0f) return p.y - by0;
            return by1 - p.y;
        }

        /// <summary>Nearest-face normal for a point at or inside a box.</summary>
        static Vector2 NearestFaceNormal(Vector2 p, float bx0, float by0, float bx1, float by1)
        {
            float left = p.x - bx0;
            float right = bx1 - p.x;
            float down = p.y - by0;
            float up = by1 - p.y;

            float best = left;
            Vector2 n = new Vector2(-1f, 0f);

            if (right < best) { best = right; n = new Vector2(1f, 0f); }
            if (down < best) { best = down; n = new Vector2(0f, -1f); }
            if (up < best) { n = new Vector2(0f, 1f); }

            return n;
        }
    }
}
