using KeepyUppy.Sim;
using KeepyUppy.Sim.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Trigger firing (GDD 9.3). Edge-triggered, never level-triggered: the shell already
    /// paid for that lesson once in ScreenStack.ObserveWin, and this is the same shape.
    /// </summary>
    public sealed class TriggerFieldTests
    {
        static SimTrigger Region(string id, bool once, TriggerKind kind = TriggerKind.PlayerRegion)
            => new SimTrigger
            {
                Id = id, Kind = kind, Once = once,
                Rect = new Rect(10f, 1f, 4f, 4f),
            };

        /// <summary>Steps the field with a player at a position and no balloon in the room.</summary>
        static void StepPlayer(TriggerField field, int step, Vector2 player)
            => field.Step(step, player, true, new Vector2(-99f, -99f), null, null);

        [Test]
        public void Region_FiresOnTheEntryEdge_NotEveryStepInside()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: false));

            StepPlayer(field, 0, new Vector2(0f, 2f));
            Assert.AreEqual(0, field.FiredThisStep.Count, "outside");

            StepPlayer(field, 1, new Vector2(12f, 2f));
            Assert.AreEqual(1, field.FiredThisStep.Count, "the entry edge fires");

            StepPlayer(field, 2, new Vector2(12.5f, 2f));
            Assert.AreEqual(0, field.FiredThisStep.Count, "still inside is not a new edge");
        }

        [Test]
        public void Region_OnceFalse_FiresAgainOnReEntry()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: false));

            StepPlayer(field, 0, new Vector2(12f, 2f));
            StepPlayer(field, 1, new Vector2(0f, 2f));
            StepPlayer(field, 2, new Vector2(12f, 2f));

            Assert.AreEqual(1, field.FiredThisStep.Count, "re-entry is a fresh edge");
        }

        [Test]
        public void Region_OnceTrue_NeverFiresTwice()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: true));

            StepPlayer(field, 0, new Vector2(12f, 2f));
            StepPlayer(field, 1, new Vector2(0f, 2f));
            StepPlayer(field, 2, new Vector2(12f, 2f));

            Assert.AreEqual(0, field.FiredThisStep.Count, "once latches for the whole run");
        }

        [Test]
        public void BalloonRegion_IgnoresThePlayer()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: true, TriggerKind.BalloonRegion));

            field.Step(0, new Vector2(12f, 2f), true, new Vector2(0f, 9f), null, null);
            Assert.AreEqual(0, field.FiredThisStep.Count, "the player is not the balloon");

            field.Step(1, new Vector2(12f, 2f), true, new Vector2(12f, 2f), null, null);
            Assert.AreEqual(1, field.FiredThisStep.Count);
        }

        [Test]
        public void PlayerRegion_WithNoPlayerInTheRoom_NeverFires()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: true));

            for (int step = 0; step < 10; step++)
                field.Step(step, new Vector2(12f, 2f), false, new Vector2(12f, 2f), null, null);

            Assert.AreEqual(0, field.FiredThisStep.Count);
        }

        [Test]
        public void Timer_FiresOnceAtItsStep()
        {
            var field = new TriggerField();
            field.Add(new SimTrigger { Id = "t01", Kind = TriggerKind.Timer, AtStep = 3, Once = true });

            for (int step = 0; step < 3; step++)
            {
                StepPlayer(field, step, Vector2.zero);
                Assert.AreEqual(0, field.FiredThisStep.Count, $"step {step} is too early");
            }

            StepPlayer(field, 3, Vector2.zero);
            Assert.AreEqual(1, field.FiredThisStep.Count, "fires on its step");

            StepPlayer(field, 4, Vector2.zero);
            Assert.AreEqual(0, field.FiredThisStep.Count, "and never again");
        }

        [Test]
        public void UnknownKind_IsCountedAndNeverFires()
        {
            var field = new TriggerField();
            field.Add(new SimTrigger
            {
                Id = "t01", Kind = TriggerKind.Unknown,
                Rect = new Rect(0f, 0f, 32f, 18f), Once = false,
            });

            StepPlayer(field, 0, new Vector2(4f, 4f));

            Assert.AreEqual(1, field.UnknownCount, "surfaced, never silently dropped");
            Assert.AreEqual(0, field.FiredThisStep.Count);
        }

        [Test]
        public void Reset_ClearsTheRunState_SoARestartReplays()
        {
            var field = new TriggerField();
            field.Add(Region("t01", once: true));

            StepPlayer(field, 0, new Vector2(12f, 2f));
            Assert.IsTrue(field[0].Fired);

            field.Reset();

            Assert.IsFalse(field[0].Fired);
            Assert.IsFalse(field[0].WasInside);

            StepPlayer(field, 0, new Vector2(12f, 2f));
            Assert.AreEqual(1, field.FiredThisStep.Count, "the room replays from the start");
        }
    }
}
