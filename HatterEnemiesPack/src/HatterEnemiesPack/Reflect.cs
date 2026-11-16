using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>Small helpers for the game's internal fields and for players.</summary>
    internal static class R
    {
        private static readonly FieldInfo Disabled = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo Dead = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static readonly FieldInfo DeathHead = AccessTools.Field(typeof(PlayerAvatar), "playerDeathHead");
        private static readonly FieldInfo PlayerName = AccessTools.Field(typeof(PlayerAvatar), "playerName");
        private static readonly FieldInfo ParentSpawned = AccessTools.Field(typeof(EnemyParent), "Spawned");
        private static readonly FieldInfo HealthCurrent = AccessTools.Field(typeof(EnemyHealth), "healthCurrent");
        private static readonly FieldInfo HealthDead = AccessTools.Field(typeof(EnemyHealth), "dead");

        internal static bool Flag(FieldInfo f, object o) => f != null && o != null && (bool)f.GetValue(o);

        /// <summary>A living, active player (not dead, not disabled).</summary>
        internal static bool Alive(PlayerAvatar p) =>
            p != null && p.gameObject.activeInHierarchy && p.playerHealth != null && !Flag(Disabled, p) && !Flag(Dead, p);

        /// <summary>Has this player died this level (their head is out)?</summary>
        internal static bool IsDead(PlayerAvatar p) => p != null && (Flag(Dead, p) || Flag(Disabled, p));

        internal static PlayerDeathHead HeadOf(PlayerAvatar p) => p != null && DeathHead != null ? DeathHead.GetValue(p) as PlayerDeathHead : null;

        internal static string Name(PlayerAvatar p)
        {
            if (p == null) return "nobody";
            try { return SemiFunc.PlayerGetName(p); }
            catch { return PlayerName != null ? PlayerName.GetValue(p) as string ?? p.name : p.name; }
        }

        internal static int ViewId(PlayerAvatar p) => p != null && p.photonView != null ? p.photonView.ViewID : -1;

        internal static PlayerAvatar PlayerByView(int id)
        {
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList()) if (ViewId(p) == id) return p;
            return null;
        }

        internal static bool Spawned(EnemyParent ep) => Flag(ParentSpawned, ep);

        internal static void SetHealth(EnemyHealth h, int hp)
        {
            if (h != null && HealthCurrent != null) HealthCurrent.SetValue(h, hp);
        }

        internal static bool EnemyDead(EnemyHealth h) => Flag(HealthDead, h);

        private static readonly FieldInfo HealthDeadImpulse = AccessTools.Field(typeof(EnemyHealth), "deadImpulse");

        /// <summary>Killed, during the game's short death freeze (before 'dead' is set).</summary>
        internal static bool EnemyDying(EnemyHealth h) => Flag(HealthDeadImpulse, h);

        internal static float GetHealth(EnemyHealth h) => h != null && HealthCurrent != null ? (int)HealthCurrent.GetValue(h) : 0f;

        private static readonly FieldInfo TumbleOf = AccessTools.Field(typeof(PlayerAvatar), "tumble");
        private static readonly FieldInfo IsTumblingF = AccessTools.Field(typeof(PlayerTumble), "isTumbling");

        /// <summary>A tumbling (ragdolled or launched) player's body, which flies off without their avatar's position.</summary>
        internal static bool Tumbling(PlayerAvatar p, out Vector3 at)
        {
            at = Vector3.zero;
            PlayerTumble t = p != null && TumbleOf != null ? TumbleOf.GetValue(p) as PlayerTumble : null;
            if (t == null || !Flag(IsTumblingF, t)) return false;
            at = t.transform.position;
            return true;
        }

        /// <summary>
        /// Where a player's eyes are: the game's own vision point (PlayerVisionTarget.VisionTransform, which drops
        /// when crouching or crawling), or the tumbling body; else 1.5 m above their feet.
        /// </summary>
        internal static Vector3 Eyes(PlayerAvatar p)
        {
            if (Tumbling(p, out Vector3 t)) return t + Vector3.up * 0.25f;
            Transform v = p.PlayerVisionTarget != null ? p.PlayerVisionTarget.VisionTransform : null;
            return v != null ? v.position : p.transform.position + Vector3.up * 1.5f;
        }

        internal static bool Multiplayer => GameManager.Multiplayer();

        internal static bool IsHost => SemiFunc.IsMasterClientOrSingleplayer();

        internal static void Info(string message)
        {
            if (Settings.DebugLog.Value) Plugin.Log.LogInfo(message);
        }
    }
}
