using KeepyUppy.Shell;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Which screen is up, and what that implies for the simulation behind it.
    ///
    /// Plain C# so the transitions are testable without a scene — the MonoBehaviour on top
    /// only draws whichever screen this says is current.
    /// </summary>
    public sealed class AppScreenTests
    {
        [Test]
        public void StartsOnTheTitle()
        {
            var stack = new ScreenStack();

            Assert.AreEqual(AppScreen.Title, stack.Current);
            Assert.IsTrue(stack.SimRuns, "the title has the game running behind it");
            Assert.IsFalse(stack.SimPaused);
        }

        [Test]
        public void PausingKeepsTheGameBehindItAlive()
        {
            // Not a teardown: resuming must continue the same run, not restart it.
            var stack = new ScreenStack();
            stack.Go(AppScreen.Playing);
            stack.Go(AppScreen.Paused);

            Assert.IsTrue(stack.SimRuns, "the room is still there");
            Assert.IsTrue(stack.SimPaused, "it is just not advancing");
        }

        [Test]
        public void BackFromPauseResumesPlay()
        {
            var stack = new ScreenStack();
            stack.Go(AppScreen.Playing);
            stack.Go(AppScreen.Paused);

            stack.Back();

            Assert.AreEqual(AppScreen.Playing, stack.Current);
            Assert.IsFalse(stack.SimPaused);
        }

        [Test]
        public void BackFromLevelSelectReturnsToTheTitle()
        {
            var stack = new ScreenStack();
            stack.Go(AppScreen.LevelSelect);

            stack.Back();

            Assert.AreEqual(AppScreen.Title, stack.Current);
        }

        [Test]
        public void BackFromTheTitleDoesNothing()
        {
            // There is nowhere above the title. Quitting is not a menu action here.
            var stack = new ScreenStack();

            stack.Back();

            Assert.AreEqual(AppScreen.Title, stack.Current);
        }

        [Test]
        public void ResultsIsAnOverlayOverAFrozenRoom()
        {
            var stack = new ScreenStack();
            stack.Go(AppScreen.Playing);
            stack.Go(AppScreen.Results);

            Assert.IsTrue(stack.SimRuns);
            Assert.IsTrue(stack.SimPaused, "the cleared room is held, not torn down");
        }

        [Test]
        public void ChangesAreAnnouncedWithBothScreens()
        {
            // The view needs to know what it is leaving as well as what it is entering.
            var stack = new ScreenStack();

            AppScreen from = AppScreen.Results;
            AppScreen to = AppScreen.Results;
            stack.Changed += (a, b) => { from = a; to = b; };

            stack.Go(AppScreen.LevelSelect);

            Assert.AreEqual(AppScreen.Title, from);
            Assert.AreEqual(AppScreen.LevelSelect, to);
        }

        [Test]
        public void GoingToTheCurrentScreenAnnouncesNothing()
        {
            var stack = new ScreenStack();
            int changes = 0;
            stack.Changed += (a, b) => changes++;

            stack.Go(AppScreen.Title);

            Assert.AreEqual(0, changes);
        }

        [Test]
        public void ObserveWinEntersResultsOnTheRisingEdgeWhilePlaying()
        {
            var stack = new ScreenStack();
            stack.Go(AppScreen.Playing);

            stack.ObserveWin(false);
            Assert.AreEqual(AppScreen.Playing, stack.Current, "not yet won");

            stack.ObserveWin(true);
            Assert.AreEqual(AppScreen.Results, stack.Current);
        }

        [Test]
        public void ObserveWinDoesNotReenterResultsWhileStillWon()
        {
            // This is the bug the fix addresses: SimWorld.IsWon stays true until the room's
            // own win-hold finishes, which cannot happen while Results has paused the sim.
            // A level-triggered check (just "IsWon && Current == Playing") re-enters Results
            // the instant Back() reaches Playing, before a single step can run, and the
            // player is stuck bouncing between the two screens forever. Edge detection is
            // what stops that: the level staying true after the edge must be a no-op.
            var stack = new ScreenStack();
            stack.Go(AppScreen.Playing);
            stack.ObserveWin(true);
            Assert.AreEqual(AppScreen.Results, stack.Current);

            stack.Back();
            Assert.AreEqual(AppScreen.Playing, stack.Current, "back from results resumes play");

            stack.ObserveWin(true);

            Assert.AreEqual(AppScreen.Playing, stack.Current,
                "IsWon never dropped, so this is not a new win - must not re-enter Results");
        }

        [Test]
        public void ObserveWinDoesNothingOffThePlayingScreen()
        {
            var stack = new ScreenStack();
            // Still on Title - the demo room behind it winning must not pull up Results for
            // a room the player never chose to play.
            stack.ObserveWin(true);

            Assert.AreEqual(AppScreen.Title, stack.Current);
        }

        [Test]
        public void ObserveWinEntersResultsAgainOnASecondGenuineWin()
        {
            var stack = new ScreenStack();
            stack.Go(AppScreen.Playing);

            stack.ObserveWin(true);
            Assert.AreEqual(AppScreen.Results, stack.Current);

            stack.Back();
            Assert.AreEqual(AppScreen.Playing, stack.Current);

            // The room genuinely restarted and IsWon dropped back to false before rising
            // again - a second win, and it must be recognised as one.
            stack.ObserveWin(false);
            stack.ObserveWin(true);

            Assert.AreEqual(AppScreen.Results, stack.Current);
        }
    }
}
