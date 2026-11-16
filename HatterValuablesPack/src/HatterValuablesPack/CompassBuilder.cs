using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// Builds the two compass valuables at startup from a vanilla small valuable (its grabbing, value, impacts, room
    /// tracking and networking). The original's look, special behaviour and colliders are replaced with the compass's
    /// (tools/build_compass.py): a glass sphere in an hourglass-shaped brass frame, a needle floating in the sphere that
    /// points in 3D, and an emblem on the cap: a red eye (Enemy Compass) or a green dollar sign (Extraction Compass),
    /// replaced at runtime by the game's own Robe / the level's extraction screen when those can be copied.
    /// </summary>
    internal static class CompassBuilder
    {
        internal const string EnemyName = "Enemy Compass", ExtractionName = "Extraction Compass";
        private static readonly string[] BasePaths =
        {
            "Valuables/02 Small/Valuable Desert Gold Bar",
            "Valuables/02 Small/Valuable Arctic Hdd",
            "Valuables/02 Small/Valuable Arctic Calculator",
            "Valuables/02 Small/Valuable Manor Globe",
        };
        // sound and weight from a small brass-and-glass valuable
        private const string WatchPath = "Valuables/01 Tiny/Valuable Manor Pocket Watch";
        // toughness: as fragile as the power crystal (user's request)
        private const string CrystalPath = "Valuables/03 Medium/Valuable Wizard Power Crystal";

        // model points (tools/build_compass.py), Unity axes: up +Y
        internal static readonly Vector3 SphereCentre = new Vector3(0f, 0.046f, 0f);
        internal static readonly Vector3 Top = new Vector3(0f, 0.090f, 0f);
        private const float FrameWidth = 0.083f;
        internal const float FrameHeight = 0.090f;
        /// <summary>The emblem's height in model units: EmblemVsCompass x the frame's height.</summary>
        internal static float EmblemHeight => FrameHeight * Settings.CompassEmblemVsCompass.Value;
        private const float BuiltInEmblemHeight = 0.03f;   // the eye and the dollar sign as modelled
        internal const string SimpleEmblemName = "Emblem (built in)";

        private static GameObject holder;

        internal static GameObject BuildEnemy() => Build(EnemyName, "Valuable Hatter Enemy Compass", Compass.Kind.Enemy,
            Settings.EnemyCompassValueMin.Value, Settings.EnemyCompassValueMax.Value);

        internal static GameObject BuildExtraction() => Build(ExtractionName, "Valuable Hatter Extraction Compass", Compass.Kind.Extraction,
            Settings.ExtractionCompassValueMin.Value, Settings.ExtractionCompassValueMax.Value);

        private static GameObject Build(string label, string prefabName, Compass.Kind kind, int valueMin, int valueMax)
        {
            GameObject source = null;
            string sourcePath = null;
            foreach (string p in BasePaths)
            {
                source = Resources.Load<GameObject>(p);
                if (source != null && source.GetComponent<ValuableObject>() != null) { sourcePath = p; break; }
                source = null;
            }
            if (source == null) { Plugin.Log.LogError($"{label}: no small vanilla valuable found to build it from."); return null; }
            MeshLibrary.Model frame = MeshLibrary.Get("compass.mesh", "Compass_Frame");
            MeshLibrary.Model glass = MeshLibrary.Get("compass.mesh", "Compass_Glass");
            MeshLibrary.Model needle = MeshLibrary.Get("compass.mesh", "Compass_Needle");
            MeshLibrary.Model emblemModel = MeshLibrary.Get("compass.mesh", kind == Compass.Kind.Enemy ? "Compass_EmblemEnemy" : "Compass_EmblemExtraction");
            if (frame == null || needle == null) return null;

            if (holder == null)
            {
                holder = new GameObject("HatterValuablesPack.CompassHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject go = Object.Instantiate(source, holder.transform);
            go.name = prefabName;
            Transform root = go.transform;
            Bounds orig = NukeBuilder.LocalMeshBounds(root);
            string from = $"'{sourcePath}'";
            NukeBuilder.LogComponents(go, label, from);

            // the original's own behaviour goes (anything named like a valuable's script, and traps); effects stop
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb is ValuableObject) continue;
                string n = mb.GetType().Name;
                bool core = n.StartsWith("ValuableDiscover") || n == "MapValuable" || n == "NotValuableObject";
                if (mb is Trap || (n.Contains("Valuable") && !core) || mb is MusicalValuableLogic)
                {
                    Plugin.Log.LogInfo($"{label}: removed {n}.");
                    Object.DestroyImmediate(mb);
                }
            }
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
            Material template = null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (template == null && r is MeshRenderer && r.sharedMaterial != null) template = r.sharedMaterial;
                r.enabled = false;
            }

            // the model, standing on the original's floor point
            float scale = Settings.CompassScale.Value;
            var modelRoot = new GameObject("Compass Model").transform;
            modelRoot.SetParent(root, false);
            modelRoot.gameObject.layer = go.layer;
            modelRoot.localPosition = new Vector3(orig.center.x, orig.min.y, orig.center.z);
            modelRoot.localScale = Vector3.one * scale;
            MeshLibrary.Attach(frame, modelRoot, "Compass Frame", template).layer = go.layer;
            if (glass != null) MeshLibrary.Attach(glass, modelRoot, "Compass Glass", template).layer = go.layer;

            var pivot = new GameObject("Needle Pivot").transform;   // the needle floats in the middle of the sphere
            pivot.SetParent(modelRoot, false);
            pivot.localPosition = SphereCentre;
            pivot.gameObject.layer = go.layer;
            MeshLibrary.Attach(needle, pivot, "Needle", template).layer = go.layer;
            // a small red glow at the red tip (the needle's tip is its local -Z, 2.6 cm out in model units)
            var tipLight = new GameObject("Needle Tip Glow");
            tipLight.transform.SetParent(pivot, false);
            tipLight.transform.localPosition = new Vector3(0f, 0f, -0.024f);
            tipLight.layer = go.layer;
            Light glow = tipLight.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.08f, 0.05f);
            glow.range = 0.05f * scale;     // about a quarter metre at the default size
            glow.intensity = 1.5f;
            glow.shadows = LightShadows.None;

            var emblem = new GameObject("Emblem").transform;
            emblem.SetParent(modelRoot, false);
            emblem.localPosition = Top;
            emblem.gameObject.layer = go.layer;
            // the built-in emblem (red eye / green dollar); hidden if the game's Robe or extraction screen is put there instead
            if (emblemModel != null)
            {
                GameObject built = MeshLibrary.Attach(emblemModel, emblem, SimpleEmblemName, template);
                built.layer = go.layer;
                built.transform.localScale = Vector3.one * (EmblemHeight / BuiltInEmblemHeight);
            }

            // colliders: the frame and the emblem
            Vector3 at = modelRoot.localPosition;
            NukeBuilder.ReplaceColliders(root, new (Vector3, Quaternion, Vector3)[]
            {
                (at + new Vector3(0f, FrameHeight / 2, 0f) * scale, Quaternion.identity, new Vector3(FrameWidth, FrameHeight, FrameWidth) * scale),
                (at + (Top + Vector3.up * EmblemHeight / 2) * scale, Quaternion.identity, new Vector3(EmblemHeight * 0.6f, EmblemHeight, EmblemHeight * 0.6f) * scale),
            }, label, from);

            // value, weight, toughness, sound
            ValuableObject valuable = go.GetComponent<ValuableObject>();
            valuable.volumeType = ValuableVolume.Type.Medium;
            Value value = ScriptableObject.CreateInstance<Value>();
            value.name = prefabName + " Value";
            value.valueMin = Mathf.Min(valueMin, valueMax);
            value.valueMax = Mathf.Max(valueMin, valueMax);
            valuable.valuePreset = value;
            ValuableObject watch = Resources.Load<GameObject>(WatchPath)?.GetComponent<ValuableObject>();
            if (watch != null)
            {
                if (watch.audioPreset != null) { valuable.audioPreset = watch.audioPreset; valuable.audioPresetPitch = watch.audioPresetPitch; }
                if (watch.physAttributePreset != null)
                {
                    PhysAttribute weight = ScriptableObject.CreateInstance<PhysAttribute>();
                    weight.name = prefabName + " Weight";
                    weight.mass = watch.physAttributePreset.mass * 6f;   // four times the size: heavier, still one-hand light
                    valuable.physAttributePreset = weight;
                }
            }
            else Plugin.Log.LogWarning($"{label}: the pocket watch wasn't found; it keeps {from}'s sound and weight.");

            // as fragile as the power crystal: its durability preset and its impact detector's fragility multipliers;
            // and nothing left over from the original that stops it breaking
            PhysGrabObjectImpactDetector detector = go.GetComponentInChildren<PhysGrabObjectImpactDetector>(true);
            GameObject crystalPrefab = Resources.Load<GameObject>(CrystalPath);
            ValuableObject crystal = crystalPrefab != null ? crystalPrefab.GetComponent<ValuableObject>() : null;
            PhysGrabObjectImpactDetector crystalDetector = crystalPrefab != null ? crystalPrefab.GetComponentInChildren<PhysGrabObjectImpactDetector>(true) : null;
            if (crystal != null && crystal.durabilityPreset != null) valuable.durabilityPreset = crystal.durabilityPreset;
            else Plugin.Log.LogWarning($"{label}: the power crystal wasn't found; it keeps {from}'s toughness.");
            if (detector != null)
            {
                if (crystalDetector != null)
                {
                    detector.fragilityMultiplier = crystalDetector.fragilityMultiplier;
                    detector.impactFragilityMultiplier = crystalDetector.impactFragilityMultiplier;
                }
                detector.destroyDisable = false;
                detector.slidingDisable = false;
            }
            Plugin.Log.LogInfo($"{label}: toughness from the power crystal: fragility {(valuable.durabilityPreset != null ? valuable.durabilityPreset.fragility : -1):0.#}, " +
                               $"durability {(valuable.durabilityPreset != null ? valuable.durabilityPreset.durability : -1):0.#}, multipliers " +
                               $"{(detector != null ? detector.fragilityMultiplier : -1):0.##} / {(detector != null ? detector.impactFragilityMultiplier : -1):0.##}.");
            var debris = new Gradient();
            debris.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.65f, 0.3f), 0f), new GradientColorKey(new Color(0.95f, 0.92f, 0.85f), 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            valuable.particleColors = debris;

            Compass c = go.AddComponent<Compass>();
            c.kind = kind;
            c.needle = pivot;
            c.emblem = emblem;
            c.modelCentre = modelRoot.localPosition + Vector3.up * (FrameHeight + EmblemHeight) * 0.5f * scale;
            Plugin.Log.LogInfo($"{label}: built from {from}, {(FrameHeight + EmblemHeight) * scale * 100f:0} cm tall, volume {valuable.volumeType}, ${value.valueMin:0}–{value.valueMax:0}, material '{(template != null ? template.name : "none")}'.");
            return go;
        }
    }
}
