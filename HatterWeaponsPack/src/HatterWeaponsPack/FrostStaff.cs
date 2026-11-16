using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Frost Staff, the Frost Wand's upgrade. Its normal cast is the Frost Wand's volley, bigger and faster: IciclesPerCast
    /// icicles forming IciclesAtOnce at a time, IcicleInterval apart, each set flying LaunchDelay after it forms (see
    /// FrostWand). Pointing the staff up at the sky readies the ice wall (see StaffArming); aiming at the floor then shows
    /// its footprint, and casting raises a wall of ice there, across the holder's view, that blocks everything (see IceWall).
    /// </summary>
    public class FrostStaff : FrostWand
    {
        private StaffArming arming;
        private float[] wallPending;      // [-1, x, y, z, yaw] while a wall cast is charging
        private StaffAura aura;
        private Sound armSound;
        // "ice-cast" (the user's file) swells to full this long after it starts: it starts this long before the charge completes
        private const float ArmFlashDelay = 0.4f;

        protected override Vector3 TipLocalPosition => WandBuilder.StaffShape.Tip;
        protected override string ChargeLines => Settings.FrostStaffVoiceLines.Value;
        protected override string LinesFor(float[] data) => IsWall(data) ? Settings.FrostStaffReleaseLine.Value : ChargeLines;   // shouted as the charged spell is cast
        protected override int IcicleCount => Mathf.Max(1, Settings.FrostStaffIcicles.Value);
        internal override int IcicleDamage => Settings.FrostStaffIcicleDamage.Value;
        protected override int IciclesAtOnce => Mathf.Max(1, Settings.FrostStaffIciclesAtOnce.Value);
        protected override float Interval => Mathf.Max(0.05f, Settings.FrostStaffInterval.Value);
        protected override float LaunchDelay => Mathf.Max(0f, Settings.FrostStaffLaunchDelay.Value);
        protected override float ReadyGlow => Armed ? 2.4f : 1f;

        private bool Armed => arming != null && arming.Armed(HeldByLocal, Holder != null);
        private static bool IsWall(float[] d) => d != null && d.Length >= 5 && d[0] < -0.5f;

        protected override void Awake()
        {
            base.Awake();
            arming = new StaffArming(() => WallMarker.Create());
            armSound = MakeSound("staff_arm_ice.pcm", 0.8f);
        }

        protected override void Update()
        {
            base.Update();
            bool report = arming.Update(HeldByLocal, HolderPitch, HolderView, PhysGrabObject, Holder,
                                        Settings.FrostStaffLookUpAngle.Value, Settings.FrostStaffWallRange.Value, Settings.FrostStaffChargeUpSeconds.Value);
            if (arming.ChargeStarted) Say(Settings.FrostStaffChargeUpLine.Value);   // the incantation as the charge begins
            if (!report) return;
            try { photonView.RPC(nameof(HWPFrostStaffStateRPC), RpcTarget.Others, arming.State()); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Frost Staff state report failed: {e}"); }
        }

        [PunRPC]
        public void HWPFrostStaffStateRPC(float[] state, PhotonMessageInfo info)
        {
            PlayerAvatar sender = SemiFunc.PlayerAvatarGetFromPhotonPlayer(info.Sender);
            if (sender == null || sender != Holder) return;
            arming.Receive(state);
        }

        /// <summary>Host: [-1, x, y, z, yaw] for an ice wall, the Frost Wand's volley data otherwise, or null (ready, no floor).</summary>
        protected override float[] HostChargeData()
        {
            if (Holder == null) return null;
            float[] s = arming.HostState(HeldByLocal);
            if (s != null && s[0] > 0.5f)
            {
                if (s[1] < 0.5f) { Plugin.Log.LogInfo("Frost Staff: ice wall ready, but not aimed at the floor."); return null; }
                Plugin.Log.LogInfo($"Frost Staff: ice wall at ({s[2]:0.0}, {s[3]:0.0}, {s[4]:0.0}), facing {s[5]:0}°.");
                return new[] { -1f, s[2], s[3], s[4], s[5] };
            }
            return base.HostChargeData();
        }

        protected override void ChargeBegin(float[] data)
        {
            // the battery comes off on every client in the game's ShootRPC, so set the cost here (this arrives first)
            if (gun != null) gun.batteryDrainFullBars = IsWall(data) ? Mathf.Max(1, Settings.FrostStaffWallBatteryBars.Value) : 1;
            if (IsWall(data)) { wallPending = data; return; }
            wallPending = null;
            base.ChargeBegin(data);
        }

        protected override void Launch()
        {
            float[] d = wallPending;
            wallPending = null;
            if (!IsWall(d)) { base.Launch(); return; }
            Sound rise = conjureSounds != null && conjureSounds.Length > 0 ? conjureSounds[Random.Range(0, conjureSounds.Length)] : null;
            IceWall.Spawn(new Vector3(d[1], d[2], d[3]), d[4], rise, crashSound);
            if (HeldByLocal) arming.Disarm();
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            float charge = arming != null ? arming.Progress(HeldByLocal, Holder != null) : 0f;
            if ((charge > 0f || Armed) && aura == null)
                aura = StaffAura.Create(Tip, StaffAura.Frost, armSound, ArmFlashDelay, Settings.FrostStaffChargeUpSeconds.Value);
            if (aura != null) aura.SetState(charge, Armed);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            arming?.Destroy();
            if (aura != null) Destroy(aura.gameObject);
        }
    }
}
