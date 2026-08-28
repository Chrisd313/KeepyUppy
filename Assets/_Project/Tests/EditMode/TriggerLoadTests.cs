using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using KeepyUppy.Sim.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Schema to simulation for triggers and links. The editor and the shipping game take
    /// this same path, so a type that loads wrong here is a level that plays wrong in both.
    /// </summary>
    public sealed class TriggerLoadTests
    {
        static LevelData LevelWithTrigger(LevelTrigger trigger, LevelLink link = null)
        {
            var grid = new TileGrid();
            grid.FillBorder();

            return new LevelData
            {
                tiles = grid.ToRows(),
                triggers = new[] { trigger },
                links = link == null ? new LevelLink[0] : new[] { link },
            };
        }

        [Test]
        public void Kinds_ParseToTheirBehaviour_AndAnythingElseIsUnknown()
        {
            Assert.AreEqual(TriggerKind.PlayerRegion, TriggerFactory.ParseKind("player_region"));
            Assert.AreEqual(TriggerKind.BalloonRegion, TriggerFactory.ParseKind("balloon_region"));
            Assert.AreEqual(TriggerKind.Timer, TriggerFactory.ParseKind("timer"));
            Assert.AreEqual(TriggerKind.Unknown, TriggerFactory.ParseKind("lever"));
            Assert.AreEqual(TriggerKind.Unknown, TriggerFactory.ParseKind("on_first_entry"));
        }

        [Test]
        public void Actions_ParseToTheirBehaviour_AndArmAndFireAreInert()
        {
            Assert.AreEqual(LinkAction.On, TriggerFactory.ParseAction("on"));
            Assert.AreEqual(LinkAction.Off, TriggerFactory.ParseAction("off"));
            Assert.AreEqual(LinkAction.Toggle, TriggerFactory.ParseAction("toggle"));
            Assert.AreEqual(LinkAction.Inert, TriggerFactory.ParseAction("arm"));
            Assert.AreEqual(LinkAction.Inert, TriggerFactory.ParseAction("fire"));
        }

        [Test]
        public void Seconds_BecomeStepsAtLoad()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(new LevelTrigger
            {
                id = "t01", type = "timer", seconds = 2.5f, once = true,
            }), world, out _, out _);

            Assert.AreEqual(150, world.Triggers[0].AtStep, "2.5 s at 60 Hz");
        }

        [Test]
        public void RectAndFlags_ComeStraightFromTheFile()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(new LevelTrigger
            {
                id = "t01", type = "player_region",
                rect = new[] { 14f, 10f, 4f, 8f },
                once = false, visible = true,
            }), world, out _, out _);

            SimTrigger trigger = world.Triggers.Find("t01");

            Assert.AreEqual(new Rect(14f, 10f, 4f, 8f), trigger.Rect);
            Assert.IsFalse(trigger.Once);
            Assert.IsTrue(trigger.Visible);
        }

        [Test]
        public void UnknownType_LoadsInertAndIsCounted()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(new LevelTrigger { id = "t01", type = "lever" }),
                              world, out _, out _);

            Assert.AreEqual(1, world.Triggers.Count, "never dropped");
            Assert.AreEqual(1, world.Triggers.UnknownCount);
        }

        [Test]
        public void Link_ResolvesItsSourceToAnIndex_AndItsDelayToSteps()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 1f, 1f, 2f, 2f } },
                new LevelLink { from = "t01", to = new[] { "e01" }, action = "on", delay = 0.4f }),
                world, out _, out _);

            Assert.AreEqual(1, world.Triggers.LinkCount);
        }

        [Test]
        public void Link_WithAMissingSource_IsDropped_NotCrashed()
        {
            var world = new SimWorld();
            LevelLoader.Apply(LevelWithTrigger(
                new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 1f, 1f, 2f, 2f } },
                new LevelLink { from = "nope", to = new[] { "e01" }, action = "on" }),
                world, out _, out _);

            Assert.AreEqual(0, world.Triggers.LinkCount, "the validator warns; the sim ignores it");
        }
    }
}
