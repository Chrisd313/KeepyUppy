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
    [DisallowMultipleComponent]
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

        void FixedUpdate()
        {
            World.Tick();
        }
    }
}
