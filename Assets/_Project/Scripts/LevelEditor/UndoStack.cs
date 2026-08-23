using System.Collections.Generic;
using KeepyUppy.Sim.Level;
using UnityEngine;

namespace KeepyUppy.LevelEditing
{
    /// <summary>
    /// Flat undo history of whole-level snapshots (level-editor-spec 4).
    ///
    /// "The level is small, don't be clever." A level is a couple of KB of JSON, so a
    /// snapshot per edit is cheaper to write and far easier to trust than a command-diff
    /// system — and it cannot desync from the thing it is meant to restore.
    /// </summary>
    public sealed class UndoStack
    {
        public const int MaxDepth = 50;

        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();

        public int UndoDepth => _undo.Count;
        public int RedoDepth => _redo.Count;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        /// <summary>Records the state BEFORE an edit. Call once per stroke, not per tile.</summary>
        public void Push(LevelData level)
        {
            _undo.Add(JsonUtility.ToJson(level));

            // Oldest first out. Fifty strokes back is far more than anyone reaches for.
            if (_undo.Count > MaxDepth) _undo.RemoveAt(0);

            // A new edit invalidates anything that was undone.
            _redo.Clear();
        }

        /// <summary>Returns the previous state, given the current one to put on the redo pile.</summary>
        public LevelData Undo(LevelData current)
        {
            if (_undo.Count == 0) return null;

            _redo.Add(JsonUtility.ToJson(current));

            string snapshot = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            return JsonUtility.FromJson<LevelData>(snapshot);
        }

        public LevelData Redo(LevelData current)
        {
            if (_redo.Count == 0) return null;

            _undo.Add(JsonUtility.ToJson(current));

            string snapshot = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            return JsonUtility.FromJson<LevelData>(snapshot);
        }
    }
}
