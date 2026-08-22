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

        public static SweepResult Move(TileGrid grid, float halfWidth, float halfHeight,
                                       ref Vector2 pos, ref Vector2 vel, float dt)
        {
            Depenetrate(grid, halfWidth, halfHeight, ref pos);

            var result = default(SweepResult);
            int contacts = 0;
            float timeLeft = dt;

            for (int i = 0; i < MaxIterations && timeLeft > 0f; i++)
            {
                Vector2 delta = new Vector2(vel.x * timeLeft, vel.y * timeLeft);

                if (!Cast(grid, pos, delta, halfWidth, halfHeight,
                          out float tHit, out Vector2 normal,
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

                timeLeft *= 1f - tHit;
            }

            result.contacts = contacts;
            return result;
        }

        public static bool Cast(TileGrid grid, Vector2 p, Vector2 d, float hx, float hy,
                                out float t, out Vector2 normal,
                                out TileKind kind, out int tileX, out int tileY)
        {
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

            if (!found) t = 0f;
            return found;
        }

        /// <summary>Ray vs the tile box expanded by the mover's half-extents (Minkowski sum).</summary>
        static bool SweepTile(Vector2 p, Vector2 d, float hx, float hy, int tx, int ty,
                              out float t, out Vector2 normal)
        {
            t = 0f;
            normal = Vector2.zero;

            float ex0 = tx - hx, ey0 = ty - hy;
            float ex1 = tx + 1 + hx, ey1 = ty + 1 + hy;

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
