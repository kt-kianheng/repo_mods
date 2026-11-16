using System.Collections;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Ember Staff, the Ember Wand's upgrade (see ChargedWand for the trigger, battery and networking).
    ///  - Normal cast: like the Ember Wand, the fireballs grow at the head during the charge, then fly one after another
    ///    (FireballsPerVolley, VolleyGap apart), each an Ember Wand fireball.
    ///  - Pointing the staff up at the sky readies the inferno (pointing up again puts it away): the crystal flares and
    ///    embers swirl round the head. While it's ready, aiming at the floor shows a ring there on the holder's screen;
    ///    casting then raises a pillar of fire on that ring, reaching the ceiling (see Inferno). Ready but not aimed at
    ///    a floor: nothing happens and no battery is used.
    /// Readiness and the ring are worked out on the holder's game (only it knows where its camera points) and reported
    /// to everyone a few times a second; the host casts from the latest report.
    /// </summary>
    public class EmberStaff : ChargedWand
    {
        internal static readonly Color Ember = new Color(1f, 0.3f, 0.04f);
        private const float InfernoChargeSeconds = 0.4f;   // a short flare before the pillar rises
        private const float GrowSize = 0.6f;               // volley fireballs grow to this (Ember Wand: 1)

        private float[] pending;                           // the host's decision for the current cast
        private FireballFx[] growing;                      // the volley growing at the head during the charge
        private FireballFx[] waiting;                      // launched volley fireballs not thrown yet
        private Sound whoosh, infernoCast, infernoBurn, fireSound;

        // inferno readiness (see StaffArming)
        private StaffArming arming;
        private StaffAura aura;
        private Sound armSound;
        // "fire-magic" (the user's file) peaks this long after it starts: it starts this long before the charge completes
        private const float ArmFlashDelay = 0.8f;

        protected override Color TipColor => EmberWand.Red;          // the crystal matches the Ember Wand's tip
        protected override Color TipDarkColor => EmberWand.DarkRed;
        protected override float ChargeSeconds => IsInferno(pending) ? InfernoChargeSeconds : Settings.StaffChargeSeconds.Value;
        protected override string ChargeLines => Settings.StaffVoiceLines.Value;
        protected override string LinesFor(float[] data) => IsInferno(data) ? Settings.StaffReleaseLine.Value : ChargeLines;   // shouted as the charged spell is cast
        protected override Vector3 TipLocalPosition => WandBuilder.StaffShape.Tip;
        protected override float ReadyGlow => Armed ? 2.4f : 1f;

        private static bool IsInferno(float[] d) => d != null && d.Length >= 4 && d[0] > 0.5f;
        private bool Armed => arming != null && arming.Armed(HeldByLocal, Holder != null);

        protected override void Awake()
        {
            base.Awake();
            whoosh = MakeSound(WandBuilder.WhooshClip, 0.6f);
            // "Fire Spell Impact" by Dragon Studio and "Burning Fire Loop 1" by Floraphonic (Pixabay, per the file names)
            infernoCast = MakeSound("inferno_cast.pcm", 0.9f);
            infernoBurn = MakeSound("inferno_burn.pcm", 0.75f);
            fireSound = MakeSound("inferno_burn.pcm", 0.4f);   // the fireballs' patches of fire (quieter than the inferno)
            armSound = MakeSound("staff_arm_fire.pcm", 0.8f);
            arming = new StaffArming(() => InfernoRing.Create(Settings.StaffRingRadius.Value), allowWalls: true);
        }

        // ---------------- readiness and the ring (holder's game) ----------------

        protected override void Update()
        {
            base.Update();
            bool report = arming.Update(HeldByLocal, HolderPitch, HolderView, PhysGrabObject, Holder,
                                        Settings.StaffLookUpAngle.Value, Settings.StaffCastRange.Value, Settings.StaffChargeUpSeconds.Value);
            if (arming.ChargeStarted) Say(Settings.StaffChargeUpLine.Value);   // the incantation as the charge begins
            if (!report) return;
            try { photonView.RPC(nameof(HWPStaffStateRPC), RpcTarget.Others, arming.State()); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Ember Staff state report failed: {e}"); }
        }

        [PunRPC]
        public void HWPStaffStateRPC(float[] state, PhotonMessageInfo info)
        {
            PlayerAvatar sender = SemiFunc.PlayerAvatarGetFromPhotonPlayer(info.Sender);
            if (sender == null || sender != Holder) return;
            arming.Receive(state);
        }

        // ---------------- cast (host) ----------------

        /// <summary>
        /// Host: [1, x, y, z, normal xyz] for an inferno on the ring (on the floor it rises; on a wall it shoots out
        /// sideways), [0] for a volley, or null (ready but no floor or wall aimed at).
        /// </summary>
        protected override float[] HostChargeData()
        {
            PlayerAvatar caster = Holder;
            if (caster == null) return null;
            float[] s = arming.HostState(HeldByLocal);
            if (s != null && s[0] > 0.5f)
            {
                if (s[1] < 0.5f) { Plugin.Log.LogInfo("Ember Staff: inferno ready, but not aimed at the floor or a wall."); return null; }
                Vector3 n = StaffArming.NormalOf(s);
                Plugin.Log.LogInfo($"Ember Staff: inferno at ({s[2]:0.0}, {s[3]:0.0}, {s[4]:0.0}), {(n.y >= 0.7f ? "on the floor" : "on a wall")}.");
                return new[] { 1f, s[2], s[3], s[4], n.x, n.y, n.z };
            }
            return new[] { 0f };
        }

        // ---------------- visuals (every client) ----------------

        protected override void ChargeBegin(float[] data)
        {
            pending = data;
            ClearGrowing();
            // the battery comes off on every client in the game's ShootRPC, so set the cost here (this arrives first)
            if (gun != null) gun.batteryDrainFullBars = IsInferno(data) ? Mathf.Max(1, Settings.StaffInfernoBatteryBars.Value) : 1;
            if (IsInferno(data)) return;
            int n = Mathf.Max(1, Settings.StaffFireballs.Value);
            growing = new FireballFx[n];
            for (int i = 0; i < n; i++)
            {
                growing[i] = FireballFx.Create("HWP Staff Fireball (charging)", VolleySpot(i, n), Tip.rotation, flying: false);
                growing[i].Size = 0.04f;
            }
        }

        /// <summary>Where volley fireball i of n sits round the head: a small ring just in front of the crystal.</summary>
        private Vector3 VolleySpot(int i, int n)
        {
            Transform tip = Tip;
            if (n == 1) return tip.position + tip.forward * 0.16f;
            float a = Mathf.PI * 0.5f + i * Mathf.PI * 2f / n;
            return tip.position + tip.forward * 0.14f + (tip.right * Mathf.Cos(a) + tip.up * Mathf.Sin(a)) * 0.11f;
        }

        protected override void ChargeTick(float progress, float elapsed)
        {
            if (growing == null) return;
            for (int i = 0; i < growing.Length; i++)
            {
                if (growing[i] == null) continue;
                growing[i].Size = Mathf.Lerp(0.04f, GrowSize, progress);
                growing[i].transform.SetPositionAndRotation(VolleySpot(i, growing.Length), Tip.rotation);
            }
        }

        protected override void ChargeEnd() => ClearGrowing();

        protected override void Launch()
        {
            float[] d = pending;
            pending = null;
            if (d == null || d.Length < 1) return;
            if (IsInferno(d))
            {
                Vector3 axis = d.Length >= 7 ? new Vector3(d[4], d[5], d[6]) : Vector3.up;
                Inferno.Spawn(new Vector3(d[1], d[2], d[3]), axis, IsHost, infernoCast, infernoBurn);
                if (HeldByLocal) arming.Disarm();
                return;
            }
            // the volley: the grown fireballs stay at the head and fly one after another
            ClearWaiting();
            waiting = growing;
            growing = null;
            StartCoroutine(Volley(waiting != null ? waiting.Length : Mathf.Max(1, Settings.StaffFireballs.Value)));
        }

        private IEnumerator Volley(int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (i > 0) yield return new WaitForSeconds(Mathf.Max(0f, Settings.StaffVolleyGap.Value));
                FireballFx w = waiting != null && i < waiting.Length ? waiting[i] : null;
                Vector3 start = w != null ? w.transform.position : VolleySpot(i, n);
                if (w != null) { Destroy(w.gameObject); waiting[i] = null; }
                int id = NextId();
                Fireball ball = Fireball.Spawn(this, id, start, Tip.rotation, IsHost);
                if (ball != null) Track(id, ball.gameObject);
                PlayAt(whoosh, null, start);
            }
            waiting = null;
        }

        protected override void ImpactEffect(Vector3 position) => Fireball.Explode(position, fireSound);

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (waiting != null)
                for (int i = 0; i < waiting.Length; i++)
                    if (waiting[i] != null)
                    {
                        waiting[i].Size = GrowSize;
                        waiting[i].transform.SetPositionAndRotation(VolleySpot(i, waiting.Length), Tip.rotation);
                    }

            float charge = arming != null ? arming.Progress(HeldByLocal, Holder != null) : 0f;
            if ((charge > 0f || Armed) && aura == null)
                aura = StaffAura.Create(Tip, StaffAura.Ember, armSound, ArmFlashDelay, Settings.StaffChargeUpSeconds.Value);
            if (aura != null) aura.SetState(charge, Armed);
        }

        private void ClearGrowing()
        {
            if (growing != null) foreach (FireballFx f in growing) if (f != null) Destroy(f.gameObject);
            growing = null;
        }

        private void ClearWaiting()
        {
            if (waiting != null) foreach (FireballFx f in waiting) if (f != null) Destroy(f.gameObject);
            waiting = null;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ClearGrowing();
            ClearWaiting();
            arming?.Destroy();
            if (aura != null) Destroy(aura.gameObject);
        }
    }
}
