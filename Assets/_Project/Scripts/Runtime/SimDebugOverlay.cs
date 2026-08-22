using KeepyUppy.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Dev-only readout. IMGUI so it needs no canvas, no font asset and no prefab.
    ///
    /// Also hot-swaps the control profile on 1 / 2 / 3, which GDD 5.2 asks for. Those keys
    /// are outside the reserved gameplay set (Space W A D J R Esc and the arrows) and are
    /// read directly rather than through IInputSource — profile switching is a dev action,
    /// not a player intent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SimDebugOverlay : MonoBehaviour
    {
        public SimDriver Driver;

        GUIStyle _style;

        void Update()
        {
            // Digits are profile hotkeys here and ordinary characters in a tuning box.
            if (DevInputCapture.KeyboardCaptured) return;

            Keyboard kb = Keyboard.current;
            if (kb == null || Driver == null) return;

            if (kb.digit1Key.wasPressedThisFrame) Driver.World.Profile = ControlProfile.JumpOnly;
            if (kb.digit2Key.wasPressedThisFrame) Driver.World.Profile = ControlProfile.JumpAndContext;
            if (kb.digit3Key.wasPressedThisFrame) Driver.World.Profile = ControlProfile.PropsOnly;
        }

        void OnGUI()
        {
            if (Driver == null) return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
            };

            SimWorld world = Driver.World;

            string text =
                $"step     {world.Clock.Step}\n" +
                $"touches  {world.TouchCount}\n" +
                $"deaths   {world.DeathCount}\n" +
                $"speed    {world.Balloon.Velocity.magnitude:F2} m/s\n" +
                $"profile  {ProfileLabel(world.Profile)}\n" +
                $"{(world.IsDead ? world.LastDeath.ToString() : string.Empty)}";

            GUI.Label(new Rect(12f, 8f, 320f, 140f), text, _style);
            GUI.Label(new Rect(12f, Screen.height - 26f, 640f, 20f),
                      "A/D move   Space jump   J context   R restart   1/2/3 profile", _style);
        }

        static string ProfileLabel(ControlProfile profile)
        {
            switch (profile)
            {
                case ControlProfile.JumpOnly: return "A — jump only";
                case ControlProfile.PropsOnly: return "C — props only";
                default: return "B — jump + context";
            }
        }
    }
}
