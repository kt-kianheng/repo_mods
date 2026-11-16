using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace HatterWeaponsPack
{
    /// <summary>The ice wall's footprint on the floor: a rectangle across the holder's view (their screen only).</summary>
    internal class WallMarker : FloorMarker
    {
        private LineRenderer outline, inner;
        private float age;

        internal static WallMarker Create()
        {
            var go = new GameObject("HWP Ice Wall Marker");
            var m = go.AddComponent<WallMarker>();
            m.outline = m.Line("Outline", 4, 0.045f, new Color(0.55f, 0.92f, 1f, 0.95f));
            m.inner = m.Line("Inner", 4, 0.018f, new Color(0.8f, 0.97f, 1f, 0.45f));
            go.SetActive(false);
            return m;
        }

        internal override void Show(Vector3 point, Vector3 normal, float yaw)
        {
            Appear();
            // flat on the floor, long side across the holder's view (local X), short side along it (local Y here)
            transform.SetPositionAndRotation(point + Vector3.up * 0.03f, Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f));
            float halfLength = Settings.FrostStaffWallLength.Value * 0.5f, halfWidth = Settings.FrostStaffWallThickness.Value * 0.5f;
            float pulse = 1f + 0.02f * Mathf.Sin(age * 6f);
            Corners(outline, halfLength * pulse, halfWidth * pulse);
            Corners(inner, halfLength - 0.08f, Mathf.Max(0.02f, halfWidth - 0.08f));
        }

        private static void Corners(LineRenderer lr, float x, float y)
        {
            lr.SetPosition(0, new Vector3(-x, -y, 0f));
            lr.SetPosition(1, new Vector3(x, -y, 0f));
            lr.SetPosition(2, new Vector3(x, y, 0f));
            lr.SetPosition(3, new Vector3(-x, y, 0f));
        }

        private void Update() => age += Time.deltaTime;
    }

    /// <summary>
    /// The Frost Staff's ice wall: WallLength long (across the caster's view), WallThickness thick, made of WallSegments
    /// upright blocks of ice side by side, each WallHeight tall give or take WallHeightVariation. The blocks burst up out
    /// of the floor one after another from the caster's left to their right, then stand for WallSeconds before the wall
    /// shatters. Every client builds the same wall from the host's position and facing (the block heights come from a
    /// seed made of those, so they match). Each block has a solid collider (players, enemies, items and projectiles all
    /// stop at it), and a carving NavMeshObstacle makes enemies path round the whole wall. Enemies standing where it
    /// rises are encased in ice and frozen until it breaks, then slowed for SlowSeconds (see TrapEnemies, Break).
    /// </summary>
    internal class IceWall : MonoBehaviour
    {
        private const float RiseSeconds = 0.3f, SinkSeconds = 0.35f, Gap = 0.03f;
        private static readonly Color IceDeep = new Color(0.35f, 0.72f, 0.95f, 0.8f);
        private static readonly Color IceLight = new Color(0.82f, 0.96f, 1f, 0.55f);

        private float length, thickness, height, duration, stagger, age, brokeAt;
        private bool broken;
        private Transform[] blocks;
        private float[] heights;
        private BoxCollider[] colliders;
        private bool[] risen;
        private NavMeshObstacle obstacle;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private Light glow;
        private ParticleSystem mist;
        private Sound rise, shatter;
        // enemies caught where the wall rose: encased in ice and frozen until it breaks, then slowed
        private readonly List<Enemy> trapped = new List<Enemy>();
        private readonly List<IceEncasement> casings = new List<IceEncasement>();
        private bool authority;
        private float nextRestun;
        private static readonly List<IceWall> walls = new List<IceWall>();

        /// <summary>Is a standing ice wall holding this enemy?</summary>
        internal static bool Holds(Enemy e)
        {
            foreach (IceWall w in walls) if (w != null && !w.broken && w.trapped.Contains(e)) return true;
            return false;
        }

        /// <summary>Fire thawed this enemy: the walls let it go (no more re-stuns, and it isn't slowed when they break).</summary>
        internal static void Release(Enemy e)
        {
            foreach (IceWall w in walls) if (w != null) w.trapped.Remove(e);
        }

        internal static void Spawn(Vector3 floor, float yaw, Sound rise, Sound shatter)
        {
            var go = new GameObject("HWP Ice Wall");
            go.transform.SetPositionAndRotation(floor, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<IceWall>().Setup(floor, yaw, rise, shatter);
        }

        private void Setup(Vector3 floor, float yaw, Sound rise, Sound shatter)
        {
            this.rise = rise;
            this.shatter = shatter;
            length = Settings.FrostStaffWallLength.Value;
            thickness = Settings.FrostStaffWallThickness.Value;
            height = Settings.FrostStaffWallHeight.Value;
            duration = Settings.FrostStaffWallSeconds.Value;
            int count = Mathf.Max(1, Settings.FrostStaffWallSegments.Value);
            float variation = Mathf.Max(0f, Settings.FrostStaffWallHeightVariation.Value);
            stagger = Mathf.Max(0f, Settings.FrostStaffWallRiseStagger.Value);

            // the same heights on every client: seeded from the wall's position and facing (sent by the host)
            int seed = unchecked(Mathf.RoundToInt(floor.x * 100f) * 73856093 ^ Mathf.RoundToInt(floor.z * 100f) * 19349663
                                 ^ Mathf.RoundToInt(floor.y * 100f) * 83492791 ^ Mathf.RoundToInt(yaw * 10f));
            var rng = new System.Random(seed);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            float width = length / count;
            blocks = new Transform[count];
            heights = new float[count];
            colliders = new BoxCollider[count];
            risen = new bool[count];
            for (int i = 0; i < count; i++)
            {
                heights[i] = Mathf.Max(0.3f, height + Range(-variation, variation));
                float x = -length / 2f + width * (i + 0.5f);   // i = 0 is the caster's left (local -X)
                blocks[i] = Block(i, x, width, heights[i], Range(-0.06f, 0.06f), rng);
            }

            // the enemies' paths go round the whole wall
            var nav = new GameObject("Nav Obstacle");
            nav.transform.SetParent(transform, false);
            obstacle = nav.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, height / 2f, 0f);
            obstacle.size = new Vector3(length, height, thickness);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;

            var lightObj = new GameObject("Cold Light");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.up * height * 0.5f;
            glow = lightObj.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(0.55f, 0.9f, 1f); glow.range = Mathf.Max(4f, length * 1.2f); glow.intensity = 0f;

            mist = Mist();
            authority = SemiFunc.IsMasterClientOrSingleplayer();
            TrapEnemies();
        }

        /// <summary>
        /// Every client: enemies inside the wall's footprint are encased in ice, and the wall's blocks pass through
        /// them (rather than lifting them). The host freezes them in place (the game's own freeze, synced to everyone)
        /// and stuns them so they can't act, until the wall breaks.
        /// </summary>
        private void TrapEnemies()
        {
            Vector3 centre = transform.TransformPoint(new Vector3(0f, height / 2f, 0f));
            Vector3 half = new Vector3(length / 2f, height / 2f + 0.3f, thickness / 2f);
            walls.Add(this);
            // the ice puts out burning players and dummies in it and patches of fire under it
            Elements.WallRises(centre, half, transform.rotation);
            var found = new HashSet<Enemy>();
            foreach (Collider c in Physics.OverlapBox(centre, half, transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                Enemy e = LightningTargets.Of(c);
                if (e != null && !LightningTargets.IsDead(e)) found.Add(e);
            }
            float hold = duration;
            foreach (Enemy e in found)
            {
                Elements.IceOn(e, force: true);   // a burning enemy goes out in steam
                trapped.Add(e);
                casings.Add(IceEncasement.Encase(e, shatter, 0f));   // stays until the wall breaks
                foreach (Collider body in LightningTargets.Body(e).GetComponentsInChildren<Collider>())
                    foreach (BoxCollider wall in colliders)
                        if (body != null && wall != null) Physics.IgnoreCollision(body, wall, true);
                if (!authority) continue;
                // the ice slams up into it
                LightningTargets.Hurt(e, Settings.FrostStaffWallImpactDamage.Value, Vector3.up);
                if (LightningTargets.Gone(e)) { Plugin.Log.LogInfo($"Frost Staff: the ice wall killed {LightningTargets.Name(e)}."); continue; }
                try { e.Freeze(hold); }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Ice wall freeze failed: {ex}"); }
                LightningTargets.Stun(e, hold);
                Plugin.Log.LogInfo($"Frost Staff: {LightningTargets.Name(e)} is frozen in the ice wall for {hold:0.#} s.");
            }
        }

        /// <summary>One upright block of ice: its own mesh, edges, foot spikes and collider, starting under the floor.</summary>
        private Transform Block(int index, float x, float width, float h, float shade, System.Random rng)
        {
            var block = new GameObject("Ice Block " + index).transform;
            block.SetParent(transform, false);
            block.localPosition = new Vector3(x, -h, 0f);

            float w = Mathf.Max(0.05f, width - Gap);   // a hairline gap between blocks so they read as separate slabs
            Mesh m = Box(w, h, thickness, shade);
            meshes.Add(m);
            var slab = new GameObject("Slab");
            slab.transform.SetParent(block, false);
            slab.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = slab.AddComponent<MeshRenderer>();
            if (FireballFx.Glow != null) mr.sharedMaterial = FireballFx.Glow;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // bright outline on the front and back faces, so each block reads against the room
            float hw = w / 2f, ht = thickness / 2f;
            Edge(block, "Front Edge", new[] { new Vector3(-hw, 0f, ht), new Vector3(-hw, h, ht), new Vector3(hw, h, ht), new Vector3(hw, 0f, ht) });
            Edge(block, "Back Edge", new[] { new Vector3(-hw, 0f, -ht), new Vector3(-hw, h, -ht), new Vector3(hw, h, -ht), new Vector3(hw, 0f, -ht) });

            // a couple of ice spikes leaning out from the foot, one each side
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 lean = (Vector3.up + Vector3.forward * side * 0.8f + Vector3.right * ((float)rng.NextDouble() - 0.5f) * 0.6f).normalized;
                Spike(block, new Vector3(((float)rng.NextDouble() - 0.5f) * w * 0.7f, 0.1f, side * thickness * 0.45f),
                      Quaternion.LookRotation(lean), 1.5f + (float)rng.NextDouble() * 0.9f);
            }

            // solid: a box on the Default layer, rising with the block
            var solid = new GameObject("Collider");
            solid.layer = LayerMask.NameToLayer("Default");
            solid.transform.SetParent(block, false);
            var box = solid.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, h / 2f, 0f);
            box.size = new Vector3(width, h, thickness);   // no gap in the collider: nothing slips between blocks
            colliders[index] = box;
            return block;
        }

        /// <summary>A box of ice with its foot at the origin, deeper blue at the bottom, lighter at the top.</summary>
        private static Mesh Box(float w, float h, float t, float shade)
        {
            float x = w / 2f, z = t / 2f;
            Color bottom = Tint(IceDeep, shade), top = Tint(IceLight, shade), cap = Tint(IceLight, shade + 0.08f);
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                colors.Add(ca); colors.Add(cb); colors.Add(cc); colors.Add(cd);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
            Quad(new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(x, h, z), new Vector3(-x, h, z), bottom, bottom, top, top);      // front
            Quad(new Vector3(x, 0, -z), new Vector3(-x, 0, -z), new Vector3(-x, h, -z), new Vector3(x, h, -z), bottom, bottom, top, top);  // back
            Quad(new Vector3(-x, 0, -z), new Vector3(-x, 0, z), new Vector3(-x, h, z), new Vector3(-x, h, -z), bottom, bottom, top, top);  // left
            Quad(new Vector3(x, 0, z), new Vector3(x, 0, -z), new Vector3(x, h, -z), new Vector3(x, h, z), bottom, bottom, top, top);      // right
            Quad(new Vector3(-x, h, z), new Vector3(x, h, z), new Vector3(x, h, -z), new Vector3(-x, h, -z), cap, cap, cap, cap);          // top
            var m = new Mesh { name = "HWP Ice Block" };
            m.SetVertices(verts);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Color Tint(Color c, float s) =>
            new Color(Mathf.Clamp01(c.r + s), Mathf.Clamp01(c.g + s), Mathf.Clamp01(c.b + s * 0.5f), c.a);

        private static void Spike(Transform parent, Vector3 at, Quaternion rotation, float scale)
        {
            GameObject spike = Icicle.CreateVisual("Ice Spike");
            Light l = spike.GetComponent<Light>();
            if (l != null) Destroy(l);                                  // one light for the whole wall
            spike.transform.SetParent(parent, false);
            spike.transform.localPosition = at;
            spike.transform.localRotation = rotation;
            spike.transform.localScale = Vector3.one * scale;
        }

        private static void Edge(Transform parent, string name, Vector3[] points)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var lr = obj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = points.Length;
            lr.SetPositions(points);
            lr.widthMultiplier = 0.025f;
            if (FireballFx.Glow != null) lr.sharedMaterial = FireballFx.Glow;
            lr.startColor = lr.endColor = new Color(0.9f, 0.98f, 1f, 0.7f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Cold mist creeping along the foot of the wall.</summary>
        private ParticleSystem Mist()
        {
            var obj = new GameObject("Mist");
            obj.transform.SetParent(transform, false);
            obj.transform.localPosition = Vector3.up * 0.15f;
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.95f, 1f, 0.16f), new Color(0.65f, 0.9f, 1f, 0.1f));
            main.gravityModifier = -0.005f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var emission = ps.emission; emission.rateOverTime = 10f * length;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(length + 0.6f, 0.2f, thickness + 0.8f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            Material soft = SoftGlow.SharedMaterial;
            pr.sharedMaterial = soft != null ? soft : FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (!broken && age >= duration) Break();

            // host: keep the trapped enemies stunned while the wall stands (the game never shortens a stun this way)
            if (!broken && authority && age >= nextRestun && trapped.Count > 0)
            {
                nextRestun = age + 0.5f;
                foreach (Enemy e in trapped) if (!LightningTargets.Gone(e)) LightningTargets.Stun(e, duration - age);
            }

            if (!broken)
            {
                // left to right: block i starts rising i x stagger after the cast
                float up = 0f;
                for (int i = 0; i < blocks.Length; i++)
                {
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - i * stagger) / RiseSeconds));
                    blocks[i].localPosition = new Vector3(blocks[i].localPosition.x, -heights[i] * (1f - k), 0f);
                    if (k > 0f && !risen[i])
                    {
                        risen[i] = true;
                        Vector3 foot = transform.TransformPoint(new Vector3(blocks[i].localPosition.x, 0.1f, 0f));
                        IceShatter.Spawn(foot);
                        if (rise != null && (i == 0 || i % 3 == 0))   // a rumble of ice as it goes, not one per block
                        {
                            try { rise.Play(foot + Vector3.up, 1f, 1f, 1f, 1f); }
                            catch (System.Exception e) { Plugin.Log.LogWarning($"Ice wall sound failed: {e}"); rise = null; }
                        }
                    }
                    up += k;
                }
                glow.intensity = 1.6f * (up / blocks.Length) * (1f + 0.08f * Mathf.Sin(age * 3f));
                return;
            }

            // shattered: what's left drops back into the floor
            float t = Mathf.Clamp01((age - brokeAt) / SinkSeconds);
            for (int i = 0; i < blocks.Length; i++)
            {
                if (!blocks[i].gameObject.activeSelf) continue;
                blocks[i].localPosition = new Vector3(blocks[i].localPosition.x, -heights[i] * t * t, 0f);
                if (t >= 1f) blocks[i].gameObject.SetActive(false);
            }
            glow.intensity = 1.6f * (1f - t);
            if (age - brokeAt >= SinkSeconds + 3f) Destroy(gameObject);   // mist clears
        }

        private void Break()
        {
            broken = true;
            brokeAt = age;
            foreach (BoxCollider c in colliders) if (c != null) c.enabled = false;
            obstacle.enabled = false;
            var mistEmission = mist.emission; mistEmission.enabled = false;
            for (int i = 0; i < blocks.Length; i++)
            {
                float x = blocks[i].localPosition.x;
                IceShatter.Spawn(transform.TransformPoint(new Vector3(x, heights[i] * 0.3f, 0f)));
                IceShatter.Spawn(transform.TransformPoint(new Vector3(x, heights[i] * 0.75f, 0f)));
            }
            if (shatter != null)
            {
                try { shatter.Play(transform.position + Vector3.up * height * 0.5f, 1f, 1f, 1f, 1f); }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Ice wall sound failed: {ex}"); }
            }

            // the trapped enemies break free, chilled: frost drifts off them while they're slowed
            float slowSeconds = Settings.FrostStaffSlowSeconds.Value;
            foreach (IceEncasement c in casings) if (c != null) c.Shatter();
            foreach (Enemy e in trapped)
            {
                if (e == null || LightningTargets.IsDead(e)) continue;
                FrostEffect.Apply(e, slowSeconds);
                if (!authority) continue;
                try { e.Freeze(0f); }   // thaw now, in step with the wall
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Ice wall thaw failed: {ex}"); }
                EnemySlow.Apply(e, slowSeconds, Settings.FrostStaffSlowFactor.Value);
            }
        }

        private void OnDestroy()
        {
            walls.Remove(this);
            foreach (Mesh m in meshes) if (m != null) Destroy(m);
        }
    }
}
