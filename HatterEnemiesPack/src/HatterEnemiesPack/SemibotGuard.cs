using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// Catches errors thrown by the game's player-visuals scripts while they run on a semibot rig's copy (it feeds
    /// them stand-ins, so a field the game expects may be missing). Each error is logged with its stack the first
    /// 3 times per method (it names what's missing) and swallowed, so one gap doesn't stop the rest of the body.
    /// Errors on real players' scripts are passed through untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class SemibotGuard
    {
        private static readonly string[] Types = { "PlayerAvatarVisuals", "PlayerExpression", "PlayerAvatarRightArm", "PlayerAvatarLeftArm", "PlayerAvatarTalkAnimation", "PlayerCosmetics", "FlashlightController", "ToolBackAway", "Cosmetic" };
        private static readonly string[] Methods = { "Awake", "Start", "OnEnable", "Update", "LateUpdate", "FixedUpdate" };
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string tn in Types)
            {
                System.Type t = AccessTools.TypeByName(tn);
                if (t == null) continue;
                foreach (string mn in Methods)
                {
                    MethodInfo m = AccessTools.DeclaredMethod(t, mn, System.Type.EmptyTypes);
                    if (m != null) yield return m;
                }
            }
        }

        /// <summary>For the debug panel: how many errors each of the game's methods has thrown on a rig ("" if none).</summary>
        internal static string Summary()
        {
            if (counts.Count == 0) return "";
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{kv.Key} ×{kv.Value}");
            return string.Join(", ", parts);
        }

        // every reference chain PlayerEyes.Update (and what it calls) follows, from the game's IL
        private static readonly string[] EyePaths =
        {
            "playerAvatar", "playerAvatarVisuals", "playerAvatarRightArm", "playerExpressions",
            "eyeLeft", "eyeRight", "pupilLeft", "pupilRight", "targetIdle", "targetLead", "lookAt",
            "springQuaternionLeft", "springQuaternionRight",
            "playerAvatar.playerHealth", "playerAvatar.physGrabber", "playerAvatar.physGrabber.grabbedObjectTransform",
            "playerAvatar.healthGrab", "playerAvatar.healthGrab.staticGrabObject", "playerAvatar.healthGrab.staticGrabObject.playerGrabbing",
            "playerAvatar.tumble", "playerAvatar.tumble.physGrabObject", "playerAvatar.tumble.physGrabObject.playerGrabbing",
            "playerAvatar.localCamera", "playerAvatar.playerAvatarVisuals",
            "playerAvatarRightArm.mapToolController", "playerAvatarRightArm.physGrabBeam",
            "playerAvatarRightArm.physGrabBeam.lineRenderer", "playerAvatarRightArm.physGrabBeam.PhysGrabPoint",
            "playerAvatarVisuals.playerAvatar",
        };

        /// <summary>Which of EyePaths are null on this PlayerEyes (and every player in GameDirector.PlayerList's grab beam / voice).</summary>
        internal static string EmptyRefs(PlayerEyes eyes)
        {
            var empty = new List<string>();
            foreach (string path in EyePaths)
            {
                object o = eyes;
                string broken = null;
                foreach (string part in path.Split('.'))
                {
                    FieldInfo f = o != null ? AccessTools.Field(o.GetType(), part) : null;
                    if (f == null) { broken = $"{path} (no field {part})"; break; }
                    o = f.GetValue(o);
                    if (o == null || (o is Object uo && uo == null)) { broken = path; break; }
                }
                if (broken != null) empty.Add(broken);
            }
            if (GameDirector.instance == null) empty.Add("GameDirector.instance");
            else
                foreach (PlayerAvatar p in GameDirector.instance.PlayerList)
                {
                    if (p == null) { empty.Add("a null player in PlayerList"); continue; }
                    if (p.physGrabber == null) empty.Add($"{R.Name(p)}.physGrabber");
                    else if (p.physGrabber.physGrabBeamComponent == null) empty.Add($"{R.Name(p)}.physGrabber.physGrabBeamComponent");
                    else if (p.physGrabber.physGrabBeamComponent.lineRenderer == null) empty.Add($"{R.Name(p)}...lineRenderer");
                }
            if (LevelGenerator.Instance == null) empty.Add("LevelGenerator.Instance");
            return empty.Count == 0 ? "none of the checked ones" : string.Join(", ", empty);
        }

        private static System.Exception Finalizer(System.Exception __exception, Component __instance, MethodBase __originalMethod)
        {
            if (__exception == null || __instance == null) return __exception;
            SemibotTag tag = null;
            try { tag = __instance.transform.root.GetComponent<SemibotTag>(); } catch { }
            if (tag == null) return __exception;   // a real player's: not ours to hide
            string key = $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}";
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
            if (n < 3) Plugin.Log.LogWarning($"Semibot: the game's {key} failed on the semibot copy ({n + 1}): {__exception}");
            if (n == 0 && __instance is PlayerEyes eyes) Plugin.Log.LogWarning($"Semibot: PlayerEyes references that are empty: {EmptyRefs(eyes)}");
            else if (n == 3) Plugin.Log.LogWarning($"Semibot: {key} keeps failing on the semibot copy; not logging it again.");
            return null;
        }
    }

    /// <summary>
    /// The game's "everyone nearby, look here" (SemiFunc.PlayerEyesOverrideSoft: a player landing a jump, enemies,
    /// impacts, breaking objects...) only reaches the players in GameDirector.PlayerList. This passes the same call
    /// on to every semibot rig's eyes, with the game's own rule: within the radius and more than 0.5 m away.
    /// </summary>
    [HarmonyPatch(typeof(SemiFunc), nameof(SemiFunc.PlayerEyesOverrideSoft))]
    internal static class SemibotEyesPatch
    {
        private static void Postfix(Vector3 _position, float _time, GameObject _obj, float _radius)
        {
            foreach (SemibotRig rig in SemibotRig.All)
            {
                if (rig == null || rig.Eyes == null || rig.Eye == null) continue;
                float d = Vector3.Distance(_position, rig.Eye.position);
                if (d < _radius && d > 0.5f)
                {
                    try { rig.Eyes.OverrideSoft(_position, _time, _obj); } catch { }
                }
            }
        }
    }
}
