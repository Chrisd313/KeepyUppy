using System.Collections.Generic;
using KeepyUppy.Sim.Entities;
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

        /// <summary>Indices of the triggers that fired on the most recent step, in list order.</summary>
        public readonly List<int> FiredThisStep = new List<int>();

        /// <summary>Types this build has no behaviour for. Surfaced, never swallowed.</summary>
        public int UnknownCount { get; private set; }

        public int Count => _triggers.Count;
        public SimTrigger this[int i] => _triggers[i];

        public void Clear()
        {
            _triggers.Clear();
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
            FiredThisStep.Clear();
        }

        /// <summary>
        /// Evaluates every trigger for this step. Called last in SimWorld.Tick, after contact
        /// and the exit check: a trap armed by the player's own movement must not be able to
        /// materialise inside the body that armed it, in the frame it armed it.
        /// </summary>
        public void Step(int step, Vector2 playerPos, bool hasPlayer, Vector2 balloonPos,
                         EntityField entities, AirField air)
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
