using System;
using System.Reflection;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Optional link to the TargetDummy mod's Ally Dummy (a training droid that counts as a teammate). Found by name
    /// through reflection, so this pack doesn't need TargetDummy installed: without it every call is a no-op.
    /// </summary>
    internal static class AllyLink
    {
        private static bool looked;
        private static PropertyInfo chestProp, fullProp;
        private static MethodInfo healMethod, isAllyColliderMethod;

        private static bool Ready()
        {
            if (!looked)
            {
                looked = true;
                try
                {
                    Type t = null;
                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                        if (a.GetName().Name == "TargetDummy") { t = a.GetType("TargetDummy.AllyApi"); break; }
                    if (t != null)
                    {
                        chestProp = t.GetProperty("Chest", BindingFlags.Public | BindingFlags.Static);
                        fullProp = t.GetProperty("IsFullHealth", BindingFlags.Public | BindingFlags.Static);
                        healMethod = t.GetMethod("Heal", BindingFlags.Public | BindingFlags.Static);
                        isAllyColliderMethod = t.GetMethod("IsAllyCollider", BindingFlags.Public | BindingFlags.Static);
                        Plugin.Log.LogInfo("TargetDummy found: the Healing Wand can heal its Ally Dummy.");
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning($"TargetDummy link failed: {e.Message}"); }
            }
            return chestProp != null;
        }

        /// <summary>Where to aim heals on the Ally Dummy, or null if there isn't one.</summary>
        internal static Transform Chest
        {
            get
            {
                if (!Ready()) return null;
                try { return chestProp.GetValue(null) as Transform; } catch { return null; }
            }
        }

        internal static bool IsFullHealth
        {
            get
            {
                if (!Ready() || fullProp == null) return false;
                try { return (bool)fullProp.GetValue(null); } catch { return false; }
            }
        }

        internal static bool IsAllyCollider(Collider c)
        {
            if (!Ready() || isAllyColliderMethod == null || c == null) return false;
            try { return (bool)isAllyColliderMethod.Invoke(null, new object[] { c }); } catch { return false; }
        }

        /// <summary>Host: heal the Ally Dummy (it shows the number and tells every player).</summary>
        internal static void Heal(int amount, Vector3 from)
        {
            if (!Ready() || healMethod == null || amount <= 0) return;
            try
            {
                healMethod.Invoke(null, new object[] { amount, from });
                Plugin.Log.LogInfo($"Healing Wand: +{amount} health to the Ally Dummy.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Healing the Ally Dummy failed: {e.Message}"); }
        }
    }
}
