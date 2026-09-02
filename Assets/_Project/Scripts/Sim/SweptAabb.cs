using KeepyUppy.Sim.Panels;
using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// Swept axis-aligned box vs static tile grid — the player's counterpart to
    /// <see cref="SweptCircle"/>.
    ///
    /// Same structure (cast, resolve earliest, repeat) and the same guarantee: movement is
    /// never applied and corrected afterwards. Two boxes need no rounded corners, so this is
    /// the simpler of the two; the circle version stays untouched so the balloon's tunnelling
    /// guarantees cannot regress through a shared edit.
    ///
    /// The player does not bounce. Contact zeroes the velocity component along the normal
    /// and keeps the tangent, which is what makes running along a wall feel right.
    /// </summary>
    public static class SweptAabb
    {
        const float Skin = 1e-4f;
        const int MaxIterations = 4;
        const float Epsilon = 1e-12f;

        /// <summary>Grid only. The rooms with no moving panels, and every existing caller.</summary>
        public static SweepResult Move(TileGrid grid, float halfWidth, float halfHeight,
                                       ref Vector2 pos, ref Vector2 vel, float dt)
            => Move(grid, null, halfWidth, halfHeight, ref pos, ref vel, dt);

        /// <summary>
        /// Grid and moving panels (moving-panels design).
        ///
        /// A panel is swept in its OWN frame - the body's delta minus the panel's - which is
        /// what makes a panel moving INTO a body a contact rather than a miss, and it is the
        /// only reason a body standing still on a rising floor is hit at all.
        ///
        /// Once a panel contact is resolved the body rides it for the rest of the step. That
        /// ride is folded into the body's DELTA, never into <paramref name="vel"/>: a player
        /// on a rising floor must jump exactly as high as one standing on the world. Carrying
        /// it in the delta rather than adding it to the position after the loop also keeps
        /// the swept guarantee - the carried metres are cast against the world like any other
        /// motion, so a rising panel cannot post a body through a ceiling.
        /// </summary>
        public static SweepResult Move(TileGrid grid, SolidField solids,
                                       float halfWidth, float halfHeight,
                                       ref Vector2 pos, ref Vector2 vel, float dt)
        {
            Depenetrate(grid, halfWidth, halfHeight, ref pos);

            var result = default(SweepResult);
            result.carrier = -1;
            int contacts = 0;
            float timeLeft = dt;

            // The panel the body is riding, in tiles per second. Zero until one is actually
            // contacted, so a room with no moving panels sweeps exactly as it always did.
            Vector2 carry = Vector2.zero;

            for (int i = 0; i < MaxIterations && timeLeft > 0f; i++)
            {
                Vector2 delta = new Vector2((vel.x + carry.x) * timeLeft,
                                            (vel.y + carry.y) * timeLeft);

                if (!Cast(grid, solids, pos, delta, halfWidth, halfHeight, timeLeft,
                          out float tHit, out Vector2 normal,
                          out TileKind kind, out int tileX, out int tileY, out int solidIndex))
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
                result.carrier = solidIndex;
                if (normal.y >= SweptCircle.FloorNormalThreshold) result.hitFloor = true;

                pos = new Vector2(
                    pos.x + delta.x * tHit + normal.x * Skin,
                    pos.y + delta.y * tHit + normal.y * Skin);

                float vn = vel.x * normal.x + vel.y * normal.y;
                if (vn < 0f)
                {
                    // Keep the tangent, kill the normal component. No restitution: a player
                    // who bounced off the floor would be unplayable.
                    vel = new Vector2(vel.x - normal.x * vn, vel.y - normal.y * vn);
                }

                // Adopt the panel just hit as the carrier. A tile contact leaves the current
                // one alone: walking into a wall must not throw the body off the floor it is
                // riding.
                if (solidIndex >= 0) carry = solids[solidIndex].velocity;

                timeLeft *= 1f - tHit;
            }

            result.contacts = contacts;
            return result;
        }

        /// <summary>Grid only. Kept for the callers that have no panels to offer.</summary>
        public static bool Cast(TileGrid grid, Vector2 p, Vector2 d, float hx, float hy,
                                out float t, out Vector2 normal,
                                out TileKind kind, out int tileX, out int tileY)
            => Cast(grid, null, p, d, hx, hy, 0f,
                    out t, out normal, out kind, out tileX, out tileY, out _);

        /// <summary>
        /// Earliest contact across the tiles and the moving panels. <paramref name="span"/> is
        /// the seconds this cast covers, which is what turns a panel's tiles-per-second into
        /// the metres it travels while the body travels <paramref name="d"/>.
        /// </summary>
        public static bool Cast(TileGrid grid, SolidField solids, Vector2 p, Vector2 d,
                                float hx, float hy, float span,
                                out float t, out Vector2 normal,
                                out TileKind kind, out int tileX, out int tileY,
                                out int solidIndex)
        {
            solidIndex = -1;
            t = float.MaxValue;
            normal = Vector2.zero;
            kind = TileKind.Empty;
            tileX = 0;
            tileY = 0;
            bool found = false;

            float minX = Mathf.Min(p.x, p.x + d.x) - hx;
            float maxX = Mathf.Max(p.x, p.x + d.x) + hx;
            float minY = Mathf.Min(p.y, p.y + d.y) - hy;
            float maxY = Mathf.Max(p.y, p.y + d.y) + hy;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(minX), -1, TileGrid.Width);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(maxX), -1, TileGrid.Width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(minY), -1, TileGrid.Height);
            int y1 = Mathf.Clamp(Mathf.FloorToInt(maxY), -1, TileGrid.Height);

            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
                if (!grid.IsBlockingPlayer(tx, ty)) continue;

                if (SweepTile(p, d, hx, hy, tx, ty, out float ht, out Vector2 hn) && ht < t)
                {
                    t = ht;
                    normal = hn;
                    kind = grid[tx, ty];
                    tileX = tx;
                    tileY = ty;
                    found = true;
                }
            }

            if (solids != null)
            {
                for (int i = 0; i < solids.Count; i++)
                {
                    MovingSolid solid = solids[i];

                    // Relative velocity: resolving in the panel's frame is what makes a panel
                    // moving INTO the body a contact rather than a miss.
                    Vector2 rel = new Vector2(d.x - solid.velocity.x * span,
                                              d.y - solid.velocity.y * span);

                    if (!SweepRect(p, rel, hx, hy,
                                   solid.rect.xMin, solid.rect.yMin,
                                   solid.rect.xMax, solid.rect.yMax,
                                   out float ht, out Vector2 hn)) continue;
                    if (ht >= t) continue;

                    t = ht;
                    normal = hn;
                    kind = TileKind.Solid;
                    tileX = 0;
                    tileY = 0;
                    solidIndex = i;
                    found = true;
                }
            }

            if (!found) t = 0f;
            return found;
        }

        /// <summary>Ray vs the tile box expanded by the mover's half-extents (Minkowski sum).</summary>
        static bool SweepTile(Vector2 p, Vector2 d, float hx, float hy, int tx, int ty,
                              out float t, out Vector2 normal)
            => SweepRect(p, d, hx, hy, tx, ty, tx + 1, ty + 1, out t, out normal);

        /// <summary>
        /// The same Minkowski sweep against an arbitrary box, so a panel and a tile share one
        /// piece of maths instead of two that can drift apart.
        /// </summary>
        static bool SweepRect(Vector2 p, Vector2 d, float hx, float hy,
                              float bx0, float by0, float bx1, float by1,
                              out float t, out Vector2 normal)
        {
            t = 0f;
            normal = Vector2.zero;

            float ex0 = bx0 - hx, ey0 = by0 - hy;
            float ex1 = bx1 + hx, ey1 = by1 + hy;

            if (p.x > ex0 && p.x < ex1 && p.y > ey0 && p.y < ey1)
            {
                // Already overlapping. Depenetrate handles the normal case; resolve here so
                // a mid-step skin nudge cannot sweep straight through a neighbour.
                normal = NearestFaceNormal(p, ex0, ey0, ex1, ey1);
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

            t = tEnter;
            normal = n;
            return true;
        }

        public static void Depenetrate(TileGrid grid, float hx, float hy, ref Vector2 pos)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(pos.x - hx), -1, TileGrid.Width);
            int x1 = Mathf.Clamp(Mathf.FloorToInt(pos.x + hx), -1, TileGrid.Width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(pos.y - hy), -1, TileGrid.Height);
            int y1 = Mathf.Clamp(Mathf.FloorToInt(pos.y + hy), -1, TileGrid.Height);

            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
                if (!grid.IsBlockingPlayer(tx, ty)) continue;

                float overlapX = (hx + 0.5f) - Mathf.Abs(pos.x - (tx + 0.5f));
                float overlapY = (hy + 0.5f) - Mathf.Abs(pos.y - (ty + 0.5f));
                if (overlapX <= 0f || overlapY <= 0f) continue;

                if (overlapX < overlapY)
                {
                    float sign = pos.x < tx + 0.5f ? -1f : 1f;
                    pos = new Vector2(pos.x + sign * (overlapX + Skin), pos.y);
                }
                else
                {
                    float sign = pos.y < ty + 0.5f ? -1f : 1f;
                    pos = new Vector2(pos.x, pos.y + sign * (overlapY + Skin));
                }
            }
        }

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
