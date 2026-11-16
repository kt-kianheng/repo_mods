using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// The fireball's look, built from simple glowing layers (no assets): a hot yellow core, an orange flame body,
    /// a see-through red outer flame, all flickering; in flight it adds a fiery trail, drifting embers and a warm light.
    /// Size is 0..1 of the full fireball (it grows at the wand tip while charging).
    /// </summary>
    internal class FireballFx : MonoBehaviour
    {
        internal const float FullRadius = 0.1f;                 // outer flame radius at full size (about 20 cm across)

        private static readonly Color Core = new Color(1f, 0.93f, 0.6f, 1f);
        private static readonly Color Mid = new Color(1f, 0.45f, 0.05f, 0.85f);
        private static readonly Color Outer = new Color(0.95f, 0.12f, 0.02f, 0.45f);
        private static readonly Color Ember = new Color(1f, 0.55f, 0.1f, 1f);

        private static Material material;
        internal static Material Glow
        {
            get
            {
                if (material == null)
                {
                    Shader s = Shader.Find("Sprites/Default");
                    if (s != null) { material = new Material(s) { name = "HWP Fireball" }; Object.DontDestroyOnLoad(material); }
                }
                return material;
            }
        }

        private Transform core, mid, outer;
        private Transform[] tongues;
        private Light glowLight;
        private float size = 1f;
        private float seed;

        internal static FireballFx Create(string name, Vector3 position, Quaternion rotation, bool flying)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            var fx = go.AddComponent<FireballFx>();
            fx.Build(flying);
            return fx;
        }

        internal float Size
        {
            get => size;
            set { size = Mathf.Clamp01(value); }
        }

        private Transform Layer(string name, Color color)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(transform, false);
            var r = g.GetComponent<MeshRenderer>();
            if (Glow != null) r.sharedMaterial = Glow;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
            return g.transform;
        }

        private void Build(bool flying)
        {
            seed = Random.value * 100f;
            // drawn back to front: outer flame, body, core
            outer = Layer("Outer", Outer);
            mid = Layer("Body", Mid);
            core = Layer("Core", Core);
            tongues = new Transform[4];
            for (int i = 0; i < tongues.Length; i++) tongues[i] = Layer("Tongue", i % 2 == 0 ? Mid : Outer);

            glowLight = gameObject.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = new Color(1f, 0.45f, 0.15f);

            if (!flying) return;

            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.25f; trail.startWidth = 0.16f; trail.endWidth = 0f; trail.minVertexDistance = 0.04f;
            if (Glow != null) trail.sharedMaterial = Glow;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.6f, 0.1f), 0f), new GradientColorKey(new Color(0.9f, 0.15f, 0.02f), 0.5f), new GradientColorKey(new Color(0.2f, 0.05f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // embers drifting off behind it
            var emberObj = new GameObject("Embers");
            emberObj.transform.SetParent(transform, false);
            var ps = emberObj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
            main.startColor = Ember;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            main.gravityModifier = -0.05f;
            var emission = ps.emission; emission.rateOverTime = 45f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.06f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Ember, 0f), new GradientColorKey(new Color(0.8f, 0.1f, 0.02f), 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var pr = emberObj.GetComponent<ParticleSystemRenderer>();
            if (Glow != null) pr.sharedMaterial = Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        private void LateUpdate()
        {
            float t = Time.time * 9f + seed;
            float flicker = 1f + 0.08f * Mathf.Sin(t) + 0.05f * Mathf.Sin(t * 2.3f + 1f);
            float r = FullRadius * size;
            // stretched slightly backwards (the fireball flies along +Z)
            core.localScale = new Vector3(0.9f, 0.9f, 0.95f) * r * flicker;
            mid.localScale = new Vector3(1.5f, 1.5f, 1.85f) * r * (2f - flicker);
            mid.localPosition = new Vector3(0f, 0f, -0.12f * r);
            outer.localScale = new Vector3(2f, 2f, 2.9f) * r * flicker;
            outer.localPosition = new Vector3(0f, 0f, -0.3f * r);
            for (int i = 0; i < tongues.Length; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f * Mathf.Sin(t * 0.7f + i);
                float wobble = 1f + 0.2f * Mathf.Sin(t * 1.3f + i * 2f);
                tongues[i].localPosition = new Vector3(Mathf.Cos(a) * 0.35f * r, Mathf.Sin(a) * 0.35f * r, -1.4f * r * wobble);
                tongues[i].localScale = new Vector3(0.75f, 0.75f, 2.6f * wobble) * r;
            }
            glowLight.range = 0.5f + 3.5f * size;
            glowLight.intensity = (1f + 2f * size) * flicker;
        }
    }
}
