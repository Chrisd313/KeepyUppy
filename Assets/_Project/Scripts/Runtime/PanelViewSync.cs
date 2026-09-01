using System.Collections.Generic;
using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Debris for a floor a link has taken away (tile-panels design).
    ///
    /// A floor that blinks out of existence reads as a rendering glitch; a floor that falls
    /// reads as a trap. But a tile that falls *as a solid* is the moving-solid problem the
    /// project has deferred on design grounds, so this is presentation only: collision ended
    /// on the step the panel switched, and these quads are scenery that nothing can touch.
    ///
    /// Nothing here is readable by the simulation, so determinism is untouched — the same
    /// rule StepInterpolator and TimeControl already follow. That is also why this may use
    /// render time when no code under Sim/ ever may.
    /// </summary>
    public sealed class PanelViewSync : MonoBehaviour
    {
        public SimWorld World;

        /// <summary>Used only to notice a restart, never to drive the fall.</summary>
        public SimDriver Driver;

        struct Debris
        {
            public GameObject view;
            public float velocity;
        }

        readonly List<Debris> _debris = new List<Debris>();
        readonly List<bool> _wasEnabled = new List<bool>();
        int _lastStep;

        /// <summary>Metres per second squared. The player's own gravity, so it looks of a piece.</summary>
        const float Gravity = 40f;

        void LateUpdate()
        {
            if (World == null) return;

            WatchForRestart();
            WatchPanels();
            FallAndFade();
        }

        /// <summary>
        /// A restart clears the debris. Clock.Step running backwards is the signal, the same
        /// one StepInterpolator uses — tiles still falling through a floor that is back would
        /// be the room half-reset (GDD 8).
        /// </summary>
        void WatchForRestart()
        {
            int step = World.Clock.Step;

            if (step < _lastStep)
            {
                foreach (Debris d in _debris) if (d.view != null) Destroy(d.view);
                _debris.Clear();
            }

            _lastStep = step;
        }

        void WatchPanels()
        {
            while (_wasEnabled.Count < World.Panels.Count) _wasEnabled.Add(true);

            for (int i = 0; i < World.Panels.Count; i++)
            {
                bool enabled = World.Panels[i].Enabled;

                // The true-to-false edge is the moment the floor gives way.
                if (_wasEnabled[i] && !enabled) Spawn(World.Panels[i].Rect);

                _wasEnabled[i] = enabled;
            }
        }

        /// <summary>
        /// One quad per tile the panel just hid. Read from the level's rows rather than the
        /// live grid: the mask is already applied by the time this runs, so the live grid
        /// reports Empty for exactly the tiles we want to draw falling.
        /// </summary>
        void Spawn(RectInt rect)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                if (!TileGrid.InBounds(x, y)) continue;
                if (!World.Grid.IsMasked(x, y)) continue;

                TileKind kind = World.Grid.BaseAt(x, y);
                if (kind == TileKind.Empty) continue;

                Color colour = kind == TileKind.Solid ? SimPalette.Solid : SimPalette.Hazard;

                _debris.Add(new Debris
                {
                    view = QuadView.Create(transform, $"debris_{x}_{y}", colour,
                                           new Vector2(x + 0.5f, y + 0.5f), Vector2.one, -0.1f),
                    velocity = 0f,
                });
            }
        }

        void FallAndFade()
        {
            float dt = Time.deltaTime;

            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                Debris d = _debris[i];

                if (d.view == null) { _debris.RemoveAt(i); continue; }

                d.velocity -= Gravity * dt;

                Vector3 p = d.view.transform.localPosition;
                p.y += d.velocity * dt;
                d.view.transform.localPosition = p;

                if (p.y < -2f)
                {
                    Destroy(d.view);
                    _debris.RemoveAt(i);
                    continue;
                }

                _debris[i] = d;
            }
        }
    }
}
