using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// The Nunchuck's chain and free handle. The item itself (the bat it was built from) is the held handle. The free
    /// handle is the only other physics body: gravity pulls it as usual, and an invisible rope joint keeps its eye ring
    /// within 40 cm (the chain's length) of the held handle's eye ring. Inside that it flops about freely; at full
    /// length it's held taut, so swinging the held end whips it round. The ten chain links are only drawn: every frame
    /// they're laid between the two rings, sagging when there's slack and pulled straight when it's taut. They weigh
    /// nothing and touch nothing.
    ///
    /// Every player's game swings its own free handle from the (networked) held handle, so it looks right everywhere.
    /// Only the host's decides damage: when the free handle strikes an enemy, the damage comes from how fast it was
    /// moving (see Settings). Fast hits also push and can stun. Other players are hurt by it too (friendly fire),
    /// never the one holding it, and anything else it strikes hard hears about it through SendMessageUpwards
    /// ("TargetDummyHit"), so training dummies can show the damage.
    ///
    /// Either end can be picked up: pressing grab while aiming at the free handle (when nobody holds the nunchuck)
    /// asks the host to swap the ends. The item jumps to where the free handle was and the free handle takes the
    /// item's old place, so the game's own grab then picks up the end you aimed at.
    /// </summary>
    internal class Nunchuck : MonoBehaviour
    {
        internal static Material Template;   // the level's shader, from the bat (shared, not serialized)

        // set by NunchuckBuilder on the prefab (serialized, so they survive the game cloning it)
        public Vector3 chainAnchor;           // the eye ring, in item space
        public Vector3 handleAxis;            // grip end -> chain end, in item space
        public Vector3 handleBottom;          // the held handle's grip end, in item space
        public Quaternion handleRotation;     // the held handle's model rotation (its +Y = handleAxis), in item space

        private const int LinkCount = 10;
        private const float LinkPitch = 0.04f;   // 10 x 4 cm = the 40 cm chain
        private const int CurveSamples = 24;
        private const float HandleRadius = 0.0255f;

        private static readonly FieldInfo EnemyHealthField = AccessTools.Field(typeof(Enemy), "Health");
        private static readonly FieldInfo EnemyStunField = AccessTools.Field(typeof(Enemy), "StateStunned");
        private static readonly FieldInfo EnemyBodyField = AccessTools.Field(typeof(EnemyRigidbody), "rb");   // internal
        private static readonly FieldInfo DisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo DeadField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");

        private PhysGrabObject physGrabObject;
        private PhotonView photonView;
        private float nextSwapRequest;
        private Rigidbody heldBody;
        private ItemEquippable equippable;
        private ItemMelee melee;
        private Collider[] ownColliders;

        private Transform chainRoot;
        private Rigidbody freeHandle;
        private CapsuleCollider freeCollider;
        private readonly Transform[] linkVisuals = new Transform[LinkCount];
        private readonly Vector3[] curve = new Vector3[CurveSamples + 1];
        private readonly float[] curveLength = new float[CurveSamples + 1];
        private bool shown;

        private readonly Dictionary<Object, float> nextHit = new Dictionary<Object, float>();
        private float nextSound, nextIgnoreRefresh;
        private readonly List<Collider> ignoreScratch = new List<Collider>();

        private float ChainLength => LinkCount * LinkPitch;

        private void Start()
        {
            physGrabObject = GetComponent<PhysGrabObject>();
            heldBody = physGrabObject != null && physGrabObject.rb != null ? physGrabObject.rb : GetComponent<Rigidbody>();
            equippable = GetComponent<ItemEquippable>();
            melee = GetComponent<ItemMelee>();
            photonView = GetComponent<PhotonView>();
            ownColliders = GetComponentsInChildren<Collider>(true);
            if (heldBody == null) { Plugin.Log.LogError("Nunchuck has no rigidbody; no chain."); enabled = false; return; }
            try { BuildChain(); }
            catch (System.Exception e) { Plugin.Log.LogError($"Building the nunchuck's chain failed: {e}"); enabled = false; }
        }

        // ---------------------------------------------------------------- building

        private void BuildChain()
        {
            chainRoot = new GameObject("Nunchuck Chain").transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(chainRoot.gameObject, gameObject.scene);
            var linkModel = MeshLibrary.Get(NunchuckBuilder.ModelFile, "NC_Link");
            var handleModel = MeshLibrary.Get(NunchuckBuilder.ModelFile, "NC_Handle");

            // the free handle: a normal physics body under gravity, ring end towards the chain
            var free = new GameObject("Free Handle");
            free.layer = 0;
            free.transform.SetParent(chainRoot, false);
            freeHandle = free.AddComponent<Rigidbody>();
            freeHandle.mass = Mathf.Max(0.1f, heldBody.mass) * Settings.NunchuckFreeHandleMass.Value;
            freeHandle.linearDamping = 0.05f;
            freeHandle.angularDamping = 0.1f;
            freeHandle.interpolation = RigidbodyInterpolation.Interpolate;
            freeHandle.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // fast swings mustn't pass through things
            freeHandle.solverIterations = 12;
            freeHandle.maxAngularVelocity = 50f;
            freeCollider = free.AddComponent<CapsuleCollider>();
            freeCollider.direction = 1;
            freeCollider.radius = HandleRadius;
            freeCollider.height = NunchuckBuilder.HandleLength;
            freeCollider.center = new Vector3(0f, NunchuckBuilder.HandleLength / 2f, 0f);
            if (handleModel != null) MeshLibrary.Attach(handleModel, free.transform, "Free Handle Mesh", Template);
            free.AddComponent<NunchuckFreeEnd>().owner = this;

            // the invisible chain: the two eye rings stay within the chain's length of each other, nothing more
            var rope = free.AddComponent<ConfigurableJoint>();
            rope.connectedBody = heldBody;
            rope.autoConfigureConnectedAnchor = false;
            rope.anchor = new Vector3(0f, NunchuckBuilder.RingTop, 0f);
            rope.connectedAnchor = chainAnchor;
            rope.xMotion = rope.yMotion = rope.zMotion = ConfigurableJointMotion.Limited;
            rope.linearLimit = new SoftJointLimit { limit = ChainLength, bounciness = 0f, contactDistance = 0.01f };
            rope.angularXMotion = rope.angularYMotion = rope.angularZMotion = ConfigurableJointMotion.Free;
            rope.enableCollision = false;
            rope.enablePreprocessing = false;
            // the held handle "feels" heavier to the rope than it is, so the swinging end tugs on your grip less
            rope.connectedMassScale = Settings.NunchuckChainPull.Value;

            // the chain links: drawn only
            for (int i = 0; i < LinkCount; i++)
            {
                var t = new GameObject("Chain Link " + (i + 1)).transform;
                t.SetParent(chainRoot, false);
                if (linkModel != null)
                {
                    GameObject visual = MeshLibrary.Attach(linkModel, t, "Link Mesh", Template);
                    visual.transform.localRotation = Quaternion.Euler(0f, i % 2 == 0 ? 0f : 90f, 0f);   // alternate links turn 90°
                }
                linkVisuals[i] = t;
            }

            ResetChain();
            shown = true;
            IgnoreOwnAndPlayers();
        }

        /// <summary>Lays the free handle out straight from the eye ring, along the held handle, with nothing moving.</summary>
        private void ResetChain()
        {
            Vector3 dir = transform.TransformDirection(handleAxis).normalized;
            Vector3 ring = transform.TransformPoint(chainAnchor);
            Quaternion pointingBack = Quaternion.FromToRotation(Vector3.up, -dir);   // its ring end faces the chain
            Place(freeHandle, ring + dir * (ChainLength + NunchuckBuilder.RingTop), pointingBack);
            DrawChain();
        }

        private static void Place(Rigidbody rb, Vector3 position, Quaternion rotation)
        {
            rb.position = position; rb.rotation = rotation;
            rb.transform.SetPositionAndRotation(position, rotation);
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Lays the ten links between the two eye rings: a curve that sags under gravity by as much as the slack allows
        /// (a parabola whose length is the chain's), straight when the chain is pulled taut.
        /// </summary>
        private void DrawChain()
        {
            if (freeHandle == null) return;
            Vector3 a = transform.TransformPoint(chainAnchor);
            Vector3 b = freeHandle.transform.TransformPoint(new Vector3(0f, NunchuckBuilder.RingTop, 0f));
            Vector3 span = b - a;
            float dist = span.magnitude;
            float sag = dist < ChainLength ? Mathf.Sqrt(3f * Mathf.Max(dist, 0.02f) * (ChainLength - dist) / 8f) : 0f;
            sag = Mathf.Min(sag, ChainLength * 0.5f);
            Vector3 sagDir = Vector3.ProjectOnPlane(Vector3.down, dist > 1e-4f ? span / dist : Vector3.up);
            if (sagDir.sqrMagnitude < 1e-4f) sagDir = Vector3.ProjectOnPlane(transform.forward, dist > 1e-4f ? span / dist : Vector3.up);
            sagDir.Normalize();
            Vector3 control = (a + b) * 0.5f + sagDir * (2f * sag);   // a quadratic curve dips half as far as its control point

            curve[0] = a; curveLength[0] = 0f;
            for (int k = 1; k <= CurveSamples; k++)
            {
                float t = k / (float)CurveSamples, u = 1f - t;
                curve[k] = u * u * a + 2f * u * t * control + t * t * b;
                curveLength[k] = curveLength[k - 1] + Vector3.Distance(curve[k - 1], curve[k]);
            }
            float total = curveLength[CurveSamples];
            int seg = 1;
            for (int i = 0; i < LinkCount; i++)
            {
                float s = total * (i + 0.5f) / LinkCount;
                while (seg < CurveSamples && curveLength[seg] < s) seg++;
                float segLen = curveLength[seg] - curveLength[seg - 1];
                float f = segLen > 1e-6f ? (s - curveLength[seg - 1]) / segLen : 0f;
                Vector3 p = Vector3.Lerp(curve[seg - 1], curve[seg], f);
                Vector3 tangent = curve[seg] - curve[seg - 1];
                if (tangent.sqrMagnitude < 1e-8f) tangent = span.sqrMagnitude > 1e-8f ? span : Vector3.down;
                tangent.Normalize();
                Vector3 facing = Vector3.ProjectOnPlane(sagDir, tangent);
                if (facing.sqrMagnitude < 1e-6f) facing = Vector3.ProjectOnPlane(Vector3.forward, tangent);
                linkVisuals[i].SetPositionAndRotation(p, Quaternion.LookRotation(facing.normalized, -tangent));   // +Y back up the chain
            }
        }

        // ---------------------------------------------------------------- upkeep

        private void FixedUpdate()
        {
            if (chainRoot == null) return;

            // hidden while the nunchuck is in an inventory slot; laid out fresh when it comes back
            bool show = equippable == null || !equippable.IsEquipped();
            if (show != shown)
            {
                shown = show;
                chainRoot.gameObject.SetActive(show);
                if (show) { ResetChain(); IgnoreOwnAndPlayers(); }
            }
            if (!show) return;

            // teleported (spawned, dropped back in, pulled through a wall)? lay it out again
            Vector3 anchor = transform.TransformPoint(chainAnchor);
            float reach = ChainLength + NunchuckBuilder.HandleLength + 0.6f;
            if ((freeHandle.position - anchor).sqrMagnitude > reach * reach) ResetChain();

            if (Time.time >= nextIgnoreRefresh) IgnoreOwnAndPlayers();
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                CheckReach();
                if (Settings.NunchuckFriendlyFire.Value) CheckPlayers();
            }
        }

        private void LateUpdate()
        {
            if (shown && chainRoot != null) DrawChain();   // after the handles have moved (and interpolated) this frame
        }

        /// <summary>
        /// The free handle passes through the nunchuck itself and through players, instead of bouncing off them (or
        /// pinning itself under someone's feet, which held the whole nunchuck down): player hits are checked
        /// separately (CheckPlayers), only on the host.
        ///
        /// "Players" here means every body the game gives them: the local player's controller, each avatar, their
        /// tumble bodies, and the host-only "phys pushers" (in multiplayer the host has an invisible physics body
        /// following each other player around; it isn't part of the avatar, which is why a friend could pin the free
        /// handle to the floor in multiplayer but nobody could in singleplayer).
        ///
        /// Unity forgets an ignored pair when either collider is switched off (the chain hides in an inventory slot,
        /// the game toggles the item's colliders, pushers switch on and off as players move), so this is re-applied
        /// every half second and whenever the chain comes back.
        /// </summary>
        private void IgnoreOwnAndPlayers()
        {
            nextIgnoreRefresh = Time.time + 0.5f;
            if (freeCollider == null) return;
            ignoreScratch.Clear();
            ignoreScratch.AddRange(GetComponentsInChildren<Collider>(true));
            if (PlayerController.instance != null) ignoreScratch.AddRange(PlayerController.instance.GetComponentsInChildren<Collider>(true));
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
                if (p != null) ignoreScratch.AddRange(p.GetComponentsInChildren<Collider>(true));
            foreach (PlayerPhysPusher pusher in Object.FindObjectsByType<PlayerPhysPusher>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (pusher != null) ignoreScratch.AddRange(pusher.GetComponentsInChildren<Collider>(true));
            foreach (PlayerTumble tumble in Object.FindObjectsByType<PlayerTumble>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (tumble != null) ignoreScratch.AddRange(tumble.GetComponentsInChildren<Collider>(true));
            foreach (Collider c in ignoreScratch)
                if (c != null && c != freeCollider) Physics.IgnoreCollision(freeCollider, c, true);
        }

        private void OnEnable()
        {
            if (chainRoot != null && shown) { chainRoot.gameObject.SetActive(true); ResetChain(); IgnoreOwnAndPlayers(); }
        }

        private void OnDisable()
        {
            if (chainRoot != null) chainRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (chainRoot != null) Destroy(chainRoot.gameObject);
        }

        // ---------------------------------------------------------------- picking up either end

        /// <summary>This player's game: grab pressed while aiming at the free handle, and nobody holds the nunchuck.</summary>
        private void Update()
        {
            if (!shown || freeCollider == null || physGrabObject == null || Time.time < nextSwapRequest) return;
            if (physGrabObject.playerGrabbing != null && physGrabObject.playerGrabbing.Count > 0) return;
            PhysGrabber grabber = PhysGrabber.instance;
            if (grabber == null || grabber.grabbed || !SemiFunc.InputDown(InputKey.Grab)) return;   // a fresh press only, never repeating
            Camera cam = Camera.main;
            if (cam == null) return;

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!freeCollider.Raycast(ray, out RaycastHit onHandle, grabber.grabRange)) return;
            // nothing closer in the way (a wall, or the held handle itself, which the game grabs normally)
            foreach (RaycastHit h in Physics.RaycastAll(ray, onHandle.distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == freeCollider || h.distance >= onHandle.distance - 0.01f) continue;
                if (h.collider.GetComponentInParent<PlayerAvatar>() != null || h.collider.GetComponentInParent<PlayerController>() != null) continue;
                return;
            }

            nextSwapRequest = Time.time + 0.75f;
            if (SemiFunc.IsMasterClientOrSingleplayer()) { if (HostSwap(LocalActor())) GrabSoon(); }
            else if (photonView != null) photonView.RPC(nameof(HMWPNunchuckSwapRequestRPC), RpcTarget.MasterClient);
        }

        private static int LocalActor() => PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;

        [PunRPC]
        public void HMWPNunchuckSwapRequestRPC(PhotonMessageInfo info)
        {
            if (SemiFunc.IsMasterClientOrSingleplayer()) HostSwap(info.Sender != null ? info.Sender.ActorNumber : -1);
        }

        /// <summary>The player who asked for the swap grabs the end they aimed at (now the item) straight away.</summary>
        private void GrabSoon() => StartCoroutine(GrabAfter(SemiFunc.IsMasterClientOrSingleplayer() ? 0f : 0.15f));

        private System.Collections.IEnumerator GrabAfter(float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);   // let the item's new position arrive first
            PhysGrabber grabber = PhysGrabber.instance;
            if (grabber != null && !grabber.grabbed && physGrabObject != null)
            {
                try { grabber.OverrideGrab(physGrabObject, 0.3f, false); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Nunchuck: grabbing after the swap failed: {e.Message}"); }
            }
        }

        /// <summary>Host: move the item to the free handle's place and tell everyone to reverse their chain.</summary>
        private bool HostSwap(int requester)
        {
            if (!shown || freeHandle == null || physGrabObject == null) return false;
            if (physGrabObject.playerGrabbing != null && physGrabObject.playerGrabbing.Count > 0) return false;   // picked up meanwhile
            Vector3 oldPos = transform.position;
            Quaternion oldRot = transform.rotation;
            // the item goes where its handle model lines up with the free handle
            Quaternion newRot = freeHandle.rotation * Quaternion.Inverse(handleRotation);
            Vector3 newPos = freeHandle.position - newRot * handleBottom;

            SwapChain(oldPos, oldRot);
            physGrabObject.Teleport(newPos, newRot);   // the game sends the new position to everyone
            if (SemiFunc.IsMultiplayer() && photonView != null)
                photonView.RPC(nameof(HMWPNunchuckSwappedRPC), RpcTarget.Others, oldPos, oldRot, requester);
            if (Settings.NunchuckDebugLog.Value) Plugin.Log.LogInfo($"Nunchuck: picked up by the other end (ends swapped, for player {requester}).");
            return true;
        }

        [PunRPC]
        public void HMWPNunchuckSwappedRPC(Vector3 oldPos, Quaternion oldRot, int requester, PhotonMessageInfo info)
        {
            if (info.Sender != null && !info.Sender.IsMasterClient) return;   // only the host swaps ends
            SwapChain(oldPos, oldRot);
            if (requester == LocalActor()) GrabSoon();
        }

        /// <summary>Every game: the free handle takes the held handle's old place (the drawn chain follows by itself).</summary>
        private void SwapChain(Vector3 oldItemPos, Quaternion oldItemRot)
        {
            if (freeHandle == null) return;
            Place(freeHandle, oldItemPos + oldItemRot * handleBottom, oldItemRot * handleRotation);
            nextHit.Clear();
        }

        // ---------------------------------------------------------------- hits

        /// <summary>
        /// Damage for a hit at this speed: a logarithmic curve through (SpeedForMinDamage, MinDamage) and
        /// (SpeedForMaxDamage, MaxDamage). It climbs quickly at first and ever more slowly, so the top end is hard to
        /// reach. Below SpeedForMinDamage it falls under MinDamage, i.e. the hit doesn't count.
        /// </summary>
        internal static int DamageFor(float speed)
        {
            float low = Settings.NunchuckSpeedLow.Value, high = Mathf.Max(low + 0.01f, Settings.NunchuckSpeedHigh.Value);
            float min = Settings.NunchuckMinDamage.Value, max = Settings.NunchuckMaxDamage.Value;
            if (speed <= 0.01f) return 0;
            float d = min + (max - min) * Mathf.Log(speed / low) / Mathf.Log(high / low);
            return Mathf.Clamp(Mathf.RoundToInt(d), 0, Settings.NunchuckMaxDamage.Value);
        }

        private const float SoundSpeed = 3f;   // quieter bumps than this make no sound

        /// <summary>The free handle touched something (every player's game; only the host's counts for damage).</summary>
        internal void OnFreeEndCollision(Collision collision)
        {
            if (collision == null || collision.collider == null || !shown) return;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : freeHandle.worldCenterOfMass;
            Vector3 velocity = freeHandle.GetPointVelocity(point);
            if (collision.rigidbody != null) velocity -= collision.rigidbody.GetPointVelocity(point);
            float speed = velocity.magnitude;
            if (speed < SoundSpeed) return;

            EnemyRigidbody erb = collision.collider.GetComponentInParent<EnemyRigidbody>();
            Enemy enemy = erb != null ? erb.enemy : collision.collider.GetComponentInParent<Enemy>();
            PlaySound(enemy != null, point, speed);
            if (SemiFunc.IsMasterClientOrSingleplayer()) RegisterHit(collision.collider, enemy, erb, velocity, speed, "hit");
        }

        /// <summary>
        /// Host, each physics step: enemies and training dummies within HitReach of the free handle count as hit too
        /// (if it's moving fast enough), so a near miss that only grazes past still lands.
        /// </summary>
        private readonly Collider[] nearby = new Collider[32];

        private void CheckReach()
        {
            float reach = Settings.NunchuckReach.Value;
            if (reach <= 0f || freeHandle == null) return;
            Vector3 bottom = freeHandle.transform.TransformPoint(Vector3.up * HandleRadius);
            Vector3 top = freeHandle.transform.TransformPoint(Vector3.up * (NunchuckBuilder.HandleLength - HandleRadius));
            // the grip end swings fastest; nothing to do if even that is too slow to count
            if (freeHandle.GetPointVelocity(bottom).magnitude < Settings.NunchuckSpeedLow.Value) return;

            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, HandleRadius + reach, nearby, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = nearby[i];
                if (c == null || c == freeCollider) continue;
                EnemyRigidbody erb = c.GetComponentInParent<EnemyRigidbody>();
                Enemy enemy = erb != null ? erb.enemy : c.GetComponentInParent<Enemy>();
                if (enemy == null && !c.name.StartsWith("Hitbox")) continue;   // only enemies and dummies; walls and floors need real contact
                // speed of the handle where it passes closest to the target, relative to the target
                Vector3 middle = Vector3.Lerp(bottom, top, 0.5f);
                Vector3 onTarget = c is MeshCollider mc && !mc.convex ? c.bounds.ClosestPoint(middle) : c.ClosestPoint(middle);
                Vector3 axis = top - bottom;
                float along = Mathf.Clamp01(Vector3.Dot(onTarget - bottom, axis) / axis.sqrMagnitude);
                Vector3 onHandle = bottom + axis * along;
                Vector3 velocity = freeHandle.GetPointVelocity(onHandle);
                if (c.attachedRigidbody != null) velocity -= c.attachedRigidbody.GetPointVelocity(onTarget);
                float speed = velocity.magnitude;
                if (speed < SoundSpeed) continue;
                if (RegisterHit(c, enemy, erb, velocity, speed, "graze")) PlaySound(enemy != null, onTarget, speed);
            }
        }

        /// <summary>Host: one hit on an enemy, a dummy, or anything else. Returns whether it counted.</summary>
        private bool RegisterHit(Collider target, Enemy enemy, EnemyRigidbody erb, Vector3 velocity, float speed, string how)
        {
            Object key = enemy != null ? (Object)enemy : target.transform.root;
            if (nextHit.TryGetValue(key, out float t) && Time.time < t) return false;
            nextHit[key] = Time.time + Settings.NunchuckHitCooldown.Value;
            if (nextHit.Count > 64) nextHit.Clear();

            Vector3 dir = velocity / Mathf.Max(speed, 1e-4f);
            int damage = DamageFor(speed);
            bool dummy = enemy == null && target.name.StartsWith("Hitbox");   // TargetDummy's dummies
            string name = enemy != null ? enemy.name : target.name;
            if (damage < Settings.NunchuckMinDamage.Value)
            {
                if (Settings.NunchuckDebugLog.Value && (enemy != null || dummy))
                    Plugin.Log.LogInfo($"Nunchuck: {speed:0.0} m/s {how} on {name} didn't count ({damage} < MinDamage {Settings.NunchuckMinDamage.Value}).");
                nextHit.Remove(key);
                return false;
            }
            if (enemy != null || dummy) UseCharge(false);
            if (enemy != null) HitEnemy(enemy, erb, damage, dir, speed);
            else
            {
                // not an enemy: a training dummy (TargetDummy mod) shows the hit; anything else ignores it
                target.SendMessageUpwards("TargetDummyHit",
                    new float[] { damage, dir.x, dir.y, dir.z, speed * Settings.NunchuckKnockback.Value }, SendMessageOptions.DontRequireReceiver);
            }
            if (Settings.NunchuckDebugLog.Value && (enemy != null || dummy))
                Plugin.Log.LogInfo($"Nunchuck {how} on {name} at {speed:0.0} m/s: {damage} damage.");
            return enemy != null || dummy;
        }

        private static void HitEnemy(Enemy enemy, EnemyRigidbody erb, int damage, Vector3 dir, float speed)
        {
            try
            {
                EnemyHealth health = EnemyHealthField?.GetValue(enemy) as EnemyHealth ?? enemy.GetComponentInChildren<EnemyHealth>();
                if (health != null && damage > 0) health.Hurt(damage, dir);

                float push = speed * Settings.NunchuckKnockback.Value;
                Rigidbody body = erb != null ? EnemyBodyField?.GetValue(erb) as Rigidbody : null;
                if (body != null && push > 0f) body.AddForce(dir * push, ForceMode.Impulse);

                bool stun = Settings.NunchuckStunSpeed.Value > 0f && speed >= Settings.NunchuckStunSpeed.Value && Settings.NunchuckStunTime.Value > 0f;
                if (stun && EnemyStunField?.GetValue(enemy) is EnemyStateStunned stunned && stunned != null) stunned.Set(Settings.NunchuckStunTime.Value);

                if (Settings.NunchuckDebugLog.Value && (push > 0f || stun))
                    Plugin.Log.LogInfo($"  push {push:0.0}{(stun ? $", stunned {Settings.NunchuckStunTime.Value:0.#} s" : "")}.");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Nunchuck enemy hit failed: {e.Message}"); }
        }

        /// <summary>Host: the free handle passes through players, so check it against each one (not the holder).</summary>
        private void CheckPlayers()
        {
            Vector3 bottom = freeHandle.transform.TransformPoint(Vector3.up * HandleRadius);
            Vector3 top = freeHandle.transform.TransformPoint(Vector3.up * (NunchuckBuilder.HandleLength - HandleRadius));
            Vector3 tipVelocity = freeHandle.GetPointVelocity(bottom);
            float speed = tipVelocity.magnitude;
            if (speed < Settings.NunchuckSpeedLow.Value) return;

            var holders = new HashSet<PlayerAvatar>();
            if (physGrabObject != null && physGrabObject.playerGrabbing != null)
                foreach (PhysGrabber g in physGrabObject.playerGrabbing) if (g != null && g.playerAvatar != null) holders.Add(g.playerAvatar);

            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                // real players only: an inactive avatar is a stand-in (e.g. TargetDummy's Ally Dummy), whose own
                // hitboxes already take the physical hit
                if (p == null || !p.gameObject.activeInHierarchy || holders.Contains(p) || p.playerHealth == null) continue;
                if (Flag(DisabledField, p) || Flag(DeadField, p)) continue;
                Vector3 feet = p.transform.position;
                ClosestPoints(bottom, top, feet + Vector3.up * 0.4f, feet + Vector3.up * 1.2f, out Vector3 onHandle, out Vector3 onPlayer);
                if ((onHandle - onPlayer).sqrMagnitude > (0.4f + HandleRadius) * (0.4f + HandleRadius)) continue;
                if (nextHit.TryGetValue(p, out float t) && Time.time < t) continue;
                nextHit[p] = Time.time + Settings.NunchuckHitCooldown.Value;

                int damage = DamageFor(speed);
                if (damage < Settings.NunchuckMinDamage.Value) { nextHit.Remove(p); continue; }   // too weak to count
                UseCharge(true);
                try { p.playerHealth.HurtOther(damage, onHandle, false, -1, false); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Nunchuck player hit failed: {e.Message}"); }
                PlaySound(true, onHandle, speed);
                if (Settings.NunchuckDebugLog.Value) Plugin.Log.LogInfo($"Nunchuck hit player {p.name} at {speed:0.0} m/s: {damage} damage.");
            }
        }

        private static bool Flag(FieldInfo f, object o) => f != null && (bool)f.GetValue(o);

        /// <summary>Host: a hit that counted uses charge, the same amount a bat hit on an enemy or player does.</summary>
        private void UseCharge(bool player)
        {
            if (melee == null) return;
            try { if (player) melee.PlayerSwingHit(); else melee.EnemySwingHit(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Nunchuck: using charge failed: {e.Message}"); }
        }

        private void PlaySound(bool flesh, Vector3 point, float speed)
        {
            if (melee == null || Time.time < nextSound) return;
            nextSound = Time.time + 0.12f;
            float volume = Mathf.Clamp01(speed / 15f);
            try
            {
                Sound s = flesh ? melee.soundHit : melee.soundDink;
                if (s != null) s.Play(point, volume, 1f, 1f, 1f);
            }
            catch (System.Exception) { }
        }

        /// <summary>Closest points between segments AB and CD.</summary>
        private static void ClosestPoints(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 p1, out Vector3 p2)
        {
            Vector3 d1 = b - a, d2 = d - c, r = a - c;
            float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (aa <= 1e-8f) { s = 0f; t = ee > 1e-8f ? Mathf.Clamp01(f / ee) : 0f; }
            else
            {
                float cc = Vector3.Dot(d1, r);
                if (ee <= 1e-8f) { t = 0f; s = Mathf.Clamp01(-cc / aa); }
                else
                {
                    float bb = Vector3.Dot(d1, d2), denom = aa * ee - bb * bb;
                    s = denom > 1e-8f ? Mathf.Clamp01((bb * f - cc * ee) / denom) : 0f;
                    t = (bb * s + f) / ee;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-cc / aa); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - cc) / aa); }
                }
            }
            p1 = a + d1 * s; p2 = c + d2 * t;
        }
    }

    /// <summary>Sits on the free handle and passes its collisions to the Nunchuck.</summary>
    internal class NunchuckFreeEnd : MonoBehaviour
    {
        internal Nunchuck owner;
        private void OnCollisionEnter(Collision c) { if (owner != null) owner.OnFreeEndCollision(c); }
        private void OnCollisionStay(Collision c) { if (owner != null) owner.OnFreeEndCollision(c); }
    }
}
