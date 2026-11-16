using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

namespace HatterWeaponsPack
{
    /// <summary>
    /// The Friendship Cannon's riders (on the same object as its OrbCannon, which has friendship set and does the rest:
    /// state, aim, cooldown, sounds, looks).
    ///  - Unloaded: the trigger (host) looks for a TUMBLING player (not the holder) along the holder's aim within
    ///    LoadRange of the cannon, the same reach rule as the Orb Cannon's orbs (or the test dummy). Found: loaded.
    ///  - Loaded: the host holds the rider's body inside the barrel (kinematic, a little behind the muzzle) and keeps
    ///    them tumbling; every game hides the body and switches its colliders off; the sight screen shows their head.
    ///    On the rider's own game: a tunnel view, no control, the camera aimed where the cannon points; jumping
    ///    there jams the cannon (a small blast at the muzzle, they pop out, the cannon cools).
    ///  - The trigger while loaded fires them: out of the muzzle at LaunchSpeed where the holder aims, in a blue bubble.
    ///    The bubble pops on its first contact with anything (BubbleDamage to an enemy or player it hit); until then the
    ///    rider can't be hurt, and the pop's impact doesn't hurt them either (a short grace), after that they're a
    ///    normal tumbling player.
    /// </summary>
    public class FriendshipCannon : MonoBehaviour
    {
        private const float BodyRadius = 0.45f;   // a tumbling player's ball, roughly
        internal const int DummyId = -2;          // the test dummy's "view id" in the RPCs (host only)

        private OrbCannon cannon;
        private PhotonView view;
        // the rider: a real player (their tumble body) or the test dummy
        private PlayerAvatar loaded;
        private FriendDummy dummy;
        private PlayerTumble tumble;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Collider> offColliders = new List<Collider>();
        private float nextKeepTumbling, loadedAt;
        // host: a rider in flight
        private Flight flight;

        private class Flight
        {
            internal Rigidbody Body;
            internal PlayerAvatar Player;
            internal Transform Root;
            internal Vector3 Last, Dir;
            internal float Start;
            internal PlayerAvatar Holder;
        }

        private static readonly FieldInfo TumbleField = AccessTools.Field(typeof(PlayerAvatar), "tumble");
        private static readonly FieldInfo IsTumblingField = AccessTools.Field(typeof(PlayerTumble), "isTumbling");
        private static readonly FieldInfo TumbleRbField = AccessTools.Field(typeof(PlayerTumble), "rb");
        private static readonly FieldInfo DisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo DeadField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static readonly FieldInfo EquippedField = AccessTools.Field(typeof(ItemEquippable), "isEquipped");
        private ItemEquippable equippable;
        private PlayerAvatar lastHolder;
        private bool Equipped => equippable != null && EquippedField != null && (bool)EquippedField.GetValue(equippable);

        /// <summary>Whose head the screen shows: the loaded player, or for the dummy your own.</summary>
        internal PlayerAvatar Loaded => loaded != null ? loaded : dummy != null ? PlayerAvatar.instance : null;
        private bool HasRider => loaded != null || dummy != null;
        private static bool IsHost => SemiFunc.IsMasterClientOrSingleplayer();
        private static void Log(string m) { if (Settings.OrbDebugLog.Value) Plugin.Log.LogInfo("Friendship Cannon: " + m); }

        private void Awake()
        {
            cannon = GetComponent<OrbCannon>();
            view = GetComponent<PhotonView>();
            equippable = GetComponent<ItemEquippable>();
        }

        internal static PlayerTumble TumbleOf(PlayerAvatar p) => p != null && TumbleField != null ? TumbleField.GetValue(p) as PlayerTumble : null;

        internal static bool IsTumbling(PlayerAvatar p)
        {
            PlayerTumble t = TumbleOf(p);
            return t != null && IsTumblingField != null && (bool)IsTumblingField.GetValue(t);
        }

        private static Rigidbody BodyOf(PlayerTumble t) => t != null && TumbleRbField != null ? TumbleRbField.GetValue(t) as Rigidbody : null;
        private static bool Flag(FieldInfo f, object o) => f != null && o != null && (bool)f.GetValue(o);
        private static bool Alive(PlayerAvatar p) => p != null && p.gameObject.activeInHierarchy && !Flag(DisabledField, p) && !Flag(DeadField, p);
        private Rigidbody RiderBody => dummy != null ? dummy.Body : BodyOf(tumble);
        private Transform RiderRoot => dummy != null ? dummy.transform : tumble != null ? tumble.transform : null;

