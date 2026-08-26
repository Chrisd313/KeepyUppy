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

        /// <summary>
        /// Level loaded at startup. The manifest is authoritative and overwrites this with its
        /// first listed level whenever it lists anything (see PrepareLevelSource below) — so
        /// changing this constant only affects a genuinely fresh project with no manifest yet.
        /// To boot into a different room, reorder the manifest instead.
        /// </summary>
        public static string StartingLevel = "w1_01";

        FileTuningSource _tuningSource;
        TuningProfile _tuningProfile;
        FileLevelSource _levelSource;
        LevelIndex _index;
        Camera _camera;

        /// <summary>The level currently being played or edited, or null on the menus.</summary>
        public LevelSession Session { get; private set; }

        public ILevelSource Levels => _levelSource;
        public LevelIndex Index => _index;

        void Awake()
        {
            LoadTuning();
            PrepareLevelSource();

            BuildCamera();
            BuildShellView();

            // TODO(Task 8): the level editor needs a running driver, so it starts a session
            // (StartLevel(StartingLevel)) and installs itself into it, rather than running
            // here at boot as it did before the menu shell existed.
        }

        /// <summary>
        /// Builds a level and everything it needs. Returns null if the level cannot be read,
        /// leaving the caller on the menu rather than in a half-built room.
        /// </summary>
        public LevelSession StartLevel(string id)
        {
            EndLevel();

            if (string.IsNullOrEmpty(id))
                return null;

            if (!_levelSource.TryLoad(id, out LevelData level))
            {
                Debug.LogWarning($"[level] '{id}' could not be loaded");
                return null;
            }

            Session = LevelSession.Create(transform, level, id, _tuningProfile, _tuningSource, Seed);
            return Session;
        }

        /// <summary>
        /// Destroys the level and everything it built. One Destroy, because the session is a
        /// single root — nothing here has to remember which views exist.
        /// </summary>
        public void EndLevel()
        {
            if (Session == null) return;

            Destroy(Session.gameObject);
            Session = null;
        }

        /// <summary>
        /// Tuning comes from a named JSON profile (CLAUDE.md, level-editor-spec 6), not from
        /// literals in this file. The code defaults are only the seed used to write the first
        /// profile, so default.json is correct by construction rather than hand-maintained.
        /// There is no world yet to apply it to — that happens per level in
        /// <see cref="LevelSession.Create"/>.
        /// </summary>
        void LoadTuning()
        {
            _tuningSource = new FileTuningSource();
            _tuningSource.SaveIfAbsent(FileTuningSource.DefaultProfileName, TuningProfile.Default);

            if (!_tuningSource.TryLoad(StartingProfile, out TuningProfile profile))
            {
                Debug.LogWarning($"[tuning] profile '{StartingProfile}' not found; using code defaults");
                profile = TuningProfile.Default;
            }

            _tuningProfile = profile;
        }

        /// <summary>
        /// Levels are JSON (level-editor-spec 3), and the shipping game and the editor read
        /// the same file. This only prepares the source and the manifest — no level is loaded
        /// here, since there is no session to load one into until <see cref="StartLevel"/> is
        /// called.
        /// </summary>
        void PrepareLevelSource()
        {
            _levelSource = new FileLevelSource();

            // The manifest decides what is in the game. The level seed only runs when
            // TryPrepareIndex reports a genuinely fresh project - see its own comment for why.
            bool freshProject = TryPrepareIndex(_levelSource, out LevelIndex index);
            if (freshProject) _levelSource.SaveIfAbsent(StartingLevel, SeedLevel());

            // The starting level is the manifest's first, so adding a level before w1_01 in
            // the editor changes what the game boots into - which is what a level list is for.
            IReadOnlyList<string> listed = index.AllLevels();
            if (listed.Count > 0) StartingLevel = listed[0];

            _index = index;
        }

        /// <summary>
        /// Loads the manifest, writing <see cref="LevelIndex.Default"/> when none exists yet.
        /// Returns true only in that no-manifest case, meaning "this is a fresh project, seed
        /// the starting level too" - a manifest that already exists is authoritative and may no
        /// longer list <see cref="StartingLevel"/>, so it must never be resurrected on disk.
        ///
        /// Public and static so this decision - the part of boot most likely to regress
        /// silently - is reachable from an EditMode test without a scene. Do not fold it back
        /// into <see cref="PrepareLevelSource"/>; that is what made the bug it fixes untestable.
        /// </summary>
        public static bool TryPrepareIndex(ILevelSource source, out LevelIndex index)
        {
            if (source.TryLoadIndex(out index)) return false;

            index = LevelIndex.Default();
            source.SaveIndex(index);
            return true;
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
        /// The shell is the only thing that exists at boot besides the camera — no
        /// <see cref="SimDriver"/>, no world, no views, until a level is chosen.
        /// </summary>
        void BuildShellView()
        {
            var go = new GameObject("Shell");
            go.transform.SetParent(transform, false);

            var shell = go.AddComponent<ShellView>();
            shell.Bootstrap = this;
        }
    }
}
