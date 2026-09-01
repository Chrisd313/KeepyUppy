using System.Collections.Generic;
using KeepyUppy.Sim.Triggers;
using UnityEngine;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Level lint (level-editor-spec 7). Cheap to write, saves hours.
    ///
    /// **Emits warnings, never blocks a save.** A half-finished level must always be
    /// saveable — the lint is there to catch the mistakes that waste a playtest, not to
    /// argue with the author.
    ///
    /// Deliberately does NOT attempt solvability checking: "it's a research project and it
    /// will be wrong."
    /// </summary>
    public static class LevelValidator
    {
        public static List<string> Validate(LevelData level)
        {
            var warnings = new List<string>();
            if (level == null) { warnings.Add("level is null"); return warnings; }

            ValidateGeometry(level, warnings);
            ValidateSpawns(level, warnings);
            ValidateExit(level, warnings);
            ValidateIds(level, warnings);
            ValidateEntities(level, warnings);
            ValidateTriggers(level, warnings);
            ValidatePanels(level, warnings);
            ValidateMeta(level, warnings);

            return warnings;
        }

        static void ValidateGeometry(LevelData level, List<string> warnings)
        {
            if (level.tiles == null || level.tiles.Length == 0)
            {
                warnings.Add("no tile rows");
                return;
            }

            if (level.tiles.Length != TileGrid.Height)
                warnings.Add($"expected {TileGrid.Height} tile rows, found {level.tiles.Length}");

            for (int i = 0; i < level.tiles.Length; i++)
            {
                string row = level.tiles[i];
                if (row == null) { warnings.Add($"tile row {i} is null"); continue; }
                if (row.Length != TileGrid.Width)
                    warnings.Add($"tile row {i} is {row.Length} chars, expected {TileGrid.Width}");
            }
        }

        static void ValidateSpawns(LevelData level, List<string> warnings)
        {
            if (level.spawn == null) { warnings.Add("no spawn block"); return; }

            CheckPoint(level.spawn.player, "player spawn", warnings);
            CheckPoint(level.spawn.balloon, "balloon spawn", warnings);

            if (level.spawn.balloon == null || level.spawn.balloon.Length < 2) return;

            // A balloon spawned inside geometry or on a hazard dies before the player moves.
            var grid = new TileGrid();
            grid.LoadRows(level.tiles ?? new string[0]);

            var pos = new Vector2(level.spawn.balloon[0], level.spawn.balloon[1]);
            float radius = InflationBands.RadiusOf(LevelLoader.ParseBand(level.spawn.balloon_inflation));

            if (OverlapsBlocking(grid, pos, radius))
                warnings.Add("balloon spawn overlaps solid geometry");

            if (Hazards.BalloonOverlapsLethal(grid, pos, radius, 0f))
                warnings.Add("balloon spawn overlaps a hazard");
        }

        static bool OverlapsBlocking(TileGrid grid, Vector2 centre, float radius)
        {
            int x0 = Mathf.FloorToInt(centre.x - radius);
            int x1 = Mathf.FloorToInt(centre.x + radius);
            int y0 = Mathf.FloorToInt(centre.y - radius);
            int y1 = Mathf.FloorToInt(centre.y + radius);

            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
            {
                if (!grid.IsBlockingBalloon(tx, ty)) continue;

                float qx = Mathf.Clamp(centre.x, tx, tx + 1);
                float qy = Mathf.Clamp(centre.y, ty, ty + 1);
                float dx = centre.x - qx;
                float dy = centre.y - qy;
                if (dx * dx + dy * dy < radius * radius) return true;
            }

            return false;
        }

        static void ValidateExit(LevelData level, List<string> warnings)
        {
            if (level.exit == null) { warnings.Add("no exit"); return; }

            CheckPoint(level.exit.pos, "exit", warnings);

            if (level.exit.diameter <= 0f)
                warnings.Add("exit diameter must be positive");

            if (level.exit.shape != "ring" && level.exit.shape != "slot")
                warnings.Add($"unknown exit shape '{level.exit.shape}' (expected ring or slot)");

            ExitGate gate = LevelLoader.ToGate(level.exit);
            if (!gate.Present) return;

            // The one lint that catches an unwinnable level. Inflation is not implemented, so
            // the balloon is whatever size it spawned at for the whole room: a gate narrower
            // than that can never be taken, and nothing in play would tell the author why.
            InflationBand band = LevelLoader.ParseBand(level.spawn?.balloon_inflation);
            float radius = InflationBands.RadiusOf(band);

            if (!gate.Fits(radius))
            {
                warnings.Add($"exit is {gate.Diameter:0.##} m but the balloon spawns {band} " +
                             $"({radius * 2f:0.##} m across) — unwinnable until inflation exists");
            }

            // required_band is the band the level asks the player to ARRIVE at, which is not
            // the same as the largest that fits: GDD 7.4's ceiling ring demands Full or
            // Critical because of where it is, not how wide it is, and no diameter can say
            // that. So only a physical contradiction is worth a warning — a stated band that
            // cannot pass through the hole at all.
            if (!string.IsNullOrEmpty(level.exit.required_band))
            {
                InflationBand stated = LevelLoader.ParseBand(level.exit.required_band);
                if (!gate.Fits(InflationBands.RadiusOf(stated)))
                {
                    warnings.Add($"exit required_band '{level.exit.required_band}' cannot fit " +
                                 $"through a {gate.Diameter:0.##} m aperture");
                }
            }

            if (OverlapsSolid(level, gate))
                warnings.Add("exit is inside solid geometry");
        }

        /// <summary>Whether any tile under the gate's aperture is solid.</summary>
        static bool OverlapsSolid(LevelData level, ExitGate gate)
        {
            if (level.tiles == null) return false;

            var grid = new TileGrid();
            grid.LoadRows(level.tiles);

            Vector2 half = gate.Size * 0.5f;

            int x0 = Mathf.FloorToInt(gate.Position.x - half.x);
            int x1 = Mathf.FloorToInt(gate.Position.x + half.x);
            int y0 = Mathf.FloorToInt(gate.Position.y - half.y);
            int y1 = Mathf.FloorToInt(gate.Position.y + half.y);

            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (grid.IsBlockingBalloon(x, y)) return true;

            return false;
        }

        static void ValidateIds(LevelData level, List<string> warnings)
        {
            var ids = new HashSet<string>();
            var duplicates = new List<string>();

            void Register(string id, string what)
            {
                if (string.IsNullOrEmpty(id)) { warnings.Add($"{what} has no id"); return; }
                if (!ids.Add(id)) duplicates.Add(id);
            }

            if (level.entities != null) foreach (var e in level.entities) Register(e?.id, "entity");
            if (level.zones != null) foreach (var z in level.zones) Register(z?.id, "zone");
            if (level.panels != null) foreach (var p in level.panels) Register(p?.id, "panel");
            if (level.triggers != null) foreach (var t in level.triggers) Register(t?.id, "trigger");

            // Sorted so the warning list is stable between runs.
            duplicates.Sort(System.StringComparer.Ordinal);
            foreach (string id in duplicates) warnings.Add($"duplicate id '{id}'");

            if (level.links == null) return;

            foreach (LevelLink link in level.links)
            {
                if (link == null) continue;

                if (!string.IsNullOrEmpty(link.from) && !ids.Contains(link.from))
                    warnings.Add($"link references missing id '{link.from}'");

                if (link.to == null) continue;
                foreach (string target in link.to)
                {
                    if (!ids.Contains(target))
                        warnings.Add($"link targets missing id '{target}'");
                }
            }
        }

        static void ValidateEntities(LevelData level, List<string> warnings)
        {
            if (level.entities == null) return;

            foreach (LevelEntity entity in level.entities)
            {
                if (entity == null) continue;

                CheckPoint(entity.pos, $"entity '{entity.id}'", warnings);

                if (string.IsNullOrEmpty(entity.type))
                    warnings.Add($"entity '{entity.id}' has no type");

                if (entity.path == null || entity.path.Length == 0) continue;

                if (entity.path.Length % 2 != 0)
                    warnings.Add($"entity '{entity.id}' path is not a flat list of x,y pairs");
                else if (entity.path.Length < 4)
                    warnings.Add($"entity '{entity.id}' has a path with fewer than 2 waypoints");
            }
        }

        /// <summary>
        /// Triggers and links (GDD 9.3). Duplicate ids and links naming a missing id are
        /// already covered by ValidateIds — this is everything else.
        /// </summary>
        static void ValidateTriggers(LevelData level, List<string> warnings)
        {
            if (level.triggers != null)
            {
                foreach (LevelTrigger trigger in level.triggers)
                {
                    if (trigger == null) continue;

                    TriggerKind kind = TriggerFactory.ParseKind(trigger.type);

                    if (kind == TriggerKind.Unknown)
                        warnings.Add($"trigger '{trigger.id}' type '{trigger.type}' is parsed but not simulated");

                    if (kind == TriggerKind.Timer && trigger.seconds <= 0f)
                        warnings.Add($"timer trigger '{trigger.id}' has no seconds — it can never fire");

                    bool region = kind == TriggerKind.PlayerRegion || kind == TriggerKind.BalloonRegion;
                    float[] r = trigger.rect;

                    if (region && (r == null || r.Length < 4 || r[2] <= 0f || r[3] <= 0f))
                        warnings.Add($"trigger '{trigger.id}' has a zero-area rect");

                    if (!LinkedFrom(level, trigger.id))
                        warnings.Add($"nothing links from trigger '{trigger.id}'");
                }

                ReportDuplicateRects(level, warnings);
            }

            if (level.links == null) return;

            foreach (LevelLink link in level.links)
            {
                if (link == null) continue;

                if (TriggerFactory.ParseAction(link.action) == LinkAction.Inert)
                    warnings.Add($"link action '{link.action}' is parsed but not simulated");
            }
        }

        static bool LinkedFrom(LevelData level, string id)
        {
            if (level.links == null || string.IsNullOrEmpty(id)) return false;

            foreach (LevelLink link in level.links)
                if (link != null && link.from == id) return true;

            return false;
        }

        /// <summary>
        /// Two once:true triggers on the same rect: almost always a duplication mistake
        /// (level-editor-spec 7). Compared pairwise — a room has a handful of triggers, and a
        /// dictionary keyed on a float rect would be worse in every way.
        /// </summary>
        static void ReportDuplicateRects(LevelData level, List<string> warnings)
        {
            LevelTrigger[] triggers = level.triggers;

            for (int i = 0; i < triggers.Length; i++)
            for (int j = i + 1; j < triggers.Length; j++)
            {
                LevelTrigger a = triggers[i];
                LevelTrigger b = triggers[j];

                if (a == null || b == null || !a.once || !b.once) continue;
                if (a.rect == null || b.rect == null || a.rect.Length < 4 || b.rect.Length < 4) continue;

                bool same = true;
                for (int k = 0; k < 4; k++)
                    if (!Mathf.Approximately(a.rect[k], b.rect[k])) { same = false; break; }

                if (same)
                    warnings.Add($"triggers '{a.id}' and '{b.id}' have identical rects");
            }
        }

        /// <summary>
        /// Panels (tile-panels design). Duplicate ids and links naming a missing panel are
        /// already covered by ValidateIds; this is everything else.
        /// </summary>
        static void ValidatePanels(LevelData level, List<string> warnings)
        {
            if (level.panels == null || level.panels.Length == 0) return;

            var grid = new TileGrid();
            grid.LoadRows(level.tiles ?? new string[0]);

            foreach (LevelPanel panel in level.panels)
            {
                if (panel == null) continue;

                float[] r = panel.rect;

                if (r == null || r.Length < 4 || r[2] <= 0f || r[3] <= 0f)
                {
                    warnings.Add($"panel '{panel.id}' has a zero-area rect");
                    continue;
                }

                if (!LinkedTo(level, panel.id))
                    warnings.Add($"nothing links to panel '{panel.id}'");

                if (!CoversATile(grid, r))
                    warnings.Add($"panel '{panel.id}' covers no tiles — it would hide nothing");
            }
        }

        static bool CoversATile(TileGrid grid, float[] rect)
        {
            int x0 = Mathf.RoundToInt(rect[0]);
            int y0 = Mathf.RoundToInt(rect[1]);
            int x1 = x0 + Mathf.RoundToInt(rect[2]);
            int y1 = y0 + Mathf.RoundToInt(rect[3]);

            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (TileGrid.InBounds(x, y) && grid[x, y] != TileKind.Empty) return true;

            return false;
        }

        /// <summary>Whether any link names this id as a target. A panel is never a source.</summary>
        static bool LinkedTo(LevelData level, string id)
        {
            if (level.links == null || string.IsNullOrEmpty(id)) return false;

            foreach (LevelLink link in level.links)
            {
                if (link?.to == null) continue;

                foreach (string target in link.to)
                    if (target == id) return true;
            }

            return false;
        }

        static void ValidateMeta(LevelData level, List<string> warnings)
        {
            if (level.meta == null) { warnings.Add("no meta block"); return; }

            if (level.meta.par_touches <= 0)
                warnings.Add("par_touches unset");
        }

        static void CheckPoint(float[] point, string what, List<string> warnings)
        {
            if (point == null || point.Length < 2) { warnings.Add($"{what} has no position"); return; }

            if (point[0] < 0f || point[0] > TileGrid.Width || point[1] < 0f || point[1] > TileGrid.Height)
                warnings.Add($"{what} is outside the room");
        }
    }
}
