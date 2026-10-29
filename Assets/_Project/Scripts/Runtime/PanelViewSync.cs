using System.Collections.Generic;
using KeepyUppy.Sim;
using KeepyUppy.Sim.Panels;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Debris for a floor a link has taken away (tile-panels design).
    ///
    /// A floor that blinks out of existence reads as a rendering glitch; a floor that falls
    /// reads as a trap. Which way it goes is the panel's exit_dir, so a wall can retract
    /// upward out of the room instead of dropping. But a tile that moves *as a solid* is the
    /// moving-solid problem the project has deferred on design grounds, so this is
    /// presentation only: collision ended on the step the panel switched, and these quads are
    /// scenery that nothing can touch.
    ///
    /// The return is the same idea inverted: a tile coming back gets a quad sliding in from
    /// the exit direction. That slide is deliberately shorter than the fall, because the real
    /// tile is solid and lethal from the switching step — anything long enough to read as
    /// travel would be showing empty space where a solid already is (GDD 8's crush).
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
            public Vector2 direction;
            public float speed;
        }

        readonly List<Debris> _debris = new List<Debris>();

        /// <summary>A tile sliding back into a cell its panel just gave back.</summary>
        struct Arrival
        {
            public GameObject view;
            public Vector2 from;
            public Vector2 to;
            public float elapsed;
        }

        readonly List<Arrival> _arrivals = new List<Arrival>();

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

        /// <summary>
        /// Seconds a returning tile takes to slide in. 0.1 s — six fixed steps — is short
        /// enough that the gap between what is solid and what is drawn cannot be acted on,
        /// and long enough to read as a slam rather than a pop.
        /// </summary>
        const float ArrivalTime = 0.1f;

        void LateUpdate()
        {
            if (World == null) return;

            WatchForRestart();
            WatchMask();
            FallAndFade();
            SlideIn();
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

                foreach (Arrival a in _arrivals) if (a.view != null) Destroy(a.view);
                _arrivals.Clear();

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

                if (_seeded && masked != _wasMasked[i])
                {
                    TileKind kind = World.Grid.BaseAt(x, y);

                    if (kind != TileKind.Empty)
                    {
                        if (masked) Spawn(x, y, kind);
                        else Arrive(x, y, kind);
                    }
                }

                _wasMasked[i] = masked;
            }

            _seeded = true;
        }

        /// <summary>One debris quad for a tile a panel just hid, leaving the way its panel says.</summary>
        void Spawn(int x, int y, TileKind kind)
        {
            Vector2Int step = PanelDirections.Step(DirectionAt(x, y, wantHidden: true));

            _debris.Add(new Debris
            {
                view = QuadView.Create(transform, $"debris_{x}_{y}", SimPalette.ForTile(kind),
                                       new Vector2(x + 0.5f, y + 0.5f), Vector2.one, -0.1f),
                direction = new Vector2(step.x, step.y),
                speed = 0f,
            });
        }

        /// <summary>One quad sliding into a cell a panel just gave back, from where it left.</summary>
        void Arrive(int x, int y, TileKind kind)
        {
            Vector2Int step = PanelDirections.Step(DirectionAt(x, y, wantHidden: false));

            var to = new Vector2(x + 0.5f, y + 0.5f);
            Vector2 from = to + new Vector2(step.x, step.y);

            _arrivals.Add(new Arrival
            {
                view = QuadView.Create(transform, $"arrival_{x}_{y}", SimPalette.ForTile(kind),
                                       from, Vector2.one, -0.1f),
                from = from,
                to = to,
                elapsed = 0f,
            });
        }

        /// <summary>
        /// Which way the cell's tile travels. The per-cell mask edge says a tile moved; this
        /// says where to, and it is the only place a cell is traced back to a panel. Where
        /// panels overlap, the first in level order that is in the state which caused this
        /// edge wins — ordered, and the view is its only reader anyway.
        /// </summary>
        PanelDirection DirectionAt(int x, int y, bool wantHidden)
        {
            for (int i = 0; i < World.Panels.Count; i++)
            {
                SimPanel panel = World.Panels[i];

                if (panel.Enabled == wantHidden) continue;
                if (!panel.Rect.Contains(new Vector2Int(x, y))) continue;

                return panel.ExitDir;
            }

            return PanelDirection.Down;
        }

        void FallAndFade()
        {
            float dt = Time.deltaTime;

            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                Debris d = _debris[i];

                if (d.view == null) { _debris.RemoveAt(i); continue; }

                // Accelerating along whichever axis it left by, not only down: a wall
                // retracting upward pulls away the way a floor drops, through one code path.
                d.speed += Gravity * dt;

                Vector3 p = d.view.transform.localPosition;
                p.x += d.direction.x * d.speed * dt;
                p.y += d.direction.y * d.speed * dt;
                d.view.transform.localPosition = p;

                if (OffRoom(p))
                {
                    Destroy(d.view);
                    _debris.RemoveAt(i);
                    continue;
                }

                _debris[i] = d;
            }
        }

        /// <summary>Past the room on any side — the four-directional form of the old y bound.</summary>
        static bool OffRoom(Vector3 p)
            => p.x < -2f || p.y < -2f || p.x > TileGrid.Width + 2f || p.y > TileGrid.Height + 2f;

        /// <summary>
        /// Arrivals run on render time like the debris, and like the debris nothing in the sim
        /// can see them: the tile they cover is already solid.
        /// </summary>
        void SlideIn()
        {
            float dt = Time.deltaTime;

            for (int i = _arrivals.Count - 1; i >= 0; i--)
            {
                Arrival a = _arrivals[i];

                if (a.view == null) { _arrivals.RemoveAt(i); continue; }

                a.elapsed += dt;

                float t = Mathf.Clamp01(a.elapsed / ArrivalTime);
                Vector2 at = Vector2.Lerp(a.from, a.to, t);
                a.view.transform.localPosition = new Vector3(at.x, at.y, -0.1f);

                if (t >= 1f)
                {
                    Destroy(a.view);
                    _arrivals.RemoveAt(i);
                    continue;
                }

                _arrivals[i] = a;
            }
        }
    }
}
