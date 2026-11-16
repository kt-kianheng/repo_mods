using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// A compass valuable's needle, floating in its glass sphere and pointing in 3D (every game works it out for
    /// itself; nothing is networked). The red half points at the target:
    ///  - Enemy Compass: the nearest living enemy.
    ///  - Extraction Compass: the nearest extraction point that isn't completed yet.
    /// It points straight at it (up and down too), however the compass is held. With nothing to point to (no enemies,
    /// the shop, every extraction done) the needle wanders: it swings to a new random direction every so often.
    /// The needle swings on a springy, slightly underdamped mount, so it overshoots and settles. As the target gets closer
    /// (from ShakeFromDistance down to ShakeFullDistance) the needle shakes round its direction, harder and faster the
    /// closer it is, up to ShakeMaxDegrees.
    /// </summary>
    public class Compass : MonoBehaviour
    {
        public enum Kind { Enemy, Extraction }

        // serialized, set by CompassBuilder
        public Kind kind;
        public Transform needle;
        public Transform emblem;
        public Vector3 modelCentre;   // the middle of the compass model, in the valuable's own space (for the value tag)

        private const float SearchInterval = 0.25f;
        private const float Stiffness = 45f, Damping = 6f;     // spring on the needle's direction

        private static readonly FieldInfo SpawnedField = AccessTools.Field(typeof(EnemyParent), "Spawned");
        private static readonly FieldInfo ParentEnemyField = AccessTools.Field(typeof(EnemyParent), "Enemy");
        private static readonly FieldInfo HealthField = AccessTools.Field(typeof(Enemy), "Health");
        private static readonly FieldInfo DeadField = AccessTools.Field(typeof(EnemyHealth), "dead");
        private static readonly FieldInfo StateField = AccessTools.Field(typeof(ExtractionPoint), "currentState");
        private static readonly FieldInfo ShopField = AccessTools.Field(typeof(ExtractionPoint), "isShop");

        // extraction points: shared by all compasses, looked up again every few seconds
        private static ExtractionPoint[] points = new ExtractionPoint[0];
        private static float pointsAt = -10f;

        private Vector3 pointing, velocity, wanderTarget;
        private float nextSearch, nextWander, shakeTime;
        private bool hasTarget;
        private Vector3 target;
        private bool emblemDone;

        private void Start()
        {
            pointing = Random.onUnitSphere;
            wanderTarget = pointing;
            if (emblem != null && !emblemDone)
            {
                emblemDone = true;
                if (kind == Kind.Enemy)
                {
                    try { if (RobeMini.Spawn(emblem, CompassBuilder.EmblemHeight, Settings.CompassRobeYaw.Value) != null) HideBuiltInEmblem(); }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Enemy Compass: the little Robe failed: {e}"); }
                }
                else
                {
                    // the level's real extraction screen in miniature; the simple screen from the model stays if there's none
                    try
                    {
                        if (ExtractionMini.Spawn(emblem, CompassBuilder.EmblemHeight) != null) HideBuiltInEmblem();
                    }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Extraction Compass: copying the extraction screen failed: {e}"); }
                }
            }
        }

        private void Update()
        {
            if (needle == null) return;
            if (Time.time >= nextSearch)
            {
                nextSearch = Time.time + SearchInterval;
                hasTarget = kind == Kind.Enemy ? NearestEnemy(needle.position, out target) : NearestExtraction(needle.position, out target);
            }

            Vector3 want;
            Vector3 to = hasTarget ? target - needle.position : Vector3.zero;
            if (hasTarget && to.sqrMagnitude > 1e-6f)
            {
                want = to.normalized;
            }
            else
            {
                // nothing to find: swing to a new random direction every so often
                if (Time.time >= nextWander)
                {
                    nextWander = Time.time + Random.Range(0.35f, 1.4f);
                    wanderTarget = Random.onUnitSphere;
                }
                want = wanderTarget;
            }
            want.Normalize();

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            velocity += ((want - pointing) * Stiffness - velocity * Damping) * dt;
            pointing += velocity * dt;
            if (pointing.sqrMagnitude < 1e-6f) pointing = want;
            pointing.Normalize();
            // shaking round that direction, stronger and faster the nearer the target (on top of the spring, so it's crisp)
            Vector3 shown = pointing;
            if (hasTarget)
            {
                float far = Mathf.Max(Settings.CompassShakeFar.Value, Settings.CompassShakeNear.Value + 0.1f);
                float near = Settings.CompassShakeNear.Value;
                float close = Mathf.Clamp01((far - to.magnitude) / (far - near));   // 0 at ShakeFromDistance, 1 at ShakeFullDistance
                if (close > 0f)
                {
                    shakeTime += Time.deltaTime * Mathf.Lerp(4f, 22f, close);
                    float amount = Mathf.Tan(Settings.CompassShakeMax.Value * Mathf.Deg2Rad) * close * close;
                    Vector3 jitter = new Vector3(Mathf.PerlinNoise(shakeTime, 0.1f), Mathf.PerlinNoise(shakeTime, 3.7f), Mathf.PerlinNoise(shakeTime, 7.3f)) * 2f
                                     - Vector3.one;
                    shown = (pointing + Vector3.ProjectOnPlane(jitter, pointing) * amount).normalized;
                }
            }
            // the needle model's red tip is its local -Z
            Vector3 up = Mathf.Abs(Vector3.Dot(shown, Vector3.up)) > 0.98f ? needle.parent.forward : Vector3.up;
            needle.rotation = Quaternion.LookRotation(-shown, up);
        }

        private void HideBuiltInEmblem()
        {
            Transform simple = emblem != null ? emblem.Find(CompassBuilder.SimpleEmblemName) : null;
            if (simple != null) simple.gameObject.SetActive(false);
        }

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
            if (Time.time - pointsAt > 3f)
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
