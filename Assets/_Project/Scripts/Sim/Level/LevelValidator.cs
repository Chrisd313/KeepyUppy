using System.Collections.Generic;
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
