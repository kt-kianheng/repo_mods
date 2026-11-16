using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace OrbCart
{
    /// <summary>
    /// Orb C.A.R.T. — a pocketable cart with a Hauler-style hatch that only accepts enemy orbs,
    /// plus a fill-percentage readout on its handle screen.
    /// The cart is assembled at startup from the game's own Pocket C.A.R.T. and Hauler prefabs,
    /// so no AssetBundle or Unity project is needed.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "OrbCart";
        public const string Name = "OrbCart";
        public const string Version = "1.0.7";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        // ---- Fixed tuning (not configurable) ------------------------------------------------------
        internal static readonly float PriceMultiplier = 3f;     // shop price relative to the vanilla Pocket C.A.R.T.
        internal static readonly bool Decals = true;             // orb emblem on the sides and front
        internal static readonly float HatchFill = 1f;           // hatch covers the basket opening edge to edge
        internal static readonly float HatchOffset = -0.15f;     // metres from the measured rim; flush with the top of the cart
        internal static readonly bool HatchStretch = true;       // stretch the hatch to the basket's exact width and depth

        private const string DefaultTint = "B88CFFFF";

        // ---- Config ---------------------------------------------------------------------------------
        internal static ConfigEntry<float> Capacity;
        private static ConfigEntry<string> TintColor;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            BindConfig();

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            // REPOLib requires network prefabs and items to be registered from Awake.
            GameObject prefab = OrbCartBuilder.Build();
            if (prefab == null)
            {
                Log.LogError("Orb C.A.R.T. could not be built; the item will not be available.");
                return;
            }

            ItemAttributes attributes = prefab.GetComponent<ItemAttributes>();
            PrefabRef registered = REPOLib.Modules.Items.RegisterItem(attributes);
            if (registered == null)
                Log.LogError("REPOLib refused to register the Orb C.A.R.T.");
            else
                Log.LogInfo($"{Name} {Version} loaded. Orb C.A.R.T. registered (capacity ${Capacity.Value:N0}).");
        }

        private void BindConfig()
        {
            Capacity = Config.Bind("General", "Capacity", 100000f,
                new ConfigDescription(
                    "Total orb value (in $) the Orb C.A.R.T. holds before it is full. The screen shows stored value as a percentage of this. " +
                    "The host's value is used and synced to everyone; it applies to carts spawned after the change.",
                    new AcceptableValueRange<float>(1000f, 10000000f)));

            TintColor = Config.Bind("Visuals", "TintColor", DefaultTint,
                "Tint colour for the cart body as a hex colour (RRGGBB or RRGGBBAA, e.g. B88CFFFF). " +
                "Leave empty to keep the same colours as the Pocket C.A.R.T. Requires a restart.");

            RemoveOldSettings();
        }

        /// <summary>Drops settings from earlier versions (layout, price, decals...) from the config file.</summary>
        private void RemoveOldSettings()
        {
            PropertyInfo orphans = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (orphans?.GetValue(Config) is Dictionary<ConfigDefinition, string> entries && entries.Count > 0)
            {
                entries.Clear();
                Config.Save();
            }
        }

        /// <summary>The configured body tint, or false when the cart should keep the Pocket C.A.R.T.'s colours.</summary>
        internal static bool TryGetTint(out Color tint)
        {
            tint = Color.white;
            string hex = (TintColor?.Value ?? "").Trim().TrimStart('#');
            if (hex.Length == 0) return false;
            if (ColorUtility.TryParseHtmlString("#" + hex, out tint)) return true;
            Log.LogWarning($"TintColor '{TintColor.Value}' isn't a valid hex colour; keeping the Pocket C.A.R.T.'s colours.");
            return false;
        }
    }
}
