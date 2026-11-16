using BepInEx.Configuration;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>A fixed value that used to be a config option (same .Value as a ConfigEntry, so the code reads it the same way).</summary>
    internal sealed class Fixed<T>
    {
        public readonly T Value;
        public Fixed(T value) { Value = value; }
    }

    /// <summary>
    /// The Alter Mimic's config: only Enabled, LatchForm and the spoken lines are options; everything else is fixed here
    /// (the values the options had by default).
    /// </summary>
    internal static class Settings
    {
        internal static ConfigEntry<bool> MimicEnabled;
        internal static Fixed<float> LatchDistance;
        internal static Fixed<float> HunterSpeed;
        internal static Fixed<float> LoneSeconds;
        internal static Fixed<float> TeleportMinDistance;
        internal static Fixed<float> TeleportMaxDistance;
        internal static Fixed<float> HeadSightRange, HeadNoticeSeconds, HeadJumpChance, HeadPossessWait;
        internal static Fixed<int> HeadFirstActions, HeadLaterActionsMin, HeadLaterActionsMax;
        internal static ConfigEntry<string> HeadLines;
        internal static ConfigEntry<string> SharedLines;
        internal static Fixed<float> ChargeMin;
        internal static Fixed<float> ChargeMax;
        internal static Fixed<float> HopUpward;
        internal static Fixed<float> HopUpwardPerCharge;
        internal static Fixed<float> HopSpeedPerCharge;
        internal static Fixed<int> ExplosionDamage;
        internal static ConfigEntry<string> CountdownWords;
        internal static Fixed<float> CountdownGap;
        internal static Fixed<float> ArmSeconds;
        internal static Fixed<float> ShockwaveForce, ShockwaveRadiusCart, ShockwaveRadiusExtraction, ShockwavePulseSeconds;
        internal static Fixed<float> ExplosionRadius;
        // semibot form
        internal static Fixed<float> BotChance;
        internal static Fixed<int> BotHealth;
        internal static Fixed<float> BotGreetDistance;
        internal static Fixed<float> BotGreetIdleMin, BotGreetIdleMax;
        internal static ConfigEntry<string> BotGreetLines;
        internal static Fixed<float> BotLookAroundMin, BotLookAroundMax;
        internal static ConfigEntry<string> BotWarnLines;
        internal static ConfigEntry<string> BotHelpLines;
        internal static Fixed<float> BotRoamSeconds;
        internal static Fixed<float> BotRoamRadius;
        internal static Fixed<float> BotSprintChance;
        internal static Fixed<float> BotSearchMin, BotSearchMax;
        internal static Fixed<float> BotCarryMin, BotCarryMax;
        internal static Fixed<int> BotShots;
        internal static Fixed<float> BotChaseRange, BotChaseGap;
        internal static Fixed<float> BotShotGapMin, BotShotGapMax;
        internal static Fixed<int> BotShotDamage;
        internal static Fixed<float> BotSpreadDegrees;
        internal static ConfigEntry<string> DebugForceForm;
        internal static Fixed<bool> DebugSolo;
        internal static Fixed<KeyboardShortcut> DebugSpawnKey;
        internal static Fixed<bool> DebugLog;
        internal static Fixed<bool> DebugSemibotAnimTest;
        internal static Fixed<KeyboardShortcut> DebugSemibotAnimNextKey;

        internal static void Bind(ConfigFile cfg)
        {
            const string M = "Alter Mimic";
            MimicEnabled = cfg.Bind(M, "Enabled", true, "Add the Alter Mimic (a tier 2 enemy; never in singleplayer) to the game.");
            HunterSpeed = new Fixed<float>(2f);
            LatchDistance = new Fixed<float>(5f);
            LoneSeconds = new Fixed<float>(3f);
            TeleportMinDistance = new Fixed<float>(4f);
            TeleportMaxDistance = new Fixed<float>(12f);
            HeadSightRange = new Fixed<float>(25f);
            HeadNoticeSeconds = new Fixed<float>(4f);
            HeadFirstActions = new Fixed<int>(2);
            HeadPossessWait = new Fixed<float>(10f);
            HeadLaterActionsMin = new Fixed<int>(1);
            HeadLaterActionsMax = new Fixed<int>(2);
            HeadJumpChance = new Fixed<float>(0.25f);
            HeadLines = cfg.Bind("Alter Mimic Lines", "HeadLines", "Can someone revive me|Im over here|Please dont forget me|Somebody come get me|Is anyone coming for me|Help me please|Revive",
                "Head form: what it says (text-to-speech, in the latched player's voice). Separate lines with |.");
            SharedLines = cfg.Bind("Alter Mimic Lines", "OneWordLines", "Help|Hello|Hihihihi|Hey|Guys|Here|Hurry|Hellooo|Heyyy|Huh|Behind|Sorry",
                "Head form: short lines the head says, mixed in with HeadLines. Separate lines with |.");
            ChargeMin = new Fixed<float>(0.3f);
            ChargeMax = new Fixed<float>(1.2f);
            HopUpward = new Fixed<float>(2.5f);
            HopUpwardPerCharge = new Fixed<float>(2f);
            HopSpeedPerCharge = new Fixed<float>(3f);
            ExplosionDamage = new Fixed<int>(100);
            CountdownWords = cfg.Bind("Alter Mimic Lines", "CountdownWords", "5|4|3|2|1",
                "Head form: once armed, a player grabbing it starts a countdown: each of these is said (text-to-speech) 1 second apart, then it explodes. Separate with |.");
            CountdownGap = new Fixed<float>(1f);
            ArmSeconds = new Fixed<float>(3f);
            ShockwavePulseSeconds = new Fixed<float>(3f);
            ShockwaveForce = new Fixed<float>(20f);
            ShockwaveRadiusCart = new Fixed<float>(2f);
            ShockwaveRadiusExtraction = new Fixed<float>(5f);
            ExplosionRadius = new Fixed<float>(5f);

            const string S = "Alter Mimic Lines";
            BotChance = new Fixed<float>(0.5f);
            BotHealth = new Fixed<int>(100);
            BotGreetDistance = new Fixed<float>(1f);
            BotGreetIdleMin = new Fixed<float>(3f);
            BotGreetIdleMax = new Fixed<float>(5f);
            BotGreetLines = cfg.Bind(S, "GreetLines", "Can you hear me|You cant hear me?|Hello test|Hihihi|Test",
                "Semibot form: its first line (text-to-speech in the latched player's voice), one picked at random. Separate lines with |.");
            BotLookAroundMin = new Fixed<float>(5f);
            BotLookAroundMax = new Fixed<float>(7f);
            BotWarnLines = cfg.Bind(S, "WarnLines", "I think the mimic got me|I think theres a mimic|Theres a mimic, find the dead head|Mimic nearby",
                "Semibot form: its second line, one picked at random. Separate lines with |.");
            BotHelpLines = cfg.Bind(S, "HelpLines", "Help me look for the mimic and kill it|Help me find it|Help find it|Shoot the head if you see it|Please help find it",
                "Semibot form: its third line, said right after the second one finishes, before it starts roaming; one picked at random. Separate lines with |.");
            BotRoamSeconds = new Fixed<float>(30f);
            BotRoamRadius = new Fixed<float>(7f);
            BotSprintChance = new Fixed<float>(0.25f);
            BotSearchMin = new Fixed<float>(3f);
            BotSearchMax = new Fixed<float>(6f);
            BotCarryMin = new Fixed<float>(5f);
            BotCarryMax = new Fixed<float>(10f);
            BotChaseRange = new Fixed<float>(10f);
            BotChaseGap = new Fixed<float>(2f);
            BotShots = new Fixed<int>(4);
            BotShotGapMin = new Fixed<float>(6f);
            BotShotGapMax = new Fixed<float>(8f);
            BotShotDamage = new Fixed<int>(0);
            BotSpreadDegrees = new Fixed<float>(3f);

            DebugForceForm = cfg.Bind(M, "LatchForm", "Random",
                new ConfigDescription("Which form the Alter Mimic takes after latching on to a player: their dead head, their walking semibot, or Random (half and half).", new AcceptableValueList<string>("Random", "Head", "Semibot")));
            DebugSolo = new Fixed<bool>(false);
            DebugSpawnKey = new Fixed<KeyboardShortcut>(KeyboardShortcut.Empty);   // the test spawn key is off
            DebugSemibotAnimTest = new Fixed<bool>(false);
            DebugSemibotAnimNextKey = new Fixed<KeyboardShortcut>(new KeyboardShortcut(KeyCode.F8));
            DebugLog = new Fixed<bool>(true);
        }
    }
}
