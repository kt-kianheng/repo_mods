using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Evolution Crystal: a white crystal (the staves' crystal shape) that turns a wand into its staff.
    ///  - Built at startup from the game's explosive grenade (for grabbing, the use button and networking); the grenade
    ///    logic and model are removed.
    ///  - Using it (the item's use button, ItemToggle) on the user's game picks the Ember, Frost or Lightning Wand nearest
    ///    the crosshair within Range of the user and asks the host. No wand in reach: nothing happens.
    ///  - The host checks it, tells everyone (white beam from the crystal to the wand, the wand floats up inside a bright
    ///    white glow) and breaks the crystal. At the glow's brightest the host swaps the wand for its staff.
    ///  - In a level the wand leaves the owned items and the staff joins them (it comes back each level). In the shop the
    ///    wand for sale becomes a staff for sale at StaffPriceVsWand x the wand's price.
    /// </summary>
    public class EvolutionCrystal : MonoBehaviour
    {
        internal const string DisplayName = "Evolution Crystal";
        internal const string PrefabName = "Item Evolution Crystal";
        private const string GrenadePath = "Items/Item Grenade Explosive";

        /// <summary>Wand Item name → staff Item name (filled in as the staves are built).</summary>
        internal static readonly Dictionary<string, string> Evolutions = new Dictionary<string, string>();

        internal static readonly Color White = new Color(0.93f, 0.97f, 1f, 1f);

        // set by Build (serialized, so every spawned crystal gets them)
        public Vector3 crystalCentre;
        public Sound beamSound, breakSound, riseSound, flashSound;

        private ItemToggle toggle;
        private PhysGrabObject physGrabObject;
        private PhotonView photonView;
        private ItemAttributes attributes;
        private PhysGrabObjectImpactDetector impact;
        private bool lastToggle, used;
        private Light glowLight;
        private SoftGlow halo;

        private static readonly FieldInfo ToggleIdField = AccessTools.Field(typeof(ItemToggle), "playerTogglePhotonID");
        private static readonly FieldInfo EquippedField = AccessTools.Field(typeof(ItemEquippable), "isEquipped");
        internal static readonly FieldInfo InstanceNameField = AccessTools.Field(typeof(ItemAttributes), "instanceName");
        internal static readonly FieldInfo ValueField = AccessTools.Field(typeof(ItemAttributes), "value");

        // ---------------- building the prefab ----------------

        internal static GameObject Build()
        {
            MeshLibrary.Model model = MeshLibrary.Get("ember_staff.mesh", "EmberStaff_Tip");
            if (model == null) { Plugin.Log.LogError("Evolution Crystal: the staff crystal model is missing from the DLL."); return null; }
            GameObject go = WeaponBuilder.CloneItem(GrenadePath, PrefabName);
            if (go == null) return null;
            Transform root = go.transform;

            float wandVsGun = (Settings.EmberPriceMultiplier.Value + Settings.FrostPriceMultiplier.Value + Settings.LightningPriceMultiplier.Value) / 3f;
            WeaponBuilder.SetupItem(go, PrefabName, DisplayName,
                $"A white crystal humming with power. Use it near an Ember, Frost or Lightning Wand (within {Settings.CrystalRange.Value:0.#} m, " +
                "the one nearest your crosshair) and the wand evolves into its staff. The crystal breaks when used.",
                wandVsGun * Settings.CrystalPriceVsWand.Value, WeaponBuilder.HandgunPath);
            Item item = go.GetComponent<ItemAttributes>().item;
            item.maxAmountInShop = Settings.CrystalMaxInShop.Value;

            // sounds use the wands' settings (the shotgun's shot sound) as a template
            Sound template = Resources.Load<GameObject>("Items/Item Gun Shotgun")?.GetComponent<ItemGun>()?.soundShoot;

            // strip the grenade: its logic, its throw line and its model (the colliders, grabbing and toggle stay)
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || !mb.GetType().Name.StartsWith("ItemGrenade")) continue;
                if (mb is ItemGrenade g && g.throwLine != null) g.throwLine.SetActive(false);
                Object.DestroyImmediate(mb);
            }
            Material lit = null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (lit == null && r is MeshRenderer && r.sharedMaterial != null) lit = r.sharedMaterial;
                r.enabled = false;
            }

            // the crystal: centred on the grenade's colliders, sized to them
            Bounds grip = ColliderBounds(root, out int colliderCount);
            Bounds shape = model.Mesh.bounds;
            float longest = Mathf.Max(shape.size.x, shape.size.y, shape.size.z);
            float wanted = Mathf.Max(grip.size.x, grip.size.y, grip.size.z) * 1.15f;
            float scale = longest > 1e-4f && wanted > 1e-4f ? Mathf.Clamp(wanted / longest, 0.6f, 1.8f) : 1f;
            var visuals = new GameObject("Evolution Crystal Visuals");
            visuals.transform.SetParent(root, false);
            visuals.layer = go.layer;
            visuals.transform.localPosition = grip.center;
            visuals.transform.localScale = Vector3.one * scale;
            // the wands' material template (the shotgun's): the grenade's own material is its red emissive one, which tints anything made from it
            Material look = WandBuilder.MaterialTemplate != null ? WandBuilder.MaterialTemplate : lit;
            GameObject crystal = MeshLibrary.Attach(model, visuals.transform, "Crystal", look, -shape.center);
            Material white = MeshLibrary.MaterialFor(look, "EC Crystal", White, 0.9f, 0.1f);
            Plugin.Log.LogInfo($"Evolution Crystal: material from '{(look != null ? look.name : "none")}' ({(look != null && look.shader != null ? look.shader.name : "-")}).");
            var mr = crystal.GetComponent<MeshRenderer>();
            var mats = new Material[mr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = white;
            mr.sharedMaterials = mats;
            Plugin.Log.LogInfo($"Evolution Crystal: {colliderCount} grenade collider(s), size {grip.size}; crystal {shape.size} x {scale:0.00}.");

            var ec = go.AddComponent<EvolutionCrystal>();
            ec.crystalCentre = grip.center;
            ec.beamSound = MakeSound(template, SoundLibrary.Get(HealingWand.BeamChimeFile), 0.6f);
            ec.breakSound = MakeSound(template, SoundLibrary.Get("icicle_crash.pcm"), 0.6f);
            ec.riseSound = MakeSound(template, RiseClip(), 0.55f);
            ec.flashSound = MakeSound(template, SoundLibrary.Get(HealingWand.AreaChimeFile), 0.8f);
            return go;
        }

        /// <summary>The grenade's colliders' combined box in the item's own space (works on the inactive prefab).</summary>
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
                else continue;
                // the box's corners, into the item's space
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

        private static Sound MakeSound(Sound template, AudioClip clip, float volume)
        {
            if (clip == null) return null;
            var s = new Sound();
            if (template != null) Sound.CopySound(template, s);
            s.Sounds = new[] { clip };
            s.Volume = volume;
            s.VolumeRandom = 0f;
            s.Pitch = 1f;
            s.PitchRandom = 0f;
            return s;
        }

        /// <summary>A rising, shimmering chord (the wand floating up in the glow), about 1.5 s.</summary>
        private static AudioClip RiseClip()
        {
            const int rate = 44100;
            int n = (int)(rate * 1.5f);
            var data = new float[n];
            float[] baseFreq = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f };
            var phase = new double[baseFreq.Length];
            var rng = new System.Random(77);
            float sparkle = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float rise = Mathf.Lerp(1f, 1.5f, t * t);
                float s = 0f;
                for (int k = 0; k < baseFreq.Length; k++)
                {
                    phase[k] += 2.0 * System.Math.PI * baseFreq[k] * rise / rate;
                    float trem = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * (9f + k * 1.7f) * i / rate);
                    s += (float)System.Math.Sin(phase[k]) * trem / (1f + k * 0.5f);
                }
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                sparkle += 0.6f * (white - sparkle);
                float hiss = (white - sparkle) * 0.25f;   // high-passed noise: a glittery hiss
                float env = Mathf.Min(1f, t / 0.15f) * Mathf.Lerp(0.5f, 1f, t) * Mathf.Min(1f, (1f - t) / 0.08f);
                data[i] = Mathf.Clamp((s * 0.22f + hiss * t) * env, -1f, 1f);
            }
            var clip = AudioClip.Create("HWP Evolution Rise", n, 1, rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return clip;
        }

        // ---------------- every spawned crystal ----------------

        private void Awake()
        {
            toggle = GetComponent<ItemToggle>();
            physGrabObject = GetComponent<PhysGrabObject>();
            photonView = GetComponent<PhotonView>();
            attributes = GetComponent<ItemAttributes>();
            impact = GetComponent<PhysGrabObjectImpactDetector>();

            var lightObj = new GameObject("Crystal Light");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = crystalCentre;
            glowLight = lightObj.AddComponent<Light>();
            glowLight.type = LightType.Point; glowLight.color = White; glowLight.range = 0.6f; glowLight.intensity = 0.6f;
            halo = SoftGlow.Create(transform, crystalCentre, "Crystal Halo");
            halo.SetSize(0.22f);
        }

        private void Start()
        {
            if (toggle != null) lastToggle = toggle.toggleState;
        }

        private void Update()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
            if (halo != null) halo.SetColor(new Color(1f, 1f, 1f, 0.35f + 0.2f * pulse));
            if (glowLight != null) glowLight.intensity = 0.45f + 0.3f * pulse;

            if (used || toggle == null) return;
            bool state = toggle.toggleState;
            if (state == lastToggle) return;
            lastToggle = state;
            // the use button was pressed; only the game of the player who pressed it looks for a wand
            int who = ToggleIdField != null ? (int)ToggleIdField.GetValue(toggle) : -1;
            if (who != SemiFunc.PhotonViewIDPlayerAvatarLocal()) return;
            try { LocalUse(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Evolution Crystal use failed: {e}"); }
        }

        /// <summary>The user's game: find the wand nearest the crosshair and ask the host to evolve it.</summary>
        private void LocalUse()
        {
            ChargedWand target = FindTarget();
            if (target == null) { Plugin.Log.LogInfo("Evolution Crystal: no wand in reach, nothing happens."); return; }
            if (SemiFunc.IsMultiplayer())
            {
                PhotonView view = target.GetComponent<PhotonView>();
                if (view != null) photonView.RPC(nameof(HWPCrystalUseRPC), RpcTarget.MasterClient, view.ViewID);
            }
            else HostUse(target, PlayerAvatar.instance);
        }

        private ChargedWand FindTarget()
        {
            PlayerAvatar me = PlayerAvatar.instance;
            Transform view = null;
            try { if (me != null && me.localCamera != null) view = me.localCamera.GetOverrideTransform(); } catch { }
            if (view == null && Camera.main != null) view = Camera.main.transform;
            if (view == null) return null;
            float range = Settings.CrystalRange.Value;
            ChargedWand best = null;
            float bestAngle = 90f;   // in front of you only
            foreach (ChargedWand w in Object.FindObjectsByType<ChargedWand>(FindObjectsSortMode.None))
            {
                if (!CanEvolve(w)) continue;
                Vector3 c = Centre(w.gameObject);
                if (Vector3.Distance(view.position, c) > range) continue;
                float angle = Vector3.Angle(view.forward, c - view.position);
                if (angle < bestAngle) { bestAngle = angle; best = w; }
            }
            return best;
        }

        /// <summary>An Ember, Frost or Lightning Wand lying in the world (not in an inventory), not already evolving.</summary>
        internal static bool CanEvolve(ChargedWand w)
        {
            if (w == null || !w.isActiveAndEnabled) return false;
            ItemAttributes a = w.GetComponent<ItemAttributes>();
            if (a == null || a.item == null || !Evolutions.ContainsKey(a.item.name)) return false;
            ItemEquippable eq = w.GetComponent<ItemEquippable>();
            if (eq != null && EquippedField != null && (bool)EquippedField.GetValue(eq)) return false;
            PhysGrabObject pg = w.GetComponent<PhysGrabObject>();
            if (pg != null && pg.dead) return false;
            return !EvolutionFx.Busy(w.gameObject);
        }

        internal static Vector3 Centre(GameObject item)
        {
            Rigidbody rb = item.GetComponent<Rigidbody>();
            return rb != null ? rb.worldCenterOfMass : item.transform.position;
        }

        [PunRPC]
        public void HWPCrystalUseRPC(int wandViewId, PhotonMessageInfo info)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            PhotonView view = PhotonView.Find(wandViewId);
            PlayerAvatar user = null;
            if (GameDirector.instance != null)
                foreach (PlayerAvatar p in GameDirector.instance.PlayerList)
                    if (p != null && p.photonView != null && p.photonView.Owner == info.Sender) { user = p; break; }
            HostUse(view != null ? view.GetComponent<ChargedWand>() : null, user);
        }

        /// <summary>Host: check the request, start the evolution for everyone, and break the crystal.</summary>
        private void HostUse(ChargedWand wand, PlayerAvatar user)
        {
            if (used || !CanEvolve(wand)) return;
            if (user != null && Vector3.Distance(user.transform.position, Centre(wand.gameObject)) > Settings.CrystalRange.Value + 2f) return;
            Vector3 origin = transform.TransformPoint(crystalCentre);
            if (SemiFunc.IsMultiplayer())
            {
                PhotonView view = wand.GetComponent<PhotonView>();
                photonView.RPC(nameof(HWPCrystalEvolveRPC), RpcTarget.All, view != null ? view.ViewID : 0, origin);
            }
            else Evolve(wand.gameObject, origin);
            Break();
        }

        [PunRPC]
        public void HWPCrystalEvolveRPC(int wandViewId, Vector3 origin, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            PhotonView view = PhotonView.Find(wandViewId);
            Evolve(view != null ? view.gameObject : null, origin);
        }

        /// <summary>Every client: the beam, the glow and the sounds (the host also lifts and swaps the wand).</summary>
        private void Evolve(GameObject wand, Vector3 origin)
        {
            used = true;
            if (halo != null) halo.gameObject.SetActive(false);
            if (glowLight != null) glowLight.enabled = false;
            Play(beamSound, origin);
            Play(breakSound, origin);
            if (wand == null) return;
            // let go of the wand if we're holding it: it floats up on its own now
            PlayerAvatar me = PlayerAvatar.instance;
            PhysGrabObject pg = wand.GetComponent<PhysGrabObject>();
            PhotonView view = wand.GetComponent<PhotonView>();
            try
            {
                if (me != null && me.physGrabber != null && pg != null && pg.playerGrabbing != null && pg.playerGrabbing.Contains(me.physGrabber))
                    me.physGrabber.ReleaseObject(view != null ? view.ViewID : 0, 1f);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Evolution Crystal: letting go of the wand failed: {e}"); }
            EvolutionFx.Begin(origin, wand, riseSound, flashSound);
        }

        /// <summary>Host: the crystal is spent. In a level it leaves the owned items; in the shop, the shopping list.</summary>
        private void Break()
        {
            try
            {
                if (SemiFunc.RunIsShop()) { if (ShopManager.instance != null) ShopManager.instance.ShoppingListItemRemove(attributes); }
                else
                {
                    string instance = InstanceNameField != null ? InstanceNameField.GetValue(attributes) as string : null;
                    if (!string.IsNullOrEmpty(instance) && StatsManager.instance != null) StatsManager.instance.ItemRemove(instance);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Evolution Crystal: removing it from the owned items failed: {e}"); }
            if (impact != null) impact.DestroyObject(false, true);   // our own shatter instead of the grenade's
            // DestroyObject only marks an item dead (nothing then removes it), so remove it as well
            if (physGrabObject != null) physGrabObject.DestroyPhysGrabObject();
        }

        internal static void Play(Sound s, Vector3 position)
        {
            if (s == null) return;
            try { s.Play(position, 1f, 1f, 1f, 1f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Evolution Crystal sound failed: {e}"); }
        }
    }

    /// <summary>
    /// The evolution on every client: a white beam from the crystal seeks the wand, which floats up inside a growing
    /// white orb; at its brightest the host swaps the wand for its staff, then the orb bursts and fades to show it.
    /// </summary>
    internal class EvolutionFx : MonoBehaviour
    {
        private const float BeamSeconds = 0.45f;     // the beam flying to the wand
        private const float SwapAt = 1.9f;           // from the start: the orb is at its biggest, the host swaps
        private const float HoldSeconds = 0.55f;     // the orb stays big (covers the network delay of the swap)
        private const float FadeSeconds = 0.9f;
        private const float WandOrb = 0.75f;         // orb diameter round the wand
        private const float StaffOrb = 1.8f;         // ... and at the swap, big enough for the staff
        private const float StaffMiddle = 0.24f;     // the staff's middle, from its grab point along its length
        private const float HangSeconds = 1.3f;      // the new staff hangs in the air before it drops

        private static readonly HashSet<GameObject> busy = new HashSet<GameObject>();
        internal static bool Busy(GameObject wand) => wand != null && busy.Contains(wand);

        private GameObject wand;
        private string wandItemName;
        private Vector3 origin, control, centre, liftTarget;
        private float t;
        private bool host, lifting, swapped, burst, riseStarted, flashed;
        private Sound rise, flash;

        private LineRenderer beamCore, beamGlow;
        private SoftGlow beamHead, halo;
        private MeshRenderer orb;
        private Material orbMaterial;
        private Light orbLight;
        private ParticleSystem inward, outward, shards;
        private readonly List<Vector3> points = new List<Vector3>();

        internal static void Begin(Vector3 origin, GameObject wand, Sound rise, Sound flash)
        {
            var go = new GameObject("HWP Evolution");
            go.transform.position = origin;
            var fx = go.AddComponent<EvolutionFx>();
            fx.origin = origin;
            fx.wand = wand;
            fx.rise = rise;
            fx.flash = flash;
            fx.host = SemiFunc.IsMasterClientOrSingleplayer();
            ItemAttributes a = wand.GetComponent<ItemAttributes>();
            fx.wandItemName = a != null && a.item != null ? a.item.name : null;
            busy.Add(wand);
            fx.centre = EvolutionCrystal.Centre(wand);
            Vector3 mid = (origin + fx.centre) * 0.5f;
            Vector3 side = Vector3.Cross(fx.centre - origin, Vector3.up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            fx.control = mid + Vector3.up * 0.6f + side * Random.Range(-0.4f, 0.4f);
            fx.Build();
        }

        private void Build()
        {
            beamGlow = LightningLook.Line(transform, "Beam Glow", 0.09f, new Color(1f, 1f, 1f, 0.35f));
            beamCore = LightningLook.Line(transform, "Beam Core", 0.03f, Color.white);
            beamHead = SoftGlow.Create(transform, Vector3.zero, "Beam Head");
            beamHead.SetSize(0.35f);
            beamHead.SetColor(new Color(1f, 1f, 1f, 0.9f));

            // the orb: a bright white ball inside a wide soft glow
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.name = "Orb";
            ball.transform.SetParent(transform, false);
            orb = ball.GetComponent<MeshRenderer>();
            Shader s = Shader.Find("Sprites/Default");
            if (s != null) { orbMaterial = new Material(s) { name = "HWP Evolution Orb" }; orb.sharedMaterial = orbMaterial; }
            orb.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orb.receiveShadows = false;
            orb.enabled = false;
            halo = SoftGlow.Create(transform, Vector3.zero, "Orb Halo");

            orbLight = gameObject.AddComponent<Light>();
            orbLight.type = LightType.Point; orbLight.color = EvolutionCrystal.White; orbLight.intensity = 0f; orbLight.range = 4f;

            inward = Particles("Gather", 0.7f, 0.9f, -1.6f, -1.1f, 0.015f, 0.04f, 0f);
            var shape = inward.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 1.1f; shape.radiusThickness = 0f;
            var em = inward.emission; em.rateOverTime = 0f;
            inward.Play();
            outward = Particles("Burst", 0.6f, 1.2f, 2f, 5f, 0.02f, 0.06f, 0.2f);
            shape = outward.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f;
            shards = Particles("Crystal Shards", 0.4f, 0.8f, 1f, 3.5f, 0.012f, 0.035f, 1f);
            shape = shards.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.04f;
            shards.transform.position = origin;
            shards.Emit(40);

            Update();
        }

        private ParticleSystem Particles(string name, float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(0.8f, 0.9f, 1f, 1f));
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission; em.rateOverTime = 0f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            if (SoftGlow.SharedMaterial != null) pr.sharedMaterial = SoftGlow.SharedMaterial;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        private static Vector3 Bezier(Vector3 a, Vector3 c, Vector3 b, float u) => Vector3.Lerp(Vector3.Lerp(a, c, u), Vector3.Lerp(c, b, u), u);

        private void Update()
        {
            t += Time.deltaTime;
            if (wand != null) centre = EvolutionCrystal.Centre(wand);
            else if (!swapped) swapped = true;   // the host swapped it (or it's gone): the orb stays where it was

            // ---- the beam ----
            float beamAlpha = t < BeamSeconds ? 1f : Mathf.Clamp01(1f - (t - BeamSeconds) / 0.35f);
            bool beamOn = beamAlpha > 0f;
            beamCore.enabled = beamGlow.enabled = beamOn;
            beamHead.gameObject.SetActive(t < BeamSeconds + 0.1f);
            if (beamOn)
            {
                float p = Mathf.Clamp01(t / BeamSeconds);
                p = 1f - (1f - p) * (1f - p);
                int count = Mathf.Max(2, Mathf.CeilToInt(24 * p) + 1);
                points.Clear();
                for (int i = 0; i < count; i++)
                {
                    float u = p * i / (count - 1);
                    Vector3 q = Bezier(origin, control, centre, u);
                    if (i > 0 && i < count - 1) q += Random.insideUnitSphere * 0.01f;   // a slight shimmer
                    points.Add(q);
                }
                LightningLook.Apply(beamCore, points);
                LightningLook.Apply(beamGlow, points);
                beamCore.startColor = beamCore.endColor = new Color(1f, 1f, 1f, beamAlpha);
                beamGlow.startColor = beamGlow.endColor = new Color(1f, 1f, 1f, 0.35f * beamAlpha);
                beamHead.transform.position = points[points.Count - 1];
            }

            // ---- the orb ----
            float since = t - BeamSeconds;
            if (since >= 0f)
            {
                if (!riseStarted) { riseStarted = true; EvolutionCrystal.Play(rise, centre); var em = inward.emission; em.rateOverTime = 70f; }
                float d, alpha = 0.92f, lightK;
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(since / 0.5f));
                float flashUp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - (SwapAt - 0.35f)) / 0.35f));
                float fade = Mathf.Clamp01((t - SwapAt - HoldSeconds) / FadeSeconds);
                d = Mathf.Lerp(WandOrb * grow, StaffOrb, flashUp) * (1f + 0.04f * Mathf.Sin(t * 14f));
                lightK = Mathf.Lerp(3f * grow, 10f, flashUp);
                if (fade > 0f)
                {
                    if (!burst)
                    {
                        burst = true;
                        var em = inward.emission; em.rateOverTime = 0f;
                        outward.transform.position = centre;
                        outward.Emit(90);
                    }
                    d = Mathf.Lerp(StaffOrb, 0.4f, fade);
                    alpha *= 1f - fade;
                    lightK *= 1f - fade;
                }
                if (t >= SwapAt && !flashed) { flashed = true; EvolutionCrystal.Play(flash, centre); }

                Vector3 at = centre;
                transform.position = at;
                inward.transform.position = at;
                orb.enabled = d > 0.01f && alpha > 0.01f;
                orb.transform.position = at;
                orb.transform.localScale = Vector3.one * d;
                if (orbMaterial != null) orbMaterial.color = new Color(1f, 1f, 1f, alpha);
                halo.transform.position = at;
                halo.SetSize(d * 2.6f);
                halo.SetColor(new Color(1f, 1f, 1f, 0.95f * (1f - fade)));
                orbLight.intensity = lightK;
                orbLight.range = Mathf.Max(3f, d * 4f);

                if (fade >= 1f && t > SwapAt + HoldSeconds + FadeSeconds + 1.2f) { Destroy(gameObject); return; }
            }

            // ---- host: the swap at the orb's brightest ----
            if (host && !swapped && wand != null && t >= SwapAt + 0.05f) Swap();
        }

        private void FixedUpdate()
        {
            if (!host || swapped || wand == null || t < BeamSeconds) return;
            Rigidbody rb = wand.GetComponent<Rigidbody>();
            PhysGrabObject pg = wand.GetComponent<PhysGrabObject>();
            if (rb == null || pg == null) return;
            if (!lifting)
            {
                lifting = true;
                Vector3 start = rb.worldCenterOfMass;
                float up = 1f;
                if (Physics.Raycast(start, Vector3.up, out RaycastHit hit, 2f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
                    up = Mathf.Clamp(hit.distance - 0.95f, 0.2f, 1f);   // keep the staff's head under the ceiling
                liftTarget = start + Vector3.up * up;
            }
            pg.OverrideZeroGravity(0.3f);
            pg.OverrideGrabDisable(0.5f);
            rb.linearVelocity = Vector3.ClampMagnitude((liftTarget - rb.worldCenterOfMass) * 3f, 2.5f);
            rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, Vector3.up * 2.5f, 0.1f);
        }

        /// <summary>Host: the wand goes, its staff appears in the same place, standing up in the light.</summary>
        private void Swap()
        {
            swapped = true;
            Vector3 at = EvolutionCrystal.Centre(wand);
            centre = at;
            Item staff = null;
            if (wandItemName != null && EvolutionCrystal.Evolutions.TryGetValue(wandItemName, out string staffName) && StatsManager.instance != null)
                StatsManager.instance.itemDictionary.TryGetValue(staffName, out staff);
            if (staff == null || staff.prefab == null) { Plugin.Log.LogError($"Evolution Crystal: no staff found for '{wandItemName}'."); return; }

            bool shop = SemiFunc.RunIsShop();
            ItemAttributes wandAttributes = wand.GetComponent<ItemAttributes>();
            int wandPrice = 0;
            try
            {
                if (shop)
                {
                    if (EvolutionCrystal.ValueField != null) wandPrice = (int)EvolutionCrystal.ValueField.GetValue(wandAttributes);
                    if (ShopManager.instance != null) ShopManager.instance.ShoppingListItemRemove(wandAttributes);
                }
                else
                {
                    // the owned items: one wand fewer, one staff more (it comes back each level)
                    string instance = EvolutionCrystal.InstanceNameField != null ? EvolutionCrystal.InstanceNameField.GetValue(wandAttributes) as string : null;
                    if (!string.IsNullOrEmpty(instance)) StatsManager.instance.ItemRemove(instance);
                    StatsManager.instance.ItemPurchase(staff.name);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Evolution Crystal: updating the items failed: {e}"); }

            Vector3 flat = Vector3.ProjectOnPlane(wand.transform.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(Vector3.up, flat.normalized);   // the staff's crystal end up
            Vector3 position = at - rotation * new Vector3(0f, 0f, StaffMiddle);

            PhysGrabObject wandGrab = wand.GetComponent<PhysGrabObject>();
            if (wandGrab != null) wandGrab.DestroyPhysGrabObject();
            else Destroy(wand);

            GameObject made = null;
            try
            {
                made = SemiFunc.IsMultiplayer()
                    ? PhotonNetwork.InstantiateRoomObject(staff.prefab.ResourcePath, position, rotation, 0, null)
                    : Object.Instantiate(staff.prefab.Prefab, position, rotation);
            }
            catch (System.Exception e) { Plugin.Log.LogError($"Evolution Crystal: spawning the {staff.itemName} failed: {e}"); }
            if (made == null) return;
            PhysGrabObject pg = made.GetComponent<PhysGrabObject>();
            if (pg != null) { pg.OverrideZeroGravity(HangSeconds); pg.OverrideGrabDisable(0.6f); }
            Rigidbody rb = made.GetComponent<Rigidbody>();
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            Plugin.Log.LogInfo($"Evolution Crystal: {wandItemName} became {staff.name}{(shop ? $" (shop, price {wandPrice} x {Settings.CrystalStaffPriceVsWand.Value})" : "")}.");
            if (shop && wandPrice > 0)
                StartCoroutine(SetPrice(made, Mathf.Max(1, Mathf.RoundToInt(wandPrice * Settings.CrystalStaffPriceVsWand.Value))));
        }

        /// <summary>Host, in the shop: once the staff has rolled its own price, replace it with StaffPriceVsWand x the wand's.</summary>
        private IEnumerator SetPrice(GameObject staff, int price)
        {
            yield return new WaitForSeconds(0.3f);
            if (staff == null) yield break;
            ItemAttributes a = staff.GetComponent<ItemAttributes>();
            if (a == null) yield break;
            try
            {
                if (EvolutionCrystal.ValueField != null) EvolutionCrystal.ValueField.SetValue(a, price);
                PhotonView view = staff.GetComponent<PhotonView>();
                if (SemiFunc.IsMultiplayer() && view != null) view.RPC("GetValueRPC", RpcTarget.All, price);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Evolution Crystal: setting the staff's price failed: {e}"); }
        }

        private void OnDestroy()
        {
            busy.RemoveWhere(w => w == null || w == wand);
            if (orbMaterial != null) Destroy(orbMaterial);
        }
    }
}
