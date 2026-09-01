using System.Collections.Generic;
using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Placeholder rendering for visible triggers (GDD 9.3).
    ///
    /// Only triggers with visible:true are drawn. The invisible ones are the point of the
    /// betrayal trap — they are drawn in the editor, and nowhere else — so this deliberately
    /// shows the player less than the editor does.
    ///
    /// A quad exists for every trigger, not only the visible ones, and Visible is applied as
    /// SetActive every frame rather than decided once at build time. Toggling visible in the
    /// inspector goes through SyncDynamic -> LevelLoader.ApplyEntities -> LoadTriggers, which
    /// clears and re-adds every trigger at the same count — so a build-time decision (a null
    /// hole for the invisible ones, as ZoneViewSync holes out fan-owned zones) would leave a
    /// newly-visible trigger undrawn and a newly-invisible one still drawn until something
    /// else changed the trigger count. Reading Visible per frame removes that drift instead
    /// of chasing it with a second rebuild trigger.
    ///
    /// Violet: not the reserved hazard hue and not balloon yellow (GDD 13.1). A trigger is
    /// not lethal, and wearing the colour that means "this hurts" would be a lie.
    /// </summary>
    public sealed class TriggerViewSync : MonoBehaviour
    {
        public SimWorld World;

        readonly List<GameObject> _views = new List<GameObject>();
        int _seenCount = -1;

        void LateUpdate()
        {
            if (World == null) return;

            if (World.Triggers.Count != _seenCount) Rebuild();

            for (int i = 0; i < World.Triggers.Count && i < _views.Count; i++)
            {
                GameObject view = _views[i];

                Rect rect = World.Triggers[i].Rect;

                // Followed every frame rather than only on a rebuild, because the editor moves
                // and resizes triggers while the level is loaded — the same reason ZoneViewSync
                // does it.
                view.transform.localPosition = new Vector3(rect.center.x, rect.center.y, -0.24f);
                view.transform.localScale = new Vector3(rect.width, rect.height, 1f);
                view.SetActive(World.Triggers[i].Visible);
            }
        }

        void Rebuild()
        {
            foreach (GameObject view in _views)
                if (view != null) Destroy(view);

            _views.Clear();

            for (int i = 0; i < World.Triggers.Count; i++)
            {
                Rect rect = World.Triggers[i].Rect;

                _views.Add(QuadView.Create(transform, $"trigger_{World.Triggers[i].Id}", Marked,
                                           rect.center, new Vector2(rect.width, rect.height), -0.24f));
            }

            _seenCount = World.Triggers.Count;
        }

        /// <summary>
        /// Faint, like a zone: it sits behind everything the player is tracking, and the
        /// balloon is the highest-contrast thing on screen, always (GDD 13.1).
        /// </summary>
        static readonly Color Marked = new Color(0.62f, 0.45f, 0.85f, 0.20f);
    }
}
