#if LEVEL_EDITOR
using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// The selected panel's fields (tile-panels design).
    ///
    /// Its own file because LevelEditorController is already long. IMGUI and untested, the
    /// same boundary ZoneInspector and TriggerInspector sit on.
    /// </summary>
    public static class PanelInspector
    {
        public static bool Draw(EditorSession session, Dictionary<string, string> fieldText)
        {
            LevelPanel panel = session.FindPanel(session.SelectedPanelId);
            if (panel == null) return false;

            bool changed = false;

            GUILayout.Label($"panel {panel.id}");
            GUILayout.Label("tiles inside this rect can be switched by a link");

            Rect rect = ZoneGeometry.RectOf(panel.rect);
            float x = Row("x", $"{panel.id}.x", rect.xMin, fieldText);
            float y = Row("y", $"{panel.id}.y", rect.yMin, fieldText);
            float w = Row("w", $"{panel.id}.w", rect.width, fieldText);
            float h = Row("h", $"{panel.id}.h", rect.height, fieldText);

            var typed = ZoneGeometry.Snap(new Rect(x, y, Mathf.Max(w, 1f), Mathf.Max(h, 1f)));
            if (typed != rect)
            {
                session.BeginEdit();
                session.SetPanelRect(panel.id, typed);
                changed = true;
            }

            bool on = panel.start_state != "off";
            bool nowOn = GUILayout.Toggle(on, on ? "start: tiles present" : "start: tiles hidden");
            if (nowOn != on)
            {
                session.BeginEdit();
                session.SetPanelEnabled(panel.id, nowOn);
                changed = true;
            }

            if (!nowOn)
                GUILayout.Label("   (hidden at load — an appearing platform)");

            LevelLink link = null;
            if (session.Level.links != null)
            {
                foreach (LevelLink candidate in session.Level.links)
                {
                    if (candidate?.to == null) continue;

                    foreach (string target in candidate.to)
                        if (target == panel.id) link = candidate;
                }
            }

            GUILayout.Label(link == null
                ? "no link — use tool 6 from a trigger"
                : $"switched by {link.from} ({link.action})");

            if (GUILayout.Button("Delete panel", GUILayout.Width(110f)))
            {
                session.BeginEdit();
                session.DeletePanel(panel.id);
                changed = true;
            }

            return changed;
        }

        /// <summary>A labelled number box, through DevNumber as every other panel field is.</summary>
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
