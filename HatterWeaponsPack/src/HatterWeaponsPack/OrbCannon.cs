using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// The Orb Cannon. Lives on the cannon root (next to its PhotonView). The host decides everything; every player's
    /// game shows it.
    ///  - Unloaded: pulling the trigger (ItemGun.Shoot, host) looks for a soul orb (EnemyValuable) along the holder's
    ///    aim, within LoadRange of the cannon. Found: the orb is destroyed (its money is gone) and the cannon is loaded
    ///    with its size; the orb flies into the muzzle and the traces light up pink. Nothing else loads.
    ///  - Loaded: the trigger fires it (the game's shot runs for the sound and kick; HWPOrbCannonFireRPC carries the
    ///    size and the crosshair point to everyone):
    ///      small  = a shockwave cone (ShockwaveRange, ShockwaveAngle, ShockwaveDamage, a shove);
    ///      medium = BurstShots small orbs, BurstGap apart (BurstDamage each, a small splash, never more than
    ///               BurstDamage per target per orb);
    ///      large  = one big slow orb: BigDamage on a direct hit plus the Rocket Launcher's blast.
    ///  - Cooling (CooldownSeconds): the traces go black and smoke pours out of the muzzle and the rear vent.
    /// Every kind of damage hurts players too (the user's friendly-fire rule); the holder is never hit by their own
    /// shockwave or by a direct hit from their own orbs, but splash and blasts can catch them.
    /// </summary>
    public class OrbCannon : MonoBehaviour
    {
        internal enum State { Unloaded = 0, Loaded = 1, Cooling = 2 }
        internal enum Kind { Small = 0, Medium = 1, Large = 2 }

        internal static readonly Color Pink = new Color(1f, 0.32f, 0.72f, 1f);
        internal static readonly Color PinkLight = new Color(1f, 0.62f, 0.86f, 1f);

        // set by OrbCannonBuilder (serialized, so every spawned cannon gets them)
        public MeshRenderer tubeRenderer;
        public int traceSlot = -1;
        public Vector3 muzzleLocal, rearLocal;
        public float boreRadius = 0.1f;
        public Sound shotSound, boomSound, loadSound;
        public bool friendship;              // the Friendship Cannon (loads tumbling players; FriendshipCannon does that part)
        private FriendshipCannon friend;

        private ItemGun gun;
        private PhysGrabObject physGrabObject;
        private PhotonView photonView;
        private State state;
        private Kind kind;
        private float coolUntil;
        private int shotCounter;
        private readonly Dictionary<int, OrbShot> inFlight = new Dictionary<int, OrbShot>();

        // looks
        private Material[] darkMaterials, glowMaterials;
        private Material glowTrace;
        private OrbTubes.Parts tubes;
        private OrbSight sight;
        private const float FlowSpeed = 0.9f;   // tiles per second (a tile is 5 cm of tube)
        private GameObject loadedOrb;
        private Light loadedLight;
        private ParticleSystem smokeMuzzle, smokeRear;
        private float suckUntil;
        private Vector3 suckFrom;

        // the holder's aim, reported to the host (only the holder's game knows where its camera points)
        private float[] reportedAim;
        private float reportedAt, nextReport;
        private const float ReportInterval = 0.1f, ReportMaxAge = 0.6f;
        private static readonly FieldInfo HeldByLocalField = AccessTools.Field(typeof(PhysGrabObject), "heldByLocalPlayer");
        private static readonly FieldInfo SuppressBatteryUIField = AccessTools.Field(typeof(ItemBattery), "suppressBatteryUI");
        private static readonly FieldInfo BatteryVisualField = AccessTools.Field(typeof(ItemBattery), "batteryVisualLogic");
        private ItemBattery battery;

        internal PhysGrabObject PhysGrabObject => physGrabObject;
        internal Vector3 Muzzle => transform.TransformPoint(muzzleLocal);
        internal float BoreRadius => boreRadius * Mathf.Max(0.01f, transform.lossyScale.x);
        private bool IsHost => SemiFunc.IsMasterClientOrSingleplayer();
        private bool HeldByLocal => HeldByLocalField != null && physGrabObject != null && (bool)HeldByLocalField.GetValue(physGrabObject);
        internal PlayerAvatar Holder => ProjectileHits.Holder(physGrabObject);
        internal State CurrentState => state;
        internal Vector3 RearPoint => transform.TransformPoint(rearLocal);

        private void Awake()
        {
            gun = GetComponent<ItemGun>();
            physGrabObject = GetComponent<PhysGrabObject>();
            photonView = GetComponent<PhotonView>();
            battery = GetComponent<ItemBattery>();
            friend = GetComponent<FriendshipCannon>();
            SetupLooks();
        }

        // ---------------- aim ----------------

        private Transform HolderView
        {
            get
            {
                PlayerAvatar p = Holder;
                if (p == null || p.localCamera == null) return null;
                try { return p.localCamera.GetOverrideTransform(); } catch { return null; }
            }
        }

        private float[] LocalAim()
        {
            // the player holding it on this game: their real camera (the player camera's override transform doesn't
            // always follow where they look, which sent the medium and large orbs off target)
            Transform view = HeldByLocal && Camera.main != null ? Camera.main.transform : HolderView;
            Vector3 o = view != null ? view.position : Muzzle;
            Vector3 d = view != null ? view.forward : transform.forward;
            return new[] { o.x, o.y, o.z, d.x, d.y, d.z };
        }

        [PunRPC]
        public void HWPOrbCannonAimRPC(float[] aim, PhotonMessageInfo info)
        {
            PlayerAvatar sender = SemiFunc.PlayerAvatarGetFromPhotonPlayer(info.Sender);
            if (sender == null || sender != Holder || aim == null || aim.Length < 6) return;
            reportedAim = aim;
            reportedAt = Time.time;
        }

        /// <summary>Host: where the holder aims (their own report if recent, else measured here).</summary>
        internal void AimRay(out Vector3 origin, out Vector3 dir)
        {
            bool reported = !HeldByLocal && reportedAim != null && Time.time - reportedAt <= ReportMaxAge;
            float[] a = reported ? reportedAim : LocalAim();
            origin = new Vector3(a[0], a[1], a[2]);
            dir = new Vector3(a[3], a[4], a[5]);
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            dir.Normalize();
        }

        /// <summary>
        /// The crosshair point: the first thing along the aim (up to 80 m), skipping every player's body (the aim starts
        /// inside the holder's head: their own collider used to be the "target", 1.5 m behind the muzzle), this cannon
        /// and anything being held.
        /// </summary>
        internal Vector3 AimPoint(Vector3 origin, Vector3 dir)
        {
            float best = 80f;
            foreach (RaycastHit h in Physics.RaycastAll(origin, dir, 80f, ProjectileHits.Mask, QueryTriggerInteraction.Ignore))
            {
                Collider c = h.collider;
                if (c == null || h.distance >= best) continue;
                if (c.GetComponentInParent<PlayerAvatar>() != null || c.GetComponentInParent<PlayerController>() != null) continue;
                if (LayerMask.LayerToName(c.gameObject.layer).StartsWith("Player")) continue;
                PhysGrabObject pgo = c.GetComponentInParent<PhysGrabObject>();
                if (pgo != null && (pgo == physGrabObject || pgo.grabbed)) continue;
                best = h.distance;
            }
            return origin + dir * best;
        }

        // ---------------- the trigger (host) ----------------

        /// <summary>Host: the trigger was pulled. True = let the game's shot run (only when firing a loaded orb).</summary>
        internal bool HostTrigger()
        {
            AimRay(out Vector3 origin, out Vector3 dir);
            if (friendship) return friend != null && friend.HostTrigger(origin, dir);
            if (state == State.Loaded)
            {
                Vector3 target = AimPoint(origin, dir);
                int id = ++shotCounter;
                if (SemiFunc.IsMultiplayer()) photonView.RPC(nameof(HWPOrbCannonFireRPC), RpcTarget.All, (int)kind, id, target);
                else Fire(kind, id, target);
                SendState(State.Cooling, kind, Muzzle);
                coolUntil = Time.time + Settings.OrbCooldown.Value;
                return true;
            }
            if (state == State.Unloaded) TryLoad(origin, dir);
            return false;   // loading (or nothing to load, or cooling) never fires
        }

        private void TryLoad(Vector3 origin, Vector3 dir)
        {
            EnemyValuable best = null;
            float bestScore = float.MaxValue;
            Vector3 rear = transform.TransformPoint(rearLocal), front = Muzzle;
            float range = Settings.OrbLoadRange.Value;
            foreach (EnemyValuable ev in Object.FindObjectsByType<EnemyValuable>(FindObjectsSortMode.None))
            {
                if (ev == null || !ev.isActiveAndEnabled) continue;
                PhysGrabObject pgo = ev.GetComponent<PhysGrabObject>();
                if (pgo == null || pgo.dead) continue;
                Vector3 c = Centre(ev);
                float r = OrbRadius(ev);
                // near the cannon: the orb's surface within LoadRange of the tube's surface
                Vector3 onAxis = ClosestOnSegment(rear, front, c);
                if (Vector3.Distance(onAxis, c) - r - BoreRadius > range) continue;
                // aimed at: in front of the eye, and the aim line passes through (or just by) the orb
                Vector3 to = c - origin;
                float along = Vector3.Dot(to, dir);
                if (along <= 0f) continue;
                float miss = (to - dir * along).magnitude;
                if (miss > r + 0.12f) continue;
                float score = miss / Mathf.Max(0.05f, r) + along * 0.01f;
                if (score < bestScore) { bestScore = score; best = ev; }
            }
            if (best == null)
            {
                if (Settings.OrbDebugLog.Value) Plugin.Log.LogInfo("Orb Cannon: trigger pulled, but no soul orb in reach along the aim.");
                return;
            }
            Kind k = KindOf(best);
            Vector3 from = Centre(best);
            PhysGrabObject orb = best.GetComponent<PhysGrabObject>();
            // it vanishes on every game at once (switched off), then the game removes it for good; marking it dead and
            // destroying it alone left a frozen, ungrabbable orb behind
            GameObject orbObject = orb != null ? orb.gameObject : best.gameObject;
            PhotonView orbView = orbObject.GetComponent<PhotonView>();
            if (Settings.OrbDebugLog.Value)
                Plugin.Log.LogInfo($"Orb Cannon: eating '{orbObject.name}' (parent '{(orbObject.transform.parent != null ? orbObject.transform.parent.name : "none")}', view {(orbView != null ? orbView.ViewID : -1)}, renderers {orbObject.GetComponentsInChildren<Renderer>(true).Length}).");
            try
            {
                if (SemiFunc.IsMultiplayer() && orbView != null) photonView.RPC(nameof(HWPOrbCannonEatRPC), RpcTarget.All, orbView.ViewID);
                else Vanish(orbObject);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Orb Cannon: hiding the orb failed: {e}"); }
            try
            {
                if (orb != null)
                {
                    orb.dead = true;
                    orb.DestroyPhysGrabObject();   // gone, money and all (no break effects, no orb explosion)
                }
                if (!SemiFunc.IsMultiplayer() && orbObject != null) Destroy(orbObject);   // singleplayer: make sure it goes
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Orb Cannon: removing the orb failed: {e}"); }
            if (Settings.OrbDebugLog.Value) Plugin.Log.LogInfo($"Orb Cannon: loaded a {k} orb ('{best.name}').");
            SendState(State.Loaded, k, from);
        }

        [PunRPC]
        public void HWPOrbCannonEatRPC(int orbViewId, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            PhotonView v = PhotonView.Find(orbViewId);
            if (v != null) Vanish(v.gameObject);
        }

        /// <summary>Every game: the eaten orb disappears at once (switched off; the host's destroy removes it for good).</summary>
        private static void Vanish(GameObject orbObject)
        {
            if (orbObject == null) return;
            foreach (Renderer r in orbObject.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Collider c in orbObject.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (Light l in orbObject.GetComponentsInChildren<Light>(true)) l.enabled = false;
            orbObject.SetActive(false);
        }

        private static Vector3 Centre(Component c)
        {
            Rigidbody rb = c.GetComponent<Rigidbody>();
            return rb != null ? rb.worldCenterOfMass : c.transform.position;
        }

        private static float OrbRadius(EnemyValuable ev)
        {
            float r = 0f;
            foreach (Collider col in ev.GetComponentsInChildren<Collider>())
                if (!col.isTrigger) r = Mathf.Max(r, col.bounds.extents.magnitude / Mathf.Sqrt(3f));
            return r > 0.01f ? r : 0.15f;
        }

        /// <summary>The orb's size: from its name ("... - Small/Medium/Big"), else its explosion's size, else its radius.</summary>
        private static Kind KindOf(EnemyValuable ev)
        {
            string n = ev.name.ToLowerInvariant();
            if (n.Contains("small")) return Kind.Small;
            if (n.Contains("medium")) return Kind.Medium;
            if (n.Contains("big") || n.Contains("large")) return Kind.Large;
            EnemyValuableExplosion x = ev.enemyValuableExplosion != null ? ev.enemyValuableExplosion.GetComponent<EnemyValuableExplosion>() : null;
            if (x != null) return x.size == EnemyValuableExplosion.Size.Small ? Kind.Small : x.size == EnemyValuableExplosion.Size.Medium ? Kind.Medium : Kind.Large;
            float r = OrbRadius(ev);
            return r < 0.15f ? Kind.Small : r < 0.25f ? Kind.Medium : Kind.Large;
        }

        internal static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return a + ab * t;
        }

        // ---------------- state (host decides, everyone sees) ----------------

        /// <summary>Host (FriendshipCannon): the cannon's state, as for orbs.</summary>
        internal void FriendSetState(State s)
        {
            if (s == State.Cooling) coolUntil = Time.time + Settings.OrbCooldown.Value;
            SendState(s, kind, Muzzle);
        }

        private void SendState(State s, Kind k, Vector3 from)
        {
            if (SemiFunc.IsMultiplayer()) photonView.RPC(nameof(HWPOrbCannonStateRPC), RpcTarget.All, (int)s, (int)k, from);
            else ApplyState(s, k, from);
        }

        [PunRPC]
        public void HWPOrbCannonStateRPC(int s, int k, Vector3 from, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            ApplyState((State)s, (Kind)k, from);
        }

        private void ApplyState(State s, Kind k, Vector3 from)
        {
            State was = state;
            state = s;
            kind = k;
            if (s == State.Loaded && was != State.Loaded)
            {
                suckFrom = from;
                suckUntil = Time.time + 0.3f;
                Play(loadSound, Muzzle);
                if (loadedOrb != null) Destroy(loadedOrb);
                loadedOrb = friendship ? null : OrbFx.OrbModel(transform, "Loaded Orb", (int)k, 1f);
            }
            if (s == State.Cooling) coolUntil = Time.time + Settings.OrbCooldown.Value;
            ShowState();
        }

        private void Update()
        {
            // the holder's game reports its aim to the host
            if (HeldByLocal && SemiFunc.IsMultiplayer() && !IsHost && Time.time >= nextReport)
            {
                nextReport = Time.time + ReportInterval;
                try { photonView.RPC(nameof(HWPOrbCannonAimRPC), RpcTarget.MasterClient, LocalAim()); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Orb Cannon aim report failed: {e}"); nextReport = Time.time + 2f; }
            }
            if (IsHost && state == State.Cooling && Time.time >= coolUntil) SendState(State.Unloaded, kind, Muzzle);
            UpdateLooks();
        }

        /// <summary>
        /// No battery bar: the cannon doesn't use one. The HUD bar is suppressed the way the game does it for vehicles
        /// (ItemBattery.suppressBatteryUI), and the little bar over the item is switched off whenever it shows.
        /// </summary>
        private void LateUpdate()
        {
            if (battery == null) return;
            if (SuppressBatteryUIField != null) SuppressBatteryUIField.SetValue(battery, true);
            if (BatteryVisualField != null && BatteryVisualField.GetValue(battery) is Component bar && bar != null && bar.gameObject.activeSelf)
                bar.gameObject.SetActive(false);
        }

        // ---------------- firing (every game) ----------------

        [PunRPC]
        public void HWPOrbCannonFireRPC(int k, int id, Vector3 target, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Fire((Kind)k, id, target);
        }

        private void Fire(Kind k, int id, Vector3 target)
        {
            bool authority = IsHost;
            Vector3 flareDir = target - Muzzle;
            OrbFx.LaserFlare(Muzzle, flareDir.sqrMagnitude > 0.01f ? flareDir : transform.forward, OrbTubes.LiquidPink, OrbTubes.LiquidPinkLight);
            PlayerAvatar shooter = Holder;
            if (Settings.OrbDebugLog.Value && authority)
                Plugin.Log.LogInfo($"Orb Cannon: firing a {k} orb from {Muzzle} at {target} ({Vector3.Distance(Muzzle, target):0.0} m, " +
                                   $"{Vector3.Angle(transform.forward, target - Muzzle):0}° off the barrel).");
            switch (k)
            {
                case Kind.Small:
                {
                    Vector3 from = Muzzle;
                    Vector3 dir = target - from;
                    if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
                    dir.Normalize();
                    OrbFx.Shockwave(from, dir, Settings.OrbConeRange.Value, Settings.OrbConeAngle.Value, Settings.OrbConeSpeed.Value);
                    // the boom twice at once: one sound source tops out at full volume, two add up (about twice as loud)
                    Play(boomSound != null ? boomSound : shotSound, from, 2f);
                    Play(boomSound, from, 2f);
                    if (authority) StartCoroutine(HostShockwave(from, dir, shooter));
                    break;
                }
                case Kind.Medium:
                    StartCoroutine(Burst(id, target, authority, shooter));
                    break;
                default:
                    Launch(id * 16, Kind.Large, target, authority, shooter);
                    break;
            }
        }

        private IEnumerator Burst(int id, Vector3 target, bool authority, PlayerAvatar shooter)
        {
            int n = Settings.OrbBurstShots.Value;
            for (int i = 0; i < n; i++)
            {
                Launch(id * 16 + i, Kind.Medium, target, authority, shooter);
                if (i < n - 1) yield return new WaitForSeconds(Settings.OrbBurstGap.Value);
            }
        }

        private void Launch(int shotId, Kind k, Vector3 target, bool authority, PlayerAvatar shooter)
        {
            Vector3 muzzle = Muzzle;
            Vector3 dir = target - muzzle;
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
            dir.Normalize();
            // the orb appears SpawnAhead in front of the muzzle (unless a wall is closer), clear of the holder
            float ahead = Settings.OrbSpawnAhead.Value;
            if (Physics.Raycast(muzzle, dir, out RaycastHit wall, ahead, SemiFunc.LayerMaskGetVisionObstruct().value, QueryTriggerInteraction.Ignore)
                && wall.collider.GetComponentInParent<PlayerAvatar>() == null && wall.collider.GetComponentInParent<PhysGrabObject>() != physGrabObject)
                ahead = Mathf.Max(0f, wall.distance - 0.05f);
            Vector3 from = muzzle + dir * ahead;
            if ((target - from).sqrMagnitude > 0.04f && Vector3.Dot(target - from, dir) > 0f) dir = (target - from).normalized;
            float speed = PlasmaBolt.Speed * (k == Kind.Large ? Settings.OrbBigSpeedVsPlasma.Value : Settings.OrbBurstSpeedVsPlasma.Value);
            // as big as the game's own orb: the small orb for the burst, the big orb for the large shot
            float radius = k == Kind.Large
                ? (OrbCannonBuilder.OrbRadii[2] > 0.01f ? OrbCannonBuilder.OrbRadii[2] : Mathf.Max(0.06f, BoreRadius * 0.85f))
                : (OrbCannonBuilder.OrbRadii[0] > 0.01f ? OrbCannonBuilder.OrbRadii[0] : 0.055f);
            Play(shotSound, muzzle);
            OrbFx.MuzzleFlash(muzzle, dir, k == Kind.Large ? 1.4f : 0.7f);
            try
            {
                OrbShot shot = OrbShot.Spawn(this, shotId, k, from, Quaternion.LookRotation(dir), speed, radius, authority, shooter);
                if (shot != null) inFlight[shotId] = shot;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Orb Cannon: launching an orb failed: {e}"); }
        }

        /// <summary>
        /// Host: the small orb's shockwave. Everyone in the cone and in sight at the moment it's fired is hit once (not
        /// the holder), each when the wave reaches them (distance / ShockwaveSpeed), if they're still alive then.
        /// </summary>
        private IEnumerator HostShockwave(Vector3 from, Vector3 dir, PlayerAvatar shooter)
        {
            float range = Settings.OrbConeRange.Value;
            float half = Settings.OrbConeAngle.Value / 2f;
            int damage = Settings.OrbConeDamage.Value;
            float speed = Mathf.Max(1f, Settings.OrbConeSpeed.Value);
            var done = new HashSet<Object>();
            var hits = new List<(float distance, Collider collider, Enemy enemy, PlayerAvatar player)>();
            foreach (Collider c in Physics.OverlapSphere(from, range, ProjectileHits.Mask | LayerMask.GetMask("PhysGrabObject"), QueryTriggerInteraction.Collide))
            {
                if (c == null || c.GetComponentInParent<PhysGrabObject>() == physGrabObject) continue;
                EnemyRigidbody erb = c.GetComponentInParent<EnemyRigidbody>();
                Enemy enemy = erb != null ? erb.enemy : c.GetComponentInParent<Enemy>();
                if (enemy == null && (c.GetComponentInParent<ItemAttributes>() != null || c.GetComponentInParent<ValuableObject>() != null
                                      || c.GetComponentInParent<PlayerAvatar>() != null)) continue;
                Object key = enemy != null ? (Object)enemy : (Object)c.transform.root;
                if (done.Contains(key)) continue;
                Vector3 at = enemy != null && enemy.CenterTransform != null ? enemy.CenterTransform.position : c.ClosestPoint(from);
                if (!InCone(from, dir, at, range, half)) continue;
                if (Blocked(from, at) && Blocked(from, c.ClosestPoint(from))) continue;
                done.Add(key);
                hits.Add((Vector3.Distance(from, at), c, enemy, null));
            }
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (p == shooter || !ProjectileHits.Alive(p) || !p.gameObject.activeInHierarchy) continue;
                Vector3 at = p.transform.position + Vector3.up * 0.9f;
                if (!InCone(from, dir, at, range + 0.3f, half)) continue;
                if (Blocked(from, at)) continue;
                hits.Add((Vector3.Distance(from, at), null, null, p));
            }
            hits.Sort((x, y) => x.distance.CompareTo(y.distance));
            if (Settings.OrbDebugLog.Value) Plugin.Log.LogInfo($"Orb Cannon: the shockwave will reach {hits.Count} target(s).");
            float start = Time.time;
            foreach (var h in hits)
            {
                float due = start + h.distance / speed;
                while (Time.time < due) yield return null;
                if (h.player != null)
                {
                    if (ProjectileHits.Alive(h.player)) ProjectileHits.HurtPlayer(h.player, damage, h.player.transform.position + Vector3.up * 0.9f);
                    continue;
                }
                if (h.collider == null) continue;
                if (h.enemy != null)
                {
                    // the game's own hurt sphere, just on the cannon's side of the enemy: its damage and knockback
                    // (pushed away from the muzzle), like the pulse grenade's
                    Vector3 near = h.collider.ClosestPoint(from);
                    Vector3 push = (near - from).sqrMagnitude > 1e-4f ? (near - from).normalized : dir;
                    RocketExplosion.EnemyHit(near - push * 0.25f, 0.45f, damage, Settings.OrbConePush.Value);
                    if (Settings.OrbDebugLog.Value) Plugin.Log.LogInfo($"Orb Cannon: the shockwave hit '{h.enemy.name}' at {h.distance:0.0} m.");
                }
                else ProjectileHits.HurtEnemy(h.collider, damage, (h.collider.bounds.center - from).normalized);   // a TargetDummy, if any
            }
        }

        /// <summary>
        /// Is the line from <paramref name="from"/> to <paramref name="to"/> blocked by the level? Enemies, players,
        /// items and valuables never block it (an enemy's own body used to block the line to its centre).
        /// </summary>
        private bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.05f) return false;
            foreach (RaycastHit h in Physics.RaycastAll(from, d / len, len - 0.02f, SemiFunc.LayerMaskGetVisionObstruct().value, QueryTriggerInteraction.Ignore))
            {
                Collider c = h.collider;
                if (c == null) continue;
                if (c.GetComponentInParent<EnemyRigidbody>() != null || c.GetComponentInParent<Enemy>() != null) continue;
                if (c.GetComponentInParent<PlayerAvatar>() != null || c.GetComponentInParent<PlayerController>() != null) continue;
                if (c.GetComponentInParent<PhysGrabObject>() != null) continue;   // items, valuables, this cannon
                return true;
            }
            return false;
        }

        private static bool InCone(Vector3 from, Vector3 dir, Vector3 at, float range, float halfAngle)
        {
            Vector3 to = at - from;
            float d = to.magnitude;
            if (d > range) return false;
            if (d < 0.3f) return true;
            return Vector3.Angle(dir, to) <= halfAngle;
        }

        /// <summary>Host only: an orb hit something (or burst in the air).</summary>
        internal void HostImpact(int shotId, Kind k, Vector3 position)
        {
            if (SemiFunc.IsMultiplayer()) photonView.RPC(nameof(HWPOrbCannonImpactRPC), RpcTarget.All, shotId, (int)k, position);
            else Impact(shotId, k, position);
        }

        [PunRPC]
        public void HWPOrbCannonImpactRPC(int shotId, int k, Vector3 position, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Impact(shotId, (Kind)k, position);
        }

        private void Impact(int shotId, Kind k, Vector3 position)
        {
            if (inFlight.TryGetValue(shotId, out OrbShot shot))
            {
                inFlight.Remove(shotId);
                if (shot != null) Destroy(shot.gameObject);
            }
            if (k == Kind.Large)
            {
                OrbFx.PlasmaExplosion(position, RocketExplosion.SplashRadius);
                RocketExplosion.SpawnBlastOnly(position);   // the Rocket Launcher's blast damage (hurts players too), no vanilla look
            }
            else OrbFx.Burst(position, 0.45f);
        }

        internal void Forget(int shotId) => inFlight.Remove(shotId);

        internal static void Play(Sound s, Vector3 at, float reach = 1f)
        {
            if (s == null) return;
            try { s.Play(at, 1f, reach, 1f, reach); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Orb Cannon sound failed: {e}"); }
        }

        private void OnDestroy()
        {
            foreach (var s in inFlight.Values) if (s != null) Destroy(s.gameObject);
            inFlight.Clear();
            if (glowTrace != null) Destroy(glowTrace);
            if (tubes != null) { Destroy(tubes.Glow); Destroy(tubes.Dark); Destroy(tubes.Glass); }
            if (sight != null) sight.Cleanup();
        }

        // ---------------- looks (every game) ----------------

        private void SetupLooks()
        {
            // the traces are now round glass tubes with a liquid core (OrbTubes) laid over the model's flat traces, which
            // stay dark underneath; the old glow swap of the flat traces is off
            if (tubeRenderer != null)
            {
                Material metal = null;
                foreach (Material m in tubeRenderer.sharedMaterials)
                    if (m != null && m.name.Contains("Metal")) { metal = m; break; }
                try
                {
                    tubes = friendship ? OrbTubes.Attach(tubeRenderer.transform, metal, OrbTubes.LiquidBlue, OrbTubes.LiquidBlueLight)
                                       : OrbTubes.Attach(tubeRenderer.transform, metal);
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Orb Cannon: building the liquid tubes failed: {e}"); tubes = null; }
                try { sight = OrbSight.Create(tubeRenderer.transform, metal, friendship); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Orb Cannon: building the sight screen failed: {e}"); sight = null; }
                if (tubes == null && traceSlot >= 0 && traceSlot < tubeRenderer.sharedMaterials.Length)
                {
                    // fallback: the old glowing flat traces
                    darkMaterials = tubeRenderer.sharedMaterials;
                    glowMaterials = (Material[])darkMaterials.Clone();
                    Shader unlit = Shader.Find("Sprites/Default");
                    if (unlit != null)
                    {
                        glowTrace = new Material(unlit) { name = "HWP Orb Cannon Trace Glow", color = Pink };
                        glowMaterials[traceSlot] = glowTrace;
                    }
                    else glowMaterials = null;
                }
            }

            // the loaded orb, sitting in the muzzle
            loadedOrb = null;   // made when an orb is loaded (the game's orb of that size)
            var lightGo = new GameObject("Loaded Orb Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = muzzleLocal;
            loadedLight = lightGo.AddComponent<Light>();
            loadedLight.type = LightType.Point;
            loadedLight.color = friendship ? OrbTubes.LiquidBlue : Pink;
            loadedLight.range = 2.5f;
            loadedLight.intensity = 0f;
            loadedLight.shadows = LightShadows.None;

            smokeMuzzle = OrbFx.Smoke(transform, muzzleLocal + new Vector3(0f, 0f, 0.02f), Vector3.forward);
            smokeRear = OrbFx.Smoke(transform, rearLocal - new Vector3(0f, 0f, 0.02f), Vector3.back);
        }

        private void ShowState()
        {
            bool loaded = state == State.Loaded;
            if (tubeRenderer != null && darkMaterials != null && glowMaterials != null)
                tubeRenderer.sharedMaterials = loaded ? glowMaterials : darkMaterials;
            if (tubes != null && tubes.Core != null)
            {
                tubes.Core.sharedMaterial = loaded ? tubes.Glow : tubes.Dark;
                if (loaded) OrbTubes.SetLit(tubes, 0f); else OrbTubes.SetUnlit(tubes);
            }
            if (sight != null)
            {
                if (friendship) sight.ShowHead(state == State.Loaded && friend != null ? friend.Loaded : null, state == State.Cooling);
                else sight.Show(state == State.Loaded ? (int)kind : state == State.Cooling ? -2 : -1);
            }
            if (loadedOrb != null) loadedOrb.SetActive(loaded);
            if (!loaded && loadedLight != null) loadedLight.intensity = 0f;
            SetSmoke(state == State.Cooling);
        }

        private void SetSmoke(bool on)
        {
            foreach (ParticleSystem ps in new[] { smokeMuzzle, smokeRear })
            {
                if (ps == null) continue;
                var em = ps.emission;
                em.enabled = on;
                if (on && !ps.isPlaying) ps.Play();
            }
        }

        private void UpdateLooks()
        {
            if (state == State.Loaded)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.2f);
                if (glowTrace != null) glowTrace.color = Color.Lerp(Pink, PinkLight, pulse * 0.6f);
                if (tubes != null && tubes.Glow != null)
                {
                    // the liquid flows towards the muzzle, its brightness breathing with the loaded orb
                    tubes.Glow.mainTextureOffset = new Vector2(-Time.time * FlowSpeed, 0f);
                    OrbTubes.SetLit(tubes, pulse * 0.35f);
                }
                if (loadedOrb != null)
                {
                    float size = BoreRadius * (kind == Kind.Small ? 1.4f : kind == Kind.Medium ? 1.65f : 1.85f) / Mathf.Max(0.01f, transform.lossyScale.x);
                    Vector3 home = muzzleLocal - new Vector3(0f, 0f, size * 0.45f);
                    Transform t = loadedOrb.transform;
                    if (Time.time < suckUntil)
                    {
                        // flying in from where the orb was
                        float k = 1f - (suckUntil - Time.time) / 0.3f;
                        Vector3 start = transform.InverseTransformPoint(suckFrom);
                        t.localPosition = Vector3.Lerp(start, home, k * k);
                        t.localScale = Vector3.one * size * Mathf.Lerp(1.5f, 1f, k);
                    }
                    else
                    {
                        t.localPosition = home;
                        t.localScale = Vector3.one * size * (0.95f + 0.05f * pulse);
                    }
                }
                if (loadedLight != null) loadedLight.intensity = 1.5f + 1f * pulse;
            }
            else if (state == State.Cooling && Time.time >= coolUntil + 0.5f && !IsHost)
            {
                SetSmoke(false);   // a client that missed the "unloaded" message stops smoking anyway
            }
        }
    }
}
