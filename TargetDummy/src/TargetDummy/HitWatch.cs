using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TargetDummy
{
    /// <summary>
    /// Host side: notices everything that would hurt an enemy standing where the Target Dummy is, or a player
    /// standing where the Ally Dummy is (friendly fire).
    ///
    /// 1. Hurt zones (HurtCollider): bullets, melee swings, explosions, grenades, staffs, and other mods' blasts.
    ///    Every hurt zone that switches on is tracked; every frame (bullet zones only live a frame or two) the ones
    ///    near the dummy are tested with the same box or sphere the game uses. A zone counts once when it switches on, then again every
    ///    enemyDamageCooldown seconds if it has one, like the game's own hit cooldown.
    /// 2. Thrown or swung objects (PhysGrabObjectImpactDetector), Target Dummy only: the game's enemy formula for
    ///    object hits, using the Robe's own multiplier. Like the game, items and unbreakable objects don't count. (Thrown objects don't hurt players, so the Ally ignores them.)
    /// 3. Direct hits from other mods' projectiles arrive through Dummy.TargetDummyHit.
    /// </summary>
    [HarmonyPatch]
    internal static class HitWatch
    {
        private static readonly HashSet<HurtCollider> active = new HashSet<HurtCollider>();
        private static readonly List<HurtCollider> scratch = new List<HurtCollider>();
        private static readonly Collider[] overlap = new Collider[64];
        private static int primedFrame = -1;

        [HarmonyPostfix, HarmonyPatch(typeof(HurtCollider), "OnEnable")]
        private static void HurtEnabled(HurtCollider __instance)
        {
            active.Add(__instance);
            foreach (Dummy d in Dummy.All) d.NextHit.Remove(__instance);   // a fresh swing / shot / blast can hit again
        }

        [HarmonyPostfix, HarmonyPatch(typeof(HurtCollider), "OnDisable")]
        private static void HurtDisabled(HurtCollider __instance)
        {
            active.Remove(__instance);
        }

        internal static void Scan(Dummy dummy)
        {
            if (primedFrame < 0 || (active.Count == 0 && Time.frameCount - primedFrame > 600))
            {
                // hurt zones that were already on before the dummies existed
                foreach (HurtCollider h in Object.FindObjectsByType<HurtCollider>(FindObjectsSortMode.None)) if (h.isActiveAndEnabled) active.Add(h);
                primedFrame = Time.frameCount;
            }
            if (active.Count == 0) return;

            Bounds near = dummy.WorldBounds;
            near.Expand(0.5f);
            scratch.Clear();
            scratch.AddRange(active);
            foreach (HurtCollider h in scratch)
            {
                if (h == null) { active.Remove(h); continue; }
                if (!h.isActiveAndEnabled) continue;
                if (dummy.IsAlly ? !h.playerLogic : (!h.enemyLogic && !h.physLogic)) continue;   // wrong kind of hurt zone
                if (dummy.IsAlly && h.playerDamage <= 0 && !h.playerKill && h.playerHitForce <= 0f && h.playerTumbleForce <= 0f) continue;
                if (dummy.NextHit.TryGetValue(h, out float t) && Time.time < t) continue;
                Collider zone = h.GetComponent<Collider>();
                if (zone == null || !ShapeBounds(zone).Intersects(near)) continue;
                if (!Touches(zone, dummy)) continue;

                float cooldown = dummy.IsAlly ? h.playerDamageCooldown : h.enemyLogic ? h.enemyDamageCooldown : h.physDamageCooldown;
                if (dummy.NextHit.Count > 512) dummy.NextHit.Clear();
                dummy.NextHit[h] = cooldown > 0f ? Time.time + cooldown : float.MaxValue;
                HitInfo hit = FromHurtCollider(h, zone, dummy);
                if (Settings.DebugLog.Value)
                    Plugin.Log.LogInfo($"{dummy.name}: hit by '{h.name}' ({hit.Type}, damage {hit.Damage}, knockback {hit.Knockback:0.#}).");
                dummy.Report(hit);
            }
        }

        /// <summary>
        /// Where the hurt zone is right now, from its transform. (Collider.bounds lags behind: guns switch their hurt
        /// zone on, move it to where the bullet landed and switch it off again within a frame or two.)
        /// </summary>
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

        /// <summary>Same overlap test as HurtCollider.ColliderCheck, filtered to the dummy's hitboxes.</summary>
        private static bool Touches(Collider zone, Dummy dummy)
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
            for (int i = 0; i < count; i++) if (dummy.Owns(overlap[i])) return true;
            return false;
        }

        private static HitInfo FromHurtCollider(HurtCollider h, Collider zone, Dummy dummy)
        {
            Vector3 center = ShapeBounds(zone).center;
            Vector3 point = dummy.ClosestPoint(center);
            Vector3 dir = point - center;
            if (dir.sqrMagnitude < 1e-4f) dir = h.transform.forward;
            var hit = new HitInfo { Point = point, Direction = dir.normalized };
            if (dummy.IsAlly)
            {
                // what a teammate standing there would get
                hit.Type = HitInfo.Kind.Hurt;
                hit.Damage = h.playerDamage;
                hit.Kill = h.playerKill;
                hit.Knockback = h.playerHitForce;
                hit.Torque = h.playerTumbleForce;
                hit.Stun = h.playerStunTime;
                hit.Freeze = h.playerTumbleTime;
            }
            else if (h.enemyLogic)
            {
                hit.Type = HitInfo.Kind.Hurt;
                hit.Damage = h.enemyDamage;
                hit.Kill = h.enemyKill;
                hit.Knockback = h.enemyHitForce;
                hit.Torque = h.enemyHitTorque;
                hit.Stun = h.enemyStun ? h.enemyStunTime : 0f;
                hit.Freeze = h.enemyFreezeTime;
            }
            else
            {
                hit.Type = HitInfo.Kind.Push;
                hit.Knockback = h.physHitForce;
                hit.Torque = h.physHitTorque;
            }
            return hit;
        }

        // ---- thrown / swung objects hitting the dummy ----

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
                Dummy dummy = Dummy.Target;
                if (dummy == null || collision == null || !dummy.Owns(collision.collider)) return;
                if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
                if (InCartField != null && (bool)InCartField.GetValue(__instance)) return;
                // like the game: items (weapons, tools) and unbreakable objects don't hurt enemies by bumping into
                // them, so they don't hurt the dummy either (a melee weapon's own hits arrive by other routes)
                if (__instance.destroyDisable || (IndestructibleField != null && (bool)IndestructibleField.GetValue(__instance))) return;
                if (__instance.GetComponentInParent<ItemAttributes>() != null) return;
                if (impactCooldown.TryGetValue(__instance, out float t) && Time.time < t) return;

                // the game's enemy-hit rule: moving faster than 2 m/s, damage = 10 x level x mass x 0.5 x the Robe's multiplier
                Vector3 velocity = VelocityField != null ? (Vector3)VelocityField.GetValue(__instance) : Vector3.zero;
                float speed = velocity.magnitude;
                if (speed <= 2f) return;
                Rigidbody rb = RbField?.GetValue(__instance) as Rigidbody;
                float mass = rb != null ? rb.mass : 1f;
                float breakForce = F(BreakForceField, __instance);
                float scaledMass = Mathf.Max(mass, 1f);
                int level = breakForce > F(Level3Field, __instance) * scaledMass ? 3
                          : breakForce > F(Level2Field, __instance) * scaledMass ? 2
                          : breakForce > F(Level1Field, __instance) * scaledMass ? 1 : 0;
                float impactForce = F(ImpactForceField, __instance);
                if (level == 0 && impactForce <= 20f) return;   // a nudge, not a hit

                int damage = Mathf.RoundToInt(10 * level * mass * 0.5f);
                damage = BorrowedModel.RobeObjectHurt ? Mathf.RoundToInt(damage * BorrowedModel.RobeObjectHurtMultiplier) : 0;

                impactCooldown[__instance] = Time.time + 0.5f;
                if (impactCooldown.Count > 256) impactCooldown.Clear();
                Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : dummy.ClosestPoint(__instance.transform.position);
                dummy.Report(new HitInfo
                {
                    Type = HitInfo.Kind.Impact,
                    Point = point,
                    Direction = velocity.normalized,
                    Damage = damage,
                    ImpactForce = impactForce,
                    Speed = speed,
                });
            }
            catch (System.Exception e) { Plugin.Log.LogDebug($"Impact check failed: {e.Message}"); }
        }
    }
}
