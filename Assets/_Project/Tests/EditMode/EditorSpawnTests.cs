using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Moving the player and balloon spawns from the editor.
    ///
    /// They ride the Select tool as two more draggable things rather than taking a tool of
    /// their own: picking, half-tile snap, arrow-key nudge and undo are already built there,
    /// and two points do not earn a mode the author has to remember to leave.
    /// </summary>
    public sealed class EditorSpawnTests
    {
        static EditorSession Session(float[] player = null, float[] balloon = null)
        {
            var grid = new TileGrid();
            grid.FillBorder();

            var session = new EditorSession();
            session.Load(new LevelData
            {
                tiles = grid.ToRows(),
                spawn = new LevelSpawn
                {
                    player = player ?? new[] { 4f, 2f },
                    balloon = balloon ?? new[] { 6f, 13f },
                    balloon_inflation = "nominal",
                },
            });
            return session;
        }

        // -------------------------------------------------------------------- picking

        [Test]
        public void AClickOnASpawn_FindsIt()
        {
            EditorSession session = Session();

            Assert.AreEqual(SpawnMarker.Player, session.SpawnAt(new Vector2(4.1f, 2.1f)));
            Assert.AreEqual(SpawnMarker.Balloon, session.SpawnAt(new Vector2(6f, 13f)));
        }

        [Test]
        public void AClickOnOpenSpace_FindsNoSpawn()
        {
            EditorSession session = Session();
            Assert.AreEqual(SpawnMarker.None, session.SpawnAt(new Vector2(20f, 9f)));
        }

        [Test]
        public void WithNoSpawnBlock_TheMarkersSitWhereTheGameWillPutThem()
        {
            // A level that never declared spawns still runs — the loader falls back. The
            // editor has to show the author the same two points, or the first drag would
            // teleport a spawn they never saw.
            var session = new EditorSession();
            session.Load(new LevelData());

            Assert.AreEqual(SpawnMarker.Player, session.SpawnAt(LevelLoader.DefaultPlayerSpawn));
            Assert.AreEqual(SpawnMarker.Balloon, session.SpawnAt(LevelLoader.DefaultBalloonSpawn));
        }

        // -------------------------------------------------------------------- moving

        [Test]
        public void MovingASpawn_WritesTheLevel_SnappedToHalfTile()
        {
            EditorSession session = Session();

            session.MoveSpawn(SpawnMarker.Player, new Vector2(12.3f, 7.1f));

            Assert.AreEqual(12.5f, session.Level.spawn.player[0], 1e-4f);
            Assert.AreEqual(7.0f, session.Level.spawn.player[1], 1e-4f);
            Assert.IsTrue(session.Dirty);
        }

        [Test]
        public void MovingTheBalloonSpawn_LeavesThePlayerSpawnAlone()
        {
            EditorSession session = Session();

            session.MoveSpawn(SpawnMarker.Balloon, new Vector2(20f, 15f));

            Assert.AreEqual(20f, session.Level.spawn.balloon[0], 1e-4f);
            Assert.AreEqual(4f, session.Level.spawn.player[0], 1e-4f);
        }

        [Test]
        public void MovingASpawn_OnALevelWithNoSpawnBlock_CreatesOne()
        {
            var session = new EditorSession();
            session.Load(new LevelData());

            session.MoveSpawn(SpawnMarker.Balloon, new Vector2(9f, 9f));

            Assert.IsNotNull(session.Level.spawn);
            Assert.AreEqual(9f, session.Level.spawn.balloon[0], 1e-4f);
            Assert.AreEqual(LevelLoader.DefaultPlayerSpawn.x, session.Level.spawn.player[0], 1e-4f,
                "the spawn the author did not touch must land on the loader's own default");
        }

        [Test]
        public void MovingASpawn_IsUndoable()
        {
            EditorSession session = Session();

            session.BeginEdit();
            session.MoveSpawn(SpawnMarker.Player, new Vector2(25f, 5f));
            Assert.AreEqual(25f, session.Level.spawn.player[0], 1e-4f);

            Assert.IsTrue(session.UndoEdit());
            Assert.AreEqual(4f, session.Level.spawn.player[0], 1e-4f);
        }

        // -------------------------------------------------------------------- selection

        [Test]
        public void SelectingASpawn_ClearsEveryOtherSelection()
        {
            EditorSession session = Session();
            session.BeginEdit();
            session.PlaceEntity("saw", new Vector2(10f, 10f));
            session.SelectExit();

            session.SelectSpawn(SpawnMarker.Balloon);

            Assert.AreEqual(SpawnMarker.Balloon, session.SelectedSpawn);
            Assert.IsFalse(session.ExitSelected);
            Assert.IsNull(session.SelectedEntityId);
        }

        [Test]
        public void SelectingSomethingElse_ClearsTheSpawnSelection(
            [Values("entity", "exit")] string what)
        {
            // The selections are mutually exclusive because Delete tests each one
            // independently; a live spawn selection alongside a live entity selection is one
            // keypress destroying something the author was not looking at.
            EditorSession session = Session();
            session.SelectSpawn(SpawnMarker.Player);

            if (what == "entity") session.SelectEntity(null);
            else session.SelectExit();

            Assert.AreEqual(SpawnMarker.None, session.SelectedSpawn);
        }

        [Test]
        public void LoadingALevel_ClearsTheSpawnSelection()
        {
            EditorSession session = Session();
            session.SelectSpawn(SpawnMarker.Player);

            session.Load(new LevelData());

            Assert.AreEqual(SpawnMarker.None, session.SelectedSpawn);
        }
    }
}
