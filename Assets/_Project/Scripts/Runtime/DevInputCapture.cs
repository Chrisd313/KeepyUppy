namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Set while a dev tool has keyboard focus — a tuning panel text box, and later the level
    /// editor's fields.
    ///
    /// Without it, typing a profile name presses half the gameplay bindings: 'a' and 'd' walk,
    /// 'r' restarts the run, 't' closes the panel you are typing into, and digits switch the
    /// control profile. Every keyboard consumer checks this before acting.
    ///
    /// Deliberately ungated so non-dev code can honour it without an #if.
    /// </summary>
    public static class DevInputCapture
    {
        /// <summary>A tuning-panel text box has focus.</summary>
        public static bool TextFieldFocused;

        /// <summary>The level editor is in Edit mode — painting must not also drive the player.</summary>
        public static bool EditorModeActive;

        public static bool KeyboardCaptured => TextFieldFocused || EditorModeActive;
    }
}
