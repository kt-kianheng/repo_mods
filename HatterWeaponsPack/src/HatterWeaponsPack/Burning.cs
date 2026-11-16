using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Burning, from the Ember Wand's (and Ember Staff's) fireballs: their blast and the patch of fire they leave set
    /// enemies, players (the caster too) and TargetDummy dummies alight for BurnSeconds. Catching fire again (standing
    /// in the fire) restarts the time. Every BurnTickSeconds a burning enemy takes BurnDamagePerTick and a player
    /// PlayerBurnDamagePerTick; the first tick comes one tick after catching fire.
    /// Every game shows the flames itself (each runs the same blast and fire checks); only the host deals damage.
    /// </summary>
    internal class Burning : MonoBehaviour
    {
        private class Entry
        {
            public Enemy Enemy;
            public PlayerAvatar Player;
            public Collider Other;      // a TargetDummy hitbox (gets the enemy damage by message)
            public float Until, NextTick;
            public BurnFx Fx;
        }

        private static Burning instance;
        private readonly Dictionary<Object, Entry> burning = new Dictionary<Object, Entry>();

        private static float Seconds => Settings.EmberBurnSeconds.Value;
        private static float Tick => Mathf.Max(0.1f, Settings.EmberBurnTickSeconds.Value);
        private static bool IsHost => SemiFunc.IsMasterClientOrSingleplayer();

        private static Burning Get()
        {
            if (instance == null) instance = new GameObject("HWP Burning").AddComponent<Burning>();
            return instance;
        }

        internal static void Ignite(Enemy e)
        {
            if (e == null || LightningTargets.Gone(e) || Seconds <= 0f) return;
            if (!Elements.FireOn(e)) return;   // fresh ice resists it; otherwise any ice on it thaws
            Get().Add(e, () => new Entry { Enemy = e, Fx = BurnFx.OnEnemy(e) });
        }

        internal static void Ignite(PlayerAvatar p)
        {
            if (!ProjectileHits.Alive(p) || Seconds <= 0f || Settings.EmberPlayerBurnDamage.Value <= 0) return;
            if (!Elements.FireOn((Object)p)) return;
            Get().Add(p, () => new Entry { Player = p, Fx = BurnFx.OnPlayer(p) });
        }

        /// <summary>Anything else that wants burn damage (the TargetDummy mod's dummies): one per object.</summary>
        internal static void IgniteOther(Collider c)
        {
            if (c == null || Seconds <= 0f) return;
            Transform root = c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform.root;
            if (!Elements.FireOn((Object)root)) return;
            Get().Add(root, () => new Entry { Other = c, Fx = BurnFx.OnCollider(c) });
        }

        /// <summary>
        /// Sorts what a blast or the fire touched: enemies, players and dummies. Players are tested separately (as
        /// standing capsules) by the callers, so colliders on a player are skipped here.
        /// </summary>
        internal static void IgniteColliders(Collider[] hits)
        {
            var done = new HashSet<Object>();
            foreach (Collider c in hits)
            {
                if (c == null) continue;
                Enemy e = LightningTargets.Of(c);
                if (e != null) { if (done.Add(e)) Ignite(e); continue; }
                if (c.GetComponentInParent<PlayerAvatar>() != null) continue;
                if (c.GetComponentInParent<PhysGrabObject>() != null) continue;   // items and valuables don't burn
                if (!HasDummyReceiver(c)) continue;
                Transform root = c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform.root;
                if (done.Add(root)) IgniteOther(c);
            }
        }

        /// <summary>Is this enemy, player (PlayerAvatar) or dummy (root transform) burning on this game?</summary>
        internal static bool IsBurning(Object key) =>
            key != null && instance != null && instance.burning.TryGetValue(key, out Entry e) && e != null && Time.time < e.Until;

        /// <summary>Puts out a burn (ice, every game): the flames go, no more ticks. True if it was burning.</summary>
        internal static bool Extinguish(Object key)
        {
            if (key == null || instance == null || !instance.burning.TryGetValue(key, out Entry e)) return false;
            instance.burning.Remove(key);
            if (e == null) return false;
            if (e.Fx != null) e.Fx.Stop();
            if (e.Player != null && e.Player == PlayerAvatar.instance) BurnScreen.Set(false);
            return Time.time < e.Until;
        }

        /// <summary>The root a dummy's burn is kept under, if this collider belongs to one (the TargetDummy mod).</summary>
        internal static Transform DummyRoot(Collider c)
        {
            if (c == null || c.GetComponentInParent<PhysGrabObject>() != null || !HasDummyReceiver(c)) return null;
            return c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform.root;
        }

        private static readonly Dictionary<System.Type, bool> receivers = new Dictionary<System.Type, bool>();

        /// <summary>Does anything above this collider listen for "TargetDummyHit" (the TargetDummy mod)?</summary>
        private static bool HasDummyReceiver(Collider c)
        {
            for (Transform t = c.transform; t != null; t = t.parent)
                foreach (MonoBehaviour mb in t.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    System.Type type = mb.GetType();
                    if (!receivers.TryGetValue(type, out bool has))
                        receivers[type] = has = type.GetMethod("TargetDummyHit",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) != null;
                    if (has) return true;
                }
            return false;
        }

        private void Add(Object key, System.Func<Entry> make)
        {
            float now = Time.time;
            if (burning.TryGetValue(key, out Entry entry) && entry != null)
            {
                entry.Until = now + Seconds;   // caught fire again: the time starts over
                return;
            }
            entry = make();
            entry.Until = now + Seconds;
            entry.NextTick = now + Tick;
            burning[key] = entry;
            if (IsHost) Plugin.Log.LogInfo($"Ember Wand: {Name(entry)} is burning.");
        }

        private static string Name(Entry e) =>
            e.Enemy != null ? LightningTargets.Name(e.Enemy) : e.Player != null ? SemiFunc.PlayerGetName(e.Player) : e.Other != null ? e.Other.transform.root.name : "?";

        private static bool Gone(Entry e) =>
            e.Enemy != null ? LightningTargets.Gone(e.Enemy)
            : e.Player != null ? !ProjectileHits.Alive(e.Player)
            : e.Other == null || !e.Other.gameObject.activeInHierarchy;

        private readonly List<Object> ended = new List<Object>();

        private void Update()
        {
            float now = Time.time;
            ended.Clear();
            bool localBurning = false;
            PlayerAvatar me = PlayerAvatar.instance;
            foreach (var kv in burning)
            {
                Entry e = kv.Value;
                if (e == null || Gone(e)) { ended.Add(kv.Key); continue; }
                bool mine = me != null && e.Player == me;
                // every game keeps the ticks (for the on-screen flash); only the host deals the damage
                while (now >= e.NextTick && e.NextTick <= e.Until + 0.01f)
                {
                    e.NextTick += Tick;
                    if (IsHost) Hurt(e);
                    if (mine) BurnScreen.Flash();
                    if (Gone(e)) break;
                }
                if (now >= e.Until) ended.Add(kv.Key);
                else if (mine) localBurning = true;
            }
            BurnScreen.Set(localBurning);
            foreach (Object key in ended)
            {
                if (burning.TryGetValue(key, out Entry e) && e != null && e.Fx != null) e.Fx.Stop();
                burning.Remove(key);
            }
        }

        /// <summary>Host: one tick of burn damage.</summary>
        private static void Hurt(Entry e)
        {
            try
            {
                if (e.Enemy != null)
                    LightningTargets.Hurt(e.Enemy, Settings.EmberBurnDamage.Value, Vector3.up);
                else if (e.Player != null)
                {
                    int dmg = Settings.EmberPlayerBurnDamage.Value;
                    if (dmg > 0) ProjectileHits.HurtPlayer(e.Player, dmg, e.Player.transform.position + Vector3.up);
                }
                else if (e.Other != null)
                    e.Other.SendMessageUpwards("TargetDummyHit", new float[] { Settings.EmberBurnDamage.Value, 0f, 1f, 0f },
                                               SendMessageOptions.DontRequireReceiver);
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"Burn damage failed: {ex}"); }
        }

        private void OnDestroy()
        {
            BurnScreen.Set(false);
            foreach (Entry e in burning.Values) if (e != null && e.Fx != null) e.Fx.Stop();
            burning.Clear();
            if (instance == this) instance = null;
        }

        /// <summary>Who stands in a vertical cylinder (feet within it): every player, the caster included.</summary>
        internal static List<PlayerAvatar> PlayersIn(Vector3 floor, float radius, float height)
        {
            var list = new List<PlayerAvatar>();
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (!ProjectileHits.Alive(p) || !p.gameObject.activeInHierarchy) continue;
                Vector3 d = p.transform.position - floor;
                if (new Vector2(d.x, d.z).magnitude > radius + 0.3f || d.y < -0.6f || d.y > height) continue;
                list.Add(p);
            }
            return list;
        }

        /// <summary>Players whose body (a standing capsule) is within radius of a point.</summary>
        internal static List<PlayerAvatar> PlayersNear(Vector3 point, float radius)
        {
            var list = new List<PlayerAvatar>();
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (!ProjectileHits.Alive(p) || !p.gameObject.activeInHierarchy) continue;
                Vector3 feet = p.transform.position;
                float y = Mathf.Clamp(point.y, feet.y + 0.4f, feet.y + 1.2f);
                Vector3 axis = new Vector3(feet.x, y, feet.z);
                if ((point - axis).magnitude <= radius + 0.4f) list.Add(p);
            }
            return list;
        }
    }

    /// <summary>Flames licking up a burning enemy, player or dummy, following it until the burn ends.</summary>
    internal class BurnFx : MonoBehaviour
    {
        private Transform follow;
        private Vector3 offset;
        private ParticleSystem flames;
        private Light glow;
        private bool stopping;
        private float stopAt;

        internal static BurnFx OnEnemy(Enemy e)
        {
            Transform centre = LightningTargets.CentreTransform(e);
            Bounds b;
            bool any = LightningTargets.BodyBounds(e, out b);
            Vector3 offset = any && centre != null ? b.center - centre.position : Vector3.zero;
            return Create(centre, offset, any ? b.extents : new Vector3(0.35f, 0.5f, 0.35f));
        }

        internal static BurnFx OnPlayer(PlayerAvatar p)
        {
            // round the legs and body; the local player's own flames stay low so they don't fill the view
            bool local = p == PlayerAvatar.instance;
            return Create(p.transform, new Vector3(0f, local ? 0.35f : 0.8f, 0f), new Vector3(0.3f, local ? 0.35f : 0.75f, 0.3f));
        }

        internal static BurnFx OnCollider(Collider c)
        {
            Bounds b = c.bounds;
            return Create(c.transform, b.center - c.transform.position, b.extents);
        }

        private static BurnFx Create(Transform follow, Vector3 offset, Vector3 extents)
        {
            var go = new GameObject("HWP Burning Flames");
            var fx = go.AddComponent<BurnFx>();
            fx.follow = follow;
            fx.offset = offset;
            go.transform.position = (follow != null ? follow.position : Vector3.zero) + offset;
            extents = new Vector3(Mathf.Clamp(extents.x, 0.15f, 2f), Mathf.Clamp(extents.y, 0.2f, 2.5f), Mathf.Clamp(extents.z, 0.15f, 2f));
            float scale = Mathf.Clamp((extents.x + extents.y + extents.z) / 1.2f, 0.6f, 3f);

            var obj = new GameObject("Flames");
            obj.transform.SetParent(go.transform, false);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f * scale, 0.32f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.25f;
            main.maxParticles = 500;
            var emission = ps.emission; emission.rateOverTime = 45f * scale;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = extents * 1.6f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.08f), 0.4f), new GradientColorKey(new Color(0.7f, 0.1f, 0.03f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.1f), new GradientAlphaKey(0.45f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.3f)));
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.3f; noise.frequency = 1.5f; noise.scrollSpeed = 1.5f;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            Material soft = SoftGlow.SharedMaterial;
            pr.sharedMaterial = soft != null ? soft : FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            ps.Play();
            fx.flames = ps;

            var lo = new GameObject("Burn Light");
            lo.transform.SetParent(go.transform, false);
            fx.glow = lo.AddComponent<Light>();
            fx.glow.type = LightType.Point;
            fx.glow.color = new Color(1f, 0.45f, 0.12f);
            fx.glow.range = 2.5f * scale;
            fx.glow.intensity = 1.5f;
            return fx;
        }

        internal void Stop()
        {
            if (this == null || stopping) return;
            stopping = true;
            stopAt = Time.time;
            if (flames != null) { var e = flames.emission; e.enabled = false; }
            Destroy(gameObject, 1f);
        }

        private void LateUpdate()
        {
            if (follow != null) transform.position = follow.position + offset;
            else if (!stopping) Stop();
            if (glow != null)
            {
                float k = stopping ? Mathf.Clamp01(1f - (Time.time - stopAt) / 0.5f) : 1f;
                glow.intensity = 1.5f * k * Random.Range(0.75f, 1.2f);
            }
        }
    }

    /// <summary>
    /// The patch of fire a fireball (or the Ember Staff's inferno, as it dies) leaves on the floor, for FireAreaSeconds. Every game draws it
    /// and, four times a second, sets alight whatever stands in it (enemies, players, dummies); staying in it keeps
    /// restarting their burn. Damage comes from Burning (host).
    /// </summary>
    internal class FireArea : MonoBehaviour
    {
        private const float CheckInterval = 0.25f;
        private const float Height = 1.6f;              // flames reach up this far for the checks
        private const float FadeSeconds = 0.8f;

        private float age, duration, radius, nextCheck, nextSound, soundUntil;
        private Vector3 floor;
        private static readonly List<FireArea> all = new List<FireArea>();

        private void Awake() => all.Add(this);
        private void OnDestroy() => all.Remove(this);

        /// <summary>
        /// Ice (the ice wall rising, every game): patches of fire whose middle lies under the box go out in steam
        /// (they fade as if burnt out).
        /// </summary>
        internal static void ExtinguishIn(Vector3 centre, Vector3 half, Quaternion rotation)
        {
            foreach (FireArea a in all.ToArray())
            {
                if (a == null || a.age >= a.duration) continue;
                Vector3 local = Quaternion.Inverse(rotation) * (a.floor - centre);
                float reach = a.radius * 0.5f;
                if (Mathf.Abs(local.x) > half.x + reach || Mathf.Abs(local.z) > half.z + reach || Mathf.Abs(local.y) > half.y + 0.6f) continue;
                a.duration = a.age;
                Steam.Spawn(a.floor + Vector3.up * 0.3f, Mathf.Clamp(a.radius, 0.8f, 2f));
            }
        }
        private ParticleSystem flames, embers;
        private Light[] lights;
        private Sound sound;
        private AudioSource soundSource;

        /// <summary>A patch of fire on the floor under <paramref name="burst"/> (nothing if there's no floor within 3 m).</summary>
        /// <param name="radiusOverride">Its radius (default: the Ember Wand's FireAreaRadius).</param>
        /// <param name="secondsOverride">How long it burns (default: the Ember Wand's FireAreaSeconds).</param>
        internal static FireArea Spawn(Vector3 burst, Sound burning, float radiusOverride = -1f, float secondsOverride = -1f)
        {
            float seconds = secondsOverride > 0f ? secondsOverride : Settings.EmberFireSeconds.Value;
            if (seconds <= 0f) return null;
            int world = LayerMask.GetMask("Default", "StaticGrabObject", "PhysGrabObjectHinge");
            if (!Physics.Raycast(burst + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 3.2f, world, QueryTriggerInteraction.Ignore))
                return null;
            var go = new GameObject("HWP Fire Area");
            var area = go.AddComponent<FireArea>();
            area.Setup(hit.point, seconds, radiusOverride > 0f ? radiusOverride : Settings.EmberFireRadius.Value, burning);
            return area;
        }

        private void Setup(Vector3 floorPoint, float seconds, float r, Sound burning)
        {
            floor = floorPoint;
            duration = seconds;
            radius = r;
            sound = burning;
            transform.position = floor;
            float density = Mathf.Clamp(Mathf.Pow(radius / 1.2f, 2f), 0.3f, 8f);

            // licking flames over the disc
            var obj = new GameObject("Flames");
            obj.transform.SetParent(transform, false);
            obj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // the cone emits along +Z: point it up
            flames = obj.AddComponent<ParticleSystem>();
            flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 3000;
            var emission = flames.emission; emission.rateOverTime = 90f * density;
            var shape = flames.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 3f; shape.radius = radius * 0.95f;
            var col = flames.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.88f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.08f), 0.35f), new GradientColorKey(new Color(0.75f, 0.12f, 0.03f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.1f), new GradientAlphaKey(0.45f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = flames.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.3f)));
            var noise = flames.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.2f; noise.scrollSpeed = 1.5f;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            Material soft = SoftGlow.SharedMaterial;
            pr.sharedMaterial = soft != null ? soft : FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            flames.Play();

            embers = Inferno.Embers(transform, radius, 0.8f, 2f, 0.015f, 0.035f);
            var ee = embers.emission; ee.rateOverTime = 25f * density;

            lights = new Light[2];
            for (int i = 0; i < lights.Length; i++)
            {
                var lo = new GameObject("Fire Light " + i);
                lo.transform.SetParent(transform, false);
                lo.transform.localPosition = Vector3.up * (0.3f + 0.5f * i);
                lights[i] = lo.AddComponent<Light>();
                lights[i].type = LightType.Point;
                lights[i].color = new Color(1f, 0.45f, 0.12f);
                lights[i].range = Mathf.Max(4f, radius * 4f);
                lights[i].intensity = 0f;
            }
            Check();
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= duration + FadeSeconds + 1.2f) { Destroy(gameObject); return; }
            float k = Mathf.Clamp01(age / 0.3f) * Mathf.Clamp01((duration + FadeSeconds - age) / FadeSeconds);
            for (int i = 0; i < lights.Length; i++) lights[i].intensity = (i == 0 ? 3f : 1.8f) * k * Random.Range(0.85f, 1.15f);
            if (age > duration)
            {
                var e = flames.emission; e.enabled = false;
                var ee = embers.emission; ee.enabled = false;
            }
            else if (age >= nextCheck)
            {
                nextCheck = age + CheckInterval;
                Check();
            }

            // the crackling fire sound, repeated if the fire outlasts it, cut as the flames die
            if (sound != null && age < duration && age >= nextSound)
            {
                AudioClip clip = sound.Sounds != null && sound.Sounds.Length > 0 ? sound.Sounds[0] : null;
                float length = clip != null ? clip.length : 2f;
                nextSound = age + Mathf.Max(0.5f, length - 0.8f);
                try { soundSource = sound.Play(floor + Vector3.up * 0.5f, 1f, 1f, 1f, 1f); soundUntil = Time.time + length; }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Fire sound failed: {ex}"); sound = null; }
            }
            if (age > duration + FadeSeconds && soundSource != null)
            {
                if (Time.time < soundUntil - 0.05f && soundSource.isPlaying) soundSource.Stop();
                soundSource = null;
            }
        }

        /// <summary>Sets alight everything standing in the fire (every game: the flames; the host: the damage).</summary>
        private void Check()
        {
            Collider[] hits = Physics.OverlapCapsule(floor + Vector3.up * 0.1f, floor + Vector3.up * Height, radius, ProjectileHits.Mask,
                                                     QueryTriggerInteraction.Ignore);
            var inside = new List<Collider>();
            foreach (Collider c in hits)
            {
                if (c == null) continue;
                Vector3 p = c.ClosestPoint(floor + Vector3.up * 0.5f);
                Vector3 d = p - floor;
                if (new Vector2(d.x, d.z).magnitude <= radius) inside.Add(c);
            }
            Burning.IgniteColliders(inside.ToArray());
            foreach (PlayerAvatar p in Burning.PlayersIn(floor, radius, Height)) Burning.Ignite(p);
        }
    }

    /// <summary>
    /// The local player's screen while they're on fire: a flickering ring of flame round the edges of the screen
    /// (two layers flickering out of step), brighter for a moment at each burn tick. Fades in and out.
    /// </summary>
    internal class BurnScreen : MonoBehaviour
    {
        private const float FadeIn = 0.25f, FadeOut = 0.6f, FlashSeconds = 0.35f;
        private static BurnScreen instance;
        private static Texture2D texture;
        private RawImage a, b;
        private bool on;
        private float level, flashAt = -10f;

        /// <summary>Show (or start fading out) the burning screen.</summary>
        internal static void Set(bool burning)
        {
            if (!burning && instance == null) return;
            if (instance == null) instance = Create();
            instance.on = burning;
        }

        internal static void Flash()
        {
            if (instance != null) instance.flashAt = Time.unscaledTime;
        }

        private static BurnScreen Create()
        {
            var go = new GameObject("HWP Burning Screen");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            var fx = go.AddComponent<BurnScreen>();
            fx.a = Layer(go.transform, "Flames A", new Rect(0f, 0f, 1f, 1f));
            fx.b = Layer(go.transform, "Flames B", new Rect(1f, 0f, -1f, 1f));   // mirrored, so the tongues differ
            return fx;
        }

        private static RawImage Layer(Transform parent, string name, Rect uv)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<RawImage>();
            img.texture = Texture();
            img.uvRect = uv;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);
            RectTransform r = img.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            return img;
        }

        /// <summary>Clear in the middle, flame-coloured tongues licking in from the edges (made once, in code).</summary>
        private static Texture2D Texture()
        {
            if (texture != null) return texture;
            const int n = 256;
            texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HWP Burning Screen" };
            var px = new Color32[n * n];
            var hot = new Color(1f, 0.62f, 0.15f);
            var deep = new Color(0.85f, 0.12f, 0.02f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float round = Mathf.Sqrt(u * u + v * v) / 1.4142f, square = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                    float r = Mathf.Lerp(round, square, 0.55f);                 // between a circle and the screen's shape
                    float angle = Mathf.Atan2(v, u);
                    float tongues = Mathf.PerlinNoise(angle * 2.2f + 10f, r * 3f) * 0.7f + Mathf.PerlinNoise(angle * 6f + 3f, r * 7f) * 0.5f;
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f + 0.25f * tongues, 1f, r));
                    float alpha = Mathf.Clamp01(edge * 1.25f);
                    Color c = Color.Lerp(hot, deep, Mathf.Clamp01((r - 0.6f) / 0.4f));
                    px[y * n + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(alpha * 255));
                }
            texture.SetPixels32(px);
            texture.Apply(false, true);
            return texture;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            level = on ? Mathf.MoveTowards(level, 1f, dt / FadeIn) : Mathf.MoveTowards(level, 0f, dt / FadeOut);
            if (!on && level <= 0f) { instance = null; Destroy(gameObject); return; }
            float t = Time.unscaledTime;
            float flash = Mathf.Clamp01(1f - (t - flashAt) / FlashSeconds);
            float fa = 0.55f + 0.25f * Mathf.PerlinNoise(t * 5f, 0.3f) + 0.35f * flash;
            float fb = 0.35f + 0.25f * Mathf.PerlinNoise(t * 7f, 5.1f) + 0.3f * flash;
            a.color = new Color(1f, 1f, 1f, Mathf.Clamp01(fa * level));
            b.color = new Color(1f, 0.85f, 0.7f, Mathf.Clamp01(fb * level));
            // the flames breathe in and out a little
            a.rectTransform.localScale = Vector3.one * (1.02f - 0.03f * Mathf.PerlinNoise(t * 3f, 1.7f) - 0.02f * flash);
            b.rectTransform.localScale = Vector3.one * (1.04f - 0.04f * Mathf.PerlinNoise(t * 4f, 8.2f));
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
