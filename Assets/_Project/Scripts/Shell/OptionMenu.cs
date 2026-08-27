using UnityEngine;

namespace KeepyUppy.Shell
{
    /// <summary>What a menu option does when confirmed. The view maps these to calls.</summary>
    public enum ShellAction
    {
        Resume,
        Restart,
        NextLevel,
        Retry,
        QuitToLevels,
    }

    /// <summary>
    /// A single column of options and a cursor over them, shared by the pause and results
    /// screens — they differ only in which options they offer.
    ///
    /// A rule, not drawing, so it lives here and is tested. The cursor clamps rather than
    /// wraps, matching LevelSelectModel: wrapping a three-item list reads as a jump.
    /// </summary>
    public sealed class OptionMenu
    {
        ShellAction[] _options;

        public OptionMenu(params ShellAction[] options)
        {
            SetOptions(options);
        }

        public int Cursor { get; private set; }

        public int Count => _options.Length;

        /// <summary>Falls back to Resume when empty — a harmless default rather than a throw.</summary>
        public ShellAction Selected => Count == 0 ? ShellAction.Resume : _options[Cursor];

        public ShellAction At(int index) => _options[index];

        /// <summary>
        /// Replaces the options and returns the cursor to the top. Results offers Next only
        /// when a next level exists, so its list changes between rooms and a kept cursor
        /// could point past the end of a shorter one.
        /// </summary>
        public void SetOptions(params ShellAction[] options)
        {
            _options = options ?? new ShellAction[0];
            Cursor = 0;
        }

        /// <summary>
        /// Moves one step. Only the vertical axis matters — these menus are a single column.
        /// Vector2Int.down is (0,-1) and the list reads top to bottom, so down is the next
        /// entry; the same inversion LevelSelectModel documents.
        /// </summary>
        public void Move(Vector2Int step)
        {
            if (Count == 0 || step.y == 0) return;

            Cursor = Mathf.Clamp(Cursor - step.y, 0, Count - 1);
        }

        /// <summary>The text the view draws for an option.</summary>
        public static string Label(ShellAction action)
        {
            switch (action)
            {
                case ShellAction.Resume:       return "RESUME";
                case ShellAction.Restart:      return "RESTART";
                case ShellAction.NextLevel:    return "NEXT LEVEL";
                case ShellAction.Retry:        return "RETRY";
                case ShellAction.QuitToLevels: return "QUIT TO LEVELS";
                default:                       return "?";
            }
        }
    }
}
