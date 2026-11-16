using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// The latched player's voice is muted on everyone else's game: after the game updates their voice chat, its
    /// voice speaker is turned down to nothing and the "talking" loudness (which moves their mouth) is cleared. Their
    /// text-to-speech has its own speaker and loudness, so it is left alone and still works. (The game's own
    /// OverrideMute can't be used: it silences the TTS too.)
    /// </summary>
    [HarmonyPatch(typeof(PlayerVoiceChat), "Update")]
    internal static class MimicVoice
    {
        private static readonly Dictionary<int, int> muted = new Dictionary<int, int>();   // player view ID -> mimics latched on
        private static readonly FieldInfo AvatarField = AccessTools.Field(typeof(PlayerVoiceChat), "playerAvatar");
        private static readonly FieldInfo SourceField = AccessTools.Field(typeof(PlayerVoiceChat), "audioSource");
        private static readonly FieldInfo LoudnessField = AccessTools.Field(typeof(PlayerVoiceChat), "clipLoudness");
        private static readonly FieldInfo LoudnessNoTtsField = AccessTools.Field(typeof(PlayerVoiceChat), "clipLoudnessNoTTS");
        private static readonly FieldInfo LoudnessTtsField = AccessTools.Field(typeof(PlayerVoiceChat), "clipLoudnessTTS");
        private static readonly FieldInfo LowPassVolume = AccessTools.Field(typeof(AudioLowPassLogic), "Volume");
        private static bool warned;

        internal static void Mute(int viewId)
        {
            if (viewId < 0) return;
            muted.TryGetValue(viewId, out int n);
            muted[viewId] = n + 1;
        }

        internal static void Unmute(int viewId)
        {
            if (!muted.TryGetValue(viewId, out int n)) return;
            if (n <= 1) muted.Remove(viewId); else muted[viewId] = n - 1;
        }

        internal static bool IsMuted(PlayerAvatar p) => p != null && muted.ContainsKey(R.ViewId(p));

        private static void Postfix(PlayerVoiceChat __instance)
        {
            if (muted.Count == 0 || __instance == null) return;
            try
            {
                PlayerAvatar who = AvatarField != null ? AvatarField.GetValue(__instance) as PlayerAvatar : null;
                if (who == null || who == PlayerAvatar.instance || !muted.ContainsKey(R.ViewId(who))) return;
                if (SourceField != null && SourceField.GetValue(__instance) is AudioSource voice && voice != null) voice.volume = 0f;
                if (__instance.lowPassLogic != null && LowPassVolume != null) LowPassVolume.SetValue(__instance.lowPassLogic, 0f);
                if (LoudnessNoTtsField != null) LoudnessNoTtsField.SetValue(__instance, 0f);
                if (LoudnessField != null && LoudnessTtsField != null) LoudnessField.SetValue(__instance, LoudnessTtsField.GetValue(__instance));
            }
            catch (System.Exception e)
            {
                if (!warned) { warned = true; Plugin.Log.LogWarning($"Alter Mimic: muting a voice failed: {e}"); }
            }
        }
    }
}
