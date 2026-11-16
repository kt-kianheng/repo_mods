using System.Collections.Generic;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Ember and Frost meet (user's spec, 14 Nov 2026):
    /// - Ice puts out fire: an icicle or the ice wall on a burning enemy, player or dummy ends the burn (with steam),
    ///   then the ice does what it always does (freeze, encase, slow).
    /// - Fire thaws ice: any fire (fireball blast, fire patch, the inferno) touching a frozen or slowed enemy shatters
    ///   its ice, ends the freeze, stun and slow (with steam), and sets it burning.
    /// - The latest element wins, but right after one lands the other can't undo it for GraceSeconds (both ways).
    /// - Lightning doesn't take part.
    /// Every game runs these checks itself at the same moments it already draws the effects (icicle impacts, the wall
    /// rising, fire checks), so the flames, casings and steam match; the host also does the game-state parts (freeze,
    /// stun, slow), which the game syncs.
    /// </summary>
    internal static class Elements
    {
        private enum Kind { Fire, Ice }

        private struct Mark { public Kind Kind; public float Time; }

        // keyed by the Enemy, the PlayerAvatar, or a dummy's root transform
        private static readonly Dictionary<Object, Mark> last = new Dictionary<Object, Mark>();

        internal static Sound Hiss;   // set up by the first wand or staff (needs a game Sound as a template)

        private static float Grace => Settings.ElementGraceSeconds.Value;
        private static bool IsHost => SemiFunc.IsMasterClientOrSingleplayer();

        /// <summary>Could the other element still be holding on (it landed less than GraceSeconds ago)?</summary>
        private static bool Resists(Object key, Kind incoming)
        {
            if (key == null || !last.TryGetValue(key, out Mark m)) return false;
            return m.Kind != incoming && Time.time - m.Time < Grace;
        }

        private static void Remember(Object key, Kind kind)
        {
            if (key == null) return;
            if (last.Count > 256)
            {
                var stale = new List<Object>();
                foreach (var kv in last) if (kv.Key == null || Time.time - kv.Value.Time > 30f) stale.Add(kv.Key);
                foreach (Object k in stale) last.Remove(k);
            }
            last[key] = new Mark { Kind = kind, Time = Time.time };
        }

        // ---------- fire ----------

        /// <summary>
        /// Fire reaches an enemy (every game, from Burning.Ignite). False if fresh ice still resists it (don't ignite);
        /// otherwise any ice on it is thawed and it may catch fire.
        /// </summary>
        internal static bool FireOn(Enemy e)
        {
            if (e == null) return false;
            if (Resists(e, Kind.Fire)) return false;
            // only a change counts as "the latest element" (standing in fire doesn't keep renewing it, or ice
            // could never take hold of an enemy in a fire patch)
            if (IsChilled(e)) { Thaw(e); Remember(e, Kind.Fire); }
            else if (!Burning.IsBurning(e)) Remember(e, Kind.Fire);
            return true;
        }

        /// <summary>Fire reaches a player or a dummy (they don't freeze, but fresh ice still resists the fire).</summary>
        internal static bool FireOn(Object key)
        {
            if (key == null) return false;
            if (Resists(key, Kind.Fire)) return false;
            if (!Burning.IsBurning(key)) Remember(key, Kind.Fire);
            return true;
        }

        /// <summary>Is the enemy frozen, encased or slowed by our ice?</summary>
        internal static bool IsChilled(Enemy e) =>
            e != null && (IceEncasement.IsEncased(e) || FrostEffect.IsOn(e) || EnemySlow.IsSlowed(e) || IceWall.Holds(e));

        /// <summary>Every game: the ice on this enemy bursts in steam; the host ends the freeze, stun and slow.</summary>
        private static void Thaw(Enemy e)
        {
            Vector3 at = LightningTargets.Gone(e) ? e.transform.position : LightningTargets.Centre(e);
            IceEncasement.ShatterOn(e);
            FrostEffect.Melt(e);
            IceWall.Release(e);
            Steam.Spawn(at, 1.2f);
            if (!IsHost) return;
            try { e.Freeze(0f); }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"Thaw failed: {ex}"); }
            LightningTargets.EndStun(e);
            EnemySlow.Clear(e);
            Plugin.Log.LogInfo($"Elements: fire thawed {LightningTargets.Name(e)}.");
        }

        // ---------- ice ----------

        /// <summary>Host check before an icicle freezes an enemy: false if fresh fire still resists the ice.</summary>
        internal static bool CanIce(Enemy e) => e != null && !Resists(e, Kind.Ice);

        /// <summary>
        /// Ice reaches an enemy (every game). False if fresh fire still resists it (no freeze or casing). Otherwise a
        /// burn on it goes out in steam. <paramref name="force"/> ignores the resistance (the ice wall).
        /// </summary>
        internal static bool IceOn(Enemy e, bool force = false)
        {
            if (e == null) return false;
            if (!force && Resists(e, Kind.Ice)) return false;
            bool wasChilled = IsChilled(e);
            if (Burning.Extinguish(e))
            {
                Steam.Spawn(LightningTargets.Centre(e), 1.2f);
                if (IsHost) Plugin.Log.LogInfo($"Elements: ice put out {LightningTargets.Name(e)}.");
                Remember(e, Kind.Ice);
            }
            else if (!wasChilled) Remember(e, Kind.Ice);
            return true;
        }

        /// <summary>Ice reaches a player or a dummy: their burn goes out (they don't freeze).</summary>
        internal static void IceOn(Object key, Vector3 at, bool force = false)
        {
            if (key == null) return;
            if (!force && Resists(key, Kind.Ice)) return;
            if (Burning.Extinguish(key)) { Steam.Spawn(at, 0.9f); Remember(key, Kind.Ice); }
        }

        /// <summary>
        /// Where an icicle landed (every game, from the impact effect): the enemy it hit (if fresh fire doesn't
        /// resist it; returned so the caller encases it), and any player or dummy right there has its burn put out.
        /// </summary>
        internal static Enemy IcicleImpact(Vector3 point)
        {
            foreach (PlayerAvatar p in Burning.PlayersNear(point, 0.25f)) IceOn(p, p.transform.position + Vector3.up);
            var done = new HashSet<Object>();
            foreach (Collider c in Physics.OverlapSphere(point, 0.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == null || LightningTargets.Of(c) != null || c.GetComponentInParent<PlayerAvatar>() != null) continue;
                Transform root = Burning.DummyRoot(c);
                if (root != null && done.Add(root)) IceOn(root, c.bounds.center);
            }
            Enemy e = FrostEffect.NearestEnemy(point);
            return e != null && IceOn(e) ? e : null;
        }

        /// <summary>
        /// The ice wall rising (every game): burning players and dummies in its box are put out, and patches of fire
        /// under it go out. (Enemies are handled by the wall's own trapping, with IceOn(e, force: true).)
        /// </summary>
        internal static void WallRises(Vector3 centre, Vector3 half, Quaternion rotation)
        {
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (!ProjectileHits.Alive(p)) continue;
                Vector3 local = Quaternion.Inverse(rotation) * (p.transform.position + Vector3.up * 0.8f - centre);
                if (Mathf.Abs(local.x) <= half.x + 0.3f && Mathf.Abs(local.y) <= half.y + 0.8f && Mathf.Abs(local.z) <= half.z + 0.3f)
                    IceOn(p, p.transform.position + Vector3.up, force: true);
            }
            var done = new HashSet<Object>();
            foreach (Collider c in Physics.OverlapBox(centre, half, rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == null || LightningTargets.Of(c) != null || c.GetComponentInParent<PlayerAvatar>() != null) continue;
                Transform root = Burning.DummyRoot(c);
                if (root != null && done.Add(root)) IceOn(root, c.bounds.center, force: true);
            }
            FireArea.ExtinguishIn(centre, half, rotation);
        }
    }

    /// <summary>A puff of steam with a hiss, where fire and ice met (every game).</summary>
    internal class Steam : MonoBehaviour
    {
        private const float Duration = 2.2f;
        private float t;

        internal static void Spawn(Vector3 position, float size)
        {
            var go = new GameObject("HWP Steam");
            go.transform.position = position;
            go.AddComponent<Steam>().Setup(size);
            if (Elements.Hiss != null)
            {
                try { Elements.Hiss.Play(position, 1f, 1f, 1f, 1f); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Steam sound failed: {e}"); Elements.Hiss = null; }
            }
        }

        private void Setup(float size)
        {
            var ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.35f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f * size, 0.55f * size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.95f, 0.97f, 1f, 0.55f);
            main.gravityModifier = -0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 70f * size;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(16 * size)) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.35f * size;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.88f, 0.92f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.12f), new GradientAlphaKey(0.25f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sizeOver = ps.sizeOverLifetime; sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 0.8f; noise.scrollSpeed = 0.6f;
            var pr = GetComponent<ParticleSystemRenderer>();
            Material soft = SoftGlow.SharedMaterial;
            pr.sharedMaterial = soft != null ? soft : FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            ps.Play();
        }

        private void Update()
        {
            t += Time.deltaTime;
            if (t >= Duration) Destroy(gameObject);
        }

        private static AudioClip hissClip;

        /// <summary>A short hiss made in code: bright noise that bursts in and dies away over 0.8 s.</summary>
        internal static AudioClip HissClip
        {
            get
            {
                if (hissClip != null) return hissClip;
                const int rate = 44100;
                int n = (int)(rate * 0.8f);
                var data = new float[n];
                var rng = new System.Random(4321);
                float lp = 0f, prev = 0f;
                for (int i = 0; i < n; i++)
                {
                    float s = (float)i / rate;
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += (white - lp) * 0.55f;                        // tame the very top
                    float hp = lp - prev; prev = lp;                    // keep the hiss, lose the rumble
                    float env = Mathf.Clamp01(s / 0.02f) * Mathf.Exp(-s * 4.2f) * (1f + 0.25f * Mathf.Sin(s * 37f));
                    data[i] = Mathf.Clamp(hp * 1.6f * env, -1f, 1f) * 0.8f;
                }
                hissClip = AudioClip.Create("HWP Steam Hiss", n, 1, rate, false);
                hissClip.SetData(data, 0);
                hissClip.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return hissClip;
            }
        }
    }
}
