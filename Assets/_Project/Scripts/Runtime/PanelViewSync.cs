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

        struct Debris
        {
            public GameObject view;
            public float velocity;
        }

        readonly List<Debris> _debris = new List<Debris>();

        /// <summary>
        /// Whether each cell was masked as of last frame. Sized and indexed the same way as
        /// TileGrid itself. The edge lives here, per cell, rather than per panel: with
        /// overlapping panels, asking "which panel hid this cell" has to reason about what
        /// every other panel did and when, and that reasoning has been wrong three times in a
        /// row. A cell that went visible-to-hidden between two frames is exactly the definition
        /// of a tile that just fell, whatever combination of panels caused it — no other panel
        /// needs to be consulted.
        /// </summary>
        readonly bool[] _wasMasked = new bool[TileGrid.Width * TileGrid.Height];
        bool _seeded;

        int _lastStep;

        /// <summary>Metres per second squared. The player's own gravity, so it looks of a piece.</summary>
        const float Gravity = 40f;

        void LateUpdate()
        {
            if (World == null) return;

            WatchForRestart();
            WatchMask();
            FallAndFade();
        }

        /// <summary>
        /// A restart clears the debris. Clock.Step running backwards is the signal, the same
        /// one StepInterpolator uses — tiles still falling through a floor that is back would
        /// be the room half-reset (GDD 8). The mask snapshot is reseeded in the same breath, so
        /// the tiles a restart puts back do not read as a fresh visible-to-hidden edge next
        /// frame.
        /// </summary>
        void WatchForRestart()
        {
            int step = World.Clock.Step;

            if (step < _lastStep)
            {
                foreach (Debris d in _debris) if (d.view != null) Destroy(d.view);
                _debris.Clear();
                _seeded = false;
            }

            _lastStep = step;
        }

        /// <summary>
        /// One pass over the whole room per frame. A cell that is masked now but was not last
        /// frame just fell, whatever caused it — a single panel, two overlapping panels
        /// switching off on the same step, or a panel that starts the level already off (the
        /// first pass seeds from the live mask instead of comparing, so that case drops
        /// nothing). 576 cells is nothing to scan once per render frame.
        /// </summary>
        void WatchMask()
        {
            for (int y = 0; y < TileGrid.Height; y++)
            for (int x = 0; x < TileGrid.Width; x++)
            {
                int i = y * TileGrid.Width + x;
                bool masked = World.Grid.IsMasked(x, y);

                if (_seeded && masked && !_wasMasked[i])
                {
                    TileKind kind = World.Grid.BaseAt(x, y);
                    if (kind != TileKind.Empty) Spawn(x, y, kind);
                }

                _wasMasked[i] = masked;
            }

            _seeded = true;
        }

        /// <summary>One debris quad for a tile a panel just hid.</summary>
        void Spawn(int x, int y, TileKind kind)
        {
            Color colour = kind == TileKind.Solid ? SimPalette.Solid : SimPalette.Hazard;

            _debris.Add(new Debris
            {
                view = QuadView.Create(transform, $"debris_{x}_{y}", colour,
                                       new Vector2(x + 0.5f, y + 0.5f), Vector2.one, -0.1f),
                velocity = 0f,
            });
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
