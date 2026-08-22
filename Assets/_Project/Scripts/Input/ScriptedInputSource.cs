using System.Collections.Generic;
using UnityEngine;

namespace KeepyUppy.InputLayer
{
    /// <summary>
    /// Replays a fixed array of per-step intents.
    ///
    /// Not a stub: this is how a run becomes reproducible input, which is what makes the
    /// determinism tests meaningful. A keyboard cannot press the same keys on the same
    /// frames twice; this can.
    ///
    /// Indexed by the step passed to <see cref="Sample"/>, so it replays correctly across a
    /// restart (which returns the step counter to zero) rather than running off the end.
    /// </summary>
    public sealed class ScriptedInputSource : IInputSource
    {
        readonly InputIntents[] _frames;

        InputIntents _current;

        public ScriptedInputSource(InputIntents[] frames)
        {
            _frames = frames ?? new InputIntents[0];
        }

        public float MoveAxis => _current.moveAxis;
        public bool JumpPressed => _current.jumpPressed;
        public bool JumpHeld => _current.jumpHeld;
        public bool ContextPressed => _current.contextPressed;
        public bool ContextHeld => _current.contextHeld;
        public Vector2 AimHint => _current.aimHint;
        public bool RestartPressed => _current.restartPressed;

        public void Sample(int step)
        {
            if (_frames.Length == 0) { _current = InputIntents.Idle; return; }

            // Past the end of the script the last frame holds, so a short script does not
            // silently become "idle" halfway through a long run.
            int index = step < 0 ? 0 : (step >= _frames.Length ? _frames.Length - 1 : step);
            _current = _frames[index];
        }

        public void ResetIntents() => _current = InputIntents.Idle;

        /// <summary>
        /// Builds a script readably. Frames are appended, so the sequence in the test reads
        /// as the sequence the player performs.
        /// </summary>
        public sealed class Builder
        {
            readonly List<InputIntents> _frames = new List<InputIntents>();

            float _move;
            Vector2 _aim;

            /// <summary>Sets the held movement axis for every frame added after this call.</summary>
            public Builder Move(float axis) { _move = axis; return this; }

            /// <summary>Sets the held aim hint for every frame added after this call.</summary>
            public Builder Aim(Vector2 hint) { _aim = hint; return this; }

            public Builder Wait(int frames)
            {
                for (int i = 0; i < frames; i++) _frames.Add(Base());
                return this;
            }

            /// <summary>A jump press followed by <paramref name="holdFrames"/> frames of hold.</summary>
            public Builder Jump(int holdFrames = 8)
            {
                InputIntents press = Base();
                press.jumpPressed = true;
                press.jumpHeld = true;
                _frames.Add(press);

                for (int i = 0; i < holdFrames; i++)
                {
                    InputIntents held = Base();
                    held.jumpHeld = true;
                    _frames.Add(held);
                }

                return this;
            }

            /// <summary>A context press held for <paramref name="holdFrames"/> frames, then released.</summary>
            public Builder Context(int holdFrames)
            {
                InputIntents press = Base();
                press.contextPressed = true;
                press.contextHeld = true;
                _frames.Add(press);

                for (int i = 0; i < holdFrames; i++)
                {
                    InputIntents held = Base();
                    held.contextHeld = true;
                    _frames.Add(held);
                }

                _frames.Add(Base());
                return this;
            }

            public Builder Restart()
            {
                InputIntents f = Base();
                f.restartPressed = true;
                _frames.Add(f);
                return this;
            }

            /// <summary>Repeats the whole script until it is at least <paramref name="frames"/> long.</summary>
            public Builder LoopTo(int frames)
            {
                if (_frames.Count == 0) return Wait(frames);

                var original = new List<InputIntents>(_frames);
                int i = 0;
                while (_frames.Count < frames) _frames.Add(original[i++ % original.Count]);
                return this;
            }

            public ScriptedInputSource Build() => new ScriptedInputSource(_frames.ToArray());

            InputIntents Base() => new InputIntents { moveAxis = _move, aimHint = _aim };
        }
    }
}
