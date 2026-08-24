using KeepyUppy.Sim.Entities;
using UnityEngine;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Turns <see cref="LevelData"/> into a configured <see cref="SimWorld"/>.
    ///
    /// A pure function of the data with no scene dependency, so the whole loading path is
    /// testable in EditMode — which matters, because this is the path the shipping game and
    /// the editor both take.
    /// </summary>
    public static class LevelLoader
    {
        /// <summary>
        /// Applies geometry and zones, and returns the spawns. Does not call Configure —
        /// the caller owns the input source and the seed.
        /// </summary>
        public static void Apply(LevelData level, SimWorld world,
                                 out Vector2 balloonSpawn, out Vector2 playerSpawn)
        {
            world.Grid.LoadRows(level.tiles ?? new string[0]);

            ApplyEntities(level, world);

            balloonSpawn = ToVector(level.spawn?.balloon, new Vector2(6f, 13f));
            playerSpawn = ToVector(level.spawn?.player, new Vector2(4f, 2f));

            world.Balloon.Radius = InflationBands.RadiusOf(ParseBand(level.spawn?.balloon_inflation));
            world.Exit = ToGate(level.exit);
        }

        /// <summary>
        /// Zones and entities, without touching geometry, spawns or the run.
        ///
        /// Split out for the editor: moving a saw should show up the moment it moves, the way
        /// painting a tile does, and reloading the whole level to achieve that would reset the
        /// balloon to its spawn on every drag.
        /// </summary>
        public static void ApplyEntities(LevelData level, SimWorld world)
        {
            LoadZones(level, world.Air);
            LoadEntities(level, world.Entities, world.Air);
            world.Entities.Step(world.Clock.Step, world.Air);
        }

        /// <summary>
        /// Air zones only. Heat, cold, kill, static and light zones are parsed and carried in
        /// the data but have no systems yet — they are deliberately not silently dropped.
        /// </summary>
        static void LoadZones(LevelData level, AirField air)
        {
            air.Clear();
            if (level.zones == null) return;

            foreach (LevelZone zone in level.zones)
            {
                if (zone == null || zone.type != "air") continue;

                float[] r = zone.rect;
                if (r == null || r.Length < 4) continue;

                air.Add(new AirZone(
                    zone.id,
                    new Rect(r[0], r[1], r[2], r[3]),
                    new Vector2(zone.@params?.vx ?? 0f, zone.@params?.vy ?? 0f),
                    zone.start_state != "off"));
            }
        }

        /// <summary>
        /// Entities, in file order. Order is load-bearing: it fixes the tick and kill order
        /// (GDD 6.1), so a re-ordered file is a different — but still deterministic — level.
        ///
        /// Fans are added after the zone pass so their air zones sit at the end of the field,
        /// where the entity field can keep republishing them as they move.
        /// </summary>
        static void LoadEntities(LevelData level, EntityField entities, AirField air)
        {
            entities.Clear();
            if (level.entities == null) return;

            foreach (LevelEntity data in level.entities)
            {
                SimEntity entity = EntityFactory.Create(data);
                if (entity != null) entities.Add(entity, air);
            }
        }

        /// <summary>The way out (GDD 7.4). A level without one is a sandbox, not an error.</summary>
        public static ExitGate ToGate(LevelExit exit)
        {
            if (exit?.pos == null || exit.pos.Length < 2 || exit.diameter <= 0f) return ExitGate.None;

            return new ExitGate
            {
                Present = true,
                Position = new Vector2(exit.pos[0], exit.pos[1]),
                Shape = exit.shape == "slot" ? ExitShape.Slot : ExitShape.Ring,
                Diameter = exit.diameter,
            };
        }

        public static InflationBand ParseBand(string band)
        {
            switch (band)
            {
                case "shrivelled": return InflationBand.Shrivelled;
                case "slack":      return InflationBand.Slack;
                case "full":       return InflationBand.Full;
                case "critical":   return InflationBand.Critical;
                default:           return InflationBand.Nominal;
            }
        }

        public static string BandName(InflationBand band) => band.ToString().ToLowerInvariant();

        static Vector2 ToVector(float[] pair, Vector2 fallback)
            => pair != null && pair.Length >= 2 ? new Vector2(pair[0], pair[1]) : fallback;

        /// <summary>Captures a world's geometry back into level data. Used by the editor's save.</summary>
        public static void CaptureGeometry(SimWorld world, LevelData level)
        {
            level.tiles = world.Grid.ToRows();
        }
    }
}
