using BepInEx.Configuration;

namespace HatterMiscellaneousPack
{
    /// <summary>Every config value, in BepInEx/config/HatterMiscellaneousPack.cfg.</summary>
    internal static class Settings
    {
        // [Pocket Compasses]
        internal static ConfigEntry<bool> EnemyCompassEnabled, ExtractionCompassEnabled;
        internal static ConfigEntry<int> EnemyCompassPriceK, ExtractionCompassPriceK;
        internal static ConfigEntry<float> CompassScale;
        internal static ConfigEntry<float> CompassEmblemVsCompass;
        internal static ConfigEntry<float> CompassRobeYaw;
        internal static ConfigEntry<string> CompassHideRobeParts;
        internal static ConfigEntry<string> CompassExtractionParts;
        internal static ConfigEntry<float> CompassEffectSeconds;
        internal static ConfigEntry<float> CompassNoTargetGrace;
        internal static ConfigEntry<float> ArrowSizeVsText, ArrowOffsetX, ArrowOffsetY, TimerSizeVsText;
        internal static ConfigEntry<bool> CompassDebugLog;

        // [All-Seeing Monocle]
        internal static ConfigEntry<bool> MonocleEnabled;
        internal static ConfigEntry<int> MonoclePriceK, MonocleCharges;
        internal static ConfigEntry<float> MonocleSecondsPerCharge, MonocleSizeVsDrone, MonocleZoomSeconds, MonocleEspRange, MonocleLensSize;
        internal static ConfigEntry<bool> MonocleDebugLog;
        internal static ConfigEntry<string> MonocleHighlight;

