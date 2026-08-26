#if LEVEL_EDITOR
using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Connects the level editor to the running game.
    ///
    /// The editor never references this assembly — Runtime installs the editor, so a
    /// reference back would be circular. It asks for what it needs through
    /// <see cref="ILevelEditorHost"/> and this supplies it.
    /// </summary>
    sealed class BootstrapEditorHost : ILevelEditorHost
    {
        readonly SimDriver _driver;
        readonly Camera _camera;
        readonly FileLevelSource _levels;

        public BootstrapEditorHost(SimDriver driver, Camera camera, FileLevelSource levels)
        {
            _driver = driver;
            _camera = camera;
            _levels = levels;
        }

        public SimWorld World => _driver.World;
        public Camera Camera => _camera;
        public ILevelSource Levels => _levels;

        /// <summary>
        /// Rebuilds the room from level data and restarts. Play therefore always starts from
        /// the author's current unsaved state, which is the point of the Tab toggle.
        /// </summary>
        public void ApplyLevel(LevelData level)
        {
            LevelLoader.Apply(level, World, out Vector2 balloon, out Vector2 player);

            World.Configure(
                balloonPos: balloon,
                balloonVel: Vector2.zero,
                playerPos: player,
                input: _driver.Keyboard,
                seed: SimBootstrap.Seed);
        }

        public void SyncEntities(LevelData level) => LevelLoader.ApplyEntities(level, World);

        public void SyncExit(LevelData level) => World.Exit = LevelLoader.ToGate(level.exit);

        public void SetGameplaySuppressed(bool suppressed)
            => DevInputCapture.EditorModeActive = suppressed;

        public void SetSimPaused(bool paused) => _driver.EditorPaused = paused;
    }
}
#endif
