#if LEVEL_EDITOR
using System.Collections.Generic;
using KeepyUppy.Sim;
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
        /// Where the UI is this frame, so a click on a button does not also paint the tile
        /// behind it. Recomputed in OnGUI because the layout follows the screen size.
        /// </summary>
        Rect _paletteRect;
        Rect _toggleRect;

        public EditorMode Mode => Session.Mode;

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

            HandleEditorKeys(kb);
            HandleMouse();
        }

        void HandleEditorKeys(Keyboard kb)
        {
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;

            if (ctrl && kb.sKey.wasPressedThisFrame) { Save(); return; }
            if (ctrl && kb.zKey.wasPressedThisFrame) { _status = Session.UndoEdit() ? "undo" : "nothing to undo"; return; }
            if (ctrl && kb.yKey.wasPressedThisFrame) { _status = Session.RedoEdit() ? "redo" : "nothing to redo"; return; }

            if (kb.digit1Key.wasPressedThisFrame) Session.Tool = EditorTool.Paint;
            if (kb.digit2Key.wasPressedThisFrame) Session.Tool = EditorTool.RectFill;
            if (kb.digit3Key.wasPressedThisFrame) Session.Tool = EditorTool.Select;

            if (kb.uKey.wasPressedThisFrame) _showPanel = !_showPanel;
            if (kb.gKey.wasPressedThisFrame) _showGrid = !_showGrid;
            if (kb.hKey.wasPressedThisFrame) _showHitboxes = !_showHitboxes;
            if (kb.tKey.wasPressedThisFrame) _showSafeArea = !_showSafeArea;

            if (kb.deleteKey.wasPressedThisFrame && Session.SelectedEntityId != null)
            {
                Session.BeginEdit();
                Session.DeleteEntity(Session.SelectedEntityId);
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
                return;
            }

            Vector2 world = ScreenToWorld(screen);
            Vector2Int tile = GridCursor.ToTile(world);

            switch (Session.Tool)
            {
                case EditorTool.Paint: PaintTool(mouse, tile); break;
                case EditorTool.RectFill: RectTool(mouse, tile); break;
                case EditorTool.Select: SelectTool(mouse, world); break;
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

            if (_dragging && Session.SelectedEntityId != null)
                Session.MoveEntity(Session.SelectedEntityId, world + _entityDragOffset);
        }

        /// <summary>Screen point (y up) against the GUI rects (y down).</summary>
        bool PointerOverUi(Vector2 screen)
        {
            var point = new Vector2(screen.x, Screen.height - screen.y);
            return _toggleRect.Contains(point) || (_showPanel && _paletteRect.Contains(point));
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
            if (_showPanel) DrawPalette();
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
            GUILayout.Space(20f);
            if (GUILayout.Button("Save (Ctrl+S)", GUILayout.Width(110f))) Save();
            if (GUILayout.Button("Undo", GUILayout.Width(60f))) Session.UndoEdit();
            if (GUILayout.Button("Redo", GUILayout.Width(60f))) Session.RedoEdit();
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
                _status = $"placed {_entityType}";
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
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
            }
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
