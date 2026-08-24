using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Entities;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Placeholder rendering for moving hazards (GDD 13.3).
    ///
    /// Rebuilds when the entity count changes and follows positions every frame otherwise.
    /// Positions are read out of the sim and never written back — the view is a spectator.
    ///
    /// GDD 8 asymmetry is visible here on purpose: the quad is drawn at the entity's *visual*
    /// radius, while the kill volume is that minus hazard_pop_radius_shrink. What you see is
    /// slightly bigger than what kills you, which is the rule.
    /// </summary>
    public sealed class EntityViewSync : MonoBehaviour
    {
        public SimWorld World;

        readonly List<GameObject> _views = new List<GameObject>();
        int _seenCount = -1;

        void LateUpdate()
        {
            if (World == null) return;

            if (World.Entities.Count != _seenCount) Rebuild();

            for (int i = 0; i < World.Entities.Count && i < _views.Count; i++)
            {
                SimEntity entity = World.Entities[i];
                GameObject view = _views[i];

                // Do not pixel-snap: these are moving entities the player is tracking
                // (CLAUDE.md rendering note).
                Vector2 centre = Centre(entity);

                view.transform.localPosition = new Vector3(centre.x, centre.y, -0.5f);
                view.SetActive(entity.Active);
            }
        }

        void Rebuild()
        {
            foreach (GameObject view in _views) Destroy(view);
            _views.Clear();

            for (int i = 0; i < World.Entities.Count; i++)
            {
                SimEntity entity = World.Entities[i];
                _views.Add(QuadView.Create(transform, $"entity_{entity.Id}",
                                           Colour(entity.Kind), Centre(entity), Size(entity), -0.5f));
            }

            _seenCount = World.Entities.Count;
        }

        /// <summary>
        /// A saw is drawn on its position; a lamp beam and a fan's blast both extend *away*
        /// from theirs, so the quad centres on the middle of the volume, not its origin.
        /// </summary>
        static Vector2 Centre(SimEntity entity)
        {
            switch (entity.Kind)
            {
                case EntityKind.Lamp: return (entity.Position + entity.KillEnd) * 0.5f;
                case EntityKind.Fan:  return entity.FanRect().center;
                default:              return entity.Position;
            }
        }

        static Vector2 Size(SimEntity entity)
        {
            switch (entity.Kind)
            {
                case EntityKind.Lamp:
                case EntityKind.Fan:
                {
                    Vector2 dir = entity.Direction();
                    return Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
                        ? new Vector2(entity.Length, entity.Radius * 2f)
                        : new Vector2(entity.Radius * 2f, entity.Length);
                }

                default:
                    return Vector2.one * (entity.Radius * 2f);
            }
        }

        static Color Colour(EntityKind kind)
        {
            switch (kind)
            {
                // Reserved hazard colour (GDD 13.1) for the things that kill on contact.
                case EntityKind.Saw:  return SimPalette.Hazard;

                // A lamp kills over time, so it reads as a translucent field rather than a
                // solid object. Still the hazard hue: the reserved colour means "this hurts".
                case EntityKind.Lamp: return new Color(SimPalette.Hazard.r, SimPalette.Hazard.g,
                                                       SimPalette.Hazard.b, 0.35f);

                // A fan is not a hazard by itself, so it must not wear the hazard colour.
                case EntityKind.Fan:  return new Color(0.45f, 0.65f, 0.75f, 0.30f);

                default:              return new Color(1f, 0f, 1f, 0.5f);
            }
        }
    }
}
