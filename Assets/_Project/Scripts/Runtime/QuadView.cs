using UnityEngine;

namespace KeepyUppy.Runtime
{
    /// <summary>
    /// Placeholder rendering: one flat coloured quad (GDD 13.3). Built entirely in code so
    /// the project carries no prefabs, materials or sprites yet.
    /// </summary>
    public static class QuadView
    {
        static Mesh _mesh;
        static Material _material;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public static GameObject Create(Transform parent, string name, Color color,
                                        Vector2 centre, Vector2 size, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(centre.x, centre.y, z);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            go.AddComponent<MeshFilter>().sharedMesh = SharedMesh();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SharedMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);

            return go;
        }

        static Mesh SharedMesh()
        {
            if (_mesh != null) return _mesh;

            _mesh = new Mesh { name = "KeepyUppy/Quad", hideFlags = HideFlags.DontSave };
            _mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
            });
            _mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f),
            });
            _mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            _mesh.RecalculateBounds();
            return _mesh;
        }

        static Material SharedMaterial()
        {
            if (_material != null) return _material;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default");

            _material = new Material(shader)
            {
                name = "KeepyUppy/Placeholder",
                hideFlags = HideFlags.DontSave,
                enableInstancing = true,
            };
            return _material;
        }
    }
}
