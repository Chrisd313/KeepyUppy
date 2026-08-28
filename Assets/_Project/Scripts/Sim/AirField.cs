using System.Collections.Generic;
using UnityEngine;

namespace KeepyUppy.Sim
{
    /// <summary>
    /// A rectangular region that sets air velocity. Maps to a <c>type: "air"</c> zone in the
    /// level schema (level-editor-spec 3), whose params are vx / vy.
    /// </summary>
    public struct AirZone
    {
        public string id;

        /// <summary>Tile-aligned, in metres. x, y is the bottom-left corner.</summary>
        public Rect rect;

        /// <summary>v_air contributed inside the rect, m/s.</summary>
        public Vector2 vAir;

        /// <summary>Maps to the schema's start_state, and later to links/triggers.</summary>
        public bool enabled;

        /// <summary>What enabled returns to on a restart, for the same reason as SimEntity.StartActive.</summary>
        public bool startEnabled;

        public AirZone(string id, Rect rect, Vector2 vAir, bool enabled = true)
        {
            this.id = id;
            this.rect = rect;
            this.vAir = vAir;
            this.enabled = enabled;
            this.startEnabled = enabled;
        }
    }

    /// <summary>
    /// The one and only wind system (GDD 6.2).
    ///
    /// Fans, vents, thermals, gusts and the vacuum cleaner are all zones with a different
    /// v_air. They compose because the drag term already works against relative velocity,
    /// so overlapping zones sum and nothing needs per-hazard code.
    /// </summary>
    public sealed class AirField
    {
        readonly List<AirZone> _zones = new List<AirZone>();

        public int Count => _zones.Count;

        public AirZone this[int i]
        {
            get => _zones[i];
            set => _zones[i] = value;
        }

        public void Clear() => _zones.Clear();

        public void Add(AirZone zone) => _zones.Add(zone);

        public void SetEnabled(string id, bool enabled)
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                if (_zones[i].id != id) continue;
                AirZone z = _zones[i];
                z.enabled = enabled;
                _zones[i] = z;
            }
        }

        /// <summary>
        /// Puts every zone back to the state its level declared. Part of the synchronous
        /// restart (GDD 8): a link that switched a zone must not outlive the attempt.
        /// </summary>
        public void ResetStates()
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                AirZone zone = _zones[i];
                if (zone.enabled == zone.startEnabled) continue;

                zone.enabled = zone.startEnabled;
                _zones[i] = zone;
            }
        }

        /// <summary>
        /// v_air at a world position: the sum of every enabled zone containing it, always
        /// accumulated in list order so the float rounding is reproducible.
        /// </summary>
        public Vector2 Sample(Vector2 p)
        {
            float vx = 0f;
            float vy = 0f;

            for (int i = 0; i < _zones.Count; i++)
            {
                AirZone z = _zones[i];
                if (!z.enabled) continue;
                if (!z.rect.Contains(p)) continue;
                vx += z.vAir.x;
                vy += z.vAir.y;
            }

            return new Vector2(vx, vy);
        }
    }
}
