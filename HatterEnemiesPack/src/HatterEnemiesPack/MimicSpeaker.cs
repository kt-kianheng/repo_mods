using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// A text-to-speech speaker with floating words, made from the game's parts (the player TTS prefab and a
    /// SemiWorldText), set to a player's voice and pitch. 3D sound through the TTS mixer; never bound to a player,
    /// so nothing shows or plays at the real player. Used by the semibot form (the head has its own copy).
    /// </summary>
    internal class MimicSpeaker : MonoBehaviour
    {
        private static readonly FieldInfo VoiceChatField = AccessTools.Field(typeof(PlayerAvatar), "voiceChat");
        private static readonly MethodInfo LocaleMethod = AccessTools.Method(typeof(LocalizationManager), "TTSLocaleGetCode");
        private static readonly FieldInfo VoiceNumField = AccessTools.Field(typeof(TTSVoice), "activeVoiceNum");
        private static readonly FieldInfo VoiceFreqField = AccessTools.Field(typeof(TTSVoice), "voiceBaseFrequency");
        private static readonly FieldInfo FetchField = AccessTools.Field(typeof(AudioLowPassLogic), "Fetch");

        private TTSVoice voice;
        private AudioSource source;
        private SemiWorldText words;

        internal bool Speaking => source != null && source.isPlaying;

        internal static MimicSpeaker Create(Transform parent, Vector3 localPosition, float textHeight, PlayerAvatar who)
        {
            var go = new GameObject("Alter Mimic Speaker");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var sp = go.AddComponent<MimicSpeaker>();
            sp.Make(who, textHeight);
            return sp;
        }

        private void Make(PlayerAvatar who, float textHeight)
        {
            PlayerVoiceChat vc = VoiceChatField != null && who != null ? VoiceChatField.GetValue(who) as PlayerVoiceChat : null;
            if (vc == null) vc = PlayerVoiceChat.instance;
            if (vc != null && vc.TTSprefab != null)
            {
                GameObject go = Instantiate(vc.TTSprefab, transform);
                go.name = "Alter Mimic Voice";
                go.transform.localPosition = Vector3.zero;
                voice = go.GetComponent<TTSVoice>() ?? go.GetComponentInChildren<TTSVoice>();
                source = voice != null ? voice.GetComponent<AudioSource>() : null;
                if (source != null)
                {
                    source.spatialBlend = 1f;
                    if (vc.mixerTTSSound != null) source.outputAudioMixerGroup = vc.mixerTTSSound;
                    AudioLowPassLogic lp = source.GetComponent<AudioLowPassLogic>();
                    if (lp != null)
                    {
                        try { FetchField?.SetValue(lp, true); lp.Setup(); }
                        catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: voice muffling setup failed: {e}"); }
                    }
                }
                TTSVoice theirs = vc.ttsVoice;
                if (voice != null && theirs != null)
                {
                    try
                    {
                        if (VoiceNumField != null) voice.setVoice((int)VoiceNumField.GetValue(theirs));
                        if (VoiceFreqField != null) VoiceFreqField.SetValue(voice, VoiceFreqField.GetValue(theirs));
                    }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: copying the voice failed: {e}"); }
                }
            }
            else Plugin.Log.LogWarning("Alter Mimic: no TTS prefab found; it can't talk.");

            var textObj = new GameObject("Alter Mimic Words");
            textObj.transform.SetParent(transform, false);
            textObj.transform.localPosition = Vector3.up * textHeight;
            words = textObj.AddComponent<SemiWorldText>();
        }

        internal void Say(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            float seconds = Mathf.Max(1f + 0.45f * line.Split(' ').Length, 0.2f * line.Length);
            try
            {
                string code = "en-US";
                if (LocalizationManager.instance != null && LocaleMethod != null) code = LocaleMethod.Invoke(LocalizationManager.instance, null) as string ?? code;
                if (voice != null) voice.TTSSpeakNow(line, false, code);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: speaking failed: {e}"); }
            try { if (words != null) words.ShowText(line, seconds); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: showing the words failed: {e}"); words = null; }
        }
    }
}