        /// <summary>Where the rider's body sits: inside the barrel, a little behind the muzzle.</summary>
        internal Vector3 HoldPoint => transform.TransformPoint(cannon.muzzleLocal - new Vector3(0f, 0f, 0.3f));

        // ---------------- host: the trigger ----------------

        /// <summary>Host: the trigger was pulled. Always false: the game's own shot never runs (the cannon plays its own sounds).</summary>
        internal bool HostTrigger(Vector3 origin, Vector3 dir)
        {
            OrbCannon.State s = cannon.CurrentState;
            if (s == OrbCannon.State.Loaded)
            {
                Vector3 target = cannon.AimPoint(origin, dir);
                Vector3 d = target - cannon.Muzzle;
                if (d.sqrMagnitude < 0.25f || Vector3.Angle(d, transform.forward) > 60f) d = transform.forward;   // too close / behind: straight out
                HostFire(d.normalized);
                cannon.FriendSetState(OrbCannon.State.Cooling);
                return false;
            }
            if (s != OrbCannon.State.Unloaded) return false;
            int id = FindTarget(origin, dir, out string who);
            if (id == -1) { Log("trigger pulled, but no tumbling player in reach along the aim."); return false; }
            Log($"loads {who}.");
            SendLoad(id);
            cannon.FriendSetState(OrbCannon.State.Loaded);
            return false;
        }

        /// <summary>The best tumbling player (their view id) or the dummy (DummyId) in reach along the aim; -1 if none.</summary>
        private int FindTarget(Vector3 origin, Vector3 dir, out string who)
        {
            who = null;
            PlayerAvatar holder = cannon.Holder;
            Vector3 rear = cannon.RearPoint, front = cannon.Muzzle;
            float bestScore = float.MaxValue;
            int best = -1;
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (p == null || p == holder || !Alive(p) || !IsTumbling(p) || p.photonView == null) continue;
                float score = Score(TumbleOf(p).transform.position, origin, dir, rear, front);
                if (score < bestScore) { bestScore = score; best = p.photonView.ViewID; who = SemiFunc.PlayerGetName(p); }
            }
            if (FriendDummy.Instance != null && FriendDummy.Instance.Body != null)
            {
                float score = Score(FriendDummy.Instance.Body.worldCenterOfMass, origin, dir, rear, front);
                if (score < bestScore) { best = DummyId; who = "the test dummy"; }
            }
            return best;
        }

        private float Score(Vector3 c, Vector3 origin, Vector3 dir, Vector3 rear, Vector3 front)
        {
            Vector3 onAxis = OrbCannon.ClosestOnSegment(rear, front, c);
            if (Vector3.Distance(onAxis, c) - BodyRadius - cannon.BoreRadius > Settings.FriendLoadRange.Value) return float.MaxValue;
            Vector3 to = c - origin;
            float along = Vector3.Dot(to, dir);
            if (along <= 0f) return float.MaxValue;
            float miss = (to - dir * along).magnitude;
            if (miss > BodyRadius + 0.15f) return float.MaxValue;
            return miss / BodyRadius + along * 0.01f;
        }

