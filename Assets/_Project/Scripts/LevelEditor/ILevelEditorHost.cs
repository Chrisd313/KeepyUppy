using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// Everything the editor needs from the game it is embedded in.
    ///
    /// Exists so the editor never references the Runtime assembly: Runtime installs the
    /// editor, so a reference back the other way would be circular. It also keeps the editor
    /// honest about its dependencies — this list is all of them.
    /// </summary>
    public interface ILevelEditorHost
    {
        SimWorld World { get; }

        /// <summary>The room camera, for screen-to-world mapping.</summary>
        Camera Camera { get; }

        ILevelSource Levels { get; }

        /// <summary>
        /// Applies a level and restarts the run: geometry, zones, spawns and input. Called on
        /// entering play, so play always starts from the current unsaved state.
        /// </summary>
        void ApplyLevel(LevelData level);

        /// <summary>
        /// Refreshes zones and entities from the level being edited, leaving geometry, the
        /// spawns and the run alone. Called after every zone or entity edit so a dragged saw
        /// moves on screen immediately, as a painted tile does.
        ///
        /// Named for what it reloads rather than for entities alone: it has always reloaded
        /// zones too (LevelLoader.ApplyEntities calls LoadZones), and the old name hid that
        /// from anyone looking for somewhere to push a zone edit.
        /// </summary>
        void SyncDynamic(LevelData level);

        /// <summary>
        /// Refreshes the exit gate alone, for the same reason as SyncDynamic: dragging the
        /// way out should move it on screen rather than restart the room.
        /// </summary>
        void SyncExit(LevelData level);

        /// <summary>
        /// Silences gameplay input while the author is editing, so painting cannot also make
        /// the player jump.
        /// </summary>
        void SetGameplaySuppressed(bool suppressed);

        /// <summary>
        /// Freezes the fixed-step sim while authoring, so the balloon is not falling, dying
        /// and restarting behind the tiles being placed.
        /// </summary>
        void SetSimPaused(bool paused);
    }
}
