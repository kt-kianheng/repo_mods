using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// Hatter Miscellaneous Pack: consumables and other odds and ends for R.E.P.O. Each item is built at startup from a
    /// vanilla item (for its grabbing, use button and networking), with a model exported from Blender and embedded in
    /// this DLL.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "HatterMiscellaneousPack";
        public const string Name = "HatterMiscellaneousPack";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            if (Settings.EnemyCompassEnabled.Value) Register(CompassItemBuilder.EnemyName, CompassItemBuilder.BuildEnemy);
            else Log.LogInfo($"{CompassItemBuilder.EnemyName} is switched off in the config.");
            if (Settings.ExtractionCompassEnabled.Value) Register(CompassItemBuilder.ExtractionName, CompassItemBuilder.BuildExtraction);
            else Log.LogInfo($"{CompassItemBuilder.ExtractionName} is switched off in the config.");
            if (Settings.MonocleEnabled.Value) Register(MonocleBuilder.DisplayName, MonocleBuilder.Build);
            else Log.LogInfo($"{MonocleBuilder.DisplayName} is switched off in the config.");
            Log.LogInfo($"{Name} {Version} loaded.");
        }

        private static void Register(string displayName, System.Func<GameObject> build)
        {
            GameObject prefab = null;
            try { prefab = build(); }
            catch (System.Exception e) { Log.LogError($"Building the {displayName} failed: {e}"); }
            if (prefab == null) { Log.LogError($"{displayName} could not be built; it will not be available."); return; }
            PrefabRef registered = REPOLib.Modules.Items.RegisterItem(prefab.GetComponent<ItemAttributes>());
            if (registered == null) Log.LogError($"REPOLib refused to register the {displayName}.");
            else Log.LogInfo($"{displayName} registered.");
        }
    }
}
