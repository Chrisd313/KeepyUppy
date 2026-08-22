using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// Player-vs-balloon contact, and the LOCKED GDD 4.2 rule that governs it:
    /// **passive contact loses energy; only active input adds it.**
    ///
    /// Without this, the optimal strategy in every level is to stand under the balloon and
    /// do nothing.
    ///
    /// The bump volume is the balloon's radius plus GDD 8's fairness bonus, so the player's
    /// reach is larger than the balloon looks.
    /// </summary>
    public static class PlayerBalloonContact
    {
        const float Skin = 1e-4f;

        /// <summary>
        /// Resolves at most one contact per step. Returns true if a hit was applied
        /// (separating overlaps are pushed apart without becoming a hit).
        /// </summary>
        public static bool Resolve(BalloonBody balloon, PlayerBody player, HitResolver resolver,
                                   in BalloonTuning t)
        {
            float reach = balloon.Radius + t.playerBumpRadiusBonus;

            float qx = Mathf.Clamp(balloon.Position.x, player.Position.x - player.HalfWidth,
                                                        player.Position.x + player.HalfWidth);
            float qy = Mathf.Clamp(balloon.Position.y, player.Position.y - player.HalfHeight,
                                                        player.Position.y + player.HalfHeight);

            float dx = balloon.Position.x - qx;
            float dy = balloon.Position.y - qy;
            float dist2 = dx * dx + dy * dy;
            if (dist2 >= reach * reach) return false;

            Vector2 normal = dist2 > 1e-12f
                ? new Vector2(dx, dy) / Mathf.Sqrt(dist2)
                : Vector2.up;   // dead centre: treat as landing on the head

            // GDD 6.5 contact-offset aiming, measured before the balloon is pushed clear so
            // the offset reflects where it actually struck.
            float span = player.HalfWidth + balloon.Radius;
            float aimOffset = span > 1e-6f
                ? Mathf.Clamp((balloon.Position.x - player.HeadCentre.x) / span, -1f, 1f)
                : 0f;

            Vector2 incoming = balloon.Velocity;
            float approach = incoming.x * normal.x + incoming.y * normal.y;

            // Push clear regardless, so one contact cannot be resolved twice.
            balloon.Position = new Vector2(qx + normal.x * (reach + Skin),
                                           qy + normal.y * (reach + Skin));

            bool active = player.Velocity.y > t.activeContactMinPlayerVy
                          || player.JumpBufferFrames > 0;

            if (active)
            {
                // Bump ignores the aim table entirely and is aimed purely by position
                // (GDD 6.5) — in every control profile, which is what makes A and C viable.
                resolver.ApplyHit(HitSource.JumpBump, Vector2.up, 1f, aimOffset);
                return true;
            }

            // Already separating: no hit, or a resting balloon would be damped every step.
            if (approach >= 0f) return false;

            Vector2 reflected = new Vector2(incoming.x - 2f * approach * normal.x,
                                            incoming.y - 2f * approach * normal.y);

            // LOCKED GDD 4.2: ~0.5 restitution. Strictly less than incoming, always.
            float outgoingSpeed = incoming.magnitude * t.restitutionPlayerPassive;
            resolver.ApplyHit(HitSource.PassiveContact, reflected, outgoingSpeed, 0f);
            return true;
        }
    }
}
