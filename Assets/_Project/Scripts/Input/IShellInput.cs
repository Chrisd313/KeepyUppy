using UnityEngine;

namespace KeepyUppy.InputLayer
{
    /// <summary>
    /// Shell-level intents: menu navigation, and the time controls.
    ///
    /// Separate from IInputSource on purpose, and not only for tidiness: IInputSource is
    /// committed by Sample(int step) INSIDE SimWorld.Tick(), so an intent bound there cannot
    /// be read while the world is not ticking — a pause bound that way could be entered and
    /// never left. Nothing here is a simulation intent; no simulation code reads any of it.
    ///
    /// GDD 22, 2026-08-24 records why this reverses the earlier prediction that PausePressed
    /// would join IInputSource.
    ///
    /// Navigation is one step per press, never per frame held: a repeating cursor races
    /// across a level grid faster than anyone can read it. Fast-forward is the exception and
    /// is genuinely held.
    /// </summary>
    public interface IShellInput
    {
        /// <summary>One step, or zero. Up is +y, matching world space rather than screen space.</summary>
        Vector2Int Navigate { get; }

        bool Confirm { get; }
        bool Back { get; }

        bool FastForwardHeld { get; }

        /// <summary>Latch edges once per render frame.</summary>
        void Latch();

        /// <summary>Commit the latched edges. Called once, when the screen reads them.</summary>
        void Sample();
    }

    /// <summary>Replays scripted menu input. For tests; not a stub.</summary>
    public sealed class ScriptedShellInput : IShellInput
    {
        Vector2Int _navigate;
        bool _confirm;
        bool _back;
        bool _fastForward;

        public Vector2Int Navigate { get; private set; }
        public bool Confirm { get; private set; }
        public bool Back { get; private set; }
        public bool FastForwardHeld { get; private set; }

        public void Press(Vector2Int navigate, bool confirm, bool back, bool fastForward = false)
        {
            _navigate = navigate;
            _confirm = confirm;
            _back = back;
            _fastForward = fastForward;
        }

        public void Latch() { }

        public void Sample()
        {
            Navigate = _navigate;
            Confirm = _confirm;
            Back = _back;

            // Held, not consumed: it stays true until the caller says otherwise.
            FastForwardHeld = _fastForward;

            // Consumed: one press moves the cursor once.
            _navigate = Vector2Int.zero;
            _confirm = false;
            _back = false;
        }
    }
}
