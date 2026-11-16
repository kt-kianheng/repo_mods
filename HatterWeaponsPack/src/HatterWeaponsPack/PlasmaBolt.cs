using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// A plasma bolt: a fast glowing projectile (not hitscan). Every client runs one for the visuals; only the
    /// host's copy ("authority") checks for hits and deals damage. No splash.
    /// </summary>
    public class PlasmaBolt : MonoBehaviour
    {
        // (HatterWeaponsPack.cfg, see Settings)
        internal static int Damage => Settings.PlasmaDamage.Value;
        internal static float Speed => Settings.PlasmaBoltSpeed.Value;          // m/s
        private static float Lifetime => Settings.PlasmaBoltLifetime.Value;     // fades out after this
        private const float HitRadius = 0.05f;

        private static readonly Color Cyan = new Color(0.25f, 0.85f, 1f, 1f);

        private PlasmaRifle rifle;
        private int id;
        private bool authority;
        private float age;
        private bool done;
        private PlayerAvatar shooter;   // never takes a direct hit from their own bolt

        private static Material glowMaterial;
        private static Material GlowMaterial
        {
            get
            {
                if (glowMaterial == null)
                {
                    Shader s = Shader.Find("Sprites/Default");
                    glowMaterial = s != null ? new Material(s) : null;
                }
                return glowMaterial;
            }
        }

        private static GameObject GlowSphere(string name, Transform parent, Vector3 scale, Color color)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localScale = scale;
            var r = g.GetComponent<MeshRenderer>();
            if (GlowMaterial != null) r.sharedMaterial = GlowMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
            return g;
        }

        internal static PlasmaBolt Spawn(PlasmaRifle rifle, int id, Vector3 position, Quaternion rotation, bool authority)
        {
            var go = new GameObject("HWP Plasma Bolt " + id);
            go.transform.SetPositionAndRotation(position, rotation);

            // stretched cyan glow with a white-hot core
            GlowSphere("Glow", go.transform, new Vector3(0.07f, 0.07f, 0.32f), new Color(Cyan.r, Cyan.g, Cyan.b, 0.75f));
            GlowSphere("Core", go.transform, new Vector3(0.035f, 0.035f, 0.24f), Color.white);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = Cyan; light.range = 2.5f; light.intensity = 3f;

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.06f; trail.startWidth = 0.05f; trail.endWidth = 0f; trail.minVertexDistance = 0.05f;
            if (GlowMaterial != null) trail.sharedMaterial = GlowMaterial;
            trail.startColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f);
            trail.endColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var p = go.AddComponent<PlasmaBolt>();
            p.rifle = rifle;
            p.id = id;
            p.authority = authority;
            p.shooter = ProjectileHits.Holder(rifle != null ? rifle.PhysGrabObject : null);
            return p;
        }

        private void FixedUpdate()
        {
            if (done) return;
            float dt = Time.fixedDeltaTime;
            age += dt;
            Vector3 from = transform.position;
            Vector3 step = transform.forward * Speed * dt;

            if (authority)
            {
                PhysGrabObject self = rifle != null ? rifle.PhysGrabObject : null;
                bool worldHit = ProjectileHits.World(from, step, HitRadius, self, out RaycastHit hit);
                float worldDistance = worldHit ? Vector3.Distance(from, hit.point) : float.MaxValue;
                if (ProjectileHits.Player(from, step, HitRadius, worldDistance, shooter, out PlayerAvatar player, out Vector3 playerPoint))
                {
                    ProjectileHits.HurtPlayer(player, Damage, playerPoint);
                    Finish(playerPoint);
                    return;
                }
                if (worldHit)
                {
                    ProjectileHits.HurtEnemy(hit.collider, Damage, step.normalized);
                    Finish(hit.point + hit.normal * 0.05f);
                    return;
                }
                if (age >= Lifetime) { Finish(from); return; }
            }
            else if (age >= Lifetime + 1f)
            {
                // visual copy: the host's impact message normally removes it first
                if (rifle != null) rifle.Forget(id);
                Destroy(gameObject);
                return;
            }
            transform.position = from + step;
        }

        private void Finish(Vector3 position)
        {
            done = true;
            if (rifle != null) rifle.HostImpact(id, position);
            else { SpawnImpactFlash(position); Destroy(gameObject); }
        }

        /// <summary>Small cyan flash where a bolt lands (visual only).</summary>
        internal static void SpawnImpactFlash(Vector3 position)
        {
            var go = new GameObject("HWP Plasma Impact");
            go.transform.position = position;
            go.AddComponent<PlasmaImpactFlash>();
        }
    }

    internal class PlasmaImpactFlash : MonoBehaviour
    {
        private const float Duration = 0.18f;
        private static readonly Color Cyan = new Color(0.25f, 0.85f, 1f, 1f);
        private float t;
        private Light flash;
        private MeshRenderer ball;
        private MaterialPropertyBlock block;

        private void Awake()
        {
            flash = gameObject.AddComponent<Light>();
            flash.type = LightType.Point; flash.color = Cyan; flash.range = 3f; flash.intensity = 5f;
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(transform, false);
            ball = g.GetComponent<MeshRenderer>();
            Shader s = Shader.Find("Sprites/Default");
            if (s != null) ball.sharedMaterial = new Material(s);
            ball.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            block = new MaterialPropertyBlock();
            Update();
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / Duration);
            ball.transform.localScale = Vector3.one * Mathf.Lerp(0.06f, 0.4f, k);
            block.SetColor("_Color", new Color(Cyan.r, Cyan.g, Cyan.b, 0.8f * (1f - k)));
            ball.SetPropertyBlock(block);
            flash.intensity = 5f * (1f - k);
            if (k >= 1f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (ball != null && ball.sharedMaterial != null) Destroy(ball.sharedMaterial);
        }
    }
}
