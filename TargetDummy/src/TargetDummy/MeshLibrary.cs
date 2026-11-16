using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace TargetDummy
{
    /// <summary>
    /// Loads models exported from Blender (tools/build_stand.py, "HWM1" format, same as HatterWeaponsPack) that are
    /// embedded in the DLL. Vertices are already in Unity space (Y up), one sub-mesh per Blender material.
    /// </summary>
    internal static class MeshLibrary
    {
        internal class Part
        {
            public string MaterialName;
            public Color Color;
            public float Smoothness;
            public float Metallic;
        }

        internal class Model
        {
            public string Name;
            public Mesh Mesh;
            public List<Part> Parts = new List<Part>();
        }

        private static readonly Dictionary<string, Dictionary<string, Model>> cache = new Dictionary<string, Dictionary<string, Model>>();

        internal static Model Get(string file, string objectName)
        {
            if (!cache.TryGetValue(file, out var models)) cache[file] = models = Load(file);
            return models != null && models.TryGetValue(objectName, out var m) ? m : null;
        }

        private static Dictionary<string, Model> Load(string file)
        {
            string resource = "TargetDummy.Assets." + file;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) { Plugin.Log.LogError($"Embedded model '{resource}' is missing."); return null; }
                using (var r = new BinaryReader(s))
                {
                    if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "HWM1") { Plugin.Log.LogError($"'{file}' is not a HWM1 model."); return null; }
                    var result = new Dictionary<string, Model>();
                    int objectCount = r.ReadInt32();
                    for (int o = 0; o < objectCount; o++)
                    {
                        var model = new Model { Name = ReadString(r) };
                        var verts = new List<Vector3>();
                        var normals = new List<Vector3>();
                        var subIndices = new List<int[]>();
                        int subCount = r.ReadInt32();
                        for (int sm = 0; sm < subCount; sm++)
                        {
                            var part = new Part { MaterialName = ReadString(r) };
                            part.Color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                            part.Smoothness = r.ReadSingle();
                            part.Metallic = r.ReadSingle();
                            model.Parts.Add(part);

                            int baseIndex = verts.Count;
                            int vc = r.ReadInt32();
                            for (int v = 0; v < vc; v++)
                            {
                                verts.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                                normals.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                            }
                            int ic = r.ReadInt32();
                            var idx = new int[ic];
                            for (int i = 0; i < ic; i++) idx[i] = r.ReadInt32() + baseIndex;
                            subIndices.Add(idx);
                        }

                        var mesh = new Mesh { name = model.Name };
                        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        mesh.SetVertices(verts);
                        mesh.SetNormals(normals);
                        mesh.subMeshCount = subIndices.Count;
                        for (int sm = 0; sm < subIndices.Count; sm++) mesh.SetTriangles(subIndices[sm], sm, false);
                        mesh.RecalculateBounds();
                        mesh.UploadMeshData(false);
                        model.Mesh = mesh;
                        result[model.Name] = model;
                    }
                    return result;
                }
            }
        }

        private static string ReadString(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));

        /// <summary>Adds a renderer for the model under parent, using the game's own shader (copied from template).</summary>
        internal static GameObject Attach(Model model, Transform parent, string name, Material template)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[model.Parts.Count];
            for (int i = 0; i < mats.Length; i++) mats[i] = MakeMaterial(template, model.Parts[i]);
            mr.sharedMaterials = mats;
            return go;
        }

        private static readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();

        private static Material MakeMaterial(Material template, Part part)
        {
            if (materialCache.TryGetValue(part.MaterialName, out var cached) && cached != null) return cached;
            var m = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
            m.name = "TargetDummy " + part.MaterialName;
            foreach (string tex in new[] { "_MainTex", "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_EmissionMap", "_OcclusionMap" })
                if (m.HasProperty(tex)) m.SetTexture(tex, null);
            foreach (string col in new[] { "_Color", "_BaseColor" })
                if (m.HasProperty(col)) m.SetColor(col, part.Color);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", part.Metallic);
            foreach (string s in new[] { "_Smoothness", "_Glossiness" })
                if (m.HasProperty(s)) m.SetFloat(s, part.Smoothness);
            materialCache[part.MaterialName] = m;
            return m;
        }
    }
}
