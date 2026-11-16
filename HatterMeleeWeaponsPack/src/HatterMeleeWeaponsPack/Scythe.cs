using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// The Scythe. You hold the end of a long shaft; the blade sits at the far end, out to one side, its edge facing
    /// back towards you. It stays on that side however you move it; the holder presses Interact (E) to flip it over.
    ///
    /// Only a pull cuts: an enemy has to be between you and the blade, and the blade has to move towards you through
    /// them, by pulling the scythe back or by backing away while it's hooked behind them. Pushing it, swinging it
    /// sideways or bumping into things does nothing.
    ///
    /// Host only, each physics step while someone holds it: everything inside the cutting zone (a box on the edge's
    /// side of the blade, between the blade and the shaft, turning with the blade) is checked. A cut's strength is the
    /// blade's speed towards you, relative to the target: a pull of at least MinPullSpeed is a cut, for a fixed
    /// Damage, and uses one battery bar (Uses cuts per full charge). Cuts drag the enemy towards you a little. Training dummies (TargetDummy mod) get the hit through
    /// SendMessageUpwards("TargetDummyHit"); other players are hurt too (friendly fire), never the holder.
    ///
    /// Scrolling: the bat's little jerk when you scroll the scythe further out is switched off; scrolling it back in
    /// still jerks it (towards you, a handy pull) with a sword swing sound; a cut that lands plays a sword slice.
    /// </summary>
    internal class Scythe : MonoBehaviour
    {
        // set by ScytheBuilder on the prefab (serialized, so they survive the game cloning it)
        public Transform model;               // the scythe's mesh
        public Transform bladeCollider;       // the blade's grab collider
        public Vector3 modelBottom;           // the model's origin (the shaft's end), in item space
        public Quaternion modelRotation;      // model -> item, with the blade hanging straight down
        public Vector3 bladeBoxCentre;        // the blade collider, in model space
        public Quaternion bladeBoxTilt;
        public Vector3 zoneCentre;            // the cutting zone, in model space
        public Vector3 zoneHalf;
        public Vector3 gripPoint;             // item space
        public Vector3 shaftAxis;             // item space, grip -> head
        public ItemMelee sliceTemplate;

        private const string SliceFile = "scythe_slice.pcm";   // "Sword Slice 2" by Dragon Studio (Pixabay, per the file name): a cut landing
        private const string SwingFile = "scythe_swing.pcm";   // "Sword Swing SFX" by oxidvideos (Pixabay, per the file name): the retract jerk
        private const float TurnSpeed = 540f;                  // degrees a second the blade turns round the shaft
        private static readonly Vector3 HeadInModel = new Vector3(0f, 1.95f, 0f);

        private static readonly FieldInfo EnemyHealthField = AccessTools.Field(typeof(Enemy), "Health");
        private static readonly FieldInfo EnemyBodyField = AccessTools.Field(typeof(EnemyRigidbody), "rb");   // internal
        private static readonly FieldInfo DisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo DeadField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");

        private PhysGrabObject physGrabObject;
        private PhotonView photonView;
        private Rigidbody body;
        private ItemMelee melee;
        private Sound slice, swingSound;
        private readonly Collider[] inZone = new Collider[48];
        private readonly Dictionary<Object, float> nextCut = new Dictionary<Object, float>();
        private readonly HashSet<PlayerAvatar> holders = new HashSet<PlayerAvatar>();
        private float nextLog;
        private ItemBattery battery;
        private ItemToggle toggle;

        // the blade's turn round the shaft: 0 = straight down, +90 / -90 = out to one side or the other
        private float roll = 90f, targetRoll = 90f;
        private bool sideKnown, lastToggle;

        private void Start()
        {
            physGrabObject = GetComponent<PhysGrabObject>();
            photonView = GetComponent<PhotonView>();
            body = physGrabObject != null && physGrabObject.rb != null ? physGrabObject.rb : GetComponent<Rigidbody>();
            melee = GetComponent<ItemMelee>();
            battery = GetComponent<ItemBattery>();
            toggle = GetComponent<ItemToggle>();
            if (body != null) body.centerOfMass = Balance;   // balance the long shaft near the hand, so it can be held level
            slice = MakeSound(SliceFile, 0.8f);
            swingSound = MakeSound(SwingFile, 0.8f);
            ApplyRoll();
        }

        private Vector3 Balance => gripPoint + shaftAxis * Settings.ScytheBalance.Value;

        /// <summary>Model -> item rotation with the blade turned `roll` degrees round the shaft.</summary>
        private Quaternion Rotation => Quaternion.AngleAxis(roll, shaftAxis) * modelRotation;

        // ---------------------------------------------------------------- the blade's side (E flips it)

        /// <summary>
        /// The blade's side follows the game's ItemToggle on the scythe (added by ScytheBuilder): it shows "[E]" after
        /// the name while held, reads Interact (E by default) for the holder and sends the new state to every player,
        /// so the host's cutting zone and every view agree. Off = the + side (the default), on = the - side. The blade
        /// otherwise stays put, however you move it.
        /// </summary>
        private void Update()
        {
            if (toggle != null && (!sideKnown || toggle.toggleState != lastToggle))
            {
                bool first = !sideKnown;
                sideKnown = true;
                lastToggle = toggle.toggleState;
                targetRoll = lastToggle ? -90f : 90f;
                if (first) { roll = targetRoll; ApplyRoll(); }
                else if (Settings.ScytheDebugLog.Value) Plugin.Log.LogInfo($"Scythe: blade flipped to the {(lastToggle ? "-" : "+")} side.");
            }
            if (!Mathf.Approximately(roll, targetRoll))
            {
                roll = Mathf.MoveTowards(roll, targetRoll, TurnSpeed * Time.deltaTime);
                ApplyRoll();
            }
        }

        /// <summary>Turns the model and the blade's collider round the shaft (the cutting zone follows in FixedUpdate).</summary>
        private void ApplyRoll()
        {
            Quaternion r = Rotation;
            if (model != null) { model.localPosition = modelBottom; model.localRotation = r; }
            if (bladeCollider != null)
            {
                bladeCollider.position = transform.TransformPoint(modelBottom + r * bladeBoxCentre);
                bladeCollider.rotation = transform.rotation * r * bladeBoxTilt;
            }
        }

        // ---------------------------------------------------------------- cutting (host)

        private void FixedUpdate()
        {
            if (body == null || physGrabObject == null) return;
            if (body.centerOfMass != Balance) body.centerOfMass = Balance;   // in case the game resets it

            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (physGrabObject.playerGrabbing == null || physGrabObject.playerGrabbing.Count == 0) return;

            holders.Clear();
            foreach (PhysGrabber g in physGrabObject.playerGrabbing) if (g != null && g.playerAvatar != null) holders.Add(g.playerAvatar);

            Quaternion r = Rotation;
            Vector3 centre = transform.TransformPoint(modelBottom + r * zoneCentre);
            Quaternion rotation = transform.rotation * r;
            Vector3 edge = -transform.TransformDirection(shaftAxis).normalized;   // the edge faces back down the shaft, towards the holder

            // the blade's sharp edge, in the world, and which way it faces (towards the shaft side of the blade)
            for (int k = 0; k < EdgeSamples; k++)
            {
                edgeWorld[k] = transform.TransformPoint(modelBottom + r * edgeModel[k]);
                edgeFacingWorld[k] = transform.TransformDirection(r * edgeNormalModel[k]);
            }

            // the zone box only narrows down the search; what counts is being right at the edge (the shaft and the
            // back of the blade never cut)
            int count = Physics.OverlapBoxNonAlloc(centre, zoneHalf, inZone, rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = inZone[i];
                if (c == null || c.GetComponentInParent<PhysGrabObject>() == physGrabObject) continue;
                EnemyRigidbody erb = c.GetComponentInParent<EnemyRigidbody>();
                Enemy enemy = erb != null ? erb.enemy : c.GetComponentInParent<Enemy>();
                bool dummy = enemy == null && c.name.StartsWith("Hitbox");   // TargetDummy's dummies
                if (enemy == null && !dummy) continue;
                if (!AtEdge(c, out Vector3 onEdge, out Vector3 point)) continue;

                Vector3 velocity = body.GetPointVelocity(onEdge);
                if (c.attachedRigidbody != null) velocity -= c.attachedRigidbody.GetPointVelocity(point);
                float pull = Vector3.Dot(velocity, edge);   // how fast the edge is coming towards you through them
                if (pull <= 0.3f) continue;
                Cut(enemy != null ? (Object)enemy : c.transform.root, enemy, erb, c, null, point, edge, pull);
            }

            if (Settings.ScytheFriendlyFire.Value) CheckPlayers(edge);
        }

        // ---- the edge: sample points along the blade's sharp side, in model space (see build_scythe.py) ----
        private const int EdgeSamples = 10;
        private const float EdgeReach = 0.22f;   // how close to the edge something must be to be cut
        private static readonly Vector3[] edgeModel = new Vector3[EdgeSamples];
        private static readonly Vector3[] edgeNormalModel = new Vector3[EdgeSamples];
        private readonly Vector3[] edgeWorld = new Vector3[EdgeSamples];
        private readonly Vector3[] edgeFacingWorld = new Vector3[EdgeSamples];

        static Scythe()
        {
            // the blade's spine leaves the head at (0, 1.965, 0) and runs out 1.02 m along +Z, dropping 0.30 m by the tip;
            // the edge is the blade's width away from the spine, on the shaft side. The first stretch next to the
            // shaft (t < 0.15) isn't counted, so the shaft and the binding never cut.
            for (int k = 0; k < EdgeSamples; k++)
            {
                float t = Mathf.Lerp(0.15f, 1f, k / (float)(EdgeSamples - 1));
                Vector3 spine = new Vector3(0f, 1.965f - 0.30f * t * t, 1.02f * t);
                Vector3 tangent = new Vector3(0f, -0.60f * t, 1.02f).normalized;
                Vector3 inward = Vector3.Cross(Vector3.right, tangent).normalized;
                if (inward.y > 0f) inward = -inward;                       // towards the grip, below the blade
                float width = 0.11f * Mathf.Pow(1f - t, 0.75f) + 0.004f;
                edgeModel[k] = spine + inward * width;
                edgeNormalModel[k] = inward;
            }
        }

        /// <summary>Whether this collider is within EdgeReach of the edge, on its sharp side.</summary>
        private bool AtEdge(Collider c, out Vector3 onEdge, out Vector3 point)
        {
            onEdge = point = default;
            float best = EdgeReach;
            bool found = false;
            bool meshConcave = c is MeshCollider mc && !mc.convex;
            for (int k = 0; k < EdgeSamples; k++)
            {
                Vector3 e = edgeWorld[k];
                Vector3 q = meshConcave ? c.bounds.ClosestPoint(e) : c.ClosestPoint(e);
                Vector3 d = q - e;
                float dist = d.magnitude;
                if (dist > best) continue;
                if (dist > 0.02f && Vector3.Dot(d / dist, edgeFacingWorld[k]) < -0.2f) continue;   // behind the blade
                best = dist; onEdge = e; point = q; found = true;
            }
            return found;
        }

        /// <summary>Other players (not the holder) whose body is at the blade's edge.</summary>
        private void CheckPlayers(Vector3 edge)
        {
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                // real players only: an inactive avatar is a stand-in (e.g. TargetDummy's Ally Dummy), whose own
                // hitboxes are already found by the zone check
                if (p == null || !p.gameObject.activeInHierarchy || holders.Contains(p) || p.playerHealth == null) continue;
                if (Flag(DisabledField, p) || Flag(DeadField, p)) continue;
                // the player as an upright capsule (radius 0.4 m, 0.4-1.2 m up): is any edge point inside its reach?
                Vector3 feet = p.transform.position;
                Vector3 nearest = default, chest = feet + Vector3.up;
                float best = 0.4f + EdgeReach;
                bool near = false;
                for (int k = 0; k < EdgeSamples; k++)
                {
                    Vector3 e = edgeWorld[k];
                    Vector3 onBody = feet + Vector3.up * Mathf.Clamp(e.y - feet.y, 0.4f, 1.2f);
                    float dist = Vector3.Distance(e, onBody);
                    if (dist < best) { best = dist; nearest = e; chest = onBody; near = true; }
                }
                if (!near) continue;
                float pull = Vector3.Dot(body.GetPointVelocity(nearest), edge);
                if (pull <= 0.3f) continue;
                Cut(p, null, null, null, p, chest, edge, pull);
            }
        }

        private void Cut(Object key, Enemy enemy, EnemyRigidbody erb, Collider target, PlayerAvatar player, Vector3 point, Vector3 edge, float pull)
        {
            if (nextCut.TryGetValue(key, out float t) && Time.time < t) return;
            string name = enemy != null ? enemy.name : player != null ? player.name : target != null ? target.name : "?";
            if (pull < Settings.ScytheSpeedLow.Value)
            {
                if (Settings.ScytheDebugLog.Value && pull >= Settings.ScytheSpeedLow.Value * 0.5f && Time.time >= nextLog)
                {
                    nextLog = Time.time + 0.3f;   // (keeps this from flooding the log)
                    Plugin.Log.LogInfo($"Scythe: {pull:0.0} m/s pull on {name} was too slow (MinPullSpeed {Settings.ScytheSpeedLow.Value:0.0}).");
                }
                return;
            }
            if (battery != null && battery.batteryLife <= 0f)
            {
                if (Settings.ScytheDebugLog.Value && Time.time >= nextLog) { nextLog = Time.time + 2f; Plugin.Log.LogInfo("Scythe: out of charge, no cut."); }
                return;
            }
            int damage = Settings.ScytheDamage.Value;
            nextCut[key] = Time.time + Settings.ScytheHitCooldown.Value;
            if (nextCut.Count > 64) nextCut.Clear();

            try
            {
                if (player != null)
                {
                    UseBar();
                    player.playerHealth.HurtOther(damage, point, false, -1, false);
                }
                else if (enemy != null)
                {
                    UseBar();
                    EnemyHealth health = EnemyHealthField?.GetValue(enemy) as EnemyHealth ?? enemy.GetComponentInChildren<EnemyHealth>();
                    if (health != null) health.Hurt(damage, edge);
                    Rigidbody enemyBody = erb != null ? EnemyBodyField?.GetValue(erb) as Rigidbody : null;
                    float drag = pull * Settings.ScytheHookPull.Value;
                    if (enemyBody != null && drag > 0f) enemyBody.AddForce(edge * drag, ForceMode.Impulse);   // hooked: dragged towards you
                }
                else
                {
                    UseBar();
                    target.SendMessageUpwards("TargetDummyHit",
                        new float[] { damage, edge.x, edge.y, edge.z, pull * Settings.ScytheHookPull.Value }, SendMessageOptions.DontRequireReceiver);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Scythe cut failed: {e.Message}"); }

            PlayCutSound(point);
            if (Settings.ScytheDebugLog.Value) Plugin.Log.LogInfo($"Scythe cut {name}: pulled at {pull:0.0} m/s, {damage} damage.");
        }

        /// <summary>Host: a cut that counted uses one battery bar (Uses bars per full charge), on every player's game.</summary>
        private void UseBar()
        {
            if (battery == null) return;
            if (SemiFunc.IsMultiplayer() && photonView != null) photonView.RPC(nameof(HMWPScytheUseRPC), RpcTarget.All);
            else RemoveBar();
        }

        [PunRPC]
        public void HMWPScytheUseRPC(PhotonMessageInfo info)
        {
            if (info.Sender != null && !info.Sender.IsMasterClient) return;
            RemoveBar();
        }

        private void RemoveBar()
        {
            if (battery == null) return;
            try { battery.RemoveFullBar(1); battery.OverrideBatteryShow(2f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Scythe: using a bar failed: {e.Message}"); }
        }

        /// <summary>Host: a cut landed; everyone hears the sword slice where it landed (the bat's hit sound is not used).</summary>
        private void PlayCutSound(Vector3 point)
        {
            if (SemiFunc.IsMultiplayer() && photonView != null) photonView.RPC(nameof(HMWPScytheSliceRPC), RpcTarget.All, point);
            else Play(slice, point);
        }

        [PunRPC]
        public void HMWPScytheSliceRPC(Vector3 point, PhotonMessageInfo info)
        {
            if (info.Sender != null && !info.Sender.IsMasterClient) return;
            Play(slice, point);
        }

        private static bool Flag(FieldInfo f, object o) => f != null && (bool)f.GetValue(o);

        // ---------------------------------------------------------------- the scroll jerk

        /// <summary>Host: the bat's scroll jerk is about to happen. Returns whether to let it.</summary>
        internal bool AllowScrollJerk(int direction)
        {
            if (direction > 0) return false;   // scrolling it further out: no jerk, no sound
            // scrolling it back in: keep the jerk, with a sword swing for everyone
            if (SemiFunc.IsMultiplayer() && photonView != null) photonView.RPC(nameof(HMWPScytheSwingRPC), RpcTarget.All);
            else Play(swingSound, HeadPosition);
            return true;
        }

        [PunRPC]
        public void HMWPScytheSwingRPC(PhotonMessageInfo info)
        {
            if (info.Sender != null && !info.Sender.IsMasterClient) return;
            Play(swingSound, HeadPosition);
        }

        private Vector3 HeadPosition => transform.TransformPoint(modelBottom + Rotation * HeadInModel);

        private static void Play(Sound sound, Vector3 at)
        {
            if (sound == null) return;
            try { sound.Play(at, 1f, 1f, 1f, 1f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Scythe sound failed: {e.Message}"); }
        }

        private Sound MakeSound(string file, float volume)
        {
            AudioClip clip = SoundLibrary.Get(file);
            ItemMelee source = sliceTemplate != null ? sliceTemplate : melee;
            if (clip == null || source == null || source.soundSwing == null) return null;
            var s = new Sound();
            Sound.CopySound(source.soundSwing, s);
            s.Sounds = new[] { clip };
            s.Volume = volume;
            s.VolumeRandom = 0f;
            s.Pitch = 1f;
            s.PitchRandom = 0.06f;
            return s;
        }
    }

    /// <summary>The bat's scroll jerk on the scythe: only when it's scrolled back in (and then with a sword slice).</summary>
    [HarmonyPatch(typeof(ItemMelee), "ScrollSwingStartRPC")]
    internal static class ScytheScrollPatch
    {
        private static bool Prefix(ItemMelee __instance, int _direction)
        {
            Scythe scythe = __instance != null ? __instance.GetComponent<Scythe>() : null;
            return scythe == null || scythe.AllowScrollJerk(_direction);
        }
    }
}
