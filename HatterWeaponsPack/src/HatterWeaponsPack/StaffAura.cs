using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// A staff's "charged" look round its crystal, shown on every game while the charged spell is ready:
    ///  - while the staff is held up charging, sparks gather in round the crystal and the glow builds;
    ///  - the charge sound is started so that its peak lands as the charge completes, with a bright flash: a burst of
    ///    light, a flare that swells and fades, sparks spraying out and soft puffs of light;
    ///  - then, while it stays ready, a dense swirl of sparks orbiting the crystal, glowing motes rising off it and a
    ///    pulsing glow and light.
    /// </summary>
    internal class StaffAura : MonoBehaviour
    {
        internal class Palette
        {
            public Color Glow;     // the halo and light
            public Color Hot;      // spark cores
            public Color Cool;     // what sparks fade to
            public float Gravity;  // the burst's sparks fall (embers) or float (frost)
        }

        internal static readonly Palette Ember = new Palette
        {
            Glow = new Color(1f, 0.32f, 0.05f), Hot = new Color(1f, 0.9f, 0.55f), Cool = new Color(0.95f, 0.15f, 0.02f), Gravity = 0.25f,
        };
        internal static readonly Palette Frost = new Palette
        {
            Glow = FrostWand.Cyan, Hot = new Color(0.92f, 0.99f, 1f), Cool = new Color(0.25f, 0.75f, 1f), Gravity = 0f,
        };
        internal static readonly Palette Lightning = new Palette
        {
            Glow = new Color(1f, 0.86f, 0.12f), Hot = new Color(1f, 0.98f, 0.8f), Cool = new Color(1f, 0.7f, 0.05f), Gravity = 0f,
        };

        private const float FlashSeconds = 0.4f;

        private Transform follow;
        private Palette palette;
        private Sound sound;
        private float soundLead, chargeSeconds;
        private bool soundPlayed, wasArmed;
        private float progress;
        private SoftGlow glow, flare;
        private Light glowLight;
        private ParticleSystem swirl, motes, burst, puffs;
        private float age, amount, flashAge = -1f;

        /// <param name="sound">The charge sound (null: silent).</param>
        /// <param name="soundLead">Seconds from the sound's start to its peak: it starts this long before the charge completes.</param>
        /// <param name="chargeSeconds">How long the charge takes.</param>
        internal static StaffAura Create(Transform follow, Palette palette, Sound sound, float soundLead, float chargeSeconds)
        {
            var go = new GameObject("HWP Staff Aura");
            var aura = go.AddComponent<StaffAura>();
            aura.follow = follow;
            aura.palette = palette;
            aura.sound = sound;
            aura.soundLead = Mathf.Max(0f, soundLead);
            aura.chargeSeconds = Mathf.Max(0.05f, chargeSeconds);
            aura.Build();
            return aura;
        }

        private void Build()
        {
            glow = SoftGlow.Create(transform, Vector3.zero, "Aura Glow");
            flare = SoftGlow.Create(transform, Vector3.zero, "Flash");
            glow.SetSize(0f);
            flare.SetSize(0f);

            var lightObj = new GameObject("Aura Light");
            lightObj.transform.SetParent(transform, false);
            glowLight = lightObj.AddComponent<Light>();
            glowLight.type = LightType.Point; glowLight.color = palette.Glow; glowLight.intensity = 0f; glowLight.range = 1.5f;

            // sparks orbiting the crystal (local space, so they stay with the staff)
            swirl = Particles("Swirl", ParticleSystemSimulationSpace.Local, 0.14f, 0f, 0f, 0.5f, 0.9f, 0.012f, 0.028f, 200, Glow());
            var vel = swirl.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            // Unity wants each group of these curves in the same mode: all "between two constants"
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalX = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(4f, 6f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.radial = new ParticleSystem.MinMaxCurve(-0.2f, -0.1f);

            // soft glowing motes drifting up off it
            motes = Particles("Motes", ParticleSystemSimulationSpace.World, 0.08f, 0.1f, 0.3f, 0.7f, 1.2f, 0.04f, 0.09f, 60, SoftGlow.SharedMaterial ?? Glow());
            var mmain = motes.main; mmain.gravityModifier = -0.04f;   // drift upwards

            // the flash: sparks spraying out, and a few big soft puffs of light
            burst = Particles("Burst", ParticleSystemSimulationSpace.World, 0.03f, 2.5f, 5.5f, 0.3f, 0.65f, 0.015f, 0.035f, 300, Glow());
            var bmain = burst.main; bmain.gravityModifier = palette.Gravity;
            puffs = Particles("Puffs", ParticleSystemSimulationSpace.World, 0.04f, 0.6f, 1.3f, 0.35f, 0.6f, 0.15f, 0.3f, 40, SoftGlow.SharedMaterial ?? Glow());
        }

        private static Material Glow() => FireballFx.Glow;

        private ParticleSystem Particles(string name, ParticleSystemSimulationSpace space, float radius, float minSpeed, float maxSpeed,
                                         float minLife, float maxLife, float minSize, float maxSize, int max, Material material)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(palette.Hot, palette.Glow);
            main.simulationSpace = space;
            main.maxParticles = max;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = Mathf.Max(0.005f, radius);
            var col = ps.colorOverLifetime; col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(palette.Hot, 0f), new GradientColorKey(palette.Glow, 0.4f), new GradientColorKey(palette.Cool, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            if (material != null) pr.sharedMaterial = material;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>Each frame: how far the charge has got (0..1) and whether the spell is ready.</summary>
        internal void SetState(float charge, bool armed)
        {
            progress = armed ? 1f : Mathf.Clamp01(charge);
            // the charge sound, timed so its peak lands as the charge completes
            if (!armed && progress > 0f && !soundPlayed && (1f - progress) * chargeSeconds <= soundLead)
            {
                soundPlayed = true;
                if (sound != null && follow != null)
                {
                    try { sound.Play(follow, 1f, 1f, 1f, 1f); }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Staff charge sound failed: {e}"); sound = null; }
                }
            }
            if (armed && !wasArmed) Flash();
            if (!armed && progress <= 0f) soundPlayed = false;   // charge cancelled or spell used: ready for the next
            wasArmed = armed;
        }

        private void Flash()
        {
            flashAge = 0f;
            burst.Emit(90);
            puffs.Emit(10);
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (follow == null) { Destroy(gameObject); return; }
            transform.position = follow.position;

            // building over the charge, then fully charged
            float target = wasArmed ? 1f : 0.5f * progress * progress;
            amount = Mathf.MoveTowards(amount, target, Time.deltaTime * (target > amount ? 3f : 4f));

            float pulse = 1f + 0.15f * Mathf.Sin(age * 7f);
            glow.SetSize(0.3f * amount * pulse);
            glow.SetColor(new Color(palette.Glow.r, palette.Glow.g, palette.Glow.b, Mathf.Clamp01(0.85f * amount)));
            glowLight.intensity = 2.5f * amount * pulse;
            glowLight.range = 1.5f + 1f * amount;

            var swirlEmission = swirl.emission; swirlEmission.rateOverTime = 130f * amount;
            var moteEmission = motes.emission; moteEmission.rateOverTime = (wasArmed ? 14f : 0f) * amount;

            // the flash: a white-hot flare that swells and fades, and a burst of light
            if (flashAge >= 0f)
            {
                flashAge += Time.deltaTime;
                float f = Mathf.Clamp01(flashAge / FlashSeconds);
                float fade = (1f - f) * (1f - f);
                flare.SetSize(Mathf.Lerp(0.25f, 1.3f, Mathf.Sqrt(f)));
                Color c = Color.Lerp(Color.white, palette.Glow, f);
                flare.SetColor(new Color(c.r, c.g, c.b, fade));
                glowLight.intensity += 12f * fade;
                glowLight.range += 4f * fade;
                if (f >= 1f) { flashAge = -1f; flare.SetSize(0f); }
            }
        }
    }
}
