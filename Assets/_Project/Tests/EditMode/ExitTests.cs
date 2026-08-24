using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The way out (GDD 3, 7.4 LOCKED).
    ///
    /// The gate is a fit test: the balloon has to be small enough for the aperture. That is
    /// what turns inflation from a modifier into the goal, so it is worth pinning before
    /// inflation exists to change the sizes.
    /// </summary>
    public sealed class ExitTests
    {
        static ExitGate Ring(Vector2 at, float diameter) => new ExitGate
        {
            Present = true, Position = at, Shape = ExitShape.Ring, Diameter = diameter,
        };

        static SimWorld Room(ExitGate gate, Vector2 balloonAt, float radius)
        {
            var world = new SimWorld { FloorKillsBalloon = false, Exit = gate };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Balloon.Radius = radius;
            world.Reset(balloonAt, Vector2.zero, 1);
            world.Balloon.Radius = radius;
            return world;
        }

        // -------------------------------------------------------------- the fit

        [Test]
        public void ABalloonTooBigDoesNotFit()
        {
            ExitGate gate = Ring(new Vector2(16f, 9f), 1.0f);

            Assert.IsTrue(gate.Fits(InflationBands.RadiusOf(InflationBand.Shrivelled)), "0.25 m radius");
            Assert.IsTrue(gate.Fits(0.5f), "exactly the aperture still fits");
            Assert.IsFalse(gate.Fits(InflationBands.RadiusOf(InflationBand.Critical)), "0.75 m radius");
        }

        [Test]
        public void ASlotIsJudgedOnItsHeight_NotItsWidth()
        {
            // The whole point of a letterbox: being wide does not help you through it.
            var slot = new ExitGate
            {
                Present = true, Position = new Vector2(16f, 2f),
                Shape = ExitShape.Slot, Diameter = 0.6f,
            };

            Assert.Greater(slot.Size.x, slot.Size.y, "a slot is wider than it is tall");
            Assert.IsTrue(slot.Fits(0.25f), "shrivelled goes through");
            Assert.IsFalse(slot.Fits(0.5f), "nominal does not, however wide the slot is");
        }

        [Test]
        public void LargestBandThatFits_MatchesTheBandTable()
        {
            Assert.AreEqual(InflationBand.Nominal,
                            Ring(Vector2.zero, InflationBands.RadiusOf(InflationBand.Nominal) * 2f)
                                .LargestBandThatFits());

            Assert.AreEqual(InflationBand.Shrivelled, Ring(Vector2.zero, 0.4f).LargestBandThatFits());
            Assert.AreEqual(InflationBand.Critical, Ring(Vector2.zero, 4f).LargestBandThatFits());
        }

        // ------------------------------------------------------------ acceptance

        [Test]
        public void AcceptVolumeIsLargerThanTheVisual()
        {
            // GDD 8's generous half, mirroring the shrink every hazard gets.
            ExitGate gate = Ring(new Vector2(16f, 9f), 1.6f);

            var justOutside = new Vector2(16f + 0.8f + 0.1f, 9f);

            Assert.IsFalse(gate.Accepts(justOutside, 0.5f, 0f), "with no bonus this is a miss");
            Assert.IsTrue(gate.Accepts(justOutside, 0.5f, 0.15f), "the bonus should let it in");
        }

        [Test]
        public void ABalloonThatDoesNotFitIsNeverAccepted()
        {
            ExitGate gate = Ring(new Vector2(16f, 9f), 0.6f);

            Assert.IsFalse(gate.Accepts(new Vector2(16f, 9f), 0.5f, 0.15f),
                           "sitting dead centre must not win if it does not fit");
        }

        [Test]
        public void AnAbsentGateAcceptsNothing()
        {
            // The physics-only test rooms have no exit, and must not win by accident.
            Assert.IsFalse(ExitGate.None.Accepts(Vector2.zero, 0.1f, 1f));
        }

        // --------------------------------------------------------------- the sim

        [Test]
        public void ReachingTheGateWinsTheRoom()
        {
            SimWorld world = Room(Ring(new Vector2(16f, 12f), 1.6f), new Vector2(16f, 14f), 0.5f);

            int steps = 0;
            while (!world.IsWon && steps++ < 600) world.Tick();

            Assert.IsTrue(world.IsWon, "a balloon falling into the gate should complete the room");
            Assert.AreEqual(1, world.WinCount);
        }

        [Test]
        public void AWinHoldsThenRestarts()
        {
            // GDD 3's "next room", with only one room to go to. The hold is what stops the
            // completion being invisible.
            SimWorld world = Room(Ring(new Vector2(16f, 12f), 1.6f), new Vector2(16f, 14f), 0.5f);

            while (!world.IsWon) world.Tick();

            world.Tick(SimWorld.WinHoldFrames - 1);
            Assert.IsTrue(world.IsWon, "the win should still be on screen");

            world.Tick();
            Assert.IsFalse(world.IsWon, "and then the room resets");
            Assert.AreEqual(1, world.WinCount, "wins survive the restart, as deaths do");
        }

        [Test]
        public void ATooLargeBalloonPassesOverTheGate()
        {
            // Deliberate while inflation is unimplemented: the radius is fixed at spawn, so a
            // gate too small is an unwinnable level rather than a moment of play. The
            // validator is what tells the author. See SimWorld.CheckExit.
            SimWorld world = Room(Ring(new Vector2(16f, 12f), 0.6f), new Vector2(16f, 14f), 0.5f);

            world.Tick(300);

            Assert.IsFalse(world.IsWon);
            Assert.IsFalse(world.IsDead, "and it certainly should not have died to the doorway");
        }

        [Test]
        public void DeathBeatsArrivalOnTheSameStep()
        {
            // The level gets the last word. Generosity lives in the accept volume, not in
            // letting a pop be overridden by arriving.
            //
            // Started already inside both volumes, because a balloon falling in from above
            // reaches the accept volume first and wins honestly before the spikes are ever in
            // range — which is correct behaviour, and therefore does not test this ordering.
            var world = new SimWorld { Exit = Ring(new Vector2(16f, 2f), 1.6f) };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Grid.Fill(14, 1, 5, 1, TileKind.SpikeUp);
            world.Balloon.Radius = 0.5f;
            world.Reset(new Vector2(16f, 1.9f), Vector2.zero, 1);

            world.Tick();

            Assert.IsTrue(world.IsDead, "the spikes under the gate should still kill");
            Assert.IsFalse(world.IsWon);
        }

        [Test]
        public void RestartClearsTheWinButKeepsTheCount()
        {
            SimWorld world = Room(Ring(new Vector2(16f, 12f), 1.6f), new Vector2(16f, 14f), 0.5f);

            while (!world.IsWon) world.Tick();
            world.Restart();

            Assert.IsFalse(world.IsWon);
            Assert.AreEqual(1, world.WinCount);
        }

        // -------------------------------------------------------------- the file

        [Test]
        public void TheGateLoadsFromTheLevel()
        {
            var level = new LevelData
            {
                tiles = new TileGrid().ToRows(),
                exit = new LevelExit { pos = new[] { 29f, 4f }, shape = "slot", diameter = 0.6f },
            };

            var world = new SimWorld();
            LevelLoader.Apply(level, world, out _, out _);

            Assert.IsTrue(world.Exit.Present);
            Assert.AreEqual(ExitShape.Slot, world.Exit.Shape);
            Assert.AreEqual(0.6f, world.Exit.Diameter, 1e-4f);
        }

        [Test]
        public void AnUnwinnableGateIsALintWarning()
        {
            // The one lint that catches a level nobody can finish. Without it the author
            // discovers it by failing the room forever.
            var level = new LevelData
            {
                tiles = Room(ExitGate.None, Vector2.zero, 0.5f).Grid.ToRows(),
                spawn = new LevelSpawn { balloon_inflation = "nominal" },
                exit = new LevelExit { pos = new[] { 16f, 9f }, shape = "ring", diameter = 0.6f,
                                       required_band = "shrivelled" },
            };

            CollectionAssert.IsNotEmpty(LevelValidator.Validate(level));
            Assert.IsTrue(LevelValidator.Validate(level)
                              .Exists(w => w.Contains("unwinnable")),
                          "the author needs to be told the balloon cannot fit");
        }

        [Test]
        public void RequiredBandIsTheAuthorsStatement_NotDerived()
        {
            // GDD 7.4's ceiling ring demands Full or Critical because of WHERE it is, not how
            // wide it is. No aperture can express that, so the editor must not overwrite the
            // author's intent with a number derived from the diameter.
            var session = new EditorSession();
            session.Load(new LevelData
            {
                tiles = new TileGrid().ToRows(),
                exit = new LevelExit { pos = new[] { 16f, 9f }, shape = "ring", diameter = 1.6f,
                                       required_band = "full" },
            });

            session.SetExitDiameter(2.0f);

            Assert.AreEqual("full", session.Level.exit.required_band,
                            "resizing the gate must not rewrite what the level asks for");

            session.SetRequiredBand("critical");
            Assert.AreEqual("critical", session.Level.exit.required_band);
        }

        [Test]
        public void OnlyAnImpossibleRequiredBandIsALintWarning()
        {
            // A band that merely differs from the largest that fits is fine — a 1.6 m ring
            // asking for nominal is a level saying "arrive nominal", not a mistake.
            LevelData level = LevelWithExit(1.6f, "nominal");
            Assert.IsFalse(LevelValidator.Validate(level).Exists(w => w.Contains("required_band")),
                           "a stated band that fits is not a warning");

            // A band that cannot physically pass through the hole is a contradiction.
            LevelData impossible = LevelWithExit(0.6f, "critical");
            Assert.IsTrue(LevelValidator.Validate(impossible).Exists(w => w.Contains("required_band")),
                          "critical cannot fit a 0.6 m aperture");
        }

        static LevelData LevelWithExit(float diameter, string band) => new LevelData
        {
            tiles = BorderedRoom(),
            spawn = new LevelSpawn { balloon_inflation = "shrivelled" },
            exit = new LevelExit { pos = new[] { 16f, 9f }, shape = "ring",
                                   diameter = diameter, required_band = band },
        };

        static string[] BorderedRoom()
        {
            var grid = new TileGrid();
            grid.FillBorder();
            return grid.ToRows();
        }

        [Test]
        public void TheExitCanBeSelectedAndDragged()
        {
            var session = new EditorSession();
            session.Load(new LevelData
            {
                tiles = new TileGrid().ToRows(),
                exit = new LevelExit { pos = new[] { 16f, 9f }, shape = "ring", diameter = 1.6f },
            });

            Assert.IsTrue(session.ExitAt(new Vector2(16.2f, 9.1f)));
            Assert.IsFalse(session.ExitAt(new Vector2(4f, 4f)));

            session.SelectExit();
            session.MoveExit(new Vector2(24.3f, 6.4f));

            Assert.IsTrue(session.ExitSelected);
            Assert.AreEqual(24.5f, session.Gate().Position.x, 1e-4f, "the exit snaps to half-tile");
            Assert.IsTrue(session.Dirty);
        }
    }
}
