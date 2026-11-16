using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Host: slows enemies' walking for a while. The game has no slow of its own, and enemies set their nav agent's
    /// speed all the time (chasing, wandering, attacking), so this scales whatever speed they set: after their agent
    /// updates, and whenever they override it, a speed we didn't write ourselves is taken as the new full speed and
    /// cut to Factor of it. When the slow ends the full speed is put back.
    /// </summary>
    [HarmonyPatch]
    internal static class EnemySlow
    {
        private class State
        {
            public float Until, Factor, FullSpeed, Written;
            public bool Applied;
            public Enemy Enemy;
        }

        private static readonly Dictionary<EnemyNavMeshAgent, State> slowed = new Dictionary<EnemyNavMeshAgent, State>();
        private static readonly FieldInfo AgentField = AccessTools.Field(typeof(EnemyNavMeshAgent), "Agent");
        private static readonly FieldInfo NavField = AccessTools.Field(typeof(Enemy), "NavMeshAgent");

        internal static void Apply(Enemy enemy, float seconds, float factor)
        {
            if (enemy == null || seconds <= 0f || !SemiFunc.IsMasterClientOrSingleplayer()) return;
            EnemyNavMeshAgent agent = (NavField != null ? NavField.GetValue(enemy) as EnemyNavMeshAgent : null)
                                      ?? enemy.GetComponentInChildren<EnemyNavMeshAgent>();
            if (agent == null) { Plugin.Log.LogInfo($"Slow: {LightningTargets.Name(enemy)} doesn't walk on the nav mesh; not slowed."); return; }

            // forget agents that are gone
            var gone = new List<EnemyNavMeshAgent>();
            foreach (var kv in slowed) if (kv.Key == null || LightningTargets.Gone(kv.Value.Enemy)) gone.Add(kv.Key);
            foreach (var key in gone)
            {
                // give a dead enemy its speed back for when it respawns
                NavMeshAgent n = key != null && AgentField != null ? AgentField.GetValue(key) as NavMeshAgent : null;
                State old = slowed[key];
                if (n != null && old.Applied && Mathf.Abs(n.speed - old.Written) < 0.001f) n.speed = old.FullSpeed;
                slowed.Remove(key);
            }

            if (LightningTargets.Gone(enemy)) return;
            if (!slowed.TryGetValue(agent, out State st)) slowed[agent] = st = new State();
            st.Enemy = enemy;
            st.Until = Mathf.Max(st.Until, Time.time + seconds);
            st.Factor = Mathf.Clamp(factor, 0.05f, 1f);
            Enforce(agent, st);
        }

        /// <summary>Host: is this enemy slowed right now?</summary>
        internal static bool IsSlowed(Enemy enemy)
        {
            if (enemy == null) return false;
            foreach (var kv in slowed) if (kv.Value.Enemy == enemy && Time.time < kv.Value.Until) return true;
            return false;
        }

        /// <summary>Host: ends a slow now (fire thawed the enemy): full speed again.</summary>
        internal static void Clear(Enemy enemy)
        {
            if (enemy == null) return;
            foreach (var kv in new List<KeyValuePair<EnemyNavMeshAgent, State>>(slowed))
                if (kv.Value.Enemy == enemy) { kv.Value.Until = 0f; if (kv.Key != null) Enforce(kv.Key, kv.Value); else slowed.Remove(kv.Key); }
        }

        private static void Enforce(EnemyNavMeshAgent agent, State st)
        {
            NavMeshAgent nav = AgentField != null ? AgentField.GetValue(agent) as NavMeshAgent : null;
            if (nav == null) return;
            if (Time.time >= st.Until || LightningTargets.Gone(st.Enemy))   // worn off, or the enemy died: full speed again
            {
                if (st.Applied && Mathf.Abs(nav.speed - st.Written) < 0.001f) nav.speed = st.FullSpeed;
                slowed.Remove(agent);
                return;
            }
            if (!st.Applied || Mathf.Abs(nav.speed - st.Written) > 0.001f)
            {
                st.FullSpeed = nav.speed;
                st.Written = nav.speed * st.Factor;
                nav.speed = st.Written;
                st.Applied = true;
            }
        }

        private static void After(EnemyNavMeshAgent agent)
        {
            if (slowed.Count == 0 || agent == null) return;
            if (slowed.TryGetValue(agent, out State st)) Enforce(agent, st);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EnemyNavMeshAgent), "Update")]
        private static void UpdatePostfix(EnemyNavMeshAgent __instance) => After(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EnemyNavMeshAgent), nameof(EnemyNavMeshAgent.OverrideAgent))]
        private static void OverridePostfix(EnemyNavMeshAgent __instance) => After(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EnemyNavMeshAgent), nameof(EnemyNavMeshAgent.UpdateAgent))]
        private static void UpdateAgentPostfix(EnemyNavMeshAgent __instance) => After(__instance);
    }

    /// <summary>
    /// A block of ice encasing a frozen enemy (visual, every client): a faceted, see-through shell round its body that
    /// creeps over it, with a cold light, until it bursts (after a set time for an icicle's freeze, or when the ice wall
    /// breaks). Used by the Frost Wand's and Frost Staff's icicles and the Frost Staff's ice wall.
    /// </summary>
    internal class IceEncasement : MonoBehaviour
    {
        private const float GrowSeconds = 0.3f, ShatterSeconds = 0.2f;
        private static readonly Color IceDeep = new Color(0.4f, 0.75f, 0.97f, 0.62f);
        private static readonly Color IceLight = new Color(0.85f, 0.97f, 1f, 0.4f);

        private Transform follow;
        private Vector3 localOffset, extents, point;
        private Quaternion localRotation = Quaternion.identity;
        private Transform shell;
        private MeshRenderer shellRenderer;
        private MaterialPropertyBlock block;
        private Mesh mesh;
        private Light coldLight;
        private float age, shatterAge = -1f, until = -1f;
        private Sound crash;
        private Enemy enemy;
        private static readonly Dictionary<Enemy, IceEncasement> active = new Dictionary<Enemy, IceEncasement>();

        /// <summary>
        /// Encases the enemy (one casing per enemy: an existing one is reused). With seconds > 0 it bursts by itself
        /// after that long (a hit again makes it last longer); with seconds &lt;= 0 it stays until Shatter() (the ice wall).
        /// </summary>
        internal static IceEncasement Encase(Enemy enemy, Sound crash, float seconds)
        {
            if (LightningTargets.Gone(enemy)) return null;
            if (active.TryGetValue(enemy, out IceEncasement fx) && fx != null && fx.shatterAge < 0f)
            {
                if (seconds <= 0f) fx.until = -1f;
                else if (fx.until >= 0f) fx.until = Mathf.Max(fx.until, fx.age + seconds);
                return fx;
            }
            var go = new GameObject("HWP Ice Encasement");
            fx = go.AddComponent<IceEncasement>();
            fx.crash = crash;
            fx.enemy = enemy;
            fx.until = seconds > 0f ? seconds : -1f;
            fx.Setup(enemy);
            active[enemy] = fx;
            return fx;
        }

        private void Setup(Enemy enemy)
        {
            follow = LightningTargets.CentreTransform(enemy);
            LightningTargets.BodyBounds(enemy, out Bounds b);
            extents = new Vector3(Mathf.Clamp(b.extents.x * 1.15f + 0.06f, 0.25f, 2.5f),
                                  Mathf.Clamp(b.extents.y * 1.1f + 0.06f, 0.3f, 3f),
                                  Mathf.Clamp(b.extents.z * 1.15f + 0.06f, 0.25f, 2.5f));
            point = b.center;
            if (follow != null)
            {
                localOffset = Quaternion.Inverse(follow.rotation) * (b.center - follow.position);
                localRotation = Quaternion.Inverse(follow.rotation);
            }

            mesh = Shell(new System.Random(enemy.GetInstanceID()));
            shell = new GameObject("Shell").transform;
            shell.SetParent(transform, false);
            shell.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            shellRenderer = shell.gameObject.AddComponent<MeshRenderer>();
            if (FireballFx.Glow != null) shellRenderer.sharedMaterial = FireballFx.Glow;
            shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shellRenderer.receiveShadows = false;
            block = new MaterialPropertyBlock();
            shell.localScale = Vector3.zero;

            coldLight = gameObject.AddComponent<Light>();
            coldLight.type = LightType.Point; coldLight.color = new Color(0.55f, 0.9f, 1f);
            coldLight.range = Mathf.Max(1.5f, Mathf.Max(extents.x, extents.y, extents.z) * 3f); coldLight.intensity = 0f;
            LateUpdate();
        }

        /// <summary>A rough, faceted ellipsoid of ice (unit-ish, scaled to the body), each facet its own shade.</summary>
        private Mesh Shell(System.Random rng)
        {
            const int rings = 6, sides = 9;
            var grid = new Vector3[rings + 1, sides];
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    float lat = Mathf.PI * r / rings, lon = 2f * Mathf.PI * (s + (r % 2) * 0.5f) / sides;
                    float jitter = r == 0 || r == rings ? 1f : 0.9f + 0.2f * (float)rng.NextDouble();
                    var unit = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon)) * jitter;
                    // a little boxier than a sphere, like a block of ice
                    unit = new Vector3(Mathf.Sign(unit.x) * Mathf.Pow(Mathf.Abs(unit.x), 0.8f), Mathf.Sign(unit.y) * Mathf.Pow(Mathf.Abs(unit.y), 0.8f),
                                       Mathf.Sign(unit.z) * Mathf.Pow(Mathf.Abs(unit.z), 0.8f));
                    grid[r, s] = Vector3.Scale(unit, extents);
                }
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                float shade = (float)rng.NextDouble();
                Color col = Color.Lerp(IceDeep, IceLight, shade);
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                colors.Add(col); colors.Add(col); colors.Add(Color.Lerp(col, Color.white, 0.15f));
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    int n = (s + 1) % sides;
                    Tri(grid[r, s], grid[r, n], grid[r + 1, s]);
                    Tri(grid[r, n], grid[r + 1, n], grid[r + 1, s]);
                }
            var m = new Mesh { name = "HWP Ice Encasement" };
            m.SetVertices(verts);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Is this enemy in an ice casing that hasn't burst yet (this game)?</summary>
        internal static bool IsEncased(Enemy enemy) =>
            enemy != null && active.TryGetValue(enemy, out IceEncasement fx) && fx != null && fx.shatterAge < 0f;

        /// <summary>Bursts this enemy's casing now, if it has one (fire thawed it).</summary>
        internal static void ShatterOn(Enemy enemy)
        {
            if (enemy != null && active.TryGetValue(enemy, out IceEncasement fx) && fx != null) fx.Shatter();
        }

        /// <summary>Bursts the ice (every client), with a crash.</summary>
        internal void Shatter()
        {
            if (shatterAge >= 0f) return;
            shatterAge = 0f;
            Vector3 c = transform.position;
            IceShatter.Spawn(c);
            for (int i = 0; i < 5; i++) IceShatter.Spawn(c + Vector3.Scale(Random.onUnitSphere, extents) * 0.8f);
            if (crash != null)
            {
                try { crash.Play(c, 1f, 1f, 1f, 1f); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Ice encasement sound failed: {e}"); }
            }
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (shatterAge < 0f && LightningTargets.Gone(enemy))
            {
                // the enemy died: the ice bursts where it was
                if (follow != null) point = follow.position + follow.rotation * localOffset;
                follow = null;
                Shatter();
            }
            if (follow != null)
            {
                transform.position = follow.position + follow.rotation * localOffset;
                transform.rotation = follow.rotation * localRotation;
            }
            else transform.position = point;

            if (until >= 0f && age >= until && shatterAge < 0f) Shatter();
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / GrowSeconds));   // the ice creeps over it
            float alpha = 1f;
            if (shatterAge >= 0f)
            {
                shatterAge += Time.deltaTime;
                float t = Mathf.Clamp01(shatterAge / ShatterSeconds);
                k *= 1f + 0.12f * t;
                alpha = 1f - t;
                if (t >= 1f) { Destroy(gameObject); return; }
            }
            shell.localScale = Vector3.one * k;
            block.SetColor("_Color", new Color(1f, 1f, 1f, alpha));
            shellRenderer.SetPropertyBlock(block);
            coldLight.intensity = 1.2f * k * alpha;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (enemy != null && active.TryGetValue(enemy, out IceEncasement fx) && fx == this) active.Remove(enemy);
            else
            {
                var stale = new List<Enemy>();
                foreach (var kv in active) if (kv.Key == null || kv.Value == null || kv.Value == this) stale.Add(kv.Key);
                foreach (Enemy key in stale) active.Remove(key);
            }
        }
    }
}
