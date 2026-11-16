using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// The Alter Mimic semibot's hitbox: a standing capsule on the Default layer (bullets, thrown objects and players
    /// stop on it, like on a player's body), following the semibot on every game. It is NOT part of the enemy's own
    /// body, so the game's hurt code never finds an enemy in it; MimicHitWatch (host) turns whatever touches it into
    /// damage on the mimic's EnemyHealth instead (the game's enemy route never worked on the reshaped Hidden collider).
    /// Other mods' projectiles (HatterWeaponsPack) send TargetDummyHit(float[] { damage, dir.x, dir.y, dir.z }).
    /// </summary>
    internal class MimicBotHitbox : MonoBehaviour
    {
        internal static readonly List<MimicBotHitbox> All = new List<MimicBotHitbox>();
        internal MimicSemibot Bot;
        internal CapsuleCollider Capsule;
        internal readonly Dictionary<HurtCollider, float> NextHit = new Dictionary<HurtCollider, float>();
        private Rigidbody rb;

        internal static MimicBotHitbox Create(MimicSemibot bot, float height)
        {
            var go = new GameObject("Alter Mimic Semibot Hitbox");
            go.layer = 0;
            var hb = go.AddComponent<MimicBotHitbox>();
            hb.Bot = bot;
            hb.rb = go.AddComponent<Rigidbody>();
            hb.rb.isKinematic = true;
            hb.rb.useGravity = false;
            hb.rb.interpolation = RigidbodyInterpolation.Interpolate;
            hb.Capsule = go.AddComponent<CapsuleCollider>();
            hb.Capsule.direction = 1;
            hb.Capsule.radius = 0.35f;
            hb.Capsule.height = Mathf.Max(1.2f, height);
            hb.Capsule.center = new Vector3(0f, hb.Capsule.height * 0.5f, 0f);
            All.Add(hb);
            return hb;
        }

        /// <summary>Standing at feet, or curled up around the tumble ball.</summary>
        internal void Place(Vector3 at, bool tumbling, float height)
        {
            if (Capsule == null) return;
            float h = tumbling ? 0.9f : Mathf.Max(1.2f, height);
            if (!Mathf.Approximately(Capsule.height, h))
            {
                Capsule.height = h;
                Capsule.radius = tumbling ? 0.45f : 0.35f;
            }
            // tumbling: 'at' is the ball's centre; standing: the feet
            Capsule.center = tumbling ? Vector3.zero : new Vector3(0f, h * 0.5f, 0f);
            rb.position = at;
            transform.position = at;
        }

        internal bool Owns(Collider c) => c != null && c == Capsule;

        internal Vector3 ClosestPoint(Vector3 p) => Capsule != null ? Capsule.ClosestPoint(p) : transform.position;

        internal Bounds WorldBounds => Capsule != null ? Capsule.bounds : new Bounds(transform.position, Vector3.one);

        /// <summary>Other mods' direct hits (HatterWeaponsPack's projectiles send this to whatever they hit).</summary>
        private void TargetDummyHit(float[] data)
        {
            if (data == null || data.Length < 1 || !R.IsHost || Bot == null) return;
            Vector3 dir = data.Length >= 4 ? new Vector3(data[1], data[2], data[3]) : Vector3.zero;
            Bot.TakeHit(Mathf.RoundToInt(data[0]), dir, "a modded projectile");
        }

        private void LateUpdate()
        {
            if (R.IsHost && Bot != null) MimicHitWatch.Scan(this);
        }

        private void OnDestroy() => All.Remove(this);
    }

    /// <summary>
    /// Host: what hurts the Alter Mimic semibot (copied from TargetDummy's HitWatch, the route that works):
    /// 1. hurt zones (HurtCollider: bullets, melee, explosions, grenades, other mods' blasts) that can hurt an enemy,
    ///    tested every frame with the game's own overlap shape against the hitbox;
    /// 2. thrown or swung objects hitting the hitbox (the game's enemy formula for object hits).
    /// </summary>
    [HarmonyPatch]
    internal static class MimicHitWatch
    {
        private static readonly HashSet<HurtCollider> active = new HashSet<HurtCollider>();
        private static readonly List<HurtCollider> scratch = new List<HurtCollider>();
        private static readonly Collider[] overlap = new Collider[64];
        private static int primedFrame = -1;

        [HarmonyPostfix, HarmonyPatch(typeof(HurtCollider), "OnEnable")]
        private static void HurtEnabled(HurtCollider __instance)
        {
            active.Add(__instance);
            foreach (MimicBotHitbox hb in MimicBotHitbox.All) hb.NextHit.Remove(__instance);   // a fresh shot / swing can hit again
        }

        [HarmonyPostfix, HarmonyPatch(typeof(HurtCollider), "OnDisable")]
        private static void HurtDisabled(HurtCollider __instance) => active.Remove(__instance);

        internal static void Scan(MimicBotHitbox hb)
        {
            if (primedFrame < 0 || (active.Count == 0 && Time.frameCount - primedFrame > 600))
            {
                foreach (HurtCollider h in Object.FindObjectsByType<HurtCollider>(FindObjectsSortMode.None)) if (h.isActiveAndEnabled) active.Add(h);
                primedFrame = Time.frameCount;
            }
            if (active.Count == 0) return;
            Bounds near = hb.WorldBounds;
            near.Expand(0.5f);
            scratch.Clear();
            scratch.AddRange(active);
            foreach (HurtCollider h in scratch)
            {
                if (h == null) { active.Remove(h); continue; }
                if (!h.isActiveAndEnabled || !h.enemyLogic) continue;
                if (h.enemyDamage <= 0 && !h.enemyKill) continue;
                if (hb.NextHit.TryGetValue(h, out float t) && Time.time < t) continue;
                Collider zone = h.GetComponent<Collider>();
                if (zone == null || !ShapeBounds(zone).Intersects(near)) continue;
                if (!Touches(zone, hb)) continue;
                if (hb.NextHit.Count > 512) hb.NextHit.Clear();
                hb.NextHit[h] = h.enemyDamageCooldown > 0f ? Time.time + h.enemyDamageCooldown : float.MaxValue;
                Vector3 c = ShapeBounds(zone).center;
                Vector3 dir = hb.ClosestPoint(c) - c;
                if (dir.sqrMagnitude < 1e-4f) dir = h.transform.forward;
                int damage = h.enemyKill ? 9999 : h.enemyDamage;
                hb.Bot?.TakeHit(damage, dir.normalized, $"'{h.name}'");
            }
        }

        private static Bounds ShapeBounds(Collider zone)
        {
            Transform t = zone.transform;
            Vector3 s = t.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            if (zone is BoxCollider box) return new Bounds(t.TransformPoint(box.center), Vector3.one * (box.size.magnitude * scale));
            if (zone is SphereCollider sphere) return new Bounds(t.TransformPoint(sphere.center), Vector3.one * (sphere.radius * 2f * scale));
            if (zone is CapsuleCollider cap) return new Bounds(t.TransformPoint(cap.center), Vector3.one * (Mathf.Max(cap.height, cap.radius * 2f) * scale));
            return zone.bounds;
        }

        private static bool Touches(Collider zone, MimicBotHitbox hb)
        {
            Transform t = zone.transform;
            int count;
            if (zone is BoxCollider box)
            {
                Vector3 half = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
                half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
                count = Physics.OverlapBoxNonAlloc(t.TransformPoint(box.center), half, overlap, t.rotation, 1, QueryTriggerInteraction.Ignore);
            }
            else if (zone is SphereCollider sphere)
            {
                Vector3 s = t.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                count = Physics.OverlapSphereNonAlloc(t.TransformPoint(sphere.center), radius, overlap, 1, QueryTriggerInteraction.Ignore);
            }
            else
            {
                Bounds b = zone.bounds;
                count = Physics.OverlapBoxNonAlloc(b.center, b.extents, overlap, Quaternion.identity, 1, QueryTriggerInteraction.Ignore);
            }
            for (int i = 0; i < count; i++) if (hb.Owns(overlap[i])) return true;
            return false;
        }

        // ---- thrown / swung objects ----

        private static readonly FieldInfo RbField = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "rb");
        private static readonly FieldInfo BreakForceField = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "breakForce");
        private static readonly FieldInfo Level1Field = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "impactLevel1");
        private static readonly FieldInfo Level2Field = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "impactLevel2");
        private static readonly FieldInfo Level3Field = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "impactLevel3");
        private static readonly FieldInfo VelocityField = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "previousPreviousVelocityRaw");
        private static readonly FieldInfo ImpactForceField = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "impactForce");
        private static readonly FieldInfo IndestructibleField = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "isIndestructible");
        private static readonly FieldInfo InCartField = AccessTools.Field(typeof(PhysGrabObjectImpactDetector), "inCart");
        private static readonly Dictionary<PhysGrabObjectImpactDetector, float> impactCooldown = new Dictionary<PhysGrabObjectImpactDetector, float>();

        private static float F(FieldInfo f, object o) => f != null ? (float)f.GetValue(o) : 0f;

        [HarmonyPostfix, HarmonyPatch(typeof(PhysGrabObjectImpactDetector), "OnCollisionStay")]
        private static void ObjectImpact(PhysGrabObjectImpactDetector __instance, Collision collision)
        {
            try
            {
                if (MimicBotHitbox.All.Count == 0 || collision == null || !R.IsHost) return;
                MimicBotHitbox hb = null;
                foreach (MimicBotHitbox h in MimicBotHitbox.All) if (h.Owns(collision.collider)) { hb = h; break; }
                if (hb == null || hb.Bot == null) return;
                if (hb.Bot.IsHolding(__instance)) return;   // what it carries itself
                if (InCartField != null && (bool)InCartField.GetValue(__instance)) return;
                if (__instance.destroyDisable || (IndestructibleField != null && (bool)IndestructibleField.GetValue(__instance))) return;
                if (__instance.GetComponentInParent<ItemAttributes>() != null) return;
                if (impactCooldown.TryGetValue(__instance, out float t) && Time.time < t) return;

                Vector3 velocity = VelocityField != null ? (Vector3)VelocityField.GetValue(__instance) : Vector3.zero;
                if (velocity.magnitude <= 2f) return;
                Rigidbody rb = RbField?.GetValue(__instance) as Rigidbody;
                float mass = rb != null ? rb.mass : 1f;
                float breakForce = F(BreakForceField, __instance);
                float scaledMass = Mathf.Max(mass, 1f);
                int level = breakForce > F(Level3Field, __instance) * scaledMass ? 3
                          : breakForce > F(Level2Field, __instance) * scaledMass ? 2
                          : breakForce > F(Level1Field, __instance) * scaledMass ? 1 : 0;
                if (level == 0 && F(ImpactForceField, __instance) <= 20f) return;   // a nudge, not a hit
                int damage = Mathf.Max(1, Mathf.RoundToInt(10 * level * mass * 0.5f));
                impactCooldown[__instance] = Time.time + 0.5f;
                if (impactCooldown.Count > 256) impactCooldown.Clear();
                hb.Bot.TakeHit(damage, velocity.normalized, $"a thrown {__instance.name}");
            }
            catch (System.Exception e) { Plugin.Log.LogDebug($"Alter Mimic: impact check failed: {e.Message}"); }
        }
    }
}
