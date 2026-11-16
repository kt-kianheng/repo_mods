using System.Collections.Generic;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// The frozen look on an enemy hit by an icicle (like the Lightning Wand's electrocuted look): ice shards growing
    /// out of its body, frost flakes and cold mist drifting off it, and a pale blue glow, for as long as the stun.
    /// One per enemy: another hit just makes it last longer. Visual only; the host's icicle does the stun.
    /// </summary>
    internal class FrostEffect : MonoBehaviour
    {
        private static readonly Color Pale = new Color(0.75f, 0.95f, 1f, 1f);
        private static readonly Color Ice = new Color(0.4f, 0.85f, 1f, 1f);
        private static readonly Dictionary<Enemy, FrostEffect> active = new Dictionary<Enemy, FrostEffect>();
        private const float GrowSeconds = 0.25f, MeltSeconds = 0.35f;

        private Enemy enemy;
        private Transform follow;
        private Vector3 point, extents;
        private Vector3 localOffset;                   // body centre, in the followed transform's space
        private float duration, age;
        private Transform[] shards;
        private Vector3[] shardOffsets;                // in the followed transform's space
        private Quaternion[] shardRotations;           // likewise
        private float[] shardSizes;
        private Light glow;
        private ParticleSystem flakes, mist;

        /// <summary>Frost over the enemy nearest this point (where an icicle landed), if there is one.</summary>
        internal static void Near(Vector3 position, float seconds)
        {
            if (seconds <= 0f) return;
            Enemy best = NearestEnemy(position);
            if (best != null) Apply(best, seconds);
        }

        /// <summary>The enemy with a body collider closest to this point, within 0.6 m (where an icicle landed).</summary>
        internal static Enemy NearestEnemy(Vector3 position)
        {
            Enemy best = null;
            float bestDistance = float.MaxValue;
            foreach (Collider c in Physics.OverlapSphere(position, 0.6f, ~0, QueryTriggerInteraction.Ignore))
            {
                Enemy e = LightningTargets.Of(c);
                if (e == null) continue;
                float d = Vector3.Distance(position, c.ClosestPoint(position));
                if (d < bestDistance) { bestDistance = d; best = e; }
            }
            return best;
        }

        /// <summary>Is frost showing on this enemy (it's chilled/slowed) on this game?</summary>
        internal static bool IsOn(Enemy enemy) =>
            enemy != null && active.TryGetValue(enemy, out FrostEffect fx) && fx != null && fx.age < fx.duration;

        /// <summary>The frost melts away now (fire thawed the enemy).</summary>
        internal static void Melt(Enemy enemy)
        {
            if (enemy != null && active.TryGetValue(enemy, out FrostEffect fx) && fx != null && fx.age < fx.duration) fx.duration = fx.age;
        }

        internal static void Apply(Enemy enemy, float seconds)
        {
            if (seconds <= 0f || LightningTargets.Gone(enemy)) return;
            if (active.TryGetValue(enemy, out FrostEffect fx) && fx != null)
            {
                fx.duration = Mathf.Max(fx.duration, fx.age + seconds);   // hit again: stay frozen longer
                return;
            }
            var go = new GameObject("HWP Frozen");
            fx = go.AddComponent<FrostEffect>();
            fx.Setup(enemy, seconds);
            active[enemy] = fx;
        }

        private void Setup(Enemy e, float seconds)
        {
            enemy = e;
            duration = seconds;
            follow = LightningTargets.CentreTransform(e);
            point = follow != null ? follow.position : e.transform.position;

            // size and centre from the enemy's body colliders
            extents = new Vector3(0.35f, 0.5f, 0.35f);
            Vector3 centre = point;
            bool any = false;
            Bounds b = default;
            foreach (Collider c in LightningTargets.Body(e).GetComponentsInChildren<Collider>())
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (any)
            {
                extents = new Vector3(Mathf.Clamp(b.extents.x, 0.15f, 2f), Mathf.Clamp(b.extents.y, 0.15f, 2.5f), Mathf.Clamp(b.extents.z, 0.15f, 2f));
                centre = b.center;
            }
            localOffset = follow != null ? Quaternion.Inverse(follow.rotation) * (centre - follow.position) : Vector3.zero;
            float size = Mathf.Max(extents.x, extents.y, extents.z);

            // ice shards jutting out of the body, pointing outwards
            int count = Mathf.Clamp(Mathf.RoundToInt(size * 12f), 5, 12);
            shards = new Transform[count];
            shardOffsets = new Vector3[count];
            shardRotations = new Quaternion[count];
            shardSizes = new float[count];
            Quaternion toLocal = follow != null ? Quaternion.Inverse(follow.rotation) : Quaternion.identity;
            for (int i = 0; i < count; i++)
            {
                Vector3 n = Random.onUnitSphere;
                n.y = Mathf.Abs(n.y) * 0.8f + 0.1f;                       // mostly sideways and up, few underneath
                n.Normalize();
                Vector3 onBody = Vector3.Scale(n, extents) * 0.85f;        // just inside the surface, so the shard sticks out of it
                Vector3 outward = (Vector3.Scale(n, new Vector3(1f / extents.x, 1f / extents.y, 1f / extents.z))).normalized;
                outward = (outward + Random.insideUnitSphere * 0.35f).normalized;
                shardOffsets[i] = toLocal * (centre - (follow != null ? follow.position : centre) + onBody);
                shardRotations[i] = toLocal * Quaternion.LookRotation(outward);
                shardSizes[i] = Mathf.Clamp(size * 0.45f, 0.2f, 0.6f) * Random.Range(0.6f, 1.1f);

                GameObject shard = Icicle.CreateVisual("Frost Shard");
                Light l = shard.GetComponent<Light>();
                if (l != null) Destroy(l);                                  // one light for the whole effect
                shard.transform.SetParent(transform, true);
                shard.transform.localScale = Vector3.zero;
                shards[i] = shard.transform;
            }

            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = Ice; glow.range = Mathf.Max(1f, size * 4f); glow.intensity = 0f;

            flakes = Particles("Frost Flakes", size, 0.01f, 0.035f, 0.6f, 1.4f, 0.05f, 0.4f, Mathf.Clamp(size * 50f, 10f, 50f), Pale, Ice, 1f);
            mist = Particles("Cold Mist", size, 0.15f, 0.35f, 0.8f, 1.6f, 0.05f, 0.2f, Mathf.Clamp(size * 12f, 4f, 14f),
                             new Color(0.8f, 0.95f, 1f, 0.12f), new Color(0.6f, 0.9f, 1f, 0.06f), -0.05f);
            LateUpdate();
        }

        private ParticleSystem Particles(string name, float size, float minSize, float maxSize, float minLife, float maxLife,
                                         float minSpeed, float maxSpeed, float rate, Color a, Color b, float gravity)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity * 0.1f;                         // flakes drift down slowly; mist rises a touch
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission; emission.rateOverTime = rate;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 1f; shape.scale = extents;
            var col = ps.colorOverLifetime; col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            if (FireballFx.Glow != null) pr.sharedMaterial = FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age < duration && LightningTargets.Gone(enemy))
            {
                // the enemy died: melt away now, where it fell
                duration = age;
                if (follow != null) { point = follow.position; follow = null; }
            }
            if (age >= duration + MeltSeconds)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 basePos = follow != null ? follow.position : point;
            Quaternion baseRot = follow != null ? follow.rotation : Quaternion.identity;
            transform.position = basePos + baseRot * localOffset;

            // grow in, hold, melt away at the end
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / GrowSeconds));
            float melt = Mathf.Clamp01((duration + MeltSeconds - age) / MeltSeconds);
            float k = grow * melt;
            for (int i = 0; i < shards.Length; i++)
            {
                if (shards[i] == null) continue;
                shards[i].SetPositionAndRotation(basePos + baseRot * shardOffsets[i], baseRot * shardRotations[i]);
                float s = shardSizes[i] * k;
                shards[i].localScale = new Vector3(s, s, s);
            }
            glow.intensity = (1.4f + 0.2f * Mathf.Sin(age * 5f)) * k;
            if (age > duration)
            {
                var e1 = flakes.emission; e1.enabled = false;
                var e2 = mist.emission; e2.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (enemy != null && active.TryGetValue(enemy, out FrostEffect fx) && fx == this) active.Remove(enemy);
            else
            {
                // the enemy is gone: drop any stale entries
                var stale = new List<Enemy>();
                foreach (var kv in active) if (kv.Key == null || kv.Value == null || kv.Value == this) stale.Add(kv.Key);
                foreach (Enemy key in stale) active.Remove(key);
            }
        }
    }
}
