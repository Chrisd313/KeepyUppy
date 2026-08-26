using UnityEngine;
using UnityEngine.InputSystem;

namespace KeepyUppy.InputLayer.Devices
{
    /// <summary>
    /// Keyboard bindings for <see cref="IShellInput"/>: menu navigation and the time controls.
    ///
    /// Mirrors <see cref="KeyboardInput"/>'s structure — edges are latched in
    /// <see cref="Latch"/> (once per render frame) and committed in <see cref="Sample"/> (once
    /// per render frame, when a menu screen reads them), so a press is never lost to a slow
    /// frame and never fires twice.
    ///
    /// Fast-forward is bound to F, not Shift: left Shift is already the context verb in
    /// KeyboardInput. It is held, not an edge, and — unlike the edges above — it is refreshed
    /// in Latch() rather than only in Sample(), because the driver reads it during FixedUpdate,
    /// which can run more than once between renders.
    /// </summary>
    public sealed class KeyboardShellInput : IShellInput
    {
        Vector2Int _navigateLatched;
        bool _confirmLatched;
        bool _backLatched;

        /// <summary>
        /// While true, every intent reads neutral and no edge is latched. Set by the driver
        /// when a dev tool owns the keyboard, so typing into a text box cannot also open or
        /// close a menu. Nothing downstream of IShellInput can tell the difference.
        /// </summary>
        public bool Suppressed { get; set; }

        public Vector2Int Navigate { get; private set; }
        public bool Confirm { get; private set; }
        public bool Back { get; private set; }
        public bool FastForwardHeld { get; private set; }

        /// <summary>Call once per render frame, before the next fixed step runs.</summary>
        public void Latch()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || Suppressed)
            {
                FastForwardHeld = false;
                return;
            }

            int x = 0;
            int y = 0;

            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) x -= 1;
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) x += 1;
            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) y -= 1;
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) y += 1;

            if (x != 0) _navigateLatched = new Vector2Int(x, 0);
            if (y != 0) _navigateLatched = new Vector2Int(0, y);

            _confirmLatched |= kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame
                                || kb.numpadEnterKey.wasPressedThisFrame;

            _backLatched |= kb.escapeKey.wasPressedThisFrame;

            // Held, not latched: FixedUpdate can run more than once per render frame, and each
            // of those reads must see the key's live state, not a value stale since Sample().
            FastForwardHeld = kb.fKey.isPressed;
        }

        /// <summary>Commit the latched edges. Called once, when a menu screen reads them.</summary>
        public void Sample()
        {
            Keyboard kb = Keyboard.current;
            // No keyboard, or a dev tool owns it: everything reads neutral and any latched
            // edge is dropped rather than fired later.
            if (kb == null || Suppressed)
            {
                ResetIntents();
                return;
            }

            Navigate = _navigateLatched;
            Confirm = _confirmLatched;
            Back = _backLatched;

            _navigateLatched = Vector2Int.zero;
            _confirmLatched = false;
            _backLatched = false;
        }

        void ResetIntents()
        {
            _navigateLatched = Vector2Int.zero;
            _confirmLatched = false;
            _backLatched = false;

            Navigate = Vector2Int.zero;
            Confirm = false;
            Back = false;
            FastForwardHeld = false;
        }
    }
}
