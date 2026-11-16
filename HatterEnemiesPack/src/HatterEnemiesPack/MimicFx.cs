using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// The head's explosion (every game; no damage, the host deals that): the explosive grenade's own blast effect
    /// and sound, bigger, with a ring of smaller fireballs, plus a bright flash, a burst of sparks and a cloud of smoke.
    /// </summary>
    internal static class MimicFx
    {
        private static ParticleScriptExplosion effects;
        private static readonly System.Reflection.FieldInfo PrefabField = HarmonyLib.AccessTools.Field(typeof(ParticleScriptExplosion), "explosionPrefab");

        /// <summary>Made at startup too, so the game's explosion is ready long before the first one.</summary>
        internal static void Warm() { try { Effects(); } catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: preparing the explosion failed: {e.Message}"); } }
        private static Material soft;

        private static ParticleScriptExplosion Effects()
        {
            if (effects != null) return effects;
            var host = new GameObject("HatterEnemiesPack.Explosions");
            Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            GameObject grenade = Resources.Load<GameObject>("Items/Item Grenade Explosive");
            ParticleScriptExplosion source = grenade != null ? grenade.GetComponentInChildren<ParticleScriptExplosion>(true) : null;
            effects = host.AddComponent<ParticleScriptExplosion>();
            if (source != null) effects.explosionPreset = source.explosionPreset;
            else Plugin.Log.LogWarning("Alter Mimic: the explosive grenade wasn't found; the explosion uses only its own effects.");
            // ParticleScriptExplosion.Start (next frame) loads its fireball prefab; this is created on first use and
            // spawned at once, so without this the first (and only) explosion had no fireball at all
            GameObject prefab = Resources.Load<GameObject>("Effects/Part Prefab Explosion");
            if (PrefabField != null && prefab != null) PrefabField.SetValue(effects, prefab);
            else Plugin.Log.LogWarning("Alter Mimic: the game's explosion prefab wasn't found; only the mod's own blast shows.");
            return effects;
        }

        internal static void Explode(Vector3 centre)
        {
            float radius = Settings.ExplosionRadius.Value;
            try
            {
                ParticleScriptExplosion fx = Effects();
                if (fx.explosionPreset != null)
                {
                    fx.Spawn(centre, Mathf.Clamp(radius * 0.6f, 1.5f, 5f), 0, 0, 0f, true, false, 5f, 15f);   // the main blast, with its sound
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 2f / 5f + Random.Range(-0.3f, 0.3f);
                        Vector3 off = new Vector3(Mathf.Cos(a), Random.Range(0.1f, 0.6f), Mathf.Sin(a)) * radius * 0.3f;
                        fx.Spawn(centre + off, Mathf.Clamp(radius * 0.3f, 0.8f, 2.5f), 0, 0, 0f, true, true, 0f, 0f);   // silent fireballs round it
                    }
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: the game's explosion effect failed: {e}"); }
            try { Blast.Spawn(centre, radius); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: explosion effect failed: {e}"); }
        }

        private static Sound pulseSound;
        private static bool pulseSoundTried;

        /// <summary>The Orb Cannon's shockwave boom (the shotgun's shot settings with the embedded clip).</summary>
        private static Sound PulseSound()
        {
            if (pulseSoundTried) return pulseSound;
            pulseSoundTried = true;
            AudioClip clip = SoundLibrary.Get("orb_shockwave.pcm");
            if (clip == null) return null;
            Sound template = Resources.Load<GameObject>("Items/Item Gun Shotgun")?.GetComponent<ItemGun>()?.soundShoot;
            var s = new Sound();
            if (template != null) Sound.CopySound(template, s);
            s.Sounds = new[] { clip };
            s.Volume = 1f;
            s.VolumeRandom = 0f;
            s.Pitch = 1f;
            s.PitchRandom = 0f;
            pulseSound = s;
            return s;
        }

        /// <summary>Every game: the head's shockwave: the Orb Cannon's boom and a faint expanding bubble.</summary>
        internal static void Pulse(Vector3 at, float radius)
        {
            try { PulseSound()?.Play(at, 1f, 1f, 1f, 1f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: pulse sound failed: {e.Message}"); }
            try { PulseFx.Spawn(at, radius); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: pulse effect failed: {e.Message}"); }
        }

        internal static Material Soft
        {
            get
            {
                if (soft != null) return soft;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                tex.Apply();
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                soft = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, hideFlags = HideFlags.DontUnloadUnusedAsset };
                return soft;
            }
        }
    }

    /// <summary>A bright flash, sparks flying out and a rising cloud of smoke.</summary>
    internal class Blast : MonoBehaviour
    {
        private Light flash;
        private float t, range;

        internal static void Spawn(Vector3 at, float radius)
        {
            var go = new GameObject("Alter Mimic Blast");
            go.transform.position = at;
            go.AddComponent<Blast>().Setup(radius);
        }

        private void Setup(float radius)
        {
            range = Mathf.Max(8f, radius * 3f);
            flash = gameObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.6f, 0.25f);
            flash.range = range;
            flash.intensity = 10f;

            // a fireball of our own as well: big, bright, short-lived puffs that swell and fade
            Burst("Fire", 45, new Vector2(0.35f, 0.7f), new Vector2(2f, 7f), new Vector2(1.2f, 2.4f) * Mathf.Max(1f, radius * 0.3f),
                  new Color(1f, 0.75f, 0.3f, 1f), new Color(0.9f, 0.2f, 0.02f, 0f), -0.2f, radius * 0.15f);

            Burst("Sparks", 70, new Vector2(0.4f, 1.1f), new Vector2(6f, 16f), new Vector2(0.03f, 0.08f),
                  new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.35f, 0.05f, 0f), 1.2f, 0.1f);
            Burst("Smoke", 40, new Vector2(1.8f, 3.2f), new Vector2(0.6f, 2.5f), new Vector2(0.8f, 1.8f) * Mathf.Max(1f, radius * 0.35f),
                  new Color(0.25f, 0.23f, 0.22f, 0.7f), new Color(0.15f, 0.15f, 0.15f, 0f), -0.08f, radius * 0.25f);
        }

        private void Burst(string name, int count, Vector2 life, Vector2 speed, Vector2 size, Color from, Color to, float gravity, float shapeRadius)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = from;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.05f, shapeRadius);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                      new[] { new GradientAlphaKey(from.a, 0f), new GradientAlphaKey(from.a * 0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, name == "Smoke" ? 1.8f : name == "Fire" ? 1.6f : 0.2f)));
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = name == "Smoke" ? 0.15f : name == "Fire" ? 0.3f : 0.05f;
            limit.limit = name == "Smoke" ? 0.8f : name == "Fire" ? 1.5f : 4f;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = MimicFx.Soft;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            ps.Play();
        }

        private void Update()
        {
            t += Time.deltaTime;
            if (flash != null) flash.intensity = 16f * Mathf.Clamp01(1f - t / 0.8f) * Random.Range(0.85f, 1.15f);
            if (t >= 4f) Destroy(gameObject);
        }
    }

    /// <summary>A faint pale bubble that swells to the shockwave's radius and fades, with a soft flash.</summary>
    internal class PulseFx : MonoBehaviour
    {
        private const float Life = 0.45f;
        private const float Grey = 0.45f, Alpha = 0.06f;   // a faint mid-grey haze
        private float t, radius;
        private Material mat;
        private Light glow;

        internal static void Spawn(Vector3 at, float radius)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Alter Mimic Pulse";
            Collider c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.transform.position = at;
            go.transform.localScale = Vector3.one * 0.2f;
            go.AddComponent<PulseFx>().Setup(radius);
        }

        private void Setup(float r)
        {
            radius = r;
            var rend = GetComponent<MeshRenderer>();
            mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(Grey, Grey, Grey, Alpha) };
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.6f, 0.6f, 0.62f);
            glow.range = Mathf.Max(3f, radius * 1.5f);
            glow.intensity = 0.8f;
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / Life);
            float ease = 1f - (1f - k) * (1f - k);   // fast out, slowing
            transform.localScale = Vector3.one * Mathf.Lerp(0.2f, radius * 2f, ease);
            if (mat != null) mat.color = new Color(Grey, Grey, Grey, Alpha * (1f - k));
            if (glow != null) glow.intensity = 0.8f * (1f - k);
            if (t >= Life) { if (mat != null) Destroy(mat); Destroy(gameObject); }
        }
    }
}
