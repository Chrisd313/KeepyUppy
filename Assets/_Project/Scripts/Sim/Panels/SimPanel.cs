using KeepyUppy.Sim.Entities;
using UnityEngine;

namespace KeepyUppy.Sim.Panels
{
    /// <summary>
    /// A rectangle of authored tiles a link can take away (GDD 9.3's falling floor), or —
    /// once it carries a path — one that travels while staying solid (moving-panels design).
    ///
    /// The tiles belong to the level; a panel only decides whether they are currently there,
    /// and where. Config is copied out of the level data at load, so nothing here reads JSON.
    /// </summary>
    public sealed class SimPanel
    {
        public string Id;

        /// <summary>Tile coordinates. A panel is always whole tiles — the mask indexes cells.</summary>
        public RectInt Rect;

        /// <summary>
        /// Which way its tiles leave, and come back from, when it is switched rather than
        /// travelled. Pathless panels only: a moving panel's tiles are not debris.
        /// </summary>
        public PanelDirection ExitDir = PanelDirection.Down;

        // --- Path config: set at load, never mutated by the tick ---

        /// <summary>Offsets from <see cref="Rect"/>, the panel's own zero first.</summary>
        public Vector2[] Points = { Vector2.zero };

        public PathMode Mode = PathMode.Once;
        public float Speed;
        public int HoldSteps;
        public float Phase;

        /// <summary>
        /// A panel with waypoints is a moving solid; one without is a mask over grid cells.
        /// One field decides, so there is no mode flag to get out of sync with the level.
        /// </summary>
        public bool IsMoving => Points != null && Points.Length > 1;

        // --- Run state: cleared by Reset(), covered by the replay-equality test ---

        /// <summary>Whether its tiles are currently present.</summary>
        public bool Enabled = true;

        /// <summary>What Enabled returns to on a restart.</summary>
        public bool StartEnabled = true;

        /// <summary>
        /// The step this travel is measured from. Forward, elapsed is step - RunStep;
        /// returning, it is RunStep - step, which is why Return() rebases rather than
        /// storing a second counter.
        /// </summary>
        public int RunStep;

        /// <summary>+1 running, -1 returning.</summary>
        public int Direction = 1;

        /// <summary>
        /// Whether <see cref="Run"/> or <see cref="Return"/> has ever been called. A panel
        /// with a path does not travel on its own the instant it loads — it waits for a
        /// trigger, exactly as a switched mask panel waits for one. Without this flag,
        /// <c>RunStep == 0, Direction == 1</c> is indistinguishable from "told to run at
        /// step 0", and every moving panel would set off the moment the level did.
        /// </summary>
        public bool HasRun;

        /// <summary>Offset from the authored rect, as of the last Place().</summary>
        public Vector2 Offset;

        /// <summary>Tiles per second, as of the last Place(). Derived, never integrated.</summary>
        public Vector2 Velocity;

        /// <summary>Where the panel actually is this step, in metres.</summary>
        public Rect WorldRect => new Rect(Rect.x + Offset.x, Rect.y + Offset.y,
                                          Rect.width, Rect.height);

        public void Reset()
        {
            Enabled = StartEnabled;
            RunStep = 0;
            Direction = 1;
            HasRun = false;
            Offset = Vector2.zero;
            Velocity = Vector2.zero;
        }

        /// <summary>
        /// Places the panel for this step, and derives the velocity from the next step's
        /// position rather than integrating one. Pure: safe to call for any step, in any
        /// order, exactly as SimEntity.Place is.
        ///
        /// A panel that has never been sent out (<see cref="HasRun"/> false) stays parked at
        /// home regardless of step — the step counter alone cannot tell "runs from step 0"
        /// apart from "has not been told to run".
        /// </summary>
        public void Place(int step)
        {
            if (!IsMoving || !HasRun) { Offset = Vector2.zero; Velocity = Vector2.zero; return; }

            Offset = At(step);
            Velocity = (At(step + 1) - Offset) / SimClock.Dt;
        }

        Vector2 At(int step)
        {
            int elapsed = Direction >= 0 ? step - RunStep : RunStep - step;
            if (elapsed < 0) elapsed = 0;

            return PanelPath.Evaluate(Points, Mode, Speed, Phase, HoldSteps, elapsed);
        }

        /// <summary>Sends the panel out along its path from this step.</summary>
        public void Run(int step)
        {
            Direction = 1;
            RunStep = step;
            HasRun = true;
        }

        /// <summary>
        /// Walks the path back from wherever the panel currently is. RunStep is rebased to
        /// step + elapsed, so At() counts down to zero from here instead of snapping the
        /// panel to an end — the same correction SimEntity.ArmedStep exists to make.
        /// </summary>
        public void Return(int step)
        {
            int elapsed = Direction >= 0 ? step - RunStep : RunStep - step;
            if (elapsed < 0) elapsed = 0;

            Direction = -1;
            RunStep = step + elapsed;
            HasRun = true;
        }
    }
}
