using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// A soft glow sprite: a camera-facing quad with a radial gradient that is bright in the middle and fades to fully
    /// transparent at the edge, so it reads as light coming off something rather than a solid ball. Tint and
    /// strength are set per glow with SetColor (alpha = strength).
    /// </summary>
    internal class SoftGlow : MonoBehaviour
    {
        private static Texture2D texture;
        private static Material material;
        private MeshRenderer quad;
        private MaterialPropertyBlock block;
        private float metres;        // wanted diameter
        private Transform item;      // the item this glow sits on (null: a free-standing glow)

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                const int size = 64;
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HWP Soft Glow" };
                var px = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                        float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                        float a = Mathf.Pow(1f - r, 3f);                   // steep falloff: bright core, faint wide edge, zero at the rim
                        float core = Mathf.Pow(Mathf.Clamp01(1f - r / 0.2f), 2f);    // a whiter centre
                        px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a * 0.85f + core * 0.25f));
                    }
                texture.SetPixels(px);
                texture.Apply(false, true);
                Object.DontDestroyOnLoad(texture);
                Shader s = Shader.Find("Sprites/Default");
                if (s == null) return null;
                material = new Material(s) { mainTexture = texture, name = "HWP Soft Glow" };
                Object.DontDestroyOnLoad(material);
                return material;
            }
        }

        /// <summary>The soft round glow material (white, fading to clear at the edge), for particles.</summary>
        internal static Material SharedMaterial => Material;

        internal static SoftGlow Create(Transform parent, Vector3 localPosition, string name = "Glow")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var glow = go.AddComponent<SoftGlow>();
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(go.transform, false);
            glow.quad = q.GetComponent<MeshRenderer>();
            if (Material != null) glow.quad.sharedMaterial = Material;
            glow.quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow.quad.receiveShadows = false;
            glow.block = new MaterialPropertyBlock();
            PhysGrabObject owner = parent != null ? parent.GetComponentInParent<PhysGrabObject>() : null;
            if (owner != null) glow.item = owner.transform;
            glow.SetSize(0f);
            return glow;
        }

        /// <summary>Tint and strength (alpha) of the glow.</summary>
        internal void SetColor(Color c)
        {
            block.SetColor("_Color", c);
            quad.SetPropertyBlock(block);
        }

        /// <summary>Diameter of the glow in metres at the item's normal size.</summary>
        internal void SetSize(float metres)
        {
            this.metres = metres;
            Apply();
        }

        /// <summary>
        /// Sizes the quad every frame. The size used to be worked out once, against the parent's scale at that moment;
        /// the game shrinks items towards zero when they go into the inventory, so a glow resized then (say, when the
        /// reload finished) came out huge and blew up to metres across when the wand was taken out again. Now a glow
        /// on an item shrinks and grows with it and never gets bigger than its set size.
        /// </summary>
        private void Apply()
        {
            if (quad == null) return;
            float parentScale = transform.parent != null ? Mathf.Abs(transform.parent.lossyScale.x) : 1f;
            float itemScale = item != null ? Mathf.Clamp01(Mathf.Abs(item.lossyScale.x)) : 1f;
            float world = metres * itemScale;
            if (parentScale < 1e-4f || world <= 1e-4f) { quad.enabled = false; return; }
            quad.enabled = true;
            float s = world / parentScale;
            quad.transform.localScale = new Vector3(s, s, s);
        }

        private void LateUpdate()
        {
            Apply();
            Camera cam = Camera.main;
            if (cam != null) quad.transform.rotation = Quaternion.LookRotation(quad.transform.position - cam.transform.position, cam.transform.up);
        }
    }
}
