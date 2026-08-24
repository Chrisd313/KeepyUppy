using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Moving hazards (GDD 9), and the property the whole design rests on: an entity's
    /// position is a pure function of the step counter, never an accumulation.
    /// </summary>
    public sealed class EntityTests
    {
        static Vector2[] Line(float y = 9f) => new[] { new Vector2(8f, y), new Vector2(20f, y) };

        // ------------------------------------------------------------------- paths

        [Test]
        public void PositionIsAPureFunctionOfTheStep()
        {
            // The point of EntityPath. Asking for step 3600 directly must equal walking
            // there one step at a time, or determinism rests on nothing being skipped.
            Vector2[] path = Line();

            Vector2 direct = EntityPath.Evaluate(path, PathMode.PingPong, 3f, 0f, 3600);

            Vector2 walked = Vector2.zero;
            for (int i = 0; i <= 3600; i++)
                walked = EntityPath.Evaluate(path, PathMode.PingPong, 3f, 0f, i);

            Assert.AreEqual(SimTestHelpers.Bits(direct.x), SimTestHelpers.Bits(walked.x));
            Assert.AreEqual(SimTestHelpers.Bits(direct.y), SimTestHelpers.Bits(walked.y));
        }

        [Test]
        public void PingPong_StaysOnTheSegment_AndTurnsAround()
        {
            Vector2[] path = Line();
            bool sawLow = false, sawHigh = false;

            for (int step = 0; step < 1200; step++)
            {
                Vector2 p = EntityPath.Evaluate(path, PathMode.PingPong, 4f, 0f, step);

                Assert.GreaterOrEqual(p.x, 8f - 1e-3f, $"step {step} ran off the start");
                Assert.LessOrEqual(p.x, 20f + 1e-3f, $"step {step} ran off the end");
                Assert.AreEqual(9f, p.y, 1e-4f);

                if (p.x < 8.5f) sawLow = true;
                if (p.x > 19.5f) sawHigh = true;
            }

            Assert.IsTrue(sawLow && sawHigh, "a pingpong path should reach both ends");
        }

        [Test]
        public void Loop_WrapsBackToTheStart()
        {
            Vector2[] path = Line();
            float total = EntityPath.Length(path);

            // One full lap at 6 m/s is total/6 seconds; the next step is back near the start.
            int lap = SimClock.StepsIn(total / 6f);

            Vector2 start = EntityPath.Evaluate(path, PathMode.Loop, 6f, 0f, 0);
            Vector2 wrapped = EntityPath.Evaluate(path, PathMode.Loop, 6f, 0f, lap + 1);

            Assert.Less(Vector2.Distance(start, wrapped), 0.3f, "loop did not wrap");
        }

        [Test]
        public void Loop_ClosesTheCircuit_AndNeverTeleports()
        {
            // A square drawn corner to corner should walk the fourth edge home. Reversing at
            // the last corner, or jumping across the diagonal, both read as a bug on screen.
            var square = new[]
            {
                new Vector2(10f, 6f), new Vector2(16f, 6f),
                new Vector2(16f, 12f), new Vector2(10f, 12f),
            };

            float perimeter = EntityPath.Length(square, closed: true);
            Assert.AreEqual(24f, perimeter, 1e-3f, "the closing edge should be counted");

            Vector2 previous = EntityPath.Evaluate(square, PathMode.Loop, 6f, 0f, 0);
            bool onClosingEdge = false;

            for (int step = 1; step <= 600; step++)
            {
                Vector2 p = EntityPath.Evaluate(square, PathMode.Loop, 6f, 0f, step);

                // At 6 m/s a step covers 0.1 m. A teleport across the square would be 6.
                Assert.Less(Vector2.Distance(previous, p), 0.5f, $"step {step} jumped");

                // The closing edge is x = 10, between the last corner and the first.
                if (Mathf.Abs(p.x - 10f) < 1e-3f && p.y > 6.5f && p.y < 11.5f) onClosingEdge = true;

                previous = p;
            }

            Assert.IsTrue(onClosingEdge, "the entity never travelled the closing edge");
        }

        [Test]
        public void Loop_OnATwoPointPath_StillWraps()
        {
            // Two points have no interior, so closing them would just double back. This is
            // the one case where loop and pingpong legitimately look alike.
            Vector2[] path = Line();

            Assert.AreEqual(EntityPath.Length(path),
                            EntityPath.Length(path, closed: true), 1e-4f);
        }

        [Test]
        public void Once_StopsAtTheEnd()
        {
            Vector2 end = EntityPath.Evaluate(Line(), PathMode.Once, 4f, 0f, 100000);
            Assert.AreEqual(20f, end.x, 1e-3f, "a once path should park at its last waypoint");
        }

        [Test]
        public void Phase_DesyncsIdenticalTraps()
        {
            // Schema phase. Two saws on one path with no phase would be one saw.
            Vector2[] path = Line();
            bool everApart = false;

            for (int step = 0; step < 600; step++)
            {
                Vector2 a = EntityPath.Evaluate(path, PathMode.PingPong, 3f, 0f, step);
                Vector2 b = EntityPath.Evaluate(path, PathMode.PingPong, 3f, 0.5f, step);
                if (Vector2.Distance(a, b) > 1f) { everApart = true; break; }
            }

            Assert.IsTrue(everApart, "phase 0.5 tracked phase 0 exactly");
        }

        [Test]
        public void NoPath_IsAStationaryEntity()
        {
            Vector2[] parked = EntityPath.FromFlat(new Vector2(5f, 5f), new float[0]);

            Assert.AreEqual(1, parked.Length);
            Assert.AreEqual(new Vector2(5f, 5f),
                            EntityPath.Evaluate(parked, PathMode.PingPong, 9f, 0.3f, 700));
        }

        [Test]
        public void FlatPathUnpacksToWaypoints()
        {
            // JsonUtility cannot nest arrays, so the schema flattens. See LevelData.path.
            Vector2[] points = EntityPath.FromFlat(new Vector2(1f, 2f), new[] { 3f, 4f, 5f, 6f });

            Assert.AreEqual(3, points.Length, "the entity's own position is waypoint zero");
            Assert.AreEqual(new Vector2(3f, 4f), points[1]);
            Assert.AreEqual(new Vector2(5f, 6f), points[2]);
        }

        // ------------------------------------------------------------- kill volumes

        [Test]
        public void SawKillVolumeIsSmallerThanItsVisual()
        {
            // GDD 8, the same rule the tile hazards obey. Stingy to the level.
            var saw = new SimEntity
            {
                Kind = EntityKind.Saw, Radius = 0.6f,
                Points = new[] { new Vector2(10f, 10f) },
            };
            saw.Reset();

            Assert.Less(saw.KillRadius(0.15f), saw.Radius);

            // A balloon grazing the visual edge survives; well inside it does not.
            var grazing = new Vector2(10f, 10f + 0.6f + 0.4f - 0.05f);
            Assert.IsFalse(saw.OverlapsCircle(grazing, 0.4f, 0.15f), "the shrink was not applied");
            Assert.IsTrue(saw.OverlapsCircle(new Vector2(10f, 10.2f), 0.4f, 0.15f));
        }

        [Test]
        public void ShrinkNeverErasesAHitbox()
        {
            var pin = new SimEntity
            {
                Kind = EntityKind.Saw, Radius = 0.05f,
                Points = new[] { Vector2.zero },
            };
            pin.Reset();

            Assert.Greater(pin.KillRadius(0.5f), 0f, "an over-shrunk hazard must still bite");
        }

        [Test]
        public void SawKillsThePlayerBox()
        {
            var saw = new SimEntity
            {
                Kind = EntityKind.Saw, Radius = 0.6f,
                Points = new[] { new Vector2(10f, 10f) },
            };
            saw.Reset();

            Assert.IsTrue(saw.OverlapsBox(new Vector2(10f, 10.5f), 0.4f, 0.8f, 0.15f));
            Assert.IsFalse(saw.OverlapsBox(new Vector2(14f, 10f), 0.4f, 0.8f, 0.15f));
        }

        [Test]
        public void LampBeamReachesAlongItsLength_NotSideways()
        {
            var lamp = new SimEntity
            {
                Kind = EntityKind.Lamp, Radius = 0.5f, Length = 5f,
                Angle = 270f, Duration = 3f,
                Points = new[] { new Vector2(10f, 15f) },
            };
            lamp.Reset();
            lamp.Place(0);

            Assert.IsTrue(lamp.OverlapsCircle(new Vector2(10f, 12f), 0.4f, 0.15f), "inside the beam");
            Assert.IsFalse(lamp.OverlapsCircle(new Vector2(10f, 8f), 0.4f, 0.15f), "past the end");
            Assert.IsFalse(lamp.OverlapsCircle(new Vector2(13f, 12f), 0.4f, 0.15f), "beside the beam");
        }

        [Test]
        public void LampNeedsSustainedExposure_AndForgetsWhenYouLeave()
        {
            // GDD 9.2: the lamp forces movement. A countdown that remembered you would
            // instead force leaving the room, which is a different hazard entirely.
            var field = new EntityField();
            var lamp = new SimEntity
            {
                Id = "lamp", Kind = EntityKind.Lamp, Radius = 0.5f, Length = 5f,
                Angle = 270f, Duration = 1f,
                Points = new[] { new Vector2(10f, 15f) },
            };
            field.Add(lamp, null);
            field.Step(0, null);

            var inside = new Vector2(10f, 12f);
            var outside = new Vector2(2f, 2f);

            for (int i = 0; i < 30; i++)
                Assert.IsFalse(field.ResolveBalloon(inside, 0.4f, 0.15f), $"popped early at {i}");

            Assert.AreEqual(30, lamp.ExposureFrames);

            field.ResolveBalloon(outside, 0.4f, 0.15f);
            Assert.AreEqual(0, lamp.ExposureFrames, "exposure must be unbroken");

            bool popped = false;
            for (int i = 0; i < 61 && !popped; i++) popped = field.ResolveBalloon(inside, 0.4f, 0.15f);

            Assert.IsTrue(popped, "one second of unbroken exposure should pop a 1 s lamp");
        }

        [Test]
        public void FanIsNotLethal_AndDrivesTheAirField()
        {
            // GDD 9.2's best dual-purpose object, implemented as an air zone rather than as
            // wind of its own (GDD 6.2: one wind system).
            var air = new AirField();
            var field = new EntityField();

            var fan = new SimEntity
            {
                Id = "fan", Kind = EntityKind.Fan, Radius = 1f, Length = 6f,
                Angle = 90f, Power = 8f,
                Points = new[] { new Vector2(10f, 4f) },
            };
            field.Add(fan, air);
            field.Step(0, air);

            Assert.AreEqual(1, air.Count, "a fan should publish exactly one zone");
            Assert.AreEqual(8f, air.Sample(new Vector2(10f, 7f)).y, 1e-4f, "inside the blast");
            Assert.AreEqual(0f, air.Sample(new Vector2(10f, 12f)).y, 1e-4f, "past the blast");

            Assert.IsFalse(field.ResolveBalloon(new Vector2(10f, 7f), 0.4f, 0.15f),
                           "a fan must never kill; it is a tool as often as a hazard");
        }

        [Test]
        public void AMovingFanCarriesItsZone()
        {
            var air = new AirField();
            var field = new EntityField();

            var fan = new SimEntity
            {
                Id = "fan", Kind = EntityKind.Fan, Radius = 1f, Length = 6f,
                Angle = 90f, Power = 8f, Speed = 4f, Mode = PathMode.Loop,
                Points = new[] { new Vector2(4f, 4f), new Vector2(24f, 4f) },
            };
            field.Add(fan, air);

            field.Step(0, air);
            Assert.AreEqual(8f, air.Sample(new Vector2(4f, 7f)).y, 1e-4f);

            field.Step(SimClock.StepsIn(2f), air);   // 8 m along at 4 m/s
            Assert.AreEqual(0f, air.Sample(new Vector2(4f, 7f)).y, 1e-4f, "the zone stayed behind");
            Assert.AreEqual(8f, air.Sample(new Vector2(12f, 7f)).y, 1e-4f, "the zone did not follow");
        }

        [Test]
        public void DormantEntitiesDoNothing()
        {
            // GDD 9.3 start_state. Triggers do not exist yet; the flag is honoured now so a
            // level authored with one behaves rather than misbehaves.
            var field = new EntityField();
            var saw = new SimEntity
            {
                Id = "saw", Kind = EntityKind.Saw, Radius = 0.6f, Speed = 5f,
                Active = false,
                Points = new[] { new Vector2(10f, 10f), new Vector2(20f, 10f) },
            };
            field.Add(saw, null);

            field.Step(SimClock.StepsIn(1f), null);

            Assert.AreEqual(new Vector2(10f, 10f), saw.Position, "a dormant entity should not move");
            Assert.IsFalse(field.ResolveBalloon(new Vector2(10f, 10f), 0.4f, 0.15f),
                           "a dormant entity should not kill");

            field.SetActive("saw", true);
            Assert.IsTrue(field.ResolveBalloon(new Vector2(10f, 10f), 0.4f, 0.15f),
                          "arming it should make it lethal");
        }

        // -------------------------------------------------------------- from a file

        [Test]
        public void LoaderBuildsEntitiesFromLevelData()
        {
            var level = new LevelData
            {
                tiles = new TileGrid().ToRows(),
                entities = new[]
                {
                    new LevelEntity { id = "e01", type = "saw", pos = new[] { 8f, 9f },
                                      path = new[] { 20f, 9f } },
                    new LevelEntity { id = "e02", type = "fan", pos = new[] { 4f, 2f } },
                },
            };

            var world = new SimWorld();
            LevelLoader.Apply(level, world, out _, out _);

            Assert.AreEqual(2, world.Entities.Count);
            Assert.AreEqual(EntityKind.Saw, world.Entities[0].Kind);
            Assert.AreEqual(2, world.Entities[0].Points.Length, "the flat path should unpack");
            Assert.AreEqual(1, world.Air.Count, "the fan should have published its zone");
        }

        [Test]
        public void UnsetParamsFallBackToTheTypeDefaults()
        {
            // JsonUtility writes every field of EntityParams, so an untouched param arrives
            // as 0. A zero-radius saw would be an invisible hazard.
            SimEntity saw = EntityFactory.Create(
                new LevelEntity { id = "e01", type = "saw", pos = new[] { 5f, 5f } });

            Assert.AreEqual(EntityDefaults.SawRadius, saw.Radius, 1e-4f);
            Assert.AreEqual(EntityDefaults.SawSpeed, saw.Speed, 1e-4f);
        }

        [Test]
        public void UnknownTypesAreKeptAndCounted_NeverDropped()
        {
            // A level file outlives any one build. Silently deleting an entity the build does
            // not understand would make the editor untrustworthy, which is the one thing
            // CLAUDE.md says it must never be.
            var level = new LevelData
            {
                tiles = new TileGrid().ToRows(),
                entities = new[]
                {
                    new LevelEntity { id = "e01", type = "trebuchet", pos = new[] { 8f, 9f } },
                },
            };

            var world = new SimWorld();
            LevelLoader.Apply(level, world, out _, out _);

            Assert.AreEqual(1, world.Entities.Count);
            Assert.AreEqual(EntityKind.Unknown, world.Entities[0].Kind);
            Assert.AreEqual(1, world.Entities.UnknownCount);

            Assert.IsFalse(world.Entities.ResolveBalloon(new Vector2(8f, 9f), 0.4f, 0.15f),
                           "an unknown entity must be inert, not lethal");
        }

        // ------------------------------------------------------------------ the sim

        [Test]
        public void ASawInTheRoomKillsTheBalloon()
        {
            var world = new SimWorld { FloorKillsBalloon = false };
            world.Grid.Clear();
            world.Grid.FillBorder();

            var saw = new SimEntity
            {
                Id = "saw", Kind = EntityKind.Saw, Radius = 0.6f,
                Points = new[] { new Vector2(16f, 12f) },
            };
            world.Entities.Add(saw, world.Air);

            world.Reset(new Vector2(16f, 14f), Vector2.zero, 1);

            // Ticked one step at a time and stopped on the death. Running a fixed 180 and
            // asserting afterwards would be a coin toss: the room auto-restarts six frames
            // later (GDD 8), so the balloon is falling again long before the count runs out.
            int steps = 0;
            while (!world.IsDead && steps++ < 300) world.Tick();

            Assert.IsTrue(world.IsDead, "a balloon falling onto a saw should pop");
            Assert.AreEqual(DeathCause.BalloonHitHazard, world.LastDeath);
        }

        [Test]
        public void RestartClearsExposureButNotTheDeathCount()
        {
            // GDD 8: every counter, buffer and cooldown clears. GDD 15: the death count does
            // not. A lamp's exposure is the first genuinely accumulated entity state, so it
            // is the one most likely to have been forgotten.
            var world = new SimWorld { FloorKillsBalloon = false };
            world.Grid.Clear();
            world.Grid.FillBorder();

            var lamp = new SimEntity
            {
                Id = "lamp", Kind = EntityKind.Lamp, Radius = 0.6f, Length = 6f,
                Angle = 270f, Duration = 5f,
                Points = new[] { new Vector2(16f, 16f) },
            };
            world.Entities.Add(lamp, world.Air);

            world.Reset(new Vector2(16f, 13f), Vector2.zero, 1);
            world.Tick(30);

            Assert.Greater(lamp.ExposureFrames, 0, "the balloon should be sitting in the beam");

            world.Restart();
            Assert.AreEqual(0, lamp.ExposureFrames, "exposure survived a restart");
        }
    }
}
