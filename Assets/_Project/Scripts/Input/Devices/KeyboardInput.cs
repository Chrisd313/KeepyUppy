using UnityEngine;
using UnityEngine.InputSystem;

namespace KeepyUppy.InputLayer.Devices
{
    /// <summary>
    /// Keyboard bindings from GDD 5.5. Built and maintained first, before touch — balloon
    /// physics cannot be tuned through an on-screen thumb pad.
    ///
    /// Both binding sets are live simultaneously: arrows for right-handers, WASD for
    /// left-hand-on-keyboard players. Web players arrive with no patience for a controls
    /// screen.
    ///
    /// Presses are latched in <see cref="Latch"/> (once per render frame) and committed in
    /// <see cref="Sample"/> (once per fixed step), so a press is never lost to a slow frame
    /// and never fires on two steps.
    /// </summary>
    public sealed class KeyboardInput : IInputSource
    {
        bool _jumpLatched;
        bool _contextLatched;
        bool _restartLatched;

        /// <summary>
        /// While true, every intent reads neutral and no edge is latched. Set by the driver
        /// when a dev tool owns the keyboard, so typing into a text box cannot also drive the
        /// player. Nothing downstream of IInputSource can tell the difference.
        /// </summary>
        public bool Suppressed { get; set; }

        public float MoveAxis { get; private set; }
        public bool JumpPressed { get; private set; }
        public bool JumpHeld { get; private set; }
        public bool ContextPressed { get; private set; }
        public bool ContextHeld { get; private set; }
        public Vector2 AimHint { get; private set; }
        public bool RestartPressed { get; private set; }

        /// <summary>Call once per render frame, before the next fixed step runs.</summary>
        public void Latch()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || Suppressed) return;

            _jumpLatched |= kb.spaceKey.wasPressedThisFrame
                            || kb.wKey.wasPressedThisFrame
                            || kb.upArrowKey.wasPressedThisFrame;

            _contextLatched |= kb.jKey.wasPressedThisFrame
                               || kb.leftShiftKey.wasPressedThisFrame;

            _restartLatched |= kb.rKey.wasPressedThisFrame;
        }

        public void Sample(int step)
        {
            Keyboard kb = Keyboard.current;
            // No keyboard, or a dev tool owns it: everything reads neutral and any latched
            // edge is dropped rather than fired later.
            if (kb == null || Suppressed)
            {
                ResetIntents();
                return;
            }

            bool left = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
            bool right = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
            bool up = kb.upArrowKey.isPressed;
            bool down = kb.downArrowKey.isPressed;

            MoveAxis = (right ? 1f : 0f) - (left ? 1f : 0f);

            JumpHeld = kb.spaceKey.isPressed || kb.wKey.isPressed || kb.upArrowKey.isPressed;
            ContextHeld = kb.jKey.isPressed || kb.leftShiftKey.isPressed;

            // GDD 5.5 aim resolution: held movement supplies the horizontal hint, the up and
            // down arrows the vertical one. Palm and spike read this; bump never does.
            AimHint = new Vector2(MoveAxis, (up ? 1f : 0f) - (down ? 1f : 0f));

            JumpPressed = _jumpLatched;
            ContextPressed = _contextLatched;
            RestartPressed = _restartLatched;

            _jumpLatched = false;
            _contextLatched = false;
            _restartLatched = false;
        }

        public void ResetIntents()
        {
            _jumpLatched = false;
            _contextLatched = false;
            _restartLatched = false;

            MoveAxis = 0f;
            JumpPressed = JumpHeld = ContextPressed = ContextHeld = RestartPressed = false;
            AimHint = Vector2.zero;
        }
    }
}
