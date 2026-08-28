using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
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

        /// <summary>
        /// The selected zone, kept separately from <see cref="SelectedEntityId"/>: a zone is
        /// not an entity, and sharing the field would mean every read had to ask which kind
        /// of thing it had.
        /// </summary>
        public string SelectedZoneId;

        /// <summary>
        /// The exit is selected instead of an entity. Kept as a separate flag rather than a
        /// magic entity id: the gate is not an entity, and pretending otherwise would leak
        /// into the entity list, the id allocator and every foreach over it.
        /// </summary>
        public bool ExitSelected;

        /// <summary>Unsaved changes exist. Shown in the status bar; never blocks anything.</summary>
        public bool Dirty { get; private set; }

        /// <summary>
        /// The manifest (spec: menus and the level list). Held here rather than in the
        /// controller so every mutation is testable without a scene, as with everything else
        /// in this class.
        /// </summary>
        public LevelIndex Index { get; private set; } = new LevelIndex();

        /// <summary>
        /// Unsaved manifest changes. Deliberately separate from <see cref="Dirty"/>: the
        /// manifest is a different file with a different save, and conflating them would make
        /// saving a level appear to save the list.
        /// </summary>
        public bool IndexDirty { get; private set; }

        public void LoadIndex(LevelIndex index)
        {
            Index = index ?? new LevelIndex();
            IndexDirty = false;
        }

        public void MarkIndexSaved() => IndexDirty = false;

        /// <summary>
        /// Lists a level at the end of a world, creating the world if it does not exist yet -
        /// authoring world 3 before world 3 exists is normal, and refusing would only mean
        /// hand-editing the manifest, which is what this exists to avoid.
        /// </summary>
        public LevelWorld AddLevel(string id, string worldId)
        {
            if (string.IsNullOrEmpty(id)) return null;

            LevelWorld existing = Index.WorldOf(id);
            if (existing != null) return existing;

            LevelWorld world = FindWorld(worldId);

            if (world == null)
            {
                world = new LevelWorld { id = worldId, name = worldId, levels = new string[0] };

                var worlds = new List<LevelWorld>(Index.worlds ?? new LevelWorld[0]) { world };
                Index.worlds = worlds.ToArray();
            }

            var levels = new List<string>(world.levels ?? new string[0]) { id };
            world.levels = levels.ToArray();

            IndexDirty = true;
            return world;
        }

        /// <summary>
        /// Unlists a level, turning it into a draft. The file is deliberately left on disk:
        /// losing authored work to a mis-click is worse than an unlisted file.
        /// </summary>
        public bool RemoveLevel(string id)
        {
            LevelWorld world = Index.WorldOf(id);
            if (world == null) return false;

            var levels = new List<string>(world.levels);
            levels.Remove(id);
            world.levels = levels.ToArray();

            IndexDirty = true;
            return true;
        }

        /// <summary>
        /// Moves a level one slot within its own world. Does not carry it into the next
        /// world: reordering and re-homing are different intentions.
        /// </summary>
        public bool MoveLevel(string id, int direction)
        {
            LevelWorld world = Index.WorldOf(id);
            if (world == null || direction == 0) return false;

            int from = Index.SlotOf(id);
            int to = from + (direction < 0 ? -1 : 1);

            if (to < 0 || to >= world.levels.Length) return false;

            world.levels[from] = world.levels[to];
            world.levels[to] = id;

            IndexDirty = true;
            return true;
        }

        LevelWorld FindWorld(string worldId)
        {
            if (Index.worlds == null) return null;

            foreach (LevelWorld world in Index.worlds)
                if (world != null && world.id == worldId) return world;

            return null;
        }

        /// <summary>
        /// Entity types offered by the palette.
        ///
        /// The first three are simulated (GDD 9.2). The rest are placeable and round-trip
        /// through the file, but have no behaviour yet — the props are GDD 10 and the cat is
        /// a behaviour tree of its own. EntityFactory decides what actually runs; this list
        /// only decides what can be placed.
        /// </summary>
        public static readonly string[] EntityTypes =
        {
            "saw", "lamp", "fan", "cat", "prop_leafblower", "prop_trampoline", "prop_tape",
        };

        public void Load(LevelData level)
        {
            Level = level ?? new LevelData();
            Grid.LoadRows(Level.tiles ?? new string[0]);
            Undo.Clear();
            SelectedEntityId = null;
            ExitSelected = false;
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

        // -------------------------------------------------------------------- zones

        public LevelZone FindZone(string id)
        {
            if (string.IsNullOrEmpty(id) || Level.zones == null) return null;

            foreach (LevelZone zone in Level.zones)
                if (zone != null && zone.id == id) return zone;

            return null;
        }

        /// <summary>
        /// The zone under a world position, smallest first.
        ///
        /// Smallest wins because zones nest and overlap by design (GDD 6.2 — overlapping air
        /// zones sum). Without this a small vent drawn inside a room-wide thermal would be
        /// permanently unselectable, which is the same reasoning that makes the select tool
        /// test entities before the exit.
        /// </summary>
        public LevelZone ZoneAt(Vector2 world)
        {
            if (Level.zones == null) return null;

            LevelZone best = null;
            float bestArea = float.MaxValue;

            foreach (LevelZone zone in Level.zones)
            {
                if (zone?.rect == null || zone.rect.Length < 4) continue;

                Rect rect = ZoneGeometry.RectOf(zone.rect);
                if (!rect.Contains(world)) continue;

                float area = rect.width * rect.height;
                if (area >= bestArea) continue;

                bestArea = area;
                best = zone;
            }

            return best;
        }

        public LevelZone AddZone(Rect rect, string type = "air")
        {
            var zone = new LevelZone
            {
                id = NextZoneId(),
                type = type,
                rect = ZoneGeometry.ToArray(rect),
                @params = new ZoneParams(),
                start_state = "on",
            };

            var zones = new List<LevelZone>(Level.zones ?? new LevelZone[0]) { zone };
            Level.zones = zones.ToArray();

            SelectedZoneId = zone.id;
            Dirty = true;
            return zone;
        }

        /// <summary>
        /// One setter for both moving and resizing: the geometry has already decided which
        /// of the two a given drag was, and two methods would be the same body twice.
        /// </summary>
        public void SetZoneRect(string id, Rect rect)
        {
            LevelZone zone = FindZone(id);
            if (zone == null) return;

            zone.rect = ZoneGeometry.ToArray(rect);
            Dirty = true;
        }

        public bool DeleteZone(string id)
        {
            LevelZone zone = FindZone(id);
            if (zone == null) return false;

            var zones = new List<LevelZone>(Level.zones);
            zones.Remove(zone);
            Level.zones = zones.ToArray();

            if (SelectedZoneId == id) SelectedZoneId = null;
            Dirty = true;
            return true;
        }

        /// <summary>Lowest unused zNN id, matching how entity ids are allocated.</summary>
        string NextZoneId()
        {
            for (int i = 1; i < 1000; i++)
            {
                string candidate = "z" + i.ToString("00");
                if (FindZone(candidate) == null) return candidate;
            }

            return "z" + Level.zones.Length;
        }

        // -------------------------------------------------------------------- exit

        /// <summary>The gate as the sim sees it, so the editor and the game agree on it.</summary>
        public ExitGate Gate() => LevelLoader.ToGate(Level.exit);

        /// <summary>Whether a click lands on the gate.</summary>
        public bool ExitAt(Vector2 world)
        {
            ExitGate gate = Gate();
            if (!gate.Present) return false;

            Vector2 half = gate.Size * 0.5f;

            return Mathf.Abs(world.x - gate.Position.x) <= half.x &&
                   Mathf.Abs(world.y - gate.Position.y) <= half.y;
        }

        public void SelectExit()
        {
            ExitSelected = true;
            SelectedEntityId = null;
        }

        public void MoveExit(Vector2 world)
        {
            if (Level.exit == null) Level.exit = new LevelExit();

            Vector2 snapped = GridCursor.SnapHalfTile(world);
            Level.exit.pos = new[] { snapped.x, snapped.y };
            Dirty = true;
        }

        public void SetExitShape(string shape)
        {
            if (Level.exit == null) Level.exit = new LevelExit();
            if (Level.exit.shape == shape) return;

            Level.exit.shape = shape;
            Dirty = true;
        }

        public void SetExitDiameter(float diameter)
        {
            if (Level.exit == null) Level.exit = new LevelExit();
            if (Mathf.Approximately(Level.exit.diameter, diameter)) return;

            Level.exit.diameter = Mathf.Max(diameter, 0f);
            Dirty = true;
        }

        /// <summary>
        /// The band the level asks the player to arrive at (schema: required_band).
        ///
        /// Deliberately NOT derived from the diameter. GDD 7.4's ceiling ring demands Full or
        /// Critical because of where it is, not how wide it is, and no aperture can express
        /// that — so this is the author's statement of intent and the editor never overwrites
        /// it. The validator only objects when the stated band physically cannot fit.
        /// </summary>
        public void SetRequiredBand(string band)
        {
            if (Level.exit == null) Level.exit = new LevelExit();
            if (Level.exit.required_band == band) return;

            Level.exit.required_band = band;
            Dirty = true;
        }

        // -------------------------------------------------------------- properties

        /// <summary>
        /// Retypes an entity in place, keeping its id, position and path.
        ///
        /// Params are deliberately NOT cleared: a saw's speed is a fan's speed, and losing a
        /// tuned number to a mistyped click is worse than carrying a stale one that the
        /// inspector no longer shows.
        /// </summary>
        public void SetEntityType(string id, string type)
        {
            LevelEntity entity = FindEntity(id);
            if (entity == null || entity.type == type) return;

            entity.type = type;
            Dirty = true;
        }

        public void SetParam(string id, EntityParamId param, float value)
        {
            LevelEntity entity = FindEntity(id);
            if (entity == null) return;

            entity.@params = entity.@params ?? new EntityParams();
            if (Mathf.Approximately(EntityFields.Get(entity.@params, param), value)) return;

            EntityFields.Set(entity.@params, param, value);
            Dirty = true;
        }

        public float GetParam(string id, EntityParamId param)
        {
            LevelEntity entity = FindEntity(id);
            return entity == null ? 0f : EntityFields.Get(entity.@params, param);
        }

        public void SetPathMode(string id, string mode)
        {
            LevelEntity entity = FindEntity(id);
            if (entity == null || entity.path_mode == mode) return;

            entity.path_mode = mode;
            Dirty = true;
        }

        public void SetStartState(string id, string state)
        {
            LevelEntity entity = FindEntity(id);
            if (entity == null || entity.start_state == state) return;

            entity.start_state = state;
            Dirty = true;
        }

        public void SetPhase(string id, float phase)
        {
            LevelEntity entity = FindEntity(id);
            if (entity == null) return;

            // Wrapped rather than clamped: phase is a fraction of a cycle, so 1.2 and 0.2 are
            // the same trap and an author typing either means the same thing.
            float wrapped = phase - Mathf.Floor(phase);
            if (Mathf.Approximately(entity.phase, wrapped)) return;

            entity.phase = wrapped;
            Dirty = true;
        }

        // --------------------------------------------------------------- waypoints

        /// <summary>
        /// Waypoints of an entity, as points. The entity's own position is waypoint 0 and is
        /// not stored in the path — the same convention EntityPath.FromFlat uses, so what the
        /// editor draws and what the sim walks can never disagree.
        /// </summary>
        public Vector2[] Waypoints(string id)
        {
            LevelEntity entity = FindEntity(id);
            if (entity?.pos == null || entity.pos.Length < 2) return new Vector2[0];

            return EntityPath.FromFlat(new Vector2(entity.pos[0], entity.pos[1]), entity.path);
        }

        public int WaypointCount(string id)
        {
            LevelEntity entity = FindEntity(id);
            return entity?.path == null ? 0 : entity.path.Length / 2;
        }

        public void AddWaypoint(string id, Vector2 world)
        {
            LevelEntity entity = FindEntity(id);
            if (entity == null) return;

            Vector2 snapped = GridCursor.SnapHalfTile(world);
            var path = new List<float>(entity.path ?? new float[0]) { snapped.x, snapped.y };

            entity.path = path.ToArray();
            Dirty = true;
        }

        /// <summary>
        /// Moves waypoint <paramref name="index"/>, where 0 is the entity itself. Dragging
        /// waypoint 0 moves the entity, which is what an author expects from the handle
        /// sitting on top of it.
        /// </summary>
        public void MoveWaypoint(string id, int index, Vector2 world)
        {
            LevelEntity entity = FindEntity(id);
            if (entity == null) return;

            if (index <= 0) { MoveEntity(id, world); return; }

            int slot = (index - 1) * 2;
            if (entity.path == null || slot + 1 >= entity.path.Length) return;

            Vector2 snapped = GridCursor.SnapHalfTile(world);
            entity.path[slot] = snapped.x;
            entity.path[slot + 1] = snapped.y;
            Dirty = true;
        }

        /// <summary>Deletes a waypoint. Waypoint 0 is the entity and cannot be deleted.</summary>
        public bool DeleteWaypoint(string id, int index)
        {
            LevelEntity entity = FindEntity(id);
            if (entity?.path == null || index <= 0) return false;

            int slot = (index - 1) * 2;
            if (slot + 1 >= entity.path.Length) return false;

            var path = new List<float>(entity.path);
            path.RemoveRange(slot, 2);

            entity.path = path.ToArray();
            Dirty = true;
            return true;
        }

        /// <summary>Index of the waypoint under a click, or -1. Waypoint 0 included.</summary>
        public int WaypointAt(string id, Vector2 world, float radius = 0.4f)
        {
            Vector2[] points = Waypoints(id);

            int best = -1;
            float bestDistance = radius * radius;

            for (int i = 0; i < points.Length; i++)
            {
                float distance = (points[i] - world).sqrMagnitude;
                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = i;
            }

            return best;
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
