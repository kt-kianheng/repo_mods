using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>
    /// Builds the Orb C.A.R.T. inventory icon at startup: the game's own baked Pocket C.A.R.T. icon,
    /// with the closed hatch (embedded in the DLL) drawn on top.
    /// No game artwork ships with the mod; it is read from the player's install.
    /// </summary>
    internal static class OrbCartIcon
    {
        // Baked icon file name for the Pocket C.A.R.T. in R.E.P.O. v0.5.0, used if the key lookup misses.
        private const string KnownPocketIconFile = "a13809b12dec3e54d6e320802e6797d4.png";
        private const int OverlaySize = 512;

        internal static Sprite Build(Item pocketItem, GameObject pocketPrefab)
        {
            try
            {
                Texture2D baseIcon = LoadPocketIcon(pocketItem, pocketPrefab);
                if (baseIcon == null)
                {
                    Plugin.Log.LogWarning("Pocket C.A.R.T. icon not found; using the built-in Orb C.A.R.T. icon.");
                    return null;
                }
                if (baseIcon.width != OverlaySize || baseIcon.height != OverlaySize)
                {
                    Plugin.Log.LogWarning($"Pocket C.A.R.T. icon is {baseIcon.width}x{baseIcon.height}, expected {OverlaySize}; using the built-in icon.");
                    return null;
                }

                Color32[] pixels = baseIcon.GetPixels32();
                Over(pixels, OrbCartAssets.HatchOverlay);

                Texture2D result = new Texture2D(OverlaySize, OverlaySize, TextureFormat.RGBA32, false)
                {
                    name = "Orb Cart Icon",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontUnloadUnusedAsset
                };
                result.SetPixels32(pixels);
                result.Apply(false, false);
                Object.Destroy(baseIcon);

                Sprite sprite = Sprite.Create(result, new Rect(0, 0, OverlaySize, OverlaySize), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = "Orb Cart Icon";
                return sprite;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Could not compose the Orb C.A.R.T. icon ({e.Message}); using the built-in icon.");
                return null;
            }
        }

        private static Texture2D LoadPocketIcon(Item pocketItem, GameObject pocketPrefab)
        {
            string root = Application.streamingAssetsPath.TrimEnd('/', '\\');
            var candidates = new List<string>();
            // Same key the game uses: "<item asset name>|<prefab name>".
            if (pocketItem != null) candidates.Add(SemiIconMaker.BakedIconPath("Items", pocketItem.name + "|" + pocketPrefab.name));
            candidates.Add(SemiIconMaker.BakedIconPath("Items", pocketPrefab.name + "|" + pocketPrefab.name));
            candidates.Add("BakedIcons/Items/" + KnownPocketIconFile);

            foreach (string relative in candidates)
            {
                string path = Path.Combine(root, relative);
                if (!File.Exists(path)) continue;
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(File.ReadAllBytes(path), false)) return tex;
                Object.Destroy(tex);
            }
            return null;
        }

        /// <summary>Straight-alpha "over" blend of an overlay onto the destination pixels.</summary>
        private static void Over(Color32[] dst, Texture2D overlay)
        {
            if (overlay == null || overlay.width != OverlaySize || overlay.height != OverlaySize) return;
            Color32[] src = overlay.GetPixels32();
            for (int i = 0; i < dst.Length; i++)
            {
                int sa = src[i].a;
                if (sa == 0) continue;
                if (sa == 255) { dst[i] = src[i]; continue; }
                int da = dst[i].a;
                int outA = sa + da * (255 - sa) / 255;
                if (outA == 0) { dst[i] = default; continue; }
                dst[i] = new Color32(
                    (byte)((src[i].r * sa + dst[i].r * da * (255 - sa) / 255) / outA),
                    (byte)((src[i].g * sa + dst[i].g * da * (255 - sa) / 255) / outA),
                    (byte)((src[i].b * sa + dst[i].b * da * (255 - sa) / 255) / outA),
                    (byte)outA);
            }
        }
    }
}
