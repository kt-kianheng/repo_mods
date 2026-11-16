using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// A flying fireball. Every client runs one for the visuals; only the host's copy ("authority") checks for hits,
    /// deals the direct damage and triggers the explosion for everyone. All of its damage hurts players too.
    /// </summary>
    public class Fireball : MonoBehaviour
    {
        private const float HitRadius = 0.08f;

        internal static int DirectDamage => Settings.EmberDirectDamage.Value;
        internal static float Speed => Settings.EmberFireballSpeed.Value;
        private static float Lifetime => Settings.EmberFireballLifetime.Value;

        private ChargedWand wand;   // Ember Wand or Ember Staff
        private int id;
        private bool authority;
        private float age;
        private bool done;
        private PlayerAvatar caster;   // can't take a direct hit from their own fireball (the splash still hurts them)

        internal static Fireball Spawn(ChargedWand wand, int id, Vector3 position, Quaternion rotation, bool authority)
        {
            FireballFx fx = FireballFx.Create("HWP Fireball " + id, position, rotation, flying: true);
            var f = fx.gameObject.AddComponent<Fireball>();
            f.wand = wand;
            f.id = id;
            f.authority = authority;
            f.caster = ProjectileHits.Holder(wand != null ? wand.PhysGrabObject : null);
            return f;
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
                PhysGrabObject self = wand != null ? wand.PhysGrabObject : null;
                bool worldHit = ProjectileHits.World(from, step, HitRadius, self, out RaycastHit hit);
                float worldDistance = worldHit ? Vector3.Distance(from, hit.point) : float.MaxValue;
                if (ProjectileHits.Player(from, step, HitRadius, worldDistance, caster, out PlayerAvatar player, out Vector3 playerPoint))
                {
                    ProjectileHits.HurtPlayer(player, DirectDamage, playerPoint);
                    Burst(playerPoint - step.normalized * 0.1f);
                    return;
                }
                if (worldHit)
                {
                    ProjectileHits.HurtEnemy(hit.collider, DirectDamage, step.normalized);
                    Burst(hit.point + hit.normal * 0.1f);
                    return;
                }
                if (age >= Lifetime) { Burst(from); return; }
            }
            else if (age >= Lifetime + 1.5f)
            {
                // visual copy: the host's explosion message normally removes it first
                if (wand != null) wand.Forget(id);
                Destroy(gameObject);
                return;
            }
            transform.position = from + step;
        }

        private void Burst(Vector3 position)
        {
            done = true;
            if (wand != null) wand.HostImpact(id, position);
            else { Explode(position); Destroy(gameObject); }
        }

        /// <summary>
        /// The fireball's blast (every client): small fiery explosion, splash on enemies and players; everything in the
        /// splash catches fire (Burning), and a patch of fire is left on the floor below (FireArea).
        /// </summary>
        internal static void Explode(Vector3 position, Sound fireSound = null)
        {
            float radius = Settings.EmberSplashRadius.Value;
            RocketExplosion.SpawnSmall(position, Mathf.Clamp(radius * 0.6f, 0.3f, 3f), radius,
                Settings.EmberSplashDamage.Value, Settings.EmberKnockback.Value, Settings.EmberValuableBreak.Value);
            try
            {
                Burning.IgniteColliders(Physics.OverlapSphere(position, radius, ProjectileHits.Mask, QueryTriggerInteraction.Ignore));
                foreach (PlayerAvatar p in Burning.PlayersNear(position, radius)) Burning.Ignite(p);
                FireArea.Spawn(position, fireSound);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Fireball fire failed: {e}"); }
        }
    }
}
