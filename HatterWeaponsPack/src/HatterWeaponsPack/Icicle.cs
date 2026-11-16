using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// A flying icicle. Every client runs one for the visuals; only the host's copy ("authority") checks for hits and
    /// deals the damage (enemies and players). No splash: it shatters where it lands.
    /// </summary>
    public class Icicle : MonoBehaviour
    {
        private const float HitRadius = 0.05f;
        private static readonly Color Cyan = new Color(0.4f, 0.85f, 1f);

        private int Damage => wand != null ? wand.IcicleDamage : Settings.FrostIcicleDamage.Value;   // the Frost Staff's hit harder
        private static float Speed => Settings.FrostIcicleSpeed.Value;
        private static float Lifetime => Settings.FrostIcicleLifetime.Value;

        private FrostWand wand;
        private int id;
        private bool authority;
        private float age;
        private bool done;
        private PlayerAvatar caster;   // can't hit the player who cast it

        /// <summary>The icicle model (pointing along +Z, centred), with a faint cyan light.</summary>
        internal static GameObject CreateVisual(string name)
        {
            var go = new GameObject(name);
            MeshLibrary.Model model = WandBuilder.IcicleModel;
            if (model != null) MeshLibrary.Attach(model, go.transform, "Mesh", WandBuilder.MaterialTemplate);
            else Plugin.Log.LogWarning("Icicle model not loaded; icicles have no visuals.");
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = Cyan; light.range = 1.2f; light.intensity = 0.8f;
            return go;
        }

        internal static Icicle Spawn(FrostWand wand, int id, Vector3 position, Quaternion rotation, bool authority)
        {
            GameObject go = CreateVisual("HWP Icicle " + id);
            go.transform.SetPositionAndRotation(position, rotation);

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.12f; trail.startWidth = 0.05f; trail.endWidth = 0f; trail.minVertexDistance = 0.05f;
            if (FireballFx.Glow != null) trail.sharedMaterial = FireballFx.Glow;
            trail.startColor = new Color(0.75f, 0.95f, 1f, 0.6f);
            trail.endColor = new Color(0.4f, 0.85f, 1f, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var ice = go.AddComponent<Icicle>();
            ice.wand = wand;
            ice.id = id;
            ice.authority = authority;
            ice.caster = ProjectileHits.Holder(wand != null ? wand.PhysGrabObject : null);
            return ice;
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
                    ProjectileHits.HurtPlayer(player, Damage, playerPoint);
                    Finish(playerPoint);
                    return;
                }
                if (worldHit)
                {
                    ProjectileHits.HurtEnemy(hit.collider, Damage, step.normalized);
                    Enemy struck = LightningTargets.Of(hit.collider);
                    if (struck != null && !LightningTargets.IsDead(struck) && Elements.CanIce(struck))   // fresh fire resists
                    {
                        // frozen in place: the game's freeze (holds the body still, synced to everyone) plus a stun so it can't act
                        float seconds = Settings.FrostStunSeconds.Value;
                        try { struck.Freeze(seconds); }
                        catch (System.Exception e) { Plugin.Log.LogWarning($"Icicle freeze failed: {e}"); }
                        LightningTargets.Stun(struck, seconds);
                    }
                    Finish(hit.point + hit.normal * 0.03f);
                    return;
                }
                if (age >= Lifetime) { Finish(from); return; }
            }
            else if (age >= Lifetime + 1.5f)
            {
                if (wand != null) wand.Forget(id);
                Destroy(gameObject);
                return;
            }
            transform.position = from + step;
        }

        private void Finish(Vector3 position)
        {
            done = true;
            if (wand != null) wand.HostImpact(id, position);
            else
            {
                IceShatter.Spawn(position);
                Enemy e = Elements.IcicleImpact(position);
                if (e != null) IceEncasement.Encase(e, null, Settings.FrostStunSeconds.Value);
                Destroy(gameObject);
            }
        }
    }

    /// <summary>Where an icicle lands: a burst of ice shards and a cold flash (visual only).</summary>
    internal class IceShatter : MonoBehaviour
    {
        private const float Duration = 0.6f;
        private float t;
        private Light flash;

        internal static void Spawn(Vector3 position)
        {
            var go = new GameObject("HWP Ice Shatter");
            go.transform.position = position;
            go.AddComponent<IceShatter>();
        }

        private void Awake()
        {
            flash = gameObject.AddComponent<Light>();
            flash.type = LightType.Point; flash.color = new Color(0.5f, 0.9f, 1f); flash.range = 2f; flash.intensity = 3f;

            var ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.97f, 1f), new Color(0.4f, 0.85f, 1f));
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)18) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.03f;
            var pr = GetComponent<ParticleSystemRenderer>();
            if (FireballFx.Glow != null) pr.sharedMaterial = FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        private void Update()
        {
            t += Time.deltaTime;
            flash.intensity = 3f * Mathf.Clamp01(1f - t / 0.15f);
            if (t >= Duration) Destroy(gameObject);
        }
    }
}
