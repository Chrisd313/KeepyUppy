using KeepyUppy.Sim;
using UnityEngine;

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

        const int Seed = 1337;

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

        SimDriver _driver;
        FileTuningSource _tuningSource;

        void Awake()
        {
            _driver = gameObject.AddComponent<SimDriver>();

            SimWorld world = _driver.World;
            BuildRoom(world.Grid);
            BuildAir(world.Air);
            LoadTuning(world);

            world.Balloon.Radius = InflationBands.RadiusOf(InflationBand.Nominal);
            world.Configure(
                balloonPos: new Vector2(6f, 13f),
                balloonVel: Vector2.zero,
                playerPos: new Vector2(4f, 2f),
                input: _driver.Keyboard,
                seed: Seed
            );

            BuildCamera();
            BuildTileViews(world.Grid);
            BuildBalloonView(world.Balloon);
            BuildPlayerView(world.Player);

            gameObject.AddComponent<SimDebugOverlay>().Driver = _driver;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TuningPanel panel = gameObject.AddComponent<TuningPanel>();
            panel.Driver = _driver;
            panel.Source = _tuningSource;
            panel.ProfileName = StartingProfile;
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
        /// Box room, two ledges, and enough hazards to make the room hostile. Hard-coded
        /// placeholder geometry — replaced by the JSON loader in a later session.
        /// </summary>
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

        /// <summary>
        /// One updraught over the right-hand ledge, to prove v_air resolves through the
        /// drag term and not through bespoke wind code (GDD 6.2).
        /// </summary>
        static void BuildAir(AirField air)
        {
            air.Clear();
            air.Add(new AirZone("z_updraught", new Rect(20f, 1f, 6f, 10f), new Vector2(0f, 4.5f)));
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
        }

        void BuildTileViews(TileGrid grid)
        {
            var root = new GameObject("Tiles");
            root.transform.SetParent(transform, false);

            for (int y = 0; y < TileGrid.Height; y++)
                for (int x = 0; x < TileGrid.Width; x++)
                {
                    TileKind kind = grid[x, y];
                    if (kind == TileKind.Empty)
                        continue;

                    Color color = kind == TileKind.Solid ? SimPalette.Solid : SimPalette.Hazard;

                    // Static geometry snaps to the tile grid (level-editor-spec 3); the tile
                    // centre sits at (x + 0.5, y + 0.5).
                    QuadView.Create(
                        root.transform,
                        $"tile_{x}_{y}",
                        color,
                        new Vector2(x + 0.5f, y + 0.5f),
                        Vector2.one,
                        0f
                    );
                }
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
