using System.Collections.Generic;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// Builds the A-Bomb prefab at startup from the vanilla piano (a wide valuable): it keeps the piano's grabbing,
    /// value, impacts, room tracking and networking; the piano's look, keys and colliders are replaced with the bomb's.
    /// </summary>
    internal static class NukeBuilder
    {
        internal const string DisplayName = "A-Bomb";
        internal const string PrefabName = "Valuable Hatter A-Bomb";
        private const string PianoPath = "Valuables/05 Wide/Valuable Manor Piano";
        // metal impact sounds, first one found wins
        private static readonly string[] MetalSoundFrom =
        {
            "Valuables/03 Medium/Valuable Arctic Propane Tank",
            "Valuables/04 Big/Valuable Arctic Barrel",
            "Valuables/03 Medium/Valuable Arctic Fire Extinguisher",
        };

        /// <summary>The model's length along its axis in model units (tools/build_nuke.py: TOTAL).</summary>
        internal const float ModelLength = 2.25f;

        internal static PrefabRef Registered;
        private static GameObject holder;

        /// <summary>Is this prefab reference the A-Bomb?</summary>
        internal static bool Is(PrefabRef p) =>
            p != null && Registered != null && (p == Registered || p.ResourcePath == Registered.ResourcePath);

        // ---- the model's parts, in model space (Unity axes: nose towards -X, up +Y, floor at y = 0) ----
        // converted from the Blender script's coordinates: Blender (x, y, z) -> Unity (-x, z, -y)
        private static Vector3 U(float bx, float by, float bz) => new Vector3(-bx, bz, -by);
        private static Vector3 S(float sx, float sy, float sz) => new Vector3(sx, sz, sy);

        private static readonly (Vector3 centre, Vector3 size)[] ModelBoxes =
        {
            (U(0.42f, 0, 0.68f), S(1.25f, 0.98f, 0.98f)),     // body core
            (U(1.00f, 0, 0.68f), S(0.25f, 0.62f, 0.62f)),     // nose
            (U(-0.33f, 0, 0.68f), S(0.20f, 0.62f, 0.62f)),    // rear of the body
            (U(-0.55f, 0, 0.68f), S(0.26f, 0.36f, 0.36f)),    // cone
            (U(-0.90f, 0, 0.68f), S(0.45f, 1.03f, 1.03f)),    // tail box
            (U(0.38f, 0, 0.12f), S(1.00f, 0.64f, 0.24f)),     // cradle
        };
        /// <summary>Where smoke leaks out while armed: the nose seam (top and both sides), the tail cone, the top cover.</summary>
        internal static readonly Vector3[] SmokePoints =
        {
            U(0.91f, 0, 1.08f), U(0.91f, 0.38f, 0.85f), U(0.91f, -0.38f, 0.85f),
            U(-0.55f, 0, 0.86f), U(0.23f, 0, 1.20f),
        };
        internal static readonly Vector3 BlastCentre = U(0.35f, 0, 0.68f);

        internal static GameObject Build()
        {
            GameObject piano = Resources.Load<GameObject>(PianoPath);
            if (piano == null) { Plugin.Log.LogError($"'{PianoPath}' not found; the {DisplayName} can't be built."); return null; }
            MeshLibrary.Model model = MeshLibrary.Get("nuke.mesh", "Nuke");
            if (model == null) return null;

            if (holder == null)
            {
                holder = new GameObject("HatterValuablesPack.PrefabHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject go = Object.Instantiate(piano, holder.transform);
            go.name = PrefabName;
            Transform root = go.transform;

            // the piano's size, measured from its meshes in its own space (before anything is hidden)
            Bounds piano3 = LocalMeshBounds(root);
            Plugin.Log.LogInfo($"{DisplayName}: the piano measures {piano3.size.x:0.00} x {piano3.size.y:0.00} x {piano3.size.z:0.00} m (centre {piano3.center}).");

            LogComponents(go);
            // the piano's own behaviour (playable keys) goes; everything else (grab, value, impacts, network) stays
            foreach (var c in go.GetComponentsInChildren<MusicalValuableLogic>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<PhysGrabObjectGrabArea>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<AudioSource>(true)) c.enabled = false;
            Material template = null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (template == null && r is MeshRenderer && r.sharedMaterial != null) template = r.sharedMaterial;
                r.enabled = false;
            }

            // the bomb lies along the piano's longer horizontal side, scaled to SizeVsPiano of it
            bool alongZ = piano3.size.z > piano3.size.x;
            float pianoLong = Mathf.Max(piano3.size.x, piano3.size.z);
            if (pianoLong < 0.3f) { pianoLong = 1.6f; Plugin.Log.LogWarning($"{DisplayName}: couldn't measure the piano; assuming 1.6 m."); }
            float scale = Settings.NukeSizeVsPiano.Value * pianoLong / ModelLength;
            var modelRoot = new GameObject("A-Bomb Model").transform;
            modelRoot.SetParent(root, false);
            modelRoot.gameObject.layer = go.layer;
            modelRoot.localPosition = new Vector3(piano3.center.x, piano3.min.y, piano3.center.z);
            modelRoot.localRotation = alongZ ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
            modelRoot.localScale = Vector3.one * scale;
            GameObject mesh = MeshLibrary.Attach(model, modelRoot, "A-Bomb Mesh", template);
            mesh.layer = go.layer;
            Plugin.Log.LogInfo($"{DisplayName}: {ModelLength * scale:0.00} m long (scale {scale:0.000}), {(alongZ ? "along Z" : "along X")}; material from '{(template != null ? template.name + " (" + template.shader.name + ")" : "none")}'.");

            // colliders: boxes round the body, tail and cradle
            var boxes = new (Vector3, Quaternion, Vector3)[ModelBoxes.Length];
            for (int i = 0; i < ModelBoxes.Length; i++)
                boxes[i] = (modelRoot.localPosition + modelRoot.localRotation * (ModelBoxes[i].centre * scale), modelRoot.localRotation,
                            ModelBoxes[i].size * scale);
            ReplaceColliders(root, boxes);

            // value, weight, toughness, sound
            ValuableObject valuable = go.GetComponent<ValuableObject>();
            if (valuable == null) { Plugin.Log.LogError($"{DisplayName}: the piano has no ValuableObject."); return null; }
            valuable.volumeType = ValuableVolume.Type.Wide;
            Value value = ScriptableObject.CreateInstance<Value>();
            value.name = PrefabName + " Value";
            value.valueMin = Mathf.Min(Settings.NukeValueMin.Value, Settings.NukeValueMax.Value);
            value.valueMax = Mathf.Max(Settings.NukeValueMin.Value, Settings.NukeValueMax.Value);
            valuable.valuePreset = value;

            PhysAttribute weight = ScriptableObject.CreateInstance<PhysAttribute>();
            weight.name = PrefabName + " Weight";
            float pianoMass = valuable.physAttributePreset != null ? valuable.physAttributePreset.mass : 0f;
            weight.mass = (pianoMass > 0f ? pianoMass : 10f) * Settings.NukeWeightVsPiano.Value;
            valuable.physAttributePreset = weight;

            // toughness: bumps never cost money (the mod also skips every value loss, see NukePatches)
            Durability tough = ScriptableObject.CreateInstance<Durability>();
            tough.name = PrefabName + " Durability";
            tough.durability = 100f;
            tough.fragility = valuable.durabilityPreset != null ? valuable.durabilityPreset.fragility : 50f;
            valuable.durabilityPreset = tough;

            PhysAudio metal = null;
            string metalFrom = null;
            foreach (string path in MetalSoundFrom)
            {
                ValuableObject v = Resources.Load<GameObject>(path)?.GetComponent<ValuableObject>();
                if (v != null && v.audioPreset != null) { metal = v.audioPreset; metalFrom = path; break; }
            }
            if (metal != null) { valuable.audioPreset = metal; valuable.audioPresetPitch = 0.75f; }
            else Plugin.Log.LogWarning($"{DisplayName}: no metal impact sound found; it keeps the piano's.");

            var debris = new Gradient();
            debris.SetKeys(new[] { new GradientColorKey(new Color(0.2f, 0.21f, 0.18f), 0f), new GradientColorKey(new Color(0.45f, 0.45f, 0.43f), 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            valuable.particleColors = debris;

            Nuke nuke = go.AddComponent<Nuke>();
            nuke.modelRoot = modelRoot;
            Plugin.Log.LogInfo($"{DisplayName}: ${value.valueMin:0}–{value.valueMax:0}, {weight.mass:0.0} kg (piano {pianoMass:0.0}), impact sound from '{metalFrom ?? "the piano"}'.");
            return go;
        }

        /// <summary>Bounds of every mesh under root, in root's space.</summary>
        internal static Bounds LocalMeshBounds(Transform root)
        {
            bool any = false;
            var b = new Bounds();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        internal static void LogComponents(GameObject go, string label = DisplayName, string source = "the piano")
        {
            var sb = new System.Text.StringBuilder($"{label}: {source}'s parts:");
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                var names = new List<string>();
                foreach (Component c in t.GetComponents<Component>()) if (c != null && !(c is Transform)) names.Add(c.GetType().Name);
                sb.Append($"\n  {t.name}: {string.Join(", ", names)}");
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        /// <summary>
        /// Replaces the piano's colliders with boxes (root-space centre, rotation, size) under the root, copying a grab
        /// collider's tag, layer and physics material, each with the game's PhysGrabObjectCollider.
        /// </summary>
        internal static void ReplaceColliders(Transform root, (Vector3 centre, Quaternion rotation, Vector3 size)[] boxes, string label = DisplayName, string source = "the piano")
        {
            var solid = new List<Collider>();
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger) solid.Add(c);
            Collider template = null;
            foreach (Collider c in solid)
                if (c.GetComponent<PhysGrabObjectCollider>() != null && c.CompareTag("Phys Grab Object")) { template = c; break; }
            if (template == null && solid.Count > 0) template = solid[0];
            string tag = template != null ? template.tag : "Phys Grab Object";
            int layer = template != null ? template.gameObject.layer : root.gameObject.layer;
            PhysicsMaterial material = template != null ? template.sharedMaterial : null;

            var log = new System.Text.StringBuilder($"{label}: {source}'s colliders (removed):");
            foreach (Collider c in solid) log.Append($"\n  '{c.name}' {c.GetType().Name}, tag {c.tag}, layer {LayerMask.LayerToName(c.gameObject.layer)}");
            Plugin.Log.LogInfo(log.ToString());

            foreach (Collider c in solid)
            {
                GameObject g = c.gameObject;
                foreach (var h in g.GetComponents<PhysGrabObjectBoxCollider>()) Object.DestroyImmediate(h);
                foreach (var h in g.GetComponents<PhysGrabObjectCapsuleCollider>()) Object.DestroyImmediate(h);
                foreach (var h in g.GetComponents<PhysGrabObjectSphereCollider>()) Object.DestroyImmediate(h);
                foreach (var h in g.GetComponents<PhysGrabObjectMeshCollider>()) Object.DestroyImmediate(h);
                // and the game's grab-collider marker: PhysGrabObject.Start lists every PhysGrabObjectCollider and later sets
                // each one's Collider material; a marker left on a removed collider threw every frame (OverrideTimersTick),
                // which stopped the rest of PhysGrabObject.Update (broken items stayed behind, grabbing misbehaved)
                foreach (var h in g.GetComponents<PhysGrabObjectCollider>()) Object.DestroyImmediate(h);
            }
            foreach (Collider c in solid) Object.DestroyImmediate(c);

            for (int i = 0; i < boxes.Length; i++)
            {
                var host = new GameObject($"{label} Collider {i + 1}") { tag = tag, layer = layer };
                Transform t = host.transform;
                t.SetParent(root, false);
                t.localPosition = boxes[i].centre;
                t.localRotation = boxes[i].rotation;
                BoxCollider box = host.AddComponent<BoxCollider>();
                box.size = boxes[i].size;
                box.sharedMaterial = material;
                host.AddComponent<PhysGrabObjectCollider>();
            }
            Plugin.Log.LogInfo($"{label}: {boxes.Length} colliders, tag {tag}, layer {LayerMask.LayerToName(layer)}.");
        }
    }
}
