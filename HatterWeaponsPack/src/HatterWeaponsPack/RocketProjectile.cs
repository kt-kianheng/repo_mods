using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// A flying rocket. Every client runs one for the visuals; only the host's copy ("authority") checks for hits,
    /// deals the contact damage and triggers the explosion for everyone.
    /// </summary>
    public class RocketProjectile : MonoBehaviour
    {
        internal static int ContactDamage => Settings.RocketContactDamage.Value;   // enemies also take the close splash
        private const float HitRadius = 0.07f;

        private RocketLauncher launcher;
        private int id;
        private bool authority;
        private float age;
        private bool done;
        private PlayerAvatar shooter;   // never takes the direct hit from their own rocket

        internal static RocketProjectile Spawn(RocketLauncher launcher, int id, Vector3 position, Quaternion rotation, bool authority)
        {
            MeshLibrary.Model model = RocketLauncherBuilder.RocketModel;
            if (model == null) { Plugin.Log.LogWarning("Rocket model not loaded; rocket has no visuals."); }
            var go = new GameObject("HWP Rocket " + id);
            go.transform.SetPositionAndRotation(position, rotation);

            // model: re-centred on the rocket's middle (it was exported in its loaded position)
            if (model != null) MeshLibrary.Attach(model, go.transform, "Mesh", RocketLauncherBuilder.MaterialTemplate, -RocketLauncherBuilder.LoadedRocketCenter);

            // exhaust glow + smoke trail out of the back
            var glow = new GameObject("Exhaust");
            glow.transform.SetParent(go.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, -0.30f);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.55f, 0.2f); light.range = 3f; light.intensity = 3f;
            var trail = glow.AddComponent<TrailRenderer>();
            trail.time = 0.35f; trail.startWidth = 0.09f; trail.endWidth = 0.25f; trail.minVertexDistance = 0.05f;
            Shader sprites = Shader.Find("Sprites/Default");
            if (sprites != null) trail.material = new Material(sprites);
            trail.startColor = new Color(1f, 0.75f, 0.45f, 0.9f);
            trail.endColor = new Color(0.55f, 0.55f, 0.55f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var p = go.AddComponent<RocketProjectile>();
            p.launcher = launcher;
            p.id = id;
            p.authority = authority;
            p.shooter = ProjectileHits.Holder(launcher != null ? launcher.PhysGrabObject : null);
            return p;
        }

        private void FixedUpdate()
        {
            if (done) return;
            float dt = Time.fixedDeltaTime;
            age += dt;
            Vector3 from = transform.position;
            Vector3 step = transform.forward * RocketLauncher.RocketSpeed * dt;

            if (authority)
            {
                PhysGrabObject self = launcher != null ? launcher.PhysGrabObject : null;
                bool worldHit = ProjectileHits.World(from, step, HitRadius, self, out RaycastHit hit);
                float worldDistance = worldHit ? Vector3.Distance(from, hit.point) : float.MaxValue;
                if (ProjectileHits.Player(from, step, HitRadius, worldDistance, shooter, out PlayerAvatar player, out Vector3 playerPoint))
                {
                    ProjectileHits.HurtPlayer(player, ContactDamage, playerPoint);
                    Detonate(playerPoint - step.normalized * 0.15f, null, step.normalized);
                    return;
                }
                if (worldHit)
                {
                    Detonate(hit.point + hit.normal * 0.15f, hit.collider, step.normalized);
                    return;
                }
                if (age >= RocketLauncher.RocketLifetime)
                {
                    Detonate(from, null, step.normalized);
                    return;
                }
            }
            else if (age >= RocketLauncher.RocketLifetime + 1.5f)
            {
                // visual copy: the host's explosion message normally removes it first
                if (launcher != null) launcher.Forget(id);
                Destroy(gameObject);
                return;
            }
            transform.position = from + step;
        }

        private void Detonate(Vector3 position, Collider hitCollider, Vector3 direction)
        {
            done = true;
            ProjectileHits.HurtEnemy(hitCollider, ContactDamage, direction);   // contact damage to the enemy struck
            if (launcher != null) launcher.HostExplode(id, position);
            else { RocketExplosion.Spawn(position); Destroy(gameObject); }
        }
    }
}
