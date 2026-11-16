using BepInEx.Configuration;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>Testing (host only): [Debug] SpawnKey spawns an Alter Mimic a few metres from you.</summary>
    internal class MimicDebug : MonoBehaviour
    {
        internal static void Create()
        {
            var go = new GameObject("HatterEnemiesPack.Debug");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<MimicDebug>();
        }

        private void Update()
        {
            KeyboardShortcut key = Settings.DebugSpawnKey.Value;
            if (key.MainKey == KeyCode.None || !key.IsDown()) return;
            if (!R.IsHost || !SemiFunc.RunIsLevel() || MimicBuilder.Setup == null || PlayerAvatar.instance == null)
            {
                Plugin.Log.LogInfo("Alter Mimic: the spawn key only works for the host, in a level.");
                return;
            }
            Vector3 me = PlayerAvatar.instance.transform.position;
            LevelPoint p = SemiFunc.LevelPointGet(me, 3f, 15f);
            Vector3 at = p != null ? p.transform.position : me + PlayerAvatar.instance.transform.forward * 3f;
            var spawned = REPOLib.Modules.Enemies.SpawnEnemy(MimicBuilder.Setup, at, Quaternion.identity, false);
            Plugin.Log.LogInfo($"Alter Mimic: debug spawn at {at}: {(spawned != null && spawned.Count > 0 ? "done" : "failed")}.");
        }
    }
}
