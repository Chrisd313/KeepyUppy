using KeepyUppy.InputLayer;
using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// Turns context intents into palm and spike hits (GDD 5.4 / 5.5).
    ///
    /// This is the only place that consults <see cref="ControlProfiles"/>, so switching
    /// profile mid-run is a config change and nothing downstream notices.
    ///
    /// Tap vs hold and both cooldowns are counted in frames, for the same reason coyote time
    /// is: at a fixed 60 Hz a frame count is exact.
    /// </summary>
    public sealed class PlayerVerbs
    {
        bool _holding;
        int _holdFrames;
        int _palmCooldown;
        int _spikeCooldown;

        /// <summary>True while the context button has been held past the spike threshold.</summary>
        public bool Charging { get; private set; }

        public void Reset()
        {
            _holding = false;
            _holdFrames = 0;
            _palmCooldown = 0;
            _spikeCooldown = 0;
            Charging = false;
        }

        public void Step(IInputSource input, ControlProfile profile, BalloonBody balloon,
                         PlayerBody player, HitResolver resolver, in BalloonTuning t)
        {
            if (_palmCooldown > 0) _palmCooldown--;
            if (_spikeCooldown > 0) _spikeCooldown--;

            if (input == null) return;

            ControlProfileConfig config = ControlProfiles.Of(profile);

            if (input.ContextPressed)
            {
                _holding = true;
                _holdFrames = 0;
            }
            else if (_holding && input.ContextHeld)
            {
                _holdFrames++;
            }

            Charging = _holding && _holdFrames >= t.contextHoldFrames;

            if (!_holding || input.ContextHeld) return;

            // Released. Under the threshold it was a tap (palm); at or over it, a charge
            // that fires the spike on release (GDD 5.5).
            bool spike = _holdFrames >= t.contextHoldFrames;
            _holding = false;
            Charging = false;

            // In profiles A and C the context button never strikes the balloon. In C it is
            // reserved for props, which do not exist yet.
            if (spike ? !config.spikeEnabled : !config.palmEnabled) return;

            if (spike ? _spikeCooldown > 0 : _palmCooldown > 0) return;

            if (!WithinReach(balloon, player, t)) return;

            ResolveAim(input.AimHint, t, out Vector2 direction, out float power);

            if (spike)
            {
                resolver.ApplyHit(HitSource.Spike, direction, power, 0f);
                _spikeCooldown = t.spikeCooldownFrames;
            }
            else
            {
                resolver.ApplyHit(HitSource.Palm, direction, power, 0f);
                _palmCooldown = t.palmCooldownFrames;
            }
        }

        /// <summary>GDD 5.4 "within arm's reach" — positioning still matters for the manual verbs.</summary>
        public static bool WithinReach(BalloonBody balloon, PlayerBody player, in BalloonTuning t)
        {
            Vector2 d = balloon.Position - player.HeadCentre;
            float limit = t.armReachRadius + balloon.Radius;
            return d.x * d.x + d.y * d.y <= limit * limit;
        }

        /// <summary>
        /// GDD 5.5's aim-resolution table. There is no aim stick on keyboard, so held
        /// direction resolves the angle:
        ///   nothing → straight up; left/right → off vertical; up → up, extra power;
        ///   down → downward smash.
        /// </summary>
        public static void ResolveAim(Vector2 aimHint, in BalloonTuning t,
                                      out Vector2 direction, out float power)
        {
            power = 1f;

            if (aimHint.y > 0.5f)
            {
                direction = Vector2.up;
                power = 1f + t.aimUpPowerBonus;
                return;
            }

            if (aimHint.y < -0.5f)
            {
                direction = Vector2.down;
                return;
            }

            if (aimHint.x > 0.5f)
            {
                direction = HitResolver.Rotate(Vector2.up, t.aimSideAngle * Mathf.Deg2Rad);
                return;
            }

            if (aimHint.x < -0.5f)
            {
                direction = HitResolver.Rotate(Vector2.up, -t.aimSideAngle * Mathf.Deg2Rad);
                return;
            }

            direction = Vector2.up;
        }
    }
}
