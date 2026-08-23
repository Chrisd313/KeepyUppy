using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// The editor's state and every mutation it can perform, with no scene, no camera and no
    /// input. The MonoBehaviour on top is just plumbing; this is the part worth testing.
    ///
    /// The working grid is authoritative while editing and is written back into
    /// <see cref="Level"/> whenever the level is snapshotted, saved or played — so the tiles
    /// on screen and the tiles in the file can never drift apart.
    /// </summary>
    public sealed class EditorSession
    {
        public LevelData Level { get; private set; } = new LevelData();

        /// <summary>
        /// Working geometry, mirroring Level.tiles.
        ///
        /// Normally this IS the live world's grid, handed in by the host — painting a tile
        /// then shows up immediately, because there is only one grid and the renderer is
        /// already watching it. Given no grid it owns a private one, which is what the tests
        /// use.
        /// </summary>
        public TileGrid Grid { get; }

        public EditorSession(TileGrid grid = null)
        {
            Grid = grid ?? new TileGrid();
        }

        public readonly UndoStack Undo = new UndoStack();

        public EditorMode Mode = EditorMode.Edit;
        public EditorTool Tool = EditorTool.Paint;
        public TileKind SelectedTile = TileKind.Solid;
        public string SelectedEntityId;

        /// <summary>Unsaved changes exist. Shown in the status bar; never blocks anything.</summary>
        public bool Dirty { get; private set; }

        /// <summary>Entity types offered by the palette. Data only — none of them do anything yet.</summary>
        public static readonly string[] EntityTypes =
        {
            "saw", "cat", "fan", "prop_leafblower", "prop_trampoline", "prop_tape",
        };

        public void Load(LevelData level)
        {
            Level = level ?? new LevelData();
            Grid.LoadRows(Level.tiles ?? new string[0]);
            Undo.Clear();
            SelectedEntityId = null;
            Dirty = false;
        }

        /// <summary>Flushes the working grid into the level data. Call before saving or playing.</summary>
        public LevelData Flush()
        {
            Level.tiles = Grid.ToRows();
            return Level;
        }

        public void MarkSaved() => Dirty = false;

        /// <summary>Records the pre-edit state. Once per stroke, not per tile.</summary>
        public void BeginEdit()
        {
            Undo.Push(Flush());
            Dirty = true;
        }

        public bool UndoEdit()
        {
            LevelData restored = Undo.Undo(Flush());
            if (restored == null) return false;

            Restore(restored);
            return true;
        }

        public bool RedoEdit()
        {
            LevelData restored = Undo.Redo(Flush());
            if (restored == null) return false;

            Restore(restored);
            return true;
        }

        void Restore(LevelData level)
        {
            Level = level;
            Grid.LoadRows(level.tiles ?? new string[0]);
            Dirty = true;

            // The selection may have been deleted by the edit we just reversed.
            if (FindEntity(SelectedEntityId) == null) SelectedEntityId = null;
        }

        // ------------------------------------------------------------------ tiles

        public void Paint(Vector2Int tile, TileKind kind)
        {
            if (!GridCursor.InBounds(tile)) return;
            if (Grid[tile.x, tile.y] == kind) return;

            Grid[tile.x, tile.y] = kind;
            Dirty = true;
        }

        public void FillRect(Vector2Int a, Vector2Int b, TileKind kind)
        {
            RectInt rect = GridCursor.TileRect(a, b);
            Grid.Fill(rect.x, rect.y, rect.width, rect.height, kind);
            Dirty = true;
        }

        // --------------------------------------------------------------- entities

        public LevelEntity FindEntity(string id)
        {
            if (string.IsNullOrEmpty(id) || Level.entities == null) return null;

            foreach (LevelEntity entity in Level.entities)
                if (entity != null && entity.id == id) return entity;

            return null;
        }

        /// <summary>Entity within half a tile of a world position — what a click hits.</summary>
        public LevelEntity EntityAt(Vector2 world, float radius = 0.5f)
        {
            if (Level.entities == null) return null;

            LevelEntity best = null;
            float bestDistance = radius * radius;

            foreach (LevelEntity entity in Level.entities)
            {
                if (entity?.pos == null || entity.pos.Length < 2) continue;

                float dx = entity.pos[0] - world.x;
                float dy = entity.pos[1] - world.y;
                float distance = dx * dx + dy * dy;

                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = entity;
            }

            return best;
        }

        public LevelEntity PlaceEntity(string type, Vector2 world)
        {
            Vector2 snapped = GridCursor.SnapHalfTile(world);

            var entity = new LevelEntity
            {
                id = NextEntityId(),
                type = type,
                pos = new[] { snapped.x, snapped.y },
            };

            var entities = new List<LevelEntity>(Level.entities ?? new LevelEntity[0]) { entity };
            Level.entities = entities.ToArray();

            SelectedEntityId = entity.id;
            Dirty = true;
            return entity;
        }

        public void MoveEntity(string id, Vector2 world)
        {
            LevelEntity entity = FindEntity(id);
            if (entity == null) return;

            Vector2 snapped = GridCursor.SnapHalfTile(world);
            entity.pos = new[] { snapped.x, snapped.y };
            Dirty = true;
        }

        public bool DeleteEntity(string id)
        {
            LevelEntity entity = FindEntity(id);
            if (entity == null) return false;

            var entities = new List<LevelEntity>(Level.entities);
            entities.Remove(entity);
            Level.entities = entities.ToArray();

            if (SelectedEntityId == id) SelectedEntityId = null;
            Dirty = true;
            return true;
        }

        /// <summary>Lowest unused eNN id, so ids stay short and predictable after deletions.</summary>
        string NextEntityId()
        {
            for (int i = 1; i < 1000; i++)
            {
                string candidate = "e" + i.ToString("00");
                if (FindEntity(candidate) == null) return candidate;
            }

            return "e" + Level.entities.Length;
        }
    }
}
