using System;
using KeepyUppy.InputLayer;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Level;
using KeepyUppy.Sim.Panels;
using KeepyUppy.Sim.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Everything the sim carries from one step to the next that a test can observe.
    ///
    /// One shared field set, so determinism and restart purity are checked against exactly
    /// the same definition of "the state". A new counter added to the sim gets covered by
    /// both the moment it is added here.
    ///
    /// DeathCount is deliberately absent: GDD 15 wants it to survive a restart, so it is not
    /// part of the state a restart is expected to reproduce.
    /// </summary>
    internal struct SimSnapshot
    {
        public int step;
        public float balloonX, balloonY, balloonVx, balloonVy;
        public float playerX, playerY, playerVx, playerVy;
        public int touchCount;
        public int coyoteFrames;
        public int jumpBufferFrames;
        public bool grounded;
        public bool isDead;
        public bool isWon;
        public int winningFrames;

        /// <summary>
        /// Entity state, folded to one value per field so the snapshot does not have to know
        /// how many entities a room has. Motion is derived from the step counter, but
        /// position is captured anyway: it is what a path bug would show up in.
        /// </summary>
        public int entityPositions;
        public int entityExposure;

        /// <summary>
        /// Trigger run state and the arm/disarm flags a link writes, folded to one value each
        /// so the snapshot does not have to know how many of either a room has.
        /// </summary>
        public int triggerState;
        public int activeStates;

        public static SimSnapshot Capture(SimWorld w) => new SimSnapshot
        {
            step = w.Clock.Step,
            balloonX = w.Balloon.Position.x,
            balloonY = w.Balloon.Position.y,
            balloonVx = w.Balloon.Velocity.x,
            balloonVy = w.Balloon.Velocity.y,
            playerX = w.Player.Position.x,
            playerY = w.Player.Position.y,
            playerVx = w.Player.Velocity.x,
            playerVy = w.Player.Velocity.y,
            touchCount = w.TouchCount,
            coyoteFrames = w.Player.CoyoteFrames,
            jumpBufferFrames = w.Player.JumpBufferFrames,
            grounded = w.Player.Grounded,
            isDead = w.IsDead,
            isWon = w.IsWon,
            winningFrames = w.WinningFrames,
            entityPositions = FoldPositions(w.Entities),
            entityExposure = FoldExposure(w.Entities),
            triggerState = FoldTriggers(w.Triggers),
            activeStates = FoldActive(w.Entities, w.Air, w.Panels),
        };

        static int FoldPositions(EntityField entities)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < entities.Count; i++)
                {
                    hash = (hash ^ SimTestHelpers.Bits(entities[i].Position.x)) * 16777619;
                    hash = (hash ^ SimTestHelpers.Bits(entities[i].Position.y)) * 16777619;
                }
                return hash;
            }
        }

        static int FoldExposure(EntityField entities)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < entities.Count; i++)
                    hash = (hash ^ entities[i].ExposureFrames) * 16777619;

                return hash;
            }
        }

        static int FoldTriggers(TriggerField triggers)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < triggers.Count; i++)
                {
                    hash = (hash ^ (triggers[i].Fired ? 1 : 0)) * 16777619;
                    hash = (hash ^ (triggers[i].WasInside ? 2 : 0)) * 16777619;
                }

                // A delay in flight is state too: two runs must have the same links pending.
                return (hash ^ triggers.PendingCount) * 16777619;
            }
        }

        static int FoldActive(EntityField entities, AirField air, PanelField panels)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < entities.Count; i++)
                {
                    hash = (hash ^ (entities[i].Active ? 1 : 0)) * 16777619;
                    hash = (hash ^ entities[i].ArmedStep) * 16777619;
                }

                for (int i = 0; i < air.Count; i++)
                    hash = (hash ^ (air[i].enabled ? 1 : 0)) * 16777619;

                for (int i = 0; i < panels.Count; i++)
                    hash = (hash ^ (panels[i].Enabled ? 1 : 0)) * 16777619;

                return hash;
            }
        }

        /// <summary>
        /// <paramref name="includeWinningFrames"/> defaults to true — every ordinary
        /// determinism comparison must cover it, same as any other field. The one exception is
        /// restart-purity: WinningFrames is a record of a completed attempt, not per-attempt
        /// state (WinCount and WinningTouches are its siblings, deliberately kept out of this
        /// snapshot for the same reason), so it is expected to survive a Restart() and a
        /// restart-purity comparison must not treat that survival as divergence.
        /// </summary>
        public void AssertMatches(in SimSnapshot other, string context, bool includeWinningFrames = true)
        {
            Assert.AreEqual(step, other.step, $"{context}: step");

            AssertBits(balloonX, other.balloonX, $"{context}: balloon.x");
            AssertBits(balloonY, other.balloonY, $"{context}: balloon.y");
            AssertBits(balloonVx, other.balloonVx, $"{context}: balloon.vx");
            AssertBits(balloonVy, other.balloonVy, $"{context}: balloon.vy");

            AssertBits(playerX, other.playerX, $"{context}: player.x");
            AssertBits(playerY, other.playerY, $"{context}: player.y");
            AssertBits(playerVx, other.playerVx, $"{context}: player.vx");
            AssertBits(playerVy, other.playerVy, $"{context}: player.vy");

            Assert.AreEqual(touchCount, other.touchCount, $"{context}: touchCount");
            Assert.AreEqual(coyoteFrames, other.coyoteFrames, $"{context}: coyoteFrames");
            Assert.AreEqual(jumpBufferFrames, other.jumpBufferFrames, $"{context}: jumpBufferFrames");
            Assert.AreEqual(grounded, other.grounded, $"{context}: grounded");
            Assert.AreEqual(isDead, other.isDead, $"{context}: isDead");
            Assert.AreEqual(isWon, other.isWon, $"{context}: isWon");
            if (includeWinningFrames)
                Assert.AreEqual(winningFrames, other.winningFrames, $"{context}: winningFrames");
            Assert.AreEqual(entityPositions, other.entityPositions, $"{context}: entity positions");
            Assert.AreEqual(entityExposure, other.entityExposure, $"{context}: entity exposure");
            Assert.AreEqual(triggerState, other.triggerState, $"{context}: trigger state");
            Assert.AreEqual(activeStates, other.activeStates, $"{context}: active states");
        }

        static void AssertBits(float a, float b, string message)
            => Assert.AreEqual(SimTestHelpers.Bits(a), SimTestHelpers.Bits(b), message);
    }

    internal static class SimTestHelpers
    {
        /// <summary>Exact bit pattern of a float. Equality here means equality, not "close".</summary>
        public static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        /// <summary>
        /// A room with enough going on that a determinism bug has somewhere to show up:
        /// walls, two ledges, hazards, two overlapping air zones, idle flutter, a player and
        /// a scripted run that bumps the balloon, dies and restarts.
        /// </summary>
        public static SimWorld BuildBusyRoom(int seed)
        {
            var world = new SimWorld { EnableFlutter = true };

            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Grid.Fill(4, 6, 10, 1, TileKind.Solid);
            world.Grid.Fill(18, 11, 10, 1, TileKind.Solid);
            world.Grid.Fill(24, 1, 3, 1, TileKind.SpikeUp);

            world.Air.Clear();
            world.Air.Add(new AirZone("z_up", new Rect(6f, 1f, 8f, 12f), new Vector2(1.5f, 5f)));
            world.Air.Add(new AirZone("z_cross", new Rect(10f, 4f, 12f, 6f), new Vector2(-3f, 1f)));
            world.Air.Add(new AirZone("z_off", new Rect(0f, 0f, 32f, 18f), new Vector2(99f, 99f), false));

            // A mover, an accumulator and a wind source: the three shapes of entity state
            // that determinism can break on. Placed clear of the balloon's opening arc so
            // the run is long enough to be worth hashing.
            world.Entities.Clear();
            world.Entities.Add(new SimEntity
            {
                Id = "saw", Kind = EntityKind.Saw, Radius = 0.6f, Speed = 3f,
                Mode = PathMode.PingPong, Phase = 0.25f,
                Points = new[] { new Vector2(20f, 8f), new Vector2(28f, 8f) },
            }, world.Air);

            world.Entities.Add(new SimEntity
            {
                Id = "lamp", Kind = EntityKind.Lamp, Radius = 0.6f, Length = 6f,
                Angle = 270f, Duration = 4f,
                Points = new[] { new Vector2(15f, 16f) },
            }, world.Air);

            world.Entities.Add(new SimEntity
            {
                Id = "fan", Kind = EntityKind.Fan, Radius = 1.2f, Length = 5f,
                Angle = 90f, Power = 4f, Speed = 2f, Mode = PathMode.Loop,
                Points = new[] { new Vector2(4f, 2f), new Vector2(12f, 2f) },
            }, world.Air);

            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Configure(new Vector2(8.3f, 12.1f), new Vector2(2.7f, -1.3f),
                            new Vector2(8.0f, 2.0f), BusyScript(), seed);

            return world;
        }

        /// <summary>
        /// A GDD 9.3 betrayal trap in miniature: a dormant spike bar and an off gust, both
        /// armed by a once:true region the scripted player walks into, one after a delay.
        /// The state triggers add is only observable in a room that actually fires them.
        /// </summary>
        public static SimWorld BuildTrapRoom(int seed)
        {
            var world = new SimWorld { EnableFlutter = true };

            world.Grid.Clear();
            world.Grid.FillBorder();

            world.Air.Clear();
            world.Air.Add(new AirZone("z_gust", new Rect(8f, 1f, 6f, 10f), new Vector2(0f, 6f), false));

            world.Entities.Clear();
            world.Entities.Add(new SimEntity
            {
                Id = "bar", Kind = EntityKind.SpikeBar, Radius = 0.25f, Length = 1f,
                Angle = 90f, Active = false, StartActive = false,
                Points = new[] { new Vector2(14f, 1f) },
            }, world.Air);

            world.Triggers.Clear();
            world.Triggers.Add(new SimTrigger
            {
                Id = "t01", Kind = TriggerKind.PlayerRegion, Once = true,
                Rect = new Rect(9f, 1f, 6f, 4f),
            });
            world.Triggers.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "bar" }, Action = LinkAction.On, DelaySteps = 0,
            });
            world.Triggers.AddLink(new SimLink
            {
                FromIndex = 0, ToIds = new[] { "z_gust" }, Action = LinkAction.Toggle, DelaySteps = 24,
            });

            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Configure(new Vector2(6f, 12f), new Vector2(1.5f, 0f),
                            new Vector2(4f, 1.5f), BusyScript(), seed);

            return world;
        }

        /// <summary>
        /// A room whose floor is taken away by a once:true region the scripted player walks
        /// into. Panels add run state — the enabled flags and the mask built from them — and
        /// state the replay test cannot see is state that rots silently (CLAUDE.md).
        /// </summary>
        public static SimWorld BuildFallingFloorRoom(int seed)
        {
            var grid = new TileGrid();
            grid.FillBorder();
            grid.Fill(8, 1, 12, 1, TileKind.Solid);

            var level = new LevelData
            {
                tiles = grid.ToRows(),
                panels = new[]
                {
                    new LevelPanel { id = "p01", rect = new[] { 12f, 1f, 6f, 1f }, start_state = "on" },
                },
                triggers = new[]
                {
                    new LevelTrigger { id = "t01", type = "player_region", rect = new[] { 6f, 1f, 6f, 4f }, once = true },
                },
                links = new[]
                {
                    new LevelLink { from = "t01", to = new[] { "p01" }, action = "off", delay = 0.25f },
                },
            };

            var world = new SimWorld { EnableFlutter = true, FloorKillsBalloon = false };
            LevelLoader.Apply(level, world, out Vector2 balloonSpawn, out Vector2 playerSpawn);
            world.Configure(balloonSpawn, new Vector2(1.5f, 0f), playerSpawn, BusyScript(), seed);
            return world;
        }

        /// <summary>
        /// A run that exercises the whole surface: movement both ways, jumps into the
        /// balloon, palms and spikes, and the idle frames in between.
        /// </summary>
        public static ScriptedInputSource BusyScript()
        {
            return new ScriptedInputSource.Builder()
                .Wait(20)
                .Move(1f).Wait(30)
                .Jump(10)
                .Wait(15)
                .Move(-1f).Wait(25)
                .Jump(4)
                .Aim(new Vector2(1f, 0f)).Context(3)
                .Wait(20)
                .Move(0f).Aim(new Vector2(0f, 1f)).Context(20)
                .Wait(30)
                .Aim(Vector2.zero).Move(1f).Jump(12)
                .Wait(40)
                .LoopTo(1200)
                .Build();
        }

        /// <summary>
        /// A bare room for contact tests: floor, walls, no hazards, no wind, no flutter, and
        /// the floor made non-lethal so a test can watch a balloon bounce.
        /// </summary>
        public static SimWorld BuildContactRoom(IInputSource input = null)
        {
            var world = new SimWorld { EnableFlutter = false, FloorKillsBalloon = false };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Configure(new Vector2(16f, 8f), Vector2.zero, new Vector2(16f, 1.8f), input, 1);
            return world;
        }

        /// <summary>
        /// A balloon-only room with a reachable exit gate, so a replay test can actually win —
        /// BuildBusyRoom deliberately has no exit (see its comment), which leaves WinningFrames
        /// at zero in every comparison built from it. A straight drop from above the ring is
        /// enough; nothing here needs a player or input to land the win.
        /// </summary>
        public static SimWorld BuildWinnableRoom(int seed)
        {
            var world = new SimWorld
            {
                FloorKillsBalloon = false,
                Exit = new ExitGate
                {
                    Present = true, Position = new Vector2(16f, 12f),
                    Shape = ExitShape.Ring, Diameter = 1.6f,
                },
            };
            world.Grid.Clear();
            world.Grid.FillBorder();
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Reset(new Vector2(16f, 14f), Vector2.zero, seed);
            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            return world;
        }

        /// <summary>True when the balloon's centre is inside a solid tile.</summary>
        public static bool CentreInsideSolid(SimWorld world)
        {
            int tx = Mathf.FloorToInt(world.Balloon.Position.x);
            int ty = Mathf.FloorToInt(world.Balloon.Position.y);
            return world.Grid.IsSolid(tx, ty);
        }

        /// <summary>
        /// Runs the world and folds every step's state into one hash. Catches divergence
        /// anywhere in the run, including divergence that later converges.
        /// </summary>
        public static int RunHash(SimWorld world, int steps)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < steps; i++)
                {
                    world.Tick();
                    SimSnapshot s = SimSnapshot.Capture(world);
                    hash = Fold(hash, Bits(s.balloonX));
                    hash = Fold(hash, Bits(s.balloonY));
                    hash = Fold(hash, Bits(s.balloonVx));
                    hash = Fold(hash, Bits(s.balloonVy));
                    hash = Fold(hash, Bits(s.playerX));
                    hash = Fold(hash, Bits(s.playerY));
                    hash = Fold(hash, s.touchCount);
                    hash = Fold(hash, s.entityPositions);
                    hash = Fold(hash, s.entityExposure);
                }
                return hash;
            }
        }

        static int Fold(int hash, int value) => unchecked((hash ^ value) * 16777619);
    }
}
