using BepInEx.Configuration;

namespace HatterValuablesPack
{
    /// <summary>Every config value, in BepInEx/config/HatterValuablesPack.cfg.</summary>
    internal static class Settings
    {
        // [A-Bomb]
        internal static ConfigEntry<bool> NukeEnabled;
        internal static ConfigEntry<float> NukeChancePerLevel;
        internal static ConfigEntry<int> NukeValueMin;
        internal static ConfigEntry<int> NukeValueMax;
        internal static ConfigEntry<float> NukeSizeVsPiano;
        internal static ConfigEntry<float> NukeWeightVsPiano;
        internal static ConfigEntry<float> NukeHeavyHitForce;
        internal static ConfigEntry<int> NukeHitsToArm;
        internal static ConfigEntry<float> NukeInstantArmForce;
        internal static ConfigEntry<float> NukeFuseSeconds;
        internal static ConfigEntry<int> NukeKillRooms;
        internal static ConfigEntry<int> NukeFlashRooms;
        internal static ConfigEntry<float> NukeMinKillRadius;
        internal static ConfigEntry<bool> NukeDebugLog;

        // [Enemy Compass] / [Extraction Compass]
        internal static ConfigEntry<bool> EnemyCompassEnabled, ExtractionCompassEnabled;
        internal static ConfigEntry<int> EnemyCompassValueMin, EnemyCompassValueMax, ExtractionCompassValueMin, ExtractionCompassValueMax;
        internal static ConfigEntry<float> CompassScale;
        internal static ConfigEntry<float> CompassEmblemVsCompass;
        internal static ConfigEntry<float> CompassRobeYaw;
        internal static ConfigEntry<string> CompassHideRobeParts;
        internal static ConfigEntry<string> CompassExtractionParts;
        internal static ConfigEntry<bool> CompassTestSpawn;
        internal static ConfigEntry<float> CompassShakeFar, CompassShakeNear, CompassShakeMax;

