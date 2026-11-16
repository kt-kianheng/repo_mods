using System.Collections.Generic;
using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>The ring on the floor or wall where the inferno will erupt (the holder's screen only, while it's ready).</summary>
    internal class InfernoRing : FloorMarker
    {
        private const int Segments = 64;
        private LineRenderer outer, inner;
        private float age, radius;

        internal static InfernoRing Create(float radius)
        {
            var go = new GameObject("HWP Inferno Ring");
            var ring = go.AddComponent<InfernoRing>();
            ring.radius = radius;
            ring.outer = ring.Line("Outer", Segments, 0.045f, new Color(1f, 0.45f, 0.08f, 0.95f));
            ring.inner = ring.Line("Inner", Segments, 0.018f, new Color(1f, 0.75f, 0.3f, 0.5f));
            go.SetActive(false);
            return ring;
        }

        internal override void Show(Vector3 point, Vector3 normal, float yaw)
        {
            Appear();
            // ring in the local XY plane; local Z along the floor's normal
            transform.SetPositionAndRotation(point + normal * 0.03f, Quaternion.LookRotation(normal));
            float pulse = 1f + 0.03f * Mathf.Sin(age * 6f);
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                outer.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius * pulse);
                float b = a + age * 0.8f;
                inner.SetPosition(i, new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f) * radius * 0.86f);
            }
        }

        private void Update() => age += Time.deltaTime;
    }

    /// <summary>
    /// The Ember Staff's inferno: a pillar of fire on the floor, InfernoRadius wide, reaching the ceiling (or
    /// InfernoMaxHeight in the open). Cast on a wall, the same pillar shoots out sideways from the wall, along its normal,
    /// to whatever it meets across the room (built the same way, turned so its "up" is the wall's normal). It lasts
    /// InfernoSeconds. Every client shows it; the host's copy does the damage every
    /// half second: enemies inside take InfernoEnemyDamagePerSecond, players inside (the caster too)
    /// InfernoPlayerDamagePerSecond.
    /// </summary>
    internal class Inferno : MonoBehaviour
    {
        private const float Tick = 0.5f;
        private const float RiseSeconds = 0.35f, FadeSeconds = 0.7f;
        private const int Segments = 40;

        private bool authority;
        private float age, duration, radius, height, nextTick = 0.25f, nextBurn, nextThaw;
        private Vector3 floor;
        private Vector3 axis = Vector3.up;             // the way the pillar grows: up from the floor, or out from a wall
        private Sound burn;
        private AudioSource burnSource;
        private float burnUntil;                       // Time.time the current burning sound ends
        private Transform outerTube, innerTube;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private ParticleSystem flames, core, sparks;
        private LineRenderer baseRing;
        private Light[] lights;
        private bool afterburnDone;
        private readonly Dictionary<PhysGrabObjectImpactDetector, float> nextBreak = new Dictionary<PhysGrabObjectImpactDetector, float>();

        /// <param name="cast">Played once as it erupts.</param>
        /// <param name="burning">Played for as long as it burns (again if it outlasts the clip; cut when it ends).</param>
        /// <param name="normal">The surface's normal: up for the floor, sideways for a wall.</param>
        internal static void Spawn(Vector3 floor, Vector3 normal, bool authority, Sound cast, Sound burning)
        {
            var go = new GameObject("HWP Inferno");
            var inferno = go.AddComponent<Inferno>();
            inferno.axis = normal.sqrMagnitude > 0.25f ? normal.normalized : Vector3.up;
            inferno.Setup(floor, authority, cast, burning);
        }

        private void Setup(Vector3 floorPoint, bool authority, Sound cast, Sound burning)
        {
            this.authority = authority;
            burn = burning;
            if (cast != null)
            {
                try { cast.Play(floorPoint + axis * 0.5f, 1f, 1f, 1f, 1f); }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Inferno cast sound failed: {ex}"); }
            }
            floor = floorPoint;
            duration = Settings.StaffInfernoSeconds.Value;
            radius = Settings.StaffRingRadius.Value;
            transform.SetPositionAndRotation(floor, Quaternion.FromToRotation(Vector3.up, axis));   // everything below is built along local up

            // up to the ceiling: the first thing straight above (walls, props), or the maximum height in the open
            height = Settings.StaffInfernoMaxHeight.Value;
            int world = LayerMask.GetMask("Default", "StaticGrabObject", "PhysGrabObjectHinge");
            if (Physics.Raycast(floor + axis * 0.1f, axis, out RaycastHit hit, height, world, QueryTriggerInteraction.Ignore))
                height = Mathf.Max(0.6f, hit.distance + 0.1f);

            // see-through tubes of fire: a wide orange one and a brighter yellow core, fading out going up
            outerTube = Tube("Outer Fire", radius, new Color(1f, 0.38f, 0.05f), new[] { 0.55f, 0.4f, 0.18f, 0f });
            innerTube = Tube("Core Fire", radius * 0.55f, new Color(1f, 0.8f, 0.3f), new[] { 0.6f, 0.38f, 0.12f, 0f });

            // rising flames: soft round puffs from the floor disc up to the top, white-yellow to orange to red.
            // More and bigger puffs for a wider pillar (the look was tuned at 0.8 m radius).
            float rise = Mathf.Max(2.5f, height / 1.1f);
            float density = Mathf.Clamp(Mathf.Pow(radius / 0.8f, 1.5f), 0.5f, 5f);
            float puff = Mathf.Clamp(0.5f + 0.5f * radius / 0.8f, 0.6f, 2.5f);
            flames = Flames("Flames", radius * 0.95f, rise, 0.35f * puff, 0.75f * puff, (120f + 30f * height) * density,
                            new Color(1f, 0.85f, 0.45f), new Color(1f, 0.45f, 0.08f), new Color(0.75f, 0.12f, 0.03f));
            core = Flames("Core Flames", radius * 0.45f, rise * 1.15f, 0.25f * puff, 0.5f * puff, (60f + 15f * height) * density,
                          new Color(1f, 0.97f, 0.8f), new Color(1f, 0.75f, 0.25f), new Color(1f, 0.35f, 0.05f));
            sparks = Embers(transform, radius, rise * 0.8f, rise * 1.3f, 0.015f, 0.04f);
            var sparkEmission = sparks.emission; sparkEmission.rateOverTime = (40f + 4f * height) * density;

            // a burning ring on the floor
            var ringObj = new GameObject("Floor Ring");
            ringObj.transform.SetParent(transform, false);
            baseRing = ringObj.AddComponent<LineRenderer>();
            baseRing.useWorldSpace = false;
            baseRing.loop = true;
            baseRing.positionCount = Segments;
            baseRing.alignment = LineAlignment.TransformZ;
            ringObj.transform.localRotation = Quaternion.LookRotation(Vector3.up);   // flat on the floor
            ringObj.transform.localPosition = Vector3.up * 0.03f;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                baseRing.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            }
            baseRing.widthMultiplier = 0.1f;
            if (FireballFx.Glow != null) baseRing.sharedMaterial = FireballFx.Glow;
            baseRing.startColor = baseRing.endColor = new Color(1f, 0.55f, 0.1f, 0.9f);
            baseRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringObj.transform.localScale = Vector3.one * radius;

            // warm flickering light up the pillar
            int lightCount = height > 4f ? 3 : 2;
            lights = new Light[lightCount];
            for (int i = 0; i < lightCount; i++)
            {
                var lo = new GameObject("Fire Light " + i);
                lo.transform.SetParent(transform, false);
                lo.transform.localPosition = Vector3.up * Mathf.Min(height - 0.2f, 0.4f + i * 1.8f);
                lights[i] = lo.AddComponent<Light>();
                lights[i].type = LightType.Point;
                lights[i].color = new Color(1f, 0.45f, 0.12f);
                lights[i].range = Mathf.Max(6f, radius * 4f);
                lights[i].intensity = 0f;
            }
            LateUpdate();
        }

        /// <summary>An open tube of the given radius, the inferno's height, fading out going up.</summary>
        private Transform Tube(string name, float r, Color color, float[] alphas)
        {
            float[] heights = { 0f, 0.15f, 0.5f, 1f };
            int rings = heights.Length, cols = Segments + 1;
            var verts = new Vector3[rings * cols];
            var colors = new Color[verts.Length];
            for (int ring = 0; ring < rings; ring++)
                for (int c = 0; c < cols; c++)
                {
                    float a = c * Mathf.PI * 2f / Segments;
                    verts[ring * cols + c] = new Vector3(Mathf.Cos(a) * r, heights[ring] * height, Mathf.Sin(a) * r);
                    colors[ring * cols + c] = new Color(color.r, color.g, color.b, alphas[ring]);
                }
            var tris = new int[(rings - 1) * Segments * 6];
            int t = 0;
            for (int ring = 0; ring < rings - 1; ring++)
                for (int c = 0; c < Segments; c++)
                {
                    int a = ring * cols + c, b = a + 1, d = a + cols, e = d + 1;
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    tris[t++] = b; tris[t++] = d; tris[t++] = e;
                }
            var mesh = new Mesh { name = "HWP " + name };
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            meshes.Add(mesh);

            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = obj.AddComponent<MeshRenderer>();
            if (FireballFx.Glow != null) mr.sharedMaterial = FireballFx.Glow;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            obj.transform.localScale = new Vector3(1f, 0f, 1f);
            return obj.transform;
        }

        /// <summary>Soft fire puffs rising from a disc on the floor, shrinking and reddening as they climb.</summary>
        private ParticleSystem Flames(string name, float discRadius, float speed, float minSize, float maxSize, float rate,
                                      Color hot, Color mid, Color cool)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            obj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // the shape emits along its +Z: point it up
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            float life = height / speed;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.75f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.85f, speed * 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            var emission = ps.emission; emission.rateOverTime = rate;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 2f; shape.radius = discRadius;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(hot, 0f), new GradientColorKey(mid, 0.35f), new GradientColorKey(cool, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.08f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.35f)));
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.2f; noise.scrollSpeed = 1.5f;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            Material soft = SoftGlow.SharedMaterial;
            pr.sharedMaterial = soft != null ? soft : FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>Little glowing embers rising (also used by the staff's "ready" aura).</summary>
        internal static ParticleSystem Embers(Transform parent, float discRadius, float minSpeed, float maxSpeed, float minSize, float maxSize)
        {
            var obj = new GameObject("Embers");
            obj.transform.SetParent(parent, false);
            obj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Max(0.05f, minSpeed), Mathf.Max(0.1f, maxSpeed));
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.05f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var emission = ps.emission; emission.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 15f; shape.radius = Mathf.Max(0.01f, discRadius);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var pr = obj.GetComponent<ParticleSystemRenderer>();
            if (FireballFx.Glow != null) pr.sharedMaterial = FireballFx.Glow;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= duration + FadeSeconds + 1.5f) { Destroy(gameObject); return; }   // extra time for the last puffs to clear

            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / RiseSeconds));
            float fade = Mathf.Clamp01((duration + FadeSeconds - age) / FadeSeconds);
            float k = rise * fade;
            float flicker = 1f + 0.06f * Mathf.Sin(age * 23f) + 0.04f * Mathf.Sin(age * 37f + 1f);
            outerTube.localScale = new Vector3(flicker, Mathf.Max(0.001f, k), flicker);
            innerTube.localScale = new Vector3(2f - flicker, Mathf.Max(0.001f, k * 0.9f), 2f - flicker);
            outerTube.Rotate(0f, 40f * Time.deltaTime, 0f, Space.Self);
            innerTube.Rotate(0f, -70f * Time.deltaTime, 0f, Space.Self);
            baseRing.widthMultiplier = 0.1f * k * flicker;
            for (int i = 0; i < lights.Length; i++)
                lights[i].intensity = (i == 0 ? 4f : 2.5f) * k * Random.Range(0.85f, 1.15f);
            if (age > duration)
            {
                foreach (ParticleSystem ps in new[] { flames, core, sparks })
                {
                    var e = ps.emission; e.enabled = false;
                }
                if (!afterburnDone) { afterburnDone = true; Afterburn(); }
            }

            if (burn != null && age < duration && age >= nextBurn)
            {
                AudioClip clip = burn.Sounds != null && burn.Sounds.Length > 0 ? burn.Sounds[0] : null;
                float length = clip != null ? clip.length : 2f;
                nextBurn = age + Mathf.Max(0.5f, length - 0.8f);   // the clip fades out over its last 0.8 s: overlap the next
                try
                {
                    burnSource = burn.Play(floor + axis * 1f, 1f, 1f, 1f, 1f);
                    burnUntil = Time.time + length;
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Inferno sound failed: {ex}"); burn = null; }
            }
            // burnt out before the sound finished: cut it as the flames die
            if (age > duration + FadeSeconds && burnSource != null)
            {
                if (Time.time < burnUntil - 0.05f && burnSource.isPlaying) burnSource.Stop();
                burnSource = null;
            }

            if (authority && age >= nextTick && age <= duration)
            {
                nextTick += Tick;
                Burn();
            }
            if (age >= nextThaw && age <= duration)
            {
                nextThaw = age + 0.25f;
                ThawInside();
            }
        }

        /// <summary>
        /// Every game, four times a second: frozen or slowed enemies in the pillar thaw in steam and catch fire
        /// (Elements). Enemies that aren't chilled don't catch fire from the pillar itself (unchanged).
        /// </summary>
        private void ThawInside()
        {
            var seen = new HashSet<Enemy>();
            foreach (Collider c in Physics.OverlapCapsule(floor, floor + axis * height, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                Enemy e = c != null ? LightningTargets.Of(c) : null;
                if (e == null || !seen.Add(e) || !Elements.IsChilled(e)) continue;
                Burning.Ignite(e);
            }
        }

        /// <summary>Host, every half second: hurt every enemy and player inside the pillar.</summary>
        private void Burn()
        {
            int enemyDamage = Mathf.RoundToInt(Settings.StaffInfernoEnemyDps.Value * Tick);
            int playerDamage = Mathf.RoundToInt(Settings.StaffInfernoAllyDps.Value * Tick);
            Vector3 top = floor + axis * height;

            var enemies = new HashSet<Enemy>();
            var others = new HashSet<Transform>();
            var valuables = new HashSet<PhysGrabObjectImpactDetector>();
            foreach (Collider c in Physics.OverlapCapsule(floor, top, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == null) continue;
                Enemy e = LightningTargets.Of(c);
                if (e != null) { enemies.Add(e); continue; }
                if (c.GetComponentInParent<PlayerAvatar>() != null) continue;
                ValuableObject v = c.GetComponentInParent<ValuableObject>();
                if (v != null)
                {
                    var detector = v.GetComponentInChildren<PhysGrabObjectImpactDetector>();
                    if (detector != null) valuables.Add(detector);
                    continue;
                }
                // anything else that wants to know (the TargetDummy mod's dummies): one message per object
                Transform root = c.attachedRigidbody != null ? c.attachedRigidbody.transform : c.transform.root;
                if (others.Add(root))
                    c.SendMessageUpwards("TargetDummyHit", new float[] { enemyDamage, 0f, 1f, 0f }, SendMessageOptions.DontRequireReceiver);
            }
            BurnValuables(valuables);
            foreach (Enemy e in enemies)
            {
                Vector3 away = LightningTargets.Centre(e) - floor;
                away -= axis * Vector3.Dot(away, axis);   // out from the pillar's middle line
                LightningTargets.Hurt(e, enemyDamage, away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.up);
            }

            if (playerDamage <= 0) return;
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (p == null || p.playerHealth == null) continue;
                // inside if the feet, the middle or the head is within the pillar (measured from its middle line)
                bool inside = false;
                foreach (float h in new[] { 0.2f, 0.9f, 1.6f })
                {
                    Vector3 d = p.transform.position + Vector3.up * h - floor;
                    float along = Vector3.Dot(d, axis);
                    if (along < -1f || along > height) continue;
                    if ((d - axis * along).magnitude <= radius + 0.3f) { inside = true; break; }
                }
                if (!inside) continue;
                ProjectileHits.HurtPlayer(p, playerDamage, p.transform.position + Vector3.up);
            }
        }

        /// <summary>
        /// Host: valuables in the fire take one of the game's own breaks (InfernoValuableDamage) every
        /// InfernoValuableDamageSeconds: value lost, the break effects and sound, as if knocked about.
        /// </summary>
        private void BurnValuables(HashSet<PhysGrabObjectImpactDetector> inside)
        {
            int level = Settings.StaffInfernoValuableBreak.Value;
            if (level <= 0) return;
            foreach (PhysGrabObjectImpactDetector d in inside)
            {
                if (d == null) continue;
                if (nextBreak.TryGetValue(d, out float due) && age < due) continue;
                nextBreak[d] = age + Mathf.Max(0.25f, Settings.StaffInfernoValuableBreakSeconds.Value);
                Vector3 at = d.transform.position;
                try
                {
                    if (level >= 3) d.BreakHeavy(at, true, 0f);
                    else if (level == 2) d.BreakMedium(at, true);
                    else d.BreakLight(at, true);
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Inferno: burning '{d.name}' failed: {ex}"); }
            }
        }

        /// <summary>
        /// Every game, as the inferno dies down: fire left on the floor (Ember Wand fire). On the floor, one patch the
        /// inferno's size; from a wall, a trail of patches along the floor under the pillar (where there's floor within
        /// reach below it).
        /// </summary>
        private void Afterburn()
        {
            float seconds = Settings.StaffInfernoAfterburnSeconds.Value;
            if (seconds <= 0f) return;
            try
            {
                if (axis.y >= 0.7f)
                {
                    FireArea.Spawn(floor + axis * 0.3f, burn, radius, seconds);
                    return;
                }
                float patch = Mathf.Clamp(radius * 0.6f, 0.6f, 2f);
                float step = patch * 1.4f;
                int count = Mathf.Clamp(Mathf.CeilToInt(height / step), 1, 10);
                for (int i = 0; i < count; i++)
                {
                    float t = Mathf.Min(height - 0.2f, patch * 0.8f + i * step);
                    FireArea.Spawn(floor + axis * Mathf.Max(0.3f, t), i == 0 ? burn : null, patch, seconds);
                }
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"Inferno afterburn failed: {ex}"); }
        }

        private void OnDestroy()
        {
            foreach (Mesh m in meshes) if (m != null) Destroy(m);
        }
    }
}
