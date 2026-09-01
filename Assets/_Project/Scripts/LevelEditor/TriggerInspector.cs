#if LEVEL_EDITOR
using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Level;
using KeepyUppy.Sim.Triggers;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// The selected trigger's fields and its outgoing links (level-editor-spec 4).
    ///
    /// Its own file because LevelEditorController is already long. IMGUI and untested, the
    /// same boundary ZoneInspector sits on — the rules it edits live in EditorSession, which
    /// is tested.
    /// </summary>
    public static class TriggerInspector
    {
        /// <summary>Every type the schema declares. Three of the seven are simulated.</summary>
        static readonly string[] Types =
        {
            "player_region", "balloon_region", "timer",
            "lever", "balloon_touches", "player_grounded", "on_first_entry",
        };

        static readonly string[] Actions = { "toggle", "on", "off", "arm", "fire" };

        /// <summary>Draws the panel. True if anything changed, so the caller can push the edit.</summary>
        public static bool Draw(EditorSession session, Dictionary<string, string> fieldText)
        {
            LevelTrigger trigger = session.FindTrigger(session.SelectedTriggerId);
            if (trigger == null) return false;

            bool changed = false;

            GUILayout.Label($"trigger {trigger.id}");

            foreach (string type in Types)
            {
                bool active = trigger.type == type;
                if (GUILayout.Toggle(active, type, "Button") == active) continue;

                session.BeginEdit();
                session.SetTriggerType(trigger.id, type);
                changed = true;
            }

            // Only three types are read by TriggerFactory. Saying so here is the whole
            // mitigation for authoring ahead of the sim, as it is for the inert zone types.
            if (TriggerFactory.ParseKind(trigger.type) == TriggerKind.Unknown)
                GUILayout.Label($"   ({trigger.type} is inert — parsed and saved, no sim behaviour yet)");

            Rect rect = ZoneGeometry.RectOf(trigger.rect);
            float x = Row("x", $"{trigger.id}.x", rect.xMin, fieldText);
            float y = Row("y", $"{trigger.id}.y", rect.yMin, fieldText);
            float w = Row("w", $"{trigger.id}.w", rect.width, fieldText);
            float h = Row("h", $"{trigger.id}.h", rect.height, fieldText);

            var typed = ZoneGeometry.Snap(new Rect(x, y, Mathf.Max(w, 1f), Mathf.Max(h, 1f)));
            if (typed != rect)
            {
                session.BeginEdit();
                session.SetTriggerRect(trigger.id, typed);
                changed = true;
            }

            if (trigger.type == "timer")
            {
                float seconds = Row("seconds", $"{trigger.id}.sec", trigger.seconds, fieldText);
                if (!Mathf.Approximately(seconds, trigger.seconds))
                {
                    session.BeginEdit();
                    session.SetTriggerSeconds(trigger.id, seconds);
                    changed = true;
                }
            }

            bool once = GUILayout.Toggle(trigger.once, trigger.once ? "once: yes" : "once: no");
            if (once != trigger.once)
            {
                session.BeginEdit();
                session.SetTriggerOnce(trigger.id, once);
                changed = true;
            }

            bool visible = GUILayout.Toggle(trigger.visible, trigger.visible ? "visible" : "invisible");
            if (visible != trigger.visible)
            {
                session.BeginEdit();
                session.SetTriggerVisible(trigger.id, visible);
                changed = true;
            }

            changed |= DrawLinks(session, trigger, fieldText);

            if (GUILayout.Button("Delete trigger", GUILayout.Width(110f)))
            {
                session.BeginEdit();
                session.DeleteTrigger(trigger.id);
                changed = true;
            }

            return changed;
        }

        static bool DrawLinks(EditorSession session, LevelTrigger trigger,
                              Dictionary<string, string> fieldText)
        {
            LevelLink link = session.LinksFrom(trigger.id);

            if (link == null)
            {
                GUILayout.Label("no links — use tool 6 to wire one");
                return false;
            }

            bool changed = false;

            GUILayout.Label("action");
            GUILayout.BeginHorizontal();

            foreach (string action in Actions)
            {
                bool active = link.action == action;
                if (GUILayout.Toggle(active, action, "Button", GUILayout.Width(46f)) == active) continue;

                session.BeginEdit();
                session.SetLinkAction(trigger.id, action);
                changed = true;
            }

            GUILayout.EndHorizontal();

            if (TriggerFactory.ParseAction(link.action) == LinkAction.Inert)
                GUILayout.Label($"   ({link.action} is inert — parsed and saved, no sim behaviour yet)");

            float delay = Row("delay s", $"{trigger.id}.delay", link.delay, fieldText);
            if (!Mathf.Approximately(delay, link.delay))
            {
                session.BeginEdit();
                session.SetLinkDelay(trigger.id, delay);
                changed = true;
            }

            foreach (string target in link.to ?? new string[0])
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"→ {target}", GUILayout.Width(120f));

                if (GUILayout.Button("x", GUILayout.Width(24f)))
                {
                    session.BeginEdit();
                    session.RemoveLink(trigger.id, target);
                    changed = true;
                    GUILayout.EndHorizontal();
                    break;
                }

                GUILayout.EndHorizontal();
            }

            return changed;
        }

        /// <summary>
        /// A labelled number box. The typed text is held per field so a half-finished number
        /// is never snapped back mid-keystroke — DevNumber, shared with the tuning panel.
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
