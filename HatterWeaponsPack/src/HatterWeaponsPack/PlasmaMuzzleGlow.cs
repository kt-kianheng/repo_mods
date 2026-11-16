using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// The Plasma Rifle's shot effect in place of the shotgun's muzzle blast: a soft blue radial-gradient glow
    /// (camera-facing) and a blue light at the muzzle that swell briefly and fade.
    /// </summary>
    internal class PlasmaMuzzleGlow : MonoBehaviour
    {
        private const float Duration = 0.16f;
        private static readonly Color Blue = new Color(0.25f, 0.6f, 1f, 1f);

        private static Texture2D gradient;
        private static Material material;

        private Transform muzzle;
        private float t;
        private Transform quad;
        private Light glowLight;
        private MeshRenderer quadRenderer;
        private MaterialPropertyBlock block;

        internal static void Spawn(Transform muzzle)
        {
            // not parented (the gun's transforms may be scaled); it follows the muzzle instead
            var go = new GameObject("HWP Plasma Muzzle Glow");
            go.transform.position = muzzle.position;
            go.AddComponent<PlasmaMuzzleGlow>().muzzle = muzzle;
        }

        /// <summary>White-hot centre fading through blue to transparent at the edge.</summary>
        private static Texture2D Gradient
        {
            get
            {
                if (gradient != null) return gradient;
                const int size = 64;
                gradient = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HWP Plasma Glow" };
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                        float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                        Color c = Color.Lerp(new Color(0.85f, 0.95f, 1f), Blue, Mathf.SmoothStep(0f, 1f, r / 0.45f));
                        c = Color.Lerp(c, new Color(0.1f, 0.3f, 1f), Mathf.Clamp01((r - 0.45f) / 0.55f));
                        c.a = Mathf.Pow(1f - r, 1.8f);
                        px[y * size + x] = c;
                    }
                gradient.SetPixels(px);
                gradient.Apply(false, true);
                Object.DontDestroyOnLoad(gradient);
                return gradient;
            }
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                Shader s = Shader.Find("Sprites/Default");
                if (s == null) return null;
                material = new Material(s) { mainTexture = Gradient, name = "HWP Plasma Glow" };
                Object.DontDestroyOnLoad(material);
                return material;
            }
        }

        private void Awake()
        {
            glowLight = gameObject.AddComponent<Light>();
            glowLight.type = LightType.Point; glowLight.color = Blue; glowLight.range = 3f; glowLight.intensity = 4f;

            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Glow";
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            quad = q.transform;
            quadRenderer = q.GetComponent<MeshRenderer>();
            if (Material != null) quadRenderer.sharedMaterial = Material;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            block = new MaterialPropertyBlock();
            LateUpdate();
        }

        private void LateUpdate()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / Duration);
            float fade = 1f - k * k;
            if (muzzle != null) transform.position = muzzle.position;
            Camera cam = Camera.main;
            if (cam != null) quad.rotation = Quaternion.LookRotation(quad.position - cam.transform.position, cam.transform.up);
            quad.localScale = Vector3.one * Mathf.Lerp(0.22f, 0.38f, Mathf.Sqrt(k));
            block.SetColor("_Color", new Color(1f, 1f, 1f, fade));
            quadRenderer.SetPropertyBlock(block);
            glowLight.intensity = 4f * fade;
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
