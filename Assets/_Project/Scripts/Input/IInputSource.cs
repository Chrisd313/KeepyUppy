using UnityEngine;

namespace KeepyUppy.InputLayer
{
    /// <summary>
    /// The input contract (GDD 5.5) — LOCKED.
    ///
    /// Emits intents, never raw keys. Downstream code, ApplyHit above all, must never be
    /// able to learn which device produced an intent.
    ///
    /// Two members extend the GDD 5.5 snippet; see the decision log entry dated 2026-08-22:
    ///   Sample(int)     — edges are per-render-frame, so a device must be given the chance
    ///                     to commit them to exactly one fixed step. Without it a press is
    ///                     silently lost whenever the frame rate falls below 60.
    ///   RestartPressed  — GDD 5.5's binding table already lists R = Restart, so restart is
    ///                     an intent. Reading R downstream instead would be the raw-key
    ///                     leak this interface exists to prevent.
    /// </summary>
    public interface IInputSource
    {
        /// <summary>-1 .. +1</summary>
        float MoveAxis { get; }

        /// <summary>True for exactly the one fixed step the press belongs to.</summary>
        bool JumpPressed { get; }

        bool JumpHeld { get; }

        /// <summary>True for exactly the one fixed step the press belongs to.</summary>
        bool ContextPressed { get; }

        bool ContextHeld { get; }

        /// <summary>Normalised; zero = no directional intent.</summary>
        Vector2 AimHint { get; }

        /// <summary>True for exactly the one fixed step the press belongs to.</summary>
        bool RestartPressed { get; }

        /// <summary>
        /// Commit one snapshot of intents for the given fixed step. Called once per step,
        /// before anything reads the properties above.
        /// </summary>
        void Sample(int step);

        /// <summary>Drop any latched edges. Called on restart so a buffered press cannot cross a life.</summary>
        void ResetIntents();
    }
}
