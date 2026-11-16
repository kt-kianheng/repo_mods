using System.Collections.Generic;
using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>Shared helpers for turning a vanilla prefab into a new item (copied from HatterValuablesPack's NukeBuilder).</summary>
    internal static class BuildHelpers
    {
        /// <summary>The enabled mesh renderers' combined box in <paramref name="root"/>'s own space (works on inactive prefabs).</summary>
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

        internal static void LogComponents(GameObject go, string label, string source)
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

        /// <summary>Removes every solid collider (and the game's grab helpers on them) and adds fresh boxes.</summary>
        internal static void ReplaceColliders(Transform root, (Vector3 centre, Quaternion rotation, Vector3 size)[] boxes, string label, string source)
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

        internal static Sound MakeSound(Sound template, AudioClip clip, float volume)
        {
            if (clip == null) return null;
            var s = new Sound();
            if (template != null) Sound.CopySound(template, s);
            s.Sounds = new[] { clip };
            s.Volume = volume;
            return s;
        }

        private static Material neutral;
        private static bool neutralTried;

        /// <summary>
        /// A plain lit material to make the models' materials from: the shotgun's (as HatterWeaponsPack's wands use).
        /// Some items' own materials (the health pack's) carry a red glow that tints everything made from them.
        /// </summary>
        internal static Material NeutralTemplate(Material fallback)
        {
            if (!neutralTried)
            {
                neutralTried = true;
                GameObject shotgun = Resources.Load<GameObject>("Items/Item Gun Shotgun");
                Transform mesh = shotgun != null ? shotgun.transform.Find("Mesh") : null;
                Renderer r = mesh != null ? mesh.GetComponentInChildren<Renderer>(true) : null;
                neutral = r != null ? r.sharedMaterial : null;
                Plugin.Log.LogInfo($"Material template: {(neutral != null ? $"'{neutral.name}' ({neutral.shader?.name})" : "the shotgun's wasn't found, using each item's own")}.");
            }
            return neutral != null ? neutral : fallback;
        }

        /// <summary>The solid colliders' combined box in <paramref name="root"/>'s own space (works on inactive prefabs).</summary>
        internal static Bounds ColliderBounds(Transform root, out int count)
        {
            count = 0;
            Bounds b = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                Vector3 centre, size;
                if (c is BoxCollider box) { centre = box.center; size = box.size; }
                else if (c is SphereCollider sphere) { centre = sphere.center; size = Vector3.one * sphere.radius * 2f; }
                else if (c is CapsuleCollider cap)
                {
                    centre = cap.center;
                    size = Vector3.one * cap.radius * 2f;
                    size[cap.direction] = Mathf.Max(cap.height, cap.radius * 2f);
                }
                else if (c is MeshCollider mc && mc.sharedMesh != null) { centre = mc.sharedMesh.bounds.center; size = mc.sharedMesh.bounds.size; }
                else continue;
                Transform t = c.transform;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = centre + Vector3.Scale(size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.InverseTransformPoint(t.TransformPoint(corner));
                    if (count == 0 && i == 0) b = new Bounds(p, Vector3.zero); else b.Encapsulate(p);
                }
                count++;
            }
            return b;
        }

        /// <summary>Switches off every listener the prefab wired to an item's use button (they'd call scripts that were removed).</summary>
        internal static int MuteToggleListeners(ItemToggle toggle)
        {
            if (toggle == null || toggle.onToggle == null) return 0;
            int n = toggle.onToggle.GetPersistentEventCount();
            for (int i = 0; i < n; i++) toggle.onToggle.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            return n;
        }
    }
}
