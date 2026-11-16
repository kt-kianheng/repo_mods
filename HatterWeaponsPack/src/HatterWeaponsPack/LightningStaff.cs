using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Lightning Staff, the Lightning Wand's upgrade (see LightningWand and ChargedWand).
    ///  - Normal cast: a ChannelTime channel (the wand's glow and sound), then Strikes lightning strikes, SecondsBetweenStrikes apart.
    ///    Each homes in on an enemy on the caster's screen (within ScreenRange and ScreenAngle of where they look, in
    ///    sight), each strike taking a different enemy first while there are enough, then chains on like the wand's bolt.
    ///    Damage lands with the strike (ChargeDamage per charge, counting up to MaxDamageCharges), and the stun still
    ///    applies. No enemy on screen: one harmless bolt at the crosshair, no battery used.
    ///  - Pointing the staff up at the sky readies the orb (see StaffArming, StaffAura). Casting then sends a slow orb of
    ///    lightning at the crosshair that bounces off walls and strikes the nearest enemy every OrbStrikeInterval for
    ///    OrbSeconds (see LightningOrb). It only ever strikes enemies.
    /// </summary>
    public class LightningStaff : LightningWand
    {
        private const float OrbChargeSeconds = 0.6f;
        // "lightning-magic" (the user's file) peaks this long after it starts: it starts this long before the charge completes
        private const float ArmFlashDelay = 0.9f;

        private StaffArming arming;
        private StaffAura aura;
        private Sound armSound;
        private bool hostOrb;                                  // host: the current cast is the orb
        private List<List<Enemy>> hostStrikes;                 // host: each strike's enemies (singleplayer has no network IDs)
        private int orbCounter;
        private readonly Dictionary<int, LightningOrb> orbs = new Dictionary<int, LightningOrb>();

        protected override Vector3 TipLocalPosition => WandBuilder.StaffShape.Tip;
        protected override float ChargeSeconds => IsOrb(cast) ? OrbChargeSeconds : Settings.LStaffChannel.Value;
        protected override string ChargeLines => Settings.LStaffVoiceLines.Value;
        protected override string LinesFor(float[] data) => IsOrb(data) ? Settings.LStaffReleaseLine.Value : ChargeLines;   // shouted as the charged spell is cast
        protected override float ReadyGlow => Armed ? 2.4f : 1f;

        private bool Armed => arming != null && arming.Armed(HeldByLocal, Holder != null);
        private static bool IsOrb(float[] d) => d != null && d.Length >= 1 && (Mathf.Approximately(d[0], 6f) || Mathf.Approximately(d[0], 5f));

        /// <summary>A straight bolt into a teammate does what a staff strike does to an enemy.</summary>
        protected override int AllyBoltDamage =>
            Settings.LStaffChargeDamage.Value
            * Mathf.Min(Mathf.Max(1, Settings.LightningChargesPerBolt.Value), Mathf.Max(1, Settings.LStaffMaxDamageCharges.Value));

        private LightningChain.Options StrikeOptions(float delay) => new LightningChain.Options
        {
            StartDelay = delay,
            InstantDamage = true,
            MaxDamageCharges = Mathf.Max(1, Settings.LStaffMaxDamageCharges.Value),
            DamagePerCharge = Settings.LStaffChargeDamage.Value,
        };

        protected override void Awake()
        {
            base.Awake();
            arming = new StaffArming(null);
            armSound = MakeSound("staff_arm_lightning.pcm", 0.8f);
        }

        // ---------------- readiness (holder's game, reported to everyone) ----------------

        protected override void Update()
        {
            base.Update();
            bool report = arming.Update(HeldByLocal, HolderPitch, HolderView, PhysGrabObject, Holder, Settings.LStaffLookUpAngle.Value, 0f,
                                        Settings.LStaffChargeUpSeconds.Value);
            if (arming.ChargeStarted) Say(Settings.LStaffChargeUpLine.Value);   // the incantation as the charge begins
            if (!report) return;
            try { photonView.RPC(nameof(HWPLightningStaffStateRPC), RpcTarget.Others, arming.State()); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Lightning Staff state report failed: {e}"); }
        }

        [PunRPC]
        public void HWPLightningStaffStateRPC(float[] state, PhotonMessageInfo info)
        {
            PlayerAvatar sender = SemiFunc.PlayerAvatarGetFromPhotonPlayer(info.Sender);
            if (sender == null || sender != Holder) return;
            arming.Receive(state);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            float charge = arming != null ? arming.Progress(HeldByLocal, Holder != null) : 0f;
            if ((charge > 0f || Armed) && aura == null)
                aura = StaffAura.Create(Tip, StaffAura.Lightning, armSound, ArmFlashDelay, Settings.LStaffChargeUpSeconds.Value);
            if (aura != null) aura.SetState(charge, Armed);
        }

        // ---------------- cast (host) ----------------

        /// <summary>Host, at the trigger: [6] (orb, channelling) if the orb is ready, else the wand's [3] (strikes, channelling).</summary>
        protected override float[] HostChargeData()
        {
            hostStrikes = null;
            if (Holder == null) return null;
            float[] s = arming.HostState(HeldByLocal);
            hostOrb = s != null && s[0] > 0.5f;
            return hostOrb ? new[] { 6f } : base.HostChargeData();
        }

        /// <summary>
        /// Host, as the channel ends:
        /// [5, start xyz, direction xyz, orb number] for the orb;
        /// [4, strikes, then per strike: chain number, n, (network ID, charges, x, y, z) x n] for the strikes;
        /// [2, x, y, z] for a harmless bolt (no enemy on screen).
        /// </summary>
        protected override float[] HostReleaseData()
        {
            AimRay(out Vector3 origin, out Vector3 dir, out bool reported);
            if (hostOrb)
            {
                // just past the staff's head, clear of it by the orb's bounce radius (a third of its size, at most 0.5 m)
                Vector3 start = Tip.position + Tip.forward * (0.35f + Mathf.Clamp(Settings.LStaffOrbRadius.Value / 3f, 0.1f, 0.5f));
                Vector3 aimAt = Crosshair(origin, dir, 60f);
                Vector3 d = aimAt - start;
                d = d.sqrMagnitude < 1f ? dir : d.normalized;
                Plugin.Log.LogInfo("Lightning Staff: lightning orb cast.");
                return new[] { 5f, start.x, start.y, start.z, d.x, d.y, d.z, ++orbCounter };
            }

            List<Enemy> screen = LightningTargets.OnScreen(origin, dir, Settings.LStaffScreenRange.Value, Settings.LStaffScreenAngle.Value,
                                                           PhysGrabObject, Holder);
            if (screen.Count == 0)
            {
                Vector3 miss = Crosshair(origin, dir, Settings.LStaffScreenRange.Value);
                return FreeBolt(origin, miss, "Lightning Staff");
            }

            int strikes = Mathf.Max(1, Settings.LStaffStrikes.Value);
            int charges = Mathf.Max(1, Settings.LightningChargesPerBolt.Value);
            int max = Mathf.Clamp(Settings.LightningMaxTargets.Value, 1, charges);
            var data = new List<float> { 4f, strikes };
            hostStrikes = new List<List<Enemy>>();
            var log = new List<string>();
            for (int s = 0; s < strikes; s++)
            {
                // a different enemy first for each strike while there are enough, then round again
                List<Enemy> chain = LightningTargets.ChainFrom(screen[s % screen.Count], Settings.LightningChainRange.Value, max);
                data.Add(NextChainId());
                int[] split = AppendTargets(data, chain, charges);
                hostStrikes.Add(chain);
                var names = new List<string>();
                for (int i = 0; i < chain.Count; i++) names.Add($"{LightningTargets.Name(chain[i])} x{split[i]}");
                log.Add(string.Join(", ", names));
            }
            Plugin.Log.LogInfo($"Lightning Staff: {strikes} strikes: [{string.Join("] [", log)}] (aim from {(reported ? "caster's report" : "host")}).");
            return data.ToArray();
        }

        /// <summary>What the crosshair is on (the first thing along the aim, not the staff or the caster), or far ahead.</summary>
        private Vector3 Crosshair(Vector3 origin, Vector3 dir, float range)
        {
            float best = range;
            PlayerAvatar caster = Holder;
            foreach (RaycastHit h in Physics.RaycastAll(origin, dir, range, ProjectileHits.Mask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.distance >= best) continue;
                if (h.collider.GetComponentInParent<PhysGrabObject>() == PhysGrabObject) continue;
                if (caster != null && h.collider.GetComponentInParent<PlayerAvatar>() == caster) continue;
                best = h.distance;
            }
            return origin + dir * best;
        }

        // ---------------- visuals (every client) ----------------

        protected override void ChargeBegin(float[] data)
        {
            // the battery comes off on every client in the game's ShootRPC, so set the cost here (this arrives first)
            if (gun != null) gun.batteryDrainFullBars = IsOrb(data) ? Mathf.Max(1, Settings.LStaffOrbBatteryBars.Value) : 1;
            base.ChargeBegin(data);
        }

        protected override void Launch()
        {
            float[] d = cast;
            if (d != null && d.Length >= 8 && Mathf.Approximately(d[0], 5f))
            {
                cast = null;
                int id = Mathf.RoundToInt(d[7]);
                if (orbs.TryGetValue(id, out LightningOrb old) && old != null) Destroy(old.gameObject);
                orbs[id] = LightningOrb.Spawn(this, id, new Vector3(d[1], d[2], d[3]), new Vector3(d[4], d[5], d[6]), IsHost, crackleSound);
                if (HeldByLocal) arming.Disarm();
                return;
            }
            if (d != null && d.Length >= 2 && Mathf.Approximately(d[0], 4f))
            {
                cast = null;
                List<List<Enemy>> picked = hostStrikes;
                hostStrikes = null;
                int strikes = Mathf.RoundToInt(d[1]);
                int k = 2;
                float gap = Mathf.Max(0f, Settings.LStaffStrikeGap.Value);
                for (int s = 0; s < strikes && k < d.Length; s++)
                {
                    int id = Mathf.RoundToInt(d[k]);
                    k++;
                    List<LightningChain.Target> targets = ReadTargets(d, ref k, picked != null && s < picked.Count ? picked[s] : null);
                    if (targets.Count == 0) continue;
                    RegisterChain(id, LightningChain.Spawn(this, id, Tip, targets, IsHost, null, crackleSound, StrikeOptions(s * gap)));
                }
                return;
            }
            base.Launch();   // the harmless bolt
        }

        // ---------------- the orb's strikes (host decides, everyone sees) ----------------

        /// <summary>Host: the orb strikes these enemies (a chain); shown on every game from the orb.</summary>
        internal void HostOrbStrike(LightningOrb orb, List<Enemy> chain)
        {
            if (orb == null || chain == null || chain.Count == 0) return;
            int charges = Mathf.Max(1, Settings.LightningChargesPerBolt.Value);
            var data = new List<float> { NextChainId() };
            AppendTargets(data, chain, charges);
            float[] array = data.ToArray();
            if (SemiFunc.IsMultiplayer())
            {
                try { photonView.RPC(nameof(HWPLightningOrbStrikeRPC), RpcTarget.Others, orb.Id, array); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Lightning orb strike RPC failed: {e}"); }
            }
            SpawnOrbStrike(orb.Id, array, chain);
        }

        [PunRPC]
        public void HWPLightningOrbStrikeRPC(int orbId, float[] data, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            SpawnOrbStrike(orbId, data, null);
        }

        private void SpawnOrbStrike(int orbId, float[] data, List<Enemy> picked)
        {
            if (data == null || data.Length < 2 || !orbs.TryGetValue(orbId, out LightningOrb orb) || orb == null) return;
            int id = Mathf.RoundToInt(data[0]);
            int k = 1;
            List<LightningChain.Target> targets = ReadTargets(data, ref k, picked);
            if (targets.Count == 0) return;
            RegisterChain(id, LightningChain.Spawn(this, id, orb.transform, targets, IsHost, null, crackleSound, StrikeOptions(0f)));
        }

        /// <summary>Host: where the orb is, a few times a second, so everyone's copy keeps to the host's path.</summary>
        internal void HostOrbSync(LightningOrb orb)
        {
            if (orb == null || !SemiFunc.IsMultiplayer()) return;
            try { photonView.RPC(nameof(HWPLightningOrbSyncRPC), RpcTarget.Others, orb.Id, orb.transform.position, orb.Velocity); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Lightning orb sync failed: {e}"); }
        }

        [PunRPC]
        public void HWPLightningOrbSyncRPC(int orbId, Vector3 position, Vector3 velocity, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            if (orbs.TryGetValue(orbId, out LightningOrb orb) && orb != null) orb.Correct(position, velocity);
        }

        internal void ForgetOrb(LightningOrb orb)
        {
            if (orb != null && orbs.TryGetValue(orb.Id, out LightningOrb o) && o == orb) orbs.Remove(orb.Id);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            arming?.Destroy();
            if (aura != null) Destroy(aura.gameObject);
            // orbs already out keep going on their own (they just stop striking without the staff to send the strikes)
        }
    }
}
