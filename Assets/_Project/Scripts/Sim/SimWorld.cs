using KeepyUppy.InputLayer;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Panels;
using KeepyUppy.Sim.Triggers;
using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// One room's simulation. Owns the clock, the geometry, the air field, the balloon, the
    /// player and the hit system, and is steppable without a scene.
    ///
    /// Everything here is a pure function of (initial state, seed, input, step count).
    /// </summary>
    public sealed class SimWorld
    {
        public readonly SimClock Clock = new SimClock();
        public readonly TileGrid Grid = new TileGrid();
        public readonly AirField Air = new AirField();
        public readonly BalloonBody Balloon = new BalloonBody();
        public readonly PlayerBody Player = new PlayerBody();
        public readonly PlayerVerbs Verbs = new PlayerVerbs();

        /// <summary>Moving hazards (GDD 9). Empty until a level supplies some.</summary>
        public readonly EntityField Entities = new EntityField();

        /// <summary>Triggers and links (GDD 9.3). Stepped last; see Tick.</summary>
        public readonly TriggerField Triggers = new TriggerField();

        /// <summary>Geometry a link can take away (GDD 9.3). Masks tiles; see PanelField.</summary>
        public readonly PanelField Panels = new PanelField();

        /// <summary>Moving panels, as the sweeps read them (moving-panels design).</summary>
        public readonly SolidField Solids = new SolidField();

        /// <summary>The way out (GDD 3, 7.4). Absent in the physics-only test rooms.</summary>
        public ExitGate Exit = ExitGate.None;

        public BalloonTuning Tuning = BalloonTuning.Default;
        public PlayerTuning PlayerTuning = PlayerTuning.Default;

        /// <summary>GDD 5.2. Hot-swappable mid-run. Default B.</summary>
        public ControlProfile Profile = ControlProfile.JumpAndContext;

        /// <summary>Per-level seed. The only input to noise, alongside the step counter.</summary>
        public int Seed;

        /// <summary>Idle flutter (GDD 6.6). Off by default while feel is untuned.</summary>
        public bool EnableFlutter;

        /// <summary>
        /// Whether floor contact ends the run. True in the game; the physics-only tests turn
        /// it off so they can watch the balloon bounce.
        /// </summary>
        public bool FloorKillsBalloon = true;

        /// <summary>Set false for balloon-only rooms; the player is then never stepped.</summary>
        public bool HasPlayer { get; private set; }

        public IInputSource Input { get; private set; }

        public HitResolver Hits { get; private set; }

        // --- Run state ---

        public bool IsDead { get; private set; }
        public DeathCause LastDeath { get; private set; }

        /// <summary>GDD 15: displayed and never hidden. Deliberately survives a restart.</summary>
        public int DeathCount { get; private set; }

        /// <summary>
        /// The balloon is through the gate. The room is over and the sim is holding still.
        /// </summary>
        public bool IsWon { get; private set; }

        /// <summary>Rooms completed. Survives a restart, as the death count does.</summary>
        public int WinCount { get; private set; }

        /// <summary>Touches taken on the winning run (GDD 15's medal number).</summary>
        public int WinningTouches { get; private set; }

        /// <summary>
        /// How many steps the winning attempt took (GDD 15's completion time). Survives a
        /// restart, as the win and death counts do — Clock resets every attempt, so a record
        /// read after the win hold would otherwise always be zero.
        ///
        /// This is the fastest single attempt by construction, not time accumulated across
        /// deaths: nothing here carries over between attempts, which is the point.
        /// </summary>
        public int WinningFrames { get; private set; }

        /// <summary>GDD 15's touch count. Active hits only; passive contacts never count.</summary>
        public int TouchCount => Hits?.TouchCount ?? 0;

        /// <summary>Surfaces the balloon struck on the most recent tick. Presentation hook.</summary>
        public int LastContacts { get; private set; }

        Vector2 _balloonSpawn;
        Vector2 _balloonSpawnVelocity;
        Vector2 _playerSpawn;
        int _deathFrames;
        int _winFrames;

        public SimWorld()
        {
            Hits = new HitResolver(Balloon, Player, Tuning);
        }

        /// <summary>Balloon-only room (session 1 physics tests, and any room without a player).</summary>
        public void Reset(Vector2 balloonPos, Vector2 balloonVel, int seed)
        {
            _balloonSpawn = balloonPos;
            _balloonSpawnVelocity = balloonVel;
            Seed = seed;
            HasPlayer = false;
            Restart();
        }

        /// <summary>Playable room: balloon, player and an input source.</summary>
        public void Configure(Vector2 balloonPos, Vector2 balloonVel, Vector2 playerPos,
                              IInputSource input, int seed)
        {
            _balloonSpawn = balloonPos;
            _balloonSpawnVelocity = balloonVel;
            _playerSpawn = playerPos;
            Input = input;
            Seed = seed;
            HasPlayer = true;
            Restart();
        }

        /// <summary>
        /// GDD 8: full state reset, under 200 ms because it is not a process — no transition,
        /// no coroutine, no delay. Every counter, buffer and cooldown clears.
        ///
        /// <see cref="DeathCount"/> is the one exception: GDD 15 wants a per-level death
        /// counter that is displayed and never hidden, so it survives.
        /// </summary>
        public void Restart()
        {
            Clock.Reset();

            Balloon.Position = _balloonSpawn;
            Balloon.Velocity = _balloonSpawnVelocity;
            Balloon.Popped = false;

            Player.Reset(PlayerTuning, _playerSpawn);
            Verbs.Reset();

            Hits.Tuning = Tuning;
            Hits.Reset();

            // Entity motion is derived from the step counter, so this only clears the state
            // that genuinely accumulates — a lamp's exposure — plus the arm/disarm flags a
            // link may have written (GDD 9.3). Triggers first, so nothing re-applies while
            // the entities and zones are being put back.
            Triggers.Reset();
            Panels.Reset();
            Panels.Apply(Grid);
            Panels.Advance(0);
            Solids.Rebuild(Panels);
            Air.ResetStates();
            Entities.Reset();
            Entities.Step(0, Air);

            Input?.ResetIntents();

            IsDead = false;
            LastDeath = DeathCause.None;
            LastContacts = 0;
            _deathFrames = 0;

            IsWon = false;
            _winFrames = 0;
        }

        /// <summary>
        /// LOCKED GDD 4.1 — linked fate. The balloon pops and the player pops in the SAME
        /// fixed step. One explosion, one sound, one restart. Nothing else in the codebase
        /// may set a death flag.
        /// </summary>
        public void Kill(DeathCause cause)
        {
            if (IsDead) return;

            IsDead = true;
            LastDeath = cause;
            DeathCount++;
            _deathFrames = 0;

            Balloon.Popped = true;
            Player.Popped = true;
        }

        /// <summary>
        /// One 60 Hz step. The order is fixed and the whole simulation depends on it:
        /// input, player, balloon, hazards, contact, verbs.
        /// </summary>
        public void Tick()
        {
            int step = Clock.Step;

            Input?.Sample(step);

            if (Input != null && Input.RestartPressed)
            {
                Restart();
                return;
            }

            if (IsDead)
            {
                // GDD 8: the death is visible for a handful of frames, then the room resets.
                _deathFrames++;
                if (_deathFrames >= PlayerTuning.deathRestartFrames) { Restart(); return; }
                Clock.Advance();
                return;
            }

            if (IsWon)
            {
                // Held, not frozen forever: with one room and no room list yet, the only
                // "next room" available is this one again. Where a level sequence lands, this
                // is the hook it replaces.
                _winFrames++;
                if (_winFrames >= WinHoldFrames) { Restart(); return; }
                Clock.Advance();
                return;
            }

            // BalloonTuning is a value type, so the resolver holds a copy. Refresh it each
            // step or a live tuning-panel edit would never reach ApplyHit.
            Hits.Tuning = Tuning;

            // Hazards move before either body does, so a saw and the balloon it catches are
            // contemporaries rather than a frame apart.
            Entities.Step(step, Air);

            if (HasPlayer) StepPlayer();
            if (!IsDead) StepBalloon();

            if (!IsDead && HasPlayer)
            {
                PlayerBalloonContact.Resolve(Balloon, Player, Hits, Tuning);
                Verbs.Step(Input, Profile, Balloon, Player, Hits, Tuning);
            }

            // Last, so a balloon that reaches the gate and a hazard on the same step dies.
            // The level is allowed the last word; GDD 8's generosity is in the accept volume,
            // not in letting a pop be overridden by arriving.
            if (!IsDead) CheckExit();

            // Last, after contact and the exit check. A trap armed by the player's own
            // movement must not materialise inside the body that armed it in the frame it
            // armed it — that is a death with no frame in which it was avoidable (GDD 8).
            // Effects therefore land on the next step; delay 0 means next step, not this one.
            Panels.BeginStep();
            Triggers.Step(step, Player.Position, HasPlayer, Balloon.Position, Entities, Air, Panels);

            // The mask is rebuilt before the crush check, so a body is tested against tiles
            // that are actually back rather than against a flag.
            if (Panels.Dirty) Panels.Apply(Grid);
            CrushBodiesInReturningPanels();

            Clock.Advance();
        }

        public void Tick(int steps)
        {
            for (int i = 0; i < steps; i++) Tick();
        }

        /// <summary>How long the win is held before the room resets. One second.</summary>
        public const int WinHoldFrames = 60;

        /// <summary>
        /// GDD 3: land it in the exit, next room.
        ///
        /// A balloon too big for the gate passes over it rather than bouncing off the rim.
        /// That is deliberate while inflation is unimplemented: the radius is fixed at spawn,
        /// so a gate too small is an unwinnable level rather than a moment of play, and the
        /// validator says so on save. When inflation lands (GDD 7.2), a too-large balloon
        /// meeting a gate becomes a real event and this is where it gets a collision.
        /// </summary>
        void CheckExit()
        {
            if (IsWon || !Exit.Present) return;
            if (!Exit.Accepts(Balloon.Position, Balloon.Radius, Tuning.exitAcceptBonus)) return;

            IsWon = true;
            _winFrames = 0;
            WinCount++;
            WinningTouches = TouchCount;

            // Clock.Step is the count of steps completed BEFORE this one, and Tick() advances
            // the clock after this runs — so the winning step is only counted by the +1. This
            // is a duration, not an index: a later screen divides it by SimClock.Hz to render
            // a time, and an off-by-one there is a permanently wrong record.
            WinningFrames = Clock.Step + 1;
        }

        /// <summary>
        /// A body caught inside tiles that just came back dies (tile-panels design).
        ///
        /// Only the panels that switched this step are tested, and only their cells that
        /// actually hold a tile — a panel whose rect covers empty space crushes nothing. The
        /// GDD 8 fairness shrink is deliberately NOT applied: the hazard is the tile arriving,
        /// not a hitbox the player was asked to read, and an inset would let a body sit
        /// visibly inside solid geometry and live.
        ///
        /// Public rather than private so the crush tests can drive it without a trigger; the
        /// test assembly is separate, so internal would cost an InternalsVisibleTo attribute.
        /// </summary>
        public void CrushBodiesInReturningPanels()
        {
            if (IsDead) return;

            for (int i = 0; i < Panels.TurnedOnThisStep.Count; i++)
            {
                RectInt rect = Panels[Panels.TurnedOnThisStep[i]].Rect;

                for (int y = rect.yMin; y < rect.yMax; y++)
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    if (!TileGrid.InBounds(x, y) || Grid[x, y] == TileKind.Empty) continue;

                    if (HasPlayer && BoxOverlapsTile(Player.Position, Player.HalfWidth, Player.HalfHeight, x, y))
                    {
                        Kill(DeathCause.Crushed);
                        return;
                    }

                    if (CircleOverlapsTile(Balloon.Position, Balloon.Radius, x, y))
                    {
                        Kill(DeathCause.Crushed);
                        return;
                    }
                }
            }
        }

        static bool BoxOverlapsTile(Vector2 centre, float halfWidth, float halfHeight, int x, int y)
            => centre.x + halfWidth > x && centre.x - halfWidth < x + 1 &&
               centre.y + halfHeight > y && centre.y - halfHeight < y + 1;

        static bool CircleOverlapsTile(Vector2 centre, float radius, int x, int y)
        {
            float dx = centre.x - Mathf.Clamp(centre.x, x, x + 1);
            float dy = centre.y - Mathf.Clamp(centre.y, y, y + 1);
            return dx * dx + dy * dy < radius * radius;
        }

        void StepPlayer()
        {
            Player.Step(PlayerTuning, Input, Grid);

            if (Hazards.PlayerOverlapsLethal(Grid, Player.Position, Player.HalfWidth,
                                             Player.HalfHeight, Tuning.hazardPopRadiusShrink))
            {
                Kill(DeathCause.PlayerHitHazard);
                return;
            }

            if (Entities.ResolvePlayer(Player.Position, Player.HalfWidth, Player.HalfHeight,
                                       Tuning.hazardPopRadiusShrink))
            {
                Kill(DeathCause.PlayerHitHazard);
                return;
            }

            if (Player.Position.y < -2f || Player.Position.y > TileGrid.Height + 2f ||
                Player.Position.x < -2f || Player.Position.x > TileGrid.Width + 2f)
            {
                Kill(DeathCause.PlayerFellOut);
            }
        }

        void StepBalloon()
        {
            if (IsDead) return;

            Vector2 vAir = Air.Sample(Balloon.Position);
            Balloon.Integrate(Tuning, vAir, Clock.Step, Seed, EnableFlutter);

            Vector2 pos = Balloon.Position;
            Vector2 vel = Balloon.Velocity;
            SweepResult result = SweptCircle.Move(Grid, Tuning, Balloon.Radius,
                                                  ref pos, ref vel, SimClock.Dt);
            Balloon.Position = pos;
            Balloon.Velocity = vel;
            LastContacts = result.contacts;

            if (Hazards.BalloonOverlapsLethal(Grid, Balloon.Position, Balloon.Radius,
                                              Tuning.hazardPopRadiusShrink))
            {
                Kill(DeathCause.BalloonHitHazard);
                return;
            }

            // Runs even on the step the balloon dies to something else: a lamp's exposure is
            // aged inside, and skipping it would make the count depend on the death order.
            if (Entities.ResolveBalloon(Balloon.Position, Balloon.Radius,
                                        Tuning.hazardPopRadiusShrink))
            {
                Kill(DeathCause.BalloonHitHazard);
                return;
            }

            // GDD 4.3: gravity is the clock, and this is where it runs out.
            if (FloorKillsBalloon && result.hitFloor) Kill(DeathCause.BalloonHitFloor);

            // The same 2 m margin the player gets, on all four sides. Outside the room rather
            // than at its edge: a bump can carry a body a little past a wall and back, and
            // killing at the boundary would make the walls feel like hazards.
            //
            // Only the bottom is reachable in practice: TileGrid's indexer opens the floor
            // below y = 0 (out-of-bounds reads Empty there) so a level with no floor tile of
            // its own lets the balloon fall out and die here rather than rest on an implicit
            // floor no one placed. The other three sides of the indexer still read Solid, so
            // this is — like the player's equivalent check — a safety net that should be
            // impossible to trigger sideways or upward in a walled level, kept for symmetry
            // and because "impossible today" is not a promise about every future panel shape.
            if (Balloon.Position.y < -2f || Balloon.Position.y > TileGrid.Height + 2f ||
                Balloon.Position.x < -2f || Balloon.Position.x > TileGrid.Width + 2f)
            {
                Kill(DeathCause.BalloonLeftRoom);
            }
        }
    }
}
