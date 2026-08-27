using KeepyUppy.Shell.Save;
using NUnit.Framework;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Every progression rule. These are decisions, so they live in a tested class rather
    /// than in ShellView — see ScreenStack.ObserveWin for what happened the one time a rule
    /// leaked into the view.
    /// </summary>
    public sealed class SaveModelTests
    {
        [Test]
        public void AtOrUnderParIsGold()
        {
            Assert.AreEqual(Medal.Gold, SaveModel.Grade(5, 7));
            Assert.AreEqual(Medal.Gold, SaveModel.Grade(7, 7), "par itself is gold");
        }

        [Test]
        public void WithinThreeOverParIsSilver()
        {
            Assert.AreEqual(Medal.Silver, SaveModel.Grade(8, 7));
            Assert.AreEqual(Medal.Silver, SaveModel.Grade(10, 7), "par + 3 is the last silver");
        }

        [Test]
        public void FurtherOverParIsBronze()
        {
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(11, 7));
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(400, 7));
        }

        [Test]
        public void AnUnsetParNeverAwardsGold()
        {
            // par_touches defaults to 0 and is only filled in from playtest metrics (GDD 15),
            // so "unset" is the normal state while a level is being authored. A level that
            // hands out gold because nobody measured it yet makes the medal meaningless
            // exactly when it is being tuned.
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(0, 0));
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(3, 0));
        }

        [Test]
        public void ANegativeParIsTreatedAsUnset()
        {
            // Nothing writes a negative par, but the level file is hand-editable JSON.
            Assert.AreEqual(Medal.Bronze, SaveModel.Grade(1, -5));
        }
    }
}
