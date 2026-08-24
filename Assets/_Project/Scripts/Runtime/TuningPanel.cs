#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using KeepyUppy.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// The live tuning panel (level-editor-spec 6, GDD 18) — sliders over the physics
    /// constants, applied immediately, with save / load / reset over named JSON profiles.
    ///
    /// The spec calls this the highest-value item in the tools list, and the reason is that
    /// feel cannot be reasoned about, only dialled in. Everything in GDD 6.4 and 5.7 is a
    /// slider here and a literal nowhere.
    ///
    /// IMGUI on purpose: no canvas, no font asset, no prefab, and it survives into a
    /// development build for playtesting. Dev-only — compiled out of release builds.
    /// Switch the guard to LEVEL_EDITOR once that define exists.
    ///
    /// Toggle with backtick. Avoids the reserved gameplay keys (Space W A D J R Esc, arrows)
    /// and leaves T to the level editor's safe-area overlay, which is what the spec assigns
    /// it to.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TuningPanel : MonoBehaviour
    {
        public SimDriver Driver;
        public FileTuningSource Source;

        /// <summary>Profile loaded at startup, and the name Save defaults to.</summary>
        public string ProfileName = FileTuningSource.DefaultProfileName;

        struct Knob
        {
            public string label;
            public float min;
            public float max;
            public bool integer;
            public Func<float> get;
            public Action<float> set;

            /// <summary>Index into the text-box buffer. Assigned after the groups are built.</summary>
            public int index;
        }

        struct Group
        {
            public string title;
            public Knob[] knobs;
        }

        Group[] _groups;

        /// <summary>
        /// Live text for each value box. Held separately from the value so a half-typed
        /// number survives until it parses — reformatting every frame would fight the typist.
        /// </summary>
        string[] _text;

        IReadOnlyList<string> _profiles = Array.Empty<string>();
        Vector2 _scroll;
        bool _open;
        string _status = string.Empty;
        GUIStyle _headerStyle;

        void Start()
        {
            BuildKnobs();
            IndexKnobs();
            RefreshProfileList();
        }

        void Update()
        {
            // The toggle must not fire while a field is being typed into. The level editor's
            // Edit mode is exempt: the panel is meant to be reachable while authoring.
            if (DevInputCapture.TextFieldFocused) return;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.backquoteKey.wasPressedThisFrame)
            {
                _open = !_open;
                if (_open) RefreshProfileList();
            }
        }

        void OnDisable() => DevInputCapture.TextFieldFocused = false;

        void RefreshProfileList() => _profiles = Source?.List() ?? Array.Empty<string>();

        // ---------------------------------------------------------------- knobs

        void BuildKnobs()
        {
            SimWorld w = Driver.World;

            _groups = new[]
            {
                new Group
                {
                    title = "Balloon — motion (GDD 6.2 / 6.4)",
                    knobs = new[]
                    {
                        F("gravity", 0f, 30f, () => w.Tuning.gravity, v => w.Tuning.gravity = v),
                        F("drag_ky (vertical)", 0f, 8f, () => w.Tuning.dragKy, v => w.Tuning.dragKy = v),
                        F("drag_kx (horizontal)", 0f, 8f, () => w.Tuning.dragKx, v => w.Tuning.dragKx = v),
                        F("min_bounce_speed", 0f, 3f, () => w.Tuning.minBounceSpeed, v => w.Tuning.minBounceSpeed = v),
                        F("max_speed", 4f, 40f, () => w.Tuning.maxSpeed, v => w.Tuning.maxSpeed = v),
                        F("flutter_accel", 0f, 6f, () => w.Tuning.flutterAccel, v => w.Tuning.flutterAccel = v),
                    },
                },
                new Group
                {
                    title = "Contact (GDD 4.2 / 6.4)",
                    knobs = new[]
                    {
                        F("restitution_wall", 0f, 1f, () => w.Tuning.restitutionWall, v => w.Tuning.restitutionWall = v),
                        F("restitution_player_passive", 0f, 1f, () => w.Tuning.restitutionPlayerPassive, v => w.Tuning.restitutionPlayerPassive = v),
                        F("active_contact_min_vy", 0f, 6f, () => w.Tuning.activeContactMinPlayerVy, v => w.Tuning.activeContactMinPlayerVy = v),
                    },
                },
                new Group
                {
                    title = "Verbs (GDD 6.4 / 6.5)",
                    knobs = new[]
                    {
                        F("bump_velocity", 0f, 20f, () => w.Tuning.bumpVelocity, v => w.Tuning.bumpVelocity = v),
                        F("bump_inherit_x", 0f, 1.5f, () => w.Tuning.bumpInheritX, v => w.Tuning.bumpInheritX = v),
                        F("bump_inherit_y", 0f, 1.5f, () => w.Tuning.bumpInheritY, v => w.Tuning.bumpInheritY = v),
                        F("palm_speed", 0f, 20f, () => w.Tuning.palmSpeed, v => w.Tuning.palmSpeed = v),
                        I("palm_cooldown_frames", 0f, 90f, () => w.Tuning.palmCooldownFrames, v => w.Tuning.palmCooldownFrames = (int)v),
                        F("spike_speed", 0f, 25f, () => w.Tuning.spikeSpeed, v => w.Tuning.spikeSpeed = v),
                        I("spike_cooldown_frames", 0f, 120f, () => w.Tuning.spikeCooldownFrames, v => w.Tuning.spikeCooldownFrames = (int)v),
                        F("aim_offset_max_angle", 0f, 80f, () => w.Tuning.aimOffsetMaxAngle, v => w.Tuning.aimOffsetMaxAngle = v),
                        F("aim_side_angle", 0f, 90f, () => w.Tuning.aimSideAngle, v => w.Tuning.aimSideAngle = v),
                        I("context_hold_frames", 1f, 40f, () => w.Tuning.contextHoldFrames, v => w.Tuning.contextHoldFrames = (int)v),
                        F("arm_reach_radius", 0.2f, 4f, () => w.Tuning.armReachRadius, v => w.Tuning.armReachRadius = v),
                    },
                },
                new Group
                {
                    title = "Fairness (GDD 8)",
                    knobs = new[]
                    {
                        F("player_bump_radius_bonus", 0f, 0.6f, () => w.Tuning.playerBumpRadiusBonus, v => w.Tuning.playerBumpRadiusBonus = v),
                        F("hazard_pop_radius_shrink", 0f, 0.4f, () => w.Tuning.hazardPopRadiusShrink, v => w.Tuning.hazardPopRadiusShrink = v),
                    },
                },
                new Group
                {
                    title = "Player (GDD 5.7)",
                    knobs = new[]
                    {
                        F("run_speed", 0f, 14f, () => w.PlayerTuning.runSpeed, v => w.PlayerTuning.runSpeed = v),
                        F("jump_height_tiles", 0.5f, 8f, () => w.PlayerTuning.jumpHeightTiles, v => w.PlayerTuning.jumpHeightTiles = v),
                        F("gravity", 0f, 40f, () => w.PlayerTuning.gravity, v => w.PlayerTuning.gravity = v),
                        F("terminal_fall_speed", 4f, 40f, () => w.PlayerTuning.terminalFallSpeed, v => w.PlayerTuning.terminalFallSpeed = v),
                        I("coyote_frames", 0f, 20f, () => w.PlayerTuning.coyoteFrames, v => w.PlayerTuning.coyoteFrames = (int)v),
                        I("jump_buffer_frames", 0f, 30f, () => w.PlayerTuning.jumpBufferFrames, v => w.PlayerTuning.jumpBufferFrames = (int)v),
                        I("ground_accel_frames", 0f, 30f, () => w.PlayerTuning.groundAccelFrames, v => w.PlayerTuning.groundAccelFrames = (int)v),
                        I("air_accel_frames", 0f, 30f, () => w.PlayerTuning.airAccelFrames, v => w.PlayerTuning.airAccelFrames = (int)v),
                        F("jump_cut_multiplier", 0f, 1f, () => w.PlayerTuning.jumpCutMultiplier, v => w.PlayerTuning.jumpCutMultiplier = v),
                        I("death_restart_frames", 0f, 60f, () => w.PlayerTuning.deathRestartFrames, v => w.PlayerTuning.deathRestartFrames = (int)v),
                    },
                },
            };
        }

        /// <summary>Numbers the knobs so each can own a slot in the text buffer.</summary>
        void IndexKnobs()
        {
            int next = 0;
            for (int g = 0; g < _groups.Length; g++)
            for (int k = 0; k < _groups[g].knobs.Length; k++)
                _groups[g].knobs[k].index = next++;

            _text = new string[next];
        }

        static Knob F(string label, float min, float max, Func<float> get, Action<float> set)
            => new Knob { label = label, min = min, max = max, get = get, set = set };

        static Knob I(string label, float min, float max, Func<float> get, Action<float> set)
            => new Knob { label = label, min = min, max = max, integer = true, get = get, set = set };

        // ---------------------------------------------------------------- gui

        void OnGUI()
        {
            if (Driver == null || _groups == null) return;

            _headerStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            if (!_open)
            {
                DevInputCapture.TextFieldFocused = false;
                GUI.Label(new Rect(Screen.width - 150f, 8f, 140f, 20f), "` — tuning panel");
                return;
            }

            // Any focused field owns the keyboard, so gameplay bindings stay out of the way.
            DevInputCapture.TextFieldFocused = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());

            // Enter commits and releases focus, which also reformats the box.
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                GUI.FocusControl(null);
                Event.current.Use();
            }

            const float width = 400f;
            GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, Screen.height - 20f),
                                GUI.skin.box);

            DrawProfileRow();
            GUILayout.Space(6f);

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (Group group in _groups)
            {
                GUILayout.Label(group.title, _headerStyle);
                foreach (Knob knob in group.knobs) DrawKnob(knob);
                GUILayout.Space(8f);
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status);

            GUILayout.EndArea();
        }

        void DrawProfileRow()
        {
            GUILayout.Label("Profile", _headerStyle);

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("profileName");
            ProfileName = GUILayout.TextField(ProfileName, GUILayout.Width(160f));

            if (GUILayout.Button("Save")) SaveProfile();
            if (GUILayout.Button("Load")) LoadProfile(ProfileName);
            if (GUILayout.Button("Reset")) ResetToDefault();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Make these values the startup default", GUILayout.Width(280f)))
                MakeDefault();
            GUILayout.EndHorizontal();

            if (_profiles.Count == 0) return;

            GUILayout.BeginHorizontal();
            for (int i = 0; i < _profiles.Count; i++)
            {
                if (GUILayout.Button(_profiles[i])) LoadProfile(_profiles[i]);

                // Wrap so a long list does not run off the panel.
                if ((i + 1) % 3 != 0 || i == _profiles.Count - 1) continue;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
            }
            GUILayout.EndHorizontal();
        }

        void DrawKnob(Knob knob)
        {
            float current = knob.get();
            string controlName = "knob" + knob.index;
            bool editing = GUI.GetNameOfFocusedControl() == controlName;

            // While the box has focus its text belongs to the typist. Otherwise it mirrors
            // the value, so dragging the slider updates the number and vice versa.
            if (!editing) _text[knob.index] = DevNumber.Format(current, knob.integer);

            GUILayout.BeginHorizontal();
            GUILayout.Label(knob.label, GUILayout.Width(186f));

            // Range widened to contain the current value, so a typed number outside the
            // guessed range is not yanked back inside on the next frame.
            DevNumber.SliderRange(current, knob.min, knob.max, out float low, out float high);

            float dragged = GUILayout.HorizontalSlider(current, low, high, GUILayout.Width(96f));
            if (knob.integer) dragged = Mathf.Round(dragged);

            GUI.SetNextControlName(controlName);
            string typed = GUILayout.TextField(_text[knob.index], GUILayout.Width(58f));

            GUILayout.EndHorizontal();

            // Applied immediately — the sim re-reads both tuning structs every step.
            if (typed != _text[knob.index])
            {
                _text[knob.index] = typed;

                // Partial input ("", "-", "2.") simply does not apply yet. The text stays as
                // typed rather than snapping back, so the field is usable mid-keystroke.
                if (DevNumber.TryParse(typed, knob.integer, out float parsed)) knob.set(parsed);
                return;
            }

            if (!Mathf.Approximately(dragged, current)) knob.set(dragged);
        }

        // ---------------------------------------------------------------- actions

        public void SaveProfile()
        {
            if (Source == null || !Source.CanSave) { _status = "this build cannot save profiles"; return; }

            string name = FileTuningSource.Sanitise(ProfileName);
            Source.Save(name, CurrentProfile(name));

            ProfileName = name;
            RefreshProfileList();
            _status = $"saved '{name}'";
        }

        public void LoadProfile(string name)
        {
            if (Source == null) return;

            if (!Source.TryLoad(name, out TuningProfile profile))
            {
                _status = $"no profile '{name}'";
                return;
            }

            Apply(profile);
            ProfileName = name;
            _status = $"loaded '{name}'";
        }

        /// <summary>
        /// Writes the current values over default.json, which is the profile the game boots
        /// with — so the feel you are holding becomes the one you start in.
        ///
        /// Nothing is lost: Reset still restores the code defaults in BalloonTuning.Default,
        /// and any named profile you saved is untouched.
        /// </summary>
        public void MakeDefault()
        {
            if (Source == null || !Source.CanSave) { _status = "this build cannot save profiles"; return; }

            Source.Save(FileTuningSource.DefaultProfileName,
                        CurrentProfile(FileTuningSource.DefaultProfileName));

            RefreshProfileList();
            _status = "these values are now the startup default";
        }

        public void ResetToDefault()
        {
            Apply(TuningProfile.Default);
            _status = "reset to code defaults";
        }

        public void Apply(TuningProfile profile)
        {
            Driver.World.Tuning = profile.balloon;
            Driver.World.PlayerTuning = profile.player;
        }

        public TuningProfile CurrentProfile(string name) => new TuningProfile
        {
            name = name,
            balloon = Driver.World.Tuning,
            player = Driver.World.PlayerTuning,
        };
    }
}
#endif
