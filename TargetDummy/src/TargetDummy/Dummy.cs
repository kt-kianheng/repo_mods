using System.Collections.Generic;
using UnityEngine;

namespace TargetDummy
{
    internal enum DummyKind : byte { Target = 0, Ally = 1 }

    /// <summary>One hit, heal or health update on a dummy, as decided by the host and sent to everyone.</summary>
    internal struct HitInfo
    {
        internal enum Kind : byte { Hurt = 0, Push = 1, Impact = 2, Direct = 3, Heal = 4, State = 5 }

        public DummyKind Dummy;
        public Kind Type;
        public Vector3 Point;
        public Vector3 Direction;
        public int Damage;         // damage dealt, or the heal asked for
        public bool Kill;          // the hit would have killed outright, whatever the damage
        public float Knockback;
        public float Torque;
        public float Stun;         // seconds
        public float Freeze;       // seconds (Target) / tumble seconds (Ally)
        public float ImpactForce;  // thrown objects: the game's impact force
        public float Speed;        // thrown objects: m/s at impact
        // Ally Dummy only
        public int Health;         // health after this hit or heal
        public int MaxHealth;
        public int Effective;      // health actually lost or gained
        public bool WouldDie;      // the hit would have downed a real player
    }

    /// <summary>
    /// A training dummy: a wooden stand (base, post, crossbar) with one of the game's models hung on it.
    ///   Target Dummy: the Robe. Takes damage like an enemy, never dies, shows enemy damage numbers.
    ///   Ally Dummy:   the player droid. Takes friendly fire like a teammate and can be healed, with its own health
    ///                 pool (MaxHealth, 1000 by default) that never drops below 1.
    /// Hitboxes are static colliders on the Default layer, so bullets, projectiles and thrown objects stop on it but
    /// nothing can move, grab or kill it. Only the looks rock when it's hit.
    /// </summary>
    internal class Dummy : MonoBehaviour
    {
        internal static Dummy Target { get; private set; }
        internal static Dummy Ally { get; private set; }
        internal static Dummy Get(DummyKind kind) => kind == DummyKind.Ally ? Ally : Target;
        internal static IEnumerable<Dummy> All
        {
            get
            {
                if (Target != null) yield return Target;
                if (Ally != null) yield return Ally;
            }
        }

        private const string ModelFile = "target_dummy.mesh";
        private const float BaseTop = 0.6f;   // top of the stand's collar, where the post starts

        internal DummyKind Kind { get; private set; }
        internal bool IsAlly => Kind == DummyKind.Ally;

        private Transform upper;              // everything above the base: rocks when hit
        private Vector2 tilt, tiltVelocity;   // degrees about local X and Z
        private readonly List<Collider> hitboxes = new List<Collider>();
        private Bounds localBounds;

        /// <summary>Hurt zones that already hit this dummy, and when they may hit it again.</summary>
        internal readonly Dictionary<HurtCollider, float> NextHit = new Dictionary<HurtCollider, float>();

        // running total over the head
        private DamagePopup.Label total;
        private int totalDamage, totalHealed, totalHits;
        private float lastHitTime = -999f;

        // Ally Dummy health (the host's numbers are the real ones; everyone else mirrors them)
        internal int Health { get; private set; }
        internal int MaxHealth { get; private set; }
        internal Transform Chest { get; private set; }
        private HealthBar bar;
        private Collider bodyHitbox;
        private float lastChange, nextStateSync;

        internal Bounds WorldBounds { get; private set; }

        /// <param name="maxHeight">Headroom at the spot: the dummy shrinks to fit under a low ceiling.</param>
        internal static Dummy Create(DummyKind kind, Vector3 position, Quaternion rotation, Material template, Transform sceneAnchor, float maxHeight = 99f)
        {
            var root = new GameObject(kind == DummyKind.Ally ? "Ally Dummy" : "Target Dummy");
            if (sceneAnchor != null) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, sceneAnchor.gameObject.scene);
            root.layer = 0;
            root.transform.SetPositionAndRotation(position, Quaternion.identity);   // fitted upright, turned at the end
            var dummy = root.AddComponent<Dummy>();
            dummy.Kind = kind;
            if (kind == DummyKind.Ally) Ally = dummy; else Target = dummy;
            dummy.Build(template, maxHeight);
            root.transform.rotation = rotation;
            dummy.UpdateWorldBounds();
            return dummy;
        }

