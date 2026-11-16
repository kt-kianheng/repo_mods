using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Shared behaviour of the wands (Ember Wand, Frost Wand), on top of the game's gun logic. Lives on the wand root,
    /// next to its PhotonView.
    ///  - ItemGun handles the trigger, battery and reload timer. A trigger pull (ItemGun.Shoot, host side) starts a
    ///    charge instead of firing (see Patches); when the charge is done the host fires the gun for real.
    ///  - While charging, every client shows the wand's charge visuals and the caster says a line (text-to-speech).
    ///  - On the shot every client launches the projectiles; the host's copies decide hits.
    ///  - The tip crystal glows only while the wand is ready (brighter while charging).
    /// </summary>
    public abstract class ChargedWand : MonoBehaviour
    {
        // set by WandBuilder (serialized, so every spawned wand gets its own reference)
        public GameObject tipCrystal;

        // ---- per wand ----
        protected abstract Color TipColor { get; }
        protected abstract Color TipDarkColor { get; }
        protected abstract float ChargeSeconds { get; }
        protected abstract string ChargeLines { get; }
        protected abstract Vector3 TipLocalPosition { get; }
        /// <summary>
        /// Host: anything decided when the trigger is pulled, sent to every client (e.g. icicle positions, a heal
        /// target). Return null to call the cast off (nothing happens and no battery is used).
        /// </summary>
        protected virtual float[] HostChargeData() => new float[0];
        /// <summary>
        /// Host: false for a cast that should not use the battery or start the reload (e.g. a healing beam that hit
        /// nobody). Its visuals still play for everyone.
        /// </summary>
        protected virtual bool CastUsesBattery(float[] data) => true;
        /// <summary>
        /// Host, as the charge ends: a final decision to replace the one made at the trigger pull (e.g. targets picked
        /// where the caster aims now, after a long channel), sent to every client before the shot. Null keeps the first.
        /// </summary>
        protected virtual float[] HostReleaseData() => null;
        /// <summary>Every client: the host's final decision (see HostReleaseData), just before Launch.</summary>
        protected virtual void ReleaseData(float[] data) { }
        /// <summary>How bright the tip is while the wand is ready (1 = normal).</summary>
        protected virtual float ReadyGlow => 1f;
        /// <summary>Every client: the charge started.</summary>
        protected abstract void ChargeBegin(float[] data);
        /// <summary>Every client, each frame while charging: progress 0..1 and seconds since the start.</summary>
        protected abstract void ChargeTick(float progress, float elapsed);
        /// <summary>Every client: remove the charge visuals (the shot went off, or the charge was cancelled).</summary>
        protected abstract void ChargeEnd();
        /// <summary>Every client: the shot went off; launch the projectiles (use NextId and Track).</summary>
        protected abstract void Launch();
        /// <summary>Every client: a projectile landed here (after the host decided the hit).</summary>
        protected abstract void ImpactEffect(Vector3 position);

        protected ItemGun gun;
        private PhysGrabObject physGrabObject;
        protected PhotonView photonView;
        private ItemEquippable equippable;
        private int idCounter;
        private readonly Dictionary<int, GameObject> inFlight = new Dictionary<int, GameObject>();

        private bool charging;                // charge visuals are up (every client)
        private float chargeTimer;
        private bool hostCharging;            // host: a charge is running
        private bool hostFreeCast;            // host: this cast doesn't use the battery
        private float hostChargeTimer;

        private Light tipLight;
        private SoftGlow tipHalo;
        private Renderer tipRenderer;
        private Material tipMaterial;         // this wand's own copy of the tip material
        private float tipGlow = -1f;

        private static readonly FieldInfo StateField = AccessTools.Field(typeof(ItemGun), "stateCurrent");
        private static readonly FieldInfo HeldByLocalField = AccessTools.Field(typeof(PhysGrabObject), "heldByLocalPlayer");
        private static readonly FieldInfo BatteryIntField = AccessTools.Field(typeof(ItemBattery), "batteryLifeInt");
        private static readonly FieldInfo EquippedField = AccessTools.Field(typeof(ItemEquippable), "isEquipped");

        /// <summary>True while the host fires the real shot after a charge (lets ItemGun.Shoot run normally).</summary>
        internal bool Releasing { get; private set; }
        internal PhysGrabObject PhysGrabObject => physGrabObject;
        protected Transform Tip => gun != null && gun.gunMuzzle != null ? gun.gunMuzzle : transform;
        protected bool IsHost => SemiFunc.IsMasterClientOrSingleplayer();

        protected virtual void Awake()
        {
            gun = GetComponent<ItemGun>();
            if (Elements.Hiss == null) Elements.Hiss = MakeSound(Steam.HissClip, 0.55f);   // steam when fire meets ice
            physGrabObject = GetComponent<PhysGrabObject>();
            photonView = GetComponent<PhotonView>();
            equippable = GetComponent<ItemEquippable>();

            var lightObj = new GameObject("Tip Light");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = TipLocalPosition;
            tipLight = lightObj.AddComponent<Light>();
            tipLight.type = LightType.Point; tipLight.color = TipColor; tipLight.range = 0.35f; tipLight.intensity = 0.6f;

            // soft glow around the tip crystal (self-lit, so it shows in the dark)
            tipHalo = SoftGlow.Create(transform, TipLocalPosition, "Tip Halo");

            if (tipCrystal != null) tipRenderer = tipCrystal.GetComponent<Renderer>();
            if (tipRenderer != null) tipMaterial = tipRenderer.material;   // instance, so each wand lights up on its own
        }

        /// <summary>The player holding the wand (null if nobody).</summary>
        protected PlayerAvatar Holder => ProjectileHits.Holder(physGrabObject);

        /// <summary>The holder's view (camera) transform, or null.</summary>
        protected Transform HolderView
        {
            get
            {
                PlayerAvatar p = Holder;
                if (p == null || p.localCamera == null) return null;
                try { return p.localCamera.GetOverrideTransform(); } catch { return null; }
            }
        }

        /// <summary>The holder's camera pitch in degrees: negative when looking up (like the void staff).</summary>
        protected float HolderPitch
        {
            get
            {
                Transform view = HolderView;
                float pitch = view != null ? view.eulerAngles.x : Tip.eulerAngles.x;
                return pitch > 180f ? pitch - 360f : pitch;
            }
        }

        protected int GunState => gun != null && StateField != null ? System.Convert.ToInt32(StateField.GetValue(gun)) : 0;
        protected bool HeldByLocal => HeldByLocalField != null && physGrabObject != null && (bool)HeldByLocalField.GetValue(physGrabObject);

        // ---------------- charge (host decides, everyone sees) ----------------

        /// <summary>
        /// Host: the trigger was pulled (ItemGun.Shoot). Returns true if the wand took it (a charge started or one is
        /// already running); false lets the game handle it (empty battery: dry-fire click).
        /// </summary>
        internal bool HostTriggerPulled()
        {
            if (hostCharging) return true;
            ItemBattery battery = GetComponent<ItemBattery>();
            if (battery != null && BatteryIntField != null && System.Convert.ToInt32(BatteryIntField.GetValue(battery)) <= 0) return false;
            if (GunState != 0) return true;   // still reloading
            float[] data = HostChargeData();
            if (data == null) return true;    // nothing to cast at
            hostCharging = true;
            hostFreeCast = !CastUsesBattery(data);
            hostChargeTimer = 0f;
            SendCharge(true, data);
            return true;
        }

        protected virtual void Update()
        {
            if (!hostCharging) return;
            if (!IsHost) { hostCharging = false; return; }
            // put in the inventory mid-charge: fizzle out
            if (equippable != null && EquippedField != null && (bool)EquippedField.GetValue(equippable))
            {
                hostCharging = false;
                SendCharge(false, new float[0]);
                return;
            }
            hostChargeTimer += Time.deltaTime;
            if (hostChargeTimer < ChargeSeconds) return;
            hostCharging = false;
            float[] release = null;
            try { release = HostReleaseData(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Wand release failed: {e}"); }
            if (release != null)
            {
                hostFreeCast = !CastUsesBattery(release);
                // same PhotonView as the gun's ShootRPC, so every client gets this first
                if (SemiFunc.IsMultiplayer()) photonView.RPC(nameof(HWPWandReleaseRPC), RpcTarget.All, release);
                else ReleaseData(release);
            }
            if (hostFreeCast)
            {
                // visuals only: no battery, no reload
                if (SemiFunc.IsMultiplayer()) photonView.RPC(nameof(HWPWandFreeShotRPC), RpcTarget.All);
                else OnShot();
                return;
            }
            Releasing = true;
            try { gun.Shoot(); }   // the game's normal shot: battery, sound, ShootRPC to everyone, then reload
            catch (System.Exception e) { Plugin.Log.LogWarning($"Wand shot failed: {e}"); }
            finally { Releasing = false; }
        }

        [PunRPC]
        public void HWPWandReleaseRPC(float[] data, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            try { ReleaseData(data ?? new float[0]); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Wand release data failed: {e}"); }
        }

        [PunRPC]
        public void HWPWandFreeShotRPC(PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            OnShot();
        }

        private void SendCharge(bool start, float[] data)
        {
            if (SemiFunc.IsMultiplayer()) photonView.RPC(nameof(HWPWandChargeRPC), RpcTarget.All, start, data);
            else ChargeVisual(start, data);
        }

        [PunRPC]
        public void HWPWandChargeRPC(bool start, float[] data, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            ChargeVisual(start, data);
        }

        private void ChargeVisual(bool start, float[] data)
        {
            StopCharge();
            if (!start) return;
            charging = true;
            chargeTimer = 0f;
            try { ChargeBegin(data ?? new float[0]); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Wand charge visuals failed: {e}"); }
            if (HeldByLocal) Say(LinesFor(data ?? new float[0]));
        }

        private void StopCharge()
        {
            if (!charging) return;
            charging = false;
            try { ChargeEnd(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Wand charge cleanup failed: {e}"); }
        }

        protected virtual void LateUpdate()
        {
            if (charging)
            {
                chargeTimer += Time.deltaTime;
                try { ChargeTick(Mathf.Clamp01(chargeTimer / Mathf.Max(0.05f, ChargeSeconds)), chargeTimer); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Wand charge update failed: {e}"); StopCharge(); }
            }
            // ItemGun states: 0 Idle, 1 OutOfAmmo, 2 Buildup, 3 Shooting, 4 Reloading.
            SetTipGlow(charging ? 1.6f : GunState == 0 ? ReadyGlow : 0f);
        }

        private void SetTipGlow(float glow)
        {
            if (Mathf.Abs(glow - tipGlow) < 0.01f) return;
            tipGlow = glow;
            // always glowing; brighter when ready, brightest while charging / raised (healing)
            float level = glow > 0f ? glow : 0f;
            tipLight.intensity = 0.6f + 0.8f * level;
            tipLight.range = 0.35f + 0.2f * level;
            if (tipHalo != null)
            {
                // soft light around the crystal, fading to nothing at the edge
                float a = glow > 0f ? Mathf.Clamp01(0.5f + 0.15f * level) : 0.3f;
                tipHalo.SetSize(glow > 0f ? 0.05f + 0.015f * level : 0.035f);   // a few cm: just a glow on the crystal
                tipHalo.SetColor(new Color(TipColor.r, TipColor.g, TipColor.b, a));
            }
            if (tipMaterial == null) return;
            // self-lit colour (see MeshLibrary): ready = bright with a hint of white; spent = the plain colour, dimmer
            Color body = glow > 0f ? Color.Lerp(TipColor, Color.white, Mathf.Clamp01(0.15f + 0.15f * level)) : Color.Lerp(TipDarkColor, TipColor, 0.5f);
            body.a = 1f;
            if (tipMaterial.HasProperty("_Color")) tipMaterial.SetColor("_Color", body);
            if (tipMaterial.HasProperty("_BaseColor")) tipMaterial.SetColor("_BaseColor", body);
        }

        /// <summary>The lines the caster says as this cast starts (the staves' charged spells say their own).</summary>
        protected virtual string LinesFor(float[] data) => ChargeLines;

        /// <summary>
        /// The local player speaks a random line from these (separated by |) through the game's chat text-to-speech,
        /// which everyone hears.
        /// </summary>
        protected static void Say(string raw)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(raw)) return;
                var lines = new List<string>();
                foreach (string l in raw.Split('|')) { string t = l.Trim(); if (t.Length > 0 && !t.StartsWith("/")) lines.Add(t); }
                if (lines.Count == 0) return;
                PlayerAvatar me = SemiFunc.PlayerAvatarLocal();
                if (me != null) me.ChatMessageSend(lines[Random.Range(0, lines.Count)], null);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Wand speech failed: {e}"); }
        }

        // ---------------- shot and projectiles ----------------

        /// <summary>Called on every client when the gun's shot is broadcast (ItemGun.ShootRPC), after the charge.</summary>
        internal void OnShot()
        {
            try { Launch(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Wand launch failed: {e}"); }
            StopCharge();
        }

        /// <summary>A game Sound with the gun's settings (volume group, falloff) playing one of our clips.</summary>
        protected Sound MakeSound(string file, float volume = 0.7f) => MakeSound(SoundLibrary.Get(file), volume);

        /// <summary>A game Sound with the gun's settings playing a clip made in code.</summary>
        protected Sound MakeSound(AudioClip clip, float volume = 0.7f)
        {
            if (clip == null || gun == null || gun.soundShoot == null) return null;
            var s = new Sound();
            Sound.CopySound(gun.soundShoot, s);
            s.Sounds = new[] { clip };
            s.Volume = volume;
            s.VolumeRandom = 0f;
            s.Pitch = 1f;
            s.PitchRandom = 0f;
            return s;
        }

        protected static void PlayAt(Sound s, Transform follow, Vector3 position)
        {
            if (s == null) return;
            try
            {
                if (follow != null) s.Play(follow, 1f, 1f, 1f, 1f);
                else s.Play(position, 1f, 1f, 1f, 1f);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Wand sound failed: {e}"); }
        }

        protected int NextId() => ++idCounter;
        protected void Track(int id, GameObject projectile) { if (projectile != null) inFlight[id] = projectile; }
        internal void Forget(int id) => inFlight.Remove(id);

        /// <summary>Host only: an authoritative projectile hit something (or ran out of time).</summary>
        internal void HostImpact(int id, Vector3 position)
        {
            if (SemiFunc.IsMultiplayer())
                photonView.RPC(nameof(HWPWandImpactRPC), RpcTarget.All, id, position);
            else
                Impact(id, position);
        }

        [PunRPC]
        public void HWPWandImpactRPC(int id, Vector3 position, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Impact(id, position);
        }

        private void Impact(int id, Vector3 position)
        {
            if (inFlight.TryGetValue(id, out GameObject p))
            {
                inFlight.Remove(id);
                if (p != null) Destroy(p);
            }
            try { ImpactEffect(position); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Wand impact effect failed: {e}"); }
        }

        protected virtual void OnDestroy()
        {
            if (tipMaterial != null) Destroy(tipMaterial);
            StopCharge();
            foreach (var p in inFlight.Values) if (p != null) Destroy(p);
            inFlight.Clear();
        }
    }
}
