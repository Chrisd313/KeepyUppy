#if LEVEL_EDITOR
using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// The selected zone's fields (level-editor-spec 4).
    ///
    /// Its own file because LevelEditorController is already long. IMGUI and untested, the
    /// same boundary every other dev panel sits on — the rules it edits live in EditorSession
    /// and ZoneGeometry, which are.
    /// </summary>
    public static class ZoneInspector
    {
        /// <summary>Every type the schema declares. Only air does anything (GDD 6.2).</summary>
        static readonly string[] Types = { "air", "heat", "cold", "kill", "static", "light" };

        /// <summary>
        /// Draws the panel. Returns true if anything changed, so the caller can push the edit
        /// into the running world.
        /// </summary>
        public static bool Draw(EditorSession session, Dictionary<string, string> fieldText)
        {
            LevelZone zone = session.FindZone(session.SelectedZoneId);
            if (zone == null) return false;

            bool changed = false;

            GUILayout.Label($"zone {zone.id}");

            GUILayout.BeginHorizontal();
            GUILayout.Label("type", GUILayout.Width(70f));

            foreach (string type in Types)
            {
                bool active = zone.type == type;
                if (GUILayout.Toggle(active, type, "Button", GUILayout.Width(52f)) == active) continue;

                zone.type = type;
                changed = true;
            }

            GUILayout.EndHorizontal();

            // Only air is read by LevelLoader.LoadZones. Saying so here is the whole
            // mitigation for authoring ahead of the sim: a heat zone parses, saves and draws,
            // and does nothing at all until inflation (GDD 7.2) exists.
            if (zone.type != "air")
                GUILayout.Label($"   ({zone.type} is inert — parsed and saved, no sim behaviour yet)");

            Rect rect = ZoneGeometry.RectOf(zone.rect);
            float x = Row("x", $"{zone.id}.x", rect.xMin, fieldText);
            float y = Row("y", $"{zone.id}.y", rect.yMin, fieldText);
            float w = Row("w", $"{zone.id}.w", rect.width, fieldText);
            float h = Row("h", $"{zone.id}.h", rect.height, fieldText);

            var typed = new Rect(x, y, Mathf.Max(w, 1f), Mathf.Max(h, 1f));
            if (typed != rect)
            {
                zone.rect = ZoneGeometry.ToArray(typed);
                changed = true;
            }

            if (zone.@params == null) zone.@params = new ZoneParams();

            if (zone.type == "air")
            {
                float vx = Row("vx", $"{zone.id}.vx", zone.@params.vx, fieldText);
                float vy = Row("vy", $"{zone.id}.vy", zone.@params.vy, fieldText);

                if (!Mathf.Approximately(vx, zone.@params.vx)) { zone.@params.vx = vx; changed = true; }
                if (!Mathf.Approximately(vy, zone.@params.vy)) { zone.@params.vy = vy; changed = true; }
            }
            else if (zone.type == "heat" || zone.type == "cold")
            {
                float rate = Row("rate", $"{zone.id}.rate", zone.@params.rate, fieldText);
                if (!Mathf.Approximately(rate, zone.@params.rate)) { zone.@params.rate = rate; changed = true; }
            }

            bool on = zone.start_state != "off";
            bool nowOn = GUILayout.Toggle(on, on ? "start: on" : "start: off");
            if (nowOn != on)
            {
                zone.start_state = nowOn ? "on" : "off";
                changed = true;
            }

            if (GUILayout.Button("Delete zone", GUILayout.Width(110f)))
            {
                session.BeginEdit();
                session.DeleteZone(zone.id);
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// A labelled number box. The typed text is held per field so a half-finished number
        /// is never snapped back mid-keystroke — DevNumber, shared with the tuning panel, so
        /// "2." and ".5" mean the same thing in both.
        /// </summary>
        static float Row(string label, string key, float value, Dictionary<string, string> fieldText)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(70f));

            string text = fieldText.TryGetValue(key, out string held) ? held : DevNumber.Format(value, false);
            string typed = GUILayout.TextField(text, GUILayout.Width(70f));

            if (typed != text) fieldText[key] = typed;

            GUILayout.EndHorizontal();

            return DevNumber.TryParse(typed, false, out float parsed) ? parsed : value;
        }
    }
}
#endif
