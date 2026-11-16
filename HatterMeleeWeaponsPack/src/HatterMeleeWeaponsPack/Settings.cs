using BepInEx.Configuration;

namespace HatterMeleeWeaponsPack
{
    /// <summary>Every setting, in BepInEx\config\HatterMeleeWeaponsPack.cfg. Saved values win over these defaults.</summary>
    internal static class Settings
    {
        // ---- Nunchuck ----
        internal static ConfigEntry<float> NunchuckPriceMultiplier;
        internal static ConfigEntry<float> NunchuckSpeedLow;
        internal static ConfigEntry<float> NunchuckSpeedHigh;
        internal static ConfigEntry<float> NunchuckReach;
        internal static ConfigEntry<int> NunchuckMaxDamage;
        internal static ConfigEntry<int> NunchuckMinDamage;
        internal static ConfigEntry<float> NunchuckKnockback;
        internal static ConfigEntry<float> NunchuckStunSpeed;
        internal static ConfigEntry<float> NunchuckStunTime;
        internal static ConfigEntry<float> NunchuckHitCooldown;
        internal static ConfigEntry<bool> NunchuckFriendlyFire;
        internal static ConfigEntry<float> NunchuckFreeHandleMass;
        internal static ConfigEntry<float> NunchuckChainPull;
        internal static ConfigEntry<bool> NunchuckDebugLog;

        // ---- Scythe ----
        internal static ConfigEntry<float> ScythePriceMultiplier;
        internal static ConfigEntry<int> ScytheDamage;
        internal static ConfigEntry<float> ScytheSpeedLow;
        internal static ConfigEntry<int> ScytheUses;
        internal static ConfigEntry<float> ScytheHookPull;
        internal static ConfigEntry<float> ScytheHitCooldown;
        internal static ConfigEntry<bool> ScytheFriendlyFire;
        internal static ConfigEntry<float> ScytheBladeRoll;
        internal static ConfigEntry<float> ScytheBalance;
        internal static ConfigEntry<bool> ScytheDebugLog;

        // ---- Katana ----
        internal static ConfigEntry<float> KatanaPriceVsScythe;
        internal static ConfigEntry<int> KatanaBladeDamage;
        internal static ConfigEntry<int> KatanaEnergy;
        internal static ConfigEntry<int> KatanaWaveEnergy;
        internal static ConfigEntry<float> KatanaWaveCooldown;
        internal static ConfigEntry<float> KatanaRiftMinLength;
        internal static ConfigEntry<float> KatanaRiftMaxLength;
        internal static ConfigEntry<float> KatanaRiftDrawSpeed;
        internal static ConfigEntry<float> KatanaRiftDuration;
        internal static ConfigEntry<int> KatanaChainLength;
        internal static ConfigEntry<float> KatanaChainWindowStart;
        internal static ConfigEntry<float> KatanaChainWindowEnd;
        internal static ConfigEntry<int> KatanaWaveDamage;
        internal static ConfigEntry<float> KatanaWaveSpeed;
        internal static ConfigEntry<float> KatanaWaveLifetime;
        internal static ConfigEntry<float> KatanaWaveSpanPerMetre;
        internal static ConfigEntry<float> KatanaWaveSpanMin;
        internal static ConfigEntry<float> KatanaWaveSpanMax;
        internal static ConfigEntry<float> KatanaWaveGrowth;
        internal static ConfigEntry<float> KatanaWaveThickness;
        internal static ConfigEntry<float> KatanaWaveHeight;
        internal static ConfigEntry<float> KatanaWavePush;
        internal static ConfigEntry<bool> KatanaWaveStopsAtWalls;
        internal static ConfigEntry<bool> KatanaWaveFriendlyFire;
        internal static ConfigEntry<float> KatanaEdgeRoll;
        internal static ConfigEntry<bool> KatanaDebugLog;

