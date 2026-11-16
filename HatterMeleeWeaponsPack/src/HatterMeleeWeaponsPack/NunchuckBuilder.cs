using System.Collections.Generic;
using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// Builds the Nunchuck at startup from the game's baseball bat: it keeps the bat's grabbing, melee grip and swing
    /// sounds, shop entry and networking. The bat's mesh is hidden and its own hurt zone switched off; its grab
    /// collider is refitted to the held handle (50 cm, starting just below the bat's grip point). The chain and the free
    /// handle are made at runtime by <see cref="Nunchuck"/>, as separate physics bodies.
    /// </summary>
    internal static class NunchuckBuilder
    {
        private const string BatPath = "Items/Item Melee Baseball Bat";
        internal const string PrefabName = "Item Melee Nunchuck";
        internal const string DisplayName = "Nunchuck";
        internal const string ModelFile = "nunchuck.mesh";

        internal const float HandleLength = 0.50f;
        internal const float HandleWidth = 0.051f;
        internal const float RingTop = 0.5232f;       // where the chain hangs from, along the handle (the eye ring; RING_TOP in build_nunchuck.py)
        private const float GripInset = 0.08f;        // the bat's grip point sits this far up from the handle's end

        private static GameObject holder;

        internal static GameObject Build()
        {
            GameObject bat = Resources.Load<GameObject>(BatPath);
            if (bat == null) { Plugin.Log.LogError($"'{BatPath}' not found."); return null; }
            if (MeshLibrary.Get(ModelFile, "NC_Handle") == null || MeshLibrary.Get(ModelFile, "NC_Link") == null) return null;

            if (holder == null)
            {
                holder = new GameObject("HatterMeleeWeaponsPack.PrefabHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject go = Object.Instantiate(bat, holder.transform);
            go.name = PrefabName;
            Transform root = go.transform;

            SetupItem(go);

            // where the bat is gripped and which way it points (both in item space)
            Transform grab = root.Find("Force Grab Point");
            Transform swing = root.Find("Swing Point");
            Bounds batBounds = LocalRendererBounds(root);
            Material template = PickTemplate(root);
            Vector3 grip = grab != null ? grab.localPosition : batBounds.center - LongAxis(batBounds) * batBounds.extents.magnitude * 0.8f;
            Vector3 axis = swing != null && (swing.localPosition - grip).sqrMagnitude > 1e-4f
                ? (swing.localPosition - grip).normalized
                : LongAxis(batBounds) * Mathf.Sign(Vector3.Dot(batBounds.center - grip, LongAxis(batBounds)) + 1e-6f);
            Vector3 handleBottom = grip - axis * GripInset;
            Quaternion handleRotation = Quaternion.FromToRotation(Vector3.up, axis);
            Plugin.Log.LogInfo($"Nunchuck from the bat: grip {grip}, axis {axis} (grip point {(grab != null ? "found" : "guessed")}, swing point {(swing != null ? "found" : "guessed")}).");

            HideBatMesh(root);
            DisableBatHurtZones(root);
            MuteBatDurabilityHooks(root);
            FitGrabCollider(root, handleBottom + axis * (HandleLength / 2f), handleRotation);
            if (swing != null) swing.localPosition = handleBottom + axis * HandleLength;   // swing sounds follow the handle's tip

            // the held handle's model
            var model = MeshLibrary.Get(ModelFile, "NC_Handle");
            GameObject mesh = MeshLibrary.Attach(model, root, "Nunchuck Held Handle", template);
            mesh.transform.localPosition = handleBottom;
            mesh.transform.localRotation = handleRotation;

            // the chain and free handle are built by this at runtime; it needs to know where the chain hangs from
            var nunchuck = go.AddComponent<Nunchuck>();
            nunchuck.chainAnchor = handleBottom + axis * RingTop;
            nunchuck.handleAxis = axis;
            nunchuck.handleBottom = handleBottom;
            nunchuck.handleRotation = handleRotation;
            Nunchuck.Template = template;
            return go;
        }

        private static void SetupItem(GameObject go)
        {
            ItemAttributes attributes = go.GetComponent<ItemAttributes>();
            Item source = attributes.item;
            Item item = Object.Instantiate(source);
            item.name = PrefabName;
            item.itemName = DisplayName;
            item.itemNameLocalized = null;
            item.description = "Two 50 cm hardwood handles on a 40 cm steel chain. Hold one end and swing: the faster the free end is moving, the harder it hits.";
            if (source.value != null)
            {
                Value price = Object.Instantiate(source.value);
                price.name = PrefabName + " Value";
                price.valueMin *= Settings.NunchuckPriceMultiplier.Value;
                price.valueMax *= Settings.NunchuckPriceMultiplier.Value;
                item.value = price;
            }
            attributes.item = item;
            attributes.icon = null; // rendered by the game from the new model
        }

        /// <summary>
        /// The bat's own painted material (the game's regular shader), to tint for our model. Taken from its visible
        /// mesh ("Mesh Healthy"), not just any renderer: some of the bat's renderers are effects with a wireframe
        /// shader, which made the first version a red wireframe.
        /// </summary>
        internal static Material PickTemplate(Transform root)
        {
            var log = new System.Text.StringBuilder("Baseball bat renderers:");
            Material best = null;
            float bestSize = -1f;
            Transform healthy = root.Find("Mesh Healthy");
            foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material m = mr.sharedMaterial;
                string shader = m != null && m.shader != null ? m.shader.name : "none";
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                float size = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size.magnitude * mr.transform.lossyScale.magnitude : 0f;
                bool onBody = healthy != null && mr.transform.IsChildOf(healthy);
                string sl = shader.ToLowerInvariant();
                bool plain = m != null && !sl.Contains("wire") && !sl.Contains("outline") && !sl.Contains("holo") && !sl.Contains("unlit")
                             && !sl.Contains("sprite") && !sl.Contains("particle") && mr.enabled;
                log.Append($"\n  {mr.name} [{(m != null ? m.name : "no material")}, shader {shader}]{(onBody ? " body" : "")}{(plain ? "" : " (skipped)")}");
                if (!plain) continue;
                float score = size + (onBody ? 1000f : 0f);   // prefer the bat's body, then the biggest mesh
                if (score > bestSize) { bestSize = score; best = m; }
            }
            Plugin.Log.LogInfo(log.ToString());
            Plugin.Log.LogInfo(best != null ? $"Nunchuck uses material '{best.name}' (shader {best.shader.name})." : "No usable bat material; the nunchuck uses a default one.");
            return best;
        }

        /// <summary>Bounds of the bat's visible mesh in item space.</summary>
        internal static Bounds LocalRendererBounds(Transform root)
        {
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        internal static Vector3 LongAxis(Bounds b)
        {
            Vector3 s = b.size;
            return s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
        }

        internal static void HideBatMesh(Transform root)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer || r is SkinnedMeshRenderer) r.enabled = false;
        }

        /// <summary>The bat's own hurt zone would hit with the bat's damage; the nunchuck does its own damage instead.</summary>
        internal static void DisableBatHurtZones(Transform root)
        {
            foreach (HurtCollider h in root.GetComponentsInChildren<HurtCollider>(true))
            {
                h.enemyLogic = false; h.enemyDamage = 0; h.enemyStun = false; h.enemyHitForce = 0f; h.enemyHitTorque = 0f;
                h.playerLogic = false; h.playerDamage = 0; h.playerHitForce = 0f; h.playerTumbleForce = 0f;
                h.physLogic = false; h.physHitForce = 0f; h.physHitTorque = 0f;
            }
        }

        /// <summary>
        /// The bat loses durability (its charge) through events wired up in the prefab, e.g. every time it bumps
        /// something (GroundHit). Those are switched off, so the nunchuck only uses charge on hits that count, which
        /// Nunchuck does itself.
        /// </summary>
        internal static void MuteBatDurabilityHooks(Transform root)
        {
            var drains = new HashSet<string> { "SwingHit", "GroundHit", "EnemySwingHit", "PlayerSwingHit" };
            int muted = 0;
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                for (System.Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                                  System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(f.FieldType)) continue;
                        if (!(f.GetValue(mb) is UnityEngine.Events.UnityEventBase evt)) continue;
                        for (int i = 0; i < evt.GetPersistentEventCount(); i++)
                        {
                            if (evt.GetPersistentTarget(i) is ItemMelee && drains.Contains(evt.GetPersistentMethodName(i)))
                            {
                                evt.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                                muted++;
                            }
                        }
                    }
                }
            }
            Plugin.Log.LogInfo($"Nunchuck: switched off {muted} of the bat's durability hooks (charge is only used on hits that count).");
        }

        /// <summary>One grab collider on the held handle (the bat's own are removed, see ReplaceGrabColliders).</summary>
        private static void FitGrabCollider(Transform root, Vector3 center, Quaternion rotation)
        {
            ReplaceGrabColliders(root, "Nunchuck", new[] { (center, rotation, new Vector3(HandleWidth, HandleLength, HandleWidth)) });
        }

        /// <summary>
        /// Replaces the bat's grab colliders with fresh boxes (item-space centre, rotation, size), each on its own new
        /// object straight under the item. The bat's own colliders are removed, not just switched off: the game turns
        /// every collider of an item back on when it comes out of an inventory slot (ItemEquippable's unequip), and one
        /// of the bat's colliders is a special "unequip" collider that the game switches off when the item spawns. Reusing
        /// those objects left a grab collider switched off, or bat-shaped ones switched back on.
        /// The new boxes copy the working grab collider's tag, layer and physics material, and get the game's
        /// PhysGrabObjectCollider (the game looks it up on whatever you grab).
        /// </summary>
        internal static Transform[] ReplaceGrabColliders(Transform root, string label, (Vector3 centre, Quaternion rotation, Vector3 size)[] boxes)
        {
            var solid = new List<Collider>();
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger && c.GetComponent<HurtCollider>() == null) solid.Add(c);
            if (solid.Count == 0) { Plugin.Log.LogWarning($"The bat has no grab collider; the {label} can't be picked up."); return null; }

            // the template: a normal grab collider (tagged for grabbing, not the unequip one)
            Collider template = solid[0];
            foreach (Collider c in solid)
            {
                var box = c.GetComponent<PhysGrabObjectBoxCollider>();
                if (box != null && box.unEquipCollider) continue;
                if (c.GetComponent<PhysGrabObjectCollider>() == null) continue;
                if (!c.CompareTag("Phys Grab Object")) continue;
                template = c;
                break;
            }
            string tag = template.tag;
            int layer = template.gameObject.layer;
            PhysicsMaterial material = template.sharedMaterial;

            var log = new System.Text.StringBuilder($"{label}: the bat's grab colliders (removed):");
            foreach (Collider c in solid)
            {
                var box = c.GetComponent<PhysGrabObjectBoxCollider>();
                log.Append($"\n  '{c.name}' {c.GetType().Name}, tag {c.tag}, layer {LayerMask.LayerToName(c.gameObject.layer)}"
                           + $"{(box != null && box.unEquipCollider ? ", unequip collider" : "")}{(c == template ? " (template)" : "")}");
            }
            Plugin.Log.LogInfo(log.ToString());

            // the game's helpers that sit next to a collider go first (they may require it)
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

            var hosts = new Transform[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                var host = new GameObject($"{label} Grab Collider {i + 1}");
                host.tag = tag;
                host.layer = layer;
                Transform t = host.transform;
                t.SetParent(root, false);
                t.localPosition = boxes[i].centre;
                t.localRotation = boxes[i].rotation;
                t.localScale = Vector3.one;
                BoxCollider box = host.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = boxes[i].size;
                box.sharedMaterial = material;
                host.AddComponent<PhysGrabObjectCollider>();
                hosts[i] = t;
            }
            Plugin.Log.LogInfo($"{label}: {boxes.Length} grab collider(s), tag {tag}, layer {LayerMask.LayerToName(layer)}.");
            return hosts;
        }
    }
}
