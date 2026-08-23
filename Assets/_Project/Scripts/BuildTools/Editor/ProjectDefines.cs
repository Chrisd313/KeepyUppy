using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace KeepyUppy.BuildTools
{
    /// <summary>
    /// Scripting define symbols.
    ///
    /// CLAUDE.md requires the in-game level editor to be gated by the LEVEL_EDITOR define
    /// rather than by folder placement — it is *runtime* code that is merely dev-only, and it
    /// must survive into development builds or it cannot be used for playtesting.
    ///
    /// Defines are per build target, not per configuration, so LEVEL_EDITOR is present by
    /// default and the release build strips it explicitly (see WebGLBuild).
    /// </summary>
    public static class ProjectDefines
    {
        public const string LevelEditor = "LEVEL_EDITOR";

        static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Standalone,
            NamedBuildTarget.WebGL,
        };

        [MenuItem("KeepyUppy/Defines/Enable LEVEL_EDITOR")]
        public static void EnableLevelEditor() => SetOnAllTargets(LevelEditor, true);

        [MenuItem("KeepyUppy/Defines/Disable LEVEL_EDITOR")]
        public static void DisableLevelEditor() => SetOnAllTargets(LevelEditor, false);

        public static void SetOnAllTargets(string define, bool enabled)
        {
            foreach (NamedBuildTarget target in Targets) Set(target, define, enabled);
        }

        public static bool IsSet(NamedBuildTarget target, string define)
            => new List<string>(Current(target)).Contains(define);

        public static void Set(NamedBuildTarget target, string define, bool enabled)
        {
            var defines = new List<string>(Current(target));
            bool present = defines.Contains(define);

            if (enabled == present) return;

            if (enabled) defines.Add(define);
            else defines.Remove(define);

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            Debug.Log($"[defines] {target.TargetName}: {define} {(enabled ? "added" : "removed")}");
        }

        static string[] Current(NamedBuildTarget target)
        {
            string value = PlayerSettings.GetScriptingDefineSymbols(target);
            return string.IsNullOrEmpty(value)
                ? Array.Empty<string>()
                : value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
