using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// Hatter Valuables Pack: new valuables for R.E.P.O. Each one is built at startup from a vanilla valuable (for its
    /// grabbing, value, impacts and networking), with a model exported from Blender and embedded in this DLL.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "HatterValuablesPack";
        public const string Name = "HatterValuablesPack";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            if (Settings.NukeEnabled.Value) Register(NukeBuilder.DisplayName, NukeBuilder.Build, p => NukeBuilder.Registered = p);
            else Log.LogInfo($"{NukeBuilder.DisplayName} is switched off in the config.");
            if (Settings.EnemyCompassEnabled.Value) Register(CompassBuilder.EnemyName, CompassBuilder.BuildEnemy, p => TestSpawner.EnemyCompass = p);
            if (Settings.ExtractionCompassEnabled.Value) Register(CompassBuilder.ExtractionName, CompassBuilder.BuildExtraction, p => TestSpawner.ExtractionCompass = p);
            TestSpawner.Create();
            Log.LogInfo($"{Name} {Version} loaded.");
        }

        private static void Register(string displayName, System.Func<GameObject> build, System.Action<PrefabRef> onRegistered)
        {
            GameObject prefab = null;
            try { prefab = build(); }
            catch (System.Exception e) { Log.LogError($"Building the {displayName} failed: {e}"); }
            if (prefab == null) { Log.LogError($"{displayName} could not be built; it will not spawn."); return; }
            PrefabRef registered = REPOLib.Modules.Valuables.RegisterValuable(prefab);   // the generic preset: every level
            if (registered == null) { Log.LogError($"REPOLib refused to register the {displayName}."); return; }
            onRegistered(registered);
            Log.LogInfo($"{displayName} registered ('{registered.ResourcePath}').");
        }
    }
}
