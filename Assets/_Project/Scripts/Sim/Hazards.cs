using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// Which tiles kill what, and where their kill volume actually is.
    ///
    /// GDD 4.1 asymmetric fragility: the balloon dies to almost anything it touches; the
    /// player dies only to explicitly lethal geometry.
    ///
    /// GDD 8 fairness: every hazard's pop hitbox is noticeably *smaller* than its visual.
    /// The shrink is applied here and nowhere else, so the rule cannot be half-applied.
    ///
    /// Spikes are lethal on every face. Direction chooses which way the sprite points and
    /// nothing else — a spike kills whatever touches it, from whichever side it arrives.
    /// </summary>
    public static class Hazards
    {
        /// <summary>Never let a shrink erase a hitbox entirely.</summary>
        const float MinExtent = 0.02f;

        /// <summary>
        /// How far a spike's kill volume protrudes past its own tile, on every face.
        /// A body that a spike blocks — the player — is held a skin's width off the surface,
        /// so without this the kill volume it is standing on could never touch it.
        /// </summary>
        const float SurfaceBleed = 0.05f;

        public static bool KillsBalloon(TileKind kind)
            => kind == TileKind.Rough || TileGrid.IsSpike(kind);

        public static bool KillsPlayer(TileKind kind)
            => kind == TileKind.PlayerLethal || TileGrid.IsSpike(kind);

        /// <summary>Any lethal tile whose kill volume overlaps the balloon.</summary>
        public static bool BalloonOverlapsLethal(TileGrid grid, Vector2 centre, float radius, float shrink)
        {
            float r = Mathf.Max(radius - shrink, MinExtent);

            // Widened by one tile: a spike's kill volume protrudes past its own tile.
            int x0 = Mathf.FloorToInt(centre.x - r) - 1;
            int x1 = Mathf.FloorToInt(centre.x + r) + 1;
            int y0 = Mathf.FloorToInt(centre.y - r) - 1;
            int y1 = Mathf.FloorToInt(centre.y + r) + 1;

            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
                TileKind kind = grid[tx, ty];
                if (!KillsBalloon(kind)) continue;

                KillBox(kind, tx, ty, shrink, out float bx0, out float by0, out float bx1, out float by1);

                float qx = Mathf.Clamp(centre.x, bx0, bx1);
                float qy = Mathf.Clamp(centre.y, by0, by1);
                float dx = centre.x - qx;
                float dy = centre.y - qy;
                if (dx * dx + dy * dy < r * r) return true;
            }

            return false;
        }

        /// <summary>Any lethal tile whose kill volume overlaps the player's box.</summary>
        public static bool PlayerOverlapsLethal(TileGrid grid, Vector2 centre, float hx, float hy, float shrink)
        {
            int x0 = Mathf.FloorToInt(centre.x - hx) - 1;
            int x1 = Mathf.FloorToInt(centre.x + hx) + 1;
            int y0 = Mathf.FloorToInt(centre.y - hy) - 1;
            int y1 = Mathf.FloorToInt(centre.y + hy) + 1;

            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
                TileKind kind = grid[tx, ty];
                if (!KillsPlayer(kind)) continue;

                KillBox(kind, tx, ty, shrink, out float bx0, out float by0, out float bx1, out float by1);

                if (centre.x + hx > bx0 && centre.x - hx < bx1 &&
                    centre.y + hy > by0 && centre.y - hy < by1) return true;
            }

            return false;
        }

        /// <summary>
        /// The lethal volume of one tile. The whole tile either way — a spike bleeds out of
        /// every face, anything else is inset by the fairness shrink.
        ///
        /// The shrink does not apply to spikes at all. It cannot: the same bleed that lets a
        /// spike reach a player standing on it has to reach one pressed against its side, and
        /// insetting the other two faces is what made those sides safe. GDD 8 still holds for
        /// the balloon, which is shrunk by hazard_pop_radius_shrink at the caller — a margin
        /// three times the bleed, so the pop volume stays smaller than the visual.
        /// </summary>
        public static void KillBox(TileKind kind, int tx, int ty, float shrink,
                                   out float bx0, out float by0, out float bx1, out float by1)
        {
            float s = Mathf.Max(shrink, 0f);

            bx0 = tx + s;
            by0 = ty + s;
            bx1 = tx + 1f - s;
            by1 = ty + 1f - s;

            if (TileGrid.IsSpike(kind))
            {
                bx0 = tx - SurfaceBleed;
                by0 = ty - SurfaceBleed;
                bx1 = tx + 1f + SurfaceBleed;
                by1 = ty + 1f + SurfaceBleed;
            }

            if (bx1 - bx0 < MinExtent)
            {
                float mid = (bx0 + bx1) * 0.5f;
                bx0 = mid - MinExtent * 0.5f;
                bx1 = mid + MinExtent * 0.5f;
            }

            if (by1 - by0 < MinExtent)
            {
                float mid = (by0 + by1) * 0.5f;
                by0 = mid - MinExtent * 0.5f;
                by1 = mid + MinExtent * 0.5f;
            }
        }
    }
}
