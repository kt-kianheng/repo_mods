using BepInEx.Configuration;

namespace TargetDummy
{
    /// <summary>Every setting, in BepInEx\config\TargetDummy.cfg. Saved values win over these defaults.</summary>
    internal static class Settings
    {
        // Placement
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> OnlyInShop;
        internal static ConfigEntry<bool> AutoPlace;
        internal static ConfigEntry<bool> PreferRightSide;
        internal static ConfigEntry<float> SideDistance;
        internal static ConfigEntry<float> ForwardDistance;
        internal static ConfigEntry<float> ExtraYaw;

        // Model
        internal static ConfigEntry<float> TotalHeight;
        internal static ConfigEntry<float> RobeHeight;
        internal static ConfigEntry<float> CrossbarHeight;
        internal static ConfigEntry<float> RobeYaw;
        internal static ConfigEntry<string> HideRobeParts;
        internal static ConfigEntry<bool> LogRobeParts;
        internal static ConfigEntry<bool> DebugLog;

        // Numbers
        internal static ConfigEntry<bool> ApplyRobeResistance;
        internal static ConfigEntry<bool> ShowForce;
        internal static ConfigEntry<bool> ShowTotal;
        internal static ConfigEntry<float> NumberSize;
        internal static ConfigEntry<float> NumberLifetime;
        internal static ConfigEntry<bool> NumbersThroughWalls;
        internal static ConfigEntry<float> Wobble;

        // Ally Dummy
        internal static ConfigEntry<bool> AllyEnabled;
        internal static ConfigEntry<int> AllyMaxHealth;
        internal static ConfigEntry<int> AllyStartHealth;
        internal static ConfigEntry<float> AllyResetAfter;
        internal static ConfigEntry<float> AllyTotalHeight;
        internal static ConfigEntry<float> DroidHeight;
        internal static ConfigEntry<float> AllyCrossbarHeight;
        internal static ConfigEntry<float> DroidYaw;
        internal static ConfigEntry<string> HideDroidParts;
        internal static ConfigEntry<bool> AllyHealthBar;

