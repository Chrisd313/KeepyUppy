using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
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

        static EntityField OneDormantSaw(out AirField air)
        {
            air = new AirField();
            var field = new EntityField();

            field.Add(new SimEntity
            {
                Id = "e01", Kind = EntityKind.Saw, Radius = 0.6f, Speed = 0f,
                Points = new[] { new Vector2(20f, 8f) },
                Active = false,
            }, air);

            return field;
        }

        [Test]
        public void Link_On_ArmsADormantEntity_OnTheStepAfterTheTriggerFires()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.On, DelaySteps = 0,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.IsFalse(entities.Find("e01").Active, "effects land on the NEXT step");

            field.Step(1, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.IsTrue(entities.Find("e01").Active);
        }

        [Test]
        public void Link_Delay_AppliesExactlyOnItsStep()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.On, DelaySteps = 24,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, entities, air);

            // Fired on step 0, so it is queued for step 0 + 1 + 24 = 25. The +1 is the
            // "effects land on the next step" rule, not an off-by-one.
            for (int step = 1; step <= 26; step++)
            {
                field.Step(step, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
                bool expected = step >= 25;
                Assert.AreEqual(expected, entities.Find("e01").Active, $"step {step}");
            }
        }

        [Test]
        public void Link_Toggle_FlipsWhateverStateTheTargetIsIn()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: false));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.Toggle, DelaySteps = 0,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            field.Step(1, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.IsTrue(entities.Find("e01").Active, "first entry armed it");

            field.Step(2, new Vector2(0f, 2f), true, Vector2.zero, entities, air);
            field.Step(3, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            field.Step(4, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.IsFalse(entities.Find("e01").Active, "re-entry toggled it back off");
        }

        [Test]
        public void Link_SwitchesAnAirZone_ById()
        {
            var air = new AirField();
            air.Add(new AirZone("z01", new Rect(0f, 0f, 4f, 4f), new Vector2(0f, 5f), false));

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "z01" }, Action = LinkAction.On, DelaySteps = 0,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, null, air);
            field.Step(1, new Vector2(12f, 2f), true, Vector2.zero, null, air);

            Assert.IsTrue(air[0].enabled);
        }

        [Test]
        public void Link_Inert_ChangesNothing()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.Inert, DelaySteps = 0,
            });

            for (int step = 0; step < 10; step++)
                field.Step(step, new Vector2(12f, 2f), true, Vector2.zero, entities, air);

            Assert.IsFalse(entities.Find("e01").Active, "arm and fire are parsed, not simulated");
        }

        [Test]
        public void Reset_DropsPendingDelays()
        {
            EntityField entities = OneDormantSaw(out AirField air);

            var field = new TriggerField();
            field.Add(Region("t01", once: true));
            field.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "e01" }, Action = LinkAction.On, DelaySteps = 30,
            });

            field.Step(0, new Vector2(12f, 2f), true, Vector2.zero, entities, air);
            Assert.AreEqual(1, field.PendingCount, "queued but not yet due");

            field.Reset();

            Assert.AreEqual(0, field.PendingCount, "a restart drops a delay in flight");
        }
    }
}
