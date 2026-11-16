using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Healing Wand (see ChargedWand for the trigger, battery and networking). When the trigger is pulled the host
    /// picks the spell from where the caster is looking:
    ///  - looking up at the sky (like the void staff): a lime cylinder of light around the caster, with green beams circling them, heals every player inside;
    ///  - otherwise: a healing beam locks onto the player under the crosshair and heals them over time.
    /// With nobody to aim the beam at, a harmless beam fires at whatever is under the crosshair and no battery is used.
    /// While the wand is raised high enough for the area heal, a lime aura swirls around the tip.
    /// The tip glows brighter the further up the caster looks, like the void staff's crystal.
    ///
    /// Aiming is measured on the caster's own game (only it knows exactly where its camera points) and reported to
    /// everyone a few times a second; the host uses the latest report when the trigger is pulled.
    /// A beam at a teammate who is already at full health heals nothing, so it uses no battery either.
    /// </summary>
    public class HealingWand : ChargedWand
    {
        internal static readonly Color Lime = new Color(0.45f, 1f, 0.15f);
        private const float CastSeconds = 0.15f;   // a short flourish before the spell (also when the line is spoken)
        private const float AimConeDegrees = 12f;  // how close to the crosshair a player must be for the beam

        private float[] cast;                        // what the host decided for the current cast
        private Sound beamSound, areaSound;          // chime-1 for the beam, chime-2 for the area heal
        private Transform aura;                      // shown while raised to the sky and ready
        private ParticleSystem auraSparkles;
        private SoftGlow auraHalo;
        private float auraAmount;

        // latest aim reported by the caster's game: [mode, a, b, c, lookUp] (see Aim)
        private float[] reportedAim;
        private float reportedAt = -10f;
        private float nextReport;
        private const float ReportInterval = 0.1f;
        private const float ReportMaxAge = 0.6f;

        private static readonly FieldInfo HealthField = AccessTools.Field(typeof(PlayerHealth), "health");
        private static readonly FieldInfo MaxHealthField = AccessTools.Field(typeof(PlayerHealth), "maxHealth");

        protected override Color TipColor => Lime;
        protected override Color TipDarkColor => Lime * 0.35f;
        protected override float ChargeSeconds => CastSeconds;
        protected override string ChargeLines => Settings.HealChargeLines.Value;
        protected override Vector3 TipLocalPosition => WandBuilder.TipPosition;

        private float LocalLookUp => Mathf.InverseLerp(0f, -Settings.HealLookUpAngle.Value, HolderPitch);

        /// <summary>How far up the caster is looking (0..1, 1 = area heal armed), from their own game's report.</summary>
        private float LookUp
        {
            get
            {
                if (Holder == null) return 0f;
                if (HeldByLocal) return LocalLookUp;
                return ReportFresh && reportedAim.Length >= 5 ? reportedAim[4] : 0f;
            }
        }

        private bool ReportFresh => reportedAim != null && Time.time - reportedAt <= ReportMaxAge;

        /// <summary>Like the void staff: brighter the more the caster looks up, brightest once the area heal is armed.</summary>
        protected override float ReadyGlow => 1f + 1.2f * LookUp + (LookUp >= 1f ? 0.8f : 0f);

        /// <summary>
        /// Host: [0, target photon ID] for a beam, [1, x, y, z] for an area heal, [2, x, y, z] for a harmless beam to a
        /// point (no target, or the target is already at full health), or null for nothing.
        /// </summary>
        protected override float[] HostChargeData()
        {
            PlayerAvatar caster = Holder;
            if (caster == null) return null;
            // the caster's own measurement if we have a recent one (another player's camera isn't reliable here)
            bool reported = !HeldByLocal && ReportFresh;
            float[] aim = reported ? reportedAim : Aim();
            if (aim == null || aim.Length < 4) { Plugin.Log.LogInfo("Healing Wand: no aim to cast with."); return null; }
            if (aim[0] < 0.5f)
            {
                PlayerAvatar target = SemiFunc.PlayerAvatarGetFromPhotonID(Mathf.RoundToInt(aim[1]));
                if (target == null) { Plugin.Log.LogInfo($"Healing Wand: target {aim[1]} not found."); return null; }
                if (IsFullHealth(target))
                {
                    Plugin.Log.LogInfo($"Healing Wand: {Name(target)} is at full health (free beam).");
                    Vector3 chest = HealEffects.Chest(target);   // nothing to heal: harmless beam to them, no battery
                    return new[] { 2f, chest.x, chest.y, chest.z };
                }
                Plugin.Log.LogInfo($"Healing Wand: {Name(caster)} beams {Name(target)} (aim from {(reported ? "caster's report" : "host")}).");
                return new[] { 0f, aim[1] };
            }
            Plugin.Log.LogInfo(aim[0] < 1.5f ? $"Healing Wand: {Name(caster)} casts the healing cylinder." : $"Healing Wand: {Name(caster)} aimed at nobody (free beam).");
            return new[] { aim[0], aim[1], aim[2], aim[3] };
        }

        private static string Name(PlayerAvatar p)
        {
            try { return SemiFunc.PlayerGetName(p); } catch { return p != null ? p.name : "?"; }
        }

        private static bool IsFullHealth(PlayerAvatar p)
        {
            if (p.playerHealth == null || HealthField == null || MaxHealthField == null) return false;
            int hp = System.Convert.ToInt32(HealthField.GetValue(p.playerHealth));
            int max = System.Convert.ToInt32(MaxHealthField.GetValue(p.playerHealth));
            return max > 0 && hp >= max;
        }

        /// <summary>
        /// The caster's game: what the wand would cast right now, as [mode, a, b, c, lookUp] with mode 0 = beam at
        /// player a (photon ID), 1 = area heal centred at (a, b, c), 2 = harmless beam to (a, b, c).
        /// </summary>
        private float[] Aim()
        {
            PlayerAvatar caster = Holder;
            if (caster == null) return null;
            float look = LocalLookUp;
            if (HolderPitch <= -Settings.HealLookUpAngle.Value)
            {
                Vector3 centre = caster.transform.position + Vector3.up * 0.9f;
                return new[] { 1f, centre.x, centre.y, centre.z, look };
            }

            PlayerAvatar target = FindAimedPlayer(caster);
            if (target == null || target.photonView == null)
            {
                // nobody to heal: a harmless beam to whatever is under the crosshair (no battery used)
                Vector3 end = MissPoint();
                return new[] { 2f, end.x, end.y, end.z, look };
            }
            return new[] { 0f, target.photonView.ViewID, 0f, 0f, look };
        }

        /// <summary>The caster's game reports its aim to everyone (host for casting, others for the glow and aura).</summary>
        protected override void Update()
        {
            base.Update();
            if (!HeldByLocal || !SemiFunc.IsMultiplayer() || Time.time < nextReport) return;
            nextReport = Time.time + ReportInterval;
            float[] aim = Aim();
            if (aim == null) return;
            try { photonView.RPC(nameof(HWPHealAimRPC), RpcTarget.Others, aim); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Healing Wand aim report failed: {e}"); nextReport = Time.time + 2f; }
        }

        [PunRPC]
        public void HWPHealAimRPC(float[] aim, PhotonMessageInfo info)
        {
            // only the player actually holding the wand may report
            PlayerAvatar sender = SemiFunc.PlayerAvatarGetFromPhotonPlayer(info.Sender);
            if (sender == null || sender != Holder || aim == null || aim.Length < 5) return;
            reportedAim = aim;
            reportedAt = Time.time;
        }

        protected override bool CastUsesBattery(float[] data) => data == null || data.Length == 0 || data[0] < 1.5f;

        /// <summary>Where a beam with no target ends: the first thing under the crosshair, or the end of its range.</summary>
        private Vector3 MissPoint()
        {
            Transform view = HolderView;
            Vector3 origin = view != null ? view.position : Tip.position;
            Vector3 aim = view != null ? view.forward : Tip.forward;
            float range = Settings.HealBeamRange.Value;
            foreach (RaycastHit h in Physics.RaycastAll(origin, aim, range, SemiFunc.LayerMaskGetVisionObstruct().value, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<PhysGrabObject>() == PhysGrabObject) continue;
                if (Holder != null && h.collider.GetComponentInParent<PlayerAvatar>() == Holder) continue;
                if (h.distance < range) range = h.distance;
            }
            return origin + aim * range;
        }

        /// <summary>The player closest to the caster's crosshair, in range and in plain sight.</summary>
        private PlayerAvatar FindAimedPlayer(PlayerAvatar caster)
        {
            Transform view = HolderView;
            Vector3 origin = view != null ? view.position : Tip.position;
            Vector3 aim = view != null ? view.forward : Tip.forward;
            float range = Settings.HealBeamRange.Value;
            int blockers = SemiFunc.LayerMaskGetVisionObstruct().value;

            PlayerAvatar best = null;
            float bestAngle = AimConeDegrees;
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (p == null || p == caster || p.playerHealth == null) continue;
                Vector3 chest = HealEffects.Chest(p);
                Vector3 to = chest - origin;
                if (to.magnitude > range) continue;
                float angle = Vector3.Angle(aim, to);
                if (angle > bestAngle) continue;
                // line of sight (ignore anything belonging to the target themselves)
                if (Physics.Linecast(origin, chest, out RaycastHit hit, blockers, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PlayerAvatar>() != p
                    && hit.collider.GetComponentInParent<PhysGrabObject>() != PhysGrabObject) continue;
                best = p; bestAngle = angle;
            }
            return best;
        }

        // "Fantasy Healing Spell - Soft Magic Chime" 1 and 2 by Vadim_Makes_Sound (Pixabay, per the file names)
        internal const string BeamChimeFile = "heal_chime_1.pcm";
        internal const string AreaChimeFile = "heal_chime_2.pcm";

        protected override void Awake()
        {
            base.Awake();
            beamSound = MakeSound(BeamChimeFile);
            areaSound = MakeSound(AreaChimeFile);
        }

        protected override void ChargeBegin(float[] data) { cast = data; }
        protected override void ChargeTick(float progress, float elapsed) { }
        protected override void ChargeEnd() { }

        protected override void Launch()
        {
            float[] d = cast;
            cast = null;
            if (d == null || d.Length < 2) return;
            if (d[0] >= 1.5f && d.Length >= 4)
            {
                HealEffects.SpawnMissBeam(Tip, new Vector3(d[1], d[2], d[3]));
                PlayAt(beamSound, Tip, Tip.position);   // the chime plays even when nobody is healed
            }
            else if (d[0] >= 1f && d.Length >= 4)
            {
                Vector3 centre = new Vector3(d[1], d[2], d[3]);
                HealEffects.SpawnArea(centre, IsHost, null);   // stays where it was cast
                PlayAt(areaSound, null, centre);
            }
            else
            {
                PlayerAvatar target = SemiFunc.PlayerAvatarGetFromPhotonID(Mathf.RoundToInt(d[1]));
                if (target != null) HealEffects.SpawnBeam(this, Tip, target, IsHost);
                PlayAt(beamSound, Tip, Tip.position);
            }
        }

        protected override void ImpactEffect(Vector3 position) { }

        /// <summary>The "armed for the area heal" aura: a pulsing lime halo and swirling sparkles at the tip.</summary>
        protected override void LateUpdate()
        {
            base.LateUpdate();
            bool armed = GunState == 0 && Holder != null && LookUp >= 1f;
            auraAmount = Mathf.MoveTowards(auraAmount, armed ? 1f : 0f, Time.deltaTime * 4f);
            if (auraAmount <= 0f && aura == null) return;
            if (aura == null) BuildAura();

            aura.position = Tip.position;
            float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 6f);
            auraHalo.SetSize(0.12f * auraAmount * pulse);
            auraHalo.SetColor(new Color(Lime.r, Lime.g, Lime.b, 0.6f * auraAmount));
            var emission = auraSparkles.emission;
            emission.rateOverTime = 40f * auraAmount;
        }

        private void BuildAura()
        {
            aura = new GameObject("Heal Aura").transform;
            aura.SetParent(transform, false);
            auraHalo = SoftGlow.Create(aura, Vector3.zero, "Aura Glow");
            auraSparkles = HealEffects.Sparkles(aura, 0.06f, 0f, Vector3.up * 0.4f);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (aura != null) Destroy(aura.gameObject);
        }
    }
}
