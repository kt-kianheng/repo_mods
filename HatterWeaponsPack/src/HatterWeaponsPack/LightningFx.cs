using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>Finding, stunning and hurting enemies for the Lightning Wand (the game keeps most of this internal).</summary>
    internal static class LightningTargets
    {
        private static readonly FieldInfo SpawnedField = AccessTools.Field(typeof(EnemyParent), "Spawned");
        private static readonly FieldInfo ParentEnemyField = AccessTools.Field(typeof(EnemyParent), "Enemy");
        private static readonly FieldInfo ViewField = AccessTools.Field(typeof(Enemy), "PhotonView");
        private static readonly FieldInfo HealthField = AccessTools.Field(typeof(Enemy), "Health");
        private static readonly FieldInfo HasStunField = AccessTools.Field(typeof(Enemy), "HasStateStunned");
        private static readonly FieldInfo StunField = AccessTools.Field(typeof(Enemy), "StateStunned");
        private static readonly FieldInfo RigidbodyField = AccessTools.Field(typeof(Enemy), "Rigidbody");
        private static readonly FieldInfo DeadField = AccessTools.Field(typeof(EnemyHealth), "dead");

        /// <summary>Every spawned, living enemy.</summary>
        internal static List<Enemy> Alive()
        {
            var list = new List<Enemy>();
            EnemyDirector director = EnemyDirector.instance;
            if (director == null || director.enemiesSpawned == null) return list;
            foreach (EnemyParent p in director.enemiesSpawned)
            {
                if (p == null) continue;
                if (SpawnedField != null && !(bool)SpawnedField.GetValue(p)) continue;
                Enemy e = (ParentEnemyField != null ? ParentEnemyField.GetValue(p) as Enemy : null) ?? p.GetComponentInChildren<Enemy>();
                if (e == null || !e.gameObject.activeInHierarchy || IsDead(e)) continue;
                list.Add(e);
            }
            return list;
        }

        internal static EnemyHealth Health(Enemy e)
        {
            if (e == null) return null;
            return (HealthField != null ? HealthField.GetValue(e) as EnemyHealth : null) ?? e.GetComponentInChildren<EnemyHealth>();
        }

        internal static bool IsDead(Enemy e)
        {
            EnemyHealth h = Health(e);
            return h != null && DeadField != null && (bool)DeadField.GetValue(h);
        }

        /// <summary>Dead, despawned or destroyed: effects on it should clear away.</summary>
        internal static bool Gone(Enemy e) => e == null || !e.gameObject.activeInHierarchy || IsDead(e);

        /// <summary>The middle of the enemy's body (what the bolts aim at).</summary>
        internal static Transform CentreTransform(Enemy e)
        {
            if (e == null) return null;
            return e.CenterTransform != null ? e.CenterTransform : e.transform;
        }

        internal static Vector3 Centre(Enemy e) => CentreTransform(e).position;

        internal static string Name(Enemy e)
        {
            if (e == null) return "?";
            EnemyParent p = e.GetComponentInParent<EnemyParent>();
            return p != null && !string.IsNullOrEmpty(p.enemyName) ? p.enemyName : e.name;
        }

        internal static int ViewId(Enemy e)
        {
            PhotonView v = ViewField != null ? ViewField.GetValue(e) as PhotonView : null;
            if (v == null) v = e.GetComponentInParent<PhotonView>();
            return v != null ? v.ViewID : 0;
        }

        internal static Enemy FromViewId(int id)
        {
            if (id <= 0) return null;
            PhotonView v = PhotonView.Find(id);
            if (v == null) return null;
            return v.GetComponent<Enemy>() ?? v.GetComponentInChildren<Enemy>() ?? v.GetComponentInParent<Enemy>();
        }

        internal static Enemy Of(Collider c)
        {
            if (c == null) return null;
            EnemyRigidbody erb = c.GetComponentInParent<EnemyRigidbody>();
            return erb != null ? erb.enemy : c.GetComponentInParent<Enemy>();
        }

        /// <summary>The colliders of the enemy's body (for the size of the electrocuted effect and line-of-sight checks).</summary>
        internal static Component Body(Enemy e)
        {
            Component rb = RigidbodyField != null ? RigidbodyField.GetValue(e) as Component : null;
            return rb != null ? rb : e;
        }

        /// <summary>World bounds of the enemy's solid body colliders (a guess round its centre if it has none).</summary>
        internal static bool BodyBounds(Enemy e, out Bounds bounds)
        {
            bounds = new Bounds(e != null ? Centre(e) : Vector3.zero, new Vector3(0.7f, 1f, 0.7f));
            if (e == null) return false;
            bool any = false;
            foreach (Collider c in Body(e).GetComponentsInChildren<Collider>())
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds);
            }
            return any;
        }

        /// <summary>Host: stun for this long (the game only ever lengthens a running stun).</summary>
        internal static void Stun(Enemy e, float seconds)
        {
            if (e == null || seconds <= 0f) return;
            try
            {
                if (HasStunField != null && !(bool)HasStunField.GetValue(e)) return;
                EnemyStateStunned stun = StunField != null ? StunField.GetValue(e) as EnemyStateStunned : null;
                if (stun != null) stun.Set(seconds);
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"Lightning stun failed: {ex}"); }
        }

        /// <summary>Host: ends a running stun now (fire thawed the enemy).</summary>
        internal static void EndStun(Enemy e)
        {
            if (e == null) return;
            try
            {
                if (HasStunField != null && !(bool)HasStunField.GetValue(e)) return;
                EnemyStateStunned stun = StunField != null ? StunField.GetValue(e) as EnemyStateStunned : null;
                if (stun != null && stun.stunTimer > 0.05f) stun.stunTimer = 0.05f;
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"Ending the stun failed: {ex}"); }
        }

        /// <summary>Host: damage (the game syncs enemy health from the host).</summary>
        internal static void Hurt(Enemy e, int damage, Vector3 direction)
        {
            if (e == null || damage <= 0 || IsDead(e)) return;
            try { Health(e)?.Hurt(damage, direction); }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"Lightning damage failed: {ex}"); }
        }

        /// <summary>Charges shared out evenly; the first enemies take whatever doesn't divide (4 over 3 = 2, 1, 1).</summary>
        internal static int[] Split(int charges, int targets)
        {
            var result = new int[targets];
            for (int i = 0; i < targets; i++) result[i] = charges / targets + (i < charges % targets ? 1 : 0);
            return result;
        }

        /// <summary>
        /// The enemy under the crosshair (or the one closest to it within the cone), then the nearest enemy within
        /// chainRange of the last one, and so on, each needing a clear line from the one before.
        /// </summary>
        internal static List<Enemy> Pick(Vector3 origin, Vector3 dir, float range, float cone, float chainRange, int max,
                                         PhysGrabObject self, PlayerAvatar caster, out Vector3 missPoint)
        {
            var result = new List<Enemy>();
            List<Enemy> alive = Alive();
            missPoint = origin + dir * range;

            // whatever is right under the crosshair
            Enemy first = null;
            float nearest = range;
            foreach (RaycastHit h in Physics.RaycastAll(origin, dir, range, ProjectileHits.Mask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.distance >= nearest) continue;
                if (self != null && h.collider.GetComponentInParent<PhysGrabObject>() == self) continue;
                if (caster != null && h.collider.GetComponentInParent<PlayerAvatar>() == caster) continue;
                nearest = h.distance;
                missPoint = h.point;
                first = Of(h.collider);
            }
            if (first != null && !alive.Contains(first)) first = null;

            // otherwise the enemy closest to the crosshair, in range and in plain sight
            if (first == null)
            {
                float bestAngle = cone;
                foreach (Enemy e in alive)
                {
                    Vector3 c = Centre(e), to = c - origin;
                    if (to.magnitude > range) continue;
                    float angle = Vector3.Angle(dir, to);
                    if (angle > bestAngle || !Clear(origin, c, e, null, self, caster, false)) continue;
                    first = e; bestAngle = angle;
                }
            }
            if (first == null) return result;
            result.Add(first);

            while (result.Count < max)
            {
                Enemy from = result[result.Count - 1];
                Vector3 a = Centre(from);
                Enemy next = null;
                float best = chainRange;
                foreach (Enemy e in alive)
                {
                    if (result.Contains(e)) continue;
                    float d = Vector3.Distance(a, Centre(e));
                    if (d > best || !CanJump(from, e)) continue;
                    next = e; best = d;
                }
                if (next == null) break;
                result.Add(next);
            }
            return result;
        }

        /// <summary>The enemy and then the nearest enemy within chainRange of the last one, and so on, along clear lines.</summary>
        internal static List<Enemy> ChainFrom(Enemy first, float chainRange, int max)
        {
            var result = new List<Enemy>();
            if (first == null) return result;
            result.Add(first);
            List<Enemy> alive = Alive();
            while (result.Count < max)
            {
                Enemy from = result[result.Count - 1];
                Vector3 a = Centre(from);
                Enemy next = null;
                float best = chainRange;
                foreach (Enemy e in alive)
                {
                    if (result.Contains(e)) continue;
                    float d = Vector3.Distance(a, Centre(e));
                    if (d > best || !CanJump(from, e)) continue;
                    next = e; best = d;
                }
                if (next == null) break;
                result.Add(next);
            }
            return result;
        }

        /// <summary>
        /// Enemies on the caster's screen: within range, within halfAngle of where they look, and in plain sight;
        /// closest to the crosshair first.
        /// </summary>
        internal static List<Enemy> OnScreen(Vector3 origin, Vector3 dir, float range, float halfAngle, PhysGrabObject self, PlayerAvatar caster)
        {
            var found = new List<(Enemy e, float angle)>();
            foreach (Enemy e in Alive())
            {
                Vector3 c = Centre(e), to = c - origin;
                if (to.magnitude > range) continue;
                float angle = Vector3.Angle(dir, to);
                if (angle > halfAngle || !Clear(origin, c, e, null, self, caster, false)) continue;
                found.Add((e, angle));
            }
            found.Sort((a, b) => a.angle.CompareTo(b.angle));
            var result = new List<Enemy>();
            foreach (var f in found) result.Add(f.e);
            return result;
        }

        /// <summary>The nearest enemy within range of a point (the Lightning Staff's orb), in plain sight of it.</summary>
        internal static Enemy NearestFrom(Vector3 point, float range)
        {
            Enemy best = null;
            float bestDistance = range;
            foreach (Enemy e in Alive())
            {
                Vector3 c = Centre(e);
                float d = Vector3.Distance(point, c);
                if (d > bestDistance || !Clear(point, c, e, null, null, null, true)) continue;
                best = e; bestDistance = d;
            }
            return best;
        }

        /// <summary>
        /// True if the bolt can jump from one enemy to the other right now: both there, the target alive, and a clear
        /// line of sight between their centres. Walls, doors, props, players and any other enemy block it.
        /// </summary>
        internal static bool CanJump(Enemy from, Enemy to)
        {
            if (from == null || to == null || IsDead(to) || !to.gameObject.activeInHierarchy) return false;
            return Clear(Centre(from), Centre(to), to, from, null, null, true);
        }

        /// <summary>
        /// Nothing between a and b except the two enemies themselves (and, for the caster's own line, the caster and
        /// the wand). With <paramref name="enemiesBlock"/>, any other enemy in the way blocks it too.
        /// </summary>
        private static bool Clear(Vector3 a, Vector3 b, Enemy target, Enemy from, PhysGrabObject self, PlayerAvatar caster, bool enemiesBlock)
        {
            Vector3 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) return true;
            int mask = SemiFunc.LayerMaskGetVisionObstruct().value;
            if (enemiesBlock) mask |= LayerMask.GetMask("Enemy");
            foreach (RaycastHit h in Physics.RaycastAll(a, d / length, length, mask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null) continue;
                Enemy owner = Of(h.collider);
                if (owner != null && (owner == target || owner == from)) continue;
                if (self != null && h.collider.GetComponentInParent<PhysGrabObject>() == self) continue;
                if (caster != null && h.collider.GetComponentInParent<PlayerAvatar>() == caster) continue;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// One Lightning Wand bolt, on every client: the tip strikes the first enemy, then every ChainDelay seconds the
    /// host checks for a clear line of sight from the last enemy struck to the next. If clear, the bolt jumps there
    /// (shown on everyone's game by RPC); if not, the bolt ends and the remaining enemies are spared.
    /// Every struck enemy is electrocuted for StunSeconds. The host's copy also stuns them and, when the stun ends,
    /// deals DamagePerCharge x charges (at most MaxDamageCharges of them). Lives on its own, so dropping the wand
    /// changes nothing.
    /// </summary>
    internal class LightningChain : MonoBehaviour
    {
        internal class Target
        {
            public Enemy Enemy;
            public int Charges;
            public Vector3 Position;   // the host's position at the cast; kept up to date while the enemy is known
            public bool Struck;
            public bool Cancelled;     // the bolt never reached it
            public float StruckAt;
            public bool Damaged;
        }

        private const float ClientWait = 3f;   // a client gives up waiting for the host's word on a jump after this

        /// <summary>Ways a chain can differ from the Lightning Wand's (null: the wand's own).</summary>
        internal class Options
        {
            public float StartDelay;            // the first strike comes this long after the chain is made
            public bool InstantDamage;          // damage on the strike instead of when the stun ends
            public int MaxDamageCharges = -1;   // charges counted for damage (-1: the wand's MaxDamageCharges)
            public int DamagePerCharge = -1;    // (-1: the wand's ChargeDamage)
        }

        internal int Id { get; private set; }
        private LightningWand wand;
        private Transform tip;
        private Vector3 tipPosition;
        private List<Target> targets;
        private bool authority;
        private Sound zap, buzz;
        private float age, delay, stunSeconds, boltSeconds, nextRestun;
        private int damagePerCharge, maxDamageCharges;
        private bool instantDamage;

        internal static LightningChain Spawn(LightningWand wand, int id, Transform tip, List<Target> targets, bool authority, Sound zap, Sound buzz,
                                             Options options = null)
        {
            var go = new GameObject("HWP Lightning Chain");
            var chain = go.AddComponent<LightningChain>();
            chain.Setup(wand, id, tip, targets, authority, zap, buzz, options);
            return chain;
        }

        /// <summary>A harmless bolt to a point (nothing to strike).</summary>
        internal static void SpawnMiss(Transform tip, Vector3 end, Sound zap)
        {
            LightningBolt.Spawn(tip, tip != null ? tip.position : end, null, end, Settings.LightningBoltSeconds.Value);
            ShockEffect.Create(null, end, Vector3.one * 0.12f, 0.35f, null, 1f);
            PlayAt(zap, end);
        }

        private void Setup(LightningWand wand, int id, Transform tip, List<Target> targets, bool authority, Sound zap, Sound buzz,
                           Options options)
        {
            this.wand = wand;
            Id = id;
            this.tip = tip;
            tipPosition = tip != null ? tip.position : transform.position;
            this.targets = targets;
            this.authority = authority;
            this.zap = zap;
            this.buzz = buzz;
            delay = Settings.LightningChainDelay.Value;
            stunSeconds = Settings.LightningStunSeconds.Value;
            boltSeconds = Settings.LightningBoltSeconds.Value;
            damagePerCharge = Settings.LightningDamagePerCharge.Value;
            maxDamageCharges = Mathf.Max(1, Settings.LightningMaxDamageCharges.Value);
            if (options != null)
            {
                age = -Mathf.Max(0f, options.StartDelay);
                instantDamage = options.InstantDamage;
                if (options.MaxDamageCharges > 0) maxDamageCharges = options.MaxDamageCharges;
                if (options.DamagePerCharge >= 0) damagePerCharge = options.DamagePerCharge;
            }
            Update();
        }

        /// <summary>Client: the host says the bolt jumped to this enemy.</summary>
        internal void RemoteJump(int index)
        {
            if (authority || index <= 0 || index >= targets.Count) return;
            for (int i = 1; i <= index; i++)
                if (!targets[i].Struck && !targets[i].Cancelled) Strike(i);
        }

        /// <summary>Client: the host says the bolt ended before this enemy.</summary>
        internal void RemoteStop(int index)
        {
            if (authority) return;
            Cancel(index);
        }

        private void Cancel(int from)
        {
            for (int i = Mathf.Max(0, from); i < targets.Count; i++)
                if (!targets[i].Struck) targets[i].Cancelled = true;
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (tip != null) tipPosition = tip.position;
            foreach (Target t in targets)
                if (t.Enemy != null) t.Position = LightningTargets.Centre(t.Enemy);

            bool done = true;
            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                if (t.Cancelled) continue;
                if (!t.Struck)
                {
                    if (i == 0)
                    {
                        if (age < 0f) { done = false; continue; }   // a staggered strike (Lightning Staff) not due yet
                        Strike(0);                                    // the host checked the caster's line at the cast
                    }
                    else if (age < i * delay) { done = false; continue; }
                    else if (authority) HostTryJump(i);
                    else if (age > i * delay + ClientWait) { Cancel(i); continue; }
                    else { done = false; continue; }
                    if (!t.Struck) continue;                     // the jump was blocked
                }

                float stunLeft = t.StruckAt + stunSeconds - age;
                if (authority && !t.Damaged)
                {
                    if (stunLeft > 0f)
                    {
                        // keep the stun going in case something cut it short (Set never shortens a stun)
                        if (age >= nextRestun) LightningTargets.Stun(t.Enemy, stunLeft);
                        done = false;
                    }
                    else
                    {
                        t.Damaged = true;
                        int counted = Mathf.Min(t.Charges, maxDamageCharges);
                        int damage = damagePerCharge * counted;
                        Vector3 from = i == 0 ? tipPosition : targets[i - 1].Position;
                        LightningTargets.Hurt(t.Enemy, damage, (t.Position - from).normalized);
                        Plugin.Log.LogInfo($"Lightning Wand: {LightningTargets.Name(t.Enemy)} takes {damage} " +
                                           $"({t.Charges} charge{(t.Charges == 1 ? "" : "s")}, {counted} counted).");
                    }
                }
                else if (!authority && stunLeft > 0f) done = false;
            }
            if (authority && age >= nextRestun) nextRestun = age + 0.25f;
            if (done) Destroy(gameObject);
        }

        /// <summary>Host: jump to target i only along a clear line of sight from the enemy struck before it.</summary>
        private void HostTryJump(int i)
        {
            Target prev = targets[i - 1], t = targets[i];
            if (prev.Struck && LightningTargets.CanJump(prev.Enemy, t.Enemy))
            {
                Strike(i);
                if (wand != null) wand.HostJumped(Id, i);
                return;
            }
            Plugin.Log.LogInfo($"Lightning Wand: no clear line from {LightningTargets.Name(prev.Enemy)} to {LightningTargets.Name(t.Enemy)}; the bolt ends.");
            Cancel(i);
            if (wand != null) wand.HostStopped(Id, i);
        }

        private void Strike(int i)
        {
            Target t = targets[i];
            t.Struck = true;
            t.StruckAt = age;

            Transform fromT = i == 0 ? tip : LightningTargets.CentreTransform(targets[i - 1].Enemy);
            Vector3 fromP = i == 0 ? tipPosition : targets[i - 1].Position;
            Transform toT = LightningTargets.CentreTransform(t.Enemy);
            LightningBolt.Spawn(fromT, fromP, toT, t.Position, boltSeconds);
            ShockEffect.OnEnemy(t.Enemy, t.Position, stunSeconds, buzz);
            PlayAt(zap, t.Position);

            if (!authority) return;
            LightningTargets.Stun(t.Enemy, stunSeconds);
            if (instantDamage)
            {
                // the Lightning Staff: damage lands with the strike (the stun still applies)
                t.Damaged = true;
                int counted = Mathf.Min(t.Charges, maxDamageCharges);
                int damage = damagePerCharge * counted;
                LightningTargets.Hurt(t.Enemy, damage, (t.Position - fromP).normalized);
                Plugin.Log.LogInfo($"Lightning Staff: {LightningTargets.Name(t.Enemy)} takes {damage} ({t.Charges} charge{(t.Charges == 1 ? "" : "s")}, {counted} counted).");
            }
        }

        private void OnDestroy()
        {
            if (wand != null) wand.Forget(this);
        }

        private static void PlayAt(Sound s, Vector3 position)
        {
            if (s == null) return;
            try { s.Play(position, 1f, 1f, 1f, 1f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Lightning sound failed: {e}"); }
        }
    }

    /// <summary>Shared look of the lightning: jagged paths and glowing lines.</summary>
    internal static class LightningLook
    {
        internal static readonly Color Glow = new Color(1f, 0.85f, 0.1f, 0.5f);
        internal static readonly Color Core = new Color(1f, 0.98f, 0.75f, 1f);

        internal static LineRenderer Line(Transform parent, string name, float width, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var lr = obj.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 0;
            if (FireballFx.Glow != null) lr.sharedMaterial = FireballFx.Glow;
            lr.startColor = color; lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>A lightning path from a to b: midpoint displacement, sharper corners the shorter the bits get.</summary>
        internal static void Jag(List<Vector3> points, Vector3 a, Vector3 b, float roughness)
        {
            points.Clear();
            points.Add(a); points.Add(b);
            float length = Vector3.Distance(a, b);
            if (length < 0.001f) return;
            Vector3 dir = (b - a) / length;
            int levels = Mathf.Clamp(Mathf.CeilToInt(Mathf.Log(length / 0.12f, 2f)), 1, 6);
            float spread = length * roughness;
            var next = new List<Vector3>();
            for (int level = 0; level < levels; level++)
            {
                next.Clear();
                for (int i = 0; i < points.Count - 1; i++)
                {
                    Vector3 p = points[i], q = points[i + 1];
                    Vector3 off = Vector3.ProjectOnPlane(Random.insideUnitSphere, dir) * spread;
                    next.Add(p);
                    next.Add((p + q) * 0.5f + off);
                }
                next.Add(points[points.Count - 1]);
                points.Clear();
                points.AddRange(next);
                spread *= 0.5f;
            }
        }

        internal static void Apply(LineRenderer lr, List<Vector3> points)
        {
            lr.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) lr.SetPosition(i, points[i]);
        }
    }

    /// <summary>
    /// A yellow lightning bolt between two points (each can follow a transform), like the Healing Wand's beam but jagged:
    /// a glow and a bright core re-drawn every few hundredths of a second, with a flickering side fork.
    /// </summary>
    internal class LightningBolt : MonoBehaviour
    {
        private const float Redraw = 0.045f;
        private Transform fromT, toT;
        private Vector3 from, to;
        private float duration, age, nextDraw;
        private LineRenderer glow, core, fork;
        private Light startLight, endLight;
        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<Vector3> forkPoints = new List<Vector3>();

        internal static void Spawn(Transform fromT, Vector3 from, Transform toT, Vector3 to, float duration)
        {
            var go = new GameObject("HWP Lightning Bolt");
            var bolt = go.AddComponent<LightningBolt>();
            bolt.fromT = fromT; bolt.from = from; bolt.toT = toT; bolt.to = to;
            bolt.duration = Mathf.Max(0.05f, duration);
            bolt.Build();
        }

        private void Build()
        {
            glow = LightningLook.Line(transform, "Glow", 0.1f, LightningLook.Glow);
            core = LightningLook.Line(transform, "Core", 0.03f, LightningLook.Core);
            fork = LightningLook.Line(transform, "Fork", 0.02f, LightningLook.Core);
            startLight = MakeLight("Start Light", 2f);
            endLight = MakeLight("End Light", 3f);
            LateUpdate();
        }

        private Light MakeLight(string name, float range)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var l = obj.AddComponent<Light>();
            l.type = LightType.Point; l.color = LightningWand.Yellow; l.range = range; l.intensity = 0f;
            return l;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= duration) { Destroy(gameObject); return; }
            if (fromT != null) from = fromT.position;
            if (toT != null) to = toT.position;

            if (age >= nextDraw)
            {
                nextDraw = age + Redraw;
                LightningLook.Jag(points, from, to, 0.12f);
                LightningLook.Apply(glow, points);
                LightningLook.Apply(core, points);

                // a short side fork from somewhere along the bolt, now and then
                if (points.Count > 4 && Random.value < 0.6f)
                {
                    Vector3 start = points[Random.Range(1, points.Count - 2)];
                    float len = Mathf.Min(0.8f, Vector3.Distance(from, to) * Random.Range(0.12f, 0.3f));
                    Vector3 dir = ((to - from).normalized + Random.insideUnitSphere * 0.9f).normalized;
                    LightningLook.Jag(forkPoints, start, start + dir * len, 0.25f);
                    LightningLook.Apply(fork, forkPoints);
                    fork.enabled = true;
                }
                else fork.enabled = false;
            }

            // full strength, flickering, then fading out over the last 40%
            float fade = Mathf.Clamp01((duration - age) / (duration * 0.4f));
            float flicker = Random.Range(0.75f, 1.15f);
            glow.widthMultiplier = 0.1f * fade * flicker;
            core.widthMultiplier = 0.03f * fade * flicker;
            fork.widthMultiplier = 0.018f * fade;
            startLight.transform.position = from; startLight.intensity = 2f * fade * flicker;
            endLight.transform.position = to; endLight.intensity = 3.5f * fade * flicker;
        }
    }

    /// <summary>
    /// The electrocuted look: short yellow arcs crackling over a body, sparks and a flickering light, for a while.
    /// Follows a transform (an enemy's centre, the wand tip) or stays at a point.
    /// </summary>
    internal class ShockEffect : MonoBehaviour
    {
        private const int Arcs = 4;
        private Transform follow;
        private Enemy enemy;                           // set when it's on an enemy: ends early when that enemy dies
        private bool ended;
        private Vector3 offset, point, extents;
        private float duration, age, nextDraw, nextBuzz, lightScale;
        private Sound buzz;
        private AudioSource buzzSource;
        private float buzzUntil;                       // Time.time the current crackle ends
        private LineRenderer[] arcs;
        private Light flicker;
        private ParticleSystem sparks;
        private readonly List<Vector3> points = new List<Vector3>();

        /// <summary>Electrocutes an enemy for this long, sized to its body.</summary>
        internal static ShockEffect OnEnemy(Enemy enemy, Vector3 fallback, float duration, Sound buzz)
        {
            Transform centre = LightningTargets.CentreTransform(enemy);
            Vector3 extents = new Vector3(0.35f, 0.5f, 0.35f), offset = Vector3.zero;
            if (enemy != null)
            {
                Component body = LightningTargets.Body(enemy);
                bool any = false;
                Bounds b = default;
                foreach (Collider c in body.GetComponentsInChildren<Collider>())
                {
                    if (c == null || !c.enabled || c.isTrigger) continue;
                    if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
                }
                if (any)
                {
                    extents = new Vector3(Mathf.Clamp(b.extents.x, 0.15f, 2f), Mathf.Clamp(b.extents.y, 0.15f, 2.5f), Mathf.Clamp(b.extents.z, 0.15f, 2f));
                    if (centre != null) offset = b.center - centre.position;
                }
            }
            ShockEffect fx = Create(centre, centre != null ? offset : fallback, extents, duration, buzz, 1f);
            fx.enemy = enemy;
            return fx;
        }

        /// <param name="follow">Transform to stay on (null: stays at <paramref name="offsetOrPoint"/>).</param>
        internal static ShockEffect Create(Transform follow, Vector3 offsetOrPoint, Vector3 extents, float duration, Sound buzz, float lightScale)
        {
            var go = new GameObject("HWP Electrocuted");
            var fx = go.AddComponent<ShockEffect>();
            fx.follow = follow;
            fx.offset = offsetOrPoint;
            fx.point = offsetOrPoint;
            fx.extents = extents;
            fx.duration = duration;
            fx.buzz = buzz;
            fx.lightScale = lightScale;
            fx.Build();
            return fx;
        }

        private Vector3 Centre => follow != null ? follow.position + offset : point;

        private void Build()
        {
            float size = Mathf.Max(extents.x, extents.y, extents.z);
            arcs = new LineRenderer[Arcs];
            for (int i = 0; i < Arcs; i++)
                arcs[i] = LightningLook.Line(transform, "Arc " + i, Mathf.Clamp(size * 0.06f, 0.008f, 0.04f), i % 2 == 0 ? LightningLook.Core : LightningLook.Glow);

            flicker = gameObject.AddComponent<Light>();
            flicker.type = LightType.Point; flicker.color = LightningWand.Yellow;
            flicker.range = Mathf.Max(0.5f, size * 4f); flicker.intensity = 0f;

            var obj = new GameObject("Sparks");
            obj.transform.SetParent(transform, false);
            sparks = obj.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
            main.startColor = new ParticleSystem.MinMaxGradient(LightningLook.Core, LightningWand.Yellow);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 150;
            var emission = sparks.emission; emission.rateOverTime = Mathf.Clamp(size * 60f, 8f, 60f);
            var shape = sparks.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 1f; shape.scale = extents;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            if (FireballFx.Glow != null) pr.sharedMaterial = FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sparks.Play();

            nextBuzz = 0f;
            LateUpdate();
        }

        private Vector3 OnSurface()
        {
            Vector3 c = Centre;
            return c + Vector3.Scale(Random.onUnitSphere, extents);
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= duration) { Destroy(gameObject); return; }
            if (!ended && enemy != null && LightningTargets.Gone(enemy)) EndNow();
            transform.position = Centre;
            float fade = Mathf.Clamp01((duration - age) / 0.3f);

            if (age >= nextDraw)
            {
                nextDraw = age + Random.Range(0.04f, 0.09f);
                for (int i = 0; i < arcs.Length; i++)
                {
                    // each arc jumps across part of the body, and is sometimes off for a beat
                    bool on = Random.value < 0.75f;
                    arcs[i].enabled = on;
                    if (!on) continue;
                    Vector3 a = OnSurface(), b = OnSurface();
                    if (Vector3.Distance(a, b) < 0.05f) b = Centre;
                    LightningLook.Jag(points, a, b, 0.25f);
                    LightningLook.Apply(arcs[i], points);
                }
                flicker.intensity = Random.Range(0.6f, 2.2f) * lightScale * fade;
            }
            float size = Mathf.Max(extents.x, extents.y, extents.z);
            for (int i = 0; i < arcs.Length; i++) arcs[i].widthMultiplier = Mathf.Clamp(size * 0.06f, 0.008f, 0.04f) * fade;
            if (fade < 1f) { var e = sparks.emission; e.enabled = false; }

            // the crackle plays from the strike, again if the stun outlasts it, following the enemy
            if (buzz != null && age >= nextBuzz && age < duration - 0.4f)
            {
                float length = buzz.Sounds != null && buzz.Sounds.Length > 0 && buzz.Sounds[0] != null ? buzz.Sounds[0].length : 0.7f;
                nextBuzz = age + Mathf.Max(0.3f, length - 0.1f);
                try
                {
                    buzzSource = follow != null ? buzz.Play(follow, 1f, 1f, 1f, 1f) : buzz.Play(Centre, 1f, 1f, 1f, 1f);
                    buzzUntil = Time.time + length;
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Lightning crackle failed: {ex}"); buzz = null; }
            }
        }

        /// <summary>The enemy died: cut the crackle and fade out quickly.</summary>
        private void EndNow()
        {
            ended = true;
            enemy = null;
            if (follow != null) { point = Centre; follow = null; }   // stay where it died, don't trail a despawning body
            duration = Mathf.Min(duration, age + 0.3f);
            if (buzzSource != null && Time.time < buzzUntil - 0.05f && buzzSource.isPlaying) buzzSource.Stop();
            buzzSource = null;
            buzz = null;
            if (sparks != null) { var e = sparks.emission; e.enabled = false; }
        }

        private void OnDestroy()
        {
            // the stun ended before the crackle did: cut it (only while that source is still playing our clip)
            if (buzzSource != null && Time.time < buzzUntil - 0.05f && buzzSource.isPlaying) buzzSource.Stop();
        }
    }
}
