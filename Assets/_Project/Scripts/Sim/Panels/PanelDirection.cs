using UnityEngine;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>
    /// Which way a panel's tiles leave when it switches off — and the way they come back
    /// from when it switches on.
    ///
    /// Presentation only. The mask switches on one fixed step whichever value this holds, so
    /// nothing here can change what the simulation does; it exists on the sim side because
    /// the view reads the world, never the JSON.
    /// </summary>
    public enum PanelDirection
    {
        Down = 0,
        Up,
        Left,
        Right,
    }

    public static class PanelDirections
    {
        /// <summary>
        /// The schema's string, or <see cref="PanelDirection.Down"/> for anything else — a
        /// level written before the field existed fell, so an absent value must still fall.
        /// The validator warns about a value nothing can draw; the loader never refuses one.
        /// </summary>
        public static PanelDirection Parse(string value)
        {
            switch (value)
            {
                case "up":    return PanelDirection.Up;
                case "left":  return PanelDirection.Left;
                case "right": return PanelDirection.Right;
                default:      return PanelDirection.Down;
            }
        }

        /// <summary>The schema string for a direction. Round-trips <see cref="Parse"/>.</summary>
        public static string ToName(PanelDirection direction)
        {
            switch (direction)
            {
                case PanelDirection.Up:    return "up";
                case PanelDirection.Left:  return "left";
                case PanelDirection.Right: return "right";
                default:                   return "down";
            }
        }

        /// <summary>Whether the schema declares this string. What the validator lints against.</summary>
        public static bool IsKnown(string value)
            => value == "down" || value == "up" || value == "left" || value == "right";

        /// <summary>Unit vector, in tiles, pointing the way the tiles leave.</summary>
        public static Vector2Int Step(PanelDirection direction)
        {
            switch (direction)
            {
                case PanelDirection.Up:    return new Vector2Int(0, 1);
                case PanelDirection.Left:  return new Vector2Int(-1, 0);
                case PanelDirection.Right: return new Vector2Int(1, 0);
                default:                   return new Vector2Int(0, -1);
            }
        }
    }
}
