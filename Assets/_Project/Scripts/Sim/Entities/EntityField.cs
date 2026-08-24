using System.Collections.Generic;
using UnityEngine;

namespace KeepyUppy.Sim.Entities
{
    /// <summary>
    /// Every entity in the room, stepped and queried in a fixed order.
    ///
    /// A List, never a Dictionary or HashSet: iteration order feeds float accumulation and
    /// which hazard reports a kill first, and unordered iteration is on CLAUDE.md's list of
    /// the things that break GDD 6.1 silently.
    ///
    /// Fans are the reason this holds an AirField reference. A fan does not implement wind —
    /// it owns a zone in the one wind system (GDD 6.2), so a moving fan carries its zone with
    /// it and overlapping fans compose for free.
    /// </summary>
    public sealed class EntityField
    {
        readonly List<SimEntity> _entities = new List<SimEntity>();

        /// <summary>Parallel to _entities: the fan's index in the AirField, or -1.</summary>
        readonly List<int> _airZones = new List<int>();

        /// <summary>
        /// Types present in the level that this build has no behaviour for. Surfaced by the
        /// editor rather than swallowed — a level that silently loses hazards is worse than
        /// one that says it is too new.
        /// </summary>
        public int UnknownCount { get; private set; }

        public int Count => _entities.Count;
        public SimEntity this[int i] => _entities[i];

        public void Clear()
        {
            _entities.Clear();
            _airZones.Clear();
            UnknownCount = 0;
        }

        public void Add(SimEntity entity, AirField air)
        {
            if (entity == null) return;

            if (entity.Kind == EntityKind.Unknown) UnknownCount++;

            entity.Reset();
            _entities.Add(entity);

            if (entity.Kind == EntityKind.Fan && air != null)
            {
                _airZones.Add(air.Count);
                air.Add(new AirZone(entity.Id, entity.FanRect(), entity.FanVelocity(), entity.Active));
            }
            else
            {
                _airZones.Add(-1);
            }
        }

        /// <summary>GDD 8: part of the synchronous restart. No motion state to unwind.</summary>
        public void Reset()
        {
            for (int i = 0; i < _entities.Count; i++) _entities[i].Reset();
        }

        /// <summary>
        /// Moves everything for this step and republishes the fan zones. Called before the
        /// bodies are stepped, so a saw and the balloon it catches are contemporaries rather
        /// than a frame apart.
        /// </summary>
        public void Step(int step, AirField air)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                SimEntity entity = _entities[i];
                entity.Place(step);

                int zone = _airZones[i];
                if (zone < 0 || air == null || zone >= air.Count) continue;

                air[zone] = new AirZone(entity.Id, entity.FanRect(), entity.FanVelocity(), entity.Active);
            }
        }

        /// <summary>
        /// Whether the balloon dies to an entity this step, and ages every lamp's exposure.
        ///
        /// Exposure is deliberately *unbroken*: leaving the beam resets it. GDD 9.2 wants a
        /// lamp to force movement, and a countdown that remembers you would instead force
        /// leaving the room, which is a different and worse hazard.
        /// </summary>
        public bool ResolveBalloon(Vector2 centre, float radius, float shrink)
        {
            bool killed = false;

            for (int i = 0; i < _entities.Count; i++)
            {
                SimEntity entity = _entities[i];
                if (!entity.Active) continue;

                bool overlapping = entity.Kind != EntityKind.Fan &&
                                   entity.OverlapsCircle(centre, radius, shrink);

                if (entity.Kind == EntityKind.Lamp)
                {
                    entity.ExposureFrames = overlapping ? entity.ExposureFrames + 1 : 0;

                    if (overlapping && entity.ExposureFrames >= SimClock.StepsIn(entity.Duration))
                        killed = true;

                    continue;
                }

                if (overlapping && entity.KillsOnContact) killed = true;
            }

            // Every lamp is aged before returning, so one lamp killing does not freeze the
            // others mid-count. Restart clears them anyway; this keeps the step honest.
            return killed;
        }

        /// <summary>Whether the player dies to an entity this step (GDD 4.1: blades only).</summary>
        public bool ResolvePlayer(Vector2 centre, float halfWidth, float halfHeight, float shrink)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                SimEntity entity = _entities[i];
                if (!entity.Active || !entity.KillsPlayer) continue;

                if (entity.OverlapsBox(centre, halfWidth, halfHeight, shrink)) return true;
            }

            return false;
        }

        public SimEntity Find(string id)
        {
            for (int i = 0; i < _entities.Count; i++)
                if (_entities[i].Id == id) return _entities[i];

            return null;
        }

        /// <summary>Arms or disarms a dormant entity. The hook GDD 9.3 triggers will use.</summary>
        public void SetActive(string id, bool active)
        {
            SimEntity entity = Find(id);
            if (entity != null) entity.Active = active;
        }
    }
}
