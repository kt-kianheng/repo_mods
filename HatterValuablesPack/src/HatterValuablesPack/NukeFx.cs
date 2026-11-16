using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace HatterValuablesPack
{
    /// <summary>The A-Bomb's explosion, flash and smoke (every player's game draws its own).</summary>
    internal static class NukeFx
    {
        private static ParticleScriptExplosion effects;
        private static Texture2D softDot;

        /// <summary>The game's explosion effect (the explosive grenade's preset) on an object of our own.</summary>
        private static ParticleScriptExplosion Effects()
        {
            if (effects != null) return effects;
            var host = new GameObject("HatterValuablesPack.Explosions");
            Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            GameObject grenade = Resources.Load<GameObject>("Items/Item Grenade Explosive");
            ParticleScriptExplosion source = grenade != null ? grenade.GetComponentInChildren<ParticleScriptExplosion>(true) : null;
            effects = host.AddComponent<ParticleScriptExplosion>();
            if (source != null) effects.explosionPreset = source.explosionPreset;
            else Plugin.Log.LogWarning("A-Bomb: explosive grenade not found; the explosion will have no fireball.");
            return effects;
        }

        /// <summary>The game's big explosion Sound (volume group, falloff), the template for the bomb's sounds.</summary>
        internal static Sound SoundTemplate()
        {
            ExplosionPreset preset = Effects().explosionPreset;
            return preset != null ? preset.explosionSoundBig : null;
        }

        /// <summary>A soft round dot (white, alpha falling off towards the edge) for smoke and glare.</summary>
        internal static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;
            const int n = 128;
            softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HVP Soft Dot" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
                }
            softDot.SetPixels32(px);
            softDot.Apply(false, true);
            return softDot;
        }

        internal static void Explode(Vector3 centre)
        {
            // the explosion: one big blast with sound, plus a ring of fireballs round it
            try
            {
                ParticleScriptExplosion fx = Effects();
                if (fx.explosionPreset != null)
                {
                    fx.Spawn(centre, 3f, 0, 0, 0f, true, true, 6f, 60f);   // silent: the bomb has its own sound
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        Vector3 off = new Vector3(Mathf.Cos(a), 0.3f + 0.4f * (i % 2), Mathf.Sin(a)) * 1.6f;
                        fx.Spawn(centre + off, 1.6f, 0, 0, 0f, true, true, 0f, 0f);
                    }
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"A-Bomb: explosion effect failed: {e}"); }

            NukeSounds.PlayExplosion(centre);

            // a huge brief light: shows through doorways in nearby rooms
            var lightGo = new GameObject("A-Bomb Light");
            lightGo.transform.position = centre + Vector3.up * 0.5f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.range = 60f;
            light.intensity = 40f;
            lightGo.AddComponent<FadeLight>().seconds = 1.8f;

            // the flash on this player's screen, by how many rooms away their camera is
            try
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                int kill = Settings.NukeKillRooms.Value;
                int flash = Mathf.Max(kill, Settings.NukeFlashRooms.Value);
                var map = NukeBlast.BuildMap();
                var dist = NukeBlast.Distances(map, NukeBlast.BlastRooms(centre), flash);
                var rooms = NukeBlast.RoomsAt(cam.transform.position);
                if (rooms.Count == 0 && PlayerAvatar.instance != null)
                    rooms = NukeBlast.RoomsOf(PlayerAvatar.instance.RoomVolumeCheck, PlayerAvatar.instance.transform.position);
                int steps = NukeBlast.StepsTo(dist, rooms);
                float r = Settings.NukeMinKillRadius.Value;
                bool close = r > 0f && (cam.transform.position - centre).sqrMagnitude <= r * r;
                NukeFlash.Kind kind = steps <= kill || close ? NukeFlash.Kind.Blinding
                                    : steps <= flash ? NukeFlash.Kind.Directional
                                    : NukeFlash.Kind.None;
                if (Settings.NukeDebugLog.Value)
                    Plugin.Log.LogInfo($"A-Bomb: this player's camera is {(steps == int.MaxValue ? "out of reach" : steps + " room(s) away")}: {kind} flash.");
                if (kind != NukeFlash.Kind.None) NukeFlash.Show(kind, centre);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"A-Bomb: flash failed: {e}"); }
        }
    }

    /// <summary>A light that fades out and removes itself.</summary>
    internal class FadeLight : MonoBehaviour
    {
        public float seconds = 1f;
        private Light l;
        private float start, t0;

        private void Start() { l = GetComponent<Light>(); start = l.intensity; t0 = Time.time; }

        private void Update()
        {
            float t = (Time.time - t0) / seconds;
            if (t >= 1f) { Destroy(gameObject); return; }
            l.intensity = start * (1f - t) * (1f - t);
        }
    }

    /// <summary>
    /// The flash on the local screen.
    ///  - Blinding: the whole screen white for a moment, then fading out.
    ///  - Directional: a bright glare on the side of the screen facing the bomb (following the camera as it turns, at
    ///    the screen's edge when the bomb is behind) over a faint white wash.
    /// </summary>
    internal class NukeFlash : MonoBehaviour
    {
        internal enum Kind { None, Directional, Blinding }

        private const float BlindHold = 1.2f, BlindFade = 3.5f;
        private const float GlareRise = 0.08f, GlareFade = 2.6f;

        private Kind kind;
        private Vector3 centre;
        private float t0;
        private RawImage wash, glare;
        private RectTransform glareRect;

        internal static void Show(Kind kind, Vector3 centre)
        {
            var go = new GameObject("A-Bomb Flash");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var f = go.AddComponent<NukeFlash>();
            f.kind = kind;
            f.centre = centre;
            f.t0 = Time.unscaledTime;

            f.wash = MakeImage(go.transform, "Wash", null);
            var wr = f.wash.rectTransform;
            wr.anchorMin = Vector2.zero; wr.anchorMax = Vector2.one; wr.offsetMin = wr.offsetMax = Vector2.zero;
            if (kind == Kind.Directional)
            {
                f.glare = MakeImage(go.transform, "Glare", NukeFx.SoftDot());
                f.glareRect = f.glare.rectTransform;
                f.glareRect.anchorMin = f.glareRect.anchorMax = Vector2.zero;
            }
            f.Apply();
        }

        private static RawImage MakeImage(Transform parent, string name, Texture tex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }

        private void Update() => Apply();

        private void Apply()
        {
            float t = Time.unscaledTime - t0;
            if (kind == Kind.Blinding)
            {
                float a = t < BlindHold ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - BlindHold) / BlindFade);
                wash.color = new Color(1f, 1f, 0.97f, a);
                if (t > BlindHold + BlindFade) Destroy(gameObject);
                return;
            }

            float g = t < GlareRise ? t / GlareRise : 1f - Mathf.SmoothStep(0f, 1f, (t - GlareRise) / GlareFade);
            wash.color = new Color(1f, 1f, 0.95f, 0.35f * g);
            glare.color = new Color(1f, 0.98f, 0.92f, g);
            float size = Screen.height * 1.6f;
            glareRect.sizeDelta = new Vector2(size, size);
            glareRect.anchoredPosition = ScreenPoint();
            if (t > GlareRise + GlareFade) Destroy(gameObject);
        }

        /// <summary>Where the bomb is on screen, or the screen edge in its direction when it's off screen or behind.</summary>
        private Vector2 ScreenPoint()
        {
            var mid = new Vector2(Screen.width, Screen.height) * 0.5f;
            Camera cam = Camera.main;
            if (cam == null) return mid;
            Vector3 local = cam.transform.InverseTransformPoint(centre);
            Vector3 sp = cam.WorldToScreenPoint(centre);
            if (local.z > 0.1f && sp.x >= 0 && sp.x <= Screen.width && sp.y >= 0 && sp.y <= Screen.height) return sp;
            var dir = new Vector2(local.x, local.y);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.down;
            dir.Normalize();
            float k = Mathf.Min(Mathf.Abs(mid.x / (Mathf.Abs(dir.x) > 1e-4f ? dir.x : 1e-4f)),
                                Mathf.Abs(mid.y / (Mathf.Abs(dir.y) > 1e-4f ? dir.y : 1e-4f)));
            return mid + dir * k;
        }
    }

    /// <summary>
    /// Smoke leaking from the armed bomb: grey puffs from the nose seam, the tail cone and the top cover, thickening and
    /// speeding up until the fuse runs out.
    /// </summary>
    internal class NukeSmoke : MonoBehaviour
    {
        private ParticleSystem[] systems;
        private float t0, fuse;
        private static Material material;

        internal static NukeSmoke Attach(Transform modelRoot, float fuseSeconds)
        {
            var go = new GameObject("A-Bomb Smoke");
            go.transform.SetParent(modelRoot, false);
            var s = go.AddComponent<NukeSmoke>();
            s.fuse = Mathf.Max(0.1f, fuseSeconds);
            s.t0 = Time.time;
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default")) { name = "HVP Smoke", mainTexture = NukeFx.SoftDot() };
            }
            float scale = modelRoot.lossyScale.x;
            s.systems = new ParticleSystem[NukeBuilder.SmokePoints.Length];
            for (int i = 0; i < s.systems.Length; i++)
                s.systems[i] = Emitter(go.transform, NukeBuilder.SmokePoints[i], scale);
            s.Ramp(0f);
            return s;
        }

        private static ParticleSystem Emitter(Transform parent, Vector3 localPoint, float scale)
        {
            var go = new GameObject("Smoke Emitter");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPoint;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0.22f, 0.22f, 0.8f), new Color(0.4f, 0.4f, 0.38f, 0.7f));
            main.gravityModifier = -0.04f;
            main.maxParticles = 400;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f * scale;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 4f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.6f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);   // all three axes must use the same curve mode
            vel.y = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingFudge = 1f;
            ps.Play();
            return ps;
        }

        private void Update() => Ramp((Time.time - t0) / fuse);

        /// <summary>0 = just armed (a thin trickle), 1 = about to go (thick, fast smoke).</summary>
        private void Ramp(float k)
        {
            k = Mathf.Clamp01(k);
            float rate = Mathf.Lerp(3f, 28f, k * k);
            float speed = Mathf.Lerp(0.3f, 1.4f, k);
            foreach (ParticleSystem ps in systems)
            {
                if (ps == null) continue;
                var em = ps.emission;
                em.rateOverTime = rate;
                var main = ps.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            }
        }

        /// <summary>Stops the smoke, leaving what's already out to drift away.</summary>
        internal void Detach()
        {
            if (this == null) return;
            transform.SetParent(null, true);
            foreach (ParticleSystem ps in systems) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            enabled = false;
            Destroy(gameObject, 4f);
        }
    }

    /// <summary>
    /// The bomb's sounds (from the user's files, converted to PCM in Assets):
    ///  - nuke_alarm_loop.pcm: one 0.5 s ring cut from "alarm-clock" (freesound_community, Pixabay), from just before one
    ///    ring to just before the next, so it repeats seamlessly. While armed it plays as one clip of the fuse's length
    ///    (the ring repeated), following the bomb.
    ///  - nuke_explosion.pcm: "bomb-explosion" (wings_of_freedom, Pixabay), 5 s, at the blast, heard from far away.
    /// </summary>
    internal static class NukeSounds
    {
        private static Sound alarm, explosion;
        private static float alarmSeconds;

        private static Sound Make(AudioClip clip, float volume)
        {
            Sound template = NukeFx.SoundTemplate();
            if (clip == null || template == null) return null;
            var s = new Sound();
            Sound.CopySound(template, s);
            s.Sounds = new[] { clip };
            s.Volume = volume;
            s.VolumeRandom = 0f;
            s.Pitch = 1f;
            s.PitchRandom = 0f;
            return s;
        }

        /// <summary>The ring repeated to fill the fuse (built once per fuse length).</summary>
        private static Sound Alarm(float seconds)
        {
            if (alarm != null && Mathf.Approximately(alarmSeconds, seconds)) return alarm;
            AudioClip ring = SoundLibrary.Get("nuke_alarm_loop.pcm");
            if (ring == null) return null;
            var one = new float[ring.samples];
            ring.GetData(one, 0);
            int total = Mathf.Max(one.Length, Mathf.RoundToInt(seconds * ring.frequency));
            var data = new float[total];
            for (int i = 0; i < total; i++) data[i] = one[i % one.Length];
            var clip = AudioClip.Create("A-Bomb Alarm", total, 1, ring.frequency, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            alarm = Make(clip, 0.7f);
            alarmSeconds = seconds;
            return alarm;
        }

        /// <summary>Starts the alarm on the bomb; returns its AudioSource so it can be cut off.</summary>
        internal static AudioSource PlayAlarm(Transform bomb, float fuseSeconds)
        {
            try { return Alarm(fuseSeconds)?.Play(bomb, 1f, 1.5f, 1f, 1f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"A-Bomb: alarm sound failed: {e}"); return null; }
        }

        internal static void PlayExplosion(Vector3 centre)
        {
            try
            {
                if (explosion == null) explosion = Make(SoundLibrary.Get("nuke_explosion.pcm"), 1f);
                explosion?.Play(centre, 1f, 4f, 1f, 1f);   // 4 x the usual falloff distance: heard across the level
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"A-Bomb: explosion sound failed: {e}"); }
        }
    }
}
