using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KeepyUppy.BuildTools
{
    /// <summary>
    /// Headless WebGL build plus a size report.
    ///
    /// GDD 17.2 flags WebGL build size as the primary shipping risk, and CLAUDE.md says to
    /// check it at the end of P0 rather than later — a number you can still act on.
    ///
    /// Editor-only by assembly definition, so it is stripped from every player build. This is
    /// genuine editor tooling; the in-game level editor is runtime code and must never live
    /// in a folder called Editor.
    ///
    /// Scenes are passed explicitly, so this never touches EditorBuildSettings. The bootstrap
    /// installs itself via RuntimeInitializeOnLoadMethod, so whatever scene ships is only a
    /// container.
    /// </summary>
    public static class WebGLBuild
    {
        const string OutputDirectory = "Build/WebGL";

        [MenuItem("KeepyUppy/Build WebGL and report size")]
        public static void BuildAndReport()
        {
            string[] scenes = ScenePaths();
            if (scenes.Length == 0)
            {
                Debug.LogError("[build] no scenes found under Assets/");
                EditorApplication.Exit(1);
                return;
            }

            ConfigureForWeb();

            string output = Path.GetFullPath(OutputDirectory);
            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            Debug.Log($"[build] WebGL → {output}");
            Debug.Log($"[build] scripting backend: {PlayerSettings.GetScriptingBackend(NamedBuildTarget.WebGL)}");
            Debug.Log($"[build] stripping level:   {PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.WebGL)}");
            Debug.Log($"[build] compression:       {PlayerSettings.WebGL.compressionFormat}");

            BuildReport report;
            BuildSummary summary;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
                summary = report.summary;
            }
            finally
            {
                // Put the dev define back however the build went, so the next editor session
                // still has the level editor.
                ProjectDefines.Set(NamedBuildTarget.WebGL, ProjectDefines.LevelEditor, true);
            }

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[build] FAILED: {summary.result}, {summary.totalErrors} errors");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log(SizeReport(output, summary));
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// The GDD 17.2 mitigations, applied as code so the shipping configuration is
        /// version-controlled and reproducible rather than a setting someone once clicked.
        /// </summary>
        static void ConfigureForWeb()
        {
            // The Unity splash logo was 2.7 MB uncompressed — 22.5% of every asset in the
            // 2026-08-22 baseline build, for four seconds of someone else's branding.
            // Optional on Personal licences as of Unity 6.
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            // "aggressive code stripping" (GDD 17.2). Anything reached only by reflection
            // must be kept alive by a link.xml — nothing in the sim relies on reflection.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
            PlayerSettings.stripEngineCode = true;

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.dataCaching = true;

            // No exception support is the smallest and fastest option. Explicit rather than
            // inherited, because it is a real tradeoff: a runtime error in the wild becomes
            // much harder to diagnose.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;

            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;

            // The level editor is dev-only runtime code. Defines are per target rather than
            // per configuration, so a release build has to strip it explicitly. Restored
            // after the build so editor work continues uninterrupted.
            ProjectDefines.Set(NamedBuildTarget.WebGL, ProjectDefines.LevelEditor, false);

            AssetDatabase.SaveAssets();
        }

        static string[] ScenePaths()
        {
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++) paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }

        /// <summary>
        /// Per-file breakdown of the shipped payload. The total is what a player downloads,
        /// so it is reported on its own line for easy grepping.
        /// </summary>
        static string SizeReport(string output, BuildSummary summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[build] ==================== SIZE REPORT ====================");
            sb.AppendLine($"[build] build time: {summary.totalTime}");

            string buildFolder = Path.Combine(output, "Build");
            long total = 0;

            if (Directory.Exists(buildFolder))
            {
                string[] files = Directory.GetFiles(buildFolder);
                Array.Sort(files, StringComparer.Ordinal);

                foreach (string file in files)
                {
                    long bytes = new FileInfo(file).Length;
                    total += bytes;
                    sb.AppendLine($"[build] {Path.GetFileName(file),-44} {Kb(bytes),12}");
                }
            }

            long everything = DirectorySize(output);

            sb.AppendLine($"[build] {"— payload total —",-44} {Kb(total),12}");
            sb.AppendLine($"[build] {"— output folder total —",-44} {Kb(everything),12}");
            sb.AppendLine("[build] =====================================================");
            sb.AppendLine($"[build] PAYLOAD_BYTES={total}");
            return sb.ToString();
        }

        static long DirectorySize(string path)
        {
            long total = 0;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                total += new FileInfo(file).Length;
            return total;
        }

        static string Kb(long bytes) => $"{bytes / 1024f / 1024f:F2} MB";
    }
}
