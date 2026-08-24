using System;

namespace KeepyUppy.Shell
{
    /// <summary>Which screen is up. The MonoBehaviour on top draws whichever this says is current.</summary>
    public enum AppScreen
    {
        Title,
        LevelSelect,
        Playing,
        Paused,
        Results,
    }

    /// <summary>
    /// Which screen is current, and what that implies for the simulation running behind it.
    ///
    /// A single current screen rather than a full stack of history, because the game only
    /// ever needs to know where "back" goes next - that mapping is fixed, not built from
    /// wherever the player happened to come from:
    ///
    /// | Screen      | SimRuns | SimPaused | Back goes to |
    /// |-------------|---------|-----------|--------------|
    /// | Title       | true    | false     | Title (no-op)|
    /// | LevelSelect | true    | false     | Title        |
    /// | Playing     | true    | false     | LevelSelect  |
    /// | Paused      | true    | true      | Playing      |
    /// | Results     | true    | true      | Playing      |
    ///
    /// SimRuns is true everywhere: the title screen has the demo room running behind it, so
    /// there is never a moment with no room at all. SimPaused is true only for Paused and
    /// Results - both are overlays over a room that must hold still rather than tear down,
    /// so that resuming (Back from Paused) or replaying (Back from Results) continues the
    /// same run instead of restarting it.
    /// </summary>
    public sealed class ScreenStack
    {
        public AppScreen Current { get; private set; } = AppScreen.Title;

        /// <summary>Edge state for <see cref="ObserveWin"/> - see its comment.</summary>
        bool _wasWon;

        /// <summary>The room behind the current screen is alive - true for every screen.</summary>
        public bool SimRuns => true;

        /// <summary>The room is held rather than advancing - true only for Paused and Results.</summary>
        public bool SimPaused => Current == AppScreen.Paused || Current == AppScreen.Results;

        /// <summary>Fired with (from, to) whenever the current screen actually changes.</summary>
        public event Action<AppScreen, AppScreen> Changed;

        /// <summary>Goes to the given screen. A no-op, with no event, if already there.</summary>
        public void Go(AppScreen screen)
        {
            if (screen == Current) return;

            AppScreen from = Current;
            Current = screen;
            Changed?.Invoke(from, Current);
        }

        /// <summary>
        /// Feeds the room's win state in, one bool per frame, and goes to Results on a
        /// false-to-true transition while on Playing.
        ///
        /// Edge-triggered rather than level-triggered on purpose: SimWorld.IsWon is cleared
        /// only by Restart(), and Restart() only fires after the win-hold's fixed steps run -
        /// steps that cannot run while Results has the sim paused. A level-triggered check
        /// (just "IsWon && Current == Playing") sees IsWon still true the instant Back()
        /// returns to Playing, immediately re-enters Results, which re-pauses the sim before
        /// a single step can run - the room can never finish its own hold-then-restart, and
        /// the player is stuck bouncing between the two screens. Tracking the previous value
        /// here, rather than in the view above, is what makes that failure mode a unit test
        /// instead of a bug found by hand.
        /// </summary>
        public void ObserveWin(bool isWon)
        {
            bool risingEdge = isWon && !_wasWon;
            _wasWon = isWon;

            if (risingEdge && Current == AppScreen.Playing) Go(AppScreen.Results);
        }

        /// <summary>Follows the fixed back-mapping in the class comment. Title has nowhere to go.</summary>
        public void Back()
        {
            switch (Current)
            {
                case AppScreen.Paused:
                case AppScreen.Results:
                    Go(AppScreen.Playing);
                    break;
                case AppScreen.LevelSelect:
                    Go(AppScreen.Title);
                    break;
                case AppScreen.Playing:
                    Go(AppScreen.LevelSelect);
                    break;
                case AppScreen.Title:
                default:
                    break;
            }
        }
    }
}
