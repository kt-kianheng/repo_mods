using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace TargetDummy
{
    /// <summary>
    /// The Ally Dummy as a pseudo-player, so other mods treat it as a teammate without knowing about this mod.
    ///
    /// It's a real PlayerAvatar, PlayerHealth, PlayerVisionTarget and PhotonView, but on a switched-off object: none
    /// of the game's player scripts ever run on it. Mods see it through the usual lookups:
    ///   SemiFunc.PlayerGetList()               includes it when a mod asks (never when the game's own code asks,
    ///                                          so trucks, spectating, enemies, the map etc. never see it)
    ///   SemiFunc.PlayerAvatarGetFromPhotonID() finds it by its network ID (the same fixed ID on every player's game)
    ///   SemiFunc.PlayerGetName()               "Ally Dummy"
    /// and use it like a player:
    ///   playerHealth.Heal / HealOther          heal the dummy's health pool
    ///   playerHealth.Hurt / HurtOther          hurt it (never below 1)
    ///   playerHealth health / maxHealth        always match the dummy's health
    ///   transform.position                     the droid's feet
    ///   PlayerVisionTarget.VisionTransform     a point on the droid's chest, on the side facing your camera
    /// Calls made on a non-host game are passed to the host, who owns the health.
    /// Mods that read GameDirector.instance.PlayerList directly won't see it, and anything else on a player
    /// (grabbing, tumbling, camera, voice) is empty here.
    /// </summary>
    [HarmonyPatch]
    internal static class PseudoPlayer
    {
        /// <summary>
        /// The dummy's network ID: fixed, the same on every player's game, and far above any real one (players get
        /// actor number x 1000 + n). Kept under 2^24 (16,777,216) so it survives being sent as a float, as mods often
        /// do (the Healing Wand packs its target's ID into a float[]); a bigger ID gets rounded and isn't found.
        /// </summary>
        internal const int ViewId = 16700001;
        internal const string DisplayName = "Ally Dummy";

        internal static PlayerAvatar Avatar { get; private set; }
        private static PlayerHealth health;
        private static Transform vision;
        private static Dummy owner;
        private static Collider body;

        private static readonly FieldInfo ViewIdField = AccessTools.Field(typeof(PhotonView), "viewIdField");
        private static readonly FieldInfo NameField = AccessTools.Field(typeof(PlayerAvatar), "playerName");
        private static readonly FieldInfo HealthField = AccessTools.Field(typeof(PlayerHealth), "health");
        private static readonly FieldInfo MaxHealthField = AccessTools.Field(typeof(PlayerHealth), "maxHealth");
        private static readonly FieldInfo HealthAvatarField = AccessTools.Field(typeof(PlayerHealth), "playerAvatar");
        private static readonly FieldInfo HealthViewField = AccessTools.Field(typeof(PlayerHealth), "photonView");
        private static readonly FieldInfo VisionAvatarField = AccessTools.Field(typeof(PlayerVisionTarget), "PlayerAvatar");
        private static readonly FieldInfo VisionViewField = AccessTools.Field(typeof(PlayerVisionTarget), "PhotonView");

        private static bool IsDummy(PlayerHealth h) => h != null && (object)h == health && health != null;

        /// <summary>Builds the pseudo-player for the Ally Dummy. feet = the droid's feet, local to the dummy.</summary>
        internal static void Create(Dummy ally, Vector3 feet, Collider bodyHitbox)
        {
            try
            {
                owner = ally;
                body = bodyHitbox;

                // switched off before anything is added, so no player script ever wakes up
                var go = new GameObject(DisplayName);
                go.SetActive(false);
                go.transform.SetParent(ally.transform, false);
                go.transform.localPosition = feet;

                PhotonView view = go.GetComponent<PhotonView>() ?? go.AddComponent<PhotonView>();
                ViewIdField?.SetValue(view, ViewId);
                PlayerHealth h = go.GetComponent<PlayerHealth>() ?? go.AddComponent<PlayerHealth>();
                PlayerVisionTarget target = go.GetComponent<PlayerVisionTarget>() ?? go.AddComponent<PlayerVisionTarget>();
                PlayerAvatar avatar = go.GetComponent<PlayerAvatar>() ?? go.AddComponent<PlayerAvatar>();

                // the chest point mods aim at lives on an active object, so it can follow the camera
                vision = new GameObject("Ally Dummy Vision Point").transform;
                vision.SetParent(ally.transform, false);
                vision.position = ally.Chest.position;

                avatar.photonView = view;
                avatar.playerHealth = h;
                avatar.PlayerVisionTarget = target;
                NameField?.SetValue(avatar, DisplayName);
                HealthAvatarField?.SetValue(h, avatar);
                HealthViewField?.SetValue(h, view);
                target.VisionTransform = vision;
                VisionAvatarField?.SetValue(target, avatar);
                VisionViewField?.SetValue(target, view);

                health = h;
                Avatar = avatar;
                SyncHealth(ally.Health, ally.MaxHealth);
                Plugin.Log.LogInfo($"Ally Dummy joined as a pseudo-player (network ID {ViewId}): other mods will see it as a teammate.");
            }
            catch (System.Exception e)
            {
                Avatar = null; health = null;
                Plugin.Log.LogError($"Making the Ally Dummy a pseudo-player failed; mods won't see it as a teammate: {e}");
            }
        }

        /// <summary>Keeps playerHealth.health / maxHealth equal to the dummy's health, for mods that read them.</summary>
        internal static void SyncHealth(int hp, int max)
        {
            if (health == null) return;
            HealthField?.SetValue(health, hp);
            MaxHealthField?.SetValue(health, max);
        }

        /// <summary>Every frame: the vision point sits on the droid's chest, on the side facing this player's camera.</summary>
        internal static void UpdateVision(Camera cam)
        {
            if (vision == null || owner == null || owner.Chest == null) return;
            Vector3 chest = owner.Chest.position;
            Vector3 toward = cam != null ? cam.transform.position - chest : owner.transform.forward;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-4f) toward = owner.transform.forward;
            toward.Normalize();
            Vector3 surface = body != null ? body.ClosestPoint(chest + toward * 2f) : chest;
            surface.y = chest.y;
            vision.position = surface + toward * 0.08f;   // just outside the hitbox, so line-of-sight checks reach it
        }

        // ---- routing heals and hurts to the dummy's health pool ----

        private static void RouteHeal(int amount)
        {
            if (amount <= 0 || owner == null) return;
            if (SemiFunc.IsMasterClientOrSingleplayer()) owner.HostHeal(amount, owner.Chest.position + owner.transform.forward);
            else Net.Request(HitInfo.Kind.Heal, amount, Vector3.zero);
        }

        private static void RouteHurt(int damage, Vector3 direction)
        {
            if (damage <= 0 || owner == null) return;
            if (SemiFunc.IsMasterClientOrSingleplayer()) owner.TargetDummyHit(new float[] { damage, direction.x, direction.y, direction.z });
            else Net.Request(HitInfo.Kind.Direct, damage, direction);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Heal))]
        private static bool HealPrefix(PlayerHealth __instance, int healAmount)
        {
            if (!IsDummy(__instance)) return true;
            RouteHeal(healAmount);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.HealOther))]
        private static bool HealOtherPrefix(PlayerHealth __instance, int healAmount)
        {
            if (!IsDummy(__instance)) return true;
            RouteHeal(healAmount);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Hurt))]
        private static bool HurtPrefix(PlayerHealth __instance, int damage)
        {
            if (!IsDummy(__instance)) return true;
            RouteHurt(damage, Vector3.zero);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.HurtOther))]
        private static bool HurtOtherPrefix(PlayerHealth __instance, int damage, Vector3 hurtPosition)
        {
            if (!IsDummy(__instance)) return true;
            Vector3 dir = owner != null ? owner.Chest.position - hurtPosition : Vector3.zero;
            RouteHurt(damage, dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero);
            return false;
        }

        // ---- being found ----

        private static readonly List<PlayerAvatar> withDummy = new List<PlayerAvatar>();
        private static readonly Assembly Game = typeof(SemiFunc).Assembly;
        private static readonly Assembly Self = typeof(PseudoPlayer).Assembly;

        /// <summary>Mods get the players plus the dummy (a copy, so the game's own list is never touched).</summary>
        [HarmonyPostfix, HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.PlayerGetList))]
        private static void PlayerGetListPostfix(ref List<PlayerAvatar> __result)
        {
            if (Avatar == null || __result == null || CalledByGame()) return;
            withDummy.Clear();
            withDummy.AddRange(__result);
            if (!withDummy.Contains(Avatar)) withDummy.Add(Avatar);
            __result = withDummy;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.PlayerAvatarGetFromPhotonID))]
        private static void FromPhotonIdPostfix(int photonID, ref PlayerAvatar __result)
        {
            if (photonID == ViewId && Avatar != null) __result = Avatar;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.PlayerGetName))]
        private static bool PlayerGetNamePrefix(PlayerAvatar player, ref string __result)
        {
            if (player == null || Avatar == null || player != Avatar) return true;
            __result = DisplayName;
            return false;
        }

        /// <summary>
        /// Whether PlayerGetList was called by the game's own code (Assembly-CSharp, Unity, Photon) rather than a mod.
        /// Walks up past Harmony's generated method to the first real caller.
        /// </summary>
        private static bool CalledByGame()
        {
            StackFrame[] frames = new StackTrace(1, false).GetFrames();
            if (frames == null) return true;
            foreach (StackFrame f in frames)
            {
                MethodBase m = f.GetMethod();
                if (m == null || m.Name.Contains("PlayerGetList")) continue;   // the patched method itself
                System.Type t = m.DeclaringType;
                if (t == null) continue;                                       // Harmony's generated replacement
                Assembly a = t.Assembly;
                if (a == Self || a.IsDynamic) continue;
                string n = a.GetName().Name;
                if (n.StartsWith("0Harmony") || n.StartsWith("Harmony") || n.StartsWith("MonoMod")) continue;
                return a == Game || n == "Assembly-CSharp-firstpass" || n.StartsWith("UnityEngine") || n.StartsWith("Unity.")
                       || n.StartsWith("Photon") || n == "mscorlib" || n.StartsWith("System");
            }
            return true;
        }

        internal static void Forget()
        {
            Avatar = null; health = null; vision = null; owner = null; body = null;
            withDummy.Clear();
        }
    }
}
