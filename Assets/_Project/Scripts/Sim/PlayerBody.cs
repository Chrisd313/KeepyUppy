using KeepyUppy.InputLayer;
using KeepyUppy.Sim.Panels;
using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// The player (GDD 5.7). Plain C# class, integrated on the same 60 Hz fixed step as the
    /// balloon, moved with the same swept-collision guarantee.
    ///
    /// Consumes intents only. It cannot discover which device produced them.
    /// </summary>
    public sealed class PlayerBody
    {
        /// <summary>Centre of the body box.</summary>
        public Vector2 Position;
        public Vector2 Velocity;

        public bool Grounded;
        public bool Popped;

        /// <summary>GDD 5.7: 6 frames of grace after walking off a ledge.</summary>
        public int CoyoteFrames;

        /// <summary>
        /// GDD 5.7 / 8: a jump pressed up to 10 frames early still fires on arrival.
        /// Deliberately NOT cleared when the jump is consumed — GDD 4.2's active-contact test
        /// asks whether a jump was *pressed* recently, which is a question about the player's
        /// intent, not about whether the jump happened to find ground.
        /// </summary>
        public int JumpBufferFrames;

        /// <summary>Guards against one press producing two jumps across the buffer window.</summary>
        bool _jumpUsedForThisPress;

        /// <summary>True while rising from a jump that has not yet been cut or peaked.</summary>
        bool _rising;

        public float HalfWidth { get; private set; } = 0.40f;
        public float HalfHeight { get; private set; } = 0.80f;

        /// <summary>Where a bump comes from: the top-centre of the body (GDD 6.5).</summary>
        public Vector2 HeadCentre => new Vector2(Position.x, Position.y + HalfHeight);

        public void Reset(in PlayerTuning tuning, Vector2 spawn)
        {
            HalfWidth = tuning.halfWidth;
            HalfHeight = tuning.halfHeight;

            Position = spawn;
            Velocity = Vector2.zero;
            Grounded = false;
            Popped = false;
            CoyoteFrames = 0;
            JumpBufferFrames = 0;
            _jumpUsedForThisPress = false;
            _rising = false;
        }

        /// <summary>Grid only. Rooms with no moving panels, and the tests that predate them.</summary>
        public SweepResult Step(in PlayerTuning t, IInputSource input, TileGrid grid)
            => Step(t, input, grid, null);

        public SweepResult Step(in PlayerTuning t, IInputSource input, TileGrid grid,
                                SolidField solids)
        {
            // --- Input buffer (frames, never seconds) ---
            if (input != null && input.JumpPressed)
            {
                JumpBufferFrames = t.jumpBufferFrames;
                _jumpUsedForThisPress = false;
            }
            else if (JumpBufferFrames > 0)
            {
                JumpBufferFrames--;
            }

            float moveAxis = input?.MoveAxis ?? 0f;
            bool jumpHeld = input?.JumpHeld ?? false;

            // --- Horizontal ---
            float target = moveAxis * t.runSpeed;
            int accelFrames = Grounded ? t.groundAccelFrames : t.airAccelFrames;
            float maxDelta = accelFrames <= 0 ? Mathf.Abs(target - Velocity.x)
                                              : t.runSpeed / accelFrames;
            float vx = Mathf.MoveTowards(Velocity.x, target, maxDelta);

            float vy = Velocity.y;

            // --- Jump ---
            if (JumpBufferFrames > 0 && !_jumpUsedForThisPress && (Grounded || CoyoteFrames > 0))
            {
                vy = t.JumpVelocity;
                _jumpUsedForThisPress = true;
                _rising = true;
                Grounded = false;
                CoyoteFrames = 0;
            }

            // --- Jump cut: releasing early shortens the hop ---
            if (_rising && vy > 0f && !jumpHeld)
            {
                vy *= t.jumpCutMultiplier;
                _rising = false;
            }
            if (vy <= 0f) _rising = false;

            // --- Gravity ---
            vy -= t.gravity * SimClock.Dt;
            if (vy < -t.terminalFallSpeed) vy = -t.terminalFallSpeed;

            Velocity = new Vector2(vx, vy);

            // --- Move ---
            Vector2 pos = Position;
            Vector2 vel = Velocity;
            SweepResult result = SweptAabb.Move(grid, solids, HalfWidth, HalfHeight,
                                                ref pos, ref vel, SimClock.Dt);
            Position = pos;
            Velocity = vel;

            // --- Ground state and coyote ---
            bool wasGrounded = Grounded;
            Grounded = result.hitFloor && Velocity.y <= 0f;

            if (Grounded) CoyoteFrames = t.coyoteFrames;
            else if (wasGrounded) CoyoteFrames = t.coyoteFrames;   // just left the ledge
            else if (CoyoteFrames > 0) CoyoteFrames--;

            return result;
        }
    }
}
