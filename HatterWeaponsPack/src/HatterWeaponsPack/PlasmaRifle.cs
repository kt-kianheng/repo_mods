using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Plasma bolts on top of the game's gun logic. Lives on the rifle root (next to its PhotonView).
    /// Every client spawns the bolt visually when the shot is broadcast; the host's bolt decides hits.
    /// </summary>
    public class PlasmaRifle : MonoBehaviour
    {
        private ItemGun gun;
        private PhysGrabObject physGrabObject;
        private PhotonView photonView;
        private int shotCounter;
        private readonly Dictionary<int, PlasmaBolt> inFlight = new Dictionary<int, PlasmaBolt>();

        internal PhysGrabObject PhysGrabObject => physGrabObject;

        private void Awake()
        {
            gun = GetComponent<ItemGun>();
            physGrabObject = GetComponent<PhysGrabObject>();
            photonView = GetComponent<PhotonView>();
        }

        /// <summary>Called on every client when the gun's shot is broadcast (ItemGun.ShootRPC).</summary>
        internal void OnShot()
        {
            int id = ++shotCounter;
            Transform muzzle = gun != null && gun.gunMuzzle != null ? gun.gunMuzzle : transform;
            Vector3 start = transform.TransformPoint(PlasmaRifleBuilder.MuzzlePosition);
            PlasmaBolt bolt = null;
            try { bolt = PlasmaBolt.Spawn(this, id, start, muzzle.rotation, SemiFunc.IsMasterClientOrSingleplayer()); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Plasma bolt spawn failed: {e}"); }
            if (bolt != null) inFlight[id] = bolt;
        }

        /// <summary>Host only: the authoritative bolt hit something (or faded out).</summary>
        internal void HostImpact(int id, Vector3 position)
        {
            if (SemiFunc.IsMultiplayer())
                photonView.RPC(nameof(HWPPlasmaImpactRPC), RpcTarget.All, id, position);
            else
                Impact(id, position);
        }

        [PunRPC]
        private void HWPPlasmaImpactRPC(int id, Vector3 position, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Impact(id, position);
        }

        private void Impact(int id, Vector3 position)
        {
            if (inFlight.TryGetValue(id, out PlasmaBolt bolt))
            {
                inFlight.Remove(id);
                if (bolt != null) Destroy(bolt.gameObject);
            }
            PlasmaBolt.SpawnImpactFlash(position);
        }

        internal void Forget(int id) => inFlight.Remove(id);

        private void OnDestroy()
        {
            foreach (var b in inFlight.Values) if (b != null) Destroy(b.gameObject);
            inFlight.Clear();
        }
    }
}