        /// <summary>Host: lets the rider out at the muzzle with this velocity (no bubble).</summary>
        internal void HostRelease(Vector3 velocity)
        {
            if (!HasRider) return;
            Rigidbody rb = RiderBody;
            SendLoad(-1);   // every game: shown again, colliders back on
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.position = cannon.Muzzle + transform.forward * 0.6f;
                rb.linearVelocity = velocity;
                rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>Host: out in front of the last holder (the cannon itself is shrunk into an inventory slot).</summary>
        private void HostEject()
        {
            if (!HasRider) return;
            Rigidbody rb = RiderBody;
            SendLoad(-1);
            if (rb == null) return;
            Transform who = lastHolder != null ? lastHolder.transform : transform;
            Vector3 fwd = who.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            rb.isKinematic = false;
            rb.position = who.position + fwd * 1.0f + Vector3.up * 0.8f;
            rb.linearVelocity = fwd * 2f + Vector3.up * 1.5f;
            rb.angularVelocity = Vector3.zero;
        }

        // ---------------- host: firing ----------------

        private void HostFire(Vector3 dir)
        {
            if (!HasRider) return;
            Rigidbody rb = RiderBody;
            PlayerAvatar player = loaded;
            int id = dummy != null ? DummyId : loaded.photonView.ViewID;
            Log($"fires {(player != null ? SemiFunc.PlayerGetName(player) : "the test dummy")} at {Settings.FriendLaunchSpeed.Value:0} m/s.");
            SendLoad(-1);                       // every game: shown again, colliders back on
            if (SemiFunc.IsMultiplayer() && view != null) view.RPC(nameof(HWPFriendFireRPC), RpcTarget.All, id, cannon.Muzzle);
            else ShowFire(id, cannon.Muzzle);
            if (rb == null) return;
            rb.isKinematic = false;
            rb.position = cannon.Muzzle + dir * 0.9f;
            rb.linearVelocity = dir * Settings.FriendLaunchSpeed.Value;
            rb.angularVelocity = Random.insideUnitSphere * 8f;
            if (player != null)
            {
                PlayerTumble t = TumbleOf(player);
                try { t.TumbleOverrideTime(1.5f); } catch { }
                try { player.playerHealth.InvincibleSet(0.3f); } catch { }
            }
            flight = new Flight
            {
                Body = rb, Player = player, Root = player != null ? TumbleOf(player).transform : rb.transform,
                Last = rb.position, Dir = dir, Start = Time.time, Holder = cannon.Holder,
            };
        }

        /// <summary>Host, every physics step: the bubble's contact check.</summary>
        private void FlightTick()
        {
            Flight f = flight;
            if (f.Body == null) { flight = null; return; }
            float age = Time.time - f.Start;
            float radius = Settings.FriendBubbleRadius.Value;
            if (f.Player != null) { try { f.Player.playerHealth.InvincibleSet(0.3f); } catch { } }   // safe inside the bubble
            Vector3 now = f.Body.worldCenterOfMass;
            Vector3 step = now - f.Last;
            Collider hitCollider = null;
            PlayerAvatar hitPlayer = null;
            Vector3 point = now;
            bool contact = false;

            // a player in the way (any but the rider; the holder only after a moment)
            PlayerAvatar shooterToSkip = age < 0.25f ? f.Holder : null;
            if (step.sqrMagnitude > 1e-6f && ProjectileHits.Player(f.Last, step, radius, step.magnitude + radius, shooterToSkip, out PlayerAvatar p, out Vector3 pp) && p != f.Player)
            { contact = true; hitPlayer = p; point = pp; }
            // anything else: world, objects, enemies (not the rider's own body, not the cannon at first)
            if (!contact)
            {
                Vector3 from = f.Last, dir = step.sqrMagnitude > 1e-6f ? step.normalized : f.Dir;
                float len = Mathf.Max(step.magnitude, 0.01f);
                foreach (RaycastHit h in Physics.SphereCastAll(from, radius, dir, len, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!Counts(h.collider, f, age)) continue;
                    contact = true; hitCollider = h.collider; point = h.point == Vector3.zero ? now : h.point;
                    break;
                }
            }
            if (!contact)
                foreach (Collider c in Physics.OverlapSphere(now, radius, ~0, QueryTriggerInteraction.Ignore))
                    if (Counts(c, f, age)) { contact = true; hitCollider = c; point = c.ClosestPoint(now); break; }
            // the body itself was stopped (it hit something the checks missed)
            if (!contact && age > 0.08f && f.Body.linearVelocity.magnitude < Settings.FriendLaunchSpeed.Value * 0.4f) contact = true;
            f.Last = now;

            if (!contact && age < Settings.FriendBubbleSeconds.Value) return;
            Pop(f, hitCollider, hitPlayer, point, contact);
        }

        /// <summary>Does this collider pop the bubble? Not the rider, not the cannon or its holder in the first moment, not other triggers.</summary>
        private bool Counts(Collider c, Flight f, float age)
        {
            if (c == null || c.isTrigger) return false;
            if (f.Root != null && c.transform.IsChildOf(f.Root)) return false;
            if (f.Player != null && c.GetComponentInParent<PlayerAvatar>() == f.Player) return false;
            if (age < 0.25f)
            {
                if (c.transform.IsChildOf(transform)) return false;
                if (f.Holder != null && (c.GetComponentInParent<PlayerAvatar>() == f.Holder || c.GetComponentInParent<PlayerController>() != null)) return false;
            }
            if (c.GetComponentInParent<PlayerController>() != null) return false;   // the local player's own controller (players are checked separately)
            return true;
        }

        private void Pop(Flight f, Collider hitCollider, PlayerAvatar hitPlayer, Vector3 point, bool contact)
        {
            flight = null;
            int damage = Settings.FriendBubbleDamage.Value;
            string what = !contact ? "nothing (timed out)" : hitPlayer != null ? SemiFunc.PlayerGetName(hitPlayer) : hitCollider != null ? hitCollider.name : "something";
            if (hitPlayer != null) ProjectileHits.HurtPlayer(hitPlayer, damage, point);
            else if (hitCollider != null) ProjectileHits.HurtEnemy(hitCollider, damage, f.Dir);   // enemies (and TargetDummy); nothing for walls
            if (f.Player != null) { try { f.Player.playerHealth.InvincibleSet(0.5f); } catch { } }   // the pop's own impact doesn't hurt the rider
            if (f.Body != null && !f.Body.isKinematic)
            {
                // after the pop the rider just rolls on at walking speed, the way they were going (no long bounce)
                Vector3 v = f.Body.linearVelocity;
                Vector3 dir = v.sqrMagnitude > 0.01f ? v.normalized : f.Dir;
                float walk = PlayerController.instance != null && PlayerController.instance.MoveSpeed > 0.1f ? PlayerController.instance.MoveSpeed : 2.5f;
                f.Body.linearVelocity = dir * walk;
                f.Body.angularVelocity *= 0.2f;
            }
            Log($"the bubble pops on {what} after {Time.time - f.Start:0.00} s{(hitPlayer != null || hitCollider != null ? $" ({damage} damage)" : "")}.");
            Vector3 at = f.Body != null ? f.Body.worldCenterOfMass : point;
            if (SemiFunc.IsMultiplayer() && view != null) view.RPC(nameof(HWPFriendPopRPC), RpcTarget.All, at);
            else ShowPop(at);
        }

        // ---------------- host: the malfunction ----------------

        /// <summary>Host: the rider jumped. A small blast at the muzzle, they pop out, the cannon cools.</summary>
        internal void HostJam()
        {
            if (cannon.CurrentState != OrbCannon.State.Loaded || !HasRider) return;
            Vector3 at = cannon.Muzzle;
            Log("the rider jumped: malfunction.");
            if (SemiFunc.IsMultiplayer() && view != null) view.RPC(nameof(HWPFriendJamRPC), RpcTarget.All, at);
            else ShowJam(at);
            HostRelease(transform.forward * 2f + Vector3.up * 1.5f);
            cannon.FriendSetState(OrbCannon.State.Cooling);
        }

        [PunRPC]
        public void HWPFriendJumpRPC(PhotonMessageInfo info)
        {
            if (!IsHost || loaded == null) return;
            PlayerAvatar sender = SemiFunc.PlayerAvatarGetFromPhotonPlayer(info.Sender);
            if (sender != loaded) return;
            HostJam();
        }

        // ---------------- every game: loaded / unloaded, effects ----------------

        private void SendLoad(int id)
        {
            if (SemiFunc.IsMultiplayer() && view != null) view.RPC(nameof(HWPFriendLoadRPC), RpcTarget.All, id);
            else ApplyLoad(id);
        }

        [PunRPC]
        public void HWPFriendLoadRPC(int id, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            ApplyLoad(id);
        }

        private void ApplyLoad(int id)
        {
            ApplyUnload();
            if (id == -1) return;
            Transform root;
            IEnumerable<Renderer> renderers;
            if (id == DummyId)
            {
                dummy = FriendDummy.Instance;
                if (dummy == null) return;     // the dummy only exists on the host's game
                root = dummy.transform;
                renderers = dummy.GetComponentsInChildren<Renderer>(true);
            }
            else
            {
                PlayerAvatar p = SemiFunc.PlayerAvatarGetFromPhotonID(id);
                if (p == null) return;
                loaded = p;
                tumble = TumbleOf(p);
                root = tumble != null ? tumble.transform : null;
                renderers = p.playerAvatarVisuals != null ? p.playerAvatarVisuals.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            }
            loadedAt = Time.time;
            nextKeepTumbling = 0f;
            // their body is inside the cannon: nobody sees it, nothing hits it
            foreach (Renderer r in renderers) if (r != null && r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
            if (root != null)
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                    if (c != null && c.enabled) { c.enabled = false; offColliders.Add(c); }
            if (loaded != null && loaded == PlayerAvatar.instance) FriendshipView.Show();
            Log($"(this game) {(dummy != null ? "the test dummy" : loaded.name)} is loaded: hidden {hiddenRenderers.Count} renderer(s), {offColliders.Count} collider(s) off{(loaded != null && loaded == PlayerAvatar.instance ? "; that's you: tunnel view on" : "")}.");
        }

        private void ApplyUnload()
        {
            foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
            foreach (Collider c in offColliders) if (c != null) c.enabled = true;
            hiddenRenderers.Clear();
            offColliders.Clear();
            if (loaded != null && loaded == PlayerAvatar.instance) FriendshipView.Hide();
            loaded = null;
            dummy = null;
            tumble = null;
        }

        [PunRPC]
        public void HWPFriendFireRPC(int id, Vector3 muzzle, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            ShowFire(id, muzzle);
        }

        /// <summary>Every game: the shot's boom, and the blue bubble round the rider.</summary>
        private void ShowFire(int id, Vector3 muzzle)
        {
            OrbCannon.Play(cannon.boomSound != null ? cannon.boomSound : cannon.shotSound, muzzle, 2f);
            OrbFx.LaserFlare(muzzle, transform.forward, OrbTubes.LiquidBlue, OrbTubes.LiquidBlueLight);
            Transform rider = null;
            if (id == DummyId) rider = FriendDummy.Instance != null ? FriendDummy.Instance.transform : null;
            else
            {
                PlayerTumble t = TumbleOf(SemiFunc.PlayerAvatarGetFromPhotonID(id));
                rider = t != null ? t.transform : null;
            }
            if (rider != null) FriendBubble.Attach(rider, Settings.FriendBubbleRadius.Value);
        }

        [PunRPC]
        public void HWPFriendPopRPC(Vector3 at, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            ShowPop(at);
        }

        private void ShowPop(Vector3 at)
        {
            FriendBubble.PopAll(at);
            OrbCannon.Play(cannon.shotSound, at, 1f);
        }

        [PunRPC]
        public void HWPFriendJamRPC(Vector3 at, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            ShowJam(at);
        }

        /// <summary>Every game: the malfunction's small blast (the effect, and on the host the hurt sphere does the damage).</summary>
        private static void ShowJam(Vector3 at)
        {
            float r = Settings.FriendJamRadius.Value;
            RocketExplosion.SpawnSmall(at, Mathf.Clamp(r * 1.2f, 0.3f, 2f), r, Settings.FriendJamDamage.Value, Settings.FriendJamKnockback.Value);
        }

        // ---------------- holding them ----------------

        private void FixedUpdate()
        {
            if (!IsHost) return;
            if (flight != null) FlightTick();
            if (!HasRider) return;
            if (dummy == null && (!Alive(loaded) || tumble == null || (Time.time - loadedAt > 1f && !IsTumbling(loaded))))
            {
                Log("the loaded player is gone or no longer tumbling; letting go.");
                HostRelease(Vector3.zero);
                if (cannon.CurrentState == OrbCannon.State.Loaded) cannon.FriendSetState(OrbCannon.State.Cooling);
                return;
            }
            PlayerAvatar h = cannon.Holder;
            if (h != null) lastHolder = h;
            if (Equipped)
            {
                // put away in an inventory slot with a friend inside: they're thrown out in front of whoever put it away
                Log("put away while loaded: the rider is ejected.");
                HostEject();
                if (cannon.CurrentState == OrbCannon.State.Loaded) cannon.FriendSetState(OrbCannon.State.Cooling);
                return;
            }
            Rigidbody rb = RiderBody;
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.MovePosition(HoldPoint);
                rb.MoveRotation(transform.rotation);
            }
            if (tumble != null && Time.time >= nextKeepTumbling)
            {
                nextKeepTumbling = Time.time + 0.5f;
                try { tumble.TumbleOverrideTime(1.5f); } catch { }
            }
        }

        private void LateUpdate()
        {
            if (!HasRider) return;
            foreach (Renderer r in hiddenRenderers) if (r != null && r.enabled) r.enabled = false;   // keep them hidden
            // the dummy "jumps" with the debug key (host)
            if (dummy != null && IsHost && Settings.FriendDebugJumpKey.Value.IsDown()) { HostJam(); return; }
            if (loaded == null || loaded != PlayerAvatar.instance) return;
            // the rider: no control, and they look where the cannon points; jumping jams the cannon
            try
            {
                if (PlayerController.instance != null) PlayerController.instance.InputDisable(0.2f);
                if (CameraAim.Instance != null) CameraAim.Instance.AimTargetSet(cannon.Muzzle + transform.forward * 30f, 0.2f, 60f, gameObject, 999);
                if (InputManager.instance != null && InputManager.instance.KeyDown(InputKey.Jump, true) && Time.time - loadedAt > 0.5f)
                {
                    if (IsHost) HostJam();
                    else if (view != null) view.RPC(nameof(HWPFriendJumpRPC), RpcTarget.MasterClient);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Friendship Cannon: the rider's controls failed: {e.Message}"); }
        }

        private void OnDisable()
        {
            try { if (IsHost && HasRider) HostRelease(Vector3.zero); } catch { }
            ApplyUnload();
            flight = null;
        }
    }

    /// <summary>The blue bubble round a launched rider (every game), until the host says it popped.</summary>
    internal class FriendBubble : MonoBehaviour
    {
        private static readonly List<FriendBubble> all = new List<FriendBubble>();
        private Material mat;
        private Light glow;
        private float born;

        internal static void Attach(Transform rider, float radius)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Friendship Bubble";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(rider, false);
            go.transform.localPosition = Vector3.zero;
            Vector3 s = rider.lossyScale;
            float k = Mathf.Max(0.01f, Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
            go.transform.localScale = Vector3.one * (radius * 2f / k);
            var b = go.AddComponent<FriendBubble>();
            b.born = Time.time;
            var r = go.GetComponent<MeshRenderer>();
            b.mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.45f, 0.82f, 1f, 0.28f) };
            r.sharedMaterial = b.mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.glow = go.AddComponent<Light>();
            b.glow.type = LightType.Point;
            b.glow.color = new Color(0.45f, 0.82f, 1f);
            b.glow.range = 3f;
            b.glow.intensity = 2f;
            b.glow.shadows = LightShadows.None;
            all.Add(b);
        }

        internal static void PopAll(Vector3 at)
        {
            foreach (FriendBubble b in all.ToArray()) if (b != null) Destroy(b.gameObject);
            all.Clear();
            try { OrbFx.Burst(at, 0.8f); } catch { }
        }

        private void Update()
        {
            if (mat != null) mat.color = new Color(0.45f, 0.82f, 1f, 0.22f + 0.08f * Mathf.Sin((Time.time - born) * 12f));
            transform.rotation = Quaternion.identity;
            if (Time.time - born > Settings.FriendBubbleSeconds.Value + 2f) Destroy(gameObject);   // never stuck if a pop message is lost
        }

        private void OnDestroy()
        {
            all.Remove(this);
            if (mat != null) Destroy(mat);
        }
    }

    /// <summary>
    /// Testing (host, [Friendship Cannon] DebugDummy): a stand-in "tumbling player": a 0.8 m physics ball wearing a
    /// copy of your head, 2.5 m in front of you 3 s after a level loads (DebugDummyRespawnKey brings it back). The
    /// cannon loads, holds and fires it like a player (no camera, so no tunnel view; DebugDummyJumpKey "jumps").
    /// </summary>
    internal class FriendDummy : MonoBehaviour
    {
        internal static FriendDummy Instance;
        internal Rigidbody Body;
        private static float levelAt = -1f;
        private static bool spawned;

        internal static void CreateWatcher()
        {
            var go = new GameObject("HatterWeaponsPack.FriendDummyWatcher");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Watcher>();
        }

        private class Watcher : MonoBehaviour
        {
            private void Update()
            {
                bool inLevel = LevelGenerator.Instance != null && LevelGenerator.Instance.Generated && SemiFunc.RunIsLevel();
                if (!Settings.FriendDebugDummy.Value || !inLevel || !SemiFunc.IsMasterClientOrSingleplayer())
                {
                    if (!inLevel) { levelAt = -1f; spawned = false; }
                    if (Instance != null && !Settings.FriendDebugDummy.Value) Destroy(Instance.gameObject);
                    return;
                }
                if (levelAt < 0f) levelAt = Time.time;
                if (!spawned && Time.time - levelAt > 3f) { spawned = true; Spawn(); }
                if (Settings.FriendDebugRespawnKey.Value.IsDown()) Spawn();
            }
        }

        private static void Spawn()
        {
            PlayerAvatar me = PlayerAvatar.instance;
            if (me == null) return;
            Vector3 fwd = me.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 at = me.transform.position + fwd.normalized * 2.5f + Vector3.up * 1.2f;
            if (Instance == null)
            {
                var go = new GameObject("Friendship Cannon Test Dummy");
                Instance = go.AddComponent<FriendDummy>();
                var col = go.AddComponent<SphereCollider>();
                col.radius = 0.4f;
                Instance.Body = go.AddComponent<Rigidbody>();
                Instance.Body.mass = 3f;
                Instance.Body.angularDamping = 0.6f;
                Instance.Body.interpolation = RigidbodyInterpolation.Interpolate;
                Instance.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                GameObject head = OrbSight.CopyHeadModel(me, go.transform, 0.8f, Vector3.zero, false);
                if (head == null)
                {
                    // no head to copy: a plain grey ball
                    GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(ball.GetComponent<Collider>());
                    ball.transform.SetParent(go.transform, false);
                    ball.transform.localScale = Vector3.one * 0.8f;
                }
            }
            Instance.Body.isKinematic = false;
            Instance.transform.position = at;
            Instance.Body.position = at;
            Instance.Body.linearVelocity = Vector3.zero;
            Instance.Body.angularVelocity = Vector3.zero;
            Plugin.Log.LogInfo($"Friendship Cannon: the test dummy is at {at} (in front of you).");
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }

    /// <summary>The loaded player's tunnel view: the screen goes black but for a soft round window, a faint blue rim.</summary>
    internal static class FriendshipView
    {
        private static GameObject root;

        internal static void Show()
        {
            if (root == null) Build();
            if (root != null) root.SetActive(true);
        }

        internal static void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        private static void Build()
        {
            root = new GameObject("HWP Friendship Cannon View");
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var go = new GameObject("Tunnel", typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            var img = go.AddComponent<RawImage>();
            img.texture = Tunnel();
            img.raycastTarget = false;
            go.AddComponent<TunnelFit>();
            root.SetActive(false);
        }

        private static Texture2D Tunnel()
        {
            const int n = 512;
            const float hole = 0.14f, soft = 0.035f, rim = 0.02f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HWP Friendship Tunnel" };
            var rimColor = new Color(0.15f, 0.45f, 0.7f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hole, hole + soft, d));
                    float r = 1f - Mathf.Clamp01(Mathf.Abs(d - hole - soft * 0.5f) / rim);   // a thin blue glow at the edge
                    Color c = Color.Lerp(Color.black, rimColor, r * 0.6f);
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, Mathf.Max(a, r * 0.5f)));
                }
            tex.Apply();
            return tex;
        }

        /// <summary>Keeps the (square) tunnel image as wide as the screen, centred, so the window stays round.</summary>
        private class TunnelFit : MonoBehaviour
        {
            private RectTransform rt;

            private void LateUpdate()
            {
                if (rt == null) rt = GetComponent<RectTransform>();
                float s = Mathf.Max(Screen.width, Screen.height) * 1.02f;
                rt.sizeDelta = new Vector2(s, s);
                rt.anchoredPosition = Vector2.zero;
            }
        }
    }
}
