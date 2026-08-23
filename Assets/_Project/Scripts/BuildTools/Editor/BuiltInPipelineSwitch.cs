using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace KeepyUppy.BuildTools
{
    /// <summary>
    /// Moves the project off URP and onto the built-in render pipeline (GDD 17.2).
    ///
    /// URP was most of the 2026-08-22 build's asset payload — film grain, blue noise, SMAA
    /// and post-processing shaders, none of which a game made of untextured quads uses. It
    /// also drags the physics and terrain modules back into the build as dependencies.
    ///
    /// Run this BEFORE deleting the URP settings assets, or the project is left pointing at
    /// objects that no longer exist.
    /// </summary>
    public static class BuiltInPipelineSwitch
    {
        /// <summary>
        /// Fallback shader for the placeholder quads. Built-in, and it honours the _Color
        /// property the quad renderer already sets alongside URP's _BaseColor.
        /// </summary>
        const string QuadShader = "Unlit/Color";

        [MenuItem("KeepyUppy/Switch to built-in render pipeline")]
        public static void Switch()
        {
            ClearRenderPipelineAssets();
            EnsureShaderAlwaysIncluded(QuadShader);

            AssetDatabase.SaveAssets();
            Debug.Log("[pipeline] switched to built-in; delete Assets/Settings and drop the URP package next");
        }

        /// <summary>
        /// Nulls the SRP asset on the graphics settings and on every quality level. Miss a
        /// quality level and that level silently keeps trying to use URP.
        /// </summary>
        static void ClearRenderPipelineAssets()
        {
            GraphicsSettings.defaultRenderPipeline = null;

            int original = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;

            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, applyExpensiveChanges: false);
                QualitySettings.renderPipeline = null;
                Debug.Log($"[pipeline] cleared render pipeline on quality level '{names[i]}'");
            }

            QualitySettings.SetQualityLevel(original, applyExpensiveChanges: false);
        }

        /// <summary>
        /// Adds a shader to Always Included Shaders.
        ///
        /// Shader.Find works in the editor for any shader in the project, but in a player it
        /// only finds shaders that something references, that live in Resources, or that are
        /// on this list. The quads are built entirely in code and reference nothing, so
        /// without this the build renders magenta — and only in the build, which is the worst
        /// possible time to discover it.
        /// </summary>
        static void EnsureShaderAlwaysIncluded(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[pipeline] shader '{shaderName}' not found");
                return;
            }

            var settings = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);

            SerializedProperty list = settings.FindProperty("m_AlwaysIncludedShaders");

            var existing = new List<Shader>();
            for (int i = 0; i < list.arraySize; i++)
                existing.Add(list.GetArrayElementAtIndex(i).objectReferenceValue as Shader);

            if (existing.Contains(shader))
            {
                Debug.Log($"[pipeline] '{shaderName}' already in Always Included Shaders");
                return;
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            settings.ApplyModifiedProperties();

            Debug.Log($"[pipeline] added '{shaderName}' to Always Included Shaders");
        }
    }
}
