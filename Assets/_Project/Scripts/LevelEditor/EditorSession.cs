using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Level;
using KeepyUppy.Sim.Panels;
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
        /// The selected trigger. Mutually exclusive with the entity, zone and exit selections
        /// for the same reason they are with each other: Delete tests each independently, so
        /// two live selections means one keypress destroying two objects.
        /// </summary>
        public string SelectedTriggerId;

        /// <summary>
        /// The selected panel. Mutually exclusive with the entity, zone, trigger and exit
        /// selections: Delete tests each independently, so two live selections means one
        /// keypress destroying two objects.
        /// </summary>
        public string SelectedPanelId;

        /// <summary>
        /// The exit is selected instead of an entity. Kept as a separate flag rather than a
        /// magic entity id: the gate is not an entity, and pretending otherwise would leak
        /// into the entity list, the id allocator and every foreach over it.
        /// </summary>
        public bool ExitSelected;

        /// <summary>
        /// Which spawn the Select tool is holding. The spawns ride that tool rather than
        /// take one of their own: two points do not earn a mode, and picking, half-tile snap,
        /// nudge and undo are all already built there.
        /// </summary>
        public SpawnMarker SelectedSpawn;

        /// <summary>
        /// Drops every selection. The kinds are mutually exclusive because more than the
        /// inspector reads them - the Delete key tests each independently, so two live
        /// selections is one keypress destroying two objects. Every setter clears through
        /// here rather than listing its five siblings, which is what stops the sixth kind
        /// from being the one somebody forgets.
        /// </summary>
        void ClearSelection()
        {
            SelectedEntityId = null;
            SelectedZoneId = null;
            SelectedTriggerId = null;
            SelectedPanelId = null;
            SelectedSpawn = SpawnMarker.None;
            ExitSelected = false;
        }

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
        /// The first four are simulated (GDD 9.2). The rest are placeable and round-trip
        /// through the file, but have no behaviour yet — the props are GDD 10 and the cat is
        /// a behaviour tree of its own. EntityFactory decides what actually runs; this list
        /// only decides what can be placed.
        /// </summary>
        public static readonly string[] EntityTypes =
        {
            "saw", "lamp", "fan", "spike_bar", "cat", "prop_leafblower", "prop_trampoline", "prop_tape",
        };

        public void Load(LevelData level)
        {
            Level = level ?? new LevelData();
            Grid.LoadRows(Level.tiles ?? new string[0]);
            Undo.Clear();
            ClearSelection();
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
            // Same for the zone selection: an undo that removes the selected zone must not
            // leave the id pointing at something that is gone, or the next Delete keypress
            // pushes an undo entry for a deletion that cannot happen.
            if (FindZone(SelectedZoneId) == null) SelectedZoneId = null;
            if (FindTrigger(SelectedTriggerId) == null) SelectedTriggerId = null;
            if (FindPanel(SelectedPanelId) == null) SelectedPanelId = null;
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

            SelectEntity(entity.id);
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

            RemoveLinksReferencing(id);

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

            SelectZone(zone.id);
            Dirty = true;
            return zone;
        }

        /// <summary>
        /// Selects a zone, clearing the entity, trigger and exit selections.
        ///
        /// The four are mutually exclusive because more than the inspector reads them: the
        /// Delete key tests each independently, so two live selections means one keypress
        /// destroying two objects.
        /// </summary>
        public void SelectZone(string id)
        {
            ClearSelection();
            SelectedZoneId = id;
        }

        /// <summary>
        /// Selects an entity, clearing the zone, trigger and exit selections.
        ///
        /// The four are mutually exclusive because more than the inspector reads them: the
        /// Delete key tests each independently, so two live selections means one keypress
        /// destroying two objects.
        /// </summary>
        public void SelectEntity(string id)
        {
            ClearSelection();
            SelectedEntityId = id;
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

        public void SetZoneType(string id, string type)
        {
            LevelZone zone = FindZone(id);
            if (zone == null || zone.type == type) return;

            zone.type = type;
            Dirty = true;
        }

        public void SetZoneParams(string id, float vx, float vy, float rate)
        {
            LevelZone zone = FindZone(id);
            if (zone == null) return;

            if (zone.@params == null) zone.@params = new ZoneParams();

            zone.@params.vx = vx;
            zone.@params.vy = vy;
            zone.@params.rate = rate;
            Dirty = true;
        }

        /// <summary>The schema stores this as the string "on" / "off" (level-editor-spec 3).</summary>
        public void SetZoneEnabled(string id, bool enabled)
        {
            LevelZone zone = FindZone(id);
            if (zone == null) return;

            string state = enabled ? "on" : "off";
            if (zone.start_state == state) return;

            zone.start_state = state;
            Dirty = true;
        }

        public bool DeleteZone(string id)
        {
            LevelZone zone = FindZone(id);
            if (zone == null) return false;

            var zones = new List<LevelZone>(Level.zones);
            zones.Remove(zone);
            Level.zones = zones.ToArray();

            RemoveLinksReferencing(id);

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

        // ----------------------------------------------------------------- triggers

        public LevelTrigger FindTrigger(string id)
        {
            if (string.IsNullOrEmpty(id) || Level.triggers == null) return null;

            foreach (LevelTrigger trigger in Level.triggers)
                if (trigger != null && trigger.id == id) return trigger;

            return null;
        }

        /// <summary>
        /// The trigger under a world position, smallest first — the same rule as ZoneAt, and
        /// for the same reason: a small trigger drawn inside a room-wide one must stay
        /// selectable.
        /// </summary>
        public LevelTrigger TriggerAt(Vector2 world)
        {
            if (Level.triggers == null) return null;

            LevelTrigger best = null;
            float bestArea = float.MaxValue;

            foreach (LevelTrigger trigger in Level.triggers)
            {
                if (trigger?.rect == null || trigger.rect.Length < 4) continue;

                Rect rect = ZoneGeometry.RectOf(trigger.rect);
                if (!rect.Contains(world)) continue;

                float area = rect.width * rect.height;
                if (area >= bestArea) continue;

                bestArea = area;
                best = trigger;
            }

            return best;
        }

        /// <summary>
        /// A new trigger. Defaults to an invisible once:true player region — GDD 9.3's
        /// workhorse, and the thing an author reaches for nine times in ten.
        /// </summary>
        public LevelTrigger AddTrigger(Rect rect, string type = "player_region")
        {
            var trigger = new LevelTrigger
            {
                id = NextTriggerId(),
                type = type,
                rect = ZoneGeometry.ToArray(ZoneGeometry.Snap(rect)),
                once = true,
                visible = false,
            };

            var triggers = new List<LevelTrigger>(Level.triggers ?? new LevelTrigger[0]) { trigger };
            Level.triggers = triggers.ToArray();

            SelectTrigger(trigger.id);
            Dirty = true;
            return trigger;
        }

        /// <summary>
        /// Selects a trigger, clearing the entity, zone and exit selections.
        ///
        /// The four are mutually exclusive because more than the inspector reads them: the
        /// Delete key tests each independently, so two live selections means one keypress
        /// destroying two objects.
        /// </summary>
        public void SelectTrigger(string id)
        {
            ClearSelection();
            SelectedTriggerId = id;
        }

        public void SetTriggerRect(string id, Rect rect)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null) return;

            trigger.rect = ZoneGeometry.ToArray(ZoneGeometry.Snap(rect));
            Dirty = true;
        }

        public void SetTriggerType(string id, string type)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null || trigger.type == type) return;

            trigger.type = type;
            Dirty = true;
        }

        public void SetTriggerOnce(string id, bool once)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null || trigger.once == once) return;

            trigger.once = once;
            Dirty = true;
        }

        public void SetTriggerVisible(string id, bool visible)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null || trigger.visible == visible) return;

            trigger.visible = visible;
            Dirty = true;
        }

        public void SetTriggerSeconds(string id, float seconds)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null || Mathf.Approximately(trigger.seconds, seconds)) return;

            trigger.seconds = seconds;
            Dirty = true;
        }

        /// <summary>
        /// Deletes a trigger and every link that fired from it. An orphaned link is a
        /// validator error, and leaving an author to clean up after their own delete is how a
        /// level file rots.
        /// </summary>
        public bool DeleteTrigger(string id)
        {
            LevelTrigger trigger = FindTrigger(id);
            if (trigger == null) return false;

            var triggers = new List<LevelTrigger>(Level.triggers);
            triggers.Remove(trigger);
            Level.triggers = triggers.ToArray();

            var links = new List<LevelLink>(Level.links ?? new LevelLink[0]);
            links.RemoveAll(link => link != null && link.from == id);
            Level.links = links.ToArray();

            // A trigger can also be another link's TARGET (TargetCentre resolves triggers
            // just like entities and zones), so the source-side removal above is not enough.
            RemoveLinksReferencing(id);

            if (SelectedTriggerId == id) SelectedTriggerId = null;
            Dirty = true;
            return true;
        }

        /// <summary>Lowest unused tNN id, matching how entity and zone ids are allocated.</summary>
        string NextTriggerId()
        {
            for (int i = 1; i < 1000; i++)
            {
                string candidate = "t" + i.ToString("00");
                if (FindTrigger(candidate) == null) return candidate;
            }

            return "t" + (Level.triggers?.Length ?? 0);
        }

        // ------------------------------------------------------------------ panels

        public LevelPanel FindPanel(string id)
        {
            if (string.IsNullOrEmpty(id) || Level.panels == null) return null;

            foreach (LevelPanel panel in Level.panels)
                if (panel != null && panel.id == id) return panel;

            return null;
        }

        /// <summary>Smallest first, as ZoneAt and TriggerAt are — panels nest by design.</summary>
        public LevelPanel PanelAt(Vector2 world)
        {
            if (Level.panels == null) return null;

            LevelPanel best = null;
            float bestArea = float.MaxValue;

            foreach (LevelPanel panel in Level.panels)
            {
                if (panel?.rect == null || panel.rect.Length < 4) continue;

                Rect rect = ZoneGeometry.RectOf(panel.rect);
                if (!rect.Contains(world)) continue;

                float area = rect.width * rect.height;
                if (area >= bestArea) continue;

                bestArea = area;
                best = panel;
            }

            return best;
        }

        public LevelPanel AddPanel(Rect rect)
        {
            var panel = new LevelPanel
            {
                id = NextPanelId(),
                rect = ZoneGeometry.ToArray(ZoneGeometry.Snap(rect)),
                start_state = "on",
            };

            var panels = new List<LevelPanel>(Level.panels ?? new LevelPanel[0]) { panel };
            Level.panels = panels.ToArray();

            SelectPanel(panel.id);
            Dirty = true;
            return panel;
        }

        /// <summary>
        /// Selects a panel, clearing the entity, zone, trigger and exit selections.
        ///
        /// The five are mutually exclusive because more than the inspector reads them: the
        /// Delete key tests each independently, so two live selections means one keypress
        /// destroying two objects.
        /// </summary>
        public void SelectPanel(string id)
        {
            ClearSelection();
            SelectedPanelId = id;
        }

        public void SetPanelRect(string id, Rect rect)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return;

            panel.rect = ZoneGeometry.ToArray(ZoneGeometry.Snap(rect));
            Dirty = true;
        }

        public void SetPanelEnabled(string id, bool enabled)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return;

            string state = enabled ? "on" : "off";
            if (panel.start_state == state) return;

            panel.start_state = state;
            Dirty = true;
        }

        /// <summary>
        /// Which way the panel's tiles leave. A value the view cannot draw is refused rather
        /// than written: the file is the source of truth for the shipping game too, and a
        /// silently-ignored string there is a level that plays differently from how it reads.
        /// </summary>
        public void SetPanelExitDir(string id, string direction)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return;

            if (!PanelDirections.IsKnown(direction)) return;
            if (panel.exit_dir == direction) return;

            panel.exit_dir = direction;
            Dirty = true;
        }

        public bool DeletePanel(string id)
        {
            LevelPanel panel = FindPanel(id);
            if (panel == null) return false;

            var panels = new List<LevelPanel>(Level.panels);
            panels.Remove(panel);
            Level.panels = panels.ToArray();

            RemoveLinksReferencing(id);

            if (SelectedPanelId == id) SelectedPanelId = null;
            Dirty = true;
            return true;
        }

        /// <summary>Lowest unused pNN id, matching how entity, zone and trigger ids are allocated.</summary>
        string NextPanelId()
        {
            for (int i = 1; i < 1000; i++)
            {
                string candidate = "p" + i.ToString("00");
                if (FindPanel(candidate) == null) return candidate;
            }

            return "p" + (Level.panels?.Length ?? 0);
        }

        // -------------------------------------------------------------------- links

        /// <summary>The link fired by a trigger, or null. One link per source, many targets.</summary>
        public LevelLink LinksFrom(string from)
        {
            if (string.IsNullOrEmpty(from) || Level.links == null) return null;

            foreach (LevelLink link in Level.links)
                if (link != null && link.from == from) return link;

            return null;
        }

        /// <summary>
        /// Wires a trigger to a target. A second target on the same trigger is appended to the
        /// existing link rather than making another one: two links from one source with the
        /// same action would be a distinction the author cannot see on screen.
        /// </summary>
        public LevelLink AddLink(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return null;
            if (FindTrigger(from) == null) return null;

            LevelLink link = LinksFrom(from);

            if (link == null)
            {
                link = new LevelLink { from = from, to = new[] { to }, action = "toggle" };
                var links = new List<LevelLink>(Level.links ?? new LevelLink[0]) { link };
                Level.links = links.ToArray();
                Dirty = true;
                return link;
            }

            var targets = new List<string>(link.to ?? new string[0]);
            if (targets.Contains(to)) return link;

            targets.Add(to);
            link.to = targets.ToArray();
            Dirty = true;
            return link;
        }

        public void SetLinkAction(string from, string action)
        {
            LevelLink link = LinksFrom(from);
            if (link == null || link.action == action) return;

            link.action = action;
            Dirty = true;
        }

        public void SetLinkDelay(string from, float delay)
        {
            LevelLink link = LinksFrom(from);
            if (link == null || Mathf.Approximately(link.delay, delay)) return;

            link.delay = Mathf.Max(delay, 0f);
            Dirty = true;
        }

        /// <summary>Unwires one target. The link goes when its last target does.</summary>
        public bool RemoveLink(string from, string to)
        {
            LevelLink link = LinksFrom(from);
            if (link == null) return false;

            var targets = new List<string>(link.to ?? new string[0]);
            if (!targets.Remove(to)) return false;

            if (targets.Count > 0)
            {
                link.to = targets.ToArray();
                Dirty = true;
                return true;
            }

            var links = new List<LevelLink>(Level.links);
            links.Remove(link);
            Level.links = links.ToArray();
            Dirty = true;
            return true;
        }

        /// <summary>
        /// Every link target a rubber band touches, in the same precedence order a single
        /// click resolves — entities, then zones, then triggers, then panels. Wiring a band
        /// is exactly the clicks the author would otherwise make, so the two must agree.
        ///
        /// An entity is a point and counts when the band contains it; everything else is a
        /// rect and counts when the band overlaps it at all, because dragging across a row
        /// clips its ends far more often than it encloses them.
        /// </summary>
        public string[] TargetIdsIn(Rect band)
        {
            var ids = new List<string>();

            if (Level.entities != null)
                foreach (LevelEntity entity in Level.entities)
                {
                    if (entity?.pos == null || entity.pos.Length < 2) continue;
                    if (band.Contains(new Vector2(entity.pos[0], entity.pos[1]))) ids.Add(entity.id);
                }

            if (Level.zones != null)
                foreach (LevelZone zone in Level.zones)
                    if (Touches(band, zone?.rect)) ids.Add(zone.id);

            if (Level.triggers != null)
                foreach (LevelTrigger trigger in Level.triggers)
                    if (Touches(band, trigger?.rect)) ids.Add(trigger.id);

            if (Level.panels != null)
                foreach (LevelPanel panel in Level.panels)
                    if (Touches(band, panel?.rect)) ids.Add(panel.id);

            return ids.ToArray();
        }

        static bool Touches(Rect band, float[] rect)
            => rect != null && rect.Length >= 4 && band.Overlaps(ZoneGeometry.RectOf(rect));

        /// <summary>
        /// Where an id sits in the room, for drawing an arrow to it. Entities, zones and
        /// triggers can all be link targets, so all three answer here.
        /// </summary>
        public bool TargetCentre(string id, out Vector2 centre)
        {
            centre = Vector2.zero;

            LevelEntity entity = FindEntity(id);
            if (entity?.pos != null && entity.pos.Length >= 2)
            {
                centre = new Vector2(entity.pos[0], entity.pos[1]);
                return true;
            }

            LevelZone zone = FindZone(id);
            if (zone != null)
            {
                centre = ZoneGeometry.RectOf(zone.rect).center;
                return true;
            }

            LevelTrigger trigger = FindTrigger(id);
            if (trigger != null)
            {
                centre = ZoneGeometry.RectOf(trigger.rect).center;
                return true;
            }

            LevelPanel panel = FindPanel(id);
            if (panel != null)
            {
                centre = ZoneGeometry.RectOf(panel.rect).center;
                return true;
            }

            return false;
        }

        /// <summary>Hit radius for clicking an arrow, in metres. About 6 px at 20 px/metre.</summary>
        public const float LinkHitRadius = 0.3f;

        /// <summary>
        /// The link arrow under a world position, in file order. Distance to the segment
        /// between the two centres — the arrow the controller draws is that segment, so the
        /// click target and the drawing cannot drift apart.
        /// </summary>
        public bool LinkAt(Vector2 world, out string from, out string to)
        {
            from = null;
            to = null;

            if (Level.links == null) return false;

            foreach (LevelLink link in Level.links)
            {
                if (link?.to == null) continue;
                if (!TargetCentre(link.from, out Vector2 source)) continue;

                foreach (string target in link.to)
                {
                    if (!TargetCentre(target, out Vector2 centre)) continue;
                    if (DistanceToSegment(world, source, centre) > LinkHitRadius) continue;

                    from = link.from;
                    to = target;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Strips a deleted id from every link's target list, dropping any link left with no
        /// targets. Entities, zones and triggers can all be link targets (TargetCentre
        /// resolves all three), so every delete path needs this or the delete leaves a
        /// dangling id behind for LevelValidator to catch instead.
        /// </summary>
        void RemoveLinksReferencing(string id)
        {
            if (Level.links == null) return;

            var links = new List<LevelLink>();
            bool changed = false;

            foreach (LevelLink link in Level.links)
            {
                if (link?.to == null || System.Array.IndexOf(link.to, id) < 0)
                {
                    links.Add(link);
                    continue;
                }

                var targets = new List<string>(link.to);
                targets.Remove(id);
                changed = true;

                if (targets.Count == 0) continue; // an empty link is not a link

                link.to = targets.ToArray();
                links.Add(link);
            }

            if (!changed) return;

            Level.links = links.ToArray();
            Dirty = true;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq <= 0f) return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------ spawns

        /// <summary>Where a marker currently sits, falling back exactly as the loader does.</summary>
        public Vector2 SpawnPos(SpawnMarker marker)
        {
            float[] pair = marker == SpawnMarker.Balloon ? Level.spawn?.balloon : Level.spawn?.player;
            Vector2 fallback = marker == SpawnMarker.Balloon
                ? LevelLoader.DefaultBalloonSpawn
                : LevelLoader.DefaultPlayerSpawn;

            return pair != null && pair.Length >= 2 ? new Vector2(pair[0], pair[1]) : fallback;
        }

        /// <summary>
        /// Which spawn a click lands on, nearest first. A level with no spawn block still
        /// answers: the loader will put bodies at its own defaults, so those are where the
        /// author has to be able to grab them.
        /// </summary>
        public SpawnMarker SpawnAt(Vector2 world, float radius = 0.5f)
        {
            var best = SpawnMarker.None;
            float bestDistance = radius;

            foreach (SpawnMarker marker in new[] { SpawnMarker.Player, SpawnMarker.Balloon })
            {
                float distance = Vector2.Distance(world, SpawnPos(marker));
                if (distance > bestDistance) continue;

                best = marker;
                bestDistance = distance;
            }

            return best;
        }

        public void SelectSpawn(SpawnMarker marker)
        {
            ClearSelection();
            SelectedSpawn = marker;
        }

        /// <summary>
        /// Half-tile snap, like every other point the Select tool moves.
        ///
        /// Writing one spawn materialises the block with the other at the loader's default,
        /// so an author who drags the balloon somewhere never silently pins the player to a
        /// position they were never shown.
        /// </summary>
        public void MoveSpawn(SpawnMarker marker, Vector2 world)
        {
            if (marker == SpawnMarker.None) return;

            Vector2 other = SpawnPos(marker == SpawnMarker.Balloon
                ? SpawnMarker.Player
                : SpawnMarker.Balloon);

            if (Level.spawn == null) Level.spawn = new LevelSpawn();

            Vector2 snapped = GridCursor.SnapHalfTile(world);

            if (marker == SpawnMarker.Balloon)
            {
                Level.spawn.balloon = new[] { snapped.x, snapped.y };
                Level.spawn.player = new[] { other.x, other.y };
            }
            else
            {
                Level.spawn.player = new[] { snapped.x, snapped.y };
                Level.spawn.balloon = new[] { other.x, other.y };
            }

            Dirty = true;
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
            ClearSelection();
            ExitSelected = true;
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
