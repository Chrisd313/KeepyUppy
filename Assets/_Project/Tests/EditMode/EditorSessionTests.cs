using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// The editor's mutations and undo history (level-editor-spec 4).
    ///
    /// The MonoBehaviour on top is input plumbing and cannot be tested in EditMode, which is
    /// exactly why none of the logic lives there.
    /// </summary>
    public sealed class EditorSessionTests
    {
        static EditorSession Session()
        {
            var grid = new TileGrid();
            grid.FillBorder();

            var session = new EditorSession();
            session.Load(new LevelData { tiles = grid.ToRows() });
            return session;
        }

        // -------------------------------------------------------------------- tiles

        [Test]
        public void Paint_SetsATile_AndMarksDirty()
        {
            EditorSession session = Session();
            Assert.IsFalse(session.Dirty);

            session.Paint(new Vector2Int(5, 5), TileKind.Solid);

            Assert.AreEqual(TileKind.Solid, session.Grid[5, 5]);
            Assert.IsTrue(session.Dirty);
        }

        [Test]
        public void Paint_OutsideTheRoom_IsIgnored()
        {
            EditorSession session = Session();
            session.Paint(new Vector2Int(-3, 40), TileKind.Solid);
            Assert.IsFalse(session.Dirty, "an out-of-bounds click should do nothing at all");
        }

        [Test]
        public void FillRect_WorksInEveryDragDirection()
        {
            // Dragging up-left must fill the same rectangle as dragging down-right.
            EditorSession forward = Session();
            forward.FillRect(new Vector2Int(4, 4), new Vector2Int(7, 6), TileKind.Solid);

            EditorSession backward = Session();
            backward.FillRect(new Vector2Int(7, 6), new Vector2Int(4, 4), TileKind.Solid);

            for (int y = 0; y < TileGrid.Height; y++)
            for (int x = 0; x < TileGrid.Width; x++)
                Assert.AreEqual(forward.Grid[x, y], backward.Grid[x, y], $"tile {x},{y}");

            Assert.AreEqual(TileKind.Solid, forward.Grid[4, 4]);
            Assert.AreEqual(TileKind.Solid, forward.Grid[7, 6]);
            Assert.AreEqual(TileKind.Empty, forward.Grid[8, 6], "fill leaked past the corner");
        }

        [Test]
        public void FillRect_ClampsToTheRoom()
        {
            EditorSession session = Session();
            Assert.DoesNotThrow(() => session.FillRect(new Vector2Int(-5, -5), new Vector2Int(99, 99), TileKind.Rough));
            Assert.AreEqual(TileKind.Rough, session.Grid[0, 0]);
        }

        // ----------------------------------------------------------------- entities

        [Test]
        public void PlaceEntity_SnapsToHalfTile_AndSelects()
        {
            EditorSession session = Session();
            LevelEntity entity = session.PlaceEntity("saw", new Vector2(6.3f, 9.1f));

            Assert.AreEqual(6.5f, entity.pos[0], 1e-4f, "entities snap to half-tile");
            Assert.AreEqual(9.0f, entity.pos[1], 1e-4f);
            Assert.AreEqual(entity.id, session.SelectedEntityId);
        }

        [Test]
        public void EntityIds_AreReusedAfterDeletion()
        {
            EditorSession session = Session();
            session.PlaceEntity("saw", Vector2.one);
            LevelEntity second = session.PlaceEntity("cat", Vector2.one * 4f);

            Assert.AreEqual("e02", second.id);

            session.DeleteEntity("e01");
            Assert.AreEqual("e01", session.PlaceEntity("fan", Vector2.one * 8f).id,
                            "ids should stay short and predictable");
        }

        [Test]
        public void EntityAt_FindsTheNearestWithinReach()
        {
            EditorSession session = Session();
            session.PlaceEntity("saw", new Vector2(10f, 10f));

            Assert.IsNotNull(session.EntityAt(new Vector2(10.2f, 10.1f)));
            Assert.IsNull(session.EntityAt(new Vector2(14f, 10f)), "well outside the click radius");
        }

        [Test]
        public void DeleteEntity_ClearsTheSelection()
        {
            EditorSession session = Session();
            LevelEntity entity = session.PlaceEntity("saw", Vector2.one * 5f);

            Assert.IsTrue(session.DeleteEntity(entity.id));
            Assert.IsNull(session.SelectedEntityId);
            Assert.IsFalse(session.DeleteEntity(entity.id), "deleting twice should be harmless");
        }

        // --------------------------------------------------------------------- undo

        [Test]
        public void Undo_RestoresTiles()
        {
            EditorSession session = Session();

            session.BeginEdit();
            session.Paint(new Vector2Int(5, 5), TileKind.Solid);
            Assert.AreEqual(TileKind.Solid, session.Grid[5, 5]);

            Assert.IsTrue(session.UndoEdit());
            Assert.AreEqual(TileKind.Empty, session.Grid[5, 5]);
        }

        [Test]
        public void Undo_RestoresEntities()
        {
            EditorSession session = Session();

            session.BeginEdit();
            session.PlaceEntity("saw", Vector2.one * 6f);
            Assert.AreEqual(1, session.Level.entities.Length);

            session.UndoEdit();
            Assert.AreEqual(0, session.Level.entities.Length);
            Assert.IsNull(session.SelectedEntityId, "the selection pointed at something that no longer exists");
        }

        [Test]
        public void RedoReappliesTheEdit()
        {
            EditorSession session = Session();

            session.BeginEdit();
            session.Paint(new Vector2Int(3, 3), TileKind.SpikeUp);

            session.UndoEdit();
            Assert.IsTrue(session.RedoEdit());
            Assert.AreEqual(TileKind.SpikeUp, session.Grid[3, 3]);
        }

        [Test]
        public void ANewEditClearsTheRedoStack()
        {
            EditorSession session = Session();

            session.BeginEdit();
            session.Paint(new Vector2Int(3, 3), TileKind.Solid);
            session.UndoEdit();
            Assert.IsTrue(session.Undo.CanRedo);

            session.BeginEdit();
            session.Paint(new Vector2Int(9, 9), TileKind.Solid);

            Assert.IsFalse(session.Undo.CanRedo, "redo must not survive a divergent edit");
        }

        [Test]
        public void UndoHistory_IsCappedAtFifty()
        {
            EditorSession session = Session();

            for (int i = 0; i < 80; i++)
            {
                session.BeginEdit();
                session.Paint(new Vector2Int(i % TileGrid.Width, 5), TileKind.Solid);
            }

            Assert.AreEqual(UndoStack.MaxDepth, session.Undo.UndoDepth);
        }

        [Test]
        public void UndoOnAFreshSession_DoesNothing()
        {
            EditorSession session = Session();
            Assert.IsFalse(session.UndoEdit());
            Assert.IsFalse(session.RedoEdit());
        }

        // -------------------------------------------------------------- round trip

        [Test]
        public void PaintingMutatesTheGridItWasGiven()
        {
            // The editor shares the live world's grid, which is what makes an edit visible
            // immediately instead of at the next Tab. Given no grid it owns a private one.
            var world = new SimWorld();
            var session = new EditorSession(world.Grid);
            session.Load(new LevelData { tiles = new TileGrid().ToRows() });

            int before = world.Grid.Version;
            session.Paint(new Vector2Int(9, 4), TileKind.Solid);

            Assert.AreEqual(TileKind.Solid, world.Grid[9, 4], "the world's grid did not change");
            Assert.Greater(world.Grid.Version, before, "the renderer watches Version to know when to rebuild");
        }

        [Test]
        public void UndoRestoresTilesInTheSharedGrid()
        {
            var world = new SimWorld();
            var session = new EditorSession(world.Grid);
            session.Load(new LevelData { tiles = new TileGrid().ToRows() });

            session.BeginEdit();
            session.Paint(new Vector2Int(9, 4), TileKind.Solid);
            session.UndoEdit();

            Assert.AreEqual(TileKind.Empty, world.Grid[9, 4]);
        }

        [Test]
        public void Flush_WritesTheWorkingGridBackIntoTheLevel()
        {
            // The tiles on screen and the tiles in the file must never drift apart.
            EditorSession session = Session();
            session.Paint(new Vector2Int(12, 7), TileKind.SpikeDown);

            LevelData level = session.Flush();
            var reloaded = new TileGrid();
            reloaded.LoadRows(level.tiles);

            Assert.AreEqual(TileKind.SpikeDown, reloaded[12, 7]);
        }

        [Test]
        public void EditedLevel_LoadsBackIntoTheSim()
        {
            EditorSession session = Session();
            session.Paint(new Vector2Int(12, 7), TileKind.SpikeDown);
            session.PlaceEntity("saw", new Vector2(8f, 8f));

            var world = new SimWorld();
            LevelLoader.Apply(session.Flush(), world, out _, out _);

            Assert.AreEqual(TileKind.SpikeDown, world.Grid[12, 7]);
        }

        [Test]
        public void AddZone_CreatesAnAirZoneAndSelectsIt()
        {
            var session = new EditorSession();

            LevelZone zone = session.AddZone(new Rect(20f, 1f, 6f, 10f));

            Assert.AreEqual("z01", zone.id);
            Assert.AreEqual("air", zone.type);
            Assert.AreEqual("on", zone.start_state);
            Assert.AreEqual(new[] { 20f, 1f, 6f, 10f }, zone.rect);
            Assert.AreEqual("z01", session.SelectedZoneId);
            Assert.AreEqual(1, session.Level.zones.Length);
            Assert.IsTrue(session.Dirty);
        }

        [Test]
        public void ZoneIds_ReuseTheLowestFreeSlotAfterADeletion()
        {
            var session = new EditorSession();
            session.AddZone(new Rect(0f, 0f, 2f, 2f));
            session.AddZone(new Rect(4f, 0f, 2f, 2f));
            session.AddZone(new Rect(8f, 0f, 2f, 2f));

            Assert.IsTrue(session.DeleteZone("z02"));

            Assert.AreEqual("z02", session.AddZone(new Rect(12f, 0f, 2f, 2f)).id);
        }

        [Test]
        public void ZoneAt_PicksTheSmallestZoneContainingThePoint()
        {
            // A small vent drawn inside a room-wide thermal must stay reachable.
            var session = new EditorSession();
            session.AddZone(new Rect(0f, 0f, 30f, 16f));           // z01, the thermal
            LevelZone vent = session.AddZone(new Rect(10f, 4f, 2f, 2f));   // z02, inside it

            Assert.AreEqual(vent.id, session.ZoneAt(new Vector2(11f, 5f)).id);
            Assert.AreEqual("z01", session.ZoneAt(new Vector2(25f, 14f)).id);
            Assert.IsNull(session.ZoneAt(new Vector2(31.5f, 17.5f)));
        }

        [Test]
        public void ZoneAt_IgnoresAZoneWithAMalformedRect()
        {
            var session = new EditorSession();
            session.AddZone(new Rect(0f, 0f, 4f, 4f));
            session.Level.zones[0].rect = new[] { 1f, 2f };   // hand-edited badly

            Assert.IsNull(session.ZoneAt(new Vector2(2f, 2f)));
        }

        [Test]
        public void SetZoneRect_WritesBackThroughTheSchemaArray()
        {
            var session = new EditorSession();
            session.AddZone(new Rect(0f, 0f, 4f, 4f));

            session.SetZoneRect("z01", new Rect(6f, 2f, 3f, 5f));

            Assert.AreEqual(new[] { 6f, 2f, 3f, 5f }, session.FindZone("z01").rect);
        }

        [Test]
        public void DeleteZone_ClearsTheSelectionOnlyForTheDeletedZone()
        {
            var session = new EditorSession();
            session.AddZone(new Rect(0f, 0f, 4f, 4f));
            LevelZone second = session.AddZone(new Rect(8f, 0f, 4f, 4f));

            Assert.IsTrue(session.DeleteZone("z01"));
            Assert.AreEqual(second.id, session.SelectedZoneId, "deleting another zone must not clear the selection");

            Assert.IsTrue(session.DeleteZone(second.id));
            Assert.IsNull(session.SelectedZoneId);

            Assert.IsFalse(session.DeleteZone("z99"), "deleting a missing zone reports failure");
        }

        [Test]
        public void ZoneEdits_SurviveAnUndoRoundTrip()
        {
            var session = new EditorSession();
            session.AddZone(new Rect(20f, 1f, 6f, 10f));

            session.BeginEdit();
            session.SetZoneRect("z01", new Rect(2f, 2f, 2f, 2f));

            Assert.IsTrue(session.UndoEdit());
            Assert.AreEqual(new[] { 20f, 1f, 6f, 10f }, session.FindZone("z01").rect);
        }

        [Test]
        public void UndoingAZoneCreation_ClearsTheDanglingSelection()
        {
            var session = new EditorSession();

            session.BeginEdit();
            session.AddZone(new Rect(20f, 1f, 6f, 10f));

            Assert.AreEqual("z01", session.SelectedZoneId);
            Assert.IsTrue(session.UndoEdit());

            Assert.IsNull(session.SelectedZoneId, "the selected zone no longer exists");
        }

        [Test]
        public void SelectingAZone_ClearsTheExitSelection()
        {
            var session = new EditorSession();
            session.SelectExit();

            session.AddZone(new Rect(4f, 4f, 2f, 2f));

            Assert.IsFalse(session.ExitSelected, "a zone and the exit must not both be selected");
            Assert.AreEqual("z01", session.SelectedZoneId);
        }

        [Test]
        public void SelectingTheExit_ClearsTheZoneSelection()
        {
            // Otherwise the inspector's zone branch, which dispatches first, permanently
            // shadows the exit inspector.
            var session = new EditorSession();
            session.AddZone(new Rect(4f, 4f, 2f, 2f));

            session.SelectExit();

            Assert.IsNull(session.SelectedZoneId);
            Assert.IsTrue(session.ExitSelected);
        }

        [Test]
        public void InspectorSetters_MarkTheSessionDirty()
        {
            // Dirty is what makes the editor auto-save on the way out. An edit that does not
            // set it is an edit the author loses without being told.
            var session = new EditorSession();
            session.AddZone(new Rect(4f, 4f, 2f, 2f));
            session.MarkSaved();

            session.SetZoneType("z01", "heat");
            Assert.IsTrue(session.Dirty, "type");

            session.MarkSaved();
            session.SetZoneParams("z01", 1f, 2f, 3f);
            Assert.IsTrue(session.Dirty, "params");

            session.MarkSaved();
            session.SetZoneEnabled("z01", false);
            Assert.IsTrue(session.Dirty, "start_state");
        }

        [Test]
        public void SetZoneEnabled_WritesTheSchemaStrings()
        {
            var session = new EditorSession();
            session.AddZone(new Rect(4f, 4f, 2f, 2f));

            session.SetZoneEnabled("z01", false);
            Assert.AreEqual("off", session.FindZone("z01").start_state);

            session.SetZoneEnabled("z01", true);
            Assert.AreEqual("on", session.FindZone("z01").start_state);
        }

        [Test]
        public void SetZoneParams_SurvivesANullParamsBlock()
        {
            var session = new EditorSession();
            session.AddZone(new Rect(4f, 4f, 2f, 2f));
            session.FindZone("z01").@params = null;   // hand-edited JSON

            session.SetZoneParams("z01", 1.5f, 4.5f, 0f);

            Assert.AreEqual(4.5f, session.FindZone("z01").@params.vy, 1e-5f);
        }

        [Test]
        public void TheThreeSelectionsAreMutuallyExclusive()
        {
            // More than the inspector reads these: the Delete key tests each independently, so
            // two live selections means one keypress destroying two objects.
            var session = new EditorSession();
            session.AddZone(new Rect(4f, 4f, 2f, 2f));

            session.SelectEntity("e01");
            Assert.IsNull(session.SelectedZoneId);
            Assert.IsFalse(session.ExitSelected);

            session.SelectZone("z01");
            Assert.IsNull(session.SelectedEntityId);
            Assert.IsFalse(session.ExitSelected);

            session.SelectExit();
            Assert.IsNull(session.SelectedEntityId);
            Assert.IsNull(session.SelectedZoneId);
        }
    }
}
