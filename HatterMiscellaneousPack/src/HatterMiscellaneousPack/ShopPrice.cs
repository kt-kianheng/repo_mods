using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// Shop prices. The game shows an item's price as ceil(value x ShopManager.itemValueMultiplier (4) / 1000) thousand,
    /// with value picked between the Value's min and max; health packs, upgrades and crystals then rise with the level.
    /// </summary>
    internal static class ShopPrice
    {
        private const float ShopMultiplier = 4f;   // ShopManager.itemValueMultiplier (set in its constructor)

        /// <summary>A Value that always shows as exactly <paramref name="thousands"/>K (for items that don't rise with the level).</summary>
        internal static Value Fixed(string prefabName, int thousands)
        {
            Value v = ScriptableObject.CreateInstance<Value>();
            v.name = prefabName + " Value";
            v.valueMin = v.valueMax = Mathf.Max(1, thousands) * 1000f / ShopMultiplier;
            return v;
        }
    }
}