        internal static void Bind(ConfigFile c)
        {
            const string n = "A-Bomb";
            NukeEnabled = c.Bind(n, "Enabled", true, "Adds the A-Bomb valuable (restart to apply).");
            NukeChancePerLevel = c.Bind(n, "ChancePerLevel", 0.1f, new ConfigDescription(
                "Chance that a level has an A-Bomb (at most one per level, in a wide valuable spot). It doesn't count towards the level's valuable budget, so it never crowds out other valuables.",
                new AcceptableValueRange<float>(0f, 1f)));
            NukeValueMin = c.Bind(n, "ValueMin", 40000, "Lowest value ($). The game picks a random value between ValueMin and ValueMax, rounded to $100.");
            NukeValueMax = c.Bind(n, "ValueMax", 60000, "Highest value ($).");
            NukeSizeVsPiano = c.Bind(n, "SizeVsPiano", 0.85f, new ConfigDescription(
                "Length compared with the vanilla piano's width (restart to apply).", new AcceptableValueRange<float>(0.3f, 1.5f)));
            NukeWeightVsPiano = c.Bind(n, "WeightVsPiano", 1.3f, new ConfigDescription(
                "Weight compared with the vanilla piano (restart to apply).", new AcceptableValueRange<float>(0.1f, 5f)));
            NukeHeavyHitForce = c.Bind(n, "HeavyHitForce", 150f,
                "A hit at least this hard counts towards arming (the game calls 150 and up a heavy impact). Lighter bumps do nothing. With DebugLog on, every heavy hit's force is logged.");
            NukeHitsToArm = c.Bind(n, "HitsToArm", 3, new ConfigDescription("Heavy hits that arm the bomb.", new AcceptableValueRange<int>(1, 20)));
            NukeInstantArmForce = c.Bind(n, "InstantArmForce", 450f, "A single hit at least this hard (a long fall) arms the bomb at once.");
            NukeFuseSeconds = c.Bind(n, "FuseSeconds", 10f, new ConfigDescription(
                "Seconds from arming to the explosion. Smoke leaks out meanwhile; it can't be stopped.", new AcceptableValueRange<float>(1f, 60f)));
            NukeKillRooms = c.Bind(n, "KillRooms", 1, new ConfigDescription(
                "Players and enemies this many rooms away or closer die (0 = only the bomb's room, 1 = also the rooms next to it), and valuables there are destroyed. The truck counts as a room too. Also: the blinding flash.",
                new AcceptableValueRange<int>(0, 5)));
            NukeFlashRooms = c.Bind(n, "FlashRooms", 2, new ConfigDescription(
                "Players further than KillRooms but no more than this many rooms away see a flash from the bomb's direction (0 or KillRooms = off).", new AcceptableValueRange<int>(0, 6)));
            NukeMinKillRadius = c.Bind(n, "MinKillRadius", 6f,
                "Everything within this many metres dies too, whatever the rooms (a backup for oddly split rooms). 0 = off.");
            const string ec = "Enemy Compass", xc = "Extraction Compass", cs = "Compasses";
            EnemyCompassEnabled = c.Bind(ec, "Enabled", true, "Adds the Enemy Compass valuable: its needle points to the nearest enemy, and wanders when there are none (restart to apply).");
            EnemyCompassValueMin = c.Bind(ec, "ValueMin", 2500, "Lowest value ($).");
            EnemyCompassValueMax = c.Bind(ec, "ValueMax", 5000, "Highest value ($).");
            ExtractionCompassEnabled = c.Bind(xc, "Enabled", true, "Adds the Extraction Compass valuable: its needle points to the nearest extraction point not yet completed, and wanders when there are none (restart to apply).");
            ExtractionCompassValueMin = c.Bind(xc, "ValueMin", 2500, "Lowest value ($).");
            ExtractionCompassValueMax = c.Bind(xc, "ValueMax", 5000, "Highest value ($).");
            CompassScale = c.Bind(cs, "ItemScale", 5.2f, new ConfigDescription(
                "Scale of both compasses (1 = 8 cm across and 9 cm tall without the emblem; 5.2 = 43 cm across; restart to apply).", new AcceptableValueRange<float>(0.5f, 12f)));
            CompassEmblemVsCompass = c.Bind(cs, "EmblemVsCompass", 0.5f, new ConfigDescription(
                "Height of the emblem on top (the Robe, the extraction screen, the eye or the dollar sign) compared with the compass's frame.", new AcceptableValueRange<float>(0.1f, 2f)));
            CompassRobeYaw = c.Bind(cs, "RobeYaw", 0f, new ConfigDescription(
                "Turns the Enemy Compass's little Robe, in degrees, in case it faces the wrong way.", new AcceptableValueRange<float>(-180f, 180f)));
            CompassHideRobeParts = c.Bind(cs, "HideRobeParts", "",
                "Comma-separated names (or parts of names) of Robe pieces to hide on the Enemy Compass, if an odd piece shows up (the names are listed in LogOutput.log).");

            CompassExtractionParts = c.Bind(cs, "ExtractionScreenParts", "",
                "The Extraction Compass copies the level's real extraction screen. Normally the pieces round the screen are picked by themselves; if the wrong ones show up, list the right ones here (comma-separated names or parts of names, from the 'extraction point's pieces' list in LogOutput.log).");

            CompassShakeFar = c.Bind(cs, "ShakeFromDistance", 25f,
                "The needle starts to shake when its target is closer than this many metres, more and faster the closer it gets.");
            CompassShakeNear = c.Bind(cs, "ShakeFullDistance", 2f,
                "At this distance or closer, the needle shakes its hardest.");
            CompassShakeMax = c.Bind(cs, "ShakeMaxDegrees", 25f, new ConfigDescription(
                "How far (degrees) the needle shakes off its direction at its hardest.", new AcceptableValueRange<float>(0f, 90f)));
            CompassTestSpawn = c.Bind(cs, "SpawnInTruckForTesting", false,
                "For testing: the host puts one of each compass in the truck at the start of every level.");

            NukeDebugLog = c.Bind(n, "DebugLog", true, "Logs heavy hits, arming, the room map and who the blast reached (lines start with 'A-Bomb:').");
        }
    }
}
