using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// The katana's wave: a red crescent "blade beam", lying in the plane of the swing with its bulge leading and its
    /// tips trailing (as wide as the slash was long, see Katana; WaveThickness deep through the middle), with an upright glowing band
    /// (WaveHeight) along its cutting edge so it reads from behind too. It flies straight ahead at WaveSpeed for WaveLifetime
    /// seconds, passing through enemies and cutting each one once for WaveDamage. Every player's game flies its own
    /// copy for the looks; only the host's ("authority") deals damage. By default it breaks on walls, floors and doors.
    /// Training dummies (TargetDummy mod) get the hit through SendMessageUpwards("TargetDummyHit"). Other players are
    /// only hurt with WaveFriendlyFire on, and never the one who slashed.
    /// </summary>
    internal class KatanaWave : MonoBehaviour
    {
        private const float FadeIn = 0.06f, FadeOut = 0.25f, BreakFade = 0.15f;
        private static readonly Color Red = new Color(0.75f, 0.02f, 0.03f, 1f);   // dark blood red (the light, and the glow's tint)

        private static readonly FieldInfo EnemyHealthField = AccessTools.Field(typeof(Enemy), "Health");
        private static readonly FieldInfo EnemyBodyField = AccessTools.Field(typeof(EnemyRigidbody), "rb");   // internal
        private static readonly FieldInfo DisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo DeadField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static bool Flag(FieldInfo f, object o) => f != null && (bool)f.GetValue(o);

        private Mesh crescent;   // each wave's own (its width follows its slash), destroyed with it
        private static Material material;

        private bool authority;
        private PhysGrabObject self;
        private PlayerAvatar slasher;
        private float age, width, thickness, height, life, broken = -1f;
        private readonly HashSet<Object> alreadyHit = new HashSet<Object>();
        private readonly Collider[] found = new Collider[64];
        private MeshRenderer glow, core;
        private MaterialPropertyBlock block;
        private Light lightSource;

        internal static void Spawn(Vector3 position, Quaternion rotation, float span, bool authority, PhysGrabObject self, PlayerAvatar slasher)
        {
            var go = new GameObject("HMWP Katana Wave");
            go.transform.SetPositionAndRotation(position, rotation);
            var wave = go.AddComponent<KatanaWave>();
            wave.authority = authority;
            wave.self = self;
            wave.slasher = slasher;
            wave.width = Mathf.Max(0.2f, span);
            wave.thickness = Settings.KatanaWaveThickness.Value;
            wave.height = Settings.KatanaWaveHeight.Value;
            wave.life = Settings.KatanaWaveLifetime.Value;
            wave.block = new MaterialPropertyBlock();
            // the crescent, and a slightly bigger faint copy round it as a red glow
            Mesh mesh = wave.crescent = Crescent(wave.width, wave.thickness, wave.height);
            wave.glow = Layer(go.transform, "Glow", mesh, new Vector3(1.06f, 1.35f, 1.12f));
            wave.core = Layer(go.transform, "Core", mesh, Vector3.one);
            wave.lightSource = go.AddComponent<Light>();
            wave.lightSource.type = LightType.Point;
            wave.lightSource.color = Red;
            wave.lightSource.range = 5f;
            wave.lightSource.intensity = 0f;
            wave.UpdateLooks();
        }

        private static MeshRenderer Layer(Transform parent, string name, Mesh mesh, Vector3 scale)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            if (Material != null) r.sharedMaterial = Material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        // ---------------------------------------------------------------- moving and hitting

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (broken >= 0f) { broken += dt; if (broken >= BreakFade) Destroy(gameObject); return; }
            if (age >= life + FadeOut) { Destroy(gameObject); return; }

            Vector3 from = transform.position;
            Vector3 step = transform.forward * Settings.KatanaWaveSpeed.Value * dt;
            float length = step.magnitude;
            if (Settings.KatanaWaveStopsAtWalls.Value && age > 0.05f &&
                Physics.Raycast(from, transform.forward, out RaycastHit wall, length, WallMask, QueryTriggerInteraction.Ignore))
            {
                length = wall.distance;
                step = transform.forward * length;
                broken = 0f;
            }
            Grow();
            if (authority && age <= life) HitAlong(from, step);
            transform.position = from + step;
        }

        /// <summary>Widens steadily from 1x at launch to WaveGrowth x at the end of its flight.</summary>
        private float Growth => Mathf.Lerp(1f, Settings.KatanaWaveGrowth.Value, Mathf.Clamp01(age / Mathf.Max(0.01f, life)));

        private void Grow() => transform.localScale = new Vector3(Growth, 1f, 1f);

        private static int WallMask => LayerMask.GetMask("Default", "StaticGrabObject", "PhysGrabObjectHinge");

        /// <summary>Host: everything in the box the wave sweeps through this step.</summary>
        private void HitAlong(Vector3 from, Vector3 step)
        {
            float length = step.magnitude;
            // the crescent's outline (from its front, at the wave's position, back to its tips: Depth deep; the band's
            // height) plus a little margin
            var half = new Vector3(width * Growth * 0.5f, Mathf.Max(height * 0.5f, 0.3f) + 0.2f, length * 0.5f + Depth * 0.5f + 0.2f);
            Vector3 centre = from + step * 0.5f + transform.forward * BackOffset(width, thickness);
            Vector3 dir = transform.forward;
            int damage = Settings.KatanaWaveDamage.Value;
            float push = Settings.KatanaWavePush.Value;

            int count = Physics.OverlapBoxNonAlloc(centre, half, found, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = found[i];
                if (c == null) continue;
                if (self != null && c.GetComponentInParent<PhysGrabObject>() == self) continue;
                EnemyRigidbody erb = c.GetComponentInParent<EnemyRigidbody>();
                Enemy enemy = erb != null ? erb.enemy : c.GetComponentInParent<Enemy>();
                try
                {
                    if (enemy != null)
                    {
                        if (!alreadyHit.Add(enemy)) continue;
                        EnemyHealth health = EnemyHealthField?.GetValue(enemy) as EnemyHealth ?? enemy.GetComponentInChildren<EnemyHealth>();
                        if (health != null && damage > 0) health.Hurt(damage, dir);
                        Rigidbody body = erb != null ? EnemyBodyField?.GetValue(erb) as Rigidbody : null;
                        if (body != null && push > 0f) body.AddForce(dir * push, ForceMode.Impulse);
                        Mark(c, centre, dir);
                        if (Settings.KatanaDebugLog.Value) Plugin.Log.LogInfo($"Katana wave hit {enemy.name}: {damage} damage.");
                    }
                    else if (c.name.StartsWith("Hitbox"))   // TargetDummy's dummies
                    {
                        if (!alreadyHit.Add(c.transform.root)) continue;
                        c.SendMessageUpwards("TargetDummyHit", new float[] { damage, dir.x, dir.y, dir.z, push }, SendMessageOptions.DontRequireReceiver);
                        Mark(c, centre, dir);
                        if (Settings.KatanaDebugLog.Value) Plugin.Log.LogInfo($"Katana wave hit dummy {c.transform.root.name}: {damage} damage.");
                    }
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Katana wave hit failed: {e.Message}"); }
            }

            if (Settings.KatanaWaveFriendlyFire.Value) HitPlayers(centre, half, damage);
        }

        /// <summary>Host: a red X on the side of what was hit that faces back along the wave (towards the slasher).</summary>
        private void Mark(Collider c, Vector3 centre, Vector3 dir)
        {
            Katana katana = self != null ? self.GetComponent<Katana>() : null;
            Vector3 from = centre - dir * 3f;
            Vector3 point = c is MeshCollider mc && !mc.convex ? c.bounds.ClosestPoint(from) : c.ClosestPoint(from);
            point -= dir * 0.08f;   // just in front of the surface, so it isn't hidden in it
            float size = Mathf.Clamp(c.bounds.size.magnitude * 0.6f, 0.6f, 1.4f);
            if (katana != null) katana.ShowImpact(point, size);
            else KatanaImpactMark.Spawn(point, size);
        }

        private void HitPlayers(Vector3 centre, Vector3 half, int damage)
        {
            Quaternion toLocal = Quaternion.Inverse(transform.rotation);
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                // real players only (an inactive avatar is a stand-in, e.g. TargetDummy's Ally Dummy, found by its hitboxes)
                if (p == null || p == slasher || !p.gameObject.activeInHierarchy || p.playerHealth == null) continue;
                if (Flag(DisabledField, p) || Flag(DeadField, p) || alreadyHit.Contains(p)) continue;
                // the player as three points up their body, each with 0.35 m of reach
                for (float up = 0.4f; up <= 1.6f; up += 0.6f)
                {
                    Vector3 point = p.transform.position + Vector3.up * up;
                    Vector3 local = toLocal * (point - centre);
                    if (Mathf.Abs(local.x) > half.x + 0.35f || Mathf.Abs(local.y) > half.y + 0.35f || Mathf.Abs(local.z) > half.z + 0.35f) continue;
                    alreadyHit.Add(p);
                    try { p.playerHealth.HurtOther(damage, point, false, -1, false); }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Katana wave player hit failed: {e.Message}"); }
                    if (Settings.KatanaDebugLog.Value) Plugin.Log.LogInfo($"Katana wave hit player {p.name}: {damage} damage.");
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- looks

        private void Update() => UpdateLooks();

        private void OnDestroy()
        {
            if (crescent != null) Destroy(crescent);
        }

        private void UpdateLooks()
        {
            float a = Mathf.Clamp01(age / FadeIn);
            if (age > life) a *= 1f - Mathf.Clamp01((age - life) / FadeOut);
            if (broken >= 0f) a *= 1f - Mathf.Clamp01(broken / BreakFade);
            float flicker = 0.92f + 0.08f * Mathf.Sin(age * 47f);
            block.SetColor("_Color", new Color(0.8f, 0.05f, 0.06f, 0.4f * a * flicker));   // the glow: faint, dark red
            if (glow != null) glow.SetPropertyBlock(block);
            block.SetColor("_Color", new Color(1f, 1f, 1f, a));                          // the crescent: its own vertex colours
            if (core != null) core.SetPropertyBlock(block);
            if (lightSource != null) lightSource.intensity = 3f * a;
        }

        // the crescent's middle line is a circular arc of ArcHalfAngle each side of the front, through both tips
        private const float ArcHalfAngle = 50f * Mathf.Deg2Rad;
        private static float ArcRadius(float width) => width * 0.5f / Mathf.Sin(ArcHalfAngle);
        // how far the arc's middle leads its tips; front to back, the whole crescent spans that plus its thickness
        private static float Lead(float width) => ArcRadius(width) * (1f - Mathf.Cos(ArcHalfAngle));
        private float Depth => Lead(width) + thickness * 0.5f;
        // where the cutting edge's middle (the wave's position) sits across the thickness (see the band, EdgeRow)
        private const float EdgeRow = 0.8f;
        // the crescent's middle, front to back, relative to the wave's position (it lies behind it, towards the tips)
        private static float BackOffset(float width, float thickness)
        {
            float front = thickness * 0.5f * (1f - EdgeRow);          // the front of its middle is just ahead of the cutting edge
            float back = -(Lead(width) + EdgeRow * thickness * 0.5f);  // its tips trail this far behind
            return (front + back) * 0.5f;
        }

        /// <summary>
        /// The wave, in metres, centred on the origin, flying along +Z with its tips along X:
        /// The middle of the cutting edge (where the band stands) is at the origin, so every wave, however wide, starts
        /// with its leading edge at the same spot; the rest of it trails back from there.
        ///  - a flat crescent in the XZ plane: its middle line a circular arc bulging forward through the tips at
        ///    x = ±width/2, the thickness measured across the arc (full in the middle, a point at each tip). Its colour
        ///    runs from a faint deep red inner (back) edge to a bright orange-red cutting edge near the front;
        ///  - an upright band standing on that cutting edge, height tall in the middle and tapering at the tips, bright
        ///    along its middle and fading up and down: that's what shows when you look at the wave from behind.
        /// Vertex colours, with Sprites/Default (unlit, see-through, both sides drawn).
        /// </summary>
        private static Mesh Crescent(float width, float thickness, float height)
        {
            const int segments = 48;
            // dark reds throughout: a near-black crimson back edge, a deep red body and a brighter (still red, not orange) cutting edge
            Color deep = new Color(0.3f, 0f, 0.02f), red = new Color(0.62f, 0.01f, 0.04f), hot = new Color(0.95f, 0.1f, 0.1f);
            // the flat crescent, inner (-1) to outer (+1): position, alpha, colour
            float[] rows = { -1f, -0.6f, -0.2f, 0.2f, 0.55f, 0.8f, 1f };
            float[] alpha = { 0f, 0.3f, 0.5f, 0.7f, 0.95f, 1f, 0f };
            Color[] colour = { deep, deep, red, red, Color.Lerp(red, hot, 0.5f), hot, red };
            // the upright band, bottom (-1) to top (+1), standing on the line at EdgeRow
            const float edgeRow = EdgeRow;
            float[] bandRows = { -1f, -0.45f, 0f, 0.45f, 1f };
            float[] bandAlpha = { 0f, 0.6f, 1f, 0.6f, 0f };
            Color[] bandColour = { red, Color.Lerp(red, hot, 0.5f), hot, Color.Lerp(red, hot, 0.5f), red };

            float radius = ArcRadius(width);
            float lead = Lead(width);
            // the cutting edge's middle at z = 0 (it sits EdgeRow of the way out across the thickness, at the arc's lead)
            float shiftZ = -(lead + edgeRow * thickness * 0.5f);
            var verts = new List<Vector3>();
            var colours = new List<Color>();
            var tris = new List<int>();

            void Strip(int rowCount, System.Func<float, int, Vector3> at, System.Func<float, int, Color> col)
            {
                int first = verts.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float t = s / (float)segments * 2f - 1f;   // -1 .. 1, tip to tip
                    for (int r = 0; r < rowCount; r++) { verts.Add(at(t, r)); colours.Add(col(t, r)); }
                }
                for (int s = 0; s < segments; s++)
                    for (int r = 0; r < rowCount - 1; r++)
                    {
                        int a = first + s * rowCount + r, b = a + 1, c = a + rowCount, d = c + 1;
                        tris.Add(a); tris.Add(c); tris.Add(b);
                        tris.Add(b); tris.Add(c); tris.Add(d);
                    }
            }
            // position on the flat crescent: t along it, v across it (-1 back .. 1 front)
            Vector3 OnCrescent(float t, float v)
            {
                float phi = t * ArcHalfAngle;
                // full in the middle, a point at each tip (clamped: cos 90° is a hair below zero in floats, and a
                // negative number to a fractional power is NaN, which once made the whole mesh invisible)
                float half = thickness * 0.5f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(t * Mathf.PI * 0.5f)), 0.9f);
                float r = radius + v * half;
                return new Vector3(Mathf.Sin(phi) * r, 0f, Mathf.Cos(phi) * r - radius * Mathf.Cos(ArcHalfAngle) + shiftZ);
            }
            float Taper(float t) => Mathf.Pow(Mathf.Max(0f, Mathf.Cos(t * Mathf.PI * 0.5f)), 0.7f);
            float TipFade(float t) => 1f - Mathf.Pow(Mathf.Abs(t), 6f);

            Strip(rows.Length,
                  (t, r) => OnCrescent(t, rows[r]),
                  (t, r) => { Color c = colour[r]; c.a = alpha[r] * TipFade(t); return c; });
            Strip(bandRows.Length,
                  (t, r) => OnCrescent(t, edgeRow) + Vector3.up * (bandRows[r] * height * 0.5f * Taper(t)),
                  (t, r) => { Color c = bandColour[r]; c.a = bandAlpha[r] * TipFade(t); return c; });

            var mesh = new Mesh { name = "HMWP Katana Wave" };
            mesh.SetVertices(verts);
            mesh.SetColors(colours);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            if (float.IsNaN(mesh.bounds.size.x) || float.IsNaN(mesh.bounds.size.z))
                Plugin.Log.LogWarning("Katana wave: the crescent mesh has invalid points; it won't be visible.");
            return mesh;
        }

        /// <summary>Sprites/Default: unlit, see-through, both sides drawn, tinted by vertex colour times _Color.</summary>
        private static Material Material
        {
            get
            {
                if (material != null) return material;
                Shader s = Shader.Find("Sprites/Default");
                if (s == null) { Plugin.Log.LogWarning("Katana wave: Sprites/Default shader not found; the wave will be invisible."); return null; }
                material = new Material(s) { name = "HMWP Katana Wave" };
                material.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return material;
            }
        }
    }
}
