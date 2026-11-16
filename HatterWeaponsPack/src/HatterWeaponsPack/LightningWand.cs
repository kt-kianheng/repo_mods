using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Lightning Wand (see ChargedWand for the trigger, battery and networking). On the trigger the host picks the
    /// enemy under the caster's crosshair, then chains on to the nearest enemy in reach of the last one (clear line
    /// needed), up to MaxTargets. The bolt's charges are shared out evenly, the first enemy taking any left over
    /// (4 charges: 1 enemy 4, 2 enemies 2+2, 3 enemies 2+1+1, 4 enemies 1 each).
    /// A jagged yellow bolt runs from the tip to the first enemy, then jumps on every ChainDelay seconds. Each enemy
    /// struck is electrocuted and stunned for StunSeconds, then takes DamagePerCharge x its charges (see LightningChain).
    /// With no enemy in sight a harmless bolt strikes whatever is under the crosshair and no battery is used.
    ///
    /// Aiming is measured on the caster's own game and reported to the host a few times a second (like the Healing Wand).
    /// </summary>
    public class LightningWand : ChargedWand
    {
        internal static readonly Color Yellow = new Color(1f, 0.86f, 0.12f);
        private const float AimConeDegrees = 10f;   // how close to the crosshair the first enemy must be
        private const float ReportInterval = 0.1f;
        private const float ReportMaxAge = 0.6f;

        protected float[] cast;              // what the host decided for the current cast
        private List<Enemy> hostChain;       // host: the enemies picked (singleplayer enemies have no network IDs)
        private PlayerAvatar hostAlly;       // host: the teammate a straight bolt hits (no enemy to strike)
        private Sound castSound;
        protected Sound crackleSound;
        private bool castPlayed;
        // "Lightning Magic 4" by Yodguard (Pixabay), trimmed to 1.6 s: a rising surge that cuts off sharply at 1.04 s.
        // It starts this long before the bolt so its cut-off lands on the bolt.
        private const float CastLead = 1.04f;
        private ShockEffect tipSparks;
        private SoftGlow chargeGlow;
        private Light chargeLight;
        private const float MaxGlowRadius = 0.1f;   // the growing glow at the tip reaches 10 cm radius
        private int chainCounter;                                                    // host: numbers each bolt
        private readonly Dictionary<int, LightningChain> chains = new Dictionary<int, LightningChain>();

        private float[] reportedAim;         // [origin xyz, direction xyz] from the caster's game
        private float reportedAt = -10f;
        private float nextReport;

        protected override Color TipColor => Yellow;
        protected override Color TipDarkColor => new Color(0.3f, 0.24f, 0.03f);
        protected override float ChargeSeconds => Settings.LightningChargeSeconds.Value;
        protected override string ChargeLines => Settings.LightningChargeLines.Value;
        protected override Vector3 TipLocalPosition => WandBuilder.TipPosition;

        protected override void Awake()
        {
            base.Awake();
            castSound = MakeSound("lightning_cast.pcm", 0.75f);
            // "Electrified Granules v1" (freesound_community, Pixabay), a 4 s stretch: crackles on each struck enemy
            crackleSound = MakeSound("lightning_crackle.pcm", 0.45f);
        }

        // ---------------- aim ----------------

        protected float[] LocalAim()
        {
            Transform view = HolderView;
            Vector3 o = view != null ? view.position : Tip.position;
            Vector3 d = view != null ? view.forward : Tip.forward;
            return new[] { o.x, o.y, o.z, d.x, d.y, d.z };
        }

        /// <summary>The caster's game reports where it aims to the host (only it knows exactly where its camera points).</summary>
        protected override void Update()
        {
            base.Update();
            if (!HeldByLocal || !SemiFunc.IsMultiplayer() || IsHost || Time.time < nextReport) return;
            nextReport = Time.time + ReportInterval;
            try { photonView.RPC(nameof(HWPLightningAimRPC), RpcTarget.MasterClient, LocalAim()); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Lightning Wand aim report failed: {e}"); nextReport = Time.time + 2f; }
        }

        [PunRPC]
        public void HWPLightningAimRPC(float[] aim, PhotonMessageInfo info)
        {
            PlayerAvatar sender = SemiFunc.PlayerAvatarGetFromPhotonPlayer(info.Sender);
            if (sender == null || sender != Holder || aim == null || aim.Length < 6) return;
            reportedAim = aim;
            reportedAt = Time.time;
        }

        /// <summary>Host: where the caster aims (their own report if recent, else measured here).</summary>
        protected void AimRay(out Vector3 origin, out Vector3 dir, out bool reported)
        {
            reported = !HeldByLocal && reportedAim != null && Time.time - reportedAt <= ReportMaxAge;
            float[] a = reported ? reportedAim : LocalAim();
            origin = new Vector3(a[0], a[1], a[2]);
            dir = new Vector3(a[3], a[4], a[5]);
            if (dir.sqrMagnitude < 1e-6f) dir = Tip.forward;
            dir.Normalize();
        }

        protected int NextChainId() => ++chainCounter;

        private protected void RegisterChain(int id, LightningChain chain)
        {
            if (chains.TryGetValue(id, out LightningChain old) && old != null && old != chain) Destroy(old.gameObject);
            chains[id] = chain;
        }

        /// <summary>
        /// Adds a chain's targets to cast data: n, then per enemy network ID, charges, x, y, z (charges shared out
        /// evenly, the first enemies taking what's left over). Returns the charges per enemy.
        /// </summary>
        protected static int[] AppendTargets(List<float> data, List<Enemy> chain, int charges)
        {
            int[] split = LightningTargets.Split(charges, chain.Count);
            data.Add(chain.Count);
            for (int i = 0; i < chain.Count; i++)
            {
                Vector3 c = LightningTargets.Centre(chain[i]);
                data.Add(LightningTargets.ViewId(chain[i]));
                data.Add(split[i]);
                data.Add(c.x); data.Add(c.y); data.Add(c.z);
            }
            return split;
        }

        /// <summary>Reads n targets written by AppendTargets, from index k (moved past them).</summary>
        private protected List<LightningChain.Target> ReadTargets(float[] d, ref int k, List<Enemy> picked)
        {
            var targets = new List<LightningChain.Target>();
            if (d == null || k >= d.Length) return targets;
            int n = Mathf.RoundToInt(d[k]);
            k++;
            for (int i = 0; i < n && k + 4 < d.Length; i++, k += 5)
            {
                Enemy e = IsHost && picked != null && i < picked.Count ? picked[i] : LightningTargets.FromViewId(Mathf.RoundToInt(d[k]));
                targets.Add(new LightningChain.Target
                {
                    Enemy = e,
                    Charges = Mathf.Max(1, Mathf.RoundToInt(d[k + 1])),
                    Position = new Vector3(d[k + 2], d[k + 3], d[k + 4]),
                });
            }
            return targets;
        }

        // ---------------- cast (host) ----------------

        /// <summary>Host, at the trigger pull: start channelling ([3] = targets not picked yet).</summary>
        protected override float[] HostChargeData()
        {
            hostChain = null;
            return Holder != null ? new[] { 3f } : null;
        }

        /// <summary>
        /// Host, as the channel ends: the targets, picked where the caster aims now.
        /// [0, n, bolt number, then per enemy: network ID, charges, x, y, z] for a bolt, or [2, x, y, z] for a harmless
        /// bolt to a point.
        /// </summary>
        protected override float[] HostReleaseData()
        {
            hostChain = null;
            PlayerAvatar caster = Holder;
            bool reported = !HeldByLocal && reportedAim != null && Time.time - reportedAt <= ReportMaxAge;
            float[] a = reported ? reportedAim : LocalAim();
            Vector3 origin = new Vector3(a[0], a[1], a[2]);
            Vector3 dir = new Vector3(a[3], a[4], a[5]);
            if (dir.sqrMagnitude < 1e-6f) dir = Tip.forward;
            dir.Normalize();

            int charges = Mathf.Max(1, Settings.LightningChargesPerBolt.Value);
            int max = Mathf.Clamp(Settings.LightningMaxTargets.Value, 1, charges);
            List<Enemy> chain = LightningTargets.Pick(origin, dir, Settings.LightningRange.Value, AimConeDegrees,
                                                      Settings.LightningChainRange.Value, max, PhysGrabObject, caster, out Vector3 missPoint);
            if (chain.Count == 0) return FreeBolt(origin, missPoint, "Lightning Wand");

            int[] split = LightningTargets.Split(charges, chain.Count);
            hostChain = chain;
            var data = new List<float> { 0f, chain.Count, ++chainCounter };
            var log = new List<string>();
            for (int i = 0; i < chain.Count; i++)
            {
                Vector3 c = LightningTargets.Centre(chain[i]);
                data.Add(LightningTargets.ViewId(chain[i]));
                data.Add(split[i]);
                data.Add(c.x); data.Add(c.y); data.Add(c.z);
                log.Add($"{LightningTargets.Name(chain[i])} x{split[i]}");
            }
            Plugin.Log.LogInfo($"Lightning Wand: bolt chains to {string.Join(", ", log)} (aim from {(reported ? "caster's report" : "host")}).");
            return data.ToArray();
        }

        /// <summary>What a straight bolt does to a teammate: an enemy's damage for the whole bolt (charges counted as for an enemy).</summary>
        protected virtual int AllyBoltDamage =>
            Settings.LightningDamagePerCharge.Value
            * Mathf.Min(Mathf.Max(1, Settings.LightningChargesPerBolt.Value), Mathf.Max(1, Settings.LightningMaxDamageCharges.Value));

        /// <summary>
        /// Host, with no enemy to strike: the bolt goes straight from the caster's view to the crosshair point. The first
        /// teammate on that line (never the caster) is hit: [8, x, y, z, their network ID], using the battery. Nobody on
        /// it: [2, x, y, z], a harmless bolt that uses no battery.
        /// </summary>
        protected float[] FreeBolt(Vector3 origin, Vector3 end, string who)
        {
            hostAlly = null;
            Vector3 line = end - origin;
            if (Settings.LightningBoltHitsAllies.Value && line.sqrMagnitude > 1e-4f
                && ProjectileHits.Player(origin, line, 0.1f, line.magnitude, Holder, out PlayerAvatar ally, out Vector3 point)
                && ProjectileHits.Alive(ally))
            {
                hostAlly = ally;
                int view = ally.photonView != null ? ally.photonView.ViewID : 0;
                Plugin.Log.LogInfo($"{who}: no enemy; the straight bolt hits {SemiFunc.PlayerGetName(ally)} for {AllyBoltDamage}.");
                return new[] { 8f, point.x, point.y, point.z, view };
            }
            Plugin.Log.LogInfo($"{who}: no enemy in sight (free bolt).");
            return new[] { 2f, end.x, end.y, end.z };
        }

        protected override bool CastUsesBattery(float[] data) => data == null || data.Length == 0 || data[0] < 1.5f || data[0] > 2.5f;

        protected override void ReleaseData(float[] data) { cast = data; }

        // ---------------- visuals (every client) ----------------

        protected override void ChargeBegin(float[] data)
        {
            cast = data;
            castPlayed = false;
            ChargeEnd();
            tipSparks = ShockEffect.Create(Tip, Vector3.zero, Vector3.one * 0.07f, ChargeSeconds + 0.5f, null, 0.4f);
            chargeGlow = SoftGlow.Create(Tip, Vector3.zero, "Lightning Charge Glow");
            chargeGlow.SetSize(0f);
            var lightObj = new GameObject("Lightning Charge Light");
            lightObj.transform.SetParent(Tip, false);
            chargeLight = lightObj.AddComponent<Light>();
            chargeLight.type = LightType.Point; chargeLight.color = Yellow; chargeLight.intensity = 0f; chargeLight.range = 0.3f;
        }

        /// <summary>A yellow glow at the tip that swells over the channel, up to 10 cm radius, flickering as it fills.</summary>
        protected override void ChargeTick(float progress, float elapsed)
        {
            if (!castPlayed && elapsed >= ChargeSeconds - CastLead)
            {
                castPlayed = true;
                PlayAt(castSound, Tip, Tip.position);
            }
            if (chargeGlow == null) return;
            float grow = progress * progress * (3f - 2f * progress);          // eases in and out
            float flicker = 1f - 0.12f * progress * Random.value;             // only ever shrinks it, so it never passes 10 cm
            float radius = MaxGlowRadius * Mathf.Lerp(0.15f, 1f, grow) * flicker;
            chargeGlow.SetSize(radius * 2f);                                  // SetSize takes the diameter
            chargeGlow.SetColor(new Color(Yellow.r, Mathf.Lerp(Yellow.g, 1f, 0.3f * grow), Yellow.b + 0.3f * grow, Mathf.Lerp(0.35f, 0.95f, grow)));
            chargeLight.intensity = Mathf.Lerp(0.3f, 3.5f, grow) * flicker;
            chargeLight.range = Mathf.Lerp(0.3f, 2f, grow);
        }

        protected override void ChargeEnd()
        {
            if (tipSparks != null) Destroy(tipSparks.gameObject);
            if (chargeGlow != null) Destroy(chargeGlow.gameObject);
            if (chargeLight != null) Destroy(chargeLight.gameObject);
            tipSparks = null; chargeGlow = null; chargeLight = null;
        }

        protected override void Launch()
        {
            float[] d = cast;
            List<Enemy> picked = hostChain;
            cast = null;
            hostChain = null;
            if (d == null || d.Length < 2) return;

            if (Mathf.Approximately(d[0], 8f) && d.Length >= 5)
            {
                // a straight bolt into a teammate
                Vector3 end = new Vector3(d[1], d[2], d[3]);
                PlayerAvatar ally = IsHost ? hostAlly : SemiFunc.PlayerAvatarGetFromPhotonID(Mathf.RoundToInt(d[4]));
                hostAlly = null;
                LightningChain.SpawnMiss(Tip, end, null);
                if (ally != null)
                    ShockEffect.Create(ally.transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.3f, 0.75f, 0.3f), 1f, crackleSound, 0.8f);
                if (IsHost && ally != null) ProjectileHits.HurtPlayer(ally, AllyBoltDamage, end);
                return;
            }
            if (d[0] >= 1.5f && d.Length >= 4)
            {
                LightningChain.SpawnMiss(Tip, new Vector3(d[1], d[2], d[3]), null);
                return;
            }

            int n = Mathf.RoundToInt(d[1]);
            int id = d.Length > 2 ? Mathf.RoundToInt(d[2]) : 0;
            var targets = new List<LightningChain.Target>();
            for (int i = 0; i < n; i++)
            {
                int k = 3 + i * 5;
                if (k + 4 >= d.Length) break;
                Enemy e = IsHost && picked != null && i < picked.Count ? picked[i] : LightningTargets.FromViewId(Mathf.RoundToInt(d[k]));
                targets.Add(new LightningChain.Target
                {
                    Enemy = e,
                    Charges = Mathf.Max(1, Mathf.RoundToInt(d[k + 1])),
                    Position = new Vector3(d[k + 2], d[k + 3], d[k + 4]),
                });
            }
            if (targets.Count == 0) return;
            if (chains.TryGetValue(id, out LightningChain old) && old != null) Destroy(old.gameObject);
            chains[id] = LightningChain.Spawn(this, id, Tip, targets, IsHost, null, crackleSound);
        }

        // ---------------- chain jumps (the host decides each one when it happens) ----------------

        internal void Forget(LightningChain chain)
        {
            if (chain != null && chains.TryGetValue(chain.Id, out LightningChain c) && c == chain) chains.Remove(chain.Id);
        }

        /// <summary>Host: the bolt jumped to target <paramref name="index"/>; show it on everyone else's game.</summary>
        internal void HostJumped(int id, int index)
        {
            if (!SemiFunc.IsMultiplayer()) return;
            try { photonView.RPC(nameof(HWPLightningJumpRPC), RpcTarget.Others, id, index); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Lightning jump RPC failed: {e}"); }
        }

        /// <summary>Host: no clear line to target <paramref name="index"/>; the bolt ends there for everyone.</summary>
        internal void HostStopped(int id, int index)
        {
            if (!SemiFunc.IsMultiplayer()) return;
            try { photonView.RPC(nameof(HWPLightningStopRPC), RpcTarget.Others, id, index); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Lightning stop RPC failed: {e}"); }
        }

        [PunRPC]
        public void HWPLightningJumpRPC(int id, int index, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            if (chains.TryGetValue(id, out LightningChain c) && c != null) c.RemoteJump(index);
        }

        [PunRPC]
        public void HWPLightningStopRPC(int id, int index, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            if (chains.TryGetValue(id, out LightningChain c) && c != null) c.RemoteStop(index);
        }

        protected override void ImpactEffect(Vector3 position) { }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ChargeEnd();
        }
    }
}
