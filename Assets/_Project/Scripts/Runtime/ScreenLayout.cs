using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Sits on an optional prefab at <c>Resources/Shell/&lt;AppScreen&gt;</c> and gives that
    /// prefab's authored anchors to <see cref="ShellView"/> in place of its hardcoded text
    /// positions. Every screen still works with no prefab at all — that is the fallback this
    /// component exists to make safe, not just possible.
    ///
    /// The anchor list is positional, not named: index 0 means whatever the drawing method
    /// documents it to mean (see the comment above each ShellView.Draw* method) and nothing
    /// else records that mapping, so a prefab author must follow those comments in order.
    ///
    /// A serialised <see cref="Transform"/> slot left empty in the inspector is a runtime
    /// null the EditMode suite has no way to see — there is no scene for it to load. So the
    /// only safe contract is: an unassigned or out-of-range anchor silently returns the
    /// caller's fallback rather than throwing, returning a default Vector2, or otherwise
    /// producing text stacked at the origin. A half-wired prefab must degrade one line at a
    /// time to the code layout, never go invisible.
    /// </summary>
    public sealed class ScreenLayout : MonoBehaviour
    {
        [SerializeField] Transform[] anchors = System.Array.Empty<Transform>();

        /// <summary>
        /// The world position of anchor <paramref name="index"/>, or <paramref name="fallback"/>
        /// if that slot doesn't exist or was never assigned in the inspector. Never throws and
        /// never fabricates a position — the fallback is always the caller's own hardcoded
        /// literal, so the worst case is "looks exactly like it did before this component
        /// existed", never "text at (0, 0)".
        /// </summary>
        public Vector2 AnchorOrDefault(int index, Vector2 fallback)
        {
            if (anchors == null || index < 0 || index >= anchors.Length) return fallback;

            Transform anchor = anchors[index];
            return anchor != null ? (Vector2)anchor.position : fallback;
        }
    }
}
