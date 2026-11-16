using TMPro;
using UnityEngine;

namespace TargetDummy
{
    /// <summary>
    /// The Ally Dummy's health bar: a camera-facing bar with "health / max" on it. Green when healthy, through yellow
    /// to red when low. A pale trail shows recent damage, and a lime flash shows a heal.
    /// </summary>
    internal class HealthBar : MonoBehaviour
    {
        private const float Width = 1.1f, Height = 0.12f;

        private Transform fill, trail;
        private Material fillMat, trailMat, backMat;
        private TextMeshPro text;
        private float shown, trailShown, target, flash;
        private int lastHealth = -1;
        private Vector3 home;   // where it sits, local to the dummy

        internal static HealthBar Create(Transform parent, Vector3 localPosition)
        {
            var go = new GameObject("Health Bar");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var bar = go.AddComponent<HealthBar>();
            bar.home = localPosition;
            bar.Build();
            return bar;
        }

        private void Build()
        {
            Shader unlit = Shader.Find("Sprites/Default");
            backMat = new Material(unlit) { color = new Color(0f, 0f, 0f, 0.65f) };
            trailMat = new Material(unlit) { color = new Color(1f, 0.95f, 0.85f, 0.9f) };
            fillMat = new Material(unlit) { color = Color.green };
            Quad("Back", backMat, new Vector3(Width + 0.04f, Height + 0.04f, 1f), 0.002f, 0);
            trail = Quad("Trail", trailMat, new Vector3(Width, Height, 1f), 0.001f, 1);
            fill = Quad("Fill", fillMat, new Vector3(Width, Height, 1f), 0f, 2);

            var label = new GameObject("Text");
            label.transform.SetParent(transform, false);
            label.transform.localPosition = new Vector3(0f, 0.19f, 0f);
            text = DamagePopup.MakeText(label, 1.6f);
        }

        private Transform Quad(string name, Material mat, Vector3 scale, float back, int order)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = name;
            q.layer = 0;
            q.transform.SetParent(transform, false);
            q.transform.localScale = scale;
            q.transform.localPosition = new Vector3(0f, 0f, back);   // behind = further from the camera
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = 90 + order;
            return q.transform;
        }

        internal void Set(int health, int max, bool instant)
        {
            target = max > 0 ? Mathf.Clamp01(health / (float)max) : 0f;
            if (instant || lastHealth < 0) { shown = trailShown = target; }
            else if (health > lastHealth) flash = 1f;
            lastHealth = health;
            text.text = $"{health:N0} <size=70%>/ {max:N0}</size>";
        }

        private void Update()
        {
            // the fill moves quickly; the damage trail catches up after a short pause
            shown = Mathf.MoveTowards(shown, target, Time.deltaTime * 1.5f);
            trailShown = trailShown > shown ? Mathf.MoveTowards(trailShown, shown, Time.deltaTime * 0.4f) : shown;
            Place(fill, shown);
            Place(trail, trailShown);

            Color c = target > 0.5f ? Color.Lerp(new Color(1f, 0.85f, 0.15f), new Color(0.3f, 0.95f, 0.3f), (target - 0.5f) * 2f)
                                    : Color.Lerp(new Color(0.95f, 0.2f, 0.15f), new Color(1f, 0.85f, 0.15f), target * 2f);
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 2f);
            fillMat.color = Color.Lerp(c, new Color(0.75f, 1f, 0.5f), flash);
            text.color = Color.Lerp(Color.white, new Color(0.6f, 1f, 0.45f), flash);

            // never hidden behind the dummy when you stand right next to it (see DamagePopup.Unblocked)
            Vector3 wanted = transform.parent != null ? transform.parent.TransformPoint(home) : transform.position;
            transform.position = DamagePopup.Unblocked(wanted, out float shrink);
            Camera cam = Camera.main;
            if (cam == null) cam = Object.FindAnyObjectByType<Camera>();
            if (cam != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
                transform.localScale = Vector3.one * Mathf.Clamp(Vector3.Distance(cam.transform.position, wanted) / 6f, 1f, 2f) * shrink;
            }
        }

        /// <summary>Left-aligned bar of the given fraction: +X is the viewer's right, so it empties towards the left.</summary>
        private static void Place(Transform t, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            t.localScale = new Vector3(Mathf.Max(0.0001f, Width * fraction), Height, 1f);
            t.localPosition = new Vector3(-Width * (1f - fraction) / 2f, 0f, t.localPosition.z);
        }

        private void OnDestroy()
        {
            Destroy(fillMat); Destroy(trailMat); Destroy(backMat);
        }
    }
}
