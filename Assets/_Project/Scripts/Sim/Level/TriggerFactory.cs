using KeepyUppy.Sim.Triggers;
using UnityEngine;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Turns schema triggers and links into runtime ones, mirroring <see cref="EntityFactory"/>.
    ///
    /// The only place a trigger type string becomes behaviour. An unrecognised type still
    /// produces a SimTrigger — an inert one — so the level round-trips intact and the editor
    /// can tell the author their file is newer than their build.
    /// </summary>
    public static class TriggerFactory
    {
        public static TriggerKind ParseKind(string type)
        {
            switch (type)
            {
                case "player_region":  return TriggerKind.PlayerRegion;
                case "balloon_region": return TriggerKind.BalloonRegion;
                case "timer":          return TriggerKind.Timer;
                default:               return TriggerKind.Unknown;
            }
        }

        public static string KindName(TriggerKind kind)
        {
            switch (kind)
            {
                case TriggerKind.PlayerRegion:  return "player_region";
                case TriggerKind.BalloonRegion: return "balloon_region";
                case TriggerKind.Timer:         return "timer";
                default:                        return "";
            }
        }

        /// <summary>arm and fire parse as Inert: declared by the schema, not simulated yet.</summary>
        public static LinkAction ParseAction(string action)
        {
            switch (action)
            {
                case "on":     return LinkAction.On;
                case "off":    return LinkAction.Off;
                case "toggle": return LinkAction.Toggle;
                default:       return LinkAction.Inert;
            }
        }

        public static SimTrigger Create(LevelTrigger data)
        {
            if (data == null) return null;

            float[] r = data.rect;

            return new SimTrigger
            {
                Id = data.id,
                Kind = ParseKind(data.type),
                Rect = r == null || r.Length < 4
                    ? new Rect(0f, 0f, 1f, 1f)
                    : new Rect(r[0], r[1], r[2], r[3]),
                Once = data.once,
                Visible = data.visible,
                AtStep = SimClock.StepsIn(data.seconds),
            };
        }

        /// <summary>
        /// Resolves a link against the triggers already loaded. A link whose source names no
        /// trigger returns null: the validator is where the author is told, and the sim is not
        /// the place to argue with a file.
        /// </summary>
        public static SimLink CreateLink(LevelLink data, TriggerField triggers)
        {
            if (data == null || triggers == null) return null;

            int from = triggers.IndexOf(data.from);
            if (from < 0) return null;

            return new SimLink
            {
                FromIndex = from,
                ToIds = data.to ?? new string[0],
                Action = ParseAction(data.action),
                DelaySteps = SimClock.StepsIn(data.delay),
            };
        }
    }
}
