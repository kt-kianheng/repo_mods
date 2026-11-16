using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HatterEnemiesPack
{
    /// <summary>
    /// Hatter Enemies Pack: new enemies for R.E.P.O. Each one is built at startup from a vanilla enemy (for its
    /// spawning, pathing, health and networking) with its own behaviour on top.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "HatterEnemiesPack";
        public const string Name = "HatterEnemiesPack";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            if (Settings.MimicEnabled.Value)
            {
                try { MimicBuilder.BuildAndRegister(); }
                catch (System.Exception e) { Log.LogError($"Building the Alter Mimic failed: {e}"); }
            }
            else Log.LogInfo("The Alter Mimic is switched off in the config.");
            MimicFx.Warm();
            MimicDebug.Create();
            SemibotAnimTest.Create();   // [Debug] SemibotAnimTest (off by default)
            Log.LogInfo($"{Name} {Version} loaded.");
        }
    }
}
