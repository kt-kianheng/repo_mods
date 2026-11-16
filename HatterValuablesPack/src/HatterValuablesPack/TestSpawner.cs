using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// [Compasses] SpawnInTruckForTesting: the host drops one of each compass in the truck a few seconds into every
    /// level, so they can be checked without hunting for them.
    /// </summary>
    internal class TestSpawner : MonoBehaviour
    {
        internal static PrefabRef EnemyCompass, ExtractionCompass;
        private LevelGenerator doneFor;
        private float generatedAt = -1f;

        internal static void Create()
        {
            var go = new GameObject("HatterValuablesPack.TestSpawner");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<TestSpawner>();
        }

        private void Update()
        {
            if (!Settings.CompassTestSpawn.Value) return;
            LevelGenerator lg = LevelGenerator.Instance;
            if (lg == null || !lg.Generated || !SemiFunc.RunIsLevel() || !SemiFunc.IsMasterClientOrSingleplayer()) { generatedAt = -1f; return; }
            if (lg == doneFor) return;
            if (generatedAt < 0f) generatedAt = Time.time;
            if (Time.time - generatedAt < 3f) return;   // let the level's own valuables settle first
            doneFor = lg;
            if (lg.LevelPathTruck == null) { Plugin.Log.LogWarning("Compass test spawn: the truck wasn't found."); return; }
            Vector3 at = lg.LevelPathTruck.transform.position + Vector3.up * 0.6f;
            Vector3 side = lg.LevelPathTruck.transform.right * 0.35f;
            Spawn(EnemyCompass, at - side, "Enemy Compass");
            Spawn(ExtractionCompass, at + side, "Extraction Compass");
        }

        private static void Spawn(PrefabRef prefab, Vector3 at, string label)
        {
            if (prefab == null) return;
            try
            {
                REPOLib.Modules.Valuables.SpawnValuable(prefab, at, Quaternion.identity);
                Plugin.Log.LogInfo($"Compass test spawn: {label} put in the truck.");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Compass test spawn: {label} failed: {e}"); }
        }
    }
}
