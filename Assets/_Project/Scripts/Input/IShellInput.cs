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

        /// <summary>
        /// Opens the level editor for the highlighted level select entry. One-shot edge, same
        /// shape as Confirm — LEVEL_EDITOR-gated at the call site, not here, so this interface
        /// stays the same shape whether or not the editor is compiled in.
        /// </summary>
        bool EditPressed { get; }

        bool FastForwardHeld { get; }

        /// <summary>
        /// Dev-only: hold to wipe the save. Held rather than an edge so the view can require a
        /// deliberate hold — a tap on a destructive key is not an intent. LEVEL_EDITOR-gated at
        /// the call site, not here, so this interface is the same shape in every build (the
        /// same rule EditPressed follows).
        /// </summary>
        bool WipeSaveHeld { get; }

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
        bool _edit;
        bool _fastForward;
        bool _wipeSave;

        public Vector2Int Navigate { get; private set; }
        public bool Confirm { get; private set; }
        public bool Back { get; private set; }
        public bool EditPressed { get; private set; }
        public bool FastForwardHeld { get; private set; }
        public bool WipeSaveHeld { get; private set; }

        public void Press(Vector2Int navigate, bool confirm, bool back, bool fastForward = false,
                          bool wipeSave = false)
        {
            _navigate = navigate;
            _confirm = confirm;
            _back = back;
            _fastForward = fastForward;
            _wipeSave = wipeSave;
        }

        /// <summary>Separate from Press because EditPressed is exercised by its own test only.</summary>
        public void PressEdit()
        {
            _edit = true;
        }

        public void Latch() { }

        public void Sample()
        {
            Navigate = _navigate;
            Confirm = _confirm;
            Back = _back;
            EditPressed = _edit;

            // Held, not consumed: it stays true until the caller says otherwise.
            FastForwardHeld = _fastForward;

            // Held, not consumed - see WipeSaveHeld on the interface.
            WipeSaveHeld = _wipeSave;

            // Consumed: one press moves the cursor once.
            _navigate = Vector2Int.zero;
            _confirm = false;
            _back = false;
            _edit = false;
        }
    }
}
