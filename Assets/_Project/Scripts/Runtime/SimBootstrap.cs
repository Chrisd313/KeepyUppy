using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using UnityEngine;
#if LEVEL_EDITOR
using KeepyUppy.LevelEditing;
#endif

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Builds a playable room entirely in code: press Play in any scene and watch the
    /// balloon fall and bounce. Nothing is authored in a .unity file, so there is no scene
    /// to keep in sync and no prefab to drift.
    ///
    /// Temporary. Replaced by the JSON level loader (level-editor-spec 3) once ILevelSource
    /// exists.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SimBootstrap : MonoBehaviour
    {
        /// <summary>Set false to stop the bootstrap auto-installing itself.</summary>
        public static bool AutoInstall = true;

        public const int Seed = 1337;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!AutoInstall)
                return;
            if (FindAnyObjectByType<SimBootstrap>() != null)
                return;

            var go = new GameObject("[KeepyUppy Sim]");
            go.AddComponent<SimBootstrap>();
            DontDestroyOnLoad(go);
        }

        /// <summary>Profile loaded at startup. Change it to boot into a different feel.</summary>
        public static string StartingProfile = FileTuningSource.DefaultProfileName;

        /// <summary>Level loaded at startup. Change it to boot into a different room.</summary>
        public static string StartingLevel = "w1_01";

        SimDriver _driver;
        FileTuningSource _tuningSource;
        FileLevelSource _levelSource;
        LevelData _level;
        Camera _camera;

        void Awake()
        {
            _driver = gameObject.AddComponent<SimDriver>();

            SimWorld world = _driver.World;
            LoadTuning(world);
            LoadLevel(world, out Vector2 balloonSpawn, out Vector2 playerSpawn);

            world.Configure(
                balloonPos: balloonSpawn,
                balloonVel: Vector2.zero,
                playerPos: playerSpawn,
                input: _driver.Keyboard,
                seed: Seed
            );

            BuildCamera();
            BuildTileViews(world.Grid);
            BuildEntityViews(world);
            BuildExitView(world);
            BuildBalloonView(world.Balloon);
            BuildPlayerView(world.Player);

            gameObject.AddComponent<SimDebugOverlay>().Driver = _driver;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TuningPanel panel = gameObject.AddComponent<TuningPanel>();
            panel.Driver = _driver;
            panel.Source = _tuningSource;
            panel.ProfileName = StartingProfile;
#endif

#if LEVEL_EDITOR
            var editor = gameObject.AddComponent<LevelEditorController>();
            editor.Host = new BootstrapEditorHost(this, _driver, _camera, _levelSource);
            editor.LevelId = StartingLevel;
            editor.Begin(_level);
#endif
        }

        /// <summary>
        /// Tuning comes from a named JSON profile (CLAUDE.md, level-editor-spec 6), not from
        /// literals in this file. The code defaults are only the seed used to write the first
        /// profile, so default.json is correct by construction rather than hand-maintained.
        /// </summary>
        void LoadTuning(SimWorld world)
        {
            _tuningSource = new FileTuningSource();
            _tuningSource.SaveIfAbsent(FileTuningSource.DefaultProfileName, TuningProfile.Default);

            if (!_tuningSource.TryLoad(StartingProfile, out TuningProfile profile))
            {
                Debug.LogWarning($"[tuning] profile '{StartingProfile}' not found; using code defaults");
                return;
            }

            world.Tuning = profile.balloon;
            world.PlayerTuning = profile.player;
        }

        /// <summary>
        /// Levels are JSON (level-editor-spec 3), and the shipping game and the editor read
        /// the same file. If the level is missing, the room built in code below seeds it once
        /// — so the file is correct by construction rather than hand-typed, and after that the
        /// JSON is the only source of truth.
        /// </summary>
        void LoadLevel(SimWorld world, out Vector2 balloonSpawn, out Vector2 playerSpawn)
        {
            _levelSource = new FileLevelSource();
            _levelSource.SaveIfAbsent(StartingLevel, SeedLevel());

            // The manifest decides what is in the game. Seeded alongside the level so a fresh
            // clone has a coherent level list rather than a room nothing lists.
            if (!_levelSource.TryLoadIndex(out LevelIndex index))
            {
                index = LevelIndex.Default();
                _levelSource.SaveIndex(index);
            }

            // The starting level is the manifest's first, so adding a level before w1_01 in
            // the editor changes what the game boots into - which is what a level list is for.
            IReadOnlyList<string> listed = index.AllLevels();
            if (listed.Count > 0) StartingLevel = listed[0];

            if (!_levelSource.TryLoad(StartingLevel, out LevelData level))
            {
                Debug.LogWarning($"[level] '{StartingLevel}' not found; falling back to the seed room");
                level = SeedLevel();
            }

            List<string> warnings = LevelValidator.Validate(level);
            foreach (string warning in warnings) Debug.LogWarning($"[level] {StartingLevel}: {warning}");

            _level = level;
            LevelLoader.Apply(level, world, out balloonSpawn, out playerSpawn);
        }

        /// <summary>
        /// The room this project used before levels were files. Kept only to seed the first
        /// JSON; edit the JSON from here on, not this method.
        /// </summary>
        public static LevelData SeedLevel()
        {
            var grid = new TileGrid();
            BuildRoom(grid);

            return new LevelData
            {
                id = StartingLevel,
                name = "The Test Room",
                world = 1,
                index = 1,
                tiles = grid.ToRows(),
                spawn = new LevelSpawn
                {
                    player = new[] { 4f, 2f },
                    balloon = new[] { 6f, 13f },
                    balloon_inflation = "nominal",
                },
                exit = new LevelExit
                {
                    pos = new[] { 29f, 4f },
                    shape = "ring",
                    diameter = 1.6f,
                    required_band = "nominal",
                },
                zones = new[]
                {
                    new LevelZone
                    {
                        id = "z_updraught",
                        type = "air",
                        rect = new[] { 20f, 1f, 6f, 10f },
                        @params = new ZoneParams { vy = 4.5f },
                        start_state = "on",
                    },
                },
                meta = new LevelMeta
                {
                    par_touches = 7,
                    author_note = "Seeded from the pre-JSON hardcoded room.",
                    tuning_profile = FileTuningSource.DefaultProfileName,
                },
            };
        }

        static void BuildRoom(TileGrid grid)
        {
            grid.Clear();
            grid.FillBorder();

            // grid.Fill(4, 6, 10, 1, TileKind.Solid);
            grid.Fill(18, 11, 10, 1, TileKind.Solid);

            // Floor spikes: a stretch of ground the player must jump, and the balloon must
            // not drift over low.
            grid.Fill(13, 1, 6, 1, TileKind.SpikeUp);

            // Ceiling spikes over the left half — punishes an over-powered bump.
            grid.Fill(8, 16, 4, 1, TileKind.SpikeDown);

            // Wall spikes on the right, pointing back into the room.
            grid.Fill(30, 6, 1, 4, TileKind.SpikeLeft);
        }

        void BuildCamera()
        {
            // Whatever camera the open scene happens to have is not ours and is framed
            // wrong. Disabled at runtime only — no scene file is touched.
            foreach (Camera existing in Camera.allCameras)
                existing.enabled = false;

            var go = new GameObject("SimCamera");
            go.transform.SetParent(transform, false);

            // 32 x 18 metres exactly: orthographicSize is half the vertical extent.
            go.transform.localPosition = new Vector3(
                TileGrid.Width * 0.5f,
                TileGrid.Height * 0.5f,
                -10f
            );

            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = TileGrid.Height * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SimPalette.Background;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            _camera = cam;
        }

        /// <summary>
        /// Tile quads follow the grid rather than being built once, so painting in the editor
        /// shows up immediately.
        /// </summary>
        void BuildTileViews(TileGrid grid)
        {
            var root = new GameObject("Tiles");
            root.transform.SetParent(transform, false);

            root.AddComponent<TileViewSync>().Grid = grid;
        }

        /// <summary>Hazard quads follow the entities, which move every step (GDD 9).</summary>
        void BuildEntityViews(SimWorld world)
        {
            var root = new GameObject("Entities");
            root.transform.SetParent(transform, false);

            root.AddComponent<EntityViewSync>().World = world;
        }

        /// <summary>The gate follows the world's exit, so editing it shows up at once.</summary>
        void BuildExitView(SimWorld world)
        {
            var root = new GameObject("Exit");
            root.transform.SetParent(transform, false);

            root.AddComponent<ExitView>().World = world;
        }

        void BuildBalloonView(BalloonBody balloon)
        {
            float d = balloon.Radius * 2f;
            GameObject go = QuadView.Create(
                transform,
                "Balloon",
                SimPalette.Balloon,
                balloon.Position,
                new Vector2(d, d),
                -1f
            );
            go.AddComponent<BalloonView>().Body = balloon;
        }

        void BuildPlayerView(PlayerBody player)
        {
            GameObject go = QuadView.Create(
                transform,
                "Player",
                SimPalette.Player,
                player.Position,
                new Vector2(player.HalfWidth * 2f, player.HalfHeight * 2f),
                -0.5f
            );
            go.AddComponent<PlayerView>().Body = player;
        }
    }
}
