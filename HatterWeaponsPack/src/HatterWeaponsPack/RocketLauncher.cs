using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Rocket behaviour on top of the game's gun logic. Lives on the launcher root (next to its PhotonView).
    ///  - ItemGun still handles trigger, battery (one bar per shot), sounds, muzzle flash and the reload timer.
    ///  - Every client spawns the rocket visually when the shot is broadcast; the host's rocket decides hits.
    ///  - The rocket head on the launcher is only shown while a rocket is loaded.
    /// </summary>
    public class RocketLauncher : MonoBehaviour
    {
        // ---- Tuning ----
        // (HatterWeaponsPack.cfg, see Settings)
        internal static float RocketSpeed => Settings.RocketSpeed.Value;         // m/s
        internal static float RocketLifetime => Settings.RocketLifetime.Value;   // explodes in the air after this
        internal static float RecoilForce => Settings.RocketRecoil.Value;        // velocity kick on the shooter

        // set by RocketLauncherBuilder (serialized, so every spawned launcher gets its own head reference)
        public GameObject rocketHead;

        private ItemGun gun;
        private PhysGrabObject physGrabObject;
        private PhotonView photonView;
        private int shotCounter;
        private readonly Dictionary<int, RocketProjectile> inFlight = new Dictionary<int, RocketProjectile>();

        private static readonly FieldInfo StateField = AccessTools.Field(typeof(ItemGun), "stateCurrent");
        private static readonly FieldInfo HeldByLocalField = AccessTools.Field(typeof(PhysGrabObject), "heldByLocalPlayer");

        private void Awake()
        {
            gun = GetComponent<ItemGun>();
            physGrabObject = GetComponent<PhysGrabObject>();
            photonView = GetComponent<PhotonView>();
        }

        private int GunState => gun != null && StateField != null ? System.Convert.ToInt32(StateField.GetValue(gun)) : 0;

        private void LateUpdate()
        {
            // ItemGun states: 0 Idle, 1 OutOfAmmo, 2 Buildup, 3 Shooting, 4 Reloading.
            // The head is out only when a rocket is ready: it disappears when fired and pops back after the reload.
            int state = GunState;
            bool loaded = state == 0 || state == 2;
            if (rocketHead != null && rocketHead.activeSelf != loaded) rocketHead.SetActive(loaded);
        }

        /// <summary>Called on every client when the gun's shot is broadcast (ItemGun.ShootRPC).</summary>
        internal void OnShot()
        {
            int id = ++shotCounter;

            // Rocket leaves from where the loaded head sits, along the barrel.
            Transform muzzle = gun != null && gun.gunMuzzle != null ? gun.gunMuzzle : transform;
            Vector3 start = transform.TransformPoint(RocketLauncherBuilder.LoadedRocketCenter);
            Quaternion rotation = muzzle.rotation;
            bool authority = SemiFunc.IsMasterClientOrSingleplayer();
            RocketProjectile rocket = null;
            try { rocket = RocketProjectile.Spawn(this, id, start, rotation, authority); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Rocket spawn failed: {e}"); }
            if (rocket != null) inFlight[id] = rocket;

            // Recoil on the shooter: an impulse added to their current velocity, so it's weaker while moving
            // forward and stronger while moving backward.
            if (HeldByLocalField != null && (bool)HeldByLocalField.GetValue(physGrabObject) && PlayerController.instance != null)
            {
                Vector3 aim = Camera.main != null ? Camera.main.transform.forward : muzzle.forward;
                PlayerController.instance.ForceImpulse(-aim.normalized * RecoilForce);
            }
        }

        internal PhysGrabObject PhysGrabObject => physGrabObject;

        /// <summary>Host only: the authoritative rocket hit something (or timed out).</summary>
        internal void HostExplode(int id, Vector3 position)
        {
            if (SemiFunc.IsMultiplayer())
                photonView.RPC(nameof(HWPRocketExplodeRPC), RpcTarget.All, id, position);
            else
                Explode(id, position);
        }

        [PunRPC]
        private void HWPRocketExplodeRPC(int id, Vector3 position, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Explode(id, position);
        }

        private void Explode(int id, Vector3 position)
        {
            Plugin.Log.LogInfo($"Rocket {id} exploded at {position}.");
            if (inFlight.TryGetValue(id, out RocketProjectile rocket))
            {
                inFlight.Remove(id);
                if (rocket != null) Destroy(rocket.gameObject);
            }
            RocketExplosion.Spawn(position);
        }

        internal void Forget(int id) => inFlight.Remove(id);

        private void OnDestroy()
        {
            foreach (var r in inFlight.Values) if (r != null) Destroy(r.gameObject);
            inFlight.Clear();
        }
    }
}
