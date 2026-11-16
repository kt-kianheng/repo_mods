using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>Hit detection and damage shared by every projectile in the pack (host side).</summary>
    internal static class ProjectileHits
    {
        // players are treated as an upright capsule for direct hits
        private const float PlayerRadius = 0.4f;
        private const float PlayerHeight = 1.6f;

        private static readonly FieldInfo EnemyHealthField = AccessTools.Field(typeof(Enemy), "Health");
        private static readonly FieldInfo DisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo DeadField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static bool Flag(FieldInfo field, object target) => field != null && (bool)field.GetValue(target);

        internal static int Mask => SemiFunc.LayerMaskGetVisionObstruct().value | LayerMask.GetMask("Enemy");

        /// <summary>The player holding the weapon (never takes a direct hit from their own shot).</summary>
        internal static PlayerAvatar Holder(PhysGrabObject pgo)
        {
            if (pgo != null && pgo.playerGrabbing != null && pgo.playerGrabbing.Count > 0 && pgo.playerGrabbing[0] != null)
                return pgo.playerGrabbing[0].playerAvatar;
            return null;
        }

        /// <summary>Nearest world/enemy hit along this step, ignoring the weapon that fired.</summary>
        internal static bool World(Vector3 from, Vector3 step, float radius, PhysGrabObject self, out RaycastHit best)
        {
            best = default;
            float bestDistance = float.MaxValue;
            RaycastHit[] hits = Physics.SphereCastAll(from, radius, step.normalized, step.magnitude, Mask, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null) continue;
                if (self != null && h.collider.GetComponentInParent<PhysGrabObject>() == self) continue;
                float d = h.distance <= 0f && h.point == Vector3.zero ? 0f : h.distance;
                if (d < bestDistance) { bestDistance = d; best = h; if (h.point == Vector3.zero) best.point = from; }
            }
            return bestDistance < float.MaxValue;
        }

        /// <summary>Direct hit on a player (anyone but the shooter) closer than maxDistance.</summary>
        internal static bool Player(Vector3 from, Vector3 step, float radius, float maxDistance, PlayerAvatar shooter,
                                    out PlayerAvatar hitPlayer, out Vector3 point)
        {
            hitPlayer = null; point = default;
            float best = maxDistance;
            Vector3 to = from + step;
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (p == null || p == shooter || p.playerHealth == null) continue;
                if (Flag(DisabledField, p) || Flag(DeadField, p)) continue;
                Vector3 bottom = p.transform.position + Vector3.up * PlayerRadius;
                Vector3 top = p.transform.position + Vector3.up * (PlayerHeight - PlayerRadius);
                ClosestPoints(from, to, bottom, top, out Vector3 onPath, out Vector3 onBody);
                float reach = PlayerRadius + radius;
                if ((onPath - onBody).sqrMagnitude > reach * reach) continue;
                float d = Vector3.Distance(from, onPath);
                if (d < best) { best = d; hitPlayer = p; point = onPath; }
            }
            return hitPlayer != null;
        }

        /// <summary>A player that can be hurt: there, not disabled, not dead.</summary>
        internal static bool Alive(PlayerAvatar p) => p != null && p.playerHealth != null && !Flag(DisabledField, p) && !Flag(DeadField, p);

        internal static void HurtPlayer(PlayerAvatar player, int damage, Vector3 point)
        {
            try { player.playerHealth.HurtOther(damage, point, false, -1, false); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Direct hit on player failed: {e}"); }
        }

        /// <summary>Damages the enemy that owns this collider, if any. The game syncs enemy health from the host.</summary>
        internal static void HurtEnemy(Collider hitCollider, int damage, Vector3 direction)
        {
            if (hitCollider == null) return;
            EnemyRigidbody erb = hitCollider.GetComponentInParent<EnemyRigidbody>();
            Enemy enemy = erb != null ? erb.enemy : hitCollider.GetComponentInParent<Enemy>();
            if (enemy == null)
            {
                // not an enemy: let the TargetDummy mod (if installed) count the hit; nothing happens otherwise
                hitCollider.SendMessageUpwards("TargetDummyHit", new float[] { damage, direction.x, direction.y, direction.z },
                                               SendMessageOptions.DontRequireReceiver);
                return;
            }
            EnemyHealth health = EnemyHealthField != null ? EnemyHealthField.GetValue(enemy) as EnemyHealth : null;
            if (health == null) health = enemy.GetComponentInChildren<EnemyHealth>();
            if (health != null) health.Hurt(damage, direction);
        }

        /// <summary>Closest points between segments AB and CD.</summary>
        private static void ClosestPoints(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 p1, out Vector3 p2)
        {
            Vector3 d1 = b - a, d2 = d - c, r = a - c;
            float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (aa <= 1e-8f) { s = 0f; t = ee > 1e-8f ? Mathf.Clamp01(f / ee) : 0f; }
            else
            {
                float cc = Vector3.Dot(d1, r);
                if (ee <= 1e-8f) { t = 0f; s = Mathf.Clamp01(-cc / aa); }
                else
                {
                    float bb = Vector3.Dot(d1, d2), denom = aa * ee - bb * bb;
                    s = denom > 1e-8f ? Mathf.Clamp01((bb * f - cc * ee) / denom) : 0f;
                    t = (bb * s + f) / ee;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-cc / aa); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((bb - cc) / aa); }
                }
            }
            p1 = a + d1 * s; p2 = c + d2 * t;
        }
    }
}
