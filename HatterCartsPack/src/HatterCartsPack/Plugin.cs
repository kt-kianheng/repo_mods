using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>
    /// Hatter Carts Pack: carts for R.E.P.O. Each cart is assembled at startup from the game's own cart prefabs
    /// (for pocketing, handling, physics and networking), so there's no asset bundle.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    // The deprecated stand-alone OrbCart mod: loaded first (if installed) so it can be detected below.
    [BepInDependency(OldOrbCartGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "HatterCartsPack";
        public const string Name = "HatterCartsPack";
        public const string Version = "1.0.0";

        private const string OldOrbCartGuid = "OrbCart";

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            if (Chainloader.PluginInfos.ContainsKey(OldOrbCartGuid))
                Log.LogWarning($"The old OrbCart mod is installed. It's deprecated and its cart is now part of {Name}; " +
                               $"uninstall OrbCart. This pack's {OrbCartBuilder.DisplayName} is skipped so there aren't two.");
            else if (Settings.OrbCartEnabled.Value) Register(OrbCartBuilder.DisplayName, OrbCartBuilder.Build);
            else Log.LogInfo($"{OrbCartBuilder.DisplayName} is switched off in the config.");

            if (Settings.CartPlusEnabled.Value) Register(CartPlusBuilder.DisplayName, CartPlusBuilder.Build);
            else Log.LogInfo($"{CartPlusBuilder.DisplayName} is switched off in the config.");

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
