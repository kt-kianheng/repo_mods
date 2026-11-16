using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// The Lightning Staff's orb: a slow, big ball of lightning (OrbSpeed, OrbRadius) that flies where it was aimed, bouncing off walls
    /// and props, with arcs crackling round it and an electric buzz, for OrbSeconds. Every OrbSecondsBetweenStrikes the host's
    /// copy strikes the nearest enemy within OrbStrikeRange that it can see, chaining on like the staff's strikes
    /// (LightningStaff.HostOrbStrike shows it on every game). Players are never targets and it passes through them.
    /// Every client flies its own copy; the host's sends its position now and then and the others ease onto it.
    /// </summary>
    internal class LightningOrb : MonoBehaviour
    {
        private float size;                            // OrbRadius: the glow's radius
        private float bounceRadius;                    // what bounces off walls: a third of that, so it fits through doors
        private const float SyncInterval = 0.25f;
        private const float FadeSeconds = 0.5f;

        internal int Id { get; private set; }
        internal Vector3 Velocity { get; private set; }

        private LightningStaff staff;
        private bool authority;
        private float speed, life, age, nextStrike, nextSync, nextHomingLook;
        private Enemy homingTarget;
        private Vector3 correction;                    // clients: what's left of the last correction to blend in
        private SoftGlow core, halo;
        private Light glow;
        private ShockEffect arcs;
        private static int obstacleMask = -1;

        private static int ObstacleMask
        {
            get
            {
                // walls, doors, props and items: not players, enemies or triggers
                if (obstacleMask == -1)
                    obstacleMask = LayerMask.GetMask("Default", "PhysGrabObject", "PhysGrabObjectCart", "PhysGrabObjectHinge", "StaticGrabObject");
                return obstacleMask;
            }
        }

        internal static LightningOrb Spawn(LightningStaff staff, int id, Vector3 start, Vector3 direction, bool authority, Sound buzz)
        {
            var go = new GameObject("HWP Lightning Orb " + id);
            go.transform.position = start;
            var orb = go.AddComponent<LightningOrb>();
            orb.Setup(staff, id, direction, authority, buzz);
            return orb;
        }

        private void Setup(LightningStaff staff, int id, Vector3 direction, bool authority, Sound buzz)
        {
            this.staff = staff;
            Id = id;
            this.authority = authority;
            speed = Settings.LStaffOrbSpeed.Value;
            size = Mathf.Max(0.1f, Settings.LStaffOrbRadius.Value);
            bounceRadius = Mathf.Clamp(size / 3f, 0.1f, 0.5f);
            life = Settings.LStaffOrbSeconds.Value;
            nextStrike = Mathf.Min(1f, Settings.LStaffOrbStrikeInterval.Value);   // the first strike comes soon after it's cast
            Velocity = (direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward) * speed;

            halo = SoftGlow.Create(transform, Vector3.zero, "Orb Halo");
            core = SoftGlow.Create(transform, Vector3.zero, "Orb Core");
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = LightningWand.Yellow; glow.range = Mathf.Max(4f, size * 8f); glow.intensity = 0f;
            // arcs crackling round it, with the electric crackle (played on repeat by ShockEffect)
            arcs = ShockEffect.Create(transform, Vector3.zero, Vector3.one * size * 0.85f, life + FadeSeconds, buzz, 1.5f);
            LateUpdate();
        }

        /// <summary>Clients: the host's position and velocity; eased in over the next moments.</summary>
        internal void Correct(Vector3 position, Vector3 velocity)
        {
            correction = position - transform.position;
            if (correction.magnitude > 3f) { transform.position = position; correction = Vector3.zero; }   // far off: snap
            Velocity = velocity;
        }

        private void FixedUpdate()
        {
            if (age >= life) return;
            float dt = Time.fixedDeltaTime;

            // fly, bouncing off obstacles (up to a few bounces a step, in corners)
            Vector3 pos = transform.position;
            Vector3 dir = Velocity.sqrMagnitude > 1e-6f ? Velocity.normalized : Vector3.forward;

            // a very slight pull towards the nearest enemy in reach (every client; the host's syncs keep them together)
            if (age >= nextHomingLook)
            {
                nextHomingLook = age + 0.25f;
                float reach = Settings.LStaffOrbHomingRange.Value;
                homingTarget = reach > 0f ? LightningTargets.NearestFrom(pos, reach) : null;
            }
            if (homingTarget != null && !LightningTargets.Gone(homingTarget))
            {
                Vector3 to = LightningTargets.Centre(homingTarget) - pos;
                if (to.sqrMagnitude > 0.01f)
                    dir = Vector3.RotateTowards(dir, to.normalized, Settings.LStaffOrbHomingTurn.Value * Mathf.Deg2Rad * dt, 0f).normalized;
            }
            float remaining = speed * dt;
            for (int bounce = 0; bounce < 3 && remaining > 1e-4f; bounce++)
            {
                if (Physics.SphereCast(pos, bounceRadius, dir, out RaycastHit hit, remaining, ObstacleMask, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PlayerAvatar>() == null && LightningTargets.Of(hit.collider) == null)
                {
                    pos += dir * Mathf.Max(0f, hit.distance - 0.005f) + hit.normal * 0.01f;
                    dir = Vector3.Reflect(dir, hit.normal).normalized;
                    remaining -= hit.distance;
                }
                else
                {
                    pos += dir * remaining;
                    remaining = 0f;
                }
            }
            if (correction.sqrMagnitude > 1e-6f)
            {
                Vector3 bit = correction * Mathf.Clamp01(dt * 6f);
                pos += bit;
                correction -= bit;
            }
            transform.position = pos;
            Velocity = dir * speed;

            if (!authority) return;
            if (age >= nextSync)
            {
                nextSync = age + SyncInterval;
                if (staff != null) staff.HostOrbSync(this);
            }
            if (age >= nextStrike)
            {
                nextStrike += Mathf.Max(0.2f, Settings.LStaffOrbStrikeInterval.Value);
                Enemy target = LightningTargets.NearestFrom(pos, Settings.LStaffOrbStrikeRange.Value);
                if (target != null && staff != null)
                {
                    int charges = Mathf.Max(1, Settings.LightningChargesPerBolt.Value);
                    int max = Mathf.Clamp(Settings.LightningMaxTargets.Value, 1, charges);
                    staff.HostOrbStrike(this, LightningTargets.ChainFrom(target, Settings.LightningChainRange.Value, max));
                }
            }
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= life + FadeSeconds)
            {
                if (staff != null) staff.ForgetOrb(this);
                Destroy(gameObject);
                return;
            }
            float rise = Mathf.Clamp01(age / 0.25f);
            float fade = Mathf.Clamp01((life + FadeSeconds - age) / FadeSeconds);
            float k = rise * fade;
            float flicker = Random.Range(0.85f, 1.15f);
            core.SetSize(size * 0.9f * k * flicker);            // SetSize takes the diameter: a bright core half the orb
            core.SetColor(new Color(1f, 0.98f, 0.85f, 0.95f * k));
            halo.SetSize(size * 2.2f * k * (1f + 0.1f * Mathf.Sin(age * 9f)));   // the glow, a little past the radius
            halo.SetColor(new Color(LightningWand.Yellow.r, LightningWand.Yellow.g, LightningWand.Yellow.b, 0.55f * k));
            glow.intensity = 4f * k * flicker;
        }

        private void OnDestroy()
        {
            if (arcs != null) Destroy(arcs.gameObject);
        }
    }
}
