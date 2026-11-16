using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace TargetDummy
{
    /// <summary>
    /// Target Dummy for R.E.P.O.: two training dummies beside the cosmetics gacha machine in the shop.
    ///   Target Dummy: a Robe on a wooden stand. Can't be moved or killed; every hit pops up the damage a Robe would
    ///                 take, plus any knockback, torque, stun or impact force behind it.
    ///   Ally Dummy:   a player droid on the same stand, treated as a teammate: friendly fire hurts it and healing
    ///                 heals it, with 1000 health that never drops below 1 and a health bar.
    /// The host decides what hit or healed them and tells every player.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "TargetDummy";
        public const string Name = "TargetDummy";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Net.Init();
            Log.LogInfo($"{Name} {Version} loaded.");
        }
    }
}
