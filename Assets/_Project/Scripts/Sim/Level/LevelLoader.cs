using KeepyUppy.InputLayer;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Panels;
using KeepyUppy.Sim.Triggers;
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
        /// <summary>Where a level that declares no spawn block puts the balloon.</summary>
        public static readonly Vector2 DefaultBalloonSpawn = new Vector2(6f, 13f);

        /// <summary>Where a level that declares no spawn block puts the player.</summary>
        public static readonly Vector2 DefaultPlayerSpawn = new Vector2(4f, 2f);

        public static void Apply(LevelData level, SimWorld world,
                                 out Vector2 balloonSpawn, out Vector2 playerSpawn)
        {
            world.Grid.LoadRows(level.tiles ?? new string[0]);

            ApplyEntities(level, world);

            balloonSpawn = ToVector(level.spawn?.balloon, DefaultBalloonSpawn);
            playerSpawn = ToVector(level.spawn?.player, DefaultPlayerSpawn);

            world.Balloon.Radius = InflationBands.RadiusOf(ParseBand(level.spawn?.balloon_inflation));
            world.Exit = ToGate(level.exit);
        }

        /// <summary>
        /// Applies the level and starts the run in one call: the sequence both SimBootstrap's
        /// boot and the shell's "confirm a level" path need, so it lives here once instead of
        /// being retyped at each call site.
        /// </summary>
        public static void ApplyAndConfigure(LevelData level, SimWorld world, IInputSource input, int seed)
        {
            Apply(level, world, out Vector2 balloonSpawn, out Vector2 playerSpawn);
            world.Configure(balloonSpawn, Vector2.zero, playerSpawn, input, seed);
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
            LoadPanels(level, world.Panels);
            world.Panels.Apply(world.Grid);
            LoadTriggers(level, world.Triggers);
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
        /// Panels, in file order. A panel's rect is rounded to whole tiles: the mask indexes
        /// cells, so half a tile is not a thing that can be hidden.
        /// </summary>
        static void LoadPanels(LevelData level, PanelField panels)
        {
            panels.Clear();
            if (level.panels == null) return;

            foreach (LevelPanel data in level.panels)
            {
                if (data == null) continue;

                float[] r = data.rect;
                if (r == null || r.Length < 4) continue;

                bool on = data.start_state != "off";

                panels.Add(new SimPanel
                {
                    Id = data.id,
                    Rect = new RectInt(Mathf.RoundToInt(r[0]), Mathf.RoundToInt(r[1]),
                                       Mathf.RoundToInt(r[2]), Mathf.RoundToInt(r[3])),
                    ExitDir = PanelDirections.Parse(data.exit_dir),
                    Enabled = on,
                    StartEnabled = on,
                });
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

        /// <summary>
        /// Triggers and the links that fan out from them (GDD 9.3).
        ///
        /// Triggers first, in file order, so a link can resolve its source to an index rather
        /// than carrying an id into the tick. File order fixes the firing order, so a
        /// re-ordered file is a different — but still deterministic — level, exactly as it is
        /// for entities.
        /// </summary>
        static void LoadTriggers(LevelData level, TriggerField triggers)
        {
            triggers.Clear();
            if (level.triggers != null)
            {
                foreach (LevelTrigger data in level.triggers)
                    triggers.Add(TriggerFactory.Create(data));
            }

            if (level.links == null) return;

            foreach (LevelLink data in level.links)
                triggers.AddLink(TriggerFactory.CreateLink(data, triggers));
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
