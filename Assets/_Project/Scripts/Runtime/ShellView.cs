using System.Collections.Generic;
using KeepyUppy.InputLayer.Devices;
using KeepyUppy.Shell;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Draws whichever screen <see cref="ScreenStack"/> says is current, and wires shell
    /// input to it. Every decision — what Back means, where the cursor may move — lives in
    /// <see cref="ScreenStack"/> and <see cref="LevelSelectModel"/>, both tested; this class
    /// only samples input, calls into them, and redraws text when their state changes.
    ///
    /// <see cref="Bootstrap"/> is assigned by <see cref="SimBootstrap"/> right after this
    /// component is added — after AddComponent runs Awake, so nothing here may depend on it
    /// until Update. There is no level session on the menus, so every read through
    /// <c>Bootstrap.Session</c> must tolerate null: that is the normal state here, not an
    /// error case.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShellView : MonoBehaviour
    {
        public SimBootstrap Bootstrap;

        readonly ScreenStack _stack = new ScreenStack();
        readonly KeyboardShellInput _input = new KeyboardShellInput();
        LevelSelectModel _levelSelect;
        bool _initialized;

        // Pause's options never change, so it is built once. Results' options depend on
        // whether a next level exists, so it is rebuilt on every entry - see OnScreenChanged.
        readonly OptionMenu _pause = new OptionMenu(ShellAction.Resume, ShellAction.Restart, ShellAction.QuitToLevels);
        readonly OptionMenu _results = new OptionMenu();

        // What the last redraw drew, so Update only rebuilds quads when something visible
        // actually changed (CLAUDE.md: a redraw destroys and rebuilds a quad per lit pixel).
        GameObject _drawn;
        AppScreen? _drawnScreen;
        int _drawnWorldCursor = -1;
        int _drawnLevelCursor = -1;
        int _drawnPauseCursor = -1;
        int _drawnResultsCursor = -1;

        void Awake()
        {
            // Fired only on an actual screen change (ScreenStack.Go is a no-op otherwise),
            // so this runs once per entry into Results rather than every frame.
            _stack.Changed += OnScreenChanged;
        }

        void Update()
        {
            EnsureInitialized();

            // Same suppression rule as every other keyboard consumer (DevInputCapture):
            // a dev tool with focus must not also drive the menu.
            _input.Suppressed = DevInputCapture.KeyboardCaptured;
            _input.Latch();
            _input.Sample();

            // No session exists on the menus (SimBootstrap builds one only on StartLevel),
            // so every driver-dependent read or write here is skipped rather than defaulted -
            // there is nothing to observe a win on, fast-forward or pause.
            SimDriver driver = Bootstrap != null ? Bootstrap.Session?.Driver : null;

            if (driver != null)
            {
                // Edge detection lives in ScreenStack, not here — see ObserveWin's comment
                // for why a level-triggered check in this file is the bug this method fixes.
                _stack.ObserveWin(driver.World.IsWon);
            }

            HandleInput();

            if (driver != null)
            {
                driver.FastForward = _input.FastForwardHeld;
                // Own only the shell's pause reason - the editor owns EditorPaused
                // separately (BootstrapEditorHost.SetSimPaused), so neither can stomp the
                // other's freeze.
                driver.ShellPaused = _stack.SimPaused;
            }

            RedrawIfNeeded();
        }

        /// <summary>
        /// The manifest only needs loading once. Deferred out of Awake because Bootstrap is
        /// not set until after AddComponent returns (see the class comment).
        /// </summary>
        void EnsureInitialized()
        {
            if (_initialized || Bootstrap == null || Bootstrap.Levels == null) return;

            LevelIndex index = Bootstrap.Levels.TryLoadIndex(out LevelIndex loaded) ? loaded : new LevelIndex();
            _levelSelect = new LevelSelectModel(index);
            _initialized = true;
        }

        void HandleInput()
        {
            switch (_stack.Current)
            {
                case AppScreen.Title:
                    if (_input.Confirm) _stack.Go(AppScreen.LevelSelect);
                    break;

                case AppScreen.LevelSelect:
                    if (_input.Navigate != Vector2Int.zero) _levelSelect?.Move(_input.Navigate);
                    if (_input.Confirm) ConfirmLevelSelect();
                    if (_input.Back) _stack.Back();
                    break;

                case AppScreen.Playing:
                    // Esc during play pauses, it does not exit to the level select - see the
                    // class comment on the Paused case. ScreenStack.Back()'s own
                    // Playing -> LevelSelect mapping is left as-is (it is what an in-menu
                    // "quit to select" action would use); this is the shell explicitly asking
                    // for a different screen, not asking "back" to decide for it.
                    if (_input.Back) _stack.Go(AppScreen.Paused);
                    break;

                case AppScreen.Paused:
                    if (_input.Navigate != Vector2Int.zero) _pause.Move(_input.Navigate);
                    if (_input.Confirm) Perform(_pause.Selected);
                    // Back always resumes regardless of cursor position - Esc is "get me out
                    // of this menu", not "confirm whatever is highlighted".
                    if (_input.Back) _stack.Back();
                    break;

                case AppScreen.Results:
                    if (_input.Navigate != Vector2Int.zero) _results.Move(_input.Navigate);
                    if (_input.Confirm) Perform(_results.Selected);
                    // ScreenStack.Back() would return to Playing, but the room is finished -
                    // there is nothing left to play. Back here means the same thing as
                    // choosing "quit to levels" from the menu.
                    if (_input.Back) Perform(ShellAction.QuitToLevels);
                    break;
            }
        }

        /// <summary>
        /// Runs whatever a confirmed menu option means. Every branch's rule already lives in
        /// a tested class (OptionMenu for the labels, ScreenStack for the transitions,
        /// LevelIndex for what comes next) - this only sequences the calls.
        /// </summary>
        void Perform(ShellAction action)
        {
            switch (action)
            {
                case ShellAction.Resume:
                    _stack.Back();
                    break;

                case ShellAction.Restart:
                case ShellAction.Retry:
                    // Both reset the win edge, same reason QuitToLevels does: the room is
                    // about to be winnable again and its previous win must not read as
                    // already-true on the very next observed step (ScreenStack.ResetWin).
                    Bootstrap.Session?.Restart();
                    _stack.ResetWin();
                    _stack.Go(AppScreen.Playing);
                    break;

                case ShellAction.NextLevel:
                    StartLevel(Bootstrap.Index?.NextAfter(Bootstrap.Session?.LevelId));
                    break;

                case ShellAction.QuitToLevels:
                    // The session must not linger behind the menus - see the class comment.
                    Bootstrap.EndLevel();
                    _stack.QuitToLevels();
                    break;
            }
        }

        /// <summary>
        /// Results' option list depends on whether a next level exists, which is only known
        /// at the moment Results is entered - so it is built here, on the transition, rather
        /// than every frame.
        /// </summary>
        void OnScreenChanged(AppScreen from, AppScreen to)
        {
            if (to != AppScreen.Results) return;

            string next = Bootstrap?.Index?.NextAfter(Bootstrap?.Session?.LevelId);

            _results.SetOptions(next == null
                ? new[] { ShellAction.Retry, ShellAction.QuitToLevels }
                : new[] { ShellAction.NextLevel, ShellAction.Retry, ShellAction.QuitToLevels });
        }

        void ConfirmLevelSelect()
        {
            if (_levelSelect == null || _levelSelect.IsEmpty || Bootstrap == null) return;

            StartLevel(_levelSelect.CurrentLevel);
        }

        /// <summary>
        /// Builds a level's session through the bootstrap - the same construction path
        /// SimBootstrap.StartLevel uses everywhere, including its own former boot path.
        /// Only goes to Playing if a session actually came back; a failed load (or a null id,
        /// e.g. NextAfter at the end of a world) leaves the shell wherever it already was
        /// rather than moving to a screen with nothing running behind it.
        /// </summary>
        void StartLevel(string id)
        {
            if (id == null || Bootstrap == null) return;
            if (Bootstrap.StartLevel(id) == null) return;

            // A win carried over from whatever was playing before must not skip Results on
            // this level's very first observed step - see ScreenStack.ResetWin.
            _stack.ResetWin();
            _stack.Go(AppScreen.Playing);
        }

        void RedrawIfNeeded()
        {
            bool changed = _stack.Current != _drawnScreen;

            if (_stack.Current == AppScreen.LevelSelect && _levelSelect != null)
            {
                changed |= _levelSelect.WorldCursor != _drawnWorldCursor
                           || _levelSelect.LevelCursor != _drawnLevelCursor;
            }

            if (_stack.Current == AppScreen.Paused)
                changed |= _pause.Cursor != _drawnPauseCursor;

            if (_stack.Current == AppScreen.Results)
                changed |= _results.Cursor != _drawnResultsCursor;

            if (!changed) return;

            TextView.Clear(_drawn);
            _drawn = BuildScreen(_stack.Current);

            _drawnScreen = _stack.Current;
            _drawnWorldCursor = _levelSelect?.WorldCursor ?? -1;
            _drawnLevelCursor = _levelSelect?.LevelCursor ?? -1;
            _drawnPauseCursor = _pause.Cursor;
            _drawnResultsCursor = _results.Cursor;
        }

        /// <summary>One container per redraw, so a single Destroy tears every quad down.</summary>
        GameObject BuildScreen(AppScreen screen)
        {
            // Playing draws nothing: the existing debug overlay stands in until phase C.
            if (screen == AppScreen.Playing) return null;

            var root = new GameObject($"ShellText_{screen}");
            root.transform.SetParent(transform, false);

            switch (screen)
            {
                case AppScreen.Title: DrawTitle(root.transform); break;
                case AppScreen.LevelSelect: DrawLevelSelect(root.transform); break;
                case AppScreen.Paused: DrawPaused(root.transform); break;
                case AppScreen.Results: DrawResults(root.transform); break;
            }

            return root;
        }

        static void DrawTitle(Transform parent)
        {
            TextView.Draw(parent, "KEEPY UPPY", new Vector2(7f, 13f), SimPalette.Text, scale: 2);
            TextView.Draw(parent, "PRESS SPACE", new Vector2(10f, 9f), SimPalette.TextDim);
        }

        void DrawLevelSelect(Transform parent)
        {
            if (_levelSelect == null || _levelSelect.IsEmpty)
            {
                TextView.Draw(parent, "NO LEVELS", new Vector2(10f, 10f), SimPalette.TextDim);
                return;
            }

            string worldName = (_levelSelect.CurrentWorld?.name ?? "").ToUpperInvariant();
            TextView.Draw(parent, worldName, new Vector2(4f, 16f), SimPalette.Text, scale: 2);

            // No scrolling and no clamp: past nine entries this list runs off the bottom of
            // the 640x360 screen. Worlds are planned at ~10 levels (GDD), so this is a known
            // future defect, not an oversight - w1 only has two levels today, which is why it
            // hasn't been hit. Deliberately not fixed here; scrolling is deferred.
            IReadOnlyList<string> levels = _levelSelect.CurrentWorldLevels;
            float y = 13f;
            for (int i = 0; i < levels.Count; i++)
            {
                bool current = i == _levelSelect.LevelCursor;
                Color colour = current ? SimPalette.Text : SimPalette.TextDim;
                string line = (current ? "> " : "  ") + levels[i];
                TextView.Draw(parent, line, new Vector2(4f, y), colour);
                y -= 1.5f;
            }

            // Only worth the hint when there is somewhere else to go.
            if (_levelSelect.WorldCount > 1)
                TextView.Draw(parent, "< WORLD >", new Vector2(4f, 2f), SimPalette.TextDim);
        }

        void DrawPaused(Transform parent)
        {
            TextView.Draw(parent, "PAUSED", new Vector2(12f, 13f), SimPalette.Text, scale: 2);
            DrawOptions(parent, _pause, 9f);
        }

        void DrawResults(Transform parent)
        {
            TextView.Draw(parent, "ROOM CLEARED", new Vector2(8f, 13f), SimPalette.Text, scale: 2);

            // Results is only reached via the win edge, which only fires with a session
            // running (see Update) - but Session can still vanish between the edge and this
            // redraw, so read the touch count defensively rather than assuming it survives.
            int touches = Bootstrap?.Session?.Driver.World.WinningTouches ?? 0;
            TextView.Draw(parent, $"{touches} TOUCHES", new Vector2(11f, 10f), SimPalette.TextDim);

            DrawOptions(parent, _results, 6.5f);
        }

        /// <summary>
        /// Shared by Paused and Results: one line per option, cursor marked the same way
        /// LevelSelect marks its own ("> " prefix) so the two menus read as the same kind of
        /// control.
        /// </summary>
        static void DrawOptions(Transform parent, OptionMenu menu, float topY)
        {
            float y = topY;
            for (int i = 0; i < menu.Count; i++)
            {
                bool current = i == menu.Cursor;
                Color colour = current ? SimPalette.Text : SimPalette.TextDim;
                string line = (current ? "> " : "  ") + OptionMenu.Label(menu.At(i));
                TextView.Draw(parent, line, new Vector2(9f, y), colour);
                y -= 1.5f;
            }
        }
    }
}