        private void Build(Material template, float maxHeight)
        {
            float height = IsAlly ? Settings.AllyTotalHeight.Value : Settings.TotalHeight.Value;
            float modelHeight = IsAlly ? Settings.DroidHeight.Value : Settings.RobeHeight.Value;
            if (maxHeight < height)
            {
                // low ceiling: shrink the whole dummy (the stand's base stays the same size)
                float k = Mathf.Max(1.6f, maxHeight) / height;
                Plugin.Log.LogInfo($"{name}: only {maxHeight:0.00} m of headroom, so it's {k * 100f:0}% of its usual height.");
                height *= k;
                modelHeight *= k;
            }
            modelHeight = Mathf.Min(modelHeight, height - BaseTop - 0.05f);
            float hem = height - modelHeight;
            float barFraction = IsAlly ? Settings.AllyCrossbarHeight.Value : Settings.CrossbarHeight.Value;
            float yaw = IsAlly ? Settings.DroidYaw.Value : Settings.RobeYaw.Value;
            BorrowedModel source = IsAlly ? BorrowedModel.Droid : BorrowedModel.Robe;

            // stand base stays put; the rest hangs off a pivot at the collar so it can rock
            var baseModel = MeshLibrary.Get(ModelFile, "TD_Base");
            if (baseModel != null) MeshLibrary.Attach(baseModel, transform, "Base", template);
            upper = new GameObject("Upper").transform;
            upper.SetParent(transform, false);
            upper.localPosition = new Vector3(0f, BaseTop, 0f);

            // the model, scaled to modelHeight and centred over the post with its bottom at `hem`
            Vector3 size = new Vector3(0.9f, modelHeight, 0.6f);
            GameObject model = source.Spawn(upper);
            if (model != null)
            {
                model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                model.SetActive(true);
                if (RendererBounds(model, out Bounds b) && b.size.y > 0.01f)
                {
                    model.transform.localScale = Vector3.one * (modelHeight / b.size.y);
                    RendererBounds(model, out b);
                    Vector3 local = transform.InverseTransformPoint(b.center);   // root is upright here: axes line up
                    float bottom = local.y - b.extents.y;
                    model.transform.position += new Vector3(-local.x, hem - bottom, -local.z);
                    size = b.size;
                }
                else Plugin.Log.LogWarning("Couldn't measure the dummy's model; it may sit at the wrong height.");
            }

            // post from the collar up into the model, crossbar across its shoulders
            float postTop = hem + modelHeight * 0.8f;
            float barY = hem + modelHeight * barFraction;
            float barLength = Mathf.Clamp(size.x * 1.1f, 1.3f, 3.2f);
            var postModel = MeshLibrary.Get(ModelFile, "TD_Post");
            if (postModel != null)
            {
                var post = MeshLibrary.Attach(postModel, upper, "Post", template);
                post.transform.localScale = new Vector3(1f, postTop - BaseTop, 1f);
            }
            var barModel = MeshLibrary.Get(ModelFile, "TD_Crossbar");
            if (barModel != null)
            {
                var crossbar = MeshLibrary.Attach(barModel, upper, "Crossbar", template);
                crossbar.transform.localPosition = new Vector3(0f, barY - BaseTop, 0f);
                crossbar.transform.localScale = new Vector3(barLength, 1f, 1f);
            }

            // where heals aim (moves with the rocking)
            Chest = new GameObject("Chest").transform;
            Chest.SetParent(upper, false);
            Chest.localPosition = new Vector3(0f, hem + modelHeight * 0.6f - BaseTop, 0f);

            // hitboxes (static, never move): body, crossbar, post, base
            float radius = Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.5f, 0.25f, 0.6f);
            var body = Hitbox("Hitbox Body").AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, hem + modelHeight / 2f, 0f);
            body.radius = radius;
            body.height = Mathf.Max(modelHeight, radius * 2f);
            body.direction = 1;
            hitboxes.Add(body);
            bodyHitbox = body;
            hitboxes.Add(Box("Hitbox Crossbar", new Vector3(0f, barY, 0f), new Vector3(barLength, 0.16f, 0.16f)));
            if (hem > BaseTop) hitboxes.Add(Box("Hitbox Post", new Vector3(0f, (BaseTop + hem) / 2f, 0f), new Vector3(0.14f, hem - BaseTop, 0.14f)));
            hitboxes.Add(Box("Hitbox Base", new Vector3(0f, BaseTop / 2f, 0f), new Vector3(0.9f, BaseTop, 0.9f)));

            localBounds = new Bounds(new Vector3(0f, height / 2f, 0f), new Vector3(Mathf.Max(barLength, 1.3f), height, Mathf.Max(size.z, 1.3f)));

            float labelY = height + 0.35f;
            if (IsAlly)
            {
                MaxHealth = Settings.AllyMaxHealth.Value;
                Health = Mathf.Clamp(Settings.AllyStartHealth.Value, 1, MaxHealth);
                PseudoPlayer.Create(this, new Vector3(0f, hem, 0f), bodyHitbox);
                if (Settings.AllyHealthBar.Value)
                {
                    bar = HealthBar.Create(transform, new Vector3(0f, height + 0.3f, 0f));
                    bar.Set(Health, MaxHealth, true);
                    labelY = height + 0.75f;
                }
            }
            total = DamagePopup.Persistent(transform, new Vector3(0f, labelY, 0f));
            Plugin.Log.LogInfo($"{name} built: {height:0.##} m tall, model {size.x:0.##} x {size.y:0.##} x {size.z:0.##} m, crossbar {barLength:0.##} m" +
                               (IsAlly ? $", {Health}/{MaxHealth} health." : "."));
        }

        private GameObject Hitbox(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = 0;
            return go;
        }

        private Collider Box(string name, Vector3 center, Vector3 size)
        {
            var box = Hitbox(name).AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            return box;
        }

        private static bool RendererBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        private void UpdateWorldBounds()
        {
            Bounds b = new Bounds(transform.position + Vector3.up * localBounds.center.y, Vector3.zero);
            foreach (Collider c in hitboxes) if (c != null) b.Encapsulate(c.bounds);
            WorldBounds = b;
        }

        internal bool Owns(Collider c) => c != null && hitboxes.Contains(c);

        /// <summary>The nearest point on the dummy to p (for placing numbers).</summary>
        internal Vector3 ClosestPoint(Vector3 p)
        {
            Vector3 best = WorldBounds.center;
            float bestD = float.MaxValue;
            foreach (Collider c in hitboxes)
            {
                if (c == null) continue;
                Vector3 q = c.ClosestPoint(p);
                float d = (q - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = q; }
            }
            return best;
        }

        // ---- host side ----

        private void LateUpdate()
        {
            // every frame, after the game has moved this frame's hurt zones into place
            if (SemiFunc.IsMasterClientOrSingleplayer()) HitWatch.Scan(this);
        }

        /// <summary>
        /// Called (through SendMessageUpwards) by other mods' projectiles that hit the dummy directly, e.g.
        /// HatterWeaponsPack: data = { damage, direction x, y, z, knockback (optional) }. Host only.
        /// On the Ally Dummy this is friendly fire.
        /// </summary>
        public void TargetDummyHit(float[] data)
        {
            if (data == null || data.Length < 1 || !SemiFunc.IsMasterClientOrSingleplayer()) return;
            Vector3 dir = data.Length >= 4 ? new Vector3(data[1], data[2], data[3]) : Vector3.zero;
            Report(new HitInfo
            {
                Type = HitInfo.Kind.Direct,
                Damage = Mathf.RoundToInt(data[0]),
                Direction = dir,
                Knockback = data.Length >= 5 ? data[4] : 0f,
                Point = ClosestPoint(WorldBounds.center - dir.normalized * 2f),
            });
        }

        /// <summary>Host: heals the Ally Dummy (healing items, the Healing Wand, other mods through AllyApi).</summary>
        internal void HostHeal(int amount, Vector3 from)
        {
            if (!IsAlly || amount <= 0 || !SemiFunc.IsMasterClientOrSingleplayer()) return;
            Vector3 dir = Chest.position - from;
            Report(new HitInfo
            {
                Type = HitInfo.Kind.Heal,
                Damage = amount,
                Point = Chest.position,
                Direction = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.zero,
            });
        }

        /// <summary>Host: settles the numbers (Robe resistance, or the Ally Dummy's health), then tells everyone.</summary>
        internal void Report(HitInfo hit)
        {
            hit.Dummy = Kind;
            if (!IsAlly)
            {
                if (Settings.ApplyRobeResistance.Value && hit.Type != HitInfo.Kind.Impact)
                    hit.Damage = (int)(hit.Damage * (1f - Mathf.Min(BorrowedModel.RobeDamageResistance, 1f)));
            }
            else
            {
                int before = Health;
                if (hit.Type == HitInfo.Kind.Heal)
                {
                    Health = Mathf.Min(MaxHealth, Health + hit.Damage);
                }
                else
                {
                    int damage = hit.Kill ? before : hit.Damage;
                    hit.WouldDie = hit.Kill || (damage > 0 && before - damage <= 0);
                    Health = Mathf.Max(1, before - damage);   // never below 1
                }
                PseudoPlayer.SyncHealth(Health, MaxHealth);
                hit.Effective = Mathf.Abs(Health - before);
                hit.Health = Health;
                hit.MaxHealth = MaxHealth;
                lastChange = Time.time;
            }
            if (Settings.DebugLog.Value && hit.Type != HitInfo.Kind.Hurt && hit.Type != HitInfo.Kind.Push)
                Plugin.Log.LogInfo($"{name}: {hit.Type} {hit.Damage}" + (IsAlly ? $", health now {Health}/{MaxHealth}." : "."));
            Net.SendHit(hit);
        }

        private void HostAllyUpkeep()
        {
            float reset = Settings.AllyResetAfter.Value;
            int start = Mathf.Clamp(Settings.AllyStartHealth.Value, 1, MaxHealth);
            if (reset > 0f && Health != start && Time.time - lastChange > reset)
            {
                Health = start;
                PseudoPlayer.SyncHealth(Health, MaxHealth);
                lastChange = Time.time;
                nextStateSync = 0f;
            }
            // a light refresh now and then, so players who arrive late (or missed a packet) see the right health
            if (Time.time >= nextStateSync)
            {
                nextStateSync = Time.time + 3f;
                if (SemiFunc.IsMultiplayer())
                    Net.SendHit(new HitInfo { Dummy = Kind, Type = HitInfo.Kind.State, Health = Health, MaxHealth = MaxHealth });
                else if (bar != null) bar.Set(Health, MaxHealth, false);
            }
        }

        // ---- every client ----

        internal void Show(HitInfo hit)
        {
            if (IsAlly && hit.MaxHealth > 0)
            {
                Health = hit.Health;
                MaxHealth = hit.MaxHealth;
                PseudoPlayer.SyncHealth(Health, MaxHealth);
                if (bar != null) bar.Set(Health, MaxHealth, hit.Type == HitInfo.Kind.State);
            }
            if (hit.Type == HitInfo.Kind.State) return;

            Vector3 dir = hit.Direction; dir.y = 0f;
            if (hit.Type != HitInfo.Kind.Heal && dir.sqrMagnitude > 1e-4f && Settings.Wobble.Value > 0f)
            {
                Vector3 local = transform.InverseTransformDirection(dir.normalized);
                float kick = Mathf.Clamp(6f + hit.Knockback * 0.8f + hit.Torque * 0.2f + hit.Damage * 0.05f + hit.ImpactForce * 0.1f, 0f, 70f);
                tiltVelocity += new Vector2(local.z, -local.x) * kick * Settings.Wobble.Value;
            }

            if (hit.Type == HitInfo.Kind.Heal) totalHealed += IsAlly ? hit.Effective : hit.Damage;
            else totalDamage += hit.Damage;
            totalHits++;
            lastHitTime = Time.time;
            if (total != null && Settings.ShowTotal.Value) total.Set(TotalText(), Color.white);

            DamagePopup.SpawnHit(hit);
        }

        private string TotalText()
        {
            string hits = $"<size=60%>({totalHits} hit{(totalHits == 1 ? "" : "s")})</size>";
            if (!IsAlly) return $"<size=70%>Total</size> {totalDamage:N0}  {hits}";
            string s = "";
            if (totalDamage > 0) s += $"<color=#ff6a5c>-{totalDamage:N0}</color>  ";
            if (totalHealed > 0 || totalDamage == 0) s += $"<color=#7dff5a>+{totalHealed:N0}</color>  ";
            return s + hits;
        }

        private void Update()
        {
            // rock on a damped spring
            float dt = Time.deltaTime;
            tiltVelocity += (-70f * tilt - 7f * tiltVelocity) * dt;
            tilt += tiltVelocity * dt;
            tilt = Vector2.ClampMagnitude(tilt, 14f);
            if (upper != null) upper.localRotation = Quaternion.Euler(tilt.x, 0f, tilt.y);

            if (total != null && totalHits > 0 && Time.time - lastHitTime > 3f)
            {
                totalDamage = 0; totalHealed = 0; totalHits = 0;
                total.Fade();
            }

            if (IsAlly)
            {
                Camera cam = Camera.main;
                PseudoPlayer.UpdateVision(cam);
                if (SemiFunc.IsMasterClientOrSingleplayer()) HostAllyUpkeep();
            }
        }

        private void OnDestroy()
        {
            if (Target == this) Target = null;
            if (Ally == this) { Ally = null; PseudoPlayer.Forget(); }
        }
    }
}
