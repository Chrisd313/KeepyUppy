using KeepyUppy.InputLayer.Devices;
using KeepyUppy.Sim;
using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// The only bridge between Unity's clock and the simulation.
    ///
    /// FixedUpdate is the sole entry point; nothing else may tick the world. The sim
    /// integrates with SimClock.Dt regardless of the project's timestep setting, so this
    /// aligns Unity to the sim rather than the other way round.
    /// </summary>
    /// <remarks>
    /// Ordered ahead of every view so that a view's own FixedUpdate - where
    /// <see cref="StepInterpolator"/> takes its one sample per step - reads the position
    /// after this step ran, not before it.
    /// </remarks>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class SimDriver : MonoBehaviour
    {
        public SimWorld World { get; } = new SimWorld();

        /// <summary>Steps taken since the world was last reset. Debug readout only.</summary>
        public int Step => World.Clock.Step;

        /// <summary>Held concretely so its per-render-frame latch can be pumped.</summary>
        public KeyboardInput Keyboard { get; private set; }

        void Awake()
        {
            // The project default is 0.02 s (50 Hz). The sim is 60 Hz (GDD 6.1), so state
            // the requirement here instead of in ProjectSettings: it travels with the code
            // and cannot silently drift out of sync with SimClock.
            Time.fixedDeltaTime = SimClock.Dt;

            Keyboard = new KeyboardInput();
        }

        void Update()
        {
            // A dev tool with a focused text box owns the keyboard; the player must not walk
            // because a profile name contains 'a'.
            Keyboard.Suppressed = DevInputCapture.KeyboardCaptured;

            // Presses arrive on render frames and are consumed on fixed steps. Latching here
            // is what stops a press being lost when the frame rate dips below 60.
            Keyboard.Latch();
        }

        /// <summary>
        /// Set by the shell (<see cref="Runtime.ShellView"/>) for menu-driven pauses - the
        /// Paused and Results overlays. Independent of <see cref="EditorPaused"/> so the two
        /// owners cannot stomp each other; see <see cref="Paused"/>.
        /// </summary>
        public bool ShellPaused { get; set; }

        /// <summary>
        /// Set by the level editor while authoring (<see cref="BootstrapEditorHost.SetSimPaused"/>) -
        /// otherwise the balloon falls, dies and restarts on a loop behind the tiles being
        /// placed. Independent of <see cref="ShellPaused"/>; see <see cref="Paused"/>.
        /// </summary>
        public bool EditorPaused { get; set; }

        /// <summary>
        /// Holds the sim still without unwinding any of its state, whenever either owner above
        /// asks for it. The step counter simply stops advancing, so determinism is unaffected —
        /// a paused run resumes exactly where it was. This is the one place that answers
        /// "is the sim frozen" - do not add another.
        /// </summary>
        public bool Paused => ShellPaused || EditorPaused;

        /// <summary>
        /// How many fixed steps each render frame gets. Lives here and not in SimWorld
        /// because the simulation must not be able to observe it (GDD 6.1).
        /// </summary>
        public TimeControl Speed { get; } = new TimeControl();

        /// <summary>Set by the shell while the fast-forward control is held.</summary>
        public bool FastForward { get; set; }

        // Counts calls to FixedUpdate, not render frames: Unity can call FixedUpdate more
        // than once per render frame when the frame rate dips (see KeyboardShellInput.Latch's
        // comment on the same fact), so this is a fixed-step counter passed to
        // TimeControl.StepsFor - never assume it advances once per render frame.
        int _fixedStep;

        /// <summary>
        /// How far the current render frame sits into the step that has not run yet, for the
        /// views that interpolate. Rendering reads this; the simulation has no access to it,
        /// exactly as with <see cref="TimeControl"/>.
        /// </summary>
        public float Alpha => StepInterpolator.AlphaOf(Time.time, Time.fixedTime, Time.fixedDeltaTime);

        void FixedUpdate()
        {
            if (Paused) return;

            Speed.Factor = FastForward ? World.Tuning.fastForwardFactor : 1;

            World.Tick(Speed.StepsFor(_fixedStep++));
        }
    }
}
