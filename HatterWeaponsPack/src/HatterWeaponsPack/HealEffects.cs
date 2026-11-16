using System.Collections.Generic;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// The Healing Wand's spells. Every client shows them; only the host's copy applies the healing
    /// (PlayerHealth.HealOther, which the game syncs to the healed player).
    /// </summary>
    internal static class HealEffects
    {

        internal static Vector3 Chest(PlayerAvatar p)
        {
            if (p.PlayerVisionTarget != null && p.PlayerVisionTarget.VisionTransform != null)
                return p.PlayerVisionTarget.VisionTransform.position - Vector3.up * 0.3f;
            return p.transform.position + Vector3.up * 1f;
        }

        internal static void Heal(PlayerAvatar p, int amount)
        {
            if (p == null || p.playerHealth == null || amount <= 0) return;
            try
            {
                p.playerHealth.HealOther(amount, true);
                Plugin.Log.LogInfo($"Healing Wand: +{amount} health to {p.name}.");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Heal failed: {e}"); }
        }

        internal static void SpawnBeam(HealingWand wand, Transform tip, PlayerAvatar target, bool authority)
        {
            var go = new GameObject("HWP Heal Beam");
            var beam = go.AddComponent<HealBeam>();
            beam.Setup(tip, target, authority);
        }

        /// <summary>A short harmless beam to a point (the wand was aimed at nobody).</summary>
        internal static void SpawnMissBeam(Transform tip, Vector3 end)
        {
            var go = new GameObject("HWP Heal Beam (miss)");
            go.AddComponent<HealBeam>().SetupMiss(tip, end);
        }

        /// <param name="caster">The player it surrounds and follows (null: it stays where it was cast).</param>
        internal static void SpawnArea(Vector3 centre, bool authority, PlayerAvatar caster)
        {
            var go = new GameObject("HWP Heal Pillar");
            go.AddComponent<HealArea>().Setup(authority, caster != null ? caster.transform : null, centre);
        }

        internal static ParticleSystem Sparkles(Transform parent, float radius, float rate, Vector3 velocity)
        {
            var obj = new GameObject("Sparkles");
            obj.transform.SetParent(parent, false);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 1f, 0.5f), HealingWand.Lime);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission; emission.rateOverTime = rate;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = radius;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = velocity.x; vel.y = velocity.y; vel.z = velocity.z;
            var col = ps.colorOverLifetime; col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(HealingWand.Lime, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            if (FireballFx.Glow != null) pr.sharedMaterial = FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }
    }

    /// <summary>Single-target heal: a lime beam from the wand tip to the target for a few seconds, healing each second.</summary>
    internal class HealBeam : MonoBehaviour
    {
        private const int Points = 14;
        private Transform tip;
        private PlayerAvatar target;
        private bool authority;
        private float age, duration, nextTick = 1f;
        private int healPerTick;
        private LineRenderer outer, core;
        private Light startLight, endLight;
        private Transform targetFx;
        private Vector3 lastTip;

        private bool miss;
        private Vector3 missEnd;

        internal void SetupMiss(Transform tip, Vector3 end)
        {
            miss = true; missEnd = end;
            Build(tip, null, false, 0.45f, 0);
        }

        internal void Setup(Transform tip, PlayerAvatar target, bool authority)
        {
            Build(tip, target, authority, Settings.HealBeamSeconds.Value, Settings.HealBeamPerSecond.Value);
        }

        private void Build(Transform tip, PlayerAvatar target, bool authority, float duration, int healPerTick)
        {
            this.tip = tip; this.target = target; this.authority = authority;
            this.duration = duration;
            this.healPerTick = healPerTick;
            lastTip = tip != null ? tip.position : transform.position;

            outer = Line("Beam", 0.07f, new Color(HealingWand.Lime.r, HealingWand.Lime.g, HealingWand.Lime.b, 0.45f));
            core = Line("Core", 0.025f, new Color(0.85f, 1f, 0.7f, 0.95f));
            startLight = LightAt("Tip Light", 1.5f, 2f);
            endLight = LightAt("Target Light", 2.5f, 3f);
            targetFx = new GameObject("Target Sparkles").transform;
            targetFx.SetParent(transform, false);
            HealEffects.Sparkles(targetFx, 0.35f, 40f, Vector3.up * 0.6f);
            LateUpdate();
        }

        private LineRenderer Line(string name, float width, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var lr = obj.AddComponent<LineRenderer>();
            lr.positionCount = Points;
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.numCapVertices = 4;
            if (FireballFx.Glow != null) lr.sharedMaterial = FireballFx.Glow;
            lr.startColor = color; lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        private Light LightAt(string name, float range, float intensity)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var l = obj.AddComponent<Light>();
            l.type = LightType.Point; l.color = HealingWand.Lime; l.range = range; l.intensity = intensity;
            return l;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            bool targetGone = !miss && target == null;
            if (age >= duration + 0.3f || targetGone) { Destroy(gameObject); return; }

            // host heals once per full second while the beam is up
            if (!miss && authority && age >= nextTick && nextTick <= duration + 0.01f)
            {
                HealEffects.Heal(target, healPerTick);
                nextTick += 1f;
            }

            if (tip != null) lastTip = tip.position;
            Vector3 a = lastTip, b = miss ? missEnd : HealEffects.Chest(target);
            float fade = Mathf.Clamp01(age / 0.15f) * Mathf.Clamp01((duration + 0.3f - age) / 0.3f);
            Vector3 dir = b - a;
            Vector3 side = Vector3.Cross(dir.normalized, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir.normalized);
            for (int i = 0; i < Points; i++)
            {
                float t = i / (float)(Points - 1);
                float env = Mathf.Sin(t * Mathf.PI);                       // pinned at both ends
                float w1 = Mathf.Sin(t * 9f - age * 14f) * 0.05f * env;
                float w2 = Mathf.Cos(t * 7f - age * 11f) * 0.05f * env;
                Vector3 p = a + dir * t;
                outer.SetPosition(i, p + side * w1 + up * w2);
                core.SetPosition(i, p + side * w1 * 0.4f + up * w2 * 0.4f);
            }
            outer.widthMultiplier = 0.07f * fade * (1f + 0.15f * Mathf.Sin(age * 20f));
            core.widthMultiplier = 0.025f * fade;
            startLight.transform.position = a; startLight.intensity = 2f * fade;
            endLight.transform.position = b; endLight.intensity = 3f * fade;
            targetFx.position = b;
        }
    }

    /// <summary>
    /// Area heal: a see-through lime cylinder of light around where the caster stood (it stays there), with small green
    /// beams of light circling inside its edge at different heights, a bright ring on the ground and rising sparkles. Every second the
    /// host heals every player standing inside the cylinder.
    /// </summary>
    internal class HealArea : MonoBehaviour
    {
        private const int Segments = 48;
        private const float Height = 2.2f;          // floor to well over head height
        private const int BeamCount = 6;
        private const float BeamLength = 0.55f;

        private bool authority;
        private float age, duration, radius, nextTick = 1f;
        private int healPerTick;
        private Transform follow;
        private Vector3 basePoint;                  // centre of the cylinder's floor
        private Transform pillar;
        private Mesh pillarMesh;
        private LineRenderer floorRing;
        private LineRenderer[] beams;
        private float[] beamAngle, beamPhase, beamSpeed;
        private Light glow;
        private ParticleSystem sparkles;

        internal void Setup(bool authority, Transform follow, Vector3 centre)
        {
            this.authority = authority;
            this.follow = follow;
            duration = Settings.HealAreaSeconds.Value;
            radius = Settings.HealAreaRadius.Value;
            healPerTick = Settings.HealAreaPerSecond.Value;
            basePoint = follow != null ? follow.position : centre - Vector3.up * 0.9f;
            transform.position = basePoint;

            // the cylinder: an open tube, brightest at the floor and fading out towards the top
            var tube = new GameObject("Heal Cylinder");
            tube.transform.SetParent(transform, false);
            pillarMesh = Tube(radius);
            tube.AddComponent<MeshFilter>().sharedMesh = pillarMesh;
            var mr = tube.AddComponent<MeshRenderer>();
            if (FireballFx.Glow != null) mr.sharedMaterial = FireballFx.Glow;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            pillar = tube.transform;
            pillar.localScale = new Vector3(1f, 0f, 1f);

            floorRing = Line("Floor Ring", Segments, 0.05f, new Color(HealingWand.Lime.r, HealingWand.Lime.g, HealingWand.Lime.b, 0.8f), Color.clear);
            floorRing.loop = true;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                floorRing.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0.02f, Mathf.Sin(a) * radius));
            }

            // small beams of light circling the caster, each at its own height and pace
            beams = new LineRenderer[BeamCount];
            beamAngle = new float[BeamCount];
            beamPhase = new float[BeamCount];
            beamSpeed = new float[BeamCount];
            for (int i = 0; i < BeamCount; i++)
            {
                beams[i] = Line("Beam " + i, 2, 0.035f, new Color(0.8f, 1f, 0.6f, 0.95f), new Color(HealingWand.Lime.r, HealingWand.Lime.g, HealingWand.Lime.b, 0f));
                beamAngle[i] = i * Mathf.PI * 2f / BeamCount;
                beamPhase[i] = Random.Range(0f, Mathf.PI * 2f);
                beamSpeed[i] = Random.Range(1.4f, 2.0f) * (i % 2 == 0 ? 1f : 0.8f);
            }

            var lightObj = new GameObject("Glow");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.up * 1f;
            glow = lightObj.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = HealingWand.Lime; glow.range = radius * 2.5f; glow.intensity = 0f;
            sparkles = HealEffects.Sparkles(transform, radius * 0.9f, 50f, Vector3.up * 0.6f);
            sparkles.transform.localPosition = Vector3.up * 0.4f;
            LateUpdate();
        }

        private LineRenderer Line(string name, int points, float width, Color start, Color end)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var lr = obj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = points;
            lr.widthMultiplier = width;
            lr.numCapVertices = 4;
            if (FireballFx.Glow != null) lr.sharedMaterial = FireballFx.Glow;
            lr.startColor = start; lr.endColor = end;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>An open cylinder of the given radius, Height tall, coloured lime and fading out going up.</summary>
        private static Mesh Tube(float radius)
        {
            float[] heights = { 0f, 0.12f, 0.45f, 1f };          // fraction of Height
            float[] alphas = { 0.42f, 0.28f, 0.12f, 0f };
            int rings = heights.Length, cols = Segments + 1;
            var verts = new Vector3[rings * cols];
            var colors = new Color[verts.Length];
            for (int r = 0; r < rings; r++)
                for (int c = 0; c < cols; c++)
                {
                    float a = c * Mathf.PI * 2f / Segments;
                    verts[r * cols + c] = new Vector3(Mathf.Cos(a) * radius, heights[r] * Height, Mathf.Sin(a) * radius);
                    colors[r * cols + c] = new Color(HealingWand.Lime.r, HealingWand.Lime.g, HealingWand.Lime.b, alphas[r]);
                }
            var tris = new int[(rings - 1) * Segments * 6];
            int t = 0;
            for (int r = 0; r < rings - 1; r++)
                for (int c = 0; c < Segments; c++)
                {
                    int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    tris[t++] = b; tris[t++] = d; tris[t++] = e;
                }
            var mesh = new Mesh { name = "HWP Heal Cylinder" };
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Inside the cylinder: within the radius of its axis, between the floor and the top.</summary>
        private bool Inside(Vector3 p)
        {
            Vector3 d = p - basePoint;
            return new Vector2(d.x, d.z).magnitude <= radius && d.y >= -0.5f && d.y <= Height + 0.3f;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= duration + 0.5f) { Destroy(gameObject); return; }
            if (follow != null) basePoint = follow.position;
            transform.position = basePoint;

            if (authority && age >= nextTick && nextTick <= duration + 0.01f)
            {
                foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
                {
                    if (p == null || p.playerHealth == null) continue;
                    if (Inside(HealEffects.Chest(p))) HealEffects.Heal(p, healPerTick);
                }
                nextTick += 1f;
            }

            // rises out of the floor, hums gently, sinks back at the end
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.35f));
            float shrink = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((duration + 0.5f - age) / 0.5f));
            float k = grow * shrink;
            float pulse = 1f + 0.04f * Mathf.Sin(age * 4f);
            pillar.localScale = new Vector3(pulse, Mathf.Max(0.001f, k), pulse);
            pillar.Rotate(0f, 15f * Time.deltaTime, 0f, Space.Self);
            floorRing.widthMultiplier = 0.05f * k;

            float orbit = radius * 0.92f;
            for (int i = 0; i < beams.Length; i++)
            {
                float a = beamAngle[i] + age * beamSpeed[i];
                float h = 0.15f + (Height - BeamLength - 0.3f) * (0.5f + 0.5f * Mathf.Sin(age * 1.3f + beamPhase[i]));
                Vector3 bottom = new Vector3(Mathf.Cos(a) * orbit, h * k, Mathf.Sin(a) * orbit);
                beams[i].SetPosition(0, bottom);
                beams[i].SetPosition(1, bottom + Vector3.up * BeamLength * k);
                beams[i].widthMultiplier = 0.035f * k;
            }
            glow.intensity = 2.2f * k;
            if (age > duration && sparkles != null) { var e = sparkles.emission; e.enabled = false; }
        }

        private void OnDestroy()
        {
            if (pillarMesh != null) Destroy(pillarMesh);
        }
    }
}
