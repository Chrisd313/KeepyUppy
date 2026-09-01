using System.Collections.Generic;
using KeepyUppy.Sim.Entities;
using KeepyUppy.Sim.Panels;
using UnityEngine;

namespace KeepyUppy.Sim.Triggers
{
    /// <summary>
    /// Every trigger in the room, evaluated in a fixed order (GDD 9.3).
    ///
    /// A List, never a Dictionary or HashSet: iteration order decides which link applies
    /// first when two fire on the same step, and unordered iteration is on CLAUDE.md's list
    /// of the things that break GDD 6.1 silently.
    ///
    /// Firing is edge-triggered. A trigger fires on the false-to-true edge of its condition,
    /// never for every step the condition holds — the same shape as ScreenStack.ObserveWin,
    /// which is where this project already learned that a level-triggered check ships broken.
    /// </summary>
    public sealed class TriggerField
    {
        readonly List<SimTrigger> _triggers = new List<SimTrigger>();

        readonly List<SimLink> _links = new List<SimLink>();

        /// <summary>Links waiting out a delay. Drained in insertion order.</summary>
        readonly List<Pending> _pending = new List<Pending>();

        struct Pending
        {
            public int step;
            public int link;
        }

        /// <summary>Indices of the triggers that fired on the most recent step, in list order.</summary>
        public readonly List<int> FiredThisStep = new List<int>();

        /// <summary>Types this build has no behaviour for. Surfaced, never swallowed.</summary>
        public int UnknownCount { get; private set; }

        public int Count => _triggers.Count;
        public SimTrigger this[int i] => _triggers[i];

        public int LinkCount => _links.Count;
        public int PendingCount => _pending.Count;

        public void Clear()
        {
            _triggers.Clear();
            _links.Clear();
            _pending.Clear();
            FiredThisStep.Clear();
            UnknownCount = 0;
        }

        public void Add(SimTrigger trigger)
        {
            if (trigger == null) return;

            if (trigger.Kind == TriggerKind.Unknown) UnknownCount++;

            trigger.Reset();
            _triggers.Add(trigger);
        }

        public void AddLink(SimLink link)
        {
            if (link != null) _links.Add(link);
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _triggers.Count; i++)
                if (_triggers[i].Id == id) return i;

            return -1;
        }

        public SimTrigger Find(string id)
        {
            int index = IndexOf(id);
            return index < 0 ? null : _triggers[index];
        }

        /// <summary>GDD 8: part of the synchronous restart. The room must replay from the start.</summary>
        public void Reset()
        {
            for (int i = 0; i < _triggers.Count; i++) _triggers[i].Reset();
            _pending.Clear();
            FiredThisStep.Clear();
        }

        /// <summary>
        /// Evaluates every trigger for this step. Called last in SimWorld.Tick, after contact
        /// and the exit check: a trap armed by the player's own movement must not be able to
        /// materialise inside the body that armed it, in the frame it armed it.
        /// </summary>
        public void Step(int step, Vector2 playerPos, bool hasPlayer, Vector2 balloonPos,
                         EntityField entities, AirField air, PanelField panels)
        {
            FiredThisStep.Clear();

            for (int i = 0; i < _triggers.Count; i++)
            {
                SimTrigger trigger = _triggers[i];

                bool inside = Holds(trigger, step, playerPos, hasPlayer, balloonPos);
                bool edge = inside && !trigger.WasInside;
                trigger.WasInside = inside;

                if (!edge) continue;
                if (trigger.Once && trigger.Fired) continue;

                trigger.Fired = true;
                FiredThisStep.Add(i);
            }

            // Queue every link the fires above own, then drain whatever is due. Queue first,
            // so a zero-delay link fires on the NEXT step rather than this one.
            for (int i = 0; i < FiredThisStep.Count; i++)
            {
                int trigger = FiredThisStep[i];

                for (int l = 0; l < _links.Count; l++)
                {
                    if (_links[l].FromIndex != trigger) continue;
                    _pending.Add(new Pending { step = step + 1 + _links[l].DelaySteps, link = l });
                }
            }

            Drain(step, entities, air, panels);
        }

        /// <summary>
        /// Applies everything due, in insertion order, and compacts what is left in place.
        /// Forward iteration is load-bearing: two links landing on the same step must apply in
        /// the order they were queued, on every run.
        /// </summary>
        void Drain(int step, EntityField entities, AirField air, PanelField panels)
        {
            int keep = 0;

            for (int i = 0; i < _pending.Count; i++)
            {
                Pending item = _pending[i];

                if (item.step > step)
                {
                    _pending[keep++] = item;
                    continue;
                }

                Apply(_links[item.link], entities, air, panels, step);
            }

            _pending.RemoveRange(keep, _pending.Count - keep);
        }

        static void Apply(SimLink link, EntityField entities, AirField air, PanelField panels, int step)
        {
            if (link.Action == LinkAction.Inert || link.ToIds == null) return;

            foreach (string id in link.ToIds)
            {
                // An entity first: a fan is both an entity and a zone sharing one id, and its
                // AirZone is republished from Active every step, so setting the entity is
                // enough and there is no fan special case.
                SimEntity entity = entities?.Find(id);
                if (entity != null)
                {
                    // step + 1: this runs at the END of the step, so the first step the
                    // entity is actually placed on is the next one. Its path starts there.
                    entity.SetActive(Next(link.Action, entity.Active), step + 1);
                    continue;
                }

                if (air != null)
                {
                    for (int i = 0; i < air.Count; i++)
                    {
                        if (air[i].id != id) continue;

                        AirZone zone = air[i];
                        zone.enabled = Next(link.Action, zone.enabled);
                        air[i] = zone;
                    }
                }

                // Panels last: a panel is geometry, and an id naming one is not naming an
                // entity or a zone. On shows its tiles, Off takes them away.
                if (panels != null && panels.IndexOf(id) >= 0)
                    panels.SetEnabled(id, Next(link.Action, panels.Find(id).Enabled));
            }
        }

        static bool Next(LinkAction action, bool current)
        {
            switch (action)
            {
                case LinkAction.On:     return true;
                case LinkAction.Off:    return false;
                case LinkAction.Toggle: return !current;
                default:                return current;
            }
        }

        static bool Holds(SimTrigger trigger, int step, Vector2 playerPos, bool hasPlayer,
                          Vector2 balloonPos)
        {
            switch (trigger.Kind)
            {
                case TriggerKind.PlayerRegion:
                    return hasPlayer && trigger.Rect.Contains(playerPos);

                case TriggerKind.BalloonRegion:
                    return trigger.Rect.Contains(balloonPos);

                // Held true from its step onwards rather than tested for equality, so the edge
                // rule above does the "fires once" work instead of a second mechanism.
                case TriggerKind.Timer:
                    return step >= trigger.AtStep;

                default:
                    return false;
            }
        }
    }
}
