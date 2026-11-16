using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace TargetDummy
{
    /// <summary>
    /// Floating numbers: world-space TextMeshPro in the game's own font that face the camera, pop, rise and fade.
    /// The big line is the damage; the small line under it lists knockback, torque, stun, freeze or impact values.
    /// </summary>
    internal class DamagePopup : MonoBehaviour
    {
        private static TMP_FontAsset font;
        private static bool fontSearched;

        private TextMeshPro text;
        private float age, life;
        private Vector3 start, drift;
        private Color color;

        // ---- hit numbers ----

        internal static void SpawnHit(HitInfo hit)
        {
            string main;
            Color c;
            var parts = new List<string>();
            bool ally = hit.Dummy == DummyKind.Ally;

            if (hit.Type == HitInfo.Kind.Heal)
            {
                // heals: what actually went in, and whether it topped the dummy up
                int amount = ally ? hit.Effective : hit.Damage;
                main = "+" + amount.ToString("N0");
                c = new Color(0.45f, 1f, 0.3f);
                if (ally && hit.Effective < hit.Damage) parts.Add(hit.Effective == 0 ? $"Full health ({hit.Damage:N0} wasted)" : $"Full health (of {hit.Damage:N0})");
            }
            else if (ally)
            {
                // friendly fire: the damage a teammate would take
                main = hit.Kill ? "KILL" : "-" + hit.Damage.ToString("N0");
                c = hit.Damage <= 0 && !hit.Kill ? new Color(0.75f, 0.75f, 0.75f) : new Color(1f, 0.38f, 0.3f);
                if (hit.WouldDie) parts.Add("<color=#ff4040>Would have downed a player</color>");
            }
            else if (hit.Kill) { main = "KILL"; c = new Color(1f, 0.2f, 0.45f); }
            else
            {
                main = hit.Damage.ToString("N0");
                c = hit.Damage <= 0 ? new Color(0.75f, 0.75f, 0.75f)
                  : hit.Damage < 50 ? Color.white
                  : hit.Damage < 150 ? new Color(1f, 0.9f, 0.3f)
                  : hit.Damage < 400 ? new Color(1f, 0.55f, 0.15f)
                  : new Color(1f, 0.22f, 0.18f);
            }

            var sb = new StringBuilder(main);
            if (Settings.ShowForce.Value)
            {
                if (hit.Type == HitInfo.Kind.Impact)
                {
                    parts.Add($"Impact {hit.ImpactForce:0}");
                    parts.Add($"{hit.Speed:0.0} m/s");
                }
                if (hit.Type == HitInfo.Kind.Push) parts.Add("Push");
                if (hit.Knockback > 0f) parts.Add($"Knockback {hit.Knockback:0.#}");
                if (hit.Torque > 0f) parts.Add(ally ? $"Tumble force {hit.Torque:0.#}" : $"Torque {hit.Torque:0.#}");
                if (hit.Stun > 0f) parts.Add($"Stun {hit.Stun:0.#}s");
                if (hit.Freeze > 0f) parts.Add(ally ? $"Tumble {hit.Freeze:0.#}s" : $"Freeze {hit.Freeze:0.#}s");
            }
            if (parts.Count > 0) sb.Append("\n<size=45%><color=#9fd8ff>").Append(string.Join("   ", parts)).Append("</color></size>");

            // a little in front of the hit, towards the viewer, so it isn't buried in the model
            Camera cam = ViewCamera();
            Vector3 p = hit.Point + Vector3.up * 0.15f;
            if (cam != null) p += (cam.transform.position - p).normalized * 0.35f;
            float scale = Mathf.Lerp(0.85f, 1.35f, Mathf.InverseLerp(0f, 400f, Mathf.Abs(hit.Damage)));
            Spawn(sb.ToString(), c, p, scale, Settings.NumberLifetime.Value);
        }

        private static void Spawn(string content, Color c, Vector3 position, float scale, float lifetime)
        {
            var go = new GameObject("Target Dummy Number");
            go.transform.position = position;
            var popup = go.AddComponent<DamagePopup>();
            popup.text = MakeText(go, 3.2f * scale * Settings.NumberSize.Value);
            popup.text.text = content;
            popup.color = c;
            popup.text.color = c;
            popup.life = lifetime;
            popup.start = position;
            popup.drift = new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
            popup.Face();
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = age / life;
            if (t >= 1f) { Destroy(gameObject); return; }
            float rise = 1f - (1f - t) * (1f - t);                       // ease out
            Vector3 wanted = start + Vector3.up * (0.9f * rise) + drift * rise;
            float pop = t < 0.12f ? Mathf.Lerp(1.6f, 1f, t / 0.12f) : 1f; // punch in
            transform.position = Unblocked(wanted, out float shrink);
            transform.localScale = Vector3.one * pop * DistanceScale(wanted) * shrink;
            Color c = color; c.a = t < 0.65f ? 1f : 1f - (t - 0.65f) / 0.35f;
            text.color = c;
            Face();
        }

        private void Face()
        {
            Camera cam = ViewCamera();
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        /// <summary>
        /// Where to draw text meant to be at <paramref name="wanted"/> so nothing hides it: if anything (the dummy
        /// itself, when you stand right next to it) is between the camera and that point, the text moves along the
        /// same line to just in front of it, and shrinks by the same factor so it looks exactly the same on screen.
        /// </summary>
        internal static Vector3 Unblocked(Vector3 wanted, out float shrink)
        {
            shrink = 1f;
            Camera cam = ViewCamera();
            if (cam == null || !Settings.NumbersThroughWalls.Value) return wanted;
            Vector3 eye = cam.transform.position;
            Vector3 toward = wanted - eye;
            float dist = toward.magnitude;
            if (dist < 0.05f) return wanted;
            if (!Physics.Raycast(eye, toward / dist, out RaycastHit hit, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return wanted;
            float near = Mathf.Max(0.3f, hit.distance - 0.15f);
            if (near >= dist) return wanted;
            shrink = near / dist;
            return eye + toward / dist * near;
        }

        /// <summary>Grows a little with distance so numbers stay readable from across the shop.</summary>
        private static float DistanceScale(Vector3 p)
        {
            Camera cam = ViewCamera();
            if (cam == null) return 1f;
            return Mathf.Clamp(Vector3.Distance(cam.transform.position, p) / 5f, 1f, 2.5f);
        }

        private static Camera cachedCamera;
        private static Camera ViewCamera()
        {
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled) return cachedCamera;
            cachedCamera = Camera.main;
            if (cachedCamera == null) cachedCamera = Object.FindAnyObjectByType<Camera>();
            return cachedCamera;
        }

        internal static TextMeshPro MakeText(GameObject go, float size)
        {
            var tmp = go.AddComponent<TextMeshPro>();
            TMP_FontAsset f = GameFont();
            if (f != null) tmp.font = f;
            tmp.fontSize = size;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.richText = true;
            tmp.fontStyle = FontStyles.Bold;
            tmp.outlineWidth = 0.18f;
            tmp.outlineColor = new Color32(0, 0, 0, 220);
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
            if (Settings.NumbersThroughWalls.Value)
            {
                // drawn on top where the game ships TextMeshPro's overlay shader; Unblocked() covers the rest
                Material m = tmp.fontMaterial;   // per-text copy
                if (m != null)
                {
                    if (OverlayShader != null) m.shader = OverlayShader;
                    if (m.HasProperty("unity_GUIZTestMode")) m.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
                    m.renderQueue = 4000;
                }
                tmp.renderer.sortingOrder = 100;
            }
            return tmp;
        }

        private static Shader overlayShader;
        private static bool overlaySearched;
        private static Shader OverlayShader
        {
            get
            {
                if (!overlaySearched) { overlaySearched = true; overlayShader = Shader.Find("TextMeshPro/Distance Field Overlay"); }
                return overlayShader;
            }
        }

        /// <summary>The font from the gacha machine's screen, or any font the game has loaded.</summary>
        private static TMP_FontAsset GameFont()
        {
            if (font != null || fontSearched) return font;
            fontSearched = true;
            CosmeticShopMachine machine = CosmeticShopMachine.instance;
            if (machine != null && machine.screenCosmeticTexts != null)
                foreach (TextMeshPro t in machine.screenCosmeticTexts) if (t != null && t.font != null) { font = t.font; break; }
            if (font == null)
            {
                foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) { font = f; break; }
            }
            if (font == null) Plugin.Log.LogWarning("No TextMeshPro font found; numbers use TextMeshPro's default.");
            return font;
        }

        // ---- the running total over the dummy's head ----

        internal class Label : MonoBehaviour
        {
            internal TextMeshPro Text;
            internal Vector3 Home;   // where it sits, local to the dummy
            private float alpha, target;

            internal void Set(string content, Color c)
            {
                Text.text = content;
                Text.color = c;
                alpha = target = 1f;
                gameObject.SetActive(true);
            }

            internal void Fade() => target = 0f;

            private void Update()
            {
                alpha = Mathf.MoveTowards(alpha, target, Time.deltaTime * 1.5f);
                Color c = Text.color; c.a = alpha; Text.color = c;
                Vector3 wanted = transform.parent != null ? transform.parent.TransformPoint(Home) : transform.position;
                transform.position = Unblocked(wanted, out float shrink);
                Camera cam = ViewCamera();
                if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
                transform.localScale = Vector3.one * DistanceScale(wanted) * shrink;
                if (alpha <= 0f && target <= 0f) gameObject.SetActive(false);
            }
        }

        internal static Label Persistent(Transform parent, Vector3 localPosition)
        {
            var go = new GameObject("Target Dummy Total");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var label = go.AddComponent<Label>();
            label.Home = localPosition;
            label.Text = MakeText(go, 2.4f * Settings.NumberSize.Value);
            label.Text.text = "";
            go.SetActive(false);
            return label;
        }
    }
}
