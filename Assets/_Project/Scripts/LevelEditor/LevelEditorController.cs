#if LEVEL_EDITOR
using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Level;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// The in-game level editor, phase 1 (level-editor-spec 10): tile painting, entity
    /// placement, save/load, Tab play toggle.
    ///
    /// **Runtime code that is merely dev-only.** It lives in LevelEditor/, never a folder
    /// called Editor — Unity strips those from every build, and this must survive into
    /// development builds or it cannot be used for playtesting. Gated by LEVEL_EDITOR, which
    /// the release build strips.
    ///
    /// IMGUI throughout. Per spec 2: "Any UI polish" is explicitly not built. Ugly is correct.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelEditorController : MonoBehaviour
    {
        public ILevelEditorHost Host;
        public string LevelId = "w1_01";

        public EditorSession Session { get; private set; } = new EditorSession();

        // Drag state
        bool _painting;
        bool _erasing;
        bool _dragging;
        Vector2Int _dragStart;
        Vector2 _entityDragOffset;
        int _draggingWaypoint = -1;
        string _lastSelected;

        /// <summary>Which corner handle the current drag has hold of, if any.</summary>
        ZoneCorner _zoneCorner;

        /// <summary>Cursor-to-bottom-left offset, so a moved zone does not jump to the cursor.</summary>
        Vector2 _zoneDragOffset;

        /// <summary>Drag-in-progress creates a zone rather than moving one.</summary>
        bool _zoneCreating;

        /// <summary>
        /// N has armed the next drag to create, even inside an existing zone. One-shot:
        /// overlapping zones are a supported design (GDD 6.2 — they sum), so there has to be
        /// a way to draw one on top of another, but it must not be the default or every
        /// attempt to nudge a zone would spawn a new one.
        /// </summary>
        bool _zoneForceNew;

        /// <summary>
        /// The tile the current resize pivots around, captured once at mouse-down.
        ///
        /// Recomputing it from the live rect each frame moves the anchor the moment a drag
        /// crosses it: the rect flips correctly on the first frame, then re-anchors to the
        /// corner under the cursor and collapses into a band trailing it.
        /// </summary>
        Vector2Int _zoneAnchor;

        /// <summary>Which corner handle the current trigger drag has hold of, if any.</summary>
        ZoneCorner _triggerCorner;

        Vector2Int _triggerAnchor;

        /// <summary>Cursor-to-bottom-left offset, so a moved trigger does not jump to the cursor.</summary>
        Vector2 _triggerDragOffset;

        /// <summary>Drag-in-progress creates a trigger rather than moving one.</summary>
        bool _triggerCreating;

        // Overlays
        bool _showPanel = true;
        bool _showGrid = true;
        bool _showHitboxes;
        bool _showSafeArea;
        bool _showBrowser;

        /// <summary>
        /// Draws the inspector against the left edge instead of the right.
        ///
        /// The room fills the screen, so the panel always covers part of it, and PointerOverUi
        /// deliberately swallows clicks underneath it - otherwise a click on the panel would
        /// also edit whatever it is sitting on top of. That makes anything behind the panel
        /// unreachable, so the panel has to be movable. Flipping sides is the whole of it:
        /// park it over the half of the room you are not working on.
        /// </summary>
        bool _inspectorOnLeft;
        Rect _browserRect;
        Vector2 _browserScroll;
        string _newLevelId = "w1_02";
        string _newWorldId = "w1";

        string _status = "";
        List<string> _warnings = new List<string>();
        string _entityType = EditorSession.EntityTypes[0];

        Texture2D _pixel;

        /// <summary>
        /// In-progress text per inspector field, so a number being typed is not snapped back
        /// to the committed value between keystrokes. UI state only — never read by the sim.
        /// </summary>
        readonly Dictionary<string, string> _fieldText = new Dictionary<string, string>();

        /// <summary>
        /// Where the UI is this frame, so a click on a button does not also paint the tile
        /// behind it. Recomputed in OnGUI because the layout follows the screen size.
        /// </summary>
        Rect _paletteRect;
        Rect _toggleRect;
        Rect _inspectorRect;

        public EditorMode Mode => Session.Mode;

        /// <summary>Drops half-typed field text when the selection moves to another entity.</summary>
        void ForgetFieldText()
        {
            _fieldText.Clear();
            GUIUtility.keyboardControl = 0;
        }

        /// <summary>
        /// The editor now dies with its session (LevelSession is destroyed on quit/restart),
        /// so a destroy while still in Edit mode is a real path, not just a domain reload. If
        /// this controller is destroyed without ever calling EnterPlay - the only other place
        /// that un-suppresses gameplay - the static DevInputCapture.EditorModeActive flag
        /// SetGameplaySuppressed(true) set on the way into Edit mode stays set forever,
        /// silently suppressing every shell and gameplay key with no editor left to clear it.
        /// Goes through Host, same as everywhere else this flag is touched, rather than
        /// reaching for DevInputCapture directly - LevelEditor cannot reference Runtime (Runtime
        /// already references LevelEditor; a reference back would be circular). Unconditional:
        /// clearing a flag that is already false is harmless, and OnDisable runs on every
        /// teardown path (Destroy included), edit or play. Host can be null in the split second
        /// after Destroy(gameObject) if this fires post-teardown - tolerate that like every
        /// other Host access in this class.
        /// </summary>
        void OnDisable()
        {
            Host?.SetGameplaySuppressed(false);
        }

        public void Begin(LevelData level)
        {
            // Share the world's grid, so edits are visible the moment they are made rather
            // than at the next Tab.
            Session = new EditorSession(Host.World.Grid);
            Session.Load(level);

            // The manifest decides what is in the game; without it the browser can only offer
            // whatever files happen to be present, which is the state this replaces.
            Session.LoadIndex(Host.Levels.TryLoadIndex(out LevelIndex index) ? index : LevelIndex.Default());

            EnterEdit();
        }

        // ------------------------------------------------------------------ input

        void Update()
        {
            if (Host == null) return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // Tab toggles Edit and Play. Play starts from the current unsaved state, which is
            // the whole point — edit, Tab, feel it, Tab back.
            if (kb.tabKey.wasPressedThisFrame)
            {
                if (Session.Mode == EditorMode.Edit) EnterPlay();
                else EnterEdit();
                return;
            }

            if (Session.Mode == EditorMode.Play) return;

            string selection = Session.ExitSelected
                ? "@exit"
                : Session.SelectedZoneId ?? Session.SelectedEntityId;
            if (selection != _lastSelected)
            {
                _lastSelected = selection;
                ForgetFieldText();
            }

            HandleEditorKeys(kb);
            HandleMouse();
        }

        void HandleEditorKeys(Keyboard kb)
        {
            // A focused IMGUI text field owns the keyboard. Without this, typing a saw's
            // speed of 3 would also switch to the Select tool.
            if (GUIUtility.keyboardControl != 0) return;

            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;

            if (ctrl && kb.sKey.wasPressedThisFrame) { Save(); return; }
            if (ctrl && kb.zKey.wasPressedThisFrame) { _status = Undo() ? "undo" : "nothing to undo"; return; }
            if (ctrl && kb.yKey.wasPressedThisFrame) { _status = Redo() ? "redo" : "nothing to redo"; return; }

            if (kb.digit1Key.wasPressedThisFrame) Session.Tool = EditorTool.Paint;
            if (kb.digit2Key.wasPressedThisFrame) Session.Tool = EditorTool.RectFill;
            if (kb.digit3Key.wasPressedThisFrame) Session.Tool = EditorTool.Select;
            if (kb.digit4Key.wasPressedThisFrame) Session.Tool = EditorTool.Zone;
            if (kb.digit5Key.wasPressedThisFrame) Session.Tool = EditorTool.Path;
            if (kb.pKey.wasPressedThisFrame) Session.Tool = EditorTool.Place;
            if (kb.digit8Key.wasPressedThisFrame) Session.Tool = EditorTool.Trigger;

            if (Session.Tool != EditorTool.Zone) _zoneForceNew = false;
            if (Session.Tool != EditorTool.Trigger) { _triggerCorner = ZoneCorner.None; _triggerCreating = false; }

            // A toggle, not a one-way arm, so it can be cancelled without Escape - which is
            // the shell's Back (KeyboardShellInput) and reserved by CLAUDE.md's keybind list.
            if (kb.nKey.wasPressedThisFrame && Session.Tool == EditorTool.Zone)
            {
                _zoneForceNew = !_zoneForceNew;
                _status = _zoneForceNew ? "new zone: drag to place" : "cancelled";
            }

            if (kb.uKey.wasPressedThisFrame) _showPanel = !_showPanel;
            if (kb.gKey.wasPressedThisFrame) _showGrid = !_showGrid;
            if (kb.hKey.wasPressedThisFrame) _showHitboxes = !_showHitboxes;
            if (kb.tKey.wasPressedThisFrame) _showSafeArea = !_showSafeArea;
            if (kb.lKey.wasPressedThisFrame) _showBrowser = !_showBrowser;

            if (kb.iKey.wasPressedThisFrame)
            {
                _inspectorOnLeft = !_inspectorOnLeft;
                _status = _inspectorOnLeft ? "inspector left" : "inspector right";
            }

            if (kb.deleteKey.wasPressedThisFrame && Session.SelectedEntityId != null)
            {
                Session.BeginEdit();
                Session.DeleteEntity(Session.SelectedEntityId);
                SyncDynamic();
                _status = "deleted entity";
            }

            if (kb.deleteKey.wasPressedThisFrame && Session.Tool == EditorTool.Zone
                && Session.SelectedZoneId != null)
            {
                Session.BeginEdit();
                Session.DeleteZone(Session.SelectedZoneId);
                SyncDynamic();
                _status = "deleted zone";
            }

            if (kb.deleteKey.wasPressedThisFrame && Session.Tool == EditorTool.Trigger
                && Session.SelectedTriggerId != null)
            {
                Session.BeginEdit();
                Session.DeleteTrigger(Session.SelectedTriggerId);
                SyncDynamic();
                _status = "deleted trigger";
            }
        }

        void HandleMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 screen = mouse.position.ReadValue();

            // A click that lands on the palette belongs to the palette. Without this, picking
            // a tile also paints the tile behind the button you just pressed.
            if (PointerOverUi(screen))
            {
                _painting = false;
                _erasing = false;
                _dragging = false;
                _draggingWaypoint = -1;
                return;
            }

            Vector2 world = ScreenToWorld(screen);
            Vector2Int tile = GridCursor.ToTile(world);

            switch (Session.Tool)
            {
                case EditorTool.Paint: PaintTool(mouse, tile); break;
                case EditorTool.RectFill: RectTool(mouse, tile); break;
                case EditorTool.Select: SelectTool(mouse, world); break;
                case EditorTool.Zone: ZoneTool(mouse, world); break;
                case EditorTool.Path: PathTool(mouse, world); break;
                case EditorTool.Place: PlaceTool(mouse, world); break;
                case EditorTool.Trigger: TriggerTool(mouse, world); break;
            }
        }

        void PaintTool(Mouse mouse, Vector2Int tile)
        {
            // One undo entry per stroke, not per tile — otherwise a single drag fills the
            // whole 50-deep history.
            if (mouse.leftButton.wasPressedThisFrame) { Session.BeginEdit(); _painting = true; }
            if (mouse.rightButton.wasPressedThisFrame) { Session.BeginEdit(); _erasing = true; }

            if (mouse.leftButton.wasReleasedThisFrame) _painting = false;
            if (mouse.rightButton.wasReleasedThisFrame) _erasing = false;

            if (_painting) Session.Paint(tile, Session.SelectedTile);
            else if (_erasing) Session.Paint(tile, TileKind.Empty);
        }

        void RectTool(Mouse mouse, Vector2Int tile)
        {
            if (mouse.leftButton.wasPressedThisFrame) { _dragStart = tile; _dragging = true; }

            if (!_dragging || !mouse.leftButton.wasReleasedThisFrame) return;

            Session.BeginEdit();
            Session.FillRect(_dragStart, tile, Session.SelectedTile);
            _dragging = false;
            _status = "filled";
        }

        void SelectTool(Mouse mouse, Vector2 world)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                LevelEntity hit = Session.EntityAt(world);
                if (hit != null)
                {
                    Session.SelectEntity(hit.id);
                    _entityDragOffset = new Vector2(hit.pos[0], hit.pos[1]) - world;
                    Session.BeginEdit();
                    _dragging = true;
                }
                else if (Session.ExitAt(world))
                {
                    // Entities are tested first because the gate is large. Anything else and a
                    // hazard placed in the doorway would be unselectable.
                    Session.SelectExit();
                    _entityDragOffset = Session.Gate().Position - world;
                    Session.BeginEdit();
                    _dragging = true;
                }
                else
                {
                    Session.SelectEntity(null);
                }
            }

            if (mouse.leftButton.wasReleasedThisFrame) _dragging = false;

            if (!_dragging) return;

            if (Session.ExitSelected)
            {
                Session.MoveExit(world + _entityDragOffset);
                SyncExit();
                return;
            }

            if (Session.SelectedEntityId == null) return;

            Session.MoveEntity(Session.SelectedEntityId, world + _entityDragOffset);
            SyncDynamic();
        }

        /// <summary>
        /// Draw, move, resize and delete zones (level-editor-spec 4, tool 4).
        ///
        /// Mouse-down chooses between three drags: a handle of the selected zone resizes, the
        /// interior of a zone moves it, and empty space creates one. N overrides the middle
        /// case so a zone can be drawn on top of another.
        /// </summary>
        void ZoneTool(Mouse mouse, Vector2 world)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                LevelZone hit = Session.ZoneAt(world);
                if (hit != null)
                {
                    Session.BeginEdit();
                    Session.DeleteZone(hit.id);
                    SyncDynamic();
                    _status = "deleted zone";
                }

                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                LevelZone selected = Session.FindZone(Session.SelectedZoneId);

                // Handles belong to the selected zone alone. Every zone showing handles at
                // once is unreadable in a room with several.
                _zoneCorner = selected?.rect != null
                    ? ZoneGeometry.HandleAt(ZoneGeometry.RectOf(selected.rect), world, ZoneHandleRadius)
                    : ZoneCorner.None;

                LevelZone hit = _zoneForceNew ? null : Session.ZoneAt(world);

                if (_zoneCorner != ZoneCorner.None)
                {
                    _zoneAnchor = ZoneGeometry.AnchorOf(ZoneGeometry.RectOf(selected.rect), _zoneCorner);
                    Session.BeginEdit();
                    _zoneCreating = false;
                    _dragging = true;
                }
                else if (hit != null)
                {
                    Session.SelectZone(hit.id);
                    Rect rect = ZoneGeometry.RectOf(hit.rect);
                    _zoneDragOffset = new Vector2(rect.xMin, rect.yMin) - world;
                    Session.BeginEdit();
                    _zoneCreating = false;
                    _dragging = true;
                }
                else
                {
                    _dragStart = GridCursor.ToTile(world);
                    _zoneCreating = true;
                    _dragging = true;
                }
            }

            if (!_dragging) return;

            if (_zoneCreating)
            {
                if (!mouse.leftButton.wasReleasedThisFrame) return;

                Session.BeginEdit();
                LevelZone created = Session.AddZone(ZoneGeometry.Normalise(_dragStart, GridCursor.ToTile(world)));
                SyncDynamic();

                _dragging = false;
                _zoneCreating = false;
                _zoneForceNew = false;
                _status = $"added {created.id}";
                return;
            }

            LevelZone dragged = Session.FindZone(Session.SelectedZoneId);
            if (dragged?.rect == null) { _dragging = false; return; }

            Rect current = ZoneGeometry.RectOf(dragged.rect);

            Rect updated = _zoneCorner != ZoneCorner.None
                ? ZoneGeometry.Normalise(_zoneAnchor, GridCursor.ToTile(world))
                : ZoneGeometry.Move(current, world + _zoneDragOffset);

            Session.SetZoneRect(dragged.id, updated);
            SyncDynamic();

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _status = _zoneCorner != ZoneCorner.None ? "resized zone" : "moved zone";
                _dragging = false;
                _zoneCorner = ZoneCorner.None;
            }
        }

        /// <summary>
        /// Trigger rects (GDD 9.3). Identical in shape to ZoneTool and deliberately so: both
        /// are tile rects on the same grid, so both route through ZoneGeometry and an author
        /// who knows one tool knows the other.
        /// </summary>
        void TriggerTool(Mouse mouse, Vector2 world)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                LevelTrigger hit = Session.TriggerAt(world);
                if (hit != null)
                {
                    Session.BeginEdit();
                    Session.DeleteTrigger(hit.id);
                    SyncDynamic();
                    _status = "deleted trigger";
                }

                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                LevelTrigger selected = Session.FindTrigger(Session.SelectedTriggerId);

                _triggerCorner = selected?.rect != null
                    ? ZoneGeometry.HandleAt(ZoneGeometry.RectOf(selected.rect), world, ZoneHandleRadius)
                    : ZoneCorner.None;

                LevelTrigger hit = Session.TriggerAt(world);

                if (_triggerCorner != ZoneCorner.None)
                {
                    _triggerAnchor = ZoneGeometry.AnchorOf(ZoneGeometry.RectOf(selected.rect), _triggerCorner);
                    Session.BeginEdit();
                    _triggerCreating = false;
                    _dragging = true;
                }
                else if (hit != null)
                {
                    Session.SelectTrigger(hit.id);
                    Rect rect = ZoneGeometry.RectOf(hit.rect);
                    _triggerDragOffset = new Vector2(rect.xMin, rect.yMin) - world;
                    Session.BeginEdit();
                    _triggerCreating = false;
                    _dragging = true;
                }
                else
                {
                    _dragStart = GridCursor.ToTile(world);
                    _triggerCreating = true;
                    _dragging = true;
                }
            }

            if (!_dragging) return;

            // A create drag resolves on release, so the author sees the rect they dragged
            // rather than one that grew out of the first tile.
            if (_triggerCreating)
            {
                if (!mouse.leftButton.wasReleasedThisFrame) return;

                Session.BeginEdit();
                LevelTrigger created =
                    Session.AddTrigger(ZoneGeometry.Normalise(_dragStart, GridCursor.ToTile(world)));
                SyncDynamic();

                _dragging = false;
                _triggerCreating = false;
                _status = $"added {created.id}";
                return;
            }

            LevelTrigger dragged = Session.FindTrigger(Session.SelectedTriggerId);
            if (dragged?.rect == null) { _dragging = false; return; }

            Rect current = ZoneGeometry.RectOf(dragged.rect);

            Rect updated = _triggerCorner != ZoneCorner.None
                ? ZoneGeometry.Normalise(_triggerAnchor, GridCursor.ToTile(world))
                : ZoneGeometry.Move(current, world + _triggerDragOffset);

            Session.SetTriggerRect(dragged.id, updated);
            SyncDynamic();

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _status = _triggerCorner != ZoneCorner.None ? "resized trigger" : "moved trigger";
                _dragging = false;
                _triggerCorner = ZoneCorner.None;
            }
        }

        /// <summary>
        /// Drops the palette entity where the cursor is. The old centre-of-room button is
        /// still there, but placing then dragging everything into position was busywork.
        /// </summary>
        void PlaceTool(Mouse mouse, Vector2 world)
        {
            if (!mouse.leftButton.wasPressedThisFrame) return;

            Session.BeginEdit();
            LevelEntity placed = Session.PlaceEntity(_entityType, world);
            SyncDynamic();
            _status = $"placed {placed.type} {placed.id}";
        }

        /// <summary>
        /// Waypoints of the selected entity: click empty space to append one, drag a handle to
        /// move it, right-click one to remove it.
        ///
        /// Handle 0 is the entity itself, so dragging it moves the whole trap and the path
        /// follows — which is what the handle sitting on top of it implies.
        /// </summary>
        void PathTool(Mouse mouse, Vector2 world)
        {
            string id = Session.SelectedEntityId;

            if (id == null)
            {
                // Nothing selected: the first click picks a target rather than doing nothing.
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    LevelEntity hit = Session.EntityAt(world);
                    Session.SelectEntity(hit?.id);
                    _status = hit == null ? "select an entity to give it a path" : $"editing {hit.id} path";
                }
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                int index = Session.WaypointAt(id, world);
                if (index > 0)
                {
                    Session.BeginEdit();
                    Session.DeleteWaypoint(id, index);
                    SyncDynamic();
                    _status = "removed waypoint";
                }
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _draggingWaypoint = Session.WaypointAt(id, world);
                Session.BeginEdit();

                if (_draggingWaypoint < 0)
                {
                    Session.AddWaypoint(id, world);
                    _draggingWaypoint = Session.WaypointCount(id);
                    _status = "added waypoint";
                }

                SyncDynamic();
            }

            if (mouse.leftButton.wasReleasedThisFrame) _draggingWaypoint = -1;

            if (_draggingWaypoint >= 0 && mouse.leftButton.isPressed)
            {
                Session.MoveWaypoint(id, _draggingWaypoint, world);
                SyncDynamic();
            }
        }

        /// <summary>
        /// Pushes entity and zone edits into the running world so they are visible at once.
        /// Geometry, spawns and the paused run are left alone — this is not a reload.
        /// </summary>
        void SyncDynamic() => Host.SyncDynamic(Session.Level);

        /// <summary>The gate lives on the world, not the entity list, so it syncs its own way.</summary>
        void SyncExit() => Host.SyncExit(Session.Level);

        /// <summary>Screen point (y up) against the GUI rects (y down).</summary>
        bool PointerOverUi(Vector2 screen)
        {
            var point = new Vector2(screen.x, Screen.height - screen.y);
            if (_toggleRect.Contains(point)) return true;
            if (_showBrowser && _browserRect.Contains(point)) return true;
            if (!_showPanel) return false;

            return _paletteRect.Contains(point) || _inspectorRect.Contains(point);
        }

        Vector2 ScreenToWorld(Vector2 screen)
        {
            Camera camera = Host.Camera;
            if (camera == null) return Vector2.zero;

            return camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -camera.transform.position.z));
        }

        // ------------------------------------------------------------------ modes

        void EnterPlay()
        {
            Session.Mode = EditorMode.Play;
            Host.SetGameplaySuppressed(false);
            Host.SetSimPaused(false);
            Host.ApplyLevel(Session.Flush());
            _status = "play";
        }

        void EnterEdit()
        {
            Session.Mode = EditorMode.Edit;
            Host.SetGameplaySuppressed(true);

            // Put the room back the way the author left it: play may have moved nothing, but
            // it is the edit-time level that is authoritative here.
            Host.ApplyLevel(Session.Flush());

            // Freeze the sim while authoring. Otherwise the balloon falls, dies and restarts
            // on a loop behind the tiles you are trying to place.
            Host.SetSimPaused(true);
            _status = "edit";
        }

        /// <summary>
        /// Undo restores whole-level snapshots, so the world's entities have to be rebuilt
        /// from the restored data — the shared grid takes care of tiles by itself.
        /// </summary>
        bool Undo()
        {
            if (!Session.UndoEdit()) return false;

            SyncDynamic();
            return true;
        }

        bool Redo()
        {
            if (!Session.RedoEdit()) return false;

            SyncDynamic();
            return true;
        }

        void Save()
        {
            LevelData level = Session.Flush();
            _warnings = LevelValidator.Validate(level);
            _warnings.AddRange(LevelIndexValidator.ValidateSlot(level, Session.Index));

            // Validation never blocks a save. A half-finished level must always be saveable.
            Host.Levels.Save(LevelId, level);
            Session.MarkSaved();

            _status = _warnings.Count == 0
                ? $"saved {LevelId}"
                : $"saved {LevelId} with {_warnings.Count} warning(s)";
        }

        /// <summary>
        /// Switches the level being edited. Unsaved work in the current one is kept by saving
        /// it first: losing a room to a mis-click in a list is not a trade worth making.
        /// </summary>
        void Open(string id)
        {
            if (id == LevelId) return;

            if (Session.Dirty) Save();

            if (!Host.Levels.TryLoad(id, out LevelData level))
            {
                _status = $"could not load '{id}'";
                return;
            }

            LevelId = id;
            Session.Load(level);

            // EnterEdit applies the level itself; applying it here too would just be a
            // duplicate ApplyLevel on the same frame.
            EnterEdit();

            // The previous level's warnings do not describe this one.
            _warnings.Clear();

            _status = $"opened {id}";
        }

        /// <summary>
        /// Creates an empty room, writes it, lists it, and opens it. All three: a new level
        /// that is not in the manifest is a draft nobody asked for.
        /// </summary>
        void CreateLevel()
        {
            string id = _newLevelId;

            if (string.IsNullOrEmpty(id)) { _status = "a new level needs an id"; return; }
            if (string.IsNullOrEmpty(_newWorldId)) { _status = "a new level needs a world id"; return; }

            if (Host.Levels.TryLoad(id, out _)) { _status = $"'{id}' already exists"; return; }

            var grid = new TileGrid();
            grid.FillBorder();

            var level = new LevelData
            {
                id = id,
                name = id,
                tiles = grid.ToRows(),
            };

            // List in the manifest BEFORE saving the file. The slot (world/index) is not
            // knowable until the manifest says where the level landed; saving first would
            // write world/index at their 0 default and the slot-drift lint would flag the
            // level the instant it exists.
            Session.AddLevel(id, _newWorldId);
            Host.Levels.SaveIndex(Session.Index);
            Session.MarkIndexSaved();

            level.world = Session.Index.WorldNumberOf(id);
            level.index = Session.Index.SlotOf(id) + 1;

            Host.Levels.Save(id, level);

            Open(id);

            // Open() sets its own status on failure ("could not load ..."); only stamp ours
            // over it when the open actually succeeded, so a real failure is what is seen.
            if (LevelId == id) _status = $"created {id}";

            _newLevelId = NextLevelId(id);
        }

        /// <summary>
        /// The next obvious id to create, so the next click after a successful create does not
        /// immediately report "already exists". Only advances a trailing number - an id with
        /// none is left alone rather than inventing a naming scheme.
        /// </summary>
        static string NextLevelId(string id)
        {
            int i = id.Length;
            while (i > 0 && char.IsDigit(id[i - 1])) i--;
            if (i == id.Length) return id;

            string prefix = id.Substring(0, i);
            string digits = id.Substring(i);
            int next = int.Parse(digits) + 1;
            return prefix + next.ToString().PadLeft(digits.Length, '0');
        }

        // -------------------------------------------------------------------- gui

        void OnGUI()
        {
            if (Host == null) return;

            _pixel = _pixel != null ? _pixel : MakePixel();

            if (Session.Mode == EditorMode.Play)
            {
                GUI.Label(new Rect(12f, Screen.height - 46f, 400f, 20f), "PLAY — Tab to edit, R to restart");
                return;
            }

            DrawOverlays();
            DrawPanelToggle();

            if (_showBrowser) DrawBrowser();

            if (_showPanel)
            {
                DrawPalette();
                DrawInspector();
            }
            else
            {
                _inspectorRect = Rect.zero;
            }

            DrawStatus();
        }

        /// <summary>
        /// Always drawn, so the palette can be brought back with the mouse alone. Hiding the
        /// tools is for seeing the room, not for leaving edit mode.
        /// </summary>
        void DrawPanelToggle()
        {
            _toggleRect = new Rect(Screen.width - 108f, Screen.height - 30f, 100f, 22f);

            if (GUI.Button(_toggleRect, _showPanel ? "hide tools (U)" : "tools (U)"))
                _showPanel = !_showPanel;
        }

        void DrawPalette()
        {
            _paletteRect = new Rect(8f, Screen.height - 118f, Screen.width - 124f, 110f);
            GUILayout.BeginArea(_paletteRect, GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Tool: {Session.Tool}", GUILayout.Width(120f));
            if (GUILayout.Button("1 Paint", GUILayout.Width(70f))) Session.Tool = EditorTool.Paint;
            if (GUILayout.Button("2 Rect", GUILayout.Width(70f))) Session.Tool = EditorTool.RectFill;
            if (GUILayout.Button("3 Select", GUILayout.Width(70f))) Session.Tool = EditorTool.Select;
            if (GUILayout.Button("4 Zone", GUILayout.Width(70f))) Session.Tool = EditorTool.Zone;
            if (GUILayout.Button("5 Path", GUILayout.Width(70f))) Session.Tool = EditorTool.Path;
            if (GUILayout.Button("8 Trigger", GUILayout.Width(70f))) Session.Tool = EditorTool.Trigger;
            if (GUILayout.Button("P Place", GUILayout.Width(70f))) Session.Tool = EditorTool.Place;
            GUILayout.Space(20f);
            if (GUILayout.Button("Save (Ctrl+S)", GUILayout.Width(110f))) Save();
            if (GUILayout.Button("Undo", GUILayout.Width(60f))) Undo();
            if (GUILayout.Button("Redo", GUILayout.Width(60f))) Redo();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Tile", GUILayout.Width(40f));
            foreach (TileKind kind in TilePalette)
            {
                bool selected = Session.SelectedTile == kind;
                string label = $"{TileGrid.ToChar(kind)} {kind}";
                if (GUILayout.Button(selected ? $"[{label}]" : label, GUILayout.Width(110f)))
                    Session.SelectedTile = kind;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Entity", GUILayout.Width(40f));
            foreach (string type in EditorSession.EntityTypes)
            {
                if (GUILayout.Button(_entityType == type ? $"[{type}]" : type, GUILayout.Width(110f)))
                    _entityType = type;
            }
            if (GUILayout.Button("Place at centre", GUILayout.Width(110f)))
            {
                Session.BeginEdit();
                Session.PlaceEntity(_entityType, new Vector2(TileGrid.Width * 0.5f, TileGrid.Height * 0.5f));
                SyncDynamic();
                _status = $"placed {_entityType}";
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        // -------------------------------------------------------------- inspector

        /// <summary>
        /// The selected entity's properties (spec 10 phase 3).
        ///
        /// Built from EntityFields rather than hand-written per hazard, so a new entity type
        /// appears here with no UI code — the same reasoning as GDD 5.1's verb layer.
        /// </summary>
        /// <summary>
        /// Where the inspector sits, on whichever edge is currently out of the way. Every
        /// inspector goes through this: they share one rect, and PointerOverUi reads it to
        /// keep a click on the panel from reaching the room behind it.
        /// </summary>
        Rect InspectorRect(float height)
            => new Rect(_inspectorOnLeft ? 8f : Screen.width - 252f, 60f, 244f, height);

        void DrawInspector()
        {
            // Before the exit and entity branches: the zone tool has its own selection, and
            // reaching the entity inspector's early returns would mean never drawing at all.
            if (Session.Tool == EditorTool.Zone && Session.SelectedZoneId != null)
            {
                DrawZoneInspector();
                return;
            }

            if (Session.ExitSelected) { DrawExitInspector(); return; }

            LevelEntity entity = Session.FindEntity(Session.SelectedEntityId);

            // pos comes from a file that a human may have edited; do not trust its length.
            if (entity?.pos == null || entity.pos.Length < 2) { _inspectorRect = Rect.zero; return; }

            _inspectorRect = InspectorRect(400f);
            GUILayout.BeginArea(_inspectorRect, GUI.skin.box);

            GUILayout.Label($"{entity.id}   {entity.type}");

            DrawTypeRow(entity);

            GUILayout.Space(4f);
            GUILayout.Label("position");
            float x = FloatRow("x", $"{entity.id}.x", entity.pos[0]);
            float y = FloatRow("y", $"{entity.id}.y", entity.pos[1]);
            if (!Mathf.Approximately(x, entity.pos[0]) || !Mathf.Approximately(y, entity.pos[1]))
            {
                Session.BeginEdit();
                Session.MoveEntity(entity.id, new Vector2(x, y));
                SyncDynamic();
            }

            EntityParamId[] fields = EntityFields.For(entity.type);
            if (fields.Length > 0)
            {
                GUILayout.Space(4f);
                GUILayout.Label("params");
            }

            foreach (EntityParamId id in fields)
            {
                float current = EntityFields.Get(entity.@params, id);
                float typed = FloatRow(EntityFields.Label(id), $"{entity.id}.{id}", current,
                                       EntityFields.DefaultFor(entity.type, id));

                if (Mathf.Approximately(typed, current)) continue;

                Session.BeginEdit();
                Session.SetParam(entity.id, id, typed);
                SyncDynamic();
            }

            GUILayout.Space(4f);
            DrawPathRow(entity);
            DrawStateRow(entity);

            GUILayout.EndArea();
        }

        /// <summary>
        /// The selected zone's fields, in its own area.
        ///
        /// A sibling of DrawExitInspector rather than a section inside the entity inspector:
        /// the zone selection is independent of the entity one, so sharing that method's body
        /// would mean the panel only appeared when an entity happened to be selected too.
        ///
        /// Setting _inspectorRect is load-bearing, not bookkeeping — PointerOverUi reads it to
        /// keep a click on the panel from falling through to the canvas and editing the zone
        /// underneath.
        /// </summary>
        void DrawZoneInspector()
        {
            _inspectorRect = InspectorRect(320f);
            GUILayout.BeginArea(_inspectorRect, GUI.skin.box);

            bool changed = ZoneInspector.Draw(Session, _fieldText);

            GUILayout.EndArea();

            if (changed) SyncDynamic();
        }

        /// <summary>
        /// The gate (GDD 7.4). Diameter is the level's question, so the band it admits is
        /// spelled out: "1.6 m" means little while authoring, but "fits up to nominal" is the
        /// sentence the level is actually making.
        /// </summary>
        void DrawExitInspector()
        {
            LevelExit exit = Session.Level.exit;
            if (exit?.pos == null || exit.pos.Length < 2) { _inspectorRect = Rect.zero; return; }

            _inspectorRect = InspectorRect(200f);
            GUILayout.BeginArea(_inspectorRect, GUI.skin.box);

            GUILayout.Label("exit");

            GUILayout.BeginHorizontal();
            GUILayout.Label("shape", GUILayout.Width(70f));
            if (GUILayout.Button(exit.shape, GUILayout.Width(80f)))
            {
                Session.BeginEdit();
                Session.SetExitShape(exit.shape == "slot" ? "ring" : "slot");
                SyncExit();
            }
            GUILayout.EndHorizontal();

            float x = FloatRow("x", "exit.x", exit.pos[0]);
            float y = FloatRow("y", "exit.y", exit.pos[1]);
            if (!Mathf.Approximately(x, exit.pos[0]) || !Mathf.Approximately(y, exit.pos[1]))
            {
                Session.BeginEdit();
                Session.MoveExit(new Vector2(x, y));
                SyncExit();
            }

            float diameter = FloatRow("diameter", "exit.d", exit.diameter);
            if (!Mathf.Approximately(diameter, exit.diameter))
            {
                Session.BeginEdit();
                Session.SetExitDiameter(diameter);
                SyncExit();
            }

            ExitGate gate = Session.Gate();
            GUILayout.Label(gate.Present
                ? $"fits up to {LevelLoader.BandName(gate.LargestBandThatFits())}"
                : "no exit");

            // The band the level ASKS for, which the aperture cannot state on its own: a
            // ceiling ring demands buoyancy because of where it is (GDD 7.4).
            GUILayout.BeginHorizontal();
            GUILayout.Label("asks for", GUILayout.Width(70f));
            if (GUILayout.Button(exit.required_band, GUILayout.Width(90f)))
            {
                int index = System.Array.IndexOf(BandCycle, exit.required_band);
                Session.BeginEdit();
                Session.SetRequiredBand(BandCycle[(index + 1 + BandCycle.Length) % BandCycle.Length]);
            }
            GUILayout.EndHorizontal();

            float spawn = InflationBands.RadiusOf(
                LevelLoader.ParseBand(Session.Level.spawn?.balloon_inflation));

            if (gate.Present && !gate.Fits(spawn))
                GUILayout.Label("! the spawn balloon does not fit");

            GUILayout.EndArea();
        }

        static readonly string[] BandCycle =
        {
            "shrivelled", "slack", "nominal", "full", "critical",
        };

        void DrawTypeRow(LevelEntity entity)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("type", GUILayout.Width(50f));

            if (GUILayout.Button("<", GUILayout.Width(24f))) CycleType(entity, -1);
            if (GUILayout.Button(">", GUILayout.Width(24f))) CycleType(entity, 1);

            GUILayout.EndHorizontal();
        }

        void CycleType(LevelEntity entity, int direction)
        {
            string[] types = EditorSession.EntityTypes;
            int index = System.Array.IndexOf(types, entity.type);
            if (index < 0) index = 0;

            index = (index + direction + types.Length) % types.Length;

            Session.BeginEdit();
            Session.SetEntityType(entity.id, types[index]);
            SyncDynamic();
            _status = $"{entity.id} is now a {types[index]}";
        }

        void DrawPathRow(LevelEntity entity)
        {
            int count = Session.WaypointCount(entity.id);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"path: {count}", GUILayout.Width(70f));

            if (GUILayout.Button(entity.path_mode, GUILayout.Width(90f)))
            {
                int index = System.Array.IndexOf(EntityFields.PathModes, entity.path_mode);
                string next = EntityFields.PathModes[(index + 1 + EntityFields.PathModes.Length)
                                                     % EntityFields.PathModes.Length];
                Session.BeginEdit();
                Session.SetPathMode(entity.id, next);
                SyncDynamic();
            }

            if (count > 0 && GUILayout.Button("clear", GUILayout.Width(50f)))
            {
                Session.BeginEdit();
                for (int i = count; i > 0; i--) Session.DeleteWaypoint(entity.id, i);
                SyncDynamic();
                _status = "cleared path";
            }

            GUILayout.EndHorizontal();

            if (count == 0)
                GUILayout.Label("5 Path, then click to add", GUI.skin.label);

            float phase = FloatRow("phase", $"{entity.id}.phase", entity.phase);
            if (Mathf.Approximately(phase, entity.phase)) return;

            Session.BeginEdit();
            Session.SetPhase(entity.id, phase);
            SyncDynamic();
        }

        void DrawStateRow(LevelEntity entity)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("state", GUILayout.Width(50f));

            if (GUILayout.Button(entity.start_state, GUILayout.Width(90f)))
            {
                string next = entity.start_state == "dormant" ? "active" : "dormant";
                Session.BeginEdit();
                Session.SetStartState(entity.id, next);
                SyncDynamic();
            }

            if (GUILayout.Button("delete", GUILayout.Width(60f)))
            {
                Session.BeginEdit();
                Session.DeleteEntity(entity.id);
                SyncDynamic();
                _status = "deleted entity";
            }

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// A labelled number box. The text is kept per field while it is being typed, so a
        /// half-finished number is never snapped back to the old value mid-keystroke
        /// (DevNumber). A zero shows the type default beside it, because 0 means "unset".
        /// </summary>
        float FloatRow(string label, string key, float value, float fallback = 0f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(70f));

            string text = _fieldText.TryGetValue(key, out string held) ? held : DevNumber.Format(value, false);
            string typed = GUILayout.TextField(text, GUILayout.Width(70f));

            if (typed != text) _fieldText[key] = typed;

            if (fallback > 0f && Mathf.Approximately(value, 0f))
                GUILayout.Label($"({DevNumber.Format(fallback, false)})", GUILayout.Width(60f));

            GUILayout.EndHorizontal();

            return DevNumber.TryParse(typed, false, out float parsed) ? parsed : value;
        }

        static readonly TileKind[] TilePalette =
        {
            TileKind.Empty, TileKind.Solid, TileKind.OneWay, TileKind.Rough,
            TileKind.SpikeUp, TileKind.SpikeDown, TileKind.SpikeLeft, TileKind.SpikeRight,
            TileKind.PlayerLethal,
        };

        /// <summary>
        /// The manifest, as a list you can open, reorder and add to (spec: menus and the
        /// level list). IMGUI, like every other dev panel - spec 2 is explicit that polish is
        /// not built here.
        /// </summary>
        void DrawBrowser()
        {
            _browserRect = new Rect(8f, 60f, 300f, 420f);
            GUILayout.BeginArea(_browserRect, GUI.skin.box);

            GUILayout.Label($"levels{(Session.IndexDirty ? " *" : "")}");

            // Fixed-height area with the seven-world roster the design calls for: without a
            // scroll view, worlds past the first couple are simply unreachable.
            _browserScroll = GUILayout.BeginScrollView(_browserScroll, GUILayout.Height(320f));

            if (Session.Index.worlds != null)
            {
                foreach (LevelWorld world in Session.Index.worlds)
                {
                    if (world?.levels == null) continue;

                    GUILayout.Space(4f);
                    GUILayout.Label($"{world.id}  {world.name}");

                    foreach (string id in new List<string>(world.levels)) DrawBrowserRow(id);
                }
            }

            DrawDrafts();
            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            DrawNewLevelRow();

            if (GUILayout.Button("save list"))
            {
                Host.Levels.SaveIndex(Session.Index);
                Session.MarkIndexSaved();
                _warnings = LevelIndexValidator.Validate(Session.Index, Host.Levels.List());
                _status = "saved the level list";
            }

            GUILayout.EndArea();
        }

        void DrawBrowserRow(string id)
        {
            GUILayout.BeginHorizontal();

            bool current = id == LevelId;
            if (GUILayout.Button(current ? $"[{id}]" : id, GUILayout.Width(130f))) Open(id);

            if (GUILayout.Button("^", GUILayout.Width(24f))) Session.MoveLevel(id, -1);
            if (GUILayout.Button("v", GUILayout.Width(24f))) Session.MoveLevel(id, 1);
            if (GUILayout.Button("unlist", GUILayout.Width(56f))) Session.RemoveLevel(id);

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Files on disk the manifest does not list. Shown so a draft is discoverable rather
        /// than invisible, with one click to put it in the game.
        /// </summary>
        void DrawDrafts()
        {
            List<string> drafts = Session.Index.DraftsAmong(Host.Levels.List());
            if (drafts.Count == 0) return;

            GUILayout.Space(4f);
            GUILayout.Label("drafts");

            foreach (string id in drafts)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(id, GUILayout.Width(130f))) Open(id);
                if (GUILayout.Button("add to list", GUILayout.Width(90f)))
                    Session.AddLevel(id, _newWorldId);
                GUILayout.EndHorizontal();
            }
        }

        void DrawNewLevelRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("new", GUILayout.Width(32f));
            _newLevelId = GUILayout.TextField(_newLevelId, GUILayout.Width(90f));
            _newWorldId = GUILayout.TextField(_newWorldId, GUILayout.Width(40f));

            if (GUILayout.Button("create", GUILayout.Width(60f))) CreateLevel();

            GUILayout.EndHorizontal();
        }

        void DrawStatus()
        {
            string dirty = Session.Dirty ? "*" : "";
            GUI.Label(new Rect(12f, 8f, 900f, 20f),
                      $"EDIT {LevelId}{dirty}   Tab play   L levels   U tools   G grid   H hitboxes   T safe area   Ctrl+S save   {_status}");

            if (Session.SelectedEntityId != null)
                GUI.Label(new Rect(12f, 28f, 700f, 20f), $"selected: {Session.SelectedEntityId}");

            if (!_showPanel) return;

            for (int i = 0; i < _warnings.Count && i < 8; i++)
                GUI.Label(new Rect(12f, 52f + i * 18f, 700f, 20f), $"! {_warnings[i]}");
        }

        void DrawOverlays()
        {
            if (_showGrid) DrawGrid();
            if (_showSafeArea) DrawSafeArea();
            DrawZones();
            DrawTriggers();
            if (_showHitboxes) DrawHitboxes();
            DrawEntities();
        }

        void DrawGrid()
        {
            var colour = new Color(1f, 1f, 1f, 0.10f);

            for (int x = 0; x <= TileGrid.Width; x++)
            {
                Vector2 a = WorldToGui(new Vector2(x, 0f));
                Vector2 b = WorldToGui(new Vector2(x, TileGrid.Height));
                DrawRect(new Rect(a.x, b.y, 1f, a.y - b.y), colour);
            }

            for (int y = 0; y <= TileGrid.Height; y++)
            {
                Vector2 a = WorldToGui(new Vector2(0f, y));
                Vector2 b = WorldToGui(new Vector2(TileGrid.Width, y));
                DrawRect(new Rect(a.x, a.y, b.x - a.x, 1f), colour);
            }
        }

        /// <summary>
        /// GDD 11.5: thumbs occlude roughly 5x5 tiles in each bottom corner on touch builds.
        /// No exits, no single-frame hazards, no must-see pickups in there.
        /// </summary>
        void DrawSafeArea()
        {
            var colour = new Color(1f, 0.2f, 0.2f, 0.15f);
            DrawWorldRect(new Rect(0f, 0f, 5f, 5f), colour);
            DrawWorldRect(new Rect(TileGrid.Width - 5f, 0f, 5f, 5f), colour);
        }

        /// <summary>
        /// GDD 8's asymmetry made visible: the player's bump volume is larger than the
        /// balloon's visual, every hazard's kill volume is smaller than its own.
        /// </summary>
        void DrawHitboxes()
        {
            SimWorld world = Host.World;

            float reach = world.Balloon.Radius + world.Tuning.playerBumpRadiusBonus;
            DrawWorldRect(new Rect(world.Balloon.Position.x - reach, world.Balloon.Position.y - reach,
                                   reach * 2f, reach * 2f), new Color(0.4f, 1f, 0.4f, 0.25f));

            float shrink = world.Tuning.hazardPopRadiusShrink;
            for (int y = 0; y < TileGrid.Height; y++)
            for (int x = 0; x < TileGrid.Width; x++)
            {
                TileKind kind = world.Grid[x, y];
                if (!Hazards.KillsBalloon(kind) && !Hazards.KillsPlayer(kind)) continue;

                Hazards.KillBox(kind, x, y, shrink, out float bx0, out float by0, out float bx1, out float by1);
                DrawWorldRect(new Rect(bx0, by0, bx1 - bx0, by1 - by0), new Color(1f, 0.3f, 0.3f, 0.35f));
            }
        }

        void DrawEntities()
        {
            if (Session.Level.entities == null) return;

            foreach (LevelEntity entity in Session.Level.entities)
            {
                if (entity?.pos == null || entity.pos.Length < 2) continue;

                bool selected = entity.id == Session.SelectedEntityId;
                var rect = new Rect(entity.pos[0] - 0.4f, entity.pos[1] - 0.4f, 0.8f, 0.8f);

                DrawWorldRect(rect, selected
                    ? new Color(1f, 1f, 0.3f, 0.55f)
                    : new Color(0.5f, 0.8f, 1f, 0.45f));

                Vector2 label = WorldToGui(new Vector2(entity.pos[0] - 0.4f, entity.pos[1] + 0.6f));
                GUI.Label(new Rect(label.x, label.y, 160f, 18f), $"{entity.id} {entity.type}");

                DrawPath(entity, selected);
            }

            DrawExit();
        }

        /// <summary>
        /// The aperture, and when selected the accept volume around it. GDD 8's generosity
        /// is invisible in play and worth seeing while authoring.
        /// </summary>
        void DrawExit()
        {
            ExitGate gate = Session.Gate();
            if (!gate.Present) return;

            Vector2 size = gate.Size;
            var aperture = new Rect(gate.Position.x - size.x * 0.5f, gate.Position.y - size.y * 0.5f,
                                    size.x, size.y);

            DrawWorldRect(aperture, Session.ExitSelected
                ? new Color(0.4f, 1f, 0.6f, 0.5f)
                : new Color(0.4f, 1f, 0.6f, 0.25f));

            Vector2 label = WorldToGui(new Vector2(aperture.xMin, aperture.yMax + 0.5f));
            GUI.Label(new Rect(label.x, label.y, 240f, 18f),
                      $"exit {gate.Diameter:0.##} m, fits {LevelLoader.BandName(gate.LargestBandThatFits())}");

            if (!Session.ExitSelected && !_showHitboxes) return;

            float bonus = Host.World.Tuning.exitAcceptBonus;
            DrawWorldRect(new Rect(aperture.x - bonus, aperture.y - bonus,
                                   aperture.width + bonus * 2f, aperture.height + bonus * 2f),
                          new Color(0.4f, 1f, 0.6f, 0.15f));
        }

        /// <summary>
        /// The polyline the entity walks, with a handle on every waypoint. Drawn for every
        /// entity that has one — an unselected saw's route is exactly the thing you need to
        /// see while placing the tiles it sweeps past.
        /// </summary>
        void DrawPath(LevelEntity entity, bool selected)
        {
            Vector2[] points = Session.Waypoints(entity.id);
            if (points.Length < 2) return;

            Color line = selected ? new Color(1f, 1f, 0.3f, 0.5f) : new Color(0.5f, 0.8f, 1f, 0.25f);

            for (int i = 1; i < points.Length; i++) DrawWorldLine(points[i - 1], points[i], line);

            // Loop closes the circuit, so the closing edge is part of the route and has to be
            // drawn or the author cannot see what the entity will actually walk.
            if (entity.path_mode == "loop" && points.Length > 2)
                DrawWorldLine(points[points.Length - 1], points[0], line);

            // Handle 0 sits on the entity itself and moves the whole trap.
            for (int i = 0; i < points.Length; i++)
            {
                var handle = new Rect(points[i].x - 0.18f, points[i].y - 0.18f, 0.36f, 0.36f);
                DrawWorldRect(handle, selected ? new Color(1f, 1f, 1f, 0.75f) : line);
            }
        }

        /// <summary>
        /// An axis-thin quad between two points. IMGUI cannot draw a rotated line without a
        /// matrix push, and spec 2 is explicit that ugly is correct here.
        /// </summary>
        void DrawWorldLine(Vector2 a, Vector2 b, Color colour)
        {
            Vector2 from = WorldToGui(a);
            Vector2 to = WorldToGui(b);

            Matrix4x4 previous = GUI.matrix;
            float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            float length = Vector2.Distance(from, to);

            GUIUtility.RotateAroundPivot(angle, from);
            DrawRect(new Rect(from.x, from.y - 1f, length, 2f), colour);
            GUI.matrix = previous;
        }

        // ------------------------------------------------------------------ draw

        Vector2 WorldToGui(Vector2 world)
        {
            Vector3 screen = Host.Camera.WorldToScreenPoint(world);
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        /// <summary>Handle hit radius in world units — about 4 px at 20 px/metre.</summary>
        const float ZoneHandleRadius = 0.35f;

        /// <summary>
        /// Every zone in the level, whatever its type.
        ///
        /// Drawn from Session.Level.zones rather than from the world's AirField, because
        /// LevelLoader.LoadZones admits only type "air" — the five inert types never reach
        /// the field, so a view reading it could never show them, and a zone that cannot be
        /// seen cannot be selected or edited. ZoneViewSync still draws the air zones in play;
        /// this is the authoring view.
        /// </summary>
        void DrawZones()
        {
            if (Session.Level.zones == null) return;

            foreach (LevelZone zone in Session.Level.zones)
            {
                if (zone?.rect == null || zone.rect.Length < 4) continue;

                Rect rect = ZoneGeometry.RectOf(zone.rect);
                bool selected = zone.id == Session.SelectedZoneId;
                bool off = zone.start_state == "off";

                // An off zone is dimmed rather than hidden: hiding it would make it
                // impossible to select, and so impossible to switch back on.
                Color fill = ZoneFill(zone.type);
                if (off) fill.a *= 0.4f;

                DrawWorldRect(rect, fill);

                if (!selected) continue;

                Color edge = new Color(1f, 1f, 1f, 0.8f);
                DrawWorldRect(new Rect(rect.xMin, rect.yMax - 0.05f, rect.width, 0.05f), edge);
                DrawWorldRect(new Rect(rect.xMin, rect.yMin, rect.width, 0.05f), edge);
                DrawWorldRect(new Rect(rect.xMin, rect.yMin, 0.05f, rect.height), edge);
                DrawWorldRect(new Rect(rect.xMax - 0.05f, rect.yMin, 0.05f, rect.height), edge);

                DrawHandle(new Vector2(rect.xMin, rect.yMin));
                DrawHandle(new Vector2(rect.xMax, rect.yMin));
                DrawHandle(new Vector2(rect.xMin, rect.yMax));
                DrawHandle(new Vector2(rect.xMax, rect.yMax));
            }
        }

        /// <summary>
        /// Every trigger, whatever its type — the author must see what the player cannot. A
        /// visible:true trigger is filled, an invisible one outlined, so the two are told
        /// apart at a glance. Violet: never the reserved hazard hue (GDD 13.1).
        /// </summary>
        void DrawTriggers()
        {
            if (Session.Level.triggers == null) return;

            foreach (LevelTrigger trigger in Session.Level.triggers)
            {
                if (trigger?.rect == null || trigger.rect.Length < 4) continue;

                Rect rect = ZoneGeometry.RectOf(trigger.rect);
                bool selected = trigger.id == Session.SelectedTriggerId;

                if (trigger.visible)
                {
                    DrawWorldRect(rect, TriggerFill);
                }
                else
                {
                    Color edge = TriggerFill;
                    edge.a = 0.55f;
                    DrawWorldRect(new Rect(rect.xMin, rect.yMax - 0.08f, rect.width, 0.08f), edge);
                    DrawWorldRect(new Rect(rect.xMin, rect.yMin, rect.width, 0.08f), edge);
                    DrawWorldRect(new Rect(rect.xMin, rect.yMin, 0.08f, rect.height), edge);
                    DrawWorldRect(new Rect(rect.xMax - 0.08f, rect.yMin, 0.08f, rect.height), edge);
                }

                if (!selected) continue;

                DrawHandle(new Vector2(rect.xMin, rect.yMin));
                DrawHandle(new Vector2(rect.xMax, rect.yMin));
                DrawHandle(new Vector2(rect.xMin, rect.yMax));
                DrawHandle(new Vector2(rect.xMax, rect.yMax));
            }
        }

        /// <summary>Violet. Not hazard red, not balloon yellow (GDD 13.1).</summary>
        static readonly Color TriggerFill = new Color(0.62f, 0.45f, 0.85f, 0.25f);

        void DrawHandle(Vector2 corner)
        {
            const float r = ZoneHandleRadius;
            DrawWorldRect(new Rect(corner.x - r * 0.5f, corner.y - r * 0.5f, r, r),
                          new Color(1f, 1f, 1f, 0.9f));
        }

        /// <summary>
        /// Air borrows the fan's blue, so wind looks like wind wherever it comes from. The
        /// inert types get a flat grey: they do nothing, and dressing them in a colour that
        /// suggests they do would be a lie. Never the reserved hazard hue (GDD 13.1).
        /// </summary>
        static Color ZoneFill(string type)
            => type == "air"
                ? new Color(0.45f, 0.65f, 0.75f, 0.30f)
                : new Color(0.55f, 0.55f, 0.60f, 0.22f);

        void DrawWorldRect(Rect worldRect, Color colour)
        {
            Vector2 topLeft = WorldToGui(new Vector2(worldRect.xMin, worldRect.yMax));
            Vector2 bottomRight = WorldToGui(new Vector2(worldRect.xMax, worldRect.yMin));

            DrawRect(new Rect(topLeft.x, topLeft.y, bottomRight.x - topLeft.x, bottomRight.y - topLeft.y), colour);
        }

        void DrawRect(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, _pixel);
            GUI.color = previous;
        }

        static Texture2D MakePixel()
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return texture;
        }
    }
}
#endif
