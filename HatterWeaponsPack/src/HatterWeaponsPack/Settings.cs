using BepInEx.Configuration;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Everything tunable, in BepInEx/config/HatterWeaponsPack.cfg (editable in r2modman's Config editor).
    /// Values are read when the game starts, so restart the game after changing them.
    /// In multiplayer every player should use the same values.
    /// </summary>
    internal static class Settings
    {
        // ---- Rocket Launcher ----
        internal static ConfigEntry<int> RocketsPerCharge;
        internal static ConfigEntry<float> RocketReloadSeconds;
        internal static ConfigEntry<float> RocketPriceMultiplier;
        internal static ConfigEntry<int> RocketContactDamage;
        internal static ConfigEntry<int> RocketSplashCloseDamage;
        internal static ConfigEntry<int> RocketSplashEdgeDamage;
        internal static ConfigEntry<float> RocketSplashRadius;
        internal static ConfigEntry<float> RocketSplashKnockback;
        internal static ConfigEntry<float> RocketRecoil;
        internal static ConfigEntry<float> RocketSpeed;
        internal static ConfigEntry<float> RocketLifetime;

        // ---- Plasma Rifle ----
        internal static ConfigEntry<int> PlasmaShotsPerCharge;
        internal static ConfigEntry<float> PlasmaTimeBetweenShots;
        internal static ConfigEntry<float> PlasmaPriceMultiplier;
        internal static ConfigEntry<int> PlasmaDamage;
        internal static ConfigEntry<float> PlasmaBoltSpeed;
        internal static ConfigEntry<float> PlasmaBoltLifetime;
        internal static ConfigEntry<float> PlasmaBodyGlow;

        // ---- Ember Wand ----
        internal static ConfigEntry<int> EmberFireballsPerCharge;
        internal static ConfigEntry<float> EmberChargeSeconds;
        internal static ConfigEntry<float> EmberReloadSeconds;
        internal static ConfigEntry<float> EmberPriceMultiplier;
        internal static ConfigEntry<int> EmberDirectDamage;
        internal static ConfigEntry<int> EmberSplashDamage;
        internal static ConfigEntry<float> EmberSplashRadius;
        internal static ConfigEntry<float> EmberKnockback;
        internal static ConfigEntry<float> EmberFireballSpeed;
        internal static ConfigEntry<float> EmberFireballLifetime;
        internal static ConfigEntry<string> EmberChargeLines;
        internal static ConfigEntry<float> EmberBurnSeconds;
        internal static ConfigEntry<float> EmberBurnTickSeconds;
        internal static ConfigEntry<int> EmberBurnDamage;
        internal static ConfigEntry<int> EmberPlayerBurnDamage;
        internal static ConfigEntry<float> EmberFireSeconds;
        internal static ConfigEntry<float> EmberFireRadius;
        internal static ConfigEntry<int> EmberValuableBreak;
        internal static ConfigEntry<float> ElementGraceSeconds;

        private const string EmberLinesDefault =
            "O ember spirit, burn!" +
            "|Flame, engulf my enemies!" +
            "|Blast my foes with thy wrath!";

        private const string FrostLinesDefault =
            "Frost! Obey me!" +
            "|Bring forth winter!" +
            "|Spirit of winter, freeze them!";

        private const string LightningLinesDefault =
            "Zeus, heed my call" +
            "|Heavens, unleash your wrath!" +
            "|Lightning strike";

        /// <summary>
        /// Bump when the default voice lines change. Config files keep their saved values, so on the first launch
        /// with a newer set the wands' VoiceLines are reset to the new defaults (once; later edits are kept).
        /// </summary>
        private const int VoiceLinesVersion = 2;

        // ---- Frost Wand ----
        internal static ConfigEntry<int> FrostCastsPerCharge;
        internal static ConfigEntry<float> FrostIcicleInterval;
        internal static ConfigEntry<float> FrostLaunchDelay;
        internal static ConfigEntry<float> FrostStunSeconds;
        internal static ConfigEntry<float> FrostReloadSeconds;
        internal static ConfigEntry<float> FrostPriceMultiplier;
        internal static ConfigEntry<int> FrostIcicleCount;
        internal static ConfigEntry<int> FrostIcicleDamage;
        internal static ConfigEntry<float> FrostSpawnDistanceMin;
        internal static ConfigEntry<float> FrostSpawnDistanceMax;
        internal static ConfigEntry<float> FrostSpawnForwardOffset;
        internal static ConfigEntry<float> FrostIcicleSpeed;
        internal static ConfigEntry<float> FrostIcicleLifetime;
        internal static ConfigEntry<string> FrostChargeLines;

        // ---- Healing Wand ----
        internal static ConfigEntry<int> HealCharges;
        internal static ConfigEntry<float> HealReloadSeconds;
        internal static ConfigEntry<float> HealPriceMultiplier;
        internal static ConfigEntry<int> HealBeamPerSecond;
        internal static ConfigEntry<float> HealBeamSeconds;
        internal static ConfigEntry<float> HealBeamRange;
        internal static ConfigEntry<int> HealAreaPerSecond;
        internal static ConfigEntry<float> HealAreaSeconds;
        internal static ConfigEntry<float> HealAreaRadius;
        internal static ConfigEntry<float> HealLookUpAngle;
        internal static ConfigEntry<string> HealChargeLines;

        // ---- Lightning Wand ----
        internal static ConfigEntry<int> LightningCasts;
        internal static ConfigEntry<float> LightningReloadSeconds;
        internal static ConfigEntry<float> LightningPriceMultiplier;
        internal static ConfigEntry<float> LightningChargeSeconds;
        internal static ConfigEntry<int> LightningChargesPerBolt;
        internal static ConfigEntry<int> LightningMaxTargets;
        internal static ConfigEntry<float> LightningChainDelay;
        internal static ConfigEntry<float> LightningChainRange;
        internal static ConfigEntry<float> LightningRange;
        internal static ConfigEntry<float> LightningStunSeconds;
        internal static ConfigEntry<int> LightningDamagePerCharge;
        internal static ConfigEntry<int> LightningMaxDamageCharges;
        internal static ConfigEntry<float> LightningBoltSeconds;
        internal static ConfigEntry<string> LightningChargeLines;
        internal static ConfigEntry<bool> LightningBoltHitsAllies;

        // ---- Ember Staff (the Ember Wand's upgrade) ----
        internal static ConfigEntry<int> StaffCasts;
        internal static ConfigEntry<float> StaffReloadSeconds;
        internal static ConfigEntry<float> StaffChargeSeconds;
        internal static ConfigEntry<int> StaffFireballs;
        internal static ConfigEntry<float> StaffVolleyGap;
        internal static ConfigEntry<float> StaffLookUpAngle;
        internal static ConfigEntry<float> StaffRingRadius;
        internal static ConfigEntry<float> StaffCastRange;
        internal static ConfigEntry<float> StaffInfernoSeconds;
        internal static ConfigEntry<int> StaffInfernoEnemyDps;
        internal static ConfigEntry<int> StaffInfernoAllyDps;
        internal static ConfigEntry<int> StaffInfernoValuableBreak;
        internal static ConfigEntry<float> StaffInfernoValuableBreakSeconds;
        internal static ConfigEntry<float> StaffInfernoAfterburnSeconds;
        internal static ConfigEntry<int> StaffInfernoBatteryBars;
        internal static ConfigEntry<float> StaffInfernoMaxHeight;
        internal static ConfigEntry<string> StaffVoiceLines;
        internal static ConfigEntry<float> StaffChargeUpSeconds;
        internal static ConfigEntry<string> StaffChargeUpLine;
        internal static ConfigEntry<string> StaffReleaseLine;

        // ---- Frost Staff (the Frost Wand's upgrade) ----
        internal static ConfigEntry<int> FrostStaffBars;
        internal static ConfigEntry<float> FrostStaffReloadSeconds;
        internal static ConfigEntry<int> FrostStaffIcicles;
        internal static ConfigEntry<int> FrostStaffIciclesAtOnce;
        internal static ConfigEntry<int> FrostStaffIcicleDamage;
        internal static ConfigEntry<int> FrostStaffWallImpactDamage;
        internal static ConfigEntry<float> FrostStaffInterval;
        internal static ConfigEntry<float> FrostStaffLaunchDelay;
        internal static ConfigEntry<float> FrostStaffLookUpAngle;
        internal static ConfigEntry<float> FrostStaffWallRange;
        internal static ConfigEntry<float> FrostStaffWallLength;
        internal static ConfigEntry<float> FrostStaffWallThickness;
        internal static ConfigEntry<float> FrostStaffWallHeight;
        internal static ConfigEntry<float> FrostStaffWallSeconds;
        internal static ConfigEntry<int> FrostStaffWallSegments;
        internal static ConfigEntry<float> FrostStaffWallHeightVariation;
        internal static ConfigEntry<float> FrostStaffWallRiseStagger;
        internal static ConfigEntry<float> FrostStaffSlowSeconds;
        internal static ConfigEntry<float> FrostStaffSlowFactor;
        internal static ConfigEntry<int> FrostStaffWallBatteryBars;
        internal static ConfigEntry<string> FrostStaffVoiceLines;
        internal static ConfigEntry<float> FrostStaffChargeUpSeconds;
        internal static ConfigEntry<string> FrostStaffChargeUpLine;
        internal static ConfigEntry<string> FrostStaffReleaseLine;

        // ---- Lightning Staff (the Lightning Wand's upgrade) ----
        internal static ConfigEntry<int> LStaffBars;
        internal static ConfigEntry<float> LStaffReloadSeconds;
        internal static ConfigEntry<float> LStaffChannel;
        internal static ConfigEntry<int> LStaffStrikes;
        internal static ConfigEntry<float> LStaffStrikeGap;
        internal static ConfigEntry<float> LStaffScreenRange;
        internal static ConfigEntry<float> LStaffScreenAngle;
        internal static ConfigEntry<int> LStaffChargeDamage;
        internal static ConfigEntry<int> LStaffMaxDamageCharges;
        internal static ConfigEntry<float> LStaffLookUpAngle;
        internal static ConfigEntry<float> LStaffOrbSpeed;
        internal static ConfigEntry<float> LStaffOrbHomingRange;
        internal static ConfigEntry<float> LStaffOrbHomingTurn;
        internal static ConfigEntry<float> LStaffOrbRadius;
        internal static ConfigEntry<float> LStaffOrbSeconds;
        internal static ConfigEntry<float> LStaffOrbStrikeInterval;
        internal static ConfigEntry<float> LStaffOrbStrikeRange;
        internal static ConfigEntry<int> LStaffOrbBatteryBars;
        internal static ConfigEntry<string> LStaffVoiceLines;
        internal static ConfigEntry<float> LStaffChargeUpSeconds;
        internal static ConfigEntry<string> LStaffChargeUpLine;
        internal static ConfigEntry<string> LStaffReleaseLine;

        // ---- Evolution Crystal (turns a wand into its staff) ----
        internal static ConfigEntry<float> CrystalPriceVsWand;
        internal static ConfigEntry<float> CrystalStaffPriceVsWand;
        internal static ConfigEntry<float> CrystalRange;
        internal static ConfigEntry<int> CrystalMaxInShop;

        // [Orb Cannon]
        internal static ConfigEntry<float> OrbPriceMultiplier, OrbLoadRange, OrbCooldown, OrbLengthScale, OrbHoldAbove, OrbSpawnAhead, OrbConeSpeed;
        internal static ConfigEntry<float> OrbConeRange, OrbConeAngle, OrbConePush;
        internal static ConfigEntry<int> OrbConeDamage;
        internal static ConfigEntry<int> OrbBurstShots, OrbBurstDamage;
        internal static ConfigEntry<float> OrbBurstGap, OrbBurstSpeedVsPlasma, OrbBurstSplashRadius;
        internal static ConfigEntry<int> OrbBigDamage;
        internal static ConfigEntry<float> OrbBigSpeedVsPlasma, OrbLifetime;
        internal static ConfigEntry<bool> OrbDebugLog;
        // [Friendship Cannon]
        internal static ConfigEntry<float> FriendPriceMultiplier, FriendLoadRange, FriendLaunchSpeed, FriendBubbleRadius, FriendBubbleSeconds;
        internal static ConfigEntry<float> FriendJamRadius, FriendJamKnockback;
        internal static ConfigEntry<int> FriendBubbleDamage, FriendJamDamage;
        internal static ConfigEntry<bool> FriendDebugDummy;
        internal static ConfigEntry<BepInEx.Configuration.KeyboardShortcut> FriendDebugRespawnKey, FriendDebugJumpKey;

        internal static void Bind(ConfigFile cfg)
        {
            const string R = "Rocket Launcher";
            RocketsPerCharge = cfg.Bind(R, "RocketsPerCharge", 6,
                new ConfigDescription("Rockets per full battery charge (one battery bar each).", new AcceptableValueRange<int>(1, 20)));
            RocketReloadSeconds = cfg.Bind(R, "ReloadSeconds", 12f,
                new ConfigDescription("Seconds from firing until the next rocket is loaded.", new AcceptableValueRange<float>(0.3f, 120f)));
            RocketPriceMultiplier = cfg.Bind(R, "PriceMultiplier", 2f,
                new ConfigDescription("Shop price as a multiple of the vanilla shotgun's.", new AcceptableValueRange<float>(0.1f, 20f)));
            RocketContactDamage = cfg.Bind(R, "ContactDamage", 300,
                new ConfigDescription("Damage to the enemy or player a rocket hits directly (added to the splash for enemies).", new AcceptableValueRange<int>(0, 5000)));
            RocketSplashCloseDamage = cfg.Bind(R, "SplashCloseDamage", 300,
                new ConfigDescription("Splash damage within half the splash radius, to enemies and players alike (you included).", new AcceptableValueRange<int>(0, 5000)));
            RocketSplashEdgeDamage = cfg.Bind(R, "SplashEdgeDamage", 150,
                new ConfigDescription("Splash damage out to the full splash radius, to enemies and players alike (you included).", new AcceptableValueRange<int>(0, 5000)));
            RocketSplashRadius = cfg.Bind(R, "SplashRadius", 4f,
                new ConfigDescription("Splash radius in metres (4 = the zero-gravity orb). Close range is half of this.", new AcceptableValueRange<float>(0.5f, 15f)));
            RocketSplashKnockback = cfg.Bind(R, "SplashKnockback", 60f,
                new ConfigDescription("How hard the blast throws enemies, players and items (60 = the pulse grenade).", new AcceptableValueRange<float>(0f, 300f)));
            RocketRecoil = cfg.Bind(R, "Recoil", 40f,
                new ConfigDescription("Kick on the shooter when firing, opposite the aim (the game's magic staffs use 10).", new AcceptableValueRange<float>(0f, 200f)));
            RocketSpeed = cfg.Bind(R, "RocketSpeed", 30f,
                new ConfigDescription("Rocket speed in metres per second.", new AcceptableValueRange<float>(1f, 200f)));
            RocketLifetime = cfg.Bind(R, "RocketLifetime", 4f,
                new ConfigDescription("Seconds before a rocket that hasn't hit anything explodes in the air.", new AcceptableValueRange<float>(0.2f, 30f)));

            const string P = "Plasma Rifle";
            PlasmaShotsPerCharge = cfg.Bind(P, "ShotsPerCharge", 12,
                new ConfigDescription("Bolts per full battery charge (one battery bar each).", new AcceptableValueRange<int>(1, 30)));
            PlasmaTimeBetweenShots = cfg.Bind(P, "TimeBetweenShots", 0.25f,
                new ConfigDescription("Minimum seconds between shots (0.25 = about 4 a second).", new AcceptableValueRange<float>(0.05f, 10f)));
            PlasmaPriceMultiplier = cfg.Bind(P, "PriceMultiplier", 2f,
                new ConfigDescription("Shop price as a multiple of the vanilla shotgun's.", new AcceptableValueRange<float>(0.1f, 20f)));
            PlasmaDamage = cfg.Bind(P, "Damage", 250,
                new ConfigDescription("Damage to the enemy or player a bolt hits. No splash.", new AcceptableValueRange<int>(0, 5000)));
            PlasmaBoltSpeed = cfg.Bind(P, "BoltSpeed", 60f,
                new ConfigDescription("Bolt speed in metres per second.", new AcceptableValueRange<float>(1f, 300f)));
            PlasmaBoltLifetime = cfg.Bind(P, "BoltLifetime", 2f,
                new ConfigDescription("Seconds before a bolt that hasn't hit anything fades out.", new AcceptableValueRange<float>(0.1f, 20f)));
            PlasmaBodyGlow = cfg.Bind(P, "BodyGlow", 0.3f,
                new ConfigDescription("How much the white body glows so it stays visible in dark rooms (0 = none).", new AcceptableValueRange<float>(0f, 2f)));

            const string W = "Ember Wand";
            EmberFireballsPerCharge = cfg.Bind(W, "FireballsPerCharge", 12,
                new ConfigDescription("Fireballs per full battery charge (one battery bar each).", new AcceptableValueRange<int>(1, 30)));
            EmberChargeSeconds = cfg.Bind(W, "ChargeSeconds", 2f,
                new ConfigDescription("Seconds the fireball grows at the tip before it launches.", new AcceptableValueRange<float>(0.1f, 10f)));
            EmberReloadSeconds = cfg.Bind(W, "ReloadSeconds", 2f,
                new ConfigDescription("Seconds after a fireball launches until the wand is ready again.", new AcceptableValueRange<float>(0.2f, 60f)));
            EmberPriceMultiplier = cfg.Bind(W, "PriceMultiplier", 1f,
                new ConfigDescription("Shop price as a multiple of the vanilla gun's (the handgun; 1 = same price).", new AcceptableValueRange<float>(0.1f, 20f)));
            EmberDirectDamage = cfg.Bind(W, "DirectDamage", 120,
                new ConfigDescription("Damage to the enemy or player a fireball hits directly (they also take the splash).", new AcceptableValueRange<int>(0, 5000)));
            EmberSplashDamage = cfg.Bind(W, "SplashDamage", 30,
                new ConfigDescription("Splash damage to every enemy and player in the radius, the caster included.", new AcceptableValueRange<int>(0, 5000)));
            EmberSplashRadius = cfg.Bind(W, "SplashRadius", 0.8f,
                new ConfigDescription("Splash radius in metres.", new AcceptableValueRange<float>(0.1f, 10f)));
            EmberKnockback = cfg.Bind(W, "Knockback", 10f,
                new ConfigDescription("How hard the blast pushes (60 = the pulse grenade).", new AcceptableValueRange<float>(0f, 300f)));
            EmberFireballSpeed = cfg.Bind(W, "FireballSpeed", 15f,
                new ConfigDescription("Fireball speed in metres per second.", new AcceptableValueRange<float>(1f, 100f)));
            EmberFireballLifetime = cfg.Bind(W, "FireballLifetime", 5f,
                new ConfigDescription("Seconds before a fireball that hasn't hit anything bursts in the air.", new AcceptableValueRange<float>(0.2f, 30f)));
            EmberChargeLines = cfg.Bind(W, "VoiceLines", EmberLinesDefault,
                "What the caster says (text-to-speech) while charging. Separate several lines with | to pick one at random. Leave empty to stay silent.");
            EmberBurnSeconds = cfg.Bind(W, "BurnSeconds", 5f,
                new ConfigDescription("How long anything caught in a fireball's blast or standing in its fire keeps burning. Standing in the fire keeps restarting it.", new AcceptableValueRange<float>(0f, 60f)));
            EmberBurnTickSeconds = cfg.Bind(W, "BurnTickSeconds", 1f,
                new ConfigDescription("Seconds between burn damage ticks (the first comes one tick after catching fire).", new AcceptableValueRange<float>(0.1f, 10f)));
            EmberBurnDamage = cfg.Bind(W, "BurnDamagePerTick", 30,
                new ConfigDescription("Burn damage to an enemy each tick (30 every 1 s = 30 per second, 150 over 5 s).", new AcceptableValueRange<int>(0, 1000)));
            EmberPlayerBurnDamage = cfg.Bind(W, "PlayerBurnDamagePerTick", 10,
                new ConfigDescription("Burn damage to a player each tick, the caster included (0 = players never burn).", new AcceptableValueRange<int>(0, 1000)));
            EmberFireSeconds = cfg.Bind(W, "FireAreaSeconds", 10f,
                new ConfigDescription("How long the patch of fire a fireball leaves on the floor burns (0 = no fire). Also applies to the Ember Staff's fireballs.", new AcceptableValueRange<float>(0f, 60f)));
            EmberValuableBreak = cfg.Bind(W, "ValuableDamage", 2,
                new ConfigDescription("How hard a fireball's blast damages valuables in it, as the game's own breaks: 0 = not at all, 1 = light (1–10% of the original value, by toughness), 2 = medium (5–50%), 3 = heavy (10–100%). Also the Ember Staff's fireballs.",
                                      new AcceptableValueRange<int>(0, 3)));
            EmberFireRadius = cfg.Bind(W, "FireAreaRadius", 1.2f,
                new ConfigDescription("Radius of the patch of fire, in metres.", new AcceptableValueRange<float>(0.3f, 5f)));

            ElementGraceSeconds = cfg.Bind("Fire and Ice", "GraceSeconds", 1f,
                new ConfigDescription("Fire and ice cancel each other out (ice puts out a burn, fire thaws frozen or slowed enemies and sets them alight; with steam). Right after one takes hold, the other can't undo it for this many seconds. The ice wall ignores this.",
                                      new AcceptableValueRange<float>(0f, 10f)));

            const string F = "Frost Wand";
            FrostCastsPerCharge = cfg.Bind(F, "CastsPerCharge", 12,
                new ConfigDescription("Casts per full battery charge (one battery bar each; every cast fires all the icicles).", new AcceptableValueRange<int>(1, 30)));
            FrostReloadSeconds = cfg.Bind(F, "ReloadSeconds", 2f,
                new ConfigDescription("Seconds after the last icicle flies until the wand is ready again.", new AcceptableValueRange<float>(0.2f, 60f)));
            FrostPriceMultiplier = cfg.Bind(F, "PriceMultiplier", 1f,
                new ConfigDescription("Shop price as a multiple of the vanilla gun's (the handgun; 1 = same price).", new AcceptableValueRange<float>(0.1f, 20f)));
            FrostIcicleCount = cfg.Bind(F, "IciclesPerVolley", 4,
                new ConfigDescription("Icicles per cast. They form one after another in a ring round the tip.", new AcceptableValueRange<int>(1, 20)));
            FrostIcicleInterval = cfg.Bind(F, "IcicleInterval", 0.5f,
                new ConfigDescription("Seconds between one icicle forming and the next.", new AcceptableValueRange<float>(0.05f, 3f)));
            FrostLaunchDelay = cfg.Bind(F, "LaunchDelay", 1f,
                new ConfigDescription("Seconds after an icicle forms until it flies, angled from its spot towards the crosshair.", new AcceptableValueRange<float>(0f, 5f)));
            FrostIcicleDamage = cfg.Bind(F, "IcicleDamage", 60,
                new ConfigDescription("Damage per Frost Wand icicle to the enemy or player it hits. No splash.", new AcceptableValueRange<int>(0, 5000)));
            FrostStunSeconds = cfg.Bind(F, "StunSeconds", 2f,
                new ConfigDescription("How long an enemy hit by an icicle is frozen in place, encased in ice (Frost Wand and Frost Staff); another hit keeps it frozen longer.", new AcceptableValueRange<float>(0f, 20f)));
            FrostSpawnDistanceMin = cfg.Bind(F, "SpawnDistanceMin", 0.3f,
                new ConfigDescription("Closest an icicle forms to the wand's line, in metres (on the plane SpawnForwardOffset in front of the tip).", new AcceptableValueRange<float>(0f, 3f)));
            FrostSpawnDistanceMax = cfg.Bind(F, "SpawnDistanceMax", 0.5f,
                new ConfigDescription("Farthest an icicle forms from the wand's line, in metres.", new AcceptableValueRange<float>(0f, 3f)));
            FrostSpawnForwardOffset = cfg.Bind(F, "SpawnForwardOffset", 0.1f,
                new ConfigDescription("How far in front of the tip, in metres, the icicles form.", new AcceptableValueRange<float>(0f, 2f)));
            FrostIcicleSpeed = cfg.Bind(F, "IcicleSpeed", 25f,
                new ConfigDescription("Icicle speed in metres per second.", new AcceptableValueRange<float>(1f, 200f)));
            FrostIcicleLifetime = cfg.Bind(F, "IcicleLifetime", 3f,
                new ConfigDescription("Seconds before an icicle that hasn't hit anything shatters.", new AcceptableValueRange<float>(0.2f, 30f)));
            FrostChargeLines = cfg.Bind(F, "VoiceLines", FrostLinesDefault,
                "What the caster says (text-to-speech) while charging. Separate several lines with | to pick one at random. Leave empty to stay silent.");

            const string H = "Healing Wand";
            HealCharges = cfg.Bind(H, "CastsPerCharge", 2,
                new ConfigDescription("Heals per full battery charge (one battery bar each).", new AcceptableValueRange<int>(1, 30)));
            HealReloadSeconds = cfg.Bind(H, "ReloadSeconds", 5f,
                new ConfigDescription("Seconds after a cast until the wand can cast again.", new AcceptableValueRange<float>(0.2f, 60f)));
            HealPriceMultiplier = cfg.Bind(H, "PriceMultiplier", 1f,
                new ConfigDescription("Shop price as a multiple of the vanilla gun's (the handgun; 1 = same price).", new AcceptableValueRange<float>(0.1f, 20f)));
            HealBeamPerSecond = cfg.Bind(H, "BeamHealPerSecond", 20,
                new ConfigDescription("Beam (aimed at a player): health restored to them each second.", new AcceptableValueRange<int>(0, 500)));
            HealBeamSeconds = cfg.Bind(H, "BeamSeconds", 5f,
                new ConfigDescription("How long the beam lasts (one heal per full second).", new AcceptableValueRange<float>(1f, 30f)));
            HealBeamRange = cfg.Bind(H, "BeamRange", 15f,
                new ConfigDescription("How far away, in metres, a player can be to lock the beam on.", new AcceptableValueRange<float>(1f, 60f)));
            HealAreaPerSecond = cfg.Bind(H, "AreaHealPerSecond", 10,
                new ConfigDescription("Area (wand pointed at the sky): health restored each second to every player in the orb, the caster included.", new AcceptableValueRange<int>(0, 500)));
            HealAreaSeconds = cfg.Bind(H, "AreaSeconds", 5f,
                new ConfigDescription("How long the healing orb stays (one heal per full second).", new AcceptableValueRange<float>(1f, 30f)));
            HealAreaRadius = cfg.Bind(H, "AreaRadius", 2f,
                new ConfigDescription("Radius of the healing orb in metres.", new AcceptableValueRange<float>(0.5f, 10f)));
            HealLookUpAngle = cfg.Bind(H, "LookUpAngle", 50f,
                new ConfigDescription("How far up (degrees above level) the caster must look for the area heal (the void staff uses 50).", new AcceptableValueRange<float>(10f, 89f)));
            HealChargeLines = cfg.Bind(H, "ChargeLines", "",
                "What the caster says (text-to-speech) when casting. Separate several lines with | to pick one at random. Leave empty to stay silent.");

            const string L = "Lightning Wand";
            LightningCasts = cfg.Bind(L, "BoltsPerCharge", 12,
                new ConfigDescription("Bolts per full battery charge (one battery bar each). A bolt that hits no enemy is free.", new AcceptableValueRange<int>(1, 30)));
            LightningReloadSeconds = cfg.Bind(L, "ReloadSeconds", 3f,
                new ConfigDescription("Seconds after a bolt until the wand can cast again.", new AcceptableValueRange<float>(0.2f, 60f)));
            LightningPriceMultiplier = cfg.Bind(L, "PriceVsGun", 1f,
                new ConfigDescription("Shop price as a multiple of the vanilla gun's (the handgun; 1 = same price).", new AcceptableValueRange<float>(0.1f, 20f)));
            LightningChargeSeconds = cfg.Bind(L, "ChannelTime", 2f,
                new ConfigDescription("Channel before the bolt: a yellow glow swells at the tip (up to 10 cm radius) while sparks crackle round it. The targets are picked where you aim when it fires.", new AcceptableValueRange<float>(0.05f, 10f)));
            LightningChargesPerBolt = cfg.Bind(L, "ChargesPerBolt", 4,
                new ConfigDescription("Charges in one bolt, shared out between the enemies it chains to (the first enemy gets any left over). " +
                                      "Each charge deals ChargeDamage.", new AcceptableValueRange<int>(1, 20)));
            LightningMaxTargets = cfg.Bind(L, "MaxTargets", 4,
                new ConfigDescription("Most enemies one bolt can chain to, the first included (never more than ChargesPerBolt).", new AcceptableValueRange<int>(1, 20)));
            LightningChainDelay = cfg.Bind(L, "ChainDelay", 0.2f,
                new ConfigDescription("Seconds after an enemy is struck before the bolt jumps to the next one.", new AcceptableValueRange<float>(0f, 2f)));
            LightningChainRange = cfg.Bind(L, "ChainRange", 6f,
                new ConfigDescription("How far, in metres, the bolt can jump from one enemy to the next. It only jumps along a clear line of sight between the two (walls, doors, props, players and other enemies block it), checked again at the moment of the jump.", new AcceptableValueRange<float>(1f, 30f)));
            LightningRange = cfg.Bind(L, "Range", 20f,
                new ConfigDescription("How far away, in metres, the first enemy can be.", new AcceptableValueRange<float>(2f, 60f)));
            LightningStunSeconds = cfg.Bind(L, "StunSeconds", 4f,
                new ConfigDescription("How long each struck enemy is stunned (and electrocuted), whatever its charges.", new AcceptableValueRange<float>(0.5f, 20f)));
            LightningDamagePerCharge = cfg.Bind(L, "ChargeDamage", 50,
                new ConfigDescription("Damage per charge, dealt when the stun ends.", new AcceptableValueRange<int>(0, 1000)));
            LightningMaxDamageCharges = cfg.Bind(L, "MaxDamageCharges", 2,
                new ConfigDescription("Most charges that count towards one enemy's damage (2 = at most 2 x ChargeDamage, even for a lone enemy holding all the charges).",
                                      new AcceptableValueRange<int>(1, 20)));
            LightningBoltSeconds = cfg.Bind(L, "BoltSeconds", 0.5f,
                new ConfigDescription("How long each lightning bolt stays visible.", new AcceptableValueRange<float>(0.1f, 3f)));
            LightningChargeLines = cfg.Bind(L, "VoiceLines", LightningLinesDefault,
                "What the caster says (text-to-speech) while charging. Separate several lines with | to pick one at random. Leave empty to stay silent.");
            LightningBoltHitsAllies = cfg.Bind(L, "StraightBoltHitsAllies", true,
                "With no enemy to strike, the bolt goes straight to the crosshair (Lightning Wand, and the Lightning Staff's normal cast). If a teammate is on that line, the bolt hits them for an enemy's damage and uses the cast's battery. Never the caster.");

            const string ES = "Ember Staff";
            StaffCasts = cfg.Bind(ES, "BarsPerCharge", 24,
                new ConfigDescription("Battery bars per full charge. A volley uses one; an inferno uses InfernoBatteryBars.", new AcceptableValueRange<int>(1, 50)));
            StaffReloadSeconds = cfg.Bind(ES, "ReloadSeconds", 2f,
                new ConfigDescription("Seconds after a cast until the staff is ready again.", new AcceptableValueRange<float>(0.2f, 60f)));
            StaffChargeSeconds = cfg.Bind(ES, "ChargeSeconds", 2f,
                new ConfigDescription("Seconds the volley's fireballs grow at the head before they fly (like the Ember Wand).", new AcceptableValueRange<float>(0.1f, 10f)));
            StaffFireballs = cfg.Bind(ES, "FireballsPerVolley", 3,
                new ConfigDescription("Fireballs per volley. Each is an Ember Wand fireball (same damage and splash, set in [Ember Wand]).", new AcceptableValueRange<int>(1, 10)));
            StaffVolleyGap = cfg.Bind(ES, "VolleyGap", 0.2f,
                new ConfigDescription("Seconds between one fireball of the volley and the next.", new AcceptableValueRange<float>(0f, 2f)));
            StaffLookUpAngle = cfg.Bind(ES, "LookUpAngle", 50f,
                new ConfigDescription("How far up (degrees above level) to point the staff to charge the inferno (or, once ready, put it away again).", new AcceptableValueRange<float>(10f, 89f)));
            StaffRingRadius = cfg.Bind(ES, "InfernoRadiusMetres", 2f,
                new ConfigDescription("Radius in metres of the inferno (and of the ring that shows where it will be).", new AcceptableValueRange<float>(0.2f, 5f)));
            StaffCastRange = cfg.Bind(ES, "InfernoRange", 20f,
                new ConfigDescription("How far away, in metres, the inferno can be cast.", new AcceptableValueRange<float>(2f, 60f)));
            StaffInfernoSeconds = cfg.Bind(ES, "InfernoSeconds", 8f,
                new ConfigDescription("How long the inferno burns.", new AcceptableValueRange<float>(1f, 60f)));
            StaffInfernoEnemyDps = cfg.Bind(ES, "InfernoEnemyDamagePerSecond", 100,
                new ConfigDescription("Damage a second to enemies in the inferno.", new AcceptableValueRange<int>(0, 5000)));
            StaffInfernoAllyDps = cfg.Bind(ES, "InfernoPlayerDamagePerSecond", 50,
                new ConfigDescription("Damage a second to players in the inferno, the caster included.", new AcceptableValueRange<int>(0, 1000)));
            StaffInfernoValuableBreak = cfg.Bind(ES, "InfernoValuableDamage", 1,
                new ConfigDescription("How hard the inferno damages valuables in it, as the game's own breaks: 0 = not at all, 1 = a light break (1% of the original value for the toughest valuables, up to 10% for the most fragile), 2 = medium (5–50%), 3 = heavy (10–100%).",
                                      new AcceptableValueRange<int>(0, 3)));
            StaffInfernoValuableBreakSeconds = cfg.Bind(ES, "InfernoValuableDamageSeconds", 1f,
                new ConfigDescription("Seconds between the inferno's breaks on each valuable in it.", new AcceptableValueRange<float>(0.25f, 10f)));
            StaffInfernoAfterburnSeconds = cfg.Bind(ES, "InfernoAfterburnSeconds", 10f,
                new ConfigDescription("When the inferno dies down it leaves fire on the floor for this long (the Ember Wand's fire: it sets things alight with the [Ember Wand] burn settings). 0 = none. On the floor: one patch the inferno's size; cast on a wall: a trail of patches on the floor under the pillar.",
                                      new AcceptableValueRange<float>(0f, 60f)));
            StaffInfernoBatteryBars = cfg.Bind(ES, "InfernoBatteryBars", 3,
                new ConfigDescription("Battery bars an inferno uses.", new AcceptableValueRange<int>(1, 30)));
            StaffInfernoMaxHeight = cfg.Bind(ES, "InfernoMaxHeight", 30f,
                new ConfigDescription("Tallest the inferno gets, in metres, when there's no ceiling above it.", new AcceptableValueRange<float>(2f, 100f)));
            StaffVoiceLines = cfg.Bind(ES, "VoiceLines", EmberLinesDefault,
                "What the caster says (text-to-speech) when casting. Separate several lines with | to pick one at random. Leave empty to stay silent.");
            StaffChargeUpSeconds = cfg.Bind(ES, "ChargeUpSeconds", 4f,
                new ConfigDescription("How long to keep the staff pointed at the sky to charge the inferno (lowering it sooner cancels the charge).", new AcceptableValueRange<float>(0.1f, 20f)));
            StaffChargeUpLine = cfg.Bind(ES, "ChargeUpLine", "Ignis, arise from ash. Take form, take flame, and burn.",
                "What the caster says (text-to-speech) as they start charging the inferno. Separate several lines with | to pick one at random. Leave empty to stay silent.");
            StaffReleaseLine = cfg.Bind(ES, "ReleaseLine", "Inferno!",
                "What the caster says when casting the inferno (instead of VoiceLines). Separate several lines with | to pick one at random. Leave empty to stay silent.");

            const string FS = "Frost Staff";
            FrostStaffBars = cfg.Bind(FS, "BarsPerCharge", 24,
                new ConfigDescription("Battery bars per full charge. A volley uses one; an ice wall uses WallBatteryBars.", new AcceptableValueRange<int>(1, 50)));
            FrostStaffReloadSeconds = cfg.Bind(FS, "ReloadSeconds", 2f,
                new ConfigDescription("Seconds after the last icicle flies (or the wall goes up) until the staff is ready again.", new AcceptableValueRange<float>(0.2f, 60f)));
            FrostStaffIcicles = cfg.Bind(FS, "IciclesPerVolley", 12,
                new ConfigDescription("Icicles per volley. Each freezes what it hits like a Frost Wand icicle (StunSeconds in [Frost Wand]).", new AcceptableValueRange<int>(1, 60)));
            FrostStaffIciclesAtOnce = cfg.Bind(FS, "IciclesTogether", 1,
                new ConfigDescription("Icicles that form, and fly, together.", new AcceptableValueRange<int>(1, 12)));
            FrostStaffIcicleDamage = cfg.Bind(FS, "IcicleDamage", 80,
                new ConfigDescription("Damage per Frost Staff icicle to the enemy or player it hits. No splash.", new AcceptableValueRange<int>(0, 5000)));
            FrostStaffWallImpactDamage = cfg.Bind(FS, "WallImpactDamage", 150,
                new ConfigDescription("Damage to each enemy the ice wall rises under (then it's frozen inside the wall).", new AcceptableValueRange<int>(0, 5000)));
            FrostStaffInterval = cfg.Bind(FS, "IcicleInterval", 0.25f,
                new ConfigDescription("Seconds between one set of icicles forming and the next.", new AcceptableValueRange<float>(0.05f, 3f)));
            FrostStaffLaunchDelay = cfg.Bind(FS, "LaunchDelay", 1f,
                new ConfigDescription("Seconds after a set forms until it flies, angled towards the crosshair.", new AcceptableValueRange<float>(0f, 5f)));
            FrostStaffLookUpAngle = cfg.Bind(FS, "LookUpAngle", 50f,
                new ConfigDescription("How far up (degrees above level) to point the staff to charge the ice wall (or, once ready, put it away again).", new AcceptableValueRange<float>(10f, 89f)));
            FrostStaffWallRange = cfg.Bind(FS, "WallRange", 20f,
                new ConfigDescription("How far away, in metres, the wall can be raised.", new AcceptableValueRange<float>(2f, 60f)));
            FrostStaffWallLength = cfg.Bind(FS, "WallLength", 5f,
                new ConfigDescription("Length of the ice wall in metres (it stands across your view).", new AcceptableValueRange<float>(0.5f, 20f)));
            FrostStaffWallThickness = cfg.Bind(FS, "WallThickness", 1f,
                new ConfigDescription("Thickness of the ice wall in metres.", new AcceptableValueRange<float>(0.2f, 5f)));
            FrostStaffWallHeight = cfg.Bind(FS, "WallHeight", 3f,
                new ConfigDescription("Height of the ice wall's blocks in metres (each varies by WallHeightVariation).", new AcceptableValueRange<float>(0.5f, 10f)));
            FrostStaffWallSeconds = cfg.Bind(FS, "WallSeconds", 10f,
                new ConfigDescription("How long the wall stands before it shatters.", new AcceptableValueRange<float>(1f, 120f)));
            FrostStaffWallSegments = cfg.Bind(FS, "WallBlocks", 8,
                new ConfigDescription("Upright blocks of ice the wall is made of, side by side.", new AcceptableValueRange<int>(1, 30)));
            FrostStaffWallHeightVariation = cfg.Bind(FS, "WallHeightVariation", 0.3f,
                new ConfigDescription("Each block is WallHeight tall, give or take up to this many metres.", new AcceptableValueRange<float>(0f, 2f)));
            FrostStaffWallRiseStagger = cfg.Bind(FS, "WallRiseStagger", 0.08f,
                new ConfigDescription("Seconds between one block bursting up and the next, from your left to your right.", new AcceptableValueRange<float>(0f, 1f)));
            FrostStaffSlowSeconds = cfg.Bind(FS, "TrappedSlowSeconds", 5f,
                new ConfigDescription("Enemies the wall rose on are frozen in ice until it breaks, then slowed for this many seconds.", new AcceptableValueRange<float>(0f, 60f)));
            FrostStaffSlowFactor = cfg.Bind(FS, "TrappedSlowSpeed", 0.5f,
                new ConfigDescription("While slowed, enemies walk at this fraction of their speed (0.5 = half speed).", new AcceptableValueRange<float>(0.05f, 1f)));
            FrostStaffWallBatteryBars = cfg.Bind(FS, "WallBatteryBars", 3,
                new ConfigDescription("Battery bars an ice wall uses.", new AcceptableValueRange<int>(1, 50)));
            FrostStaffVoiceLines = cfg.Bind(FS, "VoiceLines", FrostLinesDefault,
                "What the caster says (text-to-speech) when casting. Separate several lines with | to pick one at random. Leave empty to stay silent.");
            FrostStaffChargeUpSeconds = cfg.Bind(FS, "ChargeUpSeconds", 4f,
                new ConfigDescription("How long to keep the staff pointed at the sky to charge the ice wall (lowering it sooner cancels the charge).", new AcceptableValueRange<float>(0.1f, 20f)));
            FrostStaffChargeUpLine = cfg.Bind(FS, "ChargeUpLine", "Glacies, still the air. Draw forth the cold, and let it freeze.",
                "What the caster says (text-to-speech) as they start charging the ice wall. Separate several lines with | to pick one at random. Leave empty to stay silent.");
            FrostStaffReleaseLine = cfg.Bind(FS, "ReleaseLine", "Glacier wall!",
                "What the caster says when casting the ice wall (instead of VoiceLines). Separate several lines with | to pick one at random. Leave empty to stay silent.");

            const string LS = "Lightning Staff";
            LStaffBars = cfg.Bind(LS, "BarsPerCharge", 24,
                new ConfigDescription("Battery bars per full charge. The strikes use one; an orb uses OrbBatteryBars.", new AcceptableValueRange<int>(1, 50)));
            LStaffReloadSeconds = cfg.Bind(LS, "ReloadSeconds", 3f,
                new ConfigDescription("Seconds after a cast until the staff is ready again.", new AcceptableValueRange<float>(0.2f, 60f)));
            LStaffChannel = cfg.Bind(LS, "ChannelTime", 2f,
                new ConfigDescription("Channel before the strikes (the glow swells at the crystal).", new AcceptableValueRange<float>(0.05f, 10f)));
            LStaffStrikes = cfg.Bind(LS, "Strikes", 3,
                new ConfigDescription("Lightning strikes per cast, one after another. Each chains like the Lightning Wand's bolt (its ChargesPerBolt, MaxTargets, ChainRange, ChainDelay and StunSeconds).", new AcceptableValueRange<int>(1, 10)));
            LStaffStrikeGap = cfg.Bind(LS, "SecondsBetweenStrikes", 1.5f,
                new ConfigDescription("Seconds between one strike and the next.", new AcceptableValueRange<float>(0f, 5f)));
            LStaffScreenRange = cfg.Bind(LS, "ScreenRange", 30f,
                new ConfigDescription("How far away, in metres, an enemy on your screen can be struck.", new AcceptableValueRange<float>(2f, 100f)));
            LStaffScreenAngle = cfg.Bind(LS, "ScreenAngle", 45f,
                new ConfigDescription("How far from your crosshair, in degrees, an enemy counts as on your screen.", new AcceptableValueRange<float>(5f, 90f)));
            LStaffChargeDamage = cfg.Bind(LS, "ChargeDamage", 50,
                new ConfigDescription("Damage per charge, dealt the moment a strike lands (the stun still applies).", new AcceptableValueRange<int>(0, 1000)));
            LStaffMaxDamageCharges = cfg.Bind(LS, "MaxDamageCharges", 4,
                new ConfigDescription("Most charges that count towards one enemy's damage per strike.", new AcceptableValueRange<int>(1, 20)));
            LStaffLookUpAngle = cfg.Bind(LS, "LookUpAngle", 50f,
                new ConfigDescription("How far up (degrees above level) to point the staff to charge the orb (or, once ready, put it away again).", new AcceptableValueRange<float>(10f, 89f)));
            LStaffOrbSpeed = cfg.Bind(LS, "OrbSpeed", 0.5f,
                new ConfigDescription("Speed of the lightning orb in metres per second.", new AcceptableValueRange<float>(0.2f, 30f)));
            LStaffOrbHomingRange = cfg.Bind(LS, "OrbHomingRange", 5f,
                new ConfigDescription("The orb curves gently towards the nearest enemy it can see within this many metres (0 = no homing).", new AcceptableValueRange<float>(0f, 30f)));
            LStaffOrbHomingTurn = cfg.Bind(LS, "OrbHomingTurn", 10f,
                new ConfigDescription("How fast the orb turns towards that enemy, in degrees per second (small = a very slight curve).", new AcceptableValueRange<float>(0f, 360f)));
            LStaffOrbSeconds = cfg.Bind(LS, "OrbSeconds", 30f,
                new ConfigDescription("How long the orb lasts.", new AcceptableValueRange<float>(1f, 120f)));
            LStaffOrbRadius = cfg.Bind(LS, "OrbRadius", 1f,
                new ConfigDescription("Radius of the lightning orb's glow in metres. It bounces off walls on a smaller core (a third of this, at most 0.5 m), so it still fits through doors.",
                                      new AcceptableValueRange<float>(0.1f, 5f)));
            LStaffOrbStrikeInterval = cfg.Bind(LS, "OrbSecondsBetweenStrikes", 2f,
                new ConfigDescription("Seconds between the orb's lightning strikes.", new AcceptableValueRange<float>(0.2f, 30f)));
            LStaffOrbStrikeRange = cfg.Bind(LS, "OrbStrikeRange", 10f,
                new ConfigDescription("How far, in metres, the orb can strike an enemy it can see.", new AcceptableValueRange<float>(1f, 50f)));
            LStaffOrbBatteryBars = cfg.Bind(LS, "OrbBatteryBars", 3,
                new ConfigDescription("Battery bars an orb uses.", new AcceptableValueRange<int>(1, 50)));
            LStaffVoiceLines = cfg.Bind(LS, "VoiceLines", LightningLinesDefault,
                "What the caster says (text-to-speech) when casting. Separate several lines with | to pick one at random. Leave empty to stay silent.");
            LStaffChargeUpSeconds = cfg.Bind(LS, "ChargeUpSeconds", 4f,
                new ConfigDescription("How long to keep the staff pointed at the sky to charge the lightning orb (lowering it sooner cancels the charge).", new AcceptableValueRange<float>(0.1f, 20f)));
            LStaffChargeUpLine = cfg.Bind(LS, "ChargeUpLine", "Fulmen, gather the sparks. Pierce the veil, and strike them afar.",
                "What the caster says (text-to-speech) as they start charging the lightning orb. Separate several lines with | to pick one at random. Leave empty to stay silent.");
            LStaffReleaseLine = cfg.Bind(LS, "ReleaseLine", "Lightning orb!",
                "What the caster says when casting the lightning orb (instead of VoiceLines). Separate several lines with | to pick one at random. Leave empty to stay silent.");

            const string EC = "Evolution Crystal";
            CrystalPriceVsWand = cfg.Bind(EC, "PriceVsWand", 5f,
                new ConfigDescription("Shop price of the crystal as a multiple of a wand's.", new AcceptableValueRange<float>(0.1f, 50f)));
            CrystalStaffPriceVsWand = cfg.Bind(EC, "StaffPriceVsWand", 7f,
                new ConfigDescription("Shop price of a staff made from a wand that's for sale in the shop, as a multiple of that wand's price.", new AcceptableValueRange<float>(0.1f, 50f)));
            CrystalRange = cfg.Bind(EC, "Range", 5f,
                new ConfigDescription("How close (metres from you) a wand must be for the crystal to reach it. Of those, the one nearest your crosshair evolves.", new AcceptableValueRange<float>(1f, 30f)));
            CrystalMaxInShop = cfg.Bind(EC, "MaxInShop", 1,
                new ConfigDescription("Most crystals the shop offers at once. Restart to apply.", new AcceptableValueRange<int>(0, 10)));

            const string FC = "Friendship Cannon";
            FriendPriceMultiplier = cfg.Bind(FC, "PriceVsShotgun", 0.5f,
                new ConfigDescription("Shop price as a multiple of the vanilla shotgun's.", new AcceptableValueRange<float>(0.1f, 20f)));
            FriendLoadRange = cfg.Bind(FC, "LoadRange", 1.5f,
                new ConfigDescription("How close (metres from the cannon) a tumbling player must be to load them. Same reach rule as the Orb Cannon's orbs.", new AcceptableValueRange<float>(0.2f, 10f)));
            FriendLaunchSpeed = cfg.Bind(FC, "LaunchSpeed", 50f,
                new ConfigDescription("Firing: the loaded player flies out at this speed (m/s), where the holder aims.", new AcceptableValueRange<float>(5f, 200f)));
            FriendBubbleDamage = cfg.Bind(FC, "BubbleHitDamage", 200,
                new ConfigDescription("Firing: the bubble's damage to the first enemy or player it touches (it pops on any contact). The rider is safe until it pops.", new AcceptableValueRange<int>(0, 5000)));
            FriendBubbleRadius = cfg.Bind(FC, "BubbleRadius", 0.6f,
                new ConfigDescription("Firing: the bubble's radius (metres).", new AcceptableValueRange<float>(0.2f, 3f)));
            FriendBubbleSeconds = cfg.Bind(FC, "BubbleMaxSeconds", 4f,
                new ConfigDescription("Firing: the bubble pops by itself after this long if it hasn't touched anything.", new AcceptableValueRange<float>(0.5f, 30f)));
            FriendJamDamage = cfg.Bind(FC, "MalfunctionDamage", 5,
                new ConfigDescription("Malfunction (the loaded player jumps): the small blast's damage, to players and enemies.", new AcceptableValueRange<int>(0, 500)));
            FriendJamRadius = cfg.Bind(FC, "MalfunctionRadius", 0.5f,
                new ConfigDescription("Malfunction: the blast's radius (metres).", new AcceptableValueRange<float>(0.1f, 5f)));
            FriendJamKnockback = cfg.Bind(FC, "MalfunctionKnockback", 10f,
                new ConfigDescription("Malfunction: the blast's knockback.", new AcceptableValueRange<float>(0f, 200f)));
            FriendDebugDummy = cfg.Bind(FC, "DebugDummy", false,
                "Testing only (host): a dummy 'tumbling player' (a ball with your head on it) appears 2.5 m in front of you 3 s after a level loads, for testing the Friendship Cannon alone.");
            FriendDebugRespawnKey = cfg.Bind(FC, "DebugDummyRespawnKey", new BepInEx.Configuration.KeyboardShortcut(UnityEngine.KeyCode.K),
                "Testing only: with DebugDummy on, this key brings the dummy back in front of you.");
            FriendDebugJumpKey = cfg.Bind(FC, "DebugDummyJumpKey", new BepInEx.Configuration.KeyboardShortcut(UnityEngine.KeyCode.J),
                "Testing only: with the dummy loaded, this key makes it 'jump' (the cannon malfunctions).");

            const string OC = "Orb Cannon";
            OrbPriceMultiplier = cfg.Bind(OC, "PriceVsShotgun", 0.5f,
                new ConfigDescription("Shop price as a multiple of the vanilla shotgun's.", new AcceptableValueRange<float>(0.1f, 20f)));
            OrbLoadRange = cfg.Bind(OC, "LoadRange", 1f,
                new ConfigDescription("How close (metres from the cannon) a soul orb must be to load it.", new AcceptableValueRange<float>(0.2f, 10f)));
            OrbCooldown = cfg.Bind(OC, "CooldownSeconds", 3f,
                new ConfigDescription("Seconds after firing before the cannon can load another orb.", new AcceptableValueRange<float>(0f, 60f)));
            OrbLengthScale = cfg.Bind(OC, "LengthScale", 0.75f,
                new ConfigDescription("The cannon's length compared with the model (shorter keeps it out of your view). Restart to apply.", new AcceptableValueRange<float>(0.4f, 1.5f)));
            OrbHoldAbove = cfg.Bind(OC, "HoldAbove", 0f,
                new ConfigDescription("How far above the optic the cannon is held from, in metres. Higher = the cannon sits lower in your view. Restart to apply.", new AcceptableValueRange<float>(-0.5f, 1f)));
            OrbSpawnAhead = cfg.Bind(OC, "SpawnAhead", 0.8f,
                new ConfigDescription("Fired orbs appear this far in front of the muzzle, in metres (so they can't burst on you).", new AcceptableValueRange<float>(0f, 3f)));
            OrbConeSpeed = cfg.Bind(OC, "ShockwaveSpeed", 80f,
                new ConfigDescription("Small orb: how fast the shockwave travels, in metres per second. It hits each target when it reaches it.", new AcceptableValueRange<float>(5f, 500f)));
            OrbConeRange = cfg.Bind(OC, "ShockwaveRange", 5f,
                new ConfigDescription("Small orb: how far the shockwave reaches, in metres.", new AcceptableValueRange<float>(0.5f, 30f)));
            OrbConeAngle = cfg.Bind(OC, "ShockwaveAngle", 120f,
                new ConfigDescription("Small orb: the shockwave cone's full width, in degrees.", new AcceptableValueRange<float>(5f, 360f)));
            OrbConeDamage = cfg.Bind(OC, "ShockwaveDamage", 100,
                new ConfigDescription("Small orb: damage to every enemy and player in the cone (not you).", new AcceptableValueRange<int>(0, 5000)));
            OrbConePush = cfg.Bind(OC, "ShockwaveKnockback", 90f,
                new ConfigDescription("Small orb: how hard the shockwave knocks enemies back (the game's hit force: the pulse grenade is 60).", new AcceptableValueRange<float>(0f, 500f)));
            OrbBurstShots = cfg.Bind(OC, "BurstShots", 3,
                new ConfigDescription("Medium orb: how many small orbs it fires.", new AcceptableValueRange<int>(1, 20)));
            OrbBurstGap = cfg.Bind(OC, "BurstGap", 0.2f,
                new ConfigDescription("Medium orb: seconds between the small orbs.", new AcceptableValueRange<float>(0.02f, 2f)));
            OrbBurstSpeedVsPlasma = cfg.Bind(OC, "BurstSpeedVsPlasma", 0.7f,
                new ConfigDescription("Medium orb: the small orbs' speed as a multiple of the Plasma Rifle bolt's.", new AcceptableValueRange<float>(0.05f, 5f)));
            OrbBurstDamage = cfg.Bind(OC, "MediumBurstOrbDamage", 150,
                new ConfigDescription("Medium orb: damage per small orb. A direct hit and the splash never add up: each enemy or player takes this at most once per orb.", new AcceptableValueRange<int>(0, 5000)));
            OrbBurstSplashRadius = cfg.Bind(OC, "BurstSplashRadius", 0.2f,
                new ConfigDescription("Medium orb: each small orb's splash radius in metres.", new AcceptableValueRange<float>(0f, 5f)));
            OrbBigSpeedVsPlasma = cfg.Bind(OC, "BigSpeedVsPlasma", 0.3333f,
                new ConfigDescription("Large orb: its speed as a multiple of the Plasma Rifle bolt's.", new AcceptableValueRange<float>(0.05f, 5f)));
            OrbBigDamage = cfg.Bind(OC, "LargeOrbDamage", 500,
                new ConfigDescription("Large orb: damage on a direct hit. The blast is the Rocket Launcher's (its splash settings), on top.", new AcceptableValueRange<int>(0, 5000)));
            OrbLifetime = cfg.Bind(OC, "OrbLifetime", 4f,
                new ConfigDescription("Seconds a fired orb flies before bursting in the air.", new AcceptableValueRange<float>(0.5f, 20f)));
            OrbDebugLog = cfg.Bind(OC, "DebugLog", true, "Logs loading, firing and hits (LogOutput.log, lines starting \"Orb Cannon:\").");

            // move saved voice lines to a newer default set, once per set
            ConfigEntry<int> linesVersion = cfg.Bind("_Internal", "VoiceLinesVersion", 0,
                "Used by the mod to update the wands' default voice lines. Don't edit.");
            if (linesVersion.Value < VoiceLinesVersion)
            {
                EmberChargeLines.Value = EmberLinesDefault;
                FrostChargeLines.Value = FrostLinesDefault;
                LightningChargeLines.Value = LightningLinesDefault;
                linesVersion.Value = VoiceLinesVersion;
            }
        }
    }
}
