using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// The arrows a broken pocket compass gives its user (local only; nobody else sees them).
    ///  - Placed on the game's HUD, centred under the lowest of the haul ("$ ... / $ ...") and goal ("x / y") texts that
    ///    is showing, so it follows them as they slide in and out. Sized from that text's height (ArrowSize).
    ///    With both compasses running, the two arrows are stacked (extraction green on top, enemy red under it).
    ///  - A solid 3D arrow (<see cref="CompassArrow3D"/>) that points where the target is from where you look, in 3D:
    ///    up the screen and away = straight ahead, tilting up or down for targets above or below; it turns smoothly.
    ///  - Countdown: a small m:ss timer under each arrow (TimerSize), in a lighter shade of the arrow's colour.
    ///  - Lasts ArrowSeconds (300) from breaking the compass (another one of the same kind restarts it), blinks in its
    ///    last 10 s, and ends early once nothing has been there to point to for NoTargetGraceSeconds (3). While there's
    ///    briefly nothing to point to it fades and holds still.
    ///  - Hidden while you're dead; gone when the level ends.
    /// </summary>
    internal class CompassArrows : MonoBehaviour
    {
        private class Entry
        {
            public CompassTargets.Kind kind;
            public float until, noTargetFor, nextSearch;
            public bool hasTarget, aimed;
            public Vector3 dir = Vector3.forward;
            public CompassArrow3D rig;
            public Vector3 target;
            public RectTransform rect;
            public RawImage image;
            public TextMeshProUGUI timer; // time left, under the arrow
            public CanvasGroup group;    // fades / blinks the arrow and its pie together
            public float started, length;
        }

        private static CompassArrows instance;
        private static readonly FieldInfo DisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo HaulTextField = AccessTools.Field(typeof(HaulUI), "Text");
        private static readonly FieldInfo GoalTextField = AccessTools.Field(typeof(GoalUI), "Text");

        private static readonly Color EnemyColour = new Color(0.95f, 0.18f, 0.12f);
        private static readonly Color ExtractionColour = new Color(0.45f, 0.82f, 0.28f);   // brighter than the haul's green $

        private readonly List<Entry> entries = new List<Entry>();
        private Transform parent;
        private Vector3 anchor;           // under the texts, in the parent's space
        private float unit = 40f;         // the anchor text's height, in the parent's space
        private bool anchored;
        private Canvas fallbackCanvas;
        private string lastAnchorLog;

        /// <summary>Starts (or restarts) the arrow for this kind on this game's screen.</summary>
        internal static void Begin(CompassTargets.Kind kind)
        {
            if (instance == null) instance = new GameObject("HatterMiscellaneousPack Compass Arrows").AddComponent<CompassArrows>();
            Entry e = instance.entries.Find(x => x.kind == kind);
            if (e == null)
            {
                e = new Entry { kind = kind };
                instance.entries.Add(e);
                instance.entries.Sort((a, b) => b.kind.CompareTo(a.kind));   // extraction (left) before enemy (right)
            }
            e.started = Time.time;
            e.length = Settings.CompassEffectSeconds.Value;
            e.until = e.started + e.length;
            e.noTargetFor = 0f;
            e.nextSearch = 0f;
            if (Settings.CompassDebugLog.Value) Plugin.Log.LogInfo($"Pocket compass: {CompassTargets.Describe(kind)} arrow on for {Settings.CompassEffectSeconds.Value:0} s.");
        }

        private void Update()
        {
            if (!SemiFunc.RunIsLevel()) { Destroy(gameObject); return; }
            Transform view = ViewTransform();
            PlayerAvatar me = PlayerAvatar.instance;
            bool dead = me == null || (DisabledField != null && (bool)DisabledField.GetValue(me));
            FindAnchor();

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry e = entries[i];
                string end = null;
                if (Time.time >= e.until) end = "time's up";
                else if (view != null)
                {
                    if (Time.time >= e.nextSearch)
                    {
                        e.nextSearch = Time.time + 0.2f;
                        e.hasTarget = CompassTargets.Find(e.kind, view.position, out e.target);
                    }
                    if (e.hasTarget) e.noTargetFor = 0f;
                    else if ((e.noTargetFor += Time.deltaTime) >= Settings.CompassNoTargetGrace.Value)
                        end = $"no {CompassTargets.Describe(e.kind)} left";
                }
                if (end != null)
                {
                    if (Settings.CompassDebugLog.Value) Plugin.Log.LogInfo($"Pocket compass: the {CompassTargets.Describe(e.kind)} arrow ended ({end}).");
                    if (e.rect != null) Destroy(e.rect.gameObject);
                    if (e.rig != null) Destroy(e.rig.gameObject);
                    entries.RemoveAt(i);
                }
            }
            if (entries.Count == 0) { Destroy(gameObject); return; }

            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (!EnsureUI(e)) continue;
                float sizeUnits = unit * Settings.ArrowSizeVsText.Value;
                e.rect.sizeDelta = new Vector2(sizeUnits, sizeUnits);
                // stacked downwards under the text, each arrow with its timer under it
                float timerUnits = unit * Settings.TimerSizeVsText.Value * 1.2f;
                float step = sizeUnits + timerUnits + unit * 0.15f;
                e.rect.localPosition = anchor + new Vector3(Settings.ArrowOffsetX.Value * sizeUnits,
                                                            -unit * 0.25f - sizeUnits / 2f - i * step + Settings.ArrowOffsetY.Value * sizeUnits, 0f);
                var tr = (RectTransform)e.timer.transform;
                tr.sizeDelta = new Vector2(sizeUnits * 3f, timerUnits);
                tr.anchoredPosition = new Vector2(0f, -timerUnits / 2f);
                e.rect.localScale = Vector3.one;
                e.rect.localRotation = Quaternion.identity;

                // direction: where the target is from where you look, in your view's own space (x right, y up, z ahead)
                if (e.hasTarget && view != null)
                {
                    Vector3 to = e.target - view.position;
                    if (to.sqrMagnitude > 1e-6f)
                    {
                        Vector3 want = view.InverseTransformDirection(to).normalized;
                        if (!e.aimed) { e.dir = want; e.aimed = true; }
                        e.dir = Vector3.Slerp(e.dir, want, 1f - Mathf.Exp(-14f * Time.deltaTime)).normalized;
                    }
                }
                Color shade = e.kind == CompassTargets.Kind.Enemy ? EnemyColour : ExtractionColour;
                e.rig.Point(e.dir, shade);

                float alpha = e.hasTarget ? 1f : 0.35f;
                float left = e.until - Time.time;
                if (left < 10f) alpha *= 0.55f + 0.45f * Mathf.Cos(Time.time * Mathf.Lerp(18f, 6f, left / 10f));
                if (dead) alpha = 0f;
                e.group.alpha = alpha;   // the colour is in the 3D arrow itself
                // the countdown under the arrow, m:ss (rounded up, so it shows 5:00 at the start and 0:01 at the end)
                int secs = Mathf.Max(0, Mathf.CeilToInt(e.until - Time.time));
                e.timer.text = $"{secs / 60}:{secs % 60:00}";
                e.timer.fontSize = Mathf.Max(6f, unit * Settings.TimerSizeVsText.Value);
                Color tc = Color.Lerp(e.kind == CompassTargets.Kind.Enemy ? EnemyColour : ExtractionColour, Color.white, 0.35f);
                e.timer.color = tc;
            }
        }

        private void OnDestroy()
        {
            foreach (Entry e in entries)
            {
                if (e.rect != null) Destroy(e.rect.gameObject);
                if (e.rig != null) Destroy(e.rig.gameObject);
            }
            entries.Clear();
            if (fallbackCanvas != null) Destroy(fallbackCanvas.gameObject);
            if (instance == this) instance = null;
        }

        private static Transform ViewTransform()
        {
            Camera cam = Camera.main;
            if (cam != null) return cam.transform;
            PlayerAvatar me = PlayerAvatar.instance;
            return me != null && me.localCamera != null ? me.localCamera.transform : null;
        }

        /// <summary>Under the lowest of the haul and goal texts that's showing (in their parent's space).</summary>
        private void FindAnchor()
        {
            Transform p = null;
            bool found = false;
            Vector3 best = default;
            float bestUnit = unit;
            string which = null;
            foreach (SemiUI ui in new SemiUI[] { HaulUI.instance, GoalUI.instance })
            {
                if (ui == null) continue;
                if (p == null) p = ui.transform.parent;
                if (ui.isHidden || ui.transform.parent != p) continue;
                TMP_Text text = ui.uiText;
                if (text == null) text = (ui is HaulUI ? HaulTextField : GoalTextField)?.GetValue(ui) as TMP_Text;
                if (text == null || !text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
                Bounds b = text.textBounds;
                if (!(b.size.y > 0f) || float.IsNaN(b.size.y) || b.size.y > 1e5f) continue;
                Vector3 bottom = p.InverseTransformPoint(text.transform.TransformPoint(new Vector3(b.center.x, b.min.y, 0f)));
                Vector3 top = p.InverseTransformPoint(text.transform.TransformPoint(new Vector3(b.center.x, b.max.y, 0f)));
                if (!found || bottom.y < best.y)
                {
                    found = true;
                    best = bottom;
                    bestUnit = Mathf.Max(1e-3f, top.y - bottom.y);
                    which = ui is HaulUI ? "haul" : "goal";
                }
            }
            if (p != null)
            {
                if (parent != p) { parent = p; DetachAll(); }
                if (found) { anchor = best; unit = bestUnit; anchored = true; }
                if (!anchored)
                {
                    // nothing showing yet: near the top middle of the HUD's area
                    RectTransform pr = p as RectTransform;
                    anchor = pr != null ? new Vector3(pr.rect.center.x, pr.rect.yMax - pr.rect.height * 0.12f, 0f) : Vector3.zero;
                }
                if (Settings.CompassDebugLog.Value && which != null && which != lastAnchorLog)
                {
                    lastAnchorLog = which;
                    Plugin.Log.LogInfo($"Pocket compass: arrow under the {which} text ('{p.name}', text height {unit:0.#}).");
                }
                return;
            }
            // no HUD found at all: our own overlay canvas, near the top middle of the screen
            if (fallbackCanvas == null)
            {
                var go = new GameObject("HatterMiscellaneousPack Arrow Canvas");
                fallbackCanvas = go.AddComponent<Canvas>();
                fallbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                fallbackCanvas.sortingOrder = 30000;
                go.AddComponent<CanvasScaler>();
                if (Settings.CompassDebugLog.Value) Plugin.Log.LogWarning("Pocket compass: the haul text wasn't found; the arrow uses its own place at the top of the screen.");
            }
            if (parent != fallbackCanvas.transform) { parent = fallbackCanvas.transform; DetachAll(); }
            anchor = new Vector3(0f, Screen.height * 0.5f - Screen.height * 0.12f, 0f);
            unit = Screen.height * 0.035f;
        }

        /// <summary>The HUD's own font (the haul text's), so the timer matches the game.</summary>
        private static void TMP_Font(TMP_Text t)
        {
            TMP_FontAsset f = null;
            if (HaulUI.instance != null && HaulUI.instance.uiText != null) f = HaulUI.instance.uiText.font;
            else if (GoalUI.instance != null && GoalUI.instance.uiText != null) f = GoalUI.instance.uiText.font;
            if (f != null) t.font = f;
        }

        private void DetachAll()
        {
            foreach (Entry e in entries) if (e.rect != null) { Destroy(e.rect.gameObject); e.rect = null; e.image = null; e.timer = null; e.group = null; }
        }

        private bool EnsureUI(Entry e)
        {
            if (e.rig == null) e.rig = CompassArrow3D.Create();
            if (e.rect != null && e.image != null && e.timer != null) return true;
            if (e.rect != null) Destroy(e.rect.gameObject);
            if (parent == null) return false;
            var go = new GameObject($"Pocket Compass Arrow ({e.kind})", typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            e.rect = (RectTransform)go.transform;
            e.rect.SetParent(parent, false);
            e.rect.anchorMin = e.rect.anchorMax = e.rect.pivot = new Vector2(0.5f, 0.5f);
            e.rect.SetAsLastSibling();
            e.group = go.AddComponent<CanvasGroup>();
            e.group.interactable = false;
            e.group.blocksRaycasts = false;
            e.image = go.AddComponent<RawImage>();
            e.image.texture = e.rig.texture;
            e.image.raycastTarget = false;
            // the timer: a small text just under the arrow (its top on the arrow's bottom edge)
            var t = new GameObject("Timer", typeof(RectTransform));
            t.layer = go.layer;
            var tr = (RectTransform)t.transform;
            tr.SetParent(e.rect, false);
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            e.timer = t.AddComponent<TextMeshProUGUI>();
            TMP_Font(e.timer);
            e.timer.alignment = TextAlignmentOptions.Center;
            e.timer.textWrappingMode = TextWrappingModes.NoWrap;
            e.timer.raycastTarget = false;
            e.timer.outlineWidth = 0.25f;
            e.timer.outlineColor = new Color32(0, 0, 0, 220);
            return true;
        }
    }
}
