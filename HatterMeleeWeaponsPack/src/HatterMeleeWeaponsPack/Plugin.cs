using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// Hatter's melee weapons for R.E.P.O. Each weapon is built at startup from one of the game's own melee items
    /// (for grabbing, the shop and networking), with its model exported from Blender and embedded in this DLL.
    ///   Nunchuck: you hold one handle; the chain and the other handle are real physics. Damage comes from how
    ///             fast the free handle is moving when it hits.
    ///   Scythe:   a long scythe held level, blade hanging down at the far end. Hook an enemy and pull (or back away):
    ///             only a pull through the blade's edge cuts.
    ///   Katana:   the vanilla sword's handling, 120 damage. Press E to charge it (the edge glows red); a stroke then
    ///             tears a rift in the air, and a cutting wave flies out of it.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "HatterMeleeWeaponsPack";
        public const string Name = "HatterMeleeWeaponsPack";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            Register(NunchuckBuilder.DisplayName, NunchuckBuilder.Build);
            Register(ScytheBuilder.DisplayName, ScytheBuilder.Build);
            Register(KatanaBuilder.DisplayName, KatanaBuilder.Build);
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
