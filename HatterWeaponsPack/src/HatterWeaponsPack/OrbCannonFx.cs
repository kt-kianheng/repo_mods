using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// A fired orb. Every player's game flies its own copy for the looks; only the host's copy ("authority") checks
    /// for hits, deals the damage and tells everyone where it burst (OrbCannon.HostImpact).
    ///  - Medium (one of the burst): BurstDamage to what it hits directly, then a BurstSplashRadius splash that deals
    ///    BurstDamage to everyone else in it (enemies and players, the shooter included). Nobody takes it twice.
    ///  - Large: BigDamage to what it hits directly; everyone's game then spawns the Rocket Launcher's blast there.
    /// </summary>
    public class OrbShot : MonoBehaviour
    {
        private OrbCannon cannon;
        private int id;
        private OrbCannon.Kind kind;
        private float speed, radius, age;
        private bool authority, done;
        private PlayerAvatar shooter;

        internal static OrbShot Spawn(OrbCannon cannon, int id, OrbCannon.Kind kind, Vector3 position, Quaternion rotation, float speed,
                                      float radius, bool authority, PlayerAvatar shooter)
        {
            var go = new GameObject("HWP Orb " + id);
            go.transform.SetPositionAndRotation(position, rotation);
            // the game's own orb: the small orb's look for the burst's orbs, the big orb's for the large shot
            OrbFx.OrbModel(go.transform, "Orb", kind == OrbCannon.Kind.Large ? 2 : 0, radius * 2f);
            // a bright pink core inside it, so the orb always shows (the game's orb material can look faint away from its scripts)
            OrbFx.GlowBall(go.transform, "Orb Glow", radius * 1.3f);
            SoftGlow halo = SoftGlow.Create(go.transform, Vector3.zero, "Orb Halo");
            halo.SetColor(new Color(OrbCannon.Pink.r, OrbCannon.Pink.g, OrbCannon.Pink.b, 0.6f));
            halo.SetSize(radius * 5f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = OrbCannon.Pink;
            light.range = kind == OrbCannon.Kind.Large ? 5f : 2.5f;
            light.intensity = 4f;
            light.shadows = LightShadows.None;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = kind == OrbCannon.Kind.Large ? 0.5f : 0.3f;
            trail.startWidth = radius * 1.6f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.04f;
            if (OrbFx.Unlit != null) trail.sharedMaterial = OrbFx.Unlit;
            trail.startColor = new Color(1f, 0.5f, 0.82f, 0.85f);
            trail.endColor = new Color(1f, 0.3f, 0.7f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var s = go.AddComponent<OrbShot>();
            s.cannon = cannon;
            s.id = id;
            s.kind = kind;
            s.speed = speed;
            s.radius = radius;
            s.authority = authority;
            s.shooter = shooter;
            return s;
        }

        private void FixedUpdate()
        {
            if (done) return;
            float dt = Time.fixedDeltaTime;
            age += dt;
            Vector3 from = transform.position;
            Vector3 step = transform.forward * speed * dt;
            if (authority)
            {
                PhysGrabObject self = cannon != null ? cannon.PhysGrabObject : null;
                float hitRadius = Mathf.Min(radius, 0.15f);
                bool worldHit = WorldHit(from, step, hitRadius, self, out RaycastHit hit);
                float worldDistance = worldHit ? Vector3.Distance(from, hit.point) : float.MaxValue;
                if (ProjectileHits.Player(from, step, hitRadius, worldDistance, shooter, out PlayerAvatar player, out Vector3 playerPoint))
                {
                    Burst(playerPoint - step.normalized * 0.05f, null, player, step.normalized);
                    return;
                }
                if (worldHit)
                {
                    Burst(hit.point + hit.normal * 0.05f, hit.collider, null, step.normalized);
                    return;
                }
                if (age >= Settings.OrbLifetime.Value)
                {
                    Burst(from, null, null, step.normalized);
                    return;
                }
            }
            else if (age >= Settings.OrbLifetime.Value + 1.5f)
            {
                if (cannon != null) cannon.Forget(id);
                Destroy(gameObject);
                return;
            }
            transform.position = from + step;
            travelled += step.magnitude;
        }

        private const float ArmDistance = 0.6f;
        private float travelled;
        private static readonly FieldInfo HeldField = AccessTools.Field(typeof(PhysGrabObject), "grabbed");

        /// <summary>
        /// The nearest thing this step hits that should burst the orb. Never: this cannon, players' bodies (players are
        /// hit by ProjectileHits.Player), or anything someone is holding. For the first ArmDistance it also ignores
        /// whatever it starts inside and any loose item or valuable, so it can't burst at the muzzle.
        /// </summary>
        private bool WorldHit(Vector3 from, Vector3 step, float r, PhysGrabObject self, out RaycastHit best)
        {
            best = default;
            float bestDistance = float.MaxValue;
            bool armed = travelled >= ArmDistance;
            foreach (RaycastHit h in Physics.SphereCastAll(from, r, step.normalized, step.magnitude, ProjectileHits.Mask, QueryTriggerInteraction.Ignore))
            {
                Collider c = h.collider;
                if (c == null) continue;
                PhysGrabObject pgo = c.GetComponentInParent<PhysGrabObject>();
                if (self != null && pgo == self) continue;
                if (c.GetComponentInParent<PlayerAvatar>() != null || c.GetComponentInParent<PlayerController>() != null) continue;
                if (LayerMask.LayerToName(c.gameObject.layer).StartsWith("Player")) continue;
                bool enemy = c.GetComponentInParent<EnemyRigidbody>() != null || c.GetComponentInParent<Enemy>() != null;
                if (!enemy && pgo != null && HeldField != null && (bool)HeldField.GetValue(pgo)) continue;   // a held item
                bool inside = h.distance <= 0f && h.point == Vector3.zero;
                if (!armed && (inside || (!enemy && pgo != null))) continue;
                float d = inside ? 0f : h.distance;
                if (d < bestDistance) { bestDistance = d; best = h; if (inside) best.point = from; }
            }
            if (bestDistance < float.MaxValue && travelled < 1.5f && Settings.OrbDebugLog.Value)
                Plugin.Log.LogInfo($"Orb Cannon: {kind} orb hit '{best.collider.name}' (layer {LayerMask.LayerToName(best.collider.gameObject.layer)}, " +
                                   $"under '{best.collider.transform.root.name}') only {travelled + bestDistance:0.00} m after leaving.");
            return bestDistance < float.MaxValue;
        }

        /// <summary>Host: the direct hit (if any), the medium orb's splash, then tell everyone.</summary>
        private void Burst(Vector3 at, Collider hitCollider, PlayerAvatar hitPlayer, Vector3 dir)
        {
            done = true;
            bool large = kind == OrbCannon.Kind.Large;
            int damage = large ? Settings.OrbBigDamage.Value : Settings.OrbBurstDamage.Value;
            var damaged = new HashSet<Object>();
            if (hitPlayer != null)
            {
                ProjectileHits.HurtPlayer(hitPlayer, damage, at);
                damaged.Add(hitPlayer);
            }
            else if (hitCollider != null)
            {
                ProjectileHits.HurtEnemy(hitCollider, damage, dir);
                damaged.Add(EnemyKey(hitCollider));
            }
            if (!large) Splash(at, damage, dir, damaged);
            if (Settings.OrbDebugLog.Value)
                Plugin.Log.LogInfo($"Orb Cannon: {kind} orb burst at {at}" +
                                   (hitPlayer != null ? $", hit {SafeName(hitPlayer)}" : hitCollider != null ? $", hit '{hitCollider.name}'" : "") +
                                   $", {damaged.Count} target(s) damaged.");
            if (cannon != null) cannon.HostImpact(id, kind, at);
            else Destroy(gameObject);
        }

        /// <summary>The medium orb's small splash: BurstDamage to every enemy/player in it not already hit by this orb.</summary>
        private void Splash(Vector3 at, int damage, Vector3 dir, HashSet<Object> damaged)
        {
            float r = Settings.OrbBurstSplashRadius.Value;
            if (r <= 0f) return;
            foreach (Collider c in Physics.OverlapSphere(at, r, ProjectileHits.Mask | LayerMask.GetMask("PhysGrabObject"), QueryTriggerInteraction.Collide))
            {
                if (c == null) continue;
                if (c.GetComponentInParent<ItemAttributes>() != null || c.GetComponentInParent<ValuableObject>() != null) continue;
                Object key = EnemyKey(c);
                if (damaged.Contains(key)) continue;
                bool enemy = c.GetComponentInParent<EnemyRigidbody>() != null || c.GetComponentInParent<Enemy>() != null;
                bool dummy = !enemy && c.GetComponentInParent<PlayerAvatar>() == null;
                if (!enemy && !dummy) continue;
                damaged.Add(key);
                ProjectileHits.HurtEnemy(c, damage, dir);   // enemies, or a TargetDummy (others ignore the message)
            }
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (!ProjectileHits.Alive(p) || damaged.Contains(p) || !p.gameObject.activeInHierarchy) continue;
                Vector3 bottom = p.transform.position + Vector3.up * 0.4f, top = p.transform.position + Vector3.up * 1.2f;
                Vector3 onBody = ClosestOnSegment(bottom, top, at);
                if ((onBody - at).sqrMagnitude > (r + 0.4f) * (r + 0.4f)) continue;
                damaged.Add(p);
                ProjectileHits.HurtPlayer(p, damage, at);
            }
        }

        /// <summary>One key per enemy (all its colliders), or per object for anything else.</summary>
        private static Object EnemyKey(Collider c)
        {
            EnemyRigidbody erb = c.GetComponentInParent<EnemyRigidbody>();
            if (erb != null && erb.enemy != null) return erb.enemy;
            Enemy e = c.GetComponentInParent<Enemy>();
            if (e != null) return e;
            return c.transform.root;
        }

        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return a + ab * t;
        }

        private static string SafeName(PlayerAvatar p)
        {
            try { return SemiFunc.PlayerGetName(p); } catch { return p != null ? p.name : "?"; }
        }
    }

    /// <summary>The Orb Cannon's looks: glowing pink balls, the shockwave cone, flashes, bursts and the cooling smoke.</summary>
    internal static class OrbFx
    {
        private static Material unlit;
        internal static Material Unlit
        {
            get
            {
                if (unlit == null)
                {
                    Shader s = Shader.Find("Sprites/Default");
                    if (s != null) unlit = new Material(s) { name = "HWP Orb Glow" };
                }
                return unlit;
            }
        }

        /// <summary>
        /// A copy of one of the game's soul orbs (0 small, 1 medium, 2 big; OrbCannonBuilder.OrbModels), <paramref name="diameter"/>
        /// across; a pink glow ball if that orb couldn't be copied.
        /// </summary>
        internal static GameObject OrbModel(Transform parent, string name, int which, float diameter)
        {
            GameObject template = which >= 0 && which < OrbCannonBuilder.OrbModels.Length ? OrbCannonBuilder.OrbModels[which] : null;
            if (template == null) return GlowBall(parent, name, diameter);
            GameObject g = Object.Instantiate(template, parent, false);
            g.name = name;
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.identity;
            g.transform.localScale = Vector3.one * diameter;
            int layer = parent != null ? parent.gameObject.layer : 0;
            foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            g.SetActive(true);
            return g;
        }

        /// <summary>A pink self-lit ball (diameter 1 before scaling) with a paler core.</summary>
        internal static GameObject GlowBall(Transform parent, string name, float diameter)
        {
            GameObject outer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            outer.name = name;
            Object.DestroyImmediate(outer.GetComponent<Collider>());
            outer.transform.SetParent(parent, false);
            outer.transform.localScale = Vector3.one * diameter;
            Paint(outer.GetComponent<MeshRenderer>(), new Color(1f, 0.35f, 0.75f, 0.85f));
            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = name + " Core";
            Object.DestroyImmediate(core.GetComponent<Collider>());
            core.transform.SetParent(outer.transform, false);
            core.transform.localScale = Vector3.one * 0.6f;
            Paint(core.GetComponent<MeshRenderer>(), new Color(1f, 0.85f, 0.95f, 1f));
            return outer;
        }

        private static void Paint(Renderer r, Color c)
        {
            if (Unlit != null) r.sharedMaterial = Unlit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        /// <summary>
        /// Every game, when a cannon fires: a laser pulse flare at the muzzle: a bright beam shooting out ~2.5 m and
        /// thinning away, a wider coloured glow round it, a ring flashing outwards, and a short strong light.
        /// </summary>
        internal static void LaserFlare(Vector3 at, Vector3 dir, Color color, Color light)
        {
            var go = new GameObject("HWP Cannon Laser Flare");
            go.transform.position = at;
            go.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward);
            go.AddComponent<LaserFlareFx>().Setup(color, light);
        }

        /// <summary>A quick pink flash at the muzzle.</summary>
        internal static void MuzzleFlash(Vector3 at, Vector3 dir, float size)
        {
            var go = new GameObject("HWP Orb Muzzle Flash");
            go.transform.position = at + dir.normalized * 0.05f;
            SoftGlow g = SoftGlow.Create(go.transform, Vector3.zero, "Flash");
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = OrbCannon.Pink; l.range = 3f * size; l.shadows = LightShadows.None;
            go.AddComponent<Fade>().Setup(g, l, 0.25f, size * 0.7f, 3f, OrbCannon.PinkLight);
        }

        /// <summary>
        /// The large orb's plasma explosion (drawn by the mod, not the game's fireball), sized to the blast radius:
        ///  - a white-hot core that swells and fades in a quarter second;
        ///  - churning clouds of pink and blue plasma (soft particles, mixed), billowing out and fading;
        ///  - a dozen pink and blue electric arcs crackling out of it, re-jagged constantly, thinning as it dies down;
        ///  - a thin translucent shock shell racing out to the full radius;
        ///  - a flat shock ring along the ground;
        ///  - crackling pink sparks and slower drifting plasma motes;
        ///  - a strong pink light that flickers and dies.
        /// </summary>
        internal static void PlasmaExplosion(Vector3 at, float radius)
        {
            var go = new GameObject("HWP Plasma Explosion");
            go.transform.position = at;
            go.AddComponent<PlasmaBlast>().Setup(Mathf.Max(0.5f, radius));
        }

        /// <summary>A pink burst where an orb pops (size 1 ≈ a metre across at its peak).</summary>
        internal static void Burst(Vector3 at, float size)
        {
            var go = new GameObject("HWP Orb Burst");
            go.transform.position = at;
            SoftGlow g = SoftGlow.Create(go.transform, Vector3.zero, "Burst");
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = OrbCannon.Pink; l.range = 4f * size; l.shadows = LightShadows.None;
            go.AddComponent<Fade>().Setup(g, l, 0.4f, size, 4f, OrbCannon.Pink);
            Sparks(go.transform, size);
        }

        internal static void Sparks(Transform parent, float size)
        {
            var go = new GameObject("Sparks");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * size, 5f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f * size + 0.01f, 0.06f * size + 0.01f);
            main.startColor = new ParticleSystem.MinMaxGradient(OrbCannon.Pink, OrbCannon.PinkLight);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.3f;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(18 * size + 8, 8, 60)) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.05f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SoftGlow.SharedMaterial != null ? SoftGlow.SharedMaterial : Unlit;
            go.SetActive(true);
            ps.Play();
        }

        /// <summary>The small orb's shockwave: a translucent force-field screen racing out of the muzzle (see ShockwaveScreen).</summary>
        internal static void Shockwave(Vector3 from, Vector3 dir, float range, float angle, float speed)
        {
            var go = new GameObject("HWP Orb Shockwave");
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
            go.AddComponent<ShockwaveScreen>().Setup(range, angle, speed);
            MuzzleFlash(from, dir, 0.9f);
        }

        /// <summary>Grey smoke pouring out along <paramref name="localDir"/>; emission off until switched on.</summary>
        internal static ParticleSystem Smoke(Transform parent, Vector3 localPos, Vector3 localDir)
        {
            var go = new GameObject("Cooling Smoke");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(localDir);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = true; main.loop = true; main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.42f, 0.45f, 0.55f), new Color(0.6f, 0.6f, 0.62f, 0.4f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.06f;   // drifts up
            main.maxParticles = 120;
            var em = ps.emission; em.rateOverTime = 22f; em.enabled = false;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = 0.04f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SoftGlow.SharedMaterial != null ? SoftGlow.SharedMaterial : Unlit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(true);
            return ps;
        }

        /// <summary>Fades a glow and a light out (growing the glow), then removes the object.</summary>
        private class Fade : MonoBehaviour
        {
            private SoftGlow glow; private Light light; private float life, size, intensity, t; private Color colour;
            internal void Setup(SoftGlow g, Light l, float seconds, float diameter, float lightIntensity, Color c)
            { glow = g; light = l; life = seconds; size = diameter; intensity = lightIntensity; colour = c; Tick(); }
            private void Update() { t += Time.deltaTime; Tick(); if (t >= life) Destroy(gameObject); }
            private void Tick()
            {
                float k = Mathf.Clamp01(t / life);
                if (glow != null) { glow.SetSize(size * (0.5f + k)); glow.SetColor(new Color(colour.r, colour.g, colour.b, 1f - k)); }
                if (light != null) light.intensity = intensity * (1f - k);
            }
        }
    }

    /// <summary>
    /// The shockwave's look: a curved, see-through force-field screen (a cap of a sphere centred on the muzzle,
    /// covering the cone) that races outwards at ShockwaveSpeed, growing as it goes. It's mostly clear, brighter
    /// towards its rim, with faint rings rippling across it and shimmering, so it reads as a wall of warped air rather
    /// than a solid shape; a second, fainter screen trails just behind it. At the cone's range it keeps going a little
    /// while it fades out.
    /// </summary>
    internal class ShockwaveScreen : MonoBehaviour
    {
        private const int Rings = 10, Around = 36;
        private const float FadeSeconds = 0.18f;
        private float range, half, speed, t;
        private Mesh mesh;
        private Vector3[] dirs;
        private float[] polar;
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Color> cols = new List<Color>();

        internal void Setup(float range, float angle, float speed)
        {
            this.range = range;
            half = Mathf.Clamp(angle, 5f, 179f) / 2f;
            this.speed = Mathf.Max(1f, speed);
            mesh = new Mesh { name = "HWP Shockwave Screen" };
            mesh.MarkDynamic();
            // one direction per vertex: rings round the cone's axis, from the centre out to the cone's edge
            int n = (Rings + 1) * Around;
            dirs = new Vector3[n];
            polar = new float[n];
            for (int i = 0; i <= Rings; i++)
                for (int j = 0; j < Around; j++)
                {
                    float p = half * i / Rings, az = 360f * j / Around;
                    Quaternion q = Quaternion.AngleAxis(az, Vector3.forward) * Quaternion.AngleAxis(p, Vector3.right);
                    dirs[i * Around + j] = q * Vector3.forward;
                    polar[i * Around + j] = i / (float)Rings;
                }
            var tris = new List<int>();
            for (int layer = 0; layer < 2; layer++)
            {
                int o = layer * n;
                for (int i = 0; i < Rings; i++)
                    for (int j = 0; j < Around; j++)
                    {
                        int jn = (j + 1) % Around;
                        int a = o + i * Around + j, b = o + i * Around + jn, c = o + (i + 1) * Around + j, d = o + (i + 1) * Around + jn;
                        tris.AddRange(new[] { a, c, d, a, d, b, a, d, c, a, b, d });   // both sides
                    }
            }
            Fill();
            mesh.SetTriangles(tris, 0);
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = OrbFx.Unlit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void Update()
        {
            t += Time.deltaTime;
            Fill();
            if (t >= range / speed + FadeSeconds) Destroy(gameObject);
        }

        /// <summary>Places both screens at the current distance and colours them (alpha only: it's mostly see-through).</summary>
        private void Fill()
        {
            float reach = Mathf.Max(0.15f, speed * t);
            float travel = range / speed;
            float fade = t <= travel ? 1f : 1f - Mathf.Clamp01((t - travel) / FadeSeconds);
            float wake = Mathf.Clamp01(t * 12f);                          // fades in over the first few hundredths
            verts.Clear(); cols.Clear();
            for (int layer = 0; layer < 2; layer++)
            {
                float rr = layer == 0 ? reach : reach * 0.9f;
                float strength = (layer == 0 ? 1f : 0.45f) * fade * wake;
                for (int k = 0; k < dirs.Length; k++)
                {
                    float p = polar[k];
                    float az = (k % Around) / (float)Around * Mathf.PI * 2f;
                    // shimmer: the surface wobbles a little in and out, and faint rings ripple outwards across it
                    float wobble = 1f + 0.025f * Mathf.Sin(az * 5f + t * 40f + p * 9f);
                    verts.Add(dirs[k] * rr * wobble);
                    float ripple = 0.5f + 0.5f * Mathf.Sin(p * 18f - t * 60f);
                    float rim = Mathf.Pow(p, 2.2f);                       // brighter towards the edge, like a bubble
                    float edgeFade = 1f - Mathf.Clamp01((p - 0.92f) / 0.08f);  // soft outer edge
                    float a = (0.05f + 0.10f * ripple + 0.30f * rim) * edgeFade * strength;
                    cols.Add(new Color(0.92f, 0.86f, 1f, a));
                }
            }
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.RecalculateBounds();
        }

        private void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }
    /// <summary>Runs the large orb's plasma explosion (see OrbFx.PlasmaExplosion).</summary>
    internal class PlasmaBlast : MonoBehaviour
    {
        private const float Life = 1.1f;
        private const int Arcs = 12;
        private static readonly Color ArcPink = new Color(1f, 0.38f, 0.9f, 1f);
        private static readonly Color ArcBlue = new Color(0.55f, 0.65f, 1f, 1f);
        private float radius, t;
        private Transform core, shell, ring;
        private MaterialPropertyBlock block;
        private Renderer coreR, shellR, ringR;
        private Light glow;
        // electric arcs: a bright core line and a wide faint glow line each, re-jagged every few hundredths of a second
        private readonly LineRenderer[] arcCore = new LineRenderer[Arcs], arcGlow = new LineRenderer[Arcs];
        private readonly Vector3[] arcEnd = new Vector3[Arcs];
        private readonly float[] arcNext = new float[Arcs];
        private readonly List<Vector3> path = new List<Vector3>();

        internal void Setup(float radius)
        {
            this.radius = radius;
            block = new MaterialPropertyBlock();
            core = Sphere("Core", out coreR);
            shell = Sphere("Shock Shell", out shellR);
            ring = Ring(out ringR);
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(transform, false);
            glow = lightGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.85f, 0.45f, 1f);
            glow.range = radius * 3f;
            glow.shadows = LightShadows.None;
            // the plasma: churning pink and blue clouds instead of a ball
            Clouds("Pink Plasma", new Color(1f, 0.28f, 0.78f, 0.75f), new Color(0.78f, 0.3f, 1f, 0.7f), 26);
            Clouds("Blue Plasma", new Color(0.35f, 0.55f, 1f, 0.7f), new Color(0.6f, 0.45f, 1f, 0.65f), 20);
            Clouds("Hot Core Wisps", new Color(1f, 0.85f, 1f, 0.6f), new Color(0.8f, 0.85f, 1f, 0.55f), 8, 0.5f);
            OrbFx.Sparks(transform, radius * 0.9f);
            for (int i = 0; i < Arcs; i++)
            {
                Color c = i % 2 == 0 ? ArcPink : ArcBlue;
                arcGlow[i] = LightningLook.Line(transform, "Arc Glow " + i, 0.09f, new Color(c.r, c.g, c.b, 0.35f));
                arcCore[i] = LightningLook.Line(transform, "Arc " + i, 0.025f, Color.Lerp(c, Color.white, 0.35f));
                NewArc(i);
            }
            Tick();
        }

        private Transform Sphere(string name, out Renderer r)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(transform, false);
            r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = OrbFx.Unlit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g.transform;
        }

        /// <summary>A flat ring (annulus) lying on the ground, 1 m outer radius, 0.75 m inner, faded at both edges.</summary>
        private Transform Ring(out Renderer r)
        {
            const int seg = 48;
            var v = new List<Vector3>(); var c = new List<Color>(); var tris = new List<int>();
            float[] radii = { 0.75f, 0.9f, 1f };
            float[] alpha = { 0f, 1f, 0f };
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int k = 0; k < 3; k++) { v.Add(d * radii[k]); c.Add(new Color(1f, 1f, 1f, alpha[k])); }
            }
            for (int i = 0; i < seg; i++)
                for (int k = 0; k < 2; k++)
                {
                    int a = i * 3 + k, b = a + 3;
                    tris.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1, a, b + 1, b, a, a + 1, b + 1 });
                }
            var m = new Mesh { name = "HWP Shock Ring" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(tris, 0); m.RecalculateBounds();
            var g = new GameObject("Shock Ring");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(0f, 0.05f - FloorDrop(), 0f);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = OrbFx.Unlit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        private float FloorDrop()
        {
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, radius, SemiFunc.LayerMaskGetVisionObstruct().value, QueryTriggerInteraction.Ignore))
                return hit.distance;
            return 0f;
        }

        /// <summary>
        /// Soft glowing puffs that billow out from the centre, churn (noise), grow and fade: with two of these, one pink
        /// to purple and one blue to violet, the colours mix into a plasma cloud like the reference picture.
        /// </summary>
        private void Clouds(string name, Color a, Color b, int count, float scale = 1f)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 0.15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f * radius * scale, 1.9f * radius * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f * radius * scale, 0.65f * radius * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius * 0.12f;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = new ParticleSystem.MinMaxCurve(radius * 0.25f); lim.dampen = 0.2f;
            var noise = ps.noise; noise.enabled = true; noise.strength = radius * 0.6f; noise.frequency = 1.4f; noise.scrollSpeed = 1.5f; noise.octaveCount = 2;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.7f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SoftGlow.SharedMaterial != null ? SoftGlow.SharedMaterial : OrbFx.Unlit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(true);
            ps.Play();
        }

        /// <summary>A new arc: from near the centre out to somewhere in the cloud (often past its edge), redrawn soon.</summary>
        private void NewArc(int i)
        {
            Vector3 dir = Random.onUnitSphere;
            arcEnd[i] = dir * radius * Random.Range(0.35f, 0.95f);
            arcNext[i] = t + Random.Range(0.03f, 0.08f);
        }

        private void Update()
        {
            t += Time.deltaTime;
            Tick();
            if (t >= Life + 1.2f) Destroy(gameObject);   // the clouds and sparks finish too
        }

        private void Tick()
        {
            // core: white-hot, swells fast, gone by 0.3 s
            float kc = Mathf.Clamp01(t / 0.3f);
            Paint(coreR, core, Mathf.Lerp(0.3f, 0.9f, Mathf.Sqrt(kc)) * radius * 0.45f, new Color(1f, 0.92f, 1f, 1f - kc));
            // shock shell: thin and see-through, racing to the full radius in 0.3 s
            float ks = Mathf.Clamp01(t / 0.3f);
            Paint(shellR, shell, Mathf.Lerp(0.3f, 2f, ks) * radius, new Color(0.85f, 0.6f, 1f, 0.16f * (1f - ks)));
            // ground ring: out to 1.3x the radius in 0.45 s
            float kr = Mathf.Clamp01(t / 0.45f);
            if (ring != null)
            {
                ring.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.3f, 1f - (1f - kr) * (1f - kr)) * radius;
                block.SetColor("_Color", new Color(0.95f, 0.5f, 1f, 0.8f * (1f - kr)));
                ringR.SetPropertyBlock(block);
            }
            // electric arcs: flicker on and off, re-jag constantly, fewer and fainter as it dies down
            float arcLife = Mathf.Clamp01(1f - (t - 0.15f) / (Life - 0.15f));
            Vector3 centre = transform.position;
            for (int i = 0; i < Arcs; i++)
            {
                if (arcCore[i] == null) continue;
                bool on = arcLife > 0f && Random.value < 0.35f + 0.6f * arcLife;
                arcCore[i].enabled = arcGlow[i].enabled = on;
                if (!on) continue;
                if (t >= arcNext[i]) NewArc(i);
                Vector3 start = centre + Random.insideUnitSphere * radius * 0.15f;
                LightningLook.Jag(path, start, centre + arcEnd[i], 0.28f);
                LightningLook.Apply(arcCore[i], path);
                LightningLook.Apply(arcGlow[i], path);
                arcCore[i].widthMultiplier = 0.025f * (0.5f + arcLife);
                arcGlow[i].widthMultiplier = 0.09f * (0.5f + arcLife);
            }
            // light: a flash, then a flickering purple glow dying down
            if (glow != null)
            {
                float fl = 0.7f + 0.3f * Mathf.PerlinNoise(t * 30f, 0.5f);
                glow.intensity = 9f * Mathf.Clamp01(1f - t / Life) * fl;
                glow.color = Color.Lerp(new Color(1f, 0.4f, 0.85f), new Color(0.5f, 0.6f, 1f), 0.5f + 0.5f * Mathf.Sin(t * 25f));
            }
        }

        private void Paint(Renderer r, Transform tr, float diameter, Color c)
        {
            if (r == null) return;
            tr.localScale = Vector3.one * Mathf.Max(0.001f, diameter);
            r.enabled = c.a > 0.003f;
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        private void OnDestroy()
        {
            if (ring != null) { var mf = ring.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh); }
        }
    }

    /// <summary>The cannons' laser pulse muzzle flare (OrbFx.LaserFlare).</summary>
    internal class LaserFlareFx : MonoBehaviour
    {
        private const float Grow = 0.06f, Life = 0.28f, Length = 2.5f;
        private LineRenderer core, glow, ring;
        private Light lamp;
        private Color color, lightColor;
        private float t;

        internal void Setup(Color c, Color l)
        {
            color = c; lightColor = l;
            core = Line("Core", 2, false);
            glow = Line("Glow", 2, false);
            ring = Line("Ring", 33, true);
            lamp = gameObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = c;
            lamp.range = 5f;
            lamp.intensity = 7f;
            lamp.shadows = LightShadows.None;
            Tick();
        }

        private LineRenderer Line(string name, int points, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = points;
            lr.loop = loop;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (OrbFx.Unlit != null) lr.sharedMaterial = OrbFx.Unlit;
            return lr;
        }

        private void Update()
        {
            t += Time.deltaTime;
            Tick();
            if (t >= Life) Destroy(gameObject);
        }

        private void Tick()
        {
            float grow = Mathf.Clamp01(t / Grow);
            float fade = 1f - Mathf.Clamp01((t - Grow) / (Life - Grow));
            float len = Length * (1f - (1f - grow) * (1f - grow));
            float tail = Length * Mathf.Clamp01((t - Grow) / (Life - Grow)) * 0.6f;   // the beam's back end races after its front
            Color hot = Color.Lerp(lightColor, Color.white, 0.6f);
            // the white-hot core
            core.SetPosition(0, new Vector3(0f, 0f, tail));
            core.SetPosition(1, new Vector3(0f, 0f, len));
            core.widthMultiplier = 0.05f * fade + 0.005f;
            core.startColor = new Color(hot.r, hot.g, hot.b, fade);
            core.endColor = new Color(lightColor.r, lightColor.g, lightColor.b, 0f);
            // the coloured glow round it
            glow.SetPosition(0, new Vector3(0f, 0f, tail * 0.8f));
            glow.SetPosition(1, new Vector3(0f, 0f, len * 0.9f));
            glow.widthMultiplier = 0.22f * fade + 0.02f;
            glow.startColor = new Color(color.r, color.g, color.b, 0.55f * fade);
            glow.endColor = new Color(color.r, color.g, color.b, 0f);
            // the ring flashing out round the muzzle
            float r = Mathf.Lerp(0.08f, 0.55f, Mathf.Clamp01(t / Life));
            for (int i = 0; i < ring.positionCount; i++)
            {
                float a = i / (float)(ring.positionCount - 1) * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0.05f + r * 0.2f));
            }
            ring.widthMultiplier = 0.03f * fade;
            ring.startColor = ring.endColor = new Color(lightColor.r, lightColor.g, lightColor.b, 0.8f * fade);
            if (lamp != null) lamp.intensity = 7f * fade;
        }
    }
}
