using KeepyUppy.InputLayer;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Menu navigation, kept deliberately separate from IInputSource.
    ///
    /// GDD 5.5 exists so that simulation code cannot learn which device produced an intent.
    /// Menus are not simulation, and growing that interface for them would widen the surface
    /// ApplyHit sits behind for an unrelated reason.
    /// </summary>
    public sealed class ShellInputTests
    {
        [Test]
        public void NavigationIsOneStepPerPress()
        {
            // Held navigation would race the cursor across a level grid. One press, one move.
            var input = new ScriptedShellInput();

            input.Press(Vector2Int.down, confirm: false, back: false);
            input.Sample();
            Assert.AreEqual(Vector2Int.down, input.Navigate);

            input.Sample();
            Assert.AreEqual(Vector2Int.zero, input.Navigate, "the same press must not repeat");
        }

        [Test]
        public void ConfirmAndBackAreEdges()
        {
            var input = new ScriptedShellInput();

            input.Press(Vector2Int.zero, confirm: true, back: false);
            input.Sample();
            Assert.IsTrue(input.Confirm);
            Assert.IsFalse(input.Back);

            input.Sample();
            Assert.IsFalse(input.Confirm, "a confirm lasts exactly one sample");
        }

        [Test]
        public void EditPressedIsAnEdge()
        {
            var input = new ScriptedShellInput();

            input.PressEdit();
            input.Sample();
            Assert.IsTrue(input.EditPressed);

            input.Sample();
            Assert.IsFalse(input.EditPressed, "an edit press lasts exactly one sample");
        }

        [Test]
        public void FastForwardIsHeldNotAnEdge()
        {
            // Held, not pressed: it is released the moment something needs reacting to, and
            // that self-regulation is the point.
            var input = new ScriptedShellInput();

            input.Press(Vector2Int.zero, confirm: false, back: false, fastForward: true);
            input.Sample();
            Assert.IsTrue(input.FastForwardHeld);

            input.Sample();
            Assert.IsTrue(input.FastForwardHeld, "a held control stays held until released");

            input.Press(Vector2Int.zero, confirm: false, back: false);
            input.Sample();
            Assert.IsFalse(input.FastForwardHeld);
        }

        [Test]
        public void WipeIsHeldRatherThanConsumed()
        {
            // Held, like fast-forward: the view counts frames to require a deliberate hold, so
            // an intent that cleared itself after one Sample could never reach the threshold.
            var input = new ScriptedShellInput();
            input.Press(Vector2Int.zero, confirm: false, back: false, fastForward: false, wipeSave: true);

            input.Sample();
            Assert.IsTrue(input.WipeSaveHeld);

            input.Sample();
            Assert.IsTrue(input.WipeSaveHeld, "still held with no new press");
        }

        [Test]
        public void NothingPressedReadsNeutral()
        {
            var input = new ScriptedShellInput();
            input.Sample();

            Assert.AreEqual(Vector2Int.zero, input.Navigate);
            Assert.IsFalse(input.Confirm);
            Assert.IsFalse(input.Back);
            Assert.IsFalse(input.FastForwardHeld);
        }
    }
}
