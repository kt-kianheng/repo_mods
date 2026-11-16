using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// Builds the two pocket compasses at startup from the game's small health pack (its grabbing, the [E] use button,
    /// shop slot and networking). The health pack's own logic, model and colliders are replaced with a third-size copy of
    /// HatterValuablesPack's compass (tools/build_compass.py): a glass sphere in an hourglass-shaped brass frame, with a
    /// needle that's stuck, and the emblem on top (a red eye / the game's Robe, or a green dollar sign / the level's
    /// extraction screen).
    /// </summary>
    internal static class CompassItemBuilder
    {
        internal const string EnemyName = "Pocket Enemy Compass", ExtractionName = "Pocket Extraction Compass";
        private const string BasePath = "Items/Item Health Pack Small";
        // the sound of the compass breaking: the power crystal's
        private const string CrystalPath = "Valuables/03 Medium/Valuable Wizard Power Crystal";

        // model points (tools/build_compass.py), Unity axes: up +Y
        internal static readonly Vector3 SphereCentre = new Vector3(0f, 0.046f, 0f);
        internal static readonly Vector3 Top = new Vector3(0f, 0.090f, 0f);
        private const float FrameWidth = 0.083f;
        internal const float FrameHeight = 0.090f;
        internal static float EmblemHeight => FrameHeight * Settings.CompassEmblemVsCompass.Value;
        private const float BuiltInEmblemHeight = 0.03f;   // the eye and the dollar sign as modelled
        internal const string SimpleEmblemName = "Emblem (built in)";

        private static GameObject holder;

        private static bool HasClips(Sound s) => s != null && s.Sounds != null && s.Sounds.Length > 0;

        internal static GameObject BuildEnemy() => Build(EnemyName, "Item Hatter Pocket Enemy Compass", CompassTargets.Kind.Enemy);
        internal static GameObject BuildExtraction() => Build(ExtractionName, "Item Hatter Pocket Extraction Compass", CompassTargets.Kind.Extraction);

        private static GameObject Build(string label, string prefabName, CompassTargets.Kind kind)
        {
            GameObject source = Resources.Load<GameObject>(BasePath);
            if (source == null || source.GetComponent<ItemAttributes>() == null) { Plugin.Log.LogError($"{label}: '{BasePath}' not found."); return null; }
            MeshLibrary.Model frame = MeshLibrary.Get("compass.mesh", "Compass_Frame");
            MeshLibrary.Model glass = MeshLibrary.Get("compass.mesh", "Compass_Glass");
            MeshLibrary.Model needle = MeshLibrary.Get("compass.mesh", "Compass_Needle");
            MeshLibrary.Model emblemModel = MeshLibrary.Get("compass.mesh", kind == CompassTargets.Kind.Enemy ? "Compass_EmblemEnemy" : "Compass_EmblemExtraction");
            if (frame == null || needle == null) return null;

            if (holder == null)
            {
                holder = new GameObject("HatterMiscellaneousPack.PrefabHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject go = Object.Instantiate(source, holder.transform);
            go.name = prefabName;
            Transform root = go.transform;
            const string from = "the small health pack";
            BuildHelpers.LogComponents(go, label, from);
            Bounds orig = BuildHelpers.LocalMeshBounds(root);

            // the health pack's logic goes (its reject sound is kept); its glow, particles and model are switched off
            Sound reject = null;
            ItemHealthPack pack = go.GetComponent<ItemHealthPack>();
            if (pack != null) { reject = pack.soundReject; Object.DestroyImmediate(pack); }
            foreach (PropLight pl in go.GetComponentsInChildren<PropLight>(true)) pl.enabled = false;
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            Material template = null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (template == null && r is MeshRenderer && r.sharedMaterial != null) template = r.sharedMaterial;
                r.enabled = false;
            }
            template = BuildHelpers.NeutralTemplate(template);   // the health pack's own material tinted the brass red
            NotValuableObject nvo = go.GetComponent<NotValuableObject>();
            if (nvo != null) nvo.hasHealth = false;   // bumps never break it; only using it does

            // shop entry
            ItemAttributes attributes = go.GetComponent<ItemAttributes>();
            Item item = Object.Instantiate(attributes.item);
            item.name = prefabName;
            item.itemName = label;
            item.itemNameLocalized = null;
            float minutes = Settings.CompassEffectSeconds.Value / 60f;
            string target = kind == CompassTargets.Kind.Enemy ? "the nearest enemy" : "the nearest unfinished extraction point";
            item.description = $"A little brass compass with a stuck needle. Press [E] to break it: an arrow under your haul then points to {target} " +
                               $"for {minutes:0.#} minutes, or until there's nothing left to find. Only you see it.";
            // a fixed price: the shop shows ceil(value x 4 / 1000) K, and only health packs, upgrades and crystals rise with
            // the level, so it's sold as a tracker (normal item slots, like the game's valuable / extraction trackers)
            item.itemType = SemiFunc.itemType.tracker;
            int priceK = kind == CompassTargets.Kind.Enemy ? Settings.EnemyCompassPriceK.Value : Settings.ExtractionCompassPriceK.Value;
            item.value = ShopPrice.Fixed(prefabName, priceK);
            attributes.item = item;
            attributes.icon = null;   // rendered by the game from the new model

            // the model, centred where the health pack was
            float scale = Settings.CompassScale.Value;
            float height = (FrameHeight + EmblemHeight) * scale;
            var modelRoot = new GameObject("Compass Model").transform;
            modelRoot.SetParent(root, false);
            modelRoot.gameObject.layer = go.layer;
            modelRoot.localPosition = orig.size.sqrMagnitude > 1e-6f ? orig.center - Vector3.up * height / 2f : Vector3.zero;
            modelRoot.localScale = Vector3.one * scale;
            MeshLibrary.Attach(frame, modelRoot, "Compass Frame", template).layer = go.layer;
            if (glass != null) MeshLibrary.Attach(glass, modelRoot, "Compass Glass", template).layer = go.layer;

            // the needle is stuck: a fixed, slightly crooked pose (it doesn't point anywhere)
            var pivot = new GameObject("Needle Pivot").transform;
            pivot.SetParent(modelRoot, false);
            pivot.localPosition = SphereCentre;
            pivot.localRotation = Quaternion.Euler(14f, 37f, 6f);
            pivot.gameObject.layer = go.layer;
            MeshLibrary.Attach(needle, pivot, "Needle", template).layer = go.layer;

            var emblem = new GameObject("Emblem").transform;
            emblem.SetParent(modelRoot, false);
            emblem.localPosition = Top;
            emblem.gameObject.layer = go.layer;
            if (emblemModel != null)
            {
                GameObject built = MeshLibrary.Attach(emblemModel, emblem, SimpleEmblemName, template);
                built.layer = go.layer;
                built.transform.localScale = Vector3.one * (EmblemHeight / BuiltInEmblemHeight);
            }

            Vector3 at = modelRoot.localPosition;
            BuildHelpers.ReplaceColliders(root, new (Vector3, Quaternion, Vector3)[]
            {
                (at + new Vector3(0f, FrameHeight / 2, 0f) * scale, Quaternion.identity, new Vector3(FrameWidth, FrameHeight, FrameWidth) * scale),
                (at + (Top + Vector3.up * EmblemHeight / 2) * scale, Quaternion.identity, new Vector3(EmblemHeight * 0.6f, EmblemHeight, EmblemHeight * 0.6f) * scale),
            }, label, from);

            // the breaking sound: the power crystal's
            Sound breakSound = null;
            ValuableObject crystal = Resources.Load<GameObject>(CrystalPath)?.GetComponent<ValuableObject>();
            if (crystal != null && crystal.audioPreset != null)
                breakSound = HasClips(crystal.audioPreset.destroy) ? crystal.audioPreset.destroy
                           : HasClips(crystal.audioPreset.breakHeavy) ? crystal.audioPreset.breakHeavy : null;
            if (breakSound == null) Plugin.Log.LogWarning($"{label}: the power crystal's break sound wasn't found; it breaks silently.");

            CompassItem c = go.AddComponent<CompassItem>();
            c.kindValue = (int)kind;
            c.emblem = emblem;
            c.centre = at + SphereCentre * scale;
            c.size = height;
            c.rejectSound = HasClips(reject) ? reject : null;
            c.breakSound = breakSound;
            Plugin.Log.LogInfo($"{label}: built from {from} (its model measured {orig.size.x:0.00} x {orig.size.y:0.00} x {orig.size.z:0.00} m), " +
                               $"{height * 100f:0} cm tall, ${priceK}K, material '{(template != null ? template.name : "none")}'.");
            return go;
        }
    }
}
