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
    /// <see cref="Driver"/> and <see cref="LevelSource"/> are assigned by
    /// <see cref="SimBootstrap"/> right after this component is added — after AddComponent
    /// runs Awake, so nothing here may depend on them until Update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShellView : MonoBehaviour
    {
        public SimDriver Driver;
        public ILevelSource LevelSource;

        readonly ScreenStack _stack = new ScreenStack();
        readonly KeyboardShellInput _input = new KeyboardShellInput();
        LevelSelectModel _levelSelect;
        bool _initialized;

        // What the last redraw drew, so Update only rebuilds quads when something visible
        // actually changed (CLAUDE.md: a redraw destroys and rebuilds a quad per lit pixel).
        GameObject _drawn;
        AppScreen? _drawnScreen;
        int _drawnWorldCursor = -1;
        int _drawnLevelCursor = -1;

        void Update()
        {
            EnsureInitialized();
            if (Driver == null) return;

            // Same suppression rule as every other keyboard consumer (DevInputCapture):
            // a dev tool with focus must not also drive the menu.
            _input.Suppressed = DevInputCapture.KeyboardCaptured;
            _input.Latch();
            _input.Sample();

            // Edge detection lives in ScreenStack, not here — see ObserveWin's comment for
            // why a level-triggered check in this file is the bug this method fixes.
            _stack.ObserveWin(Driver.World.IsWon);
            HandleInput();

            Driver.FastForward = _input.FastForwardHeld;
            Driver.Paused = _stack.SimPaused;

            RedrawIfNeeded();
        }

        /// <summary>
        /// The manifest only needs loading once. Deferred out of Awake because Driver and
        /// LevelSource are not set until after AddComponent returns (see the class comment).
        /// </summary>
        void EnsureInitialized()
        {
            if (_initialized || LevelSource == null) return;

            LevelIndex index = LevelSource.TryLoadIndex(out LevelIndex loaded) ? loaded : new LevelIndex();
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
                    if (_input.Back) _stack.Back();
                    break;

                case AppScreen.Paused:
                    // Confirm resumes here as well as Back — see the brief: pause is Back,
                    // but Space is also how everything else in the menu confirms.
                    if (_input.Confirm || _input.Back) _stack.Back();
                    break;

                case AppScreen.Results:
                    if (_input.Back) _stack.Back();
                    break;
            }
        }

        /// <summary>
        /// Loads the selected level through the same path the game always loads levels
        /// through (LevelLoader over ILevelSource — CLAUDE.md's shared-format rule) and hands
        /// the room straight to the driver's own input, exactly as SimBootstrap does at boot.
        /// </summary>
        void ConfirmLevelSelect()
        {
            if (_levelSelect == null || _levelSelect.IsEmpty || LevelSource == null) return;

            string id = _levelSelect.CurrentLevel;
            if (id == null) return;
            if (!LevelSource.TryLoad(id, out LevelData level)) return;

            LevelLoader.ApplyAndConfigure(level, Driver.World, Driver.Keyboard, SimBootstrap.Seed);

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

            if (!changed) return;

            TextView.Clear(_drawn);
            _drawn = BuildScreen(_stack.Current);

            _drawnScreen = _stack.Current;
            _drawnWorldCursor = _levelSelect?.WorldCursor ?? -1;
            _drawnLevelCursor = _levelSelect?.LevelCursor ?? -1;
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

        static void DrawPaused(Transform parent)
        {
            TextView.Draw(parent, "PAUSED", new Vector2(12f, 11f), SimPalette.Text, scale: 2);
            TextView.Draw(parent, "SPACE RESUME", new Vector2(9f, 8f), SimPalette.TextDim);
            TextView.Draw(parent, "ESC RESUME", new Vector2(10f, 7f), SimPalette.TextDim);
        }

        void DrawResults(Transform parent)
        {
            TextView.Draw(parent, "ROOM CLEARED", new Vector2(8f, 11f), SimPalette.Text, scale: 2);
            TextView.Draw(parent, $"{Driver.World.WinningTouches} TOUCHES", new Vector2(11f, 8f), SimPalette.TextDim);
        }
    }
}