        internal static void Bind(ConfigFile c)
        {
            const string mo = "All-Seeing Monocle";
            MonocleEnabled = c.Bind(mo, "Enabled", true, "Adds the All-Seeing Monocle to the shop (restart to apply).");
            MonoclePriceK = c.Bind(mo, "Price", 8, new ConfigDescription(
                "Shop price in thousands ($8K = 8). Fixed: it doesn't rise from level to level (restart to apply).", new AcceptableValueRange<int>(1, 100)));
            MonocleCharges = c.Bind(mo, "ChargesPerBattery", 10, new ConfigDescription(
                "Charges in a full battery (restart to apply).", new AcceptableValueRange<int>(1, 50)));
            MonocleSecondsPerCharge = c.Bind(mo, "SecondsPerCharge", 10f, new ConfigDescription(
                "Each charge gives this many seconds of sight. A charge is used the moment you look through it and again at the start of every further block of this length, so stopping early still costs the whole charge.", new AcceptableValueRange<float>(1f, 120f)));
            MonocleSizeVsDrone = c.Bind(mo, "SizeVsDrone", 1f, new ConfigDescription(
                "The monocle's size compared with the drone it's built on (it keeps the drone's colliders and feel) (restart to apply).", new AcceptableValueRange<float>(0.3f, 3f)));
            MonocleZoomSeconds = c.Bind(mo, "ZoomSeconds", 0.55f, new ConfigDescription(
                "How long the monocle takes to fly up to your eye (and a bit less to go back).", new AcceptableValueRange<float>(0.1f, 3f)));
            MonocleEspRange = c.Bind(mo, "SightRangeMetres", 10f, new ConfigDescription(
                "Valuables within this many metres are shown through walls.", new AcceptableValueRange<float>(5f, 500f)));
            MonocleLensSize = c.Bind(mo, "LensSize", 0.95f, new ConfigDescription(
                "The lens's diameter on screen as a fraction of the screen's diagonal (1 = the rim just touches the corners; smaller shows more of the gold rim).", new AcceptableValueRange<float>(0.5f, 1.2f)));
            MonocleHighlight = c.Bind(mo, "HighlightStyle", "Discover", new ConfigDescription(
                "Which of the game's valuable highlights to use: Discover (the yellow one you see when you find a valuable) or Reminder.",
                new AcceptableValueList<string>("Discover", "Reminder")));
            MonocleDebugLog = c.Bind(mo, "DebugLog", true, "Logs the cosmetic's pieces, uses, charges spent and why it switched off (LogOutput.log).");

            const string pc = "Pocket Compasses";
            EnemyCompassEnabled = c.Bind(pc, "EnemyCompassEnabled", true,
                "Adds the Pocket Enemy Compass to the shop: break it to get an arrow on your screen pointing to the nearest enemy (restart to apply).");
            ExtractionCompassEnabled = c.Bind(pc, "ExtractionCompassEnabled", true,
                "Adds the Pocket Extraction Compass to the shop: break it to get an arrow on your screen pointing to the nearest unfinished extraction point (restart to apply).");
            EnemyCompassPriceK = c.Bind(pc, "EnemyCompassPrice", 5, new ConfigDescription(
                "Shop price of the Pocket Enemy Compass, in thousands ($5K = 5). Fixed: it doesn't rise from level to level (restart to apply).", new AcceptableValueRange<int>(1, 100)));
            ExtractionCompassPriceK = c.Bind(pc, "ExtractionCompassPrice", 2, new ConfigDescription(
                "Shop price of the Pocket Extraction Compass, in thousands ($2K = 2). Fixed: it doesn't rise from level to level (restart to apply).", new AcceptableValueRange<int>(1, 100)));
            CompassScale = c.Bind(pc, "ItemScale", 1.733f, new ConfigDescription(
                "Size of the compass model. 1.733 is a third of the compass valuables' default size (5.2) (restart to apply).", new AcceptableValueRange<float>(0.5f, 8f)));
            CompassEmblemVsCompass = c.Bind(pc, "EmblemVsCompass", 0.5f, new ConfigDescription(
                "Height of the emblem on top (the little Robe or extraction screen) as a fraction of the compass frame's height (restart to apply).", new AcceptableValueRange<float>(0.1f, 2f)));
            CompassRobeYaw = c.Bind(pc, "RobeYaw", 0f, new ConfigDescription(
                "Turns the Pocket Enemy Compass's little Robe, in degrees, in case it faces the wrong way.", new AcceptableValueRange<float>(-180f, 180f)));
            CompassHideRobeParts = c.Bind(pc, "HideRobeParts", "",
                "Comma-separated names (or parts of names) of Robe pieces to hide on the Pocket Enemy Compass, if an odd piece shows up (the names are listed in LogOutput.log).");
            CompassExtractionParts = c.Bind(pc, "ExtractionScreenParts", "",
                "The Pocket Extraction Compass copies the level's real extraction screen. If the wrong pieces show up, list the right ones here (comma-separated names or parts of names, from the 'extraction point's pieces' list in LogOutput.log).");
            CompassEffectSeconds = c.Bind(pc, "ArrowSeconds", 300f, new ConfigDescription(
                "How long the arrow lasts after breaking a compass, in seconds (300 = 5 minutes). It ends sooner when there's nothing left to point to.", new AcceptableValueRange<float>(5f, 3600f)));
            CompassNoTargetGrace = c.Bind(pc, "NoTargetGraceSeconds", 3f, new ConfigDescription(
                "The arrow ends once there has been nothing to point to for this long (so it doesn't vanish the moment an enemy dies while another is about to appear).", new AcceptableValueRange<float>(0f, 60f)));
            ArrowSizeVsText = c.Bind(pc, "ArrowSize", 0.8f, new ConfigDescription(
                "The arrow's size as a multiple of the height of the haul text above it.", new AcceptableValueRange<float>(0.3f, 6f)));
            TimerSizeVsText = c.Bind(pc, "TimerSize", 0.45f, new ConfigDescription(
                "The countdown under each arrow: its text height as a multiple of the haul text's height.", new AcceptableValueRange<float>(0.1f, 2f)));
            ArrowOffsetX = c.Bind(pc, "ArrowOffsetX", 0f,
                "Moves the arrow sideways, in multiples of its own size (+ = right).");
            ArrowOffsetY = c.Bind(pc, "ArrowOffsetY", 0f,
                "Moves the arrow up or down, in multiples of its own size (+ = up).");
            CompassDebugLog = c.Bind(pc, "DebugLog", true,
                "Logs uses, rejections, where the arrow is anchored and why it ended (LogOutput.log).");
        }
    }
}
