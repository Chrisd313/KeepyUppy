using KeepyUppy.Shell;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The cursor the pause and results screens share. A list and an index — but it is a
    /// rule, so it lives here rather than in the untested view (see ScreenStack.ObserveWin
    /// for what happened the one time a rule leaked upward).
    /// </summary>
    public sealed class OptionMenuTests
    {
        [Test]
        public void StartsOnTheFirstOption()
        {
            var menu = new OptionMenu(ShellAction.Resume, ShellAction.Restart, ShellAction.QuitToLevels);

            Assert.AreEqual(0, menu.Cursor);
            Assert.AreEqual(ShellAction.Resume, menu.Selected);
            Assert.AreEqual(3, menu.Count);
        }

        [Test]
        public void DownMovesToTheNextOption()
        {
            // Vector2Int.down is (0,-1) and the list reads top to bottom, so down means the
            // next entry — the same convention LevelSelectModel uses.
            var menu = new OptionMenu(ShellAction.Resume, ShellAction.Restart);

            menu.Move(Vector2Int.down);

            Assert.AreEqual(ShellAction.Restart, menu.Selected);
        }

        [Test]
        public void TheCursorStopsAtTheEndsRatherThanWrapping()
        {
            // Same choice as the level select: wrapping a three-item list reads as a jump.
            var menu = new OptionMenu(ShellAction.Resume, ShellAction.Restart);

            menu.Move(Vector2Int.up);
            Assert.AreEqual(ShellAction.Resume, menu.Selected, "already at the top");

            menu.Move(Vector2Int.down);
            menu.Move(Vector2Int.down);
            Assert.AreEqual(ShellAction.Restart, menu.Selected, "already at the bottom");
        }

        [Test]
        public void HorizontalMovementDoesNothing()
        {
            var menu = new OptionMenu(ShellAction.Resume, ShellAction.Restart);

            menu.Move(Vector2Int.right);

            Assert.AreEqual(0, menu.Cursor, "these menus are a single column");
        }

        [Test]
        public void SettingOptionsResetsTheCursor()
        {
            // Results offers Next only when there is a next level, so its option list changes
            // between rooms. A stale cursor would point past the end of a shorter list.
            var menu = new OptionMenu(ShellAction.NextLevel, ShellAction.Retry, ShellAction.QuitToLevels);
            menu.Move(Vector2Int.down);
            menu.Move(Vector2Int.down);

            menu.SetOptions(ShellAction.Retry, ShellAction.QuitToLevels);

            Assert.AreEqual(0, menu.Cursor);
            Assert.AreEqual(ShellAction.Retry, menu.Selected);
        }

        [Test]
        public void AnEmptyMenuIsSurvivable()
        {
            var menu = new OptionMenu();

            Assert.AreEqual(0, menu.Count);
            Assert.DoesNotThrow(() => menu.Move(Vector2Int.down));
            Assert.AreEqual(ShellAction.Resume, menu.Selected, "a harmless default, not a throw");
        }

        [Test]
        public void EveryActionHasALabel()
        {
            // The view draws these. A missing one would render as an empty row.
            foreach (ShellAction action in System.Enum.GetValues(typeof(ShellAction)))
                Assert.IsNotEmpty(OptionMenu.Label(action), $"no label for {action}");
        }
    }
}
