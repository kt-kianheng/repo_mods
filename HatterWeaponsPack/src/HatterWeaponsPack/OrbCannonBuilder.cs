using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Builds the Orb Cannon prefab at startup from the game's shotgun (see WeaponBuilder) with the Blender model
    /// (tools/build_orb_cannon.py, orb_cannon.mesh: OC_Tube, OC_Top, OC_Bottom). The tube is modelled at a nominal
    /// outer radius of 0.12 m and scaled sideways here so its radius is the game's small soul orb's radius + 2 cm;
    /// the optic moves up and the grips move down by the same amount. No battery use: the cannon fires what it loads.
    /// </summary>
    internal static class OrbCannonBuilder
    {
        internal const string PrefabName = "Item Gun Orb Cannon";
        internal const string DisplayName = "Orb Cannon";
        private const string ModelFile = "orb_cannon.mesh";
        private const string SmallOrbPath = "Valuables/Enemy Valuable - Small";
        private static readonly string[] OrbPaths =
            { "Valuables/Enemy Valuable - Small", "Valuables/Enemy Valuable - Medium", "Valuables/Enemy Valuable - Big" };

        /// <summary>The game's soul orbs' looks (small, medium, big): copies of their meshes, centred and 1 m across
        /// at scale 1, under an inactive holder; null if one couldn't be copied. Their real radii alongside.</summary>
        internal static readonly GameObject[] OrbModels = new GameObject[3];
        internal static readonly float[] OrbRadii = { 0f, 0f, 0f };
        private static GameObject holder;


        // the model's numbers (Unity space; build_orb_cannon.py: R, RS, the muzzle at Blender y -0.70, the rear at +0.56)
        private const float NominalRadius = 0.12f, SleeveRadius = 0.132f;
        private const float MuzzleZ = 0.70f, RearZ = -0.56f;
        private const float WallThickness = 0.012f;

        internal const string FriendPrefabName = "Item Gun Friendship Cannon";
        internal const string FriendDisplayName = "Friendship Cannon";

        internal static GameObject Build() => BuildCannon(false);

        /// <summary>The Friendship Cannon: the same cannon, light blue, loaded with tumbling players (FriendshipCannon).</summary>
        internal static GameObject BuildFriendship() => BuildCannon(true);

        private static GameObject BuildCannon(bool friend)
        {
            MeshLibrary.Model tube = MeshLibrary.Get(ModelFile, "OC_Tube");
            MeshLibrary.Model top = MeshLibrary.Get(ModelFile, "OC_Top");
            MeshLibrary.Model bottom = MeshLibrary.Get(ModelFile, "OC_Bottom");
            if (tube == null || top == null || bottom == null) { Plugin.Log.LogError("Orb Cannon model missing from the DLL."); return null; }

            // the tube: the small soul orb's radius + 2 cm
            float orbRadius = SmallOrbRadius();
            float radius = orbRadius + 0.02f;
            float f = radius / NominalRadius;          // sideways scale of the tube
            float lift = (f - 1f) * SleeveRadius;      // how far the optic moves up / the grips move down
            float len = Settings.OrbLengthScale.Value;  // shorter than modelled, so it doesn't fill the view
            Plugin.Log.LogInfo($"Orb Cannon: the small soul orb's radius is {orbRadius * 100f:0.0} cm, so the tube's is {radius * 100f:0.0} cm (x{f:0.00}).");

            GameObject go = WeaponBuilder.CloneShotgun(friend ? FriendPrefabName : PrefabName);
            if (go == null) return null;
            Transform root = go.transform;

            float plasma = PlasmaBolt.Speed;
            if (friend) WeaponBuilder.SetupItem(go, FriendPrefabName, FriendDisplayName,
                "Load a tumbling teammate: pull the trigger while aiming at them within " + $"{Settings.FriendLoadRange.Value:0.#} m. " +
                "They ride inside, looking down the barrel, and go where it points. " +
                $"{Settings.OrbCooldown.Value:0.#} s cooldown after each shot.",
                Settings.FriendPriceMultiplier.Value);
            else WeaponBuilder.SetupItem(go, PrefabName, DisplayName,
                "Feed it soul orbs: pull the trigger while aiming at one within " + $"{Settings.OrbLoadRange.Value:0.#} m to load it (the orb and its money are gone). " +
                $"Small orb: a {Settings.OrbConeRange.Value:0} m shockwave, {Settings.OrbConeDamage.Value} damage. " +
                $"Medium: {Settings.OrbBurstShots.Value} orbs of {Settings.OrbBurstDamage.Value}. " +
                $"Large: a slow orb, {Settings.OrbBigDamage.Value} plus a rocket's blast. Everything hurts teammates too. " +
                $"{Settings.OrbCooldown.Value:0.#} s cooldown after each shot.",
                Settings.OrbPriceMultiplier.Value);

            ItemGun gun = go.GetComponent<ItemGun>();
            // sounds: the gun's own shot is silenced; the cannon plays the shotgun's shot for the orbs it fires and the
            // medium boom (orb_shockwave.pcm: "medium boom", soundreality, Pixabay; 3 s, pitched down) for the shockwave
            Sound shot = null, boom = null, load = null;
            if (gun.soundShoot != null)
            {
                // each fired orb: "futuristic smg sound effect" (freesound_community, Pixabay), first 0.9 s
                shot = MakeSound(gun.soundShoot, SoundLibrary.Get("orb_fire.pcm"), 0.9f);
                if (shot == null) { shot = new Sound(); Sound.CopySound(gun.soundShoot, shot); }
                boom = MakeSound(gun.soundShoot, SoundLibrary.Get("orb_shockwave.pcm"), 1f);
                // loading an orb: "high battery charge" (snorcon, Pixabay; supplied by the user), 1.5 s
                load = MakeSound(gun.soundShoot, SoundLibrary.Get("orb_load.pcm"), 1f);
                gun.soundShoot.Volume = 0f;
            }
            if (gun.soundShootGlobal != null) gun.soundShootGlobal.Volume = 0f;
            BuildOrbModels();
            gun.numberOfBullets = 0;              // OrbCannon fires instead of pellets
            gun.gunRandomSpread = 0f;
            gun.shootTime = 0.2f;
            gun.shootCooldown = 0.3f;             // the 3 s cooldown is the cannon's own
            gun.batteryDrain = 0f;                // unlimited: no battery use
            gun.batteryDrainFullBar = false;
            gun.batteryDrainFullBars = 0;
            gun.misfirePercentageChange = 0f;
            gun.gunRecoilForce = 20f;
            gun.cameraShakeMultiplier = 2.5f;
            if (gun.gunMuzzle != null) gun.gunMuzzle.localPosition = new Vector3(0f, 0f, MuzzleZ * len);

            // the model
            Material template = WeaponBuilder.HideShotgunMesh(root);
            var visuals = new GameObject("Orb Cannon Visuals");
            visuals.transform.SetParent(root, false);
            visuals.layer = go.layer;
            visuals.transform.localScale = new Vector3(1f, 1f, len);
            GameObject tubeGo = MeshLibrary.Attach(tube, visuals.transform, "Tube", template);
            tubeGo.transform.localScale = new Vector3(f, f, 1f);
            GameObject topGo = MeshLibrary.Attach(top, visuals.transform, "Optic", template, new Vector3(0f, lift, 0f));
            GameObject bottomGo = MeshLibrary.Attach(bottom, visuals.transform, "Grips", template, new Vector3(0f, -lift, 0f));
            int traceSlot = tube.Parts.FindIndex(p => p.MaterialName == "OC Trace");
            if (traceSlot < 0) Plugin.Log.LogWarning("Orb Cannon: the model has no 'OC Trace' material; the lines won't light up.");

            // grabbing: the tube, the pistol grip, the front grip, the optic (all along z shortened with the model)
            float rs = SleeveRadius + lift;
            WeaponBuilder.FitColliders(root, new (Vector3, Vector3)[]
            {
                (new Vector3(0f, 0f, (MuzzleZ + RearZ) / 2f * len), new Vector3(2f * (radius + 0.03f), 2f * (radius + 0.03f), (MuzzleZ - RearZ) * len)),
                (new Vector3(0f, -(rs + 0.06f), -0.045f * len), new Vector3(0.04f, 0.12f, 0.05f)),
                (new Vector3(0f, -(NominalRadius + lift + 0.05f), 0.34f * len), new Vector3(0.036f, 0.09f, 0.04f)),
                (new Vector3(0f, rs + 0.045f, 0.035f * len), new Vector3(0.05f, 0.06f, 0.15f * len)),
            });
            // held from a point above the tube (not the pistol grip), so the cannon hangs below the crosshair and the
            // view isn't looking down the tube
            float holdY = rs + 0.06f + Settings.OrbHoldAbove.Value;
            WeaponBuilder.SetForceGrabPoint(root, new Vector3(0f, holdY, 0f));
            Plugin.Log.LogInfo($"Orb Cannon: {(MuzzleZ - RearZ) * len:0.00} m long, held {holdY:0.00} m above the tube's axis.");

            var cannon = go.AddComponent<OrbCannon>();
            cannon.friendship = friend;
            if (friend) go.AddComponent<FriendshipCannon>();
            cannon.tubeRenderer = tubeGo.GetComponent<MeshRenderer>();
            cannon.traceSlot = traceSlot;
            cannon.muzzleLocal = new Vector3(0f, 0f, MuzzleZ * len);
            cannon.rearLocal = new Vector3(0f, 0f, RearZ * len);
            cannon.boreRadius = radius - WallThickness * f;
            cannon.shotSound = shot;
            cannon.boomSound = boom;
            cannon.loadSound = load;
            Plugin.Log.LogInfo($"Orb Cannon: built, price x{Settings.OrbPriceMultiplier.Value} the shotgun, plasma bolt speed {plasma} m/s.");
            return go;
        }

        private static Sound MakeSound(Sound template, AudioClip clip, float volume)
        {
            if (clip == null) { Plugin.Log.LogWarning("Orb Cannon: a sound file is missing from the DLL; using the shotgun's."); return null; }
            var s = new Sound();
            if (template != null) Sound.CopySound(template, s);
            s.Sounds = new[] { clip };
            s.Volume = volume;
            s.VolumeRandom = 0f;
            s.Pitch = 1f;
            s.PitchRandom = 0f;
            return s;
        }

        /// <summary>
        /// Copies the three soul orbs' meshes (every enabled MeshRenderer, placed as in the prefab, none of its scripts)
        /// into clean objects, centred and scaled to 1 m across, for the fired orbs and the one sitting in the muzzle.
        /// </summary>
        private static void BuildOrbModels()
        {
            if (holder == null)
            {
                holder = new GameObject("HatterWeaponsPack.OrbModels");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            for (int i = 0; i < OrbPaths.Length; i++)
            {
                if (OrbModels[i] != null) continue;
                try
                {
                    GameObject prefab = Resources.Load<GameObject>(OrbPaths[i]) ?? Resources.Load<GameObject>(OrbPaths[i].ToLowerInvariant());
                    if (prefab == null) { Plugin.Log.LogWarning($"Orb Cannon: '{OrbPaths[i]}' not found; its fired orbs use a plain glow."); continue; }
                    GameObject clone = Object.Instantiate(prefab, holder.transform);   // inactive: none of its scripts run
                    clone.transform.localPosition = Vector3.zero;
                    clone.transform.localRotation = Quaternion.identity;
                    clone.transform.localScale = Vector3.one;
                    Matrix4x4 toRoot = clone.transform.worldToLocalMatrix;
                    var model = new GameObject("HWP Orb Model " + i);
                    model.transform.SetParent(holder.transform, false);
                    var inner = new GameObject("Pieces");
                    inner.transform.SetParent(model.transform, false);
                    bool any = false;
                    Bounds b = default;
                    var names = new System.Text.StringBuilder();
                    foreach (MeshRenderer r in clone.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null || !r.enabled || !ActiveUnder(r.transform, clone.transform)) continue;
                        Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
                        var piece = new GameObject(r.name);
                        piece.transform.SetParent(inner.transform, false);
                        piece.transform.localPosition = m.GetColumn(3);
                        piece.transform.localRotation = m.rotation;
                        piece.transform.localScale = m.lossyScale;
                        piece.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                        var mr = piece.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = r.sharedMaterials;
                        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        Bounds mb = mf.sharedMesh.bounds;
                        for (int k = 0; k < 8; k++)
                        {
                            Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                            if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                        }
                        names.Append(names.Length > 0 ? ", " : "").Append(r.name);
                    }
                    Object.Destroy(clone);
                    if (!any) { Object.Destroy(model); Plugin.Log.LogWarning($"Orb Cannon: '{OrbPaths[i]}' has no meshes to copy."); continue; }
                    float diameter = Mathf.Max(b.size.x, b.size.y, b.size.z);
                    inner.transform.localScale = Vector3.one / Mathf.Max(1e-4f, diameter);
                    inner.transform.localPosition = -b.center / Mathf.Max(1e-4f, diameter);
                    OrbModels[i] = model;
                    OrbRadii[i] = diameter / 2f;
                    Plugin.Log.LogInfo($"Orb Cannon: orb model {i} from '{OrbPaths[i]}': {diameter * 100f:0} cm across, pieces: {names}.");
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Orb Cannon: copying the orb model '{OrbPaths[i]}' failed: {e}"); }
            }
        }

        private static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        /// <summary>The small soul orb's radius, from its colliders (or its meshes); 0.1 m if it can't be measured.</summary>
        private static float SmallOrbRadius()
        {
            GameObject orb = Resources.Load<GameObject>(SmallOrbPath) ?? Resources.Load<GameObject>(SmallOrbPath.ToLowerInvariant());
            if (orb == null) { Plugin.Log.LogWarning($"Orb Cannon: '{SmallOrbPath}' not found; assuming a 10 cm orb radius."); return 0.1f; }
            Bounds b = EvolutionCrystal.ColliderBounds(orb.transform, out int count);
            float r = count > 0 ? Mathf.Max(b.size.x, b.size.y, b.size.z) / 2f : 0f;
            if (r < 0.02f)
            {
                foreach (MeshFilter mf in orb.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null)
                        r = Mathf.Max(r, Vector3.Scale(mf.sharedMesh.bounds.extents, mf.transform.lossyScale).magnitude / Mathf.Sqrt(3f));
            }
            if (r < 0.02f || r > 0.5f) { Plugin.Log.LogWarning($"Orb Cannon: the small orb measured {r:0.000} m; assuming 10 cm."); return 0.1f; }
            return r;
        }
    }
}
