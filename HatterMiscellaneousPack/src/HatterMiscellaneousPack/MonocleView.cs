using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// What the All-Seeing Monocle's user sees (their game only).
    ///  1. Zoom in: the held monocle is hidden and a copy of it flies from where it is to just in front of the camera,
    ///     turning its lens to face the eye and growing until the lens is bigger than the screen (ZoomSeconds, speeding up).
    ///  2. Looking: a full-screen lens (a slight warm tint, darker towards a gold rim that shows in the corners, LensSize)
    ///     and every valuable within SightRangeMetres highlighted through walls with the game's own discover highlight.
    ///  3. Zoom out: the lens and boxes go, and the copy flies back to the item (or shrinks away if it's gone), then the
    ///     real monocle shows again.
    /// </summary>
    internal class MonocleView : MonoBehaviour
    {
        private enum Phase { In, Looking, Out }

        private static MonocleView instance;

        private MonocleItem item;
        private Phase phase;
        private float t;
        private Transform cam;
        private GameObject copy;
        private readonly List<Renderer> hidden = new List<Renderer>();
        // the copy's pose in the camera's space: from (where the item was) to (filling the view)
        private Vector3 fromPos, toPos;
        private Quaternion fromRot, toRot;
        private float fromScale, toScale;

        private Canvas canvas;
        private RawImage lens;
        private Texture2D lensTexture;
        private int lensW, lensH;
        private float lensAlpha;
        private readonly Dictionary<PhysGrabObject, ValuableDiscoverGraphic> highlights = new Dictionary<PhysGrabObject, ValuableDiscoverGraphic>();
        private bool warned;
        private static readonly FieldInfo WaitTimerField = AccessTools.Field(typeof(ValuableDiscoverGraphic), "waitTimer");
        private static readonly FieldInfo WaitTimeField = AccessTools.Field(typeof(ValuableDiscoverGraphic), "waitTime");

        private ValuableObject[] valuables = new ValuableObject[0];
        private float nextValuableScan;

        private const float EyeDistance = 0.3f;    // where the lens ends up, in front of the camera

        internal static void Begin(MonocleItem m)
        {
            if (instance == null) instance = new GameObject("HatterMiscellaneousPack Monocle View").AddComponent<MonocleView>();
            instance.StartFor(m);
        }

        internal static void End(MonocleItem m)
        {
            if (instance != null && instance.item == m) instance.Stop();
        }

        // ---------------- zooming ----------------

        private void StartFor(MonocleItem m)
        {
            if (item != null && item != m) RestoreItem();
            item = m;
            Camera c = Camera.main;
            cam = c != null ? c.transform : null;
            if (cam == null || m.Display == null) { phase = Phase.Looking; t = 0f; return; }

            if (copy == null)
            {
                copy = Instantiate(m.Display.gameObject);
                copy.SetActive(true);
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                copy.name = "All-Seeing Monocle (zoom)";
                copy.transform.SetPositionAndRotation(m.Display.position, m.Display.rotation);
                copy.transform.localScale = m.Display.lossyScale;
                copy.transform.SetParent(cam, true);
                foreach (Collider col in copy.GetComponentsInChildren<Collider>(true)) Destroy(col);
            }
            // hide the real one while it's "at your eye"
            hidden.Clear();
            m.hiddenByView = true;   // the item's model hides itself while this is set

            fromPos = copy.transform.localPosition;
            fromRot = copy.transform.localRotation;
            fromScale = copy.transform.localScale.x;
            // the lens faces the eye (whichever side of it is nearer to facing it already)
            Vector3 n = fromRot * m.lensNormal;
            Vector3 face = Vector3.Dot(n, Vector3.forward) >= 0f ? Vector3.forward : Vector3.back;
            toRot = Quaternion.FromToRotation(n, face) * fromRot;
            // big enough that the lens covers the whole view at EyeDistance
            float fov = c.fieldOfView * Mathf.Deg2Rad;
            float halfH = EyeDistance * Mathf.Tan(fov / 2f);
            float needed = halfH * Mathf.Sqrt(1f + c.aspect * c.aspect) * 1.25f;
            toScale = m.lensRadius > 1e-5f ? needed / m.lensRadius : fromScale * 10f;
            toPos = new Vector3(0f, 0f, EyeDistance) - toRot * (m.lensCentre * toScale);
            copy.SetActive(true);
            t = 0f;   // from wherever the copy is now (also when turned back on mid-way out)
            phase = Phase.In;
        }

        private void Stop()
        {
            HideEsp();
            if (copy == null || cam == null) { Finish(); return; }
            copy.SetActive(true);
            if (phase == Phase.In) t = 1f - Mathf.Clamp01(t); else t = 0f;
            phase = Phase.Out;
        }

        private void Update()
        {
            if (item == null && phase != Phase.Out) { Finish(); return; }
            if (cam == null) { Camera c = Camera.main; cam = c != null ? c.transform : null; }
            float zoom = Mathf.Max(0.05f, Settings.MonocleZoomSeconds.Value);
            switch (phase)
            {
                case Phase.In:
                    t += Time.deltaTime / zoom;
                    Pose(Mathf.Clamp01(t) * Mathf.Clamp01(t));   // speeds up towards the eye
                    if (t >= 1f) { phase = Phase.Looking; if (copy != null) copy.SetActive(false); }
                    lensAlpha = Mathf.Clamp01((t - 0.75f) / 0.25f);
                    break;
                case Phase.Looking:
                    lensAlpha = Mathf.MoveTowards(lensAlpha, 1f, Time.deltaTime / 0.15f);
                    break;
                case Phase.Out:
                    t += Time.deltaTime / (zoom * 0.7f);
                    float u = 1f - Mathf.Clamp01(t);
                    lensAlpha = Mathf.Clamp01(1f - t / 0.2f);
                    if (item != null && item.Display != null && cam != null)
                    {
                        // follow the item home, wherever it is now (where its model would be)
                        Transform home = item.transform;
                        fromPos = cam.InverseTransformPoint(home.TransformPoint(item.visual.localPosition));
                        fromRot = Quaternion.Inverse(cam.rotation) * (home.rotation * item.visual.localRotation);
                        fromScale = home.lossyScale.x * item.visual.localScale.x;
                    }
                    else fromScale = 0.0001f;
                    Pose(u * u);
                    if (t >= 1f) { Finish(); return; }
                    break;
            }
            UpdateLens();
            if (phase == Phase.Looking) UpdateEsp(); else HideEsp();
        }

        /// <summary>k = 0: where the item is; 1: filling the view.</summary>
        private void Pose(float k)
        {
            if (copy == null) return;
            Transform ct = copy.transform;
            ct.localPosition = Vector3.LerpUnclamped(fromPos, toPos, k);
            ct.localRotation = Quaternion.Slerp(fromRot, toRot, k);
            float s = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(1e-5f, fromScale)), Mathf.Log(Mathf.Max(1e-5f, toScale)), k));
            ct.localScale = Vector3.one * s;
        }

        private void Finish()
        {
            RestoreItem();
            item = null;
            Destroy(gameObject);
        }

        private void RestoreItem()
        {
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
            if (item != null) item.hiddenByView = false;
        }

        private void OnDestroy()
        {
            RestoreItem();
            if (copy != null) Destroy(copy);
            if (canvas != null) Destroy(canvas.gameObject);
            if (lensTexture != null) Destroy(lensTexture);
            if (instance == this) instance = null;
        }

        // ---------------- the lens ----------------

        private void EnsureCanvas()
        {
            if (canvas != null) return;
            var go = new GameObject("HatterMiscellaneousPack Monocle Canvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29000;
            var lensGo = new GameObject("Lens", typeof(RectTransform));
            lensGo.transform.SetParent(go.transform, false);
            lens = lensGo.AddComponent<RawImage>();
            lens.raycastTarget = false;
            Stretch(lens.rectTransform);
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        private void UpdateLens()
        {
            if (lensAlpha <= 0.001f && canvas == null) return;
            EnsureCanvas();
            int w = Mathf.Max(64, Screen.width / 3), h = Mathf.Max(36, Screen.height / 3);
            if (lensTexture == null || w != lensW || h != lensH) BuildLensTexture(w, h);
            lens.color = new Color(1f, 1f, 1f, lensAlpha);
            lens.enabled = lensAlpha > 0.001f;
        }

        /// <summary>The lens at screen proportions (a third of the screen's resolution, stretched; it's all soft).</summary>
        private void BuildLensTexture(int w, int h)
        {
            if (lensTexture != null) Destroy(lensTexture);
            lensW = w; lensH = h;
            lensTexture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "HMP Monocle Lens", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            float diag = Mathf.Sqrt(w * w + h * h);
            float R = diag * 0.5f * Settings.MonocleLensSize.Value;
            float rim = Mathf.Max(3f, h * 0.035f);
            Color tint = new Color(1f, 0.85f, 0.55f, 0.05f);
            Color dark = new Color(0.04f, 0.03f, 0.02f, 0.96f);
            Color goldLight = new Color(1f, 0.86f, 0.45f, 1f), goldDark = new Color(0.45f, 0.3f, 0.08f, 1f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = x + 0.5f - w / 2f, dy = y + 0.5f - h / 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c;
                    if (d < R - rim)
                    {
                        // glass: a faint warm tint, darkening a little towards the rim, and a soft glint up and left
                        float edge = Mathf.Clamp01((d - (R - rim) * 0.75f) / ((R - rim) * 0.25f));
                        c = Color.Lerp(tint, new Color(0.1f, 0.07f, 0.02f, 0.35f), edge * edge);
                        float gx = dx + R * 0.35f, gy = dy - R * 0.35f;
                        float glint = Mathf.Exp(-(gx * gx + gy * gy) / (2f * (R * 0.12f) * (R * 0.12f)));
                        c = Color.Lerp(c, new Color(1f, 1f, 1f, 0.12f), glint);
                    }
                    else if (d < R)
                    {
                        // the gold rim, lit from the top left
                        float across = (d - (R - rim)) / rim;                        // 0 inner edge → 1 outer edge
                        float light = 0.5f + 0.5f * (-dx * 0.7f + dy * 0.7f) / Mathf.Max(1f, d);
                        float bevel = Mathf.Sin(across * Mathf.PI);
                        c = Color.Lerp(goldDark, goldLight, Mathf.Clamp01(light * 0.7f + bevel * 0.5f));
                    }
                    else c = dark;
                    px[y * w + x] = c;
                }
            lensTexture.SetPixels32(px);
            lensTexture.Apply(false, false);
            lens.texture = lensTexture;
        }

        // ---------------- the valuables: the game's own highlight ----------------

        /// <summary>
        /// Every valuable within SightRange gets the game's discover highlight (the yellow corners you see when you find a
        /// valuable; ValuableDiscover.New), kept up while it's in range by holding the graphic's wait timer, and silenced
        /// (its sound plays once per new highlight otherwise). Out of range or the monocle lowered: it plays its own fade-out.
        /// </summary>
        private void UpdateEsp()
        {
            ValuableDiscover vd = ValuableDiscover.instance;
            Camera c = Camera.main;
            if (vd == null || c == null) { HideEsp(); return; }
            if (Time.time >= nextValuableScan)
            {
                nextValuableScan = Time.time + 0.5f;
                valuables = FindObjectsByType<ValuableObject>(FindObjectsSortMode.None);
            }
            float range = Settings.MonocleEspRange.Value;
            Vector3 eye = c.transform.position;
            var keep = new HashSet<PhysGrabObject>();
            foreach (ValuableObject v in valuables)
            {
                if (v == null || !v.isActiveAndEnabled) continue;
                PhysGrabObject pgo = v.GetComponent<PhysGrabObject>();
                if (pgo == null || pgo.dead) continue;
                Rigidbody rb = pgo.GetComponent<Rigidbody>();
                Vector3 at = rb != null ? rb.worldCenterOfMass : v.transform.position;
                if ((at - eye).sqrMagnitude > range * range) continue;
                keep.Add(pgo);
                if (!highlights.TryGetValue(pgo, out ValuableDiscoverGraphic g) || g == null)
                {
                    g = NewHighlight(vd, pgo);
                    if (g == null) continue;
                    highlights[pgo] = g;
                }
                if (WaitTimerField != null && WaitTimeField != null) WaitTimerField.SetValue(g, WaitTimeField.GetValue(g));
            }
            // out of range now: stop holding them (they fade out by themselves)
            if (highlights.Count > keep.Count)
            {
                var drop = new List<PhysGrabObject>();
                foreach (var kv in highlights) if (kv.Key == null || !keep.Contains(kv.Key)) drop.Add(kv.Key);
                foreach (PhysGrabObject k in drop) highlights.Remove(k);
            }
        }

        private void HideEsp()
        {
            highlights.Clear();   // the graphics finish their wait and fade out on their own
        }

        private ValuableDiscoverGraphic NewHighlight(ValuableDiscover vd, PhysGrabObject pgo)
        {
            try
            {
                int before = vd.transform.childCount;
                vd.New(pgo, HighlightState(), null);
                for (int i = vd.transform.childCount - 1; i >= before && i >= 0; i--)
                {
                    ValuableDiscoverGraphic g = vd.transform.GetChild(i).GetComponent<ValuableDiscoverGraphic>();
                    if (g == null || g.target != pgo) continue;
                    if (g.sound != null)
                    {
                        var quiet = new Sound();
                        Sound.CopySound(g.sound, quiet);
                        quiet.Volume = 0f;
                        g.sound = quiet;
                    }
                    return g;
                }
            }
            catch (System.Exception e)
            {
                if (!warned) { warned = true; Plugin.Log.LogWarning($"{MonocleBuilder.DisplayName}: the game's highlight failed: {e}"); }
            }
            return null;
        }

        private static ValuableDiscoverGraphic.State HighlightState() =>
            System.Enum.TryParse(Settings.MonocleHighlight.Value, true, out ValuableDiscoverGraphic.State st) ? st : ValuableDiscoverGraphic.State.Discover;
    }
}