        internal static void Bind(ConfigFile cfg)
        {
            const string N = "Nunchuck";
            NunchuckPriceMultiplier = cfg.Bind(N, "PriceMultiplier", 1f,
                new ConfigDescription("Shop price as a multiple of the vanilla baseball bat's.", new AcceptableValueRange<float>(0.1f, 20f)));
            NunchuckSpeedLow = cfg.Bind(N, "SpeedForMinDamage", 8f,
                new ConfigDescription("Hit speed (m/s) that does MinDamage. Slower hits don't count. " +
                                      "Damage climbs on a logarithmic curve from here to MaxDamage at SpeedForMaxDamage: quick gains at first, " +
                                      "then ever harder to add more: damage = MinDamage + (MaxDamage - MinDamage) x ln(speed / low) / ln(high / low).",
                    new AcceptableValueRange<float>(0.5f, 100f)));
            NunchuckSpeedHigh = cfg.Bind(N, "SpeedForMaxDamage", 30f,
                new ConfigDescription("Hit speed (m/s) that reaches MaxDamage (faster hits don't do more).", new AcceptableValueRange<float>(1f, 200f)));
            NunchuckReach = cfg.Bind(N, "HitReach", 0.10f,
                new ConfigDescription("Enemies (and training dummies) this close to the free handle, in metres, count as hit even if it only grazes past.",
                    new AcceptableValueRange<float>(0f, 0.5f)));
            NunchuckMaxDamage = cfg.Bind(N, "MaxDamage", 200,
                new ConfigDescription("Damage of a hit at SpeedForMaxDamage, and the most a single hit can do.", new AcceptableValueRange<int>(1, 5000)));
            NunchuckMinDamage = cfg.Bind(N, "MinDamage", 80,
                new ConfigDescription("Damage of a hit at SpeedForMinDamage. Weaker hits don't count at all: no damage, no push, and no charge (durability) used. " +
                                      "Only hits that count use charge, the same amount as a bat hit.", new AcceptableValueRange<int>(0, 5000)));
            NunchuckKnockback = cfg.Bind(N, "KnockbackPerSpeed", 0.6f,
                new ConfigDescription("Extra push on an enemy for every metre per second of the hit (on top of the physical impact).", new AcceptableValueRange<float>(0f, 20f)));
            NunchuckStunSpeed = cfg.Bind(N, "StunSpeed", 16f,
                new ConfigDescription("Hits at least this fast (m/s) also stun the enemy (0 = never stun).", new AcceptableValueRange<float>(0f, 100f)));
            NunchuckStunTime = cfg.Bind(N, "StunTime", 1.5f,
                new ConfigDescription("Seconds a fast hit stuns an enemy.", new AcceptableValueRange<float>(0f, 10f)));
            NunchuckHitCooldown = cfg.Bind(N, "HitCooldown", 0.35f,
                new ConfigDescription("Seconds before the same enemy or player can be hit again (stops one hit counting several times as it bounces).",
                    new AcceptableValueRange<float>(0.05f, 5f)));
            NunchuckFriendlyFire = cfg.Bind(N, "FriendlyFire", true, "The free handle hurts other players it hits (never the one holding it).");
            NunchuckFreeHandleMass = cfg.Bind(N, "FreeHandleMass", 0.35f,
                new ConfigDescription("Weight of the free handle, as a fraction of the held handle's. Heavier hits harder physically but swings more sluggishly.",
                    new AcceptableValueRange<float>(0.05f, 3f)));
            NunchuckChainPull = cfg.Bind(N, "ChainPullOnHand", 0.2f,
                new ConfigDescription("How much the swinging free handle tugs on the handle you hold (1 = full physics, 0.2 = a fifth). " +
                                      "Lower makes it easier to lift and aim; higher feels heavier.", new AcceptableValueRange<float>(0.02f, 1f)));
            NunchuckDebugLog = cfg.Bind(N, "DebugLog", true, "Write every hit (speed and damage) to LogOutput.log, to help tune the numbers. Turn off later.");

            const string S = "Scythe";
            ScythePriceMultiplier = cfg.Bind(S, "PriceMultiplier", 1f,
                new ConfigDescription("Shop price as a multiple of the vanilla baseball bat's.", new AcceptableValueRange<float>(0.1f, 20f)));
            ScytheDamage = cfg.Bind(S, "Damage", 150,
                new ConfigDescription("Damage of every cut (the same however fast you pull).", new AcceptableValueRange<int>(0, 5000)));
            ScytheSpeedLow = cfg.Bind(S, "MinPullSpeed", 2f,
                new ConfigDescription("How fast (m/s) the blade must be pulled towards you, through the enemy, for it to count as a cut.",
                    new AcceptableValueRange<float>(0.1f, 50f)));
            ScytheUses = cfg.Bind(S, "Uses", 12,
                new ConfigDescription("Cuts per full charge: each cut that counts uses one battery bar (restart to apply).", new AcceptableValueRange<int>(1, 30)));
            ScytheHookPull = cfg.Bind(S, "HookPull", 1.5f,
                new ConfigDescription("How hard a cut drags the enemy towards you, per m/s of the pull (0 = not at all).", new AcceptableValueRange<float>(0f, 20f)));
            ScytheHitCooldown = cfg.Bind(S, "HitCooldown", 0.5f,
                new ConfigDescription("Seconds before the same enemy or player can be cut again.", new AcceptableValueRange<float>(0.05f, 5f)));
            ScytheFriendlyFire = cfg.Bind(S, "FriendlyFire", true, "Cutting through another player hurts them (never the one holding it).");
            ScytheBladeRoll = cfg.Bind(S, "BladeRoll", 0f,
                new ConfigDescription("Extra turn of the blade round the shaft, in degrees (restart to apply). With 0 the blade swings out to the side " +
                                      "of your last sideways swing; change this only if it ends up pointing up or down instead.", new AcceptableValueRange<float>(-180f, 180f)));
            ScytheBalance = cfg.Bind(S, "BalancePoint", 0.5f,
                new ConfigDescription("Where the scythe balances, in metres from the grip end. Nearer the grip makes the long shaft easier to hold level.",
                    new AcceptableValueRange<float>(0f, 2.0f)));
            ScytheDebugLog = cfg.Bind(S, "DebugLog", true, "Write every cut (pull speed and damage) to LogOutput.log.");

            const string K = "Katana";
            KatanaPriceVsScythe = cfg.Bind(K, "PriceVsScythe", 2f,
                new ConfigDescription("Shop price as a multiple of the Scythe's (2 = twice the Scythe).", new AcceptableValueRange<float>(0.1f, 20f)));
            KatanaBladeDamage = cfg.Bind(K, "BladeDamage", 120,
                new ConfigDescription("Damage of every hit with the blade itself, including the scroll-out swing (the vanilla sword does 50).",
                    new AcceptableValueRange<int>(0, 5000)));
            KatanaEnergy = cfg.Bind(K, "Energy", 24,
                new ConfigDescription("Energy (battery bars) of a full charge (restart to apply).", new AcceptableValueRange<int>(1, 50)));
            KatanaWaveEnergy = cfg.Bind(K, "WaveEnergy", 2,
                new ConfigDescription("Energy (bars) a charge's first wave uses; the rest of its chain is free. The katana won't charge (E) without enough.",
                    new AcceptableValueRange<int>(0, 50)));
            KatanaWaveCooldown = cfg.Bind(K, "WaveCooldown", 2f,
                new ConfigDescription("Seconds after a chain's last wave before the katana can be charged (E) again.", new AcceptableValueRange<float>(0f, 30f)));
            KatanaRiftMinLength = cfg.Bind(K, "RiftMinLength", 0.5f,
                new ConfigDescription("While charged, a stroke of the blade draws a rift in the air. A rift shorter than this (metres) when the stroke " +
                                      "ends fizzles out: no wave, and the katana stays charged.", new AcceptableValueRange<float>(0.05f, 5f)));
            KatanaRiftMaxLength = cfg.Bind(K, "RiftMaxLength", 1f,
                new ConfigDescription("Longest rift (metres). A stroke that reaches this length ends there: the rift opens and the wave comes out.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            KatanaRiftDrawSpeed = cfg.Bind(K, "RiftDrawSpeed", 1.5f,
                new ConfigDescription("How fast (m/s, round you) the blade must move to start drawing a rift; the stroke ends when it slows to " +
                                      "half of this. Slower moves, like aiming, draw nothing.", new AcceptableValueRange<float>(0.1f, 20f)));
            KatanaRiftDuration = cfg.Bind(K, "RiftDuration", 2f,
                new ConfigDescription("Seconds an opened rift hangs in the air, building up, before its wave launches out of it (then it vanishes). " +
                                      "Each rift times itself, so a chain's rifts launch one after another.",
                    new AcceptableValueRange<float>(0.3f, 10f)));
            KatanaChainLength = cfg.Bind(K, "ChainLength", 3,
                new ConfigDescription("Rifts one charge can make in a row, each with its own wave. Only the first uses energy.",
                    new AcceptableValueRange<int>(1, 10)));
            KatanaChainWindowStart = cfg.Bind(K, "ChainWindowStart", 0.1f,
                new ConfigDescription("After a rift opens, the next stroke counts from this many seconds later...", new AcceptableValueRange<float>(0f, 5f)));
            KatanaChainWindowEnd = cfg.Bind(K, "ChainWindowEnd", 1f,
                new ConfigDescription("...and must start within this many seconds, or the katana stops glowing (uncharged) and the cooldown starts.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            KatanaWaveDamage = cfg.Bind(K, "WaveDamage", 80,
                new ConfigDescription("Damage of the wave to each enemy it passes through (once per enemy per wave).", new AcceptableValueRange<int>(0, 5000)));
            KatanaWaveSpeed = cfg.Bind(K, "WaveTravelSpeed", 20f,
                new ConfigDescription("How fast the wave travels (m/s).", new AcceptableValueRange<float>(0.5f, 100f)));
            KatanaWaveLifetime = cfg.Bind(K, "WaveLifetime", 2f,
                new ConfigDescription("How long the wave travels (seconds).", new AcceptableValueRange<float>(0.1f, 10f)));
            KatanaWaveSpanPerMetre = cfg.Bind(K, "WaveWidthPerRiftMetre", 6f,
                new ConfigDescription("The wave's width when it leaves (tip to tip) for every metre of its rift's length (so 3 m for a 0.5 m rift, " +
                                      "6 m for a 1 m one), within WaveSpanMin and WaveSpanMax.", new AcceptableValueRange<float>(0.1f, 30f)));
            KatanaWaveGrowth = cfg.Bind(K, "WaveGrowth", 2f,
                new ConfigDescription("How many times wider the wave is at the end of its flight than when it left (it widens steadily as it flies).",
                    new AcceptableValueRange<float>(1f, 10f)));
            KatanaWaveSpanMin = cfg.Bind(K, "WaveSpanMin", 2f,
                new ConfigDescription("Narrowest wave (metres, tip to tip).", new AcceptableValueRange<float>(0.2f, 30f)));
            KatanaWaveSpanMax = cfg.Bind(K, "WaveSpanMax", 8f,
                new ConfigDescription("Widest wave (metres, tip to tip).", new AcceptableValueRange<float>(0.2f, 30f)));
            KatanaWaveThickness = cfg.Bind(K, "WaveThickness", 1f,
                new ConfigDescription("Depth of the crescent through its middle, front to back (metres); it tapers to a point at each tip.",
                    new AcceptableValueRange<float>(0.1f, 5f)));
            KatanaWaveHeight = cfg.Bind(K, "WaveHeight", 0.5f,
                new ConfigDescription("Height of the glowing band along the crescent's cutting edge (metres), which is what you see from behind.",
                    new AcceptableValueRange<float>(0.05f, 3f)));
            KatanaWavePush = cfg.Bind(K, "WavePush", 3f,
                new ConfigDescription("How hard the wave shoves the enemies it hits (0 = not at all).", new AcceptableValueRange<float>(0f, 50f)));
            KatanaWaveStopsAtWalls = cfg.Bind(K, "WaveStopsAtWalls", true, "The wave breaks on walls, floors and doors. Off: it passes through everything.");
            KatanaWaveFriendlyFire = cfg.Bind(K, "WaveFriendlyFire", false,
                "The wave hurts other players it passes through (never the one who slashed). Off by default, like the vanilla sword outside the arena.");
            KatanaEdgeRoll = cfg.Bind(K, "EdgeRoll", 0f,
                new ConfigDescription("Extra turn of the katana model round its own length, in degrees (restart to apply). " +
                                      "Set 180 if the sharp edge trails behind your swings instead of leading.", new AcceptableValueRange<float>(-180f, 180f)));
            KatanaDebugLog = cfg.Bind(K, "DebugLog", true, "Write every slash and wave hit to LogOutput.log.");
        }
    }
}
