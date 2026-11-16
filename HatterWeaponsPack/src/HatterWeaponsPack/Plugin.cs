using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Hatter Weapons Pack — custom weapons for R.E.P.O.
    /// Weapons are built at startup from the game's own items (for grabbing, battery and networking)
    /// with models exported from Blender and embedded in this DLL, so no AssetBundle is needed.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "HatterWeaponsPack";
        public const string Name = "HatterWeaponsPack";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            RocketExplosion.Init();
            FriendDummy.CreateWatcher();   // [Friendship Cannon] DebugDummy (off by default)

            Register(RocketLauncherBuilder.DisplayName, RocketLauncherBuilder.Build);
            Register(PlasmaRifleBuilder.DisplayName, PlasmaRifleBuilder.Build);
            Register(WandBuilder.EmberName, WandBuilder.BuildEmber);
            Register(WandBuilder.FrostName, WandBuilder.BuildFrost);
            Register(WandBuilder.HealName, WandBuilder.BuildHealing);
            Register(WandBuilder.LightningName, WandBuilder.BuildLightning);
            Register(WandBuilder.EmberStaffName, WandBuilder.BuildEmberStaff);
            Register(WandBuilder.FrostStaffName, WandBuilder.BuildFrostStaff);
            Register(WandBuilder.LightningStaffName, WandBuilder.BuildLightningStaff);
            Register(EvolutionCrystal.DisplayName, EvolutionCrystal.Build);   // after the staves (it maps wands to them)
            Register(OrbCannonBuilder.DisplayName, OrbCannonBuilder.Build);
            Register(OrbCannonBuilder.FriendDisplayName, OrbCannonBuilder.BuildFriendship);
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
