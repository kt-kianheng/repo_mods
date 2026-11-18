using BepInEx.Configuration;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>Every config value, in BepInEx/config/HatterCartsPack.cfg. One section per cart.</summary>
    internal static class Settings
    {
        // [Orb C.A.R.T.]
        internal static ConfigEntry<bool> OrbCartEnabled;
        internal static ConfigEntry<float> OrbCartCapacity;
        private static ConfigEntry<string> OrbCartTint;

        // [C.A.R.T. Plus]
        internal static ConfigEntry<bool> CartPlusEnabled;

        internal static void Bind(ConfigFile c)
        {
            const string oc = "Orb C.A.R.T.";
            OrbCartEnabled = c.Bind(oc, "Enabled", true, "Adds the Orb C.A.R.T. to the shop (restart to apply).");
            OrbCartCapacity = c.Bind(oc, "Capacity", 100000f, new ConfigDescription(
                "Total orb value (in $) the Orb C.A.R.T. holds before it is full. The screen shows stored value as a percentage of this. " +
                "The host's value is used and synced to everyone; it applies to carts spawned after the change.",
                new AcceptableValueRange<float>(1000f, 10000000f)));
            OrbCartTint = c.Bind(oc, "TintColor", "B88CFFFF",
                "Tint colour for the cart body as a hex colour (RRGGBB or RRGGBBAA, e.g. B88CFFFF). " +
                "Leave empty to keep the same colours as the Pocket C.A.R.T. (restart to apply).");

            const string cp = "C.A.R.T. Plus";
            CartPlusEnabled = c.Bind(cp, "Enabled", true, "Adds the C.A.R.T. Plus to the shop (restart to apply).");
        }

        /// <summary>The configured Orb C.A.R.T. body tint, or false when it should keep the Pocket C.A.R.T.'s colours.</summary>
        internal static bool TryGetOrbCartTint(out Color tint)
        {
            tint = Color.white;
            string hex = (OrbCartTint?.Value ?? "").Trim().TrimStart('#');
            if (hex.Length == 0) return false;
            if (ColorUtility.TryParseHtmlString("#" + hex, out tint)) return true;
            Plugin.Log.LogWarning($"Orb C.A.R.T. TintColor '{OrbCartTint.Value}' isn't a valid hex colour; keeping the Pocket C.A.R.T.'s colours.");
            return false;
        }
    }
}
