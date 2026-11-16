using UnityEngine;

namespace TargetDummy
{
    /// <summary>
    /// For other mods: treat the Ally Dummy as a teammate. No reference to this DLL needed: look the type up by name,
    /// e.g. System.Type.GetType("TargetDummy.AllyApi, TargetDummy"), and call these through reflection. Everything is
    /// safe to call when there's no Ally Dummy (Exists is false, Chest is null, Heal/Hurt do nothing).
    /// Heal and Hurt only act on the host (or offline); the host then tells every player.
    /// </summary>
    public static class AllyApi
    {
        /// <summary>Whether there's an Ally Dummy in the current level.</summary>
        public static bool Exists => Dummy.Ally != null;

        /// <summary>The point to aim heals at (moves with the dummy as it rocks), or null.</summary>
        public static Transform Chest => Dummy.Ally != null ? Dummy.Ally.Chest : null;

        /// <summary>The dummy's root object (its hitboxes are children of it), or null.</summary>
        public static Transform Root => Dummy.Ally != null ? Dummy.Ally.transform : null;

        public static int Health => Dummy.Ally != null ? Dummy.Ally.Health : 0;
        public static int MaxHealth => Dummy.Ally != null ? Dummy.Ally.MaxHealth : 0;
        public static bool IsFullHealth => Dummy.Ally != null && Dummy.Ally.Health >= Dummy.Ally.MaxHealth;

        /// <summary>Whether this collider belongs to the Ally Dummy (e.g. to let line-of-sight checks through it).</summary>
        public static bool IsAllyCollider(Collider c) => Dummy.Ally != null && c != null && c.transform.IsChildOf(Dummy.Ally.transform);

        /// <summary>Host: heal the dummy, like PlayerHealth.HealOther. from = where the heal came from (for nothing but direction).</summary>
        public static void Heal(int amount, Vector3 from)
        {
            if (Dummy.Ally != null) Dummy.Ally.HostHeal(amount, from);
        }

        /// <summary>Host: hurt the dummy, like PlayerHealth.HurtOther. It never drops below 1 health.</summary>
        public static void Hurt(int damage, Vector3 direction)
        {
            if (Dummy.Ally != null) Dummy.Ally.TargetDummyHit(new float[] { damage, direction.x, direction.y, direction.z });
        }
    }
}
