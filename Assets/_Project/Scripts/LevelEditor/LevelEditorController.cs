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

        // Overlays
        bool _showPanel = true;
        bool _showGrid = true;
        bool _showHitboxes;
        bool _showSafeArea;

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

        public void Begin(LevelData level)
        {
            // Share the world's grid, so edits are visible the moment they are made rather
            // than at the next Tab.
            Session = new EditorSession(Host.World.Grid);
            Session.Load(level);
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

            if (Session.SelectedEntityId != _lastSelected)
            {
                _lastSelected = Session.SelectedEntityId;
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
            if (kb.digit5Key.wasPressedThisFrame) Session.Tool = EditorTool.Path;
            if (kb.pKey.wasPressedThisFrame) Session.Tool = EditorTool.Place;

            if (kb.uKey.wasPressedThisFrame) _showPanel = !_showPanel;
            if (kb.gKey.wasPressedThisFrame) _showGrid = !_showGrid;
            if (kb.hKey.wasPressedThisFrame) _showHitboxes = !_showHitboxes;
            if (kb.tKey.wasPressedThisFrame) _showSafeArea = !_showSafeArea;

            if (kb.deleteKey.wasPressedThisFrame && Session.SelectedEntityId != null)
            {
                Session.BeginEdit();
                Session.DeleteEntity(Session.SelectedEntityId);
                SyncEntities();
                _status = "deleted entity";
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
                case EditorTool.Path: PathTool(mouse, world); break;
                case EditorTool.Place: PlaceTool(mouse, world); break;
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
                    Session.SelectedEntityId = hit.id;
                    _entityDragOffset = new Vector2(hit.pos[0], hit.pos[1]) - world;
                    Session.BeginEdit();
                    _dragging = true;
                }
                else
                {
                    Session.SelectedEntityId = null;
                }
            }

            if (mouse.leftButton.wasReleasedThisFrame) _dragging = false;

            if (!_dragging || Session.SelectedEntityId == null) return;

            Session.MoveEntity(Session.SelectedEntityId, world + _entityDragOffset);
            SyncEntities();
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
            SyncEntities();
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
                    Session.SelectedEntityId = hit?.id;
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
                    SyncEntities();
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

                SyncEntities();
            }

            if (mouse.leftButton.wasReleasedThisFrame) _draggingWaypoint = -1;

            if (_draggingWaypoint >= 0 && mouse.leftButton.isPressed)
            {
                Session.MoveWaypoint(id, _draggingWaypoint, world);
                SyncEntities();
            }
        }

        /// <summary>
        /// Pushes entity and zone edits into the running world so they are visible at once.
        /// Geometry, spawns and the paused run are left alone — this is not a reload.
        /// </summary>
        void SyncEntities() => Host.SyncEntities(Session.Level);

        /// <summary>Screen point (y up) against the GUI rects (y down).</summary>
        bool PointerOverUi(Vector2 screen)
        {
            var point = new Vector2(screen.x, Screen.height - screen.y);
            if (_toggleRect.Contains(point)) return true;
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

            SyncEntities();
            return true;
        }

        bool Redo()
        {
            if (!Session.RedoEdit()) return false;

            SyncEntities();
            return true;
        }

        void Save()
        {
            LevelData level = Session.Flush();
            _warnings = LevelValidator.Validate(level);

            // Validation never blocks a save. A half-finished level must always be saveable.
            Host.Levels.Save(LevelId, level);
            Session.MarkSaved();

            _status = _warnings.Count == 0
                ? $"saved {LevelId}"
                : $"saved {LevelId} with {_warnings.Count} warning(s)";
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
            if (GUILayout.Button("5 Path", GUILayout.Width(70f))) Session.Tool = EditorTool.Path;
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
                SyncEntities();
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
        void DrawInspector()
        {
            LevelEntity entity = Session.FindEntity(Session.SelectedEntityId);

            // pos comes from a file that a human may have edited; do not trust its length.
            if (entity?.pos == null || entity.pos.Length < 2) { _inspectorRect = Rect.zero; return; }

            _inspectorRect = new Rect(Screen.width - 252f, 60f, 244f, 400f);
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
                SyncEntities();
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
                SyncEntities();
            }

            GUILayout.Space(4f);
            DrawPathRow(entity);
            DrawStateRow(entity);

            GUILayout.EndArea();
        }

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
            SyncEntities();
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
                SyncEntities();
            }

            if (count > 0 && GUILayout.Button("clear", GUILayout.Width(50f)))
            {
                Session.BeginEdit();
                for (int i = count; i > 0; i--) Session.DeleteWaypoint(entity.id, i);
                SyncEntities();
                _status = "cleared path";
            }

            GUILayout.EndHorizontal();

            if (count == 0)
                GUILayout.Label("5 Path, then click to add", GUI.skin.label);

            float phase = FloatRow("phase", $"{entity.id}.phase", entity.phase);
            if (Mathf.Approximately(phase, entity.phase)) return;

            Session.BeginEdit();
            Session.SetPhase(entity.id, phase);
            SyncEntities();
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
                SyncEntities();
            }

            if (GUILayout.Button("delete", GUILayout.Width(60f)))
            {
                Session.BeginEdit();
                Session.DeleteEntity(entity.id);
                SyncEntities();
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

        void DrawStatus()
        {
            string dirty = Session.Dirty ? "*" : "";
            GUI.Label(new Rect(12f, 8f, 900f, 20f),
                      $"EDIT {LevelId}{dirty}   Tab play   U tools   G grid   H hitboxes   T safe area   Ctrl+S save   {_status}");

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
