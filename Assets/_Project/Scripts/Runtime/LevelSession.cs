using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using UnityEngine;
#if LEVEL_EDITOR
using KeepyUppy.LevelEditing;
#endif

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Everything one played level needs: the driver, the world it ticks, and every view
    /// that reads that world. All of it lives under a single root <see cref="GameObject"/>
    /// so tearing a level down is one <c>Destroy</c> call with no per-view cleanup to
    /// forget — the boot/shell layer only ever destroys the session as a whole, never a
    /// piece of it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelSession : MonoBehaviour
    {
        public SimDriver Driver { get; private set; }
        public string LevelId { get; private set; }

        /// <summary>
        /// The data this session was built from. The editor needs it to seed a
        /// LevelEditorController — everything else here only reads the live SimWorld.
        /// </summary>
        public LevelData Level { get; private set; }

        /// <summary>
        /// Builds a level and everything it needs to run. The world must be fully configured
        /// before any view is built, since every view reads it at construction time: the
        /// root is made, <see cref="SimDriver"/> is added, the tuning profile is applied,
        /// then <see cref="LevelLoader.ApplyAndConfigure"/> populates geometry, zones,
        /// entities, the exit and the spawns — only then do the views get built.
        /// </summary>
        public static LevelSession Create(
            Transform parent,
            LevelData level,
            string id,
            TuningProfile tuning,
            FileTuningSource tuningSource,
            int seed)
        {
            var go = new GameObject("[Level Session]");
            go.transform.SetParent(parent, false);

            var session = go.AddComponent<LevelSession>();
            session.LevelId = id;
            session.Level = level;

            var driver = go.AddComponent<SimDriver>();
            session.Driver = driver;

            SimWorld world = driver.World;
            world.Tuning = tuning.balloon;
            world.PlayerTuning = tuning.player;

            LevelLoader.ApplyAndConfigure(level, world, driver.Keyboard, seed);

            session.BuildTileViews(world.Grid);
            session.BuildZoneViews(world);
            session.BuildTriggerViews(world);
            session.BuildEntityViews(world, driver);
            session.BuildExitView(world);
            session.BuildBalloonView(world.Balloon, driver);
            session.BuildPlayerView(world.Player, driver);

            session.gameObject.AddComponent<SimDebugOverlay>().Driver = driver;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TuningPanel panel = session.gameObject.AddComponent<TuningPanel>();
            panel.Driver = driver;
            panel.Source = tuningSource;
            panel.ProfileName = SimBootstrap.StartingProfile;
#endif

            return session;
        }

        /// <summary>The room's own reset (GDD 8: restart under 200 ms), not a rebuild.</summary>
        public void Restart() => Driver.World.Restart();

#if LEVEL_EDITOR
        /// <summary>
        /// Turns this session into an editing session: installs LevelEditorController and
        /// hands it a host onto this session's own driver and world. Moved here from
        /// SimBootstrap.Awake now that a session, not the boot object, is what the editor
        /// needs — the editor is a mode of a level session, not a separate path.
        /// </summary>
        public void BeginEditing(FileLevelSource levels, Camera camera)
        {
            var editor = gameObject.AddComponent<LevelEditorController>();
            editor.Host = new BootstrapEditorHost(Driver, camera, levels);
            editor.LevelId = LevelId;
            editor.Begin(Level);
        }
#endif

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

        /// <summary>
        /// Air zones are drawn behind the entities and in front of the tiles. Without this
        /// they act on the balloon while being invisible, which reads as the balloon bouncing
        /// off geometry that is not there (GDD 6.2).
        /// </summary>
        void BuildZoneViews(SimWorld world)
        {
            var root = new GameObject("Zones");
            root.transform.SetParent(transform, false);

            root.AddComponent<ZoneViewSync>().World = world;
        }

        /// <summary>
        /// Only the triggers authored visible:true are drawn here; the rest stay invisible
        /// in play, which is the point of the betrayal trap (GDD 9.3).
        /// </summary>
        void BuildTriggerViews(SimWorld world)
        {
            var root = new GameObject("Triggers");
            root.transform.SetParent(transform, false);

            root.AddComponent<TriggerViewSync>().World = world;
        }

        /// <summary>Hazard quads follow the entities, which move every step (GDD 9).</summary>
        void BuildEntityViews(SimWorld world, SimDriver driver)
        {
            var root = new GameObject("Entities");
            root.transform.SetParent(transform, false);

            var sync = root.AddComponent<EntityViewSync>();
            sync.World = world;
            sync.Driver = driver;
        }

        /// <summary>The gate follows the world's exit, so editing it shows up at once.</summary>
        void BuildExitView(SimWorld world)
        {
            var root = new GameObject("Exit");
            root.transform.SetParent(transform, false);

            root.AddComponent<ExitView>().World = world;
        }

        void BuildBalloonView(BalloonBody balloon, SimDriver driver)
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
            var view = go.AddComponent<BalloonView>();
            view.Body = balloon;
            view.Driver = driver;
        }

        void BuildPlayerView(PlayerBody player, SimDriver driver)
        {
            GameObject go = QuadView.Create(
                transform,
                "Player",
                SimPalette.Player,
                player.Position,
                new Vector2(player.HalfWidth * 2f, player.HalfHeight * 2f),
                -0.5f
            );
            var view = go.AddComponent<PlayerView>();
            view.Body = player;
            view.Driver = driver;
        }
    }
}
