using System.IO;
using System.Reflection;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>Textures embedded in the DLL for the C.A.R.T. Plus (Assets/cart_plus_*.png).</summary>
    internal static class CartPlusAssets
    {
        private static Texture2D shrinkIcon;

        /// <summary>The stasis button's icon: four arrows pointing in from the corners (pixel art, so point-filtered).</summary>
        internal static Texture2D ShrinkIcon => shrinkIcon != null ? shrinkIcon : (shrinkIcon = Load("cart_plus_shrink_icon.png"));

        private static Texture2D Load(string file)
        {
            string resource = "HatterCartsPack.Assets." + file;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) { Plugin.Log.LogWarning($"Embedded asset '{resource}' is missing."); return null; }
                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = Path.GetFileNameWithoutExtension(file),
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point,
                    hideFlags = HideFlags.DontUnloadUnusedAsset
                };
                if (!tex.LoadImage(bytes)) { Plugin.Log.LogWarning($"Could not decode embedded asset '{resource}'."); return null; }
                return tex;
            }
        }
    }
}
