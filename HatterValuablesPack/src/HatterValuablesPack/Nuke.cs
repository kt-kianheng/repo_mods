using System.Collections;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// The A-Bomb's behaviour (on the prefab root, next to the piano's PhotonView).
    ///  - Host: counts heavy hits (NukePatches feeds them in). HitsToArm hits of at least HeavyHitForce, or one of at
    ///    least InstantArmForce, arm it. Hits closer together than HitGap count once (one landing bounces several times).
    ///  - Armed (every game, from NukeArmRPC): smoke leaks out and thickens for FuseSeconds; nothing stops it.
    ///  - Host at the end of the fuse: NukeBlast works out who and what is in range and kills/destroys it, sends
    ///    NukeDetonateRPC (every game: the explosion and the flash, NukeFx), then removes the bomb.
    /// </summary>
    public class Nuke : MonoBehaviour
    {
        public Transform modelRoot;   // serialized, set by NukeBuilder (scaled model space)

        private const float HitGap = 0.5f;
        private const float SpawnGrace = 4f;   // impacts while the level settles don't count

        private PhotonView view;
        private PhysGrabObject grab;
        private float spawnedAt;
        private float lastCountedHit = -10f;
        private int hits;
        private bool armed;
        private bool detonated;
        private NukeSmoke smoke;
        private AudioSource alarm;

        private static bool Debug => Settings.NukeDebugLog.Value;

        private void Awake()
        {
            view = GetComponent<PhotonView>();
            grab = GetComponent<PhysGrabObject>();
            spawnedAt = Time.time;
        }

        /// <summary>The middle of the bomb's body, in world space.</summary>
        internal Vector3 Centre => modelRoot != null ? modelRoot.TransformPoint(NukeBuilder.BlastCentre) : transform.position;

        // ---------------------------------------------------------------- arming (host)

        internal void HostHeavyImpact(float force)
        {
            if (armed || detonated || !SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (Time.time - spawnedAt < SpawnGrace) { if (Debug) Plugin.Log.LogInfo($"A-Bomb: heavy hit {force:0} ignored (just spawned)."); return; }
            if (force >= Settings.NukeInstantArmForce.Value)
            {
                Plugin.Log.LogInfo($"A-Bomb: hit {force:0} >= {Settings.NukeInstantArmForce.Value:0}: armed at once.");
                Arm();
                return;
            }
            if (force < Settings.NukeHeavyHitForce.Value) { if (Debug) Plugin.Log.LogInfo($"A-Bomb: hit {force:0}, below HeavyHitForce."); return; }
            if (Time.time - lastCountedHit < HitGap) { if (Debug) Plugin.Log.LogInfo($"A-Bomb: hit {force:0} (same landing)."); return; }
            lastCountedHit = Time.time;
            hits++;
            Plugin.Log.LogInfo($"A-Bomb: heavy hit {force:0}, {hits}/{Settings.NukeHitsToArm.Value}.");
            if (hits >= Settings.NukeHitsToArm.Value) Arm();
        }

        private void Arm()
        {
            if (armed) return;
            if (SemiFunc.IsMultiplayer()) view.RPC(nameof(NukeArmRPC), RpcTarget.All);
            else NukeArmRPC();
            StartCoroutine(Fuse());
        }

        [PunRPC]
        public void NukeArmRPC()
        {
            if (armed) return;
            armed = true;
            if (Debug) Plugin.Log.LogInfo($"A-Bomb: armed, {Settings.NukeFuseSeconds.Value:0.#} s to go.");
            if (modelRoot != null) smoke = NukeSmoke.Attach(modelRoot, Settings.NukeFuseSeconds.Value);
            alarm = NukeSounds.PlayAlarm(modelRoot != null ? modelRoot : transform, Settings.NukeFuseSeconds.Value);
        }

        private IEnumerator Fuse()
        {
            yield return new WaitForSeconds(Settings.NukeFuseSeconds.Value);
            if (this == null || detonated) yield break;
            Detonate();
        }

        // ---------------------------------------------------------------- the blast (host)

        private void Detonate()
        {
            detonated = true;
            Vector3 centre = Centre;
            Plugin.Log.LogInfo($"A-Bomb: detonating at {centre}.");
            try { NukeBlast.HostApply(this, centre); }
            catch (System.Exception e) { Plugin.Log.LogError($"A-Bomb: blast failed: {e}"); }

            if (SemiFunc.IsMultiplayer()) view.RPC(nameof(NukeDetonateRPC), RpcTarget.All, centre);
            else NukeDetonateRPC(centre);

            // the bomb itself goes (no value is counted: it's gone)
            try
            {
                var detector = GetComponentInChildren<PhysGrabObjectImpactDetector>();
                if (detector != null) detector.DestroyObject(false, true);
                else if (SemiFunc.IsMultiplayer()) PhotonNetwork.Destroy(gameObject);
                else Destroy(gameObject);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"A-Bomb: removing the bomb failed: {e}"); }
        }

        [PunRPC]
        public void NukeDetonateRPC(Vector3 centre)
        {
            detonated = true;
            // the smoke stays behind; the bomb disappears at once (its removal follows over the network)
            if (smoke != null) smoke.Detach();
            smoke = null;
            StopAlarm();
            if (modelRoot != null) modelRoot.gameObject.SetActive(false);
            NukeFx.Explode(centre);
        }

        private void OnDestroy()
        {
            try { if (smoke != null) smoke.Detach(); }
            catch (System.Exception) { }
            StopAlarm();
        }

        private void StopAlarm()
        {
            try { if (alarm != null) alarm.Stop(); }
            catch (System.Exception) { }
            alarm = null;
        }
    }
}
