using System.Collections.Generic;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.Shell
{
    /// <summary>
    /// The level-select cursor. Worlds are tabs (GDD 15 wants a grid per world), so
    /// left/right changes world and up/down changes level.
    ///
    /// The cursor clamps at either end rather than wrapping: wrapping a short list is
    /// disorienting, because the same input that just moved one step now teleports the
    /// cursor to the opposite end and the player has to relocate it by eye. Clamping keeps
    /// "press the same direction again" a safe, boring no-op instead.
    ///
    /// `LevelIndex` can be malformed in the field - `worlds` null, an entry inside it null,
    /// a world's `levels` null or empty. A world with zero levels is reachable today (the
    /// editor can list a world before filling it) so every read here treats those cases as
    /// "nothing to select" rather than throwing.
    /// </summary>
    public sealed class LevelSelectModel
    {
        readonly LevelIndex index;

        public int WorldCursor { get; private set; }
        public int LevelCursor { get; private set; }

        public LevelSelectModel(LevelIndex index)
        {
            this.index = index;
            WorldCursor = 0;
            LevelCursor = 0;
        }

        public int WorldCount => index.worlds?.Length ?? 0;

        /// <summary>True when there are no worlds, or every world is empty of levels.</summary>
        public bool IsEmpty
        {
            get
            {
                if (index.worlds == null) return true;

                foreach (LevelWorld world in index.worlds)
                    if (world?.levels != null && world.levels.Length > 0) return false;

                return true;
            }
        }

        /// <summary>The current world, or null if there are none.</summary>
        public LevelWorld CurrentWorld
        {
            get
            {
                if (index.worlds == null || WorldCursor < 0 || WorldCursor >= index.worlds.Length)
                    return null;

                return index.worlds[WorldCursor];
            }
        }

        /// <summary>The levels of the current world, never null - an empty list stands in for a missing one.</summary>
        public IReadOnlyList<string> CurrentWorldLevels =>
            (IReadOnlyList<string>)CurrentWorld?.levels ?? System.Array.Empty<string>();

        /// <summary>The id of the selected level, or null when there is nothing to select.</summary>
        public string CurrentLevel
        {
            get
            {
                IReadOnlyList<string> levels = CurrentWorldLevels;
                if (LevelCursor < 0 || LevelCursor >= levels.Count) return null;

                return levels[LevelCursor];
            }
        }

        /// <summary>
        /// Moves the cursor. `step.y` moves within the current world (down increases the
        /// index, matching the on-screen list running top to bottom); `step.x` changes world.
        /// Both clamp - see the class comment - and both are safe on an empty manifest.
        ///
        /// `Vector2Int.down` is (0, -1) - Unity's y axis points up - so it is inverted here
        /// to make "down" move down the on-screen list rather than up it.
        /// </summary>
        public void Move(Vector2Int step)
        {
            if (step.x != 0) MoveWorld(step.x);
            if (step.y != 0) MoveLevel(-step.y);
        }

        void MoveWorld(int delta)
        {
            int count = WorldCount;
            if (count == 0) return;

            WorldCursor = Mathf.Clamp(WorldCursor + delta, 0, count - 1);
            ClampLevelCursor();
        }

        void MoveLevel(int delta)
        {
            int count = CurrentWorldLevels.Count;
            if (count == 0) return;

            LevelCursor = Mathf.Clamp(LevelCursor + delta, 0, count - 1);
        }

        /// <summary>
        /// Re-clamps the level cursor after a world change, so a cursor left at the bottom
        /// of a long world doesn't point past the end of a shorter one.
        /// </summary>
        void ClampLevelCursor()
        {
            int count = CurrentWorldLevels.Count;
            LevelCursor = count == 0 ? 0 : Mathf.Clamp(LevelCursor, 0, count - 1);
        }
    }
}
