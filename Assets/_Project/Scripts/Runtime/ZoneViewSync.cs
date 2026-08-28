using System.Collections.Generic;
using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Placeholder rendering for air zones (GDD 6.2).
    ///
    /// Zones used to be drawn by nothing at all, which made them the one part of a room that
    /// acted on the balloon while being completely invisible — in a level whose tiles are
    /// empty, an updraught reads as the balloon bouncing off a platform that is not there.
    /// A fan's blast was already drawn by <see cref="EntityViewSync"/>, so identical wind was
    /// visible or invisible purely according to which half of the level schema declared it.
    ///
    /// Zones authored in the level's own <c>zones</c> array are drawn here. A fan's zone is
    /// skipped, because the entity that owns it already draws it — see <see cref="OwnedByEntity"/>.
    ///
    /// Not a hazard colour (GDD 13.1): wind is not lethal, and the reserved hue means "this
    /// hurts". It borrows the fan's existing blue so that wind looks like wind wherever it
    /// comes from.
    /// </summary>
    public sealed class ZoneViewSync : MonoBehaviour
    {
        public SimWorld World;

        readonly List<GameObject> _views = new List<GameObject>();
        int _seenCount = -1;

        void LateUpdate()
        {
            if (World == null) return;

            if (World.Air.Count != _seenCount) Rebuild();

            for (int i = 0; i < World.Air.Count && i < _views.Count; i++)
            {
                GameObject view = _views[i];
                if (view == null) continue;   // a fan's zone: drawn by its entity instead

                AirZone zone = World.Air[i];

                // Followed every frame rather than only on a rebuild: a fan carries its zone
                // with it, and the editor moves and resizes zones while the level runs.
                view.transform.localPosition = new Vector3(zone.rect.center.x,
                                                           zone.rect.center.y, -0.25f);
                view.transform.localScale = new Vector3(zone.rect.width, zone.rect.height, 1f);
                view.SetActive(zone.enabled);
            }
        }

        void Rebuild()
        {
            foreach (GameObject view in _views)
                if (view != null) Destroy(view);

            _views.Clear();

            for (int i = 0; i < World.Air.Count; i++)
            {
                AirZone zone = World.Air[i];

                if (OwnedByEntity(zone.id))
                {
                    // Hold the slot so the list stays index-parallel with the AirField.
                    _views.Add(null);
                    continue;
                }

                _views.Add(QuadView.Create(transform, $"zone_{zone.id}", Air,
                                           zone.rect.center,
                                           new Vector2(zone.rect.width, zone.rect.height),
                                           -0.25f));
            }

            _seenCount = World.Air.Count;
        }

        /// <summary>
        /// A fan publishes its blast into the same AirField as a level-authored zone
        /// (EntityField holds the field for exactly that reason), and shares the entity's id.
        /// Drawing it here as well would double the quad on top of the one EntityViewSync
        /// already places.
        /// </summary>
        bool OwnedByEntity(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            for (int i = 0; i < World.Entities.Count; i++)
                if (World.Entities[i].Id == id) return true;

            return false;
        }

        /// <summary>
        /// Deliberately faint. A zone covers a large part of the room and sits behind
        /// everything the player is actually tracking, so it has to be readable without
        /// competing with the balloon (GDD 13.1: the balloon is the highest-contrast thing
        /// on screen, always).
        /// </summary>
        static readonly Color Air = new Color(0.45f, 0.65f, 0.75f, 0.22f);
    }
}
