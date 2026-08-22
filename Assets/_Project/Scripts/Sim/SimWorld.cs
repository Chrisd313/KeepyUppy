using KeepyUppy.InputLayer;
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

        /// <summary>GDD 15's touch count. Active hits only; passive contacts never count.</summary>
        public int TouchCount => Hits?.TouchCount ?? 0;

        /// <summary>Surfaces the balloon struck on the most recent tick. Presentation hook.</summary>
        public int LastContacts { get; private set; }

        Vector2 _balloonSpawn;
        Vector2 _balloonSpawnVelocity;
        Vector2 _playerSpawn;
        int _deathFrames;

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

            Input?.ResetIntents();

            IsDead = false;
            LastDeath = DeathCause.None;
            LastContacts = 0;
            _deathFrames = 0;
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

            // BalloonTuning is a value type, so the resolver holds a copy. Refresh it each
            // step or a live tuning-panel edit would never reach ApplyHit.
            Hits.Tuning = Tuning;

            if (HasPlayer) StepPlayer();
            if (!IsDead) StepBalloon();

            if (!IsDead && HasPlayer)
            {
                PlayerBalloonContact.Resolve(Balloon, Player, Hits, Tuning);
                Verbs.Step(Input, Profile, Balloon, Player, Hits, Tuning);
            }

            Clock.Advance();
        }

        public void Tick(int steps)
        {
            for (int i = 0; i < steps; i++) Tick();
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

            // GDD 4.3: gravity is the clock, and this is where it runs out.
            if (FloorKillsBalloon && result.hitFloor) Kill(DeathCause.BalloonHitFloor);
        }
    }
}
