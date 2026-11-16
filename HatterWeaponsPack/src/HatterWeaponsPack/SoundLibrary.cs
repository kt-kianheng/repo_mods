using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Sound effects embedded in the DLL as raw PCM (16-bit little-endian, mono, 44.1 kHz), converted from the
    /// source files with: ffmpeg -i in.mp3 -ac 1 -ar 44100 -f s16le out.pcm
    /// </summary>
    internal static class SoundLibrary
    {
        private const int SampleRate = 44100;
        private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        internal static AudioClip Get(string file)
        {
            if (cache.TryGetValue(file, out AudioClip clip)) return clip;
            string resource = "HatterWeaponsPack.Assets." + file;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) { Plugin.Log.LogError($"Embedded sound '{resource}' is missing."); return null; }
                var bytes = new byte[s.Length];
                int read = 0;
                while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                var samples = new float[read / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = (short)(bytes[2 * i] | (bytes[2 * i + 1] << 8)) / 32768f;
                clip = AudioClip.Create(Path.GetFileNameWithoutExtension(file), samples.Length, 1, SampleRate, false);
                clip.SetData(samples, 0);
                clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            cache[file] = clip;
            return clip;
        }
    }
}
