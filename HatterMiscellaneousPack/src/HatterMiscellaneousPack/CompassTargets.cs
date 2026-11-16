using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// What the pocket compasses point to (the same rules as HatterValuablesPack's compass valuables):
    ///  - Enemy: the nearest living enemy that's in the level right now (despawned enemies don't count).
    ///  - Extraction: the nearest extraction point that isn't completed yet (not the shop's).
    /// </summary>
    internal static class CompassTargets
    {
        internal enum Kind { Enemy, Extraction }

        private static readonly FieldInfo SpawnedField = AccessTools.Field(typeof(EnemyParent), "Spawned");
        private static readonly FieldInfo ParentEnemyField = AccessTools.Field(typeof(EnemyParent), "Enemy");
        private static readonly FieldInfo HealthField = AccessTools.Field(typeof(Enemy), "Health");
        private static readonly FieldInfo DeadField = AccessTools.Field(typeof(EnemyHealth), "dead");
        private static readonly FieldInfo StateField = AccessTools.Field(typeof(ExtractionPoint), "currentState");
        private static readonly FieldInfo ShopField = AccessTools.Field(typeof(ExtractionPoint), "isShop");

        // extraction points: looked up again every few seconds
        private static ExtractionPoint[] points = new ExtractionPoint[0];
        private static float pointsAt = -10f;

        internal static bool Find(Kind kind, Vector3 from, out Vector3 target) =>
            kind == Kind.Enemy ? NearestEnemy(from, out target) : NearestExtraction(from, out target);

        internal static string Describe(Kind kind) => kind == Kind.Enemy ? "enemy" : "extraction point";

        private static bool NearestEnemy(Vector3 from, out Vector3 best)
        {
            best = default;
            EnemyDirector director = EnemyDirector.instance;
            if (director == null || director.enemiesSpawned == null) return false;
            float bestSq = float.MaxValue;
            foreach (EnemyParent p in director.enemiesSpawned)
            {
                if (p == null) continue;
                if (SpawnedField != null && !(bool)SpawnedField.GetValue(p)) continue;
                Enemy e = (ParentEnemyField != null ? ParentEnemyField.GetValue(p) as Enemy : null) ?? p.GetComponentInChildren<Enemy>();
                if (e == null || !e.gameObject.activeInHierarchy) continue;
                EnemyHealth h = HealthField != null ? HealthField.GetValue(e) as EnemyHealth : null;
                if (h != null && DeadField != null && (bool)DeadField.GetValue(h)) continue;
                Vector3 at = e.CenterTransform != null ? e.CenterTransform.position : e.transform.position;
                float d = (at - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = at; }
            }
            return bestSq < float.MaxValue;
        }

        private static bool NearestExtraction(Vector3 from, out Vector3 best)
        {
            best = default;
            if (Time.time - pointsAt > 3f || Time.time < pointsAt)
            {
                pointsAt = Time.time;
                points = Object.FindObjectsByType<ExtractionPoint>(FindObjectsSortMode.None);
            }
            float bestSq = float.MaxValue;
            foreach (ExtractionPoint ep in points)
            {
                if (ep == null || !ep.gameObject.activeInHierarchy) continue;
                if (ShopField != null && (bool)ShopField.GetValue(ep)) continue;
                if (StateField != null && StateField.GetValue(ep)?.ToString() == "Complete") continue;
                float d = (ep.transform.position - from).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = ep.transform.position; }
            }
            return bestSq < float.MaxValue;
        }
    }
}
