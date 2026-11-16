using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Frost Wand: a volley. The trigger uses a battery bar straight away (see ChargedWand); then IciclesPerCast icicles
    /// form one after another, IcicleInterval apart, in a ring 10 cm in front of the tip, each pointing at the crosshair.
    /// LaunchDelay after it formed, each icicle flies from its spot towards the crosshair (so they converge at a slight
    /// angle instead of flying parallel). An icicle that hits an enemy stuns it for StunSeconds and frosts it over.
    ///
    /// Forming is shown on every client. The host decides each launch (from where the caster aims, reported by the
    /// caster's game a few times a second) and sends the icicle's start and direction to everyone, so all clients fly
    /// the same icicles with the same IDs. The reload runs from the trigger and covers the whole volley.
    /// </summary>
    public class FrostWand : ChargedWand
    {
        internal static readonly Color Cyan = new Color(0.1f, 0.85f, 1f);
        private const float GrowSeconds = 0.25f;     // how long each icicle takes to form
        private const float MaxConvergeAngle = 20f;  // never angled more than this from the aim line
        private const float AimRange = 40f;
        private const float ReportInterval = 0.1f;
        private const float ReportMaxAge = 0.6f;

        private enum Ice { Pending, Forming, Flown, Gone }

        // the current volley (every client)
        private float[] pending;                      // host's data from the trigger: [volley, angle, distance, ...]
        private int volley;
        private bool volleyActive;
        private float volleyAge;
        private Vector2[] offsets;                    // (angle in radians, distance) per icicle
        private Ice[] state;
        private Transform[] forming;
        private int hostVolleyCounter;

        private float[] reportedAim;                  // [origin xyz, direction xyz] from the caster's game
        private float reportedAt = -10f;
        private float nextReport;

        // "Iced Magic" 1, 2 and 5 by Yodguard (Pixabay, per the file names), trimmed to 1.8 s
        private static readonly string[] ConjureFiles = { "ice_conjure_1.pcm", "ice_conjure_2.pcm", "ice_conjure_3.pcm" };
        protected Sound[] conjureSounds;
        protected Sound flySound;      // follows each icicle in flight
        protected Sound crashSound;    // where an icicle lands
        private ItemEquippable equip;

        protected override void Awake()
        {
            base.Awake();
            conjureSounds = new Sound[ConjureFiles.Length];
            for (int i = 0; i < ConjureFiles.Length; i++) conjureSounds[i] = MakeSound(ConjureFiles[i], 0.5f);
            // "Ice Fly" and "Icicle Crashing" (the user's sound files)
            flySound = MakeSound("icicle_fly.pcm", 0.45f);
            crashSound = MakeSound("icicle_crash.pcm", 0.6f);
            equip = GetComponent<ItemEquippable>();
        }

        protected override Color TipColor => Cyan;
        internal static readonly Color DarkCyan = new Color(0.03f, 0.15f, 0.2f);
        protected override Color TipDarkColor => DarkCyan;
        protected override float ChargeSeconds => 0.05f;   // no charge: the volley itself is the build-up
        protected override string ChargeLines => Settings.FrostChargeLines.Value;
        protected override Vector3 TipLocalPosition => WandBuilder.TipPosition;

        // the volley (the Frost Staff changes these)
        protected virtual int IcicleCount => Mathf.Max(1, Settings.FrostIcicleCount.Value);
        internal virtual int IcicleDamage => Settings.FrostIcicleDamage.Value;
        protected virtual int IciclesAtOnce => 1;                     // icicles that form (and fly) together
        protected virtual float Interval => Mathf.Max(0.05f, Settings.FrostIcicleInterval.Value);
        protected virtual float LaunchDelay => Mathf.Max(0f, Settings.FrostLaunchDelay.Value);

        /// <summary>When icicle i forms, counted from the start of the volley.</summary>
        private float AppearTime(int i) => (i / Mathf.Max(1, IciclesAtOnce)) * Interval;

        /// <summary>In the inventory (or shrinking into it): icicles that haven't flown yet melt away.</summary>
        private bool Stowed => (equip != null && equip.IsEquipped()) || transform.lossyScale.x < 0.5f;

        // ---------------- trigger (host) ----------------

        /// <summary>Host: a volley number and a spot on the ring for each icicle (shared so everyone sees the same).</summary>
        protected override float[] HostChargeData()
        {
            int count = IcicleCount, together = Mathf.Clamp(IciclesAtOnce, 1, count);
            int groups = (count + together - 1) / together;
            float min = Settings.FrostSpawnDistanceMin.Value, max = Mathf.Max(min, Settings.FrostSpawnDistanceMax.Value);
            var data = new float[1 + count * 2];
            data[0] = ++hostVolleyCounter;
            float start = Random.Range(0f, Mathf.PI * 2f), step = Mathf.PI * 2f / count;
            for (int i = 0; i < count; i++)
            {
                // round the ring in order (icicles forming together spread evenly round it), a little uneven so it
                // looks conjured rather than machined
                int group = i / together, k = i % together;
                float angle = start + k * Mathf.PI * 2f / together + group * Mathf.PI * 2f / together / groups;
                data[1 + 2 * i] = angle + Random.Range(-0.25f, 0.25f) * step;
                data[2 + 2 * i] = Random.Range(min, max);
            }
            return data;
        }

        protected override void ChargeBegin(float[] data) { pending = data; }
        protected override void ChargeTick(float progress, float elapsed) { }
        protected override void ChargeEnd() { }

        /// <summary>Every client: the trigger's shot went off (battery used); start forming the volley.</summary>
        protected override void Launch()
        {
            float[] d = pending;
            pending = null;
            ClearVolley();
            if (d == null || d.Length < 3) return;
            int count = (d.Length - 1) / 2;
            volley = Mathf.RoundToInt(d[0]);
            offsets = new Vector2[count];
            state = new Ice[count];
            forming = new Transform[count];
            for (int i = 0; i < count; i++) offsets[i] = new Vector2(d[1 + 2 * i], d[2 + 2 * i]);
            volleyAge = 0f;
            volleyActive = true;
        }

        private Vector3 SpotFor(int i)
        {
            Vector2 o = offsets[i];
            Transform tip = Tip;
            return tip.position + tip.forward * Settings.FrostSpawnForwardOffset.Value
                 + (tip.right * Mathf.Cos(o.x) + tip.up * Mathf.Sin(o.x)) * o.y;
        }

        // ---------------- aim ----------------

        private float[] LocalAim()
        {
            Transform view = HeldByLocal ? HolderView : null;
            Vector3 o = view != null ? view.position : Tip.position;
            Vector3 d = view != null ? view.forward : Tip.forward;
            return new[] { o.x, o.y, o.z, d.x, d.y, d.z };
        }

        /// <summary>The caster's aim: their own camera on their game, their latest report on the host.</summary>
        private void AimRay(out Vector3 origin, out Vector3 dir)
        {
            bool reported = !HeldByLocal && reportedAim != null && Time.time - reportedAt <= ReportMaxAge;
            float[] a = reported ? reportedAim : LocalAim();
            origin = new Vector3(a[0], a[1], a[2]);
            dir = new Vector3(a[3], a[4], a[5]);
            if (dir.sqrMagnitude < 1e-6f) dir = Tip.forward;
            dir.Normalize();
        }

        /// <summary>What the crosshair is on: the first thing along the aim (not the wand or the caster), or far ahead.</summary>
        private Vector3 CrosshairPoint(out Vector3 aimDir)
        {
            AimRay(out Vector3 origin, out aimDir);
            float best = AimRange;
            PlayerAvatar caster = Holder;
            foreach (RaycastHit h in Physics.RaycastAll(origin, aimDir, AimRange, ProjectileHits.Mask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.distance >= best) continue;
                if (h.collider.GetComponentInParent<PhysGrabObject>() == PhysGrabObject) continue;
                if (caster != null && h.collider.GetComponentInParent<PlayerAvatar>() == caster) continue;
                best = h.distance;
            }
            return origin + aimDir * Mathf.Max(best, 1f);
        }

        /// <summary>From a spot towards the crosshair, never more than MaxConvergeAngle off the aim line.</summary>
        private Quaternion TowardsCrosshair(Vector3 spot, Vector3 target, Vector3 aimDir)
        {
            Vector3 dir = target - spot;
            if (dir.sqrMagnitude < 1e-4f) dir = aimDir;
            dir.Normalize();
            if (Vector3.Angle(aimDir, dir) > MaxConvergeAngle)
                dir = Vector3.RotateTowards(aimDir, dir, MaxConvergeAngle * Mathf.Deg2Rad, 0f);
            return Quaternion.LookRotation(dir, Tip.up);
        }

        protected override void Update()
        {
            base.Update();
            // the caster's game reports where it aims, for the host to angle the icicles
            if (!HeldByLocal || !SemiFunc.IsMultiplayer() || IsHost || Time.time < nextReport) return;
            nextReport = Time.time + ReportInterval;
            try { photonView.RPC(nameof(HWPFrostAimRPC), RpcTarget.MasterClient, LocalAim()); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Frost Wand aim report failed: {e}"); nextReport = Time.time + 2f; }
        }

        [PunRPC]
        public void HWPFrostAimRPC(float[] aim, PhotonMessageInfo info)
        {
            PlayerAvatar sender = SemiFunc.PlayerAvatarGetFromPhotonPlayer(info.Sender);
            if (sender == null || sender != Holder || aim == null || aim.Length < 6) return;
            reportedAim = aim;
            reportedAt = Time.time;
        }

        // ---------------- the volley ----------------

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!volleyActive) return;
            volleyAge += Time.deltaTime;
            bool stowed = Stowed;
            Vector3 target = CrosshairPoint(out Vector3 aimDir);
            bool busy = false;

            for (int i = 0; i < state.Length; i++)
            {
                float appear = AppearTime(i);
                if (state[i] == Ice.Pending)
                {
                    if (volleyAge < appear) { busy = true; continue; }
                    if (stowed) { state[i] = Ice.Gone; continue; }
                    GameObject ice = Icicle.CreateVisual("HWP Icicle (forming)");
                    ice.transform.localScale = Vector3.zero;
                    forming[i] = ice.transform;
                    state[i] = Ice.Forming;
                    if (conjureSounds != null && conjureSounds.Length > 0)
                        PlayAt(conjureSounds[Random.Range(0, conjureSounds.Length)], null, SpotFor(i));
                }
                if (state[i] != Ice.Forming) continue;
                busy = true;

                if (stowed)
                {
                    Melt(i);
                    if (IsHost) SendLaunch(i, false, Vector3.zero, Quaternion.identity);
                    continue;
                }

                // grow in quickly, held in the ring in front of the tip, pointing at the crosshair
                float k = Mathf.Clamp01((volleyAge - appear) / GrowSeconds);
                float s = k * k * (3f - 2f * k);
                Vector3 spot = SpotFor(i);
                if (forming[i] != null)
                {
                    forming[i].localScale = new Vector3(s, s, Mathf.Lerp(0.3f, 1f, s) * s);
                    forming[i].SetPositionAndRotation(spot, TowardsCrosshair(spot, target, aimDir));
                }

                if (IsHost && volleyAge >= appear + LaunchDelay)
                    SendLaunch(i, true, spot, TowardsCrosshair(spot, target, aimDir));
                else if (!IsHost && volleyAge >= appear + LaunchDelay + 3f)
                    Melt(i);   // never heard from the host
            }
            if (!busy) volleyActive = false;
        }

        /// <summary>Host: fly icicle i (or melt it if <paramref name="fly"/> is false) on everyone's game.</summary>
        private void SendLaunch(int i, bool fly, Vector3 start, Quaternion rotation)
        {
            int id = fly ? NextId() : 0;
            if (SemiFunc.IsMultiplayer())
                photonView.RPC(nameof(HWPFrostLaunchRPC), RpcTarget.All, volley, i, id, fly, start, rotation);
            else
                DoLaunch(volley, i, id, fly, start, rotation);
        }

        [PunRPC]
        public void HWPFrostLaunchRPC(int volleyNumber, int index, int id, bool fly, Vector3 start, Quaternion rotation, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            DoLaunch(volleyNumber, index, id, fly, start, rotation);
        }

        private void DoLaunch(int volleyNumber, int index, int id, bool fly, Vector3 start, Quaternion rotation)
        {
            bool current = volleyNumber == volley && state != null && index >= 0 && index < state.Length;
            if (current)
            {
                if (state[index] == Ice.Flown || state[index] == Ice.Gone) return;
                Melt(index);
                state[index] = Ice.Flown;
            }
            if (!fly) return;
            Icicle ice = Icicle.Spawn(this, id, start, rotation, IsHost);
            if (ice != null) Track(id, ice.gameObject);
            PlayAt(flySound, ice != null ? ice.transform : null, start);
        }

        private void Melt(int i)
        {
            if (forming[i] != null) Destroy(forming[i].gameObject);
            forming[i] = null;
            if (state[i] != Ice.Flown) state[i] = Ice.Gone;
        }

        private void ClearVolley()
        {
            if (forming != null) foreach (Transform t in forming) if (t != null) Destroy(t.gameObject);
            forming = null; state = null; offsets = null;
            volleyActive = false;
        }

        protected override void ImpactEffect(Vector3 position)
        {
            IceShatter.Spawn(position);
            PlayAt(crashSound, null, position);
            // puts out a burn there; encases the enemy it hit in ice for the freeze (the host's icicle froze it),
            // unless it only just caught fire (Elements: GraceSeconds)
            Enemy hit = Elements.IcicleImpact(position);
            if (hit != null) IceEncasement.Encase(hit, crashSound, Settings.FrostStunSeconds.Value);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ClearVolley();
        }
    }
}
