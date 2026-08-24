using KeepyUppy.LevelEditing;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Level;
using NUnit.Framework;
using UnityEngine;

namespace KeepyUppy.SimTests
{
    /// <summary>
    /// Authoring entities: the inspector's edits and the path tool's waypoints
    /// (level-editor-spec 10, phases 3 and 4).
    ///
    /// All of it sits on EditorSession rather than the MonoBehaviour, which is what makes it
    /// testable at all — the controller above is mouse plumbing.
    /// </summary>
    public sealed class EditorEntityTests
    {
        static EditorSession Session()
        {
            var session = new EditorSession();
            session.Load(new LevelData { tiles = new TileGrid().ToRows() });
            return session;
        }

        static LevelEntity Saw(EditorSession session, Vector2 at)
            => session.PlaceEntity("saw", at);

        // -------------------------------------------------------------- waypoints

        [Test]
        public void WaypointZeroIsTheEntityItself()
        {
            // The convention EntityPath.FromFlat uses. If the editor disagreed with it, the
            // route drawn on screen would not be the route the saw walks.
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));

            session.AddWaypoint(saw.id, new Vector2(14f, 9f));
            Vector2[] points = session.Waypoints(saw.id);

            Assert.AreEqual(2, points.Length);
            Assert.AreEqual(new Vector2(6f, 9f), points[0], "waypoint 0 is the entity");
            Assert.AreEqual(new Vector2(14f, 9f), points[1]);
            Assert.AreEqual(1, session.WaypointCount(saw.id), "only stored waypoints are counted");
        }

        [Test]
        public void WaypointsSnapToHalfTile()
        {
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));

            session.AddWaypoint(saw.id, new Vector2(14.3f, 9.4f));

            Assert.AreEqual(14.5f, session.Waypoints(saw.id)[1].x, 1e-4f);
            Assert.AreEqual(9.5f, session.Waypoints(saw.id)[1].y, 1e-4f);
        }

        [Test]
        public void MovingWaypointZeroMovesTheEntity()
        {
            // The handle sits on top of the entity, so dragging it has to mean what an author
            // expects: the trap moves and the path comes with it.
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));
            session.AddWaypoint(saw.id, new Vector2(14f, 9f));

            session.MoveWaypoint(saw.id, 0, new Vector2(8f, 3f));

            Assert.AreEqual(8f, saw.pos[0], 1e-4f);
            Assert.AreEqual(3f, saw.pos[1], 1e-4f);
            Assert.AreEqual(new Vector2(14f, 9f), session.Waypoints(saw.id)[1],
                            "the rest of the path should not have moved");
        }

        [Test]
        public void MovingAndDeletingWaypoints()
        {
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));

            session.AddWaypoint(saw.id, new Vector2(14f, 9f));
            session.AddWaypoint(saw.id, new Vector2(20f, 9f));

            session.MoveWaypoint(saw.id, 1, new Vector2(12f, 5f));
            Assert.AreEqual(new Vector2(12f, 5f), session.Waypoints(saw.id)[1]);

            Assert.IsTrue(session.DeleteWaypoint(saw.id, 1));
            Assert.AreEqual(1, session.WaypointCount(saw.id));
            Assert.AreEqual(new Vector2(20f, 9f), session.Waypoints(saw.id)[1],
                            "deleting the first waypoint should leave the second");
        }

        [Test]
        public void WaypointZeroCannotBeDeleted()
        {
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));
            session.AddWaypoint(saw.id, new Vector2(14f, 9f));

            Assert.IsFalse(session.DeleteWaypoint(saw.id, 0), "waypoint 0 is the entity itself");
            Assert.IsFalse(session.DeleteWaypoint(saw.id, 9), "out of range should be harmless");
            Assert.AreEqual(1, session.WaypointCount(saw.id));
        }

        [Test]
        public void WaypointAt_FindsTheHandleUnderAClick()
        {
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));
            session.AddWaypoint(saw.id, new Vector2(14f, 9f));

            Assert.AreEqual(0, session.WaypointAt(saw.id, new Vector2(6.1f, 9.1f)));
            Assert.AreEqual(1, session.WaypointAt(saw.id, new Vector2(13.9f, 9f)));
            Assert.AreEqual(-1, session.WaypointAt(saw.id, new Vector2(10f, 2f)), "nowhere near one");
        }

        [Test]
        public void AnEditedPathReachesTheSim()
        {
            // The whole point of the tool: what is drawn has to be what runs. A saw with a
            // path must actually move when the level is loaded.
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));
            session.SetParam(saw.id, EntityParamId.Speed, 4f);
            session.AddWaypoint(saw.id, new Vector2(18f, 9f));

            var world = new SimWorld();
            LevelLoader.Apply(session.Flush(), world, out _, out _);

            SimEntity runtime = world.Entities.Find(saw.id);
            Assert.IsNotNull(runtime);
            Assert.AreEqual(2, runtime.Points.Length);

            Vector2 start = runtime.Position;
            world.Entities.Step(SimClock.StepsIn(1f), world.Air);

            Assert.AreEqual(4f, Vector2.Distance(start, runtime.Position), 0.05f,
                            "a 4 m/s saw should be 4 m along after a second");
        }

        [Test]
        public void APathlessEntityIsStationary()
        {
            // The state every entity placed before the path tool existed was in.
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));

            var world = new SimWorld();
            LevelLoader.Apply(session.Flush(), world, out _, out _);

            SimEntity runtime = world.Entities.Find(saw.id);
            world.Entities.Step(SimClock.StepsIn(3f), world.Air);

            Assert.AreEqual(new Vector2(6f, 9f), runtime.Position);
        }

        // ------------------------------------------------------------- properties

        [Test]
        public void ParamsRoundTripThroughTheSession()
        {
            EditorSession session = Session();
            LevelEntity lamp = session.PlaceEntity("lamp", new Vector2(10f, 15f));

            session.SetParam(lamp.id, EntityParamId.Duration, 1.5f);
            session.SetParam(lamp.id, EntityParamId.Length, 7f);

            Assert.AreEqual(1.5f, session.GetParam(lamp.id, EntityParamId.Duration), 1e-4f);
            Assert.AreEqual(1.5f, lamp.@params.duration, 1e-4f, "the field the JSON key maps to");
            Assert.AreEqual(7f, lamp.@params.length, 1e-4f);
        }

        [Test]
        public void EditedParamsReachTheSim()
        {
            EditorSession session = Session();
            LevelEntity lamp = session.PlaceEntity("lamp", new Vector2(10f, 15f));
            session.SetParam(lamp.id, EntityParamId.Duration, 1.5f);

            var world = new SimWorld();
            LevelLoader.Apply(session.Flush(), world, out _, out _);

            Assert.AreEqual(1.5f, world.Entities.Find(lamp.id).Duration, 1e-4f);
        }

        [Test]
        public void InspectorFieldsMatchWhatTheTypeActuallyUses()
        {
            // EntityFields is what the inspector is built from. If it drifted from
            // EntityFactory, the panel would offer knobs that do nothing.
            CollectionAssert.Contains(EntityFields.For("saw"), EntityParamId.Speed);
            CollectionAssert.Contains(EntityFields.For("lamp"), EntityParamId.Duration);
            CollectionAssert.Contains(EntityFields.For("fan"), EntityParamId.Power);

            CollectionAssert.DoesNotContain(EntityFields.For("saw"), EntityParamId.Duration,
                                            "a saw has no exposure time");
            Assert.IsEmpty(EntityFields.For("prop_trampoline"), "a type with no behaviour has no knobs");
        }

        [Test]
        public void ZeroMeansUnset_AndTheInspectorCanSayWhatItWouldBe()
        {
            // JsonUtility writes every field, so untouched params arrive as 0. The panel shows
            // the fallback beside an empty field rather than pretending the value is zero.
            Assert.AreEqual(EntityDefaults.SawSpeed,
                            EntityFields.DefaultFor("saw", EntityParamId.Speed), 1e-4f);
            Assert.AreEqual(EntityDefaults.LampDuration,
                            EntityFields.DefaultFor("lamp", EntityParamId.Duration), 1e-4f);
        }

        [Test]
        public void RetypingKeepsPositionPathAndParams()
        {
            // Losing a tuned number to a mistyped click is worse than carrying a stale one
            // the inspector no longer shows.
            EditorSession session = Session();
            LevelEntity entity = Saw(session, new Vector2(6f, 9f));
            session.AddWaypoint(entity.id, new Vector2(14f, 9f));
            session.SetParam(entity.id, EntityParamId.Speed, 5f);

            session.SetEntityType(entity.id, "fan");

            Assert.AreEqual("fan", entity.type);
            Assert.AreEqual(6f, entity.pos[0], 1e-4f);
            Assert.AreEqual(1, session.WaypointCount(entity.id));
            Assert.AreEqual(5f, session.GetParam(entity.id, EntityParamId.Speed), 1e-4f);
        }

        [Test]
        public void PhaseWrapsRatherThanClamping()
        {
            // Phase is a fraction of a cycle, so 1.2 and 0.2 are the same trap.
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));

            session.SetPhase(saw.id, 1.25f);
            Assert.AreEqual(0.25f, saw.phase, 1e-4f);

            session.SetPhase(saw.id, -0.25f);
            Assert.AreEqual(0.75f, saw.phase, 1e-4f);
        }

        [Test]
        public void StartStateAndPathModeAreEditable()
        {
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));

            session.SetPathMode(saw.id, "loop");
            session.SetStartState(saw.id, "dormant");

            var world = new SimWorld();
            LevelLoader.Apply(session.Flush(), world, out _, out _);

            SimEntity runtime = world.Entities.Find(saw.id);
            Assert.AreEqual(PathMode.Loop, runtime.Mode);
            Assert.IsFalse(runtime.Active, "dormant should reach the sim (GDD 9.3)");
        }

        [Test]
        public void EveryEntityEditMarksTheLevelDirty()
        {
            // The status bar's asterisk is the only warning an author gets before closing.
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));
            session.MarkSaved();

            session.AddWaypoint(saw.id, new Vector2(14f, 9f));
            Assert.IsTrue(session.Dirty, "adding a waypoint");

            session.MarkSaved();
            session.SetParam(saw.id, EntityParamId.Speed, 2f);
            Assert.IsTrue(session.Dirty, "editing a param");

            session.MarkSaved();
            session.SetPathMode(saw.id, "once");
            Assert.IsTrue(session.Dirty, "changing path mode");
        }

        [Test]
        public void UndoRestoresAPath()
        {
            EditorSession session = Session();
            LevelEntity saw = Saw(session, new Vector2(6f, 9f));

            session.BeginEdit();
            session.AddWaypoint(saw.id, new Vector2(14f, 9f));
            Assert.AreEqual(1, session.WaypointCount(saw.id));

            Assert.IsTrue(session.UndoEdit());
            Assert.AreEqual(0, session.WaypointCount("e01"), "the path came back with the entity");
        }
    }
}