        internal static void Bind(ConfigFile cfg)
        {
            const string P = "Placement";
            Enabled = cfg.Bind(P, "Enabled", true, "Spawn the target dummy.");
            OnlyInShop = cfg.Bind(P, "OnlyInShop", true, "Only spawn it in the shop (set false to spawn it wherever the cosmetics gacha machine is).");
            AutoPlace = cfg.Bind(P, "AutoPlace", true,
                "Search around the gacha machine for the clear spot nearest the ideal one (floor, no walls, room for the stand). " +
                "Set false to use the ideal spot exactly, even if it's tight.");
            PreferRightSide = cfg.Bind(P, "PreferRightSide", true,
                "Put the Target Dummy on the machine's right side (seen from the front) and the Ally Dummy on its left. False swaps them.");
            SideDistance = cfg.Bind(P, "SideGap", 1.3f,
                new ConfigDescription("Ideal gap in metres between the side of the gacha machine and the dummy's centre.", new AcceptableValueRange<float>(0.5f, 8f)));
            ForwardDistance = cfg.Bind(P, "ForwardOffset", 0.3f,
                new ConfigDescription("Ideal distance in metres in front of the machine's centre (negative = further back).", new AcceptableValueRange<float>(-4f, 8f)));
            ExtraYaw = cfg.Bind(P, "ExtraYaw", 0f,
                new ConfigDescription("Extra turn in degrees. By default the dummy faces the same way as the machine's front.", new AcceptableValueRange<float>(-180f, 180f)));

            const string M = "Model";
            TotalHeight = cfg.Bind(M, "TotalHeight", 2.6f,
                new ConfigDescription("Height of the whole dummy in metres, stand included.", new AcceptableValueRange<float>(1.5f, 4f)));
            RobeHeight = cfg.Bind(M, "RobeHeight", 1.9f,
                new ConfigDescription("Height of the Robe on the stand in metres. Its hem sits at TotalHeight - RobeHeight.", new AcceptableValueRange<float>(0.8f, 3.5f)));
            CrossbarHeight = cfg.Bind(M, "CrossbarHeight", 0.72f,
                new ConfigDescription("Where the arm crossbar sits, as a fraction of the Robe's height from its hem (0.72 is about the shoulders).",
                    new AcceptableValueRange<float>(0.3f, 0.95f)));
            RobeYaw = cfg.Bind(M, "RobeYaw", 0f,
                new ConfigDescription("Turns only the Robe on the stand, in degrees, in case it faces the wrong way.", new AcceptableValueRange<float>(-180f, 180f)));
            HideRobeParts = cfg.Bind(M, "HideRobeParts", "",
                "Comma-separated names (or parts of names) of Robe pieces to hide, if an odd piece shows up. " +
                "Turn on LogRobeParts to list the names in LogOutput.log.");
            LogRobeParts = cfg.Bind(M, "LogRobeParts", true, "List the visible pieces of the Robe and the droid in LogOutput.log when each dummy is first built.");

            DebugLog = cfg.Bind(M, "DebugLog", true,
                "Write every hit, heal and placement decision to LogOutput.log (handy while testing; turn off later).");

            const string N = "Numbers";
            ApplyRobeResistance = cfg.Bind(N, "ApplyRobeResistance", true,
                "Show the damage after the Robe's own damage resistance, i.e. what a real Robe would lose. False shows the raw damage.");
            ShowForce = cfg.Bind(N, "ShowForce", true, "Show knockback, torque, stun and impact values under the damage.");
            ShowTotal = cfg.Bind(N, "ShowTotal", true, "Show a running total above the dummy. It resets after 3 seconds without a hit.");
            NumberSize = cfg.Bind(N, "NumberSize", 1f, new ConfigDescription("Size of the numbers.", new AcceptableValueRange<float>(0.3f, 4f)));
            NumberLifetime = cfg.Bind(N, "NumberLifetime", 4.4f, new ConfigDescription("Seconds each number stays up.", new AcceptableValueRange<float>(0.3f, 15f)));
            NumbersThroughWalls = cfg.Bind(N, "NumbersThroughWalls", true, "Draw the numbers on top of everything so the dummy never hides them.");
            Wobble = cfg.Bind(N, "Wobble", 1f,
                new ConfigDescription("How much the dummy rocks when hit (0 = rigid). Only the looks move; it never actually shifts.", new AcceptableValueRange<float>(0f, 3f)));

            const string A = "Ally Dummy";
            AllyEnabled = cfg.Bind(A, "Enabled", true,
                "Spawn the Ally Dummy: a player droid on a stand, on the other side of the gacha machine. It counts as a teammate: " +
                "friendly fire hurts it and healing (e.g. HatterWeaponsPack's Healing Wand) heals it. Its health never drops below 1.");
            AllyMaxHealth = cfg.Bind(A, "MaxHealth", 1000, new ConfigDescription("Its maximum health.", new AcceptableValueRange<int>(2, 100000)));
            AllyStartHealth = cfg.Bind(A, "StartHealth", 1000,
                new ConfigDescription("Health it starts with (capped at MaxHealth). Lower it to test healing straight away.", new AcceptableValueRange<int>(1, 100000)));
            AllyResetAfter = cfg.Bind(A, "ResetAfterSeconds", 0f,
                new ConfigDescription("Return to StartHealth after this many seconds without being hurt or healed (0 = never).", new AcceptableValueRange<float>(0f, 600f)));
            AllyTotalHeight = cfg.Bind(A, "TotalHeight", 2.5f,
                new ConfigDescription("Height of the whole Ally Dummy in metres, stand included.", new AcceptableValueRange<float>(1.5f, 4f)));
            DroidHeight = cfg.Bind(A, "DroidHeight", 1.6f,
                new ConfigDescription("Height of the droid on the stand in metres. Its feet hang at TotalHeight - DroidHeight.", new AcceptableValueRange<float>(0.6f, 3.5f)));
            AllyCrossbarHeight = cfg.Bind(A, "CrossbarHeight", 0.62f,
                new ConfigDescription("Where the arm crossbar sits, as a fraction of the droid's height from its feet.", new AcceptableValueRange<float>(0.3f, 0.95f)));
            DroidYaw = cfg.Bind(A, "DroidYaw", 0f,
                new ConfigDescription("Turns only the droid on the stand, in degrees, in case it faces the wrong way.", new AcceptableValueRange<float>(-180f, 180f)));
            HideDroidParts = cfg.Bind(A, "HideDroidParts", "",
                "Comma-separated names (or parts of names) of droid pieces to hide. The names are listed in LogOutput.log (LogRobeParts lists both models).");
            AllyHealthBar = cfg.Bind(A, "HealthBar", true, "Show a health bar with its health over its head.");
        }
    }
}
