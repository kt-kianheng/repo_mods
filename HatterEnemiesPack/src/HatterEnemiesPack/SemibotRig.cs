using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// A semibot body animated by the game's own player code, for a monster.
    ///
    /// How a teammate's (remote player's) semibot is drawn in the game: their PlayerAvatarVisuals object (meshes,
    /// the Animator, cosmetics, and the scripts PlayerAvatarVisuals, PlayerEyes, PlayerExpression,
    /// PlayerAvatarRightArm/LeftArm) reads their PlayerAvatar every frame (clientPositionCurrent/RotationCurrent,
    /// isMoving, isSprinting, isGrounded, isTumbling, rbVelocity, InputAnalogAmount, the camera's direction through
    /// PlayerLocalCamera.GetOverrideTransform = PlayerVisionTarget.VisionTransform, PhysGrabber.physGrabBeamActive...)
    /// and animates: Animator bools/triggers (Moving, Turning, Sprinting/SprintingImpulse, Jumping/JumpingImpulse,
    /// Falling, Tumbling/TumblingImpulse/TumblingMove, Grabbing), the lean/tilt/leg-twist springs, the head's up and
    /// side look, the pupils (PlayerEyes picks a player or held object to look at), the arms (the right one points at
    /// what the grab beam holds).
    ///
    /// Here those scripts run unchanged on a copy of a player's visuals. What they read is a stand-in PlayerAvatar
    /// (plus PlayerTumble, PhysGrabber, PhysGrabBeam, PhysGrabObject, PlayerLocalCamera, PlayerVisionTarget,
    /// PlayerHealth, MapToolController, FlashlightController) on a holder object that is never switched on, so none
    /// of the stand-ins' own code runs; this class writes their fields every frame from the monster's state.
    /// Errors from the game's scripts on the copy are caught and logged by SemibotGuard.
    /// </summary>
    internal class SemibotRig : MonoBehaviour
    {
        // ---- what the monster sets (read every frame) ----
        internal Vector3 Feet;                 // on the floor
        // where its "camera" points: yaw (world degrees) and pitch (positive = down). As for a real player, the body
        // always faces the camera's yaw (PlayerAvatar rotation = camera yaw; the game's bodySpring smooths the
        // turn) and only the head shows the pitch, so Yaw and LookYaw are the same thing.
        internal float LookYaw, LookPitch;
        internal float Yaw { get => LookYaw; set => LookYaw = value; }
        internal Vector3 Velocity;             // world, m/s
        internal bool Moving, Sprinting, Grounded = true;
        internal float Analog = 1f;            // like a stick: walk animation speed

        // ---- the game's scripts on the copy ----
        internal PlayerAvatarVisuals Visuals { get; private set; }
        /// <summary>The copy's Animator (PlayerAvatarVisuals.animator is internal; its Start takes the one on the same object).</summary>
        private Animator Anim => anim != null ? anim : (anim = Visuals != null ? Visuals.GetComponent<Animator>() : null);
        private Animator anim;
        internal PlayerAvatarRightArm RightArm { get; private set; }
        internal PlayerAvatar Source { get; private set; }
        internal PlayerAvatar Puppet => puppet;
        internal bool Tumbling => tumbling;
        internal Rigidbody TumbleBody => tumbleRb;
        internal Transform Eye => eye;
        /// <summary>The copy's PlayerEyes (the game's pupil code).</summary>
        internal PlayerEyes Eyes { get; private set; }
        /// <summary>The copy's flashlight (the game's FlashlightController), if any.</summary>
        internal FlashlightController Flashlight { get; private set; }
        internal Animator BodyAnimator => Anim;
        internal bool BeamOn => held != null || prop != null;
        /// <summary>The tumble ball's collider (the Alter Mimic's hitbox ignores it).</summary>
        internal Collider TumbleCollider => tumbleRb != null ? tumbleRb.GetComponent<Collider>() : null;
        /// <summary>Height of its eyes above its feet (from the copied player).</summary>
        internal float EyeHeight => eyeHeight;
        /// <summary>Every rig alive (SemibotEyesPatch passes the game's "everyone look here" calls on to them).</summary>
        internal static readonly List<SemibotRig> All = new List<SemibotRig>();

        private GameObject holder, visualsRoot;
        private PlayerAvatar puppet;
        private PlayerTumble tumble;
        private PhysGrabObject tumblePgo;
        private PhysGrabber grabber;
        private PhysGrabBeam beamFake;
        private Transform eye, grabPoint, pullerPoint, tumbleFollow;
        // the stand-in camera: PlayerLocalCamera.GetOverrideTransform() returns the camera's own transform (the
        // VisionTransform only during a camera override), and the head's up/down look and the eyes' direction come
        // from it, so it must carry the rig's camera yaw and pitch
        private Transform camT;
        private float eyeHeight = 1.45f;
        private bool tumbling;
        private Rigidbody tumbleRb;
        private LineRenderer beam;
        private Material beamMat;
        private Vector2 beamScroll = new Vector2(-2f, 0f);
        private PhysGrabObject held;
        private readonly List<Material> ownMaterials = new List<Material>();

        // the player's own sounds (PlayerAvatar: jump, land, tumble start/stop)
        private Sound jumpSound, landSound, tumbleStartSound, tumbleStopSound;
        // the idle pose: the Animator's state when it first stands still (jumps start from it)
        private int idleHash;
        private float builtAt, jumpAt = -1f;
        // tumble form: curls up first (with the tumble sound); the ball is launched once the tumble pose is reached
        private const float CurlMin = 0.25f, CurlMax = 0.9f;
        private float launchAt = -1f, curlStart;
        private int curlFromState;
        private float jumpLogAt = -1f;
        private Vector3 launchVelocity, launchSpin;
        private static readonly FieldInfo TumbleFollowLerp = AccessTools.Field(typeof(PlayerAvatarVisuals), "visualTumbleFollowLerp");

        // ---- building ----

        /// <summary>A rig copying <paramref name="source"/>'s look (their colours and cosmetics), standing at feet.</summary>
        internal static SemibotRig Create(PlayerAvatar source, Vector3 feet, float yaw)
        {
            if (source == null || source.playerAvatarVisuals == null) { Plugin.Log.LogWarning("Semibot: no player body to copy."); return null; }
            var go = new GameObject("Semibot Rig");
            var rig = go.AddComponent<SemibotRig>();
            rig.Feet = feet; rig.Yaw = rig.LookYaw = yaw;
            try { rig.Build(source); }
            catch (System.Exception e) { Plugin.Log.LogError($"Semibot: building the rig failed: {e}"); Destroy(go); return null; }
            return rig;
        }

        private void Build(PlayerAvatar source)
        {
            Source = source;
            transform.position = Feet;

            // the "camera" / vision point (VisionTransform): GetOverrideTransform returns it, the head looks its way
            eye = new GameObject("Semibot Eye").transform;
            eye.SetParent(transform, false);
            Transform srcEye = source.PlayerVisionTarget != null ? source.PlayerVisionTarget.VisionTransform : null;
            if (srcEye != null) eyeHeight = Mathf.Clamp(srcEye.position.y - source.transform.position.y, 0.8f, 2f);
            grabPoint = new GameObject("Semibot Grab Point").transform; grabPoint.SetParent(transform, false);
            pullerPoint = new GameObject("Semibot Puller Point").transform; pullerPoint.SetParent(transform, false);

            // ---- the stand-ins, on a holder that is never switched on (their own code never runs) ----
            holder = new GameObject("Semibot Stand-ins");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            puppet = holder.AddComponent<PlayerAvatar>();
            // (children are parented under the switched-off holder before their scripts are added, so no Awake runs)
            tumble = Child<PlayerTumble>("Tumble");
            tumblePgo = tumble.gameObject.AddComponent<PhysGrabObject>();
            // AnimationLogic reads tumble.physGrabObject.impactDetector.inCart whenever the ball moves > 1 m/s; without
            // one it threw every frame of a roll and never set 'Tumbling' (it curled up only once the ball slowed)
            Set(tumblePgo, "impactDetector", tumble.gameObject.GetComponent<PhysGrabObjectImpactDetector>() ?? tumble.gameObject.AddComponent<PhysGrabObjectImpactDetector>());
            grabber = Child<PhysGrabber>("Grabber");
            beamFake = Child<PhysGrabBeam>("Beam");
            var cam = Child<PlayerLocalCamera>("Camera");
            camT = cam.transform;
            var vision = Child<PlayerVisionTarget>("Vision");
            var health = holder.AddComponent<PlayerHealth>();
            var mapTool = Child<MapToolController>("MapTool");
            var flashlight = Child<FlashlightController>("Flashlight");
            var dummy = new GameObject("Dummy").transform; dummy.SetParent(holder.transform, false);
            // PlayerEyes.LookAtTransform checks playerAvatar.healthGrab.staticGrabObject.playerGrabbing every frame
            // (is someone grabbing this player?); without them it threw before ever picking what to look at
            var healthGrab = Child<PlayerHealthGrab>("HealthGrab");
            var staticGrab = healthGrab.gameObject.GetComponent<StaticGrabObject>() ?? healthGrab.gameObject.AddComponent<StaticGrabObject>();
            if (staticGrab.playerGrabbing == null) staticGrab.playerGrabbing = new List<PhysGrabber>();
            Set(healthGrab, "staticGrabObject", staticGrab);
            healthGrab.playerAvatar = puppet;
            puppet.healthGrab = healthGrab;

            // the tumble's follow point: where the curled-up body sits relative to the rolling ball (copied from the player's)
            tumbleFollow = new GameObject("Semibot Tumble Follow").transform;
            PlayerTumble srcTumble = F<PlayerTumble>(source, "tumble");
            Vector3 followLocal = Vector3.zero; Quaternion followRot = Quaternion.identity;
            if (srcTumble != null && srcTumble.followPosition != null)
            {
                followLocal = srcTumble.transform.InverseTransformPoint(srcTumble.followPosition.position);
                followRot = Quaternion.Inverse(srcTumble.transform.rotation) * srcTumble.followPosition.rotation;
            }

            vision.VisionTransform = eye;
            Set(cam, "playerAvatar", puppet);
            Set(health, "playerAvatar", puppet);
            Set(mapTool, "Active", false);
            mapTool.VisualTransform = dummy; mapTool.PlayerLookTarget = dummy;
            tumble.playerAvatar = puppet;
            tumble.followPosition = tumbleFollow;
            Set(tumble, "physGrabObject", tumblePgo);
            if (tumblePgo.playerGrabbing == null) tumblePgo.playerGrabbing = new List<PhysGrabber>();
            grabber.playerAvatar = puppet;
            grabber.physGrabBeam = beamFake.gameObject;
            grabber.physGrabBeamComponent = beamFake;
            grabber.physGrabPoint = grabPoint;
            beamFake.PhysGrabPoint = grabPoint;
            beamFake.PhysGrabPointPuller = pullerPoint;
            beamFake.PhysGrabPointOrigin = beamFake.PhysGrabPointOriginClient = beamFake.PhysGrabPointOriginLocal = dummy;

            puppet.photonView = holder.GetComponent<Photon.Pun.PhotonView>() ?? holder.AddComponent<Photon.Pun.PhotonView>();
            puppet.physGrabber = grabber;
            puppet.playerHealth = health;
            puppet.mapToolController = mapTool;
            puppet.flashlightController = flashlight;
            puppet.localCamera = cam;
            puppet.PlayerVisionTarget = vision;
            puppet.MaterialTrigger = new Materials.MaterialTrigger();
            Set(puppet, "tumble", tumble);
            Set(puppet, "isLocal", false);
            Set(puppet, "isDisabled", false);
            Set(puppet, "steamID", F<string>(source, "steamID") ?? "");   // speed upgrades: sprint animation speed
            Set(puppet, "investigateTimer", 999f);                          // its footsteps don't call enemies over

            // ---- the copy of the player's visuals (built switched off, so nothing runs before it's wired) ----
            PlayerAvatarVisuals src = source.playerAvatarVisuals;
            var temp = new GameObject("Semibot (building)");
            temp.SetActive(false);
            visualsRoot = Instantiate(src.gameObject, temp.transform);
            visualsRoot.name = "Semibot Visuals";
            visualsRoot.AddComponent<SemibotTag>().Rig = this;
            var removed = new List<string>();
            foreach (Component c in visualsRoot.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string n = c.GetType().Name;
                // networking, physics and the map tool: the copy is only a picture (the flashlight stays: the game's
                // FlashlightController holds it up in the left hand and puts it away while tumbling)
                if (c is Collider || c is Rigidbody || n.StartsWith("Photon") || n == "MapToolController"
                    || n == "PhysGrabBeam" || n == "PlayerVoiceChat" || n == "AudioListener"
                    || n == "CosmeticBlocked")   // hides cosmetics that clip into walls, using the colliders removed here
                {
                    removed.Add(n);
                    DestroyImmediate(c);
                }
            }

            // the flashlight: a teammate's sits in their visuals; the local player's was moved onto their camera
            // (FlashlightController.Start), so it's copied from the player into the copy. Every frame it moves itself
            // to FollowTransformClient (the left hand, mapped into the copy below) as it does for a teammate.
            FlashlightController ownLight = visualsRoot.GetComponentInChildren<FlashlightController>(true);
            string lightFrom = "the body";
            if (ownLight == null && source.flashlightController != null)
            {
                GameObject lightCopy = Instantiate(source.flashlightController.gameObject, visualsRoot.transform);
                lightCopy.name = "Semibot Flashlight";
                ownLight = lightCopy.GetComponent<FlashlightController>();
                if (ownLight != null) ownLight.mirrorClient = false;
                lightFrom = "the player (copied)";
            }
            if (ownLight == null) lightFrom = "none";
            Flashlight = ownLight;
            if (ownLight != null) puppet.flashlightController = ownLight;

            // runtime-only fields (not serialized, so Instantiate left them empty) copied from the player's scripts
            int runtimeCopied = CopyRuntimeFields(src.transform, visualsRoot.transform, source.transform);

            // every reference the copy's scripts hold to the real player's parts now points at the stand-ins
            int remapped = 0;
            var srcRoot = source.transform;
            foreach (MonoBehaviour mb in visualsRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                for (System.Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        object stand = f.FieldType == typeof(PlayerAvatar) ? puppet
                                     : f.FieldType == typeof(PhysGrabber) ? grabber
                                     : f.FieldType == typeof(PhysGrabBeam) ? beamFake
                                     : f.FieldType == typeof(PlayerTumble) ? tumble
                                     : f.FieldType == typeof(MapToolController) ? mapTool
                                     : f.FieldType == typeof(FlashlightController) ? (ownLight != null ? ownLight : flashlight)
                                     : f.FieldType == typeof(PlayerHealth) ? health
                                     : f.FieldType == typeof(PlayerHealthGrab) ? healthGrab
                                     : f.FieldType == typeof(PlayerLocalCamera) ? cam
                                     : f.FieldType == typeof(PlayerVisionTarget) ? (object)vision : null;
                        if (stand != null) { f.SetValue(mb, stand); remapped++; continue; }
                        // transforms inside the player's own visuals (e.g. the flashlight's FollowTransformClient, as
                        // the flashlight was copied separately): the same transform in the copy
                        if (f.FieldType == typeof(Transform) && f.GetValue(mb) is Transform inSrc && inSrc != null && inSrc.IsChildOf(src.transform) && !inSrc.IsChildOf(visualsRoot.transform))
                        {
                            string path = PathIn(src.transform, inSrc);
                            Transform same = path == null ? null : path.Length == 0 ? visualsRoot.transform : visualsRoot.transform.Find(path);
                            if (same != null) { f.SetValue(mb, same); remapped++; continue; }
                        }
                        // transforms outside the copy (on the player's own avatar): a stand-in at the same place
                        // relative to the rig (e.g. the grabber's target points, which the arm script then moves)
                        if (f.FieldType == typeof(Transform) && f.GetValue(mb) is Transform tr && tr != null && !tr.IsChildOf(visualsRoot.transform) && tr.IsChildOf(srcRoot))
                        {
                            var mirror = new GameObject($"Semibot ({tr.name})").transform;
                            mirror.SetParent(transform, false);
                            mirror.localPosition = srcRoot.InverseTransformPoint(tr.position);
                            mirror.localRotation = Quaternion.Inverse(srcRoot.rotation) * tr.rotation;
                            f.SetValue(mb, mirror);
                            remapped++;
                        }
                    }
                }
            }

            visualsRoot.SetActive(false);   // stays off until it is fully wired (below)
            visualsRoot.transform.SetParent(null, false);
            visualsRoot.transform.position = Feet;
            visualsRoot.transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
            Destroy(temp);

            // drawn fully (the local player's own body draws only its shadow), on a normal layer, with its own
            // materials (the game flashes the player's when they're hurt)
            int defaultLayer = LayerMask.NameToLayer("Default");
            var copied = new Dictionary<Material, Material>();   // the player's material -> the copy's own
            foreach (Transform t in visualsRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = defaultLayer;
            foreach (Renderer r in visualsRoot.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
                r.forceRenderingOff = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    Material old = mats[i];
                    if (!copied.TryGetValue(old, out Material mine))
                    {
                        mine = new Material(old);
                        if (mine.HasProperty("_ColorOverlayAmount")) mine.SetFloat("_ColorOverlayAmount", 0f);
                        copied[old] = mine;
                        ownMaterials.Add(mine);
                    }
                    mats[i] = mine;
                }
                r.sharedMaterials = mats;
            }

            // the copy's scripts must never hold the player's own material instances: PlayerMaterial.OnDestroy
            // destroys its 'material', so destroying a copy used to destroy the real player's materials (the next copy,
            // and the player's own body, then drew with missing materials: purple). Every Material reference in the
            // copy's scripts now points at the copy's own material.
            int matsRemapped = RemapMaterials(visualsRoot.transform, copied);
            if (matsRemapped > 0) R.Info($"Semibot: {matsRemapped} material reference(s) in the copy's scripts pointed at its own materials.");

            Visuals = visualsRoot.GetComponent<PlayerAvatarVisuals>();
            RightArm = visualsRoot.GetComponentInChildren<PlayerAvatarRightArm>(true);
            All.Add(this);
            if (Visuals != null) Visuals.isMenuAvatar = false;
            puppet.playerAvatarVisuals = Visuals;
            Push(0f);
            // the eyes are run by the rig (DriveEyes), unpatched, so an error shows exactly where it happens
            Eyes = visualsRoot.GetComponentInChildren<PlayerEyes>(true);
            if (Eyes != null) Eyes.enabled = false;
            visualsRoot.SetActive(true);

            // the tumble ball: rolls with physics while tumbling (the visuals follow its follow point)
            var ball = new GameObject("Semibot Tumble Ball");
            ball.layer = defaultLayer;
            ball.SetActive(false);
            var sc = ball.AddComponent<SphereCollider>(); sc.radius = 0.4f;
            tumbleRb = ball.AddComponent<Rigidbody>();
            tumbleRb.mass = 2f; tumbleRb.angularDamping = 0.6f; tumbleRb.linearDamping = 0.2f;
            tumbleRb.interpolation = RigidbodyInterpolation.Interpolate;
            tumbleRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            tumbleFollow.SetParent(ball.transform, false);
            tumbleFollow.localPosition = followLocal;
            tumbleFollow.localRotation = followRot;

            BuildBeam(source);
            jumpSound = source.jumpSound; landSound = source.landSound;
            tumbleStartSound = source.tumbleStartSound; tumbleStopSound = source.tumbleStopSound;
            builtAt = Time.time;

            var kinds = new List<string>();
            foreach (MonoBehaviour mb in visualsRoot.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) kinds.Add(mb.GetType().Name);
            R.Info($"Semibot: copied {R.Name(source)}'s body; kept [{string.Join(", ", kinds)}]; removed [{string.Join(", ", removed)}]; {remapped} reference(s) pointed at the stand-ins; {runtimeCopied} runtime field(s) copied from the player's scripts; eye height {eyeHeight:0.##} m; tumble follow {followLocal}; flashlight from {lightFrom}{(ownLight != null && ownLight.FollowTransformClient != null ? $", follows '{ownLight.FollowTransformClient.name}'{(ownLight.FollowTransformClient.IsChildOf(visualsRoot.transform) ? " (on the copy)" : " (NOT on the copy)")}" : "")}.");
        }

        /// <summary>
        /// Instantiate copies only serialized fields; fields the game fills in at runtime (e.g. Cosmetic's
        /// cosmeticAsset / cosmeticTypeAsset / playerCosmetics, set when cosmetics are put on) stay empty on the
        /// copy, and its scripts then throw every frame. For each script in the player's visuals and its twin in the
        /// copy (same path, type and order), every non-serialized reference field that is empty on the copy but set
        /// on the player is filled: objects inside the visuals map to the copy's twins, a PlayerCosmetics
        /// CosmeticParent maps to the copy's at the same index, assets and other outside objects are shared;
        /// references into the rest of the player's avatar are left for the stand-in sweep.
        /// </summary>
        private static int CopyRuntimeFields(Transform srcRoot, Transform copyRoot, Transform avatarRoot)
        {
            int copied = 0;
            PlayerCosmetics srcCos = srcRoot.GetComponentInChildren<PlayerCosmetics>(true);
            PlayerCosmetics copyCos = copyRoot.GetComponentInChildren<PlayerCosmetics>(true);
            foreach (MonoBehaviour a in srcRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (a == null) continue;
                MonoBehaviour b = Twin(a, srcRoot, copyRoot) as MonoBehaviour;
                if (b == null) continue;
                for (System.Type t = a.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType.IsValueType || f.IsLiteral || f.IsInitOnly) continue;
                        bool serialized = (f.IsPublic && !f.IsNotSerialized) || f.GetCustomAttribute<SerializeField>() != null;
                        if (serialized) continue;   // Instantiate already copied (and remapped) it
                        object have, want;
                        try { have = f.GetValue(b); want = f.GetValue(a); } catch { continue; }
                        if (want == null || (want is Object wo && wo == null)) continue;
                        if (have != null && !(have is Object ho && ho == null) && !(have is System.Collections.ICollection hc && hc.Count == 0)) continue;   // already set on the copy
                        object value = null;
                        if (want is Object uo)
                        {
                            Transform wt = uo is Component wc ? wc.transform : uo is GameObject wg ? wg.transform : null;
                            if (wt != null && wt.IsChildOf(srcRoot)) value = Twin(uo, srcRoot, copyRoot);              // inside the visuals: the copy's twin
                            else if (wt != null && wt.IsChildOf(avatarRoot)) continue;                                // the player's other parts: stand-ins
                            else value = uo;                                                                         // an asset or a shared object
                        }
                        else if (want is System.Collections.IList list && f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            // a runtime list (e.g. Cosmetic.meshParentsScale): a new list with the same items, objects inside
                            // the visuals mapped to the copy's twins
                            var copy = (System.Collections.IList)System.Activator.CreateInstance(f.FieldType);
                            foreach (object item in list)
                            {
                                if (item is Object io && io != null)
                                {
                                    Transform it = io is Component ic ? ic.transform : io is GameObject ig ? ig.transform : null;
                                    copy.Add(it != null && it.IsChildOf(srcRoot) ? Twin(io, srcRoot, copyRoot) : io);
                                }
                                else copy.Add(item);
                            }
                            value = copy;
                        }
                        else if (want is PlayerCosmetics.CosmeticParent cp && srcCos != null && copyCos != null)
                        {
                            int i = srcCos.cosmeticParents.IndexOf(cp);
                            if (i >= 0 && i < copyCos.cosmeticParents.Count) value = copyCos.cosmeticParents[i];
                        }
                        if (value == null) continue;
                        try { f.SetValue(b, value); copied++; } catch { }
                    }
                }
            }
            return copied;
        }

        /// <summary>Points every Material field (and List of Materials) of the copy's scripts at the copy's own materials.</summary>
        private static int RemapMaterials(Transform root, Dictionary<Material, Material> map)
        {
            int n = 0;
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                for (System.Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsLiteral || f.IsInitOnly) continue;
                        object v;
                        try { v = f.GetValue(mb); } catch { continue; }
                        if (f.FieldType == typeof(Material))
                        {
                            Material one = v as Material;
                            if (one == null) continue;
                            if (map.TryGetValue(one, out Material ownOne)) { f.SetValue(mb, ownOne); n++; }
                            else if (mb is PlayerMaterial) { f.SetValue(mb, null); n++; }   // never the player's
                        }
                        else if (f.FieldType == typeof(List<Material>))
                        {
                            var list = v as List<Material>;
                            if (list == null || list.Count == 0) continue;
                            var newList = new List<Material>(list.Count);
                            foreach (Material item in list) newList.Add(item != null && map.TryGetValue(item, out Material ownItem) ? ownItem : item);
                            f.SetValue(mb, newList);
                            n++;
                        }
                        else if (f.FieldType == typeof(Material[]))
                        {
                            var arr = v as Material[];
                            if (arr == null || arr.Length == 0) continue;
                            var newArr = new Material[arr.Length];
                            for (int i = 0; i < arr.Length; i++) newArr[i] = arr[i] != null && map.TryGetValue(arr[i], out Material ownEl) ? ownEl : arr[i];
                            f.SetValue(mb, newArr);
                            n++;
                        }
                    }
                }
            }
            return n;
        }

        /// <summary>The copy's twin of an object in the player's visuals: same path, same type, same order among its kind.</summary>
        private static Object Twin(Object o, Transform srcRoot, Transform copyRoot)
        {
            Transform t = o is Component c ? c.transform : o is GameObject g ? g.transform : null;
            if (t == null) return null;
            string path = PathIn(srcRoot, t);
            if (path == null) return null;
            Transform ct = path.Length == 0 ? copyRoot : copyRoot.Find(path);
            if (ct == null) return null;
            if (o is GameObject) return ct.gameObject;
            if (o is Transform) return ct;
            Component[] mine = t.GetComponents(o.GetType()), theirs = ct.GetComponents(o.GetType());
            int i = System.Array.IndexOf(mine, o);
            return i >= 0 && i < theirs.Length ? theirs[i] : null;
        }

        /// <summary>The path of t under root ("" for root itself), or null if it isn't under it.</summary>
        private static string PathIn(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return t == root ? string.Join("/", parts) : null;
        }

        /// <summary>A stand-in component on its own child of the switched-off holder.</summary>
        private T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, false);   // switched off from here on (its parent is off)
            return go.AddComponent<T>();
        }

        /// <summary>The grab beam: a LineRenderer with the player's own beam material and scroll speed (PhysGrabBeam).</summary>
        private void BuildBeam(PlayerAvatar source)
        {
            PhysGrabBeam pgb = source.physGrabber != null ? source.physGrabber.physGrabBeamComponent : null;
            LineRenderer src = pgb != null ? pgb.lineRenderer : null;
            if (pgb != null && pgb.scrollSpeed.sqrMagnitude > 0f) beamScroll = pgb.scrollSpeed;
            var go = new GameObject("Semibot Grab Beam");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 16;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.receiveShadows = false;
            if (src != null && src.sharedMaterial != null)
            {
                beamMat = new Material(src.sharedMaterial);
                beam.widthCurve = src.widthCurve;
                beam.widthMultiplier = src.widthMultiplier;
                beam.colorGradient = src.colorGradient;
                beam.textureMode = src.textureMode;
                beam.alignment = src.alignment;
            }
            else
            {
                beamMat = new Material(Shader.Find("Sprites/Default"));
                beam.widthMultiplier = 0.03f;
                beam.startColor = beam.endColor = new Color(0.55f, 0.85f, 1f, 0.8f);
            }
            beam.material = beamMat;
            beam.enabled = false;
            beamFake.lineRenderer = beam;   // PlayerEyes checks the beam's renderer
        }

        // ---- actions ----

        /// <summary>
        /// A jump: the game's own jump animation (PlayerAvatarVisuals.JumpImpulse) and the player's jump sound, next
        /// frame. The caller lifts it once JumpPending is false.
        /// </summary>
        internal void Jump()
        {
            jumpAt = Time.time;   // the game's own jump: no forcing of poses (the Animator blends from wherever it is)
            JumpPending = true;
            jumpLogAt = Time.time + 0.15f;
        }
        internal bool JumpPending { get; private set; }

        /// <summary>
        /// Landing: the player's land sound, and (as PlayerAvatar.LandRPC does for a player) everyone within 5 m
        /// glances at it for 2 s (SemiFunc.PlayerEyesOverrideSoft).
        /// </summary>
        internal void Land()
        {
            PlaySound(landSound, Feet);
            try { SemiFunc.PlayerEyesOverrideSoft(eye.position, 2f, gameObject, 5f); } catch { }
        }

        private static readonly MethodInfo EyesStart = AccessTools.Method(typeof(PlayerEyes), "Start");
        private static readonly MethodInfo EyesUpdate = AccessTools.Method(typeof(PlayerEyes), "Update");
        private bool eyesStarted;
        private int eyesErrors;
        private bool talkerWarned;
        private static readonly FieldInfo EyesTalker = AccessTools.Field(typeof(PlayerEyes), "currentTalker");
        private static readonly FieldInfo VoiceChatF = AccessTools.Field(typeof(PlayerAvatar), "voiceChat");

        /// <summary>
        /// Runs the game's PlayerEyes Start (once) and Update (every frame, after the Animator) on the copy. They aren't
        /// patched, so an error carries its IL offset (logged with the whole stack the first 3 times).
        /// </summary>
        private void DriveEyes()
        {
            if (Eyes == null || EyesUpdate == null) return;
            try
            {
                if (!eyesStarted)
                {
                    eyesStarted = true;
                    EyesTalker?.SetValue(Eyes, null);   // not the player's talker (copied with the runtime fields)
                    EyesStart?.Invoke(Eyes, null);
                }
                // PlayerEyes.Update line 321: "if (currentTalker && !currentTalker.voiceChat.isTalking)" threw here: a
                // talker without a voice chat (e.g. a stand-in player in the list). Such a talker is let go first.
                if (EyesTalker?.GetValue(Eyes) is PlayerAvatar talker && talker != null && !(VoiceChatF?.GetValue(talker) is Object vc && vc != null))
                {
                    if (!talkerWarned) { talkerWarned = true; Plugin.Log.LogWarning($"Semibot: its eyes' talker '{talker.name}' ({R.Name(talker)}) has no voice chat; let go."); }
                    EyesTalker.SetValue(Eyes, null);
                }
                EyesUpdate.Invoke(Eyes, null);
            }
            catch (TargetInvocationException e)
            {
                if (eyesErrors++ < 3) Plugin.Log.LogWarning($"Semibot: the game's PlayerEyes failed on the copy ({eyesErrors}): {e.InnerException}\n  players: {PlayersInfo()}");
                else if (eyesErrors == 4) Plugin.Log.LogWarning("Semibot: PlayerEyes keeps failing on the copy; not logging it again.");
            }
        }

        internal int EyesErrors => eyesErrors;

        /// <summary>Every entry in GameDirector.PlayerList: name, object, voice chat present, death head.</summary>
        private static string PlayersInfo()
        {
            if (GameDirector.instance == null) return "no GameDirector";
            var parts = new List<string>();
            foreach (PlayerAvatar p in GameDirector.instance.PlayerList)
            {
                if (p == null) { parts.Add("(null)"); continue; }
                object vc = VoiceChatF?.GetValue(p);
                parts.Add($"'{p.name}' {R.Name(p)} voice {(vc is Object o && o != null ? "yes" : "NO")}");
            }
            return string.Join("; ", parts);
        }

        private static string Bool(Animator a, string name)
        {
            foreach (AnimatorControllerParameter p in a.parameters) if (p.name == name) return a.GetBool(name) ? "on" : "off";
            return "-";
        }

        private static void PlaySound(Sound s, Vector3 at)
        {
            if (s == null) return;
            try { s.Play(at, 1f, 1f, 1f, 1f); } catch (System.Exception e) { Plugin.Log.LogWarning($"Semibot: a sound failed: {e.Message}"); }
        }

        /// <summary>Into tumble form: a rolling ball launched with this velocity and spin; the visuals curl up and follow it.</summary>
        internal void StartTumble(Vector3 velocity, Vector3 spin)
        {
            if (tumbleRb == null) return;
            // the change of form comes first: it curls up on the spot (the ball held still under it) with the
            // player's tumble sound, and is launched once the Animator is in the tumble pose (CurlMin-CurlMax s, FixedUpdate)
            tumbleRb.gameObject.SetActive(true);
            tumbleRb.isKinematic = true;
            tumbleRb.position = Feet + Vector3.up * 0.4f;
            tumbleRb.rotation = Quaternion.Euler(0f, Yaw, 0f);
            tumbleRb.transform.SetPositionAndRotation(tumbleRb.position, tumbleRb.rotation);
            launchVelocity = velocity;
            launchSpin = spin;
            curlStart = Time.time;
            launchAt = Time.time + CurlMin;
            Animator a = Anim;
            curlFromState = a != null ? a.GetCurrentAnimatorStateInfo(0).fullPathHash : 0;
            tumbling = true;
            // the body sits on the ball at once (no gradual catching up)
            if (Visuals != null && TumbleFollowLerp != null) TumbleFollowLerp.SetValue(Visuals, 1f);
            PlaySound(tumbleStartSound, Feet);
        }

        /// <summary>The Animator has left the pose it had and finished blending into the tumble pose (or CurlMax passed).</summary>
        private bool CurledUp()
        {
            if (Time.time - curlStart >= CurlMax) return true;
            Animator a = Anim;
            if (a == null) return true;
            return !a.IsInTransition(0) && a.GetCurrentAnimatorStateInfo(0).fullPathHash != curlFromState;
        }

        /// <summary>Still curling up (not launched yet).</summary>
        internal bool Curling => tumbling && launchAt > 0f;

        /// <summary>Out of tumble form: stands up where the ball is (on the floor below it), facing the way it rolled.</summary>
        internal void EndTumble()
        {
            if (!tumbling) return;
            tumbling = false;
            launchAt = -1f;
            tumbleRb.isKinematic = false;
            PlaySound(tumbleStopSound, tumbleRb.position);
            Vector3 p = tumbleRb.position;
            if (UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) p = hit.position;
            else if (Physics.Raycast(p, Vector3.down, out RaycastHit floor, 3f, SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore)) p = floor.point;
            Feet = p;
            Vector3 v = tumbleRb.linearVelocity; v.y = 0f;
            if (v.sqrMagnitude > 0.04f) Yaw = LookYaw = Quaternion.LookRotation(v).eulerAngles.y;
            tumbleRb.gameObject.SetActive(false);
        }

        /// <summary>How close a valuable must be to be grabbed (metres from its body).</summary>
        internal const float GrabReach = 1f;

        /// <summary>
        /// The nearest valuable it can grab: some part of it within GrabReach of its body (the line from its feet
        /// to its eyes) and in clear sight from its eyes. Null if there's none.
        /// </summary>
        internal PhysGrabObject FindGrabbable()
        {
            PhysGrabObject best = null;
            float bestD = float.MaxValue;
            foreach (ValuableObject v in Object.FindObjectsByType<ValuableObject>(FindObjectsSortMode.None))
            {
                if (v == null || !v.gameObject.activeInHierarchy) continue;
                PhysGrabObject pgo = v.GetComponent<PhysGrabObject>();
                if (pgo == null || (v.transform.position - Feet).sqrMagnitude > 25f) continue;   // quick: within 5 m
                if (CanGrab(pgo, out float d) && d < bestD) { bestD = d; best = pgo; }
            }
            return best;
        }

        /// <summary>Within GrabReach of its body and visible from its eyes (nothing but the object itself in between).</summary>
        internal bool CanGrab(PhysGrabObject obj, out float distance)
        {
            distance = float.MaxValue;
            if (obj == null) return false;
            Vector3 a = Feet, b = Feet + Vector3.up * eyeHeight;
            Vector3 near = obj.transform.position;
            foreach (Collider c in obj.GetComponentsInChildren<Collider>())
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                Vector3 onBody = ClosestOnSegment(a, b, c.bounds.center);
                Vector3 p = c.ClosestPoint(onBody);
                float d = Vector3.Distance(p, ClosestOnSegment(a, b, p));
                if (d < distance) { distance = d; near = p; }
            }
            if (distance > GrabReach) return false;
            Vector3 from = Feet + Vector3.up * eyeHeight;
            Vector3 dir = near - from;
            float len = dir.magnitude;
            if (len < 0.05f) return true;
            foreach (RaycastHit h in Physics.RaycastAll(from, dir / len, len - 0.02f, SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(obj.transform)) continue;                       // the object itself
                if (tumbleRb != null && h.collider.transform.IsChildOf(tumbleRb.transform)) continue;   // its own ball
                if (h.collider.GetComponentInParent<PlayerAvatar>() != null) continue;              // players don't block
                return false;
            }
            return true;
        }

        private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return a + ab * t;
        }

        private static readonly FieldInfo GrabberOverrideTimer = AccessTools.Field(typeof(PlayerAvatarRightArm), "grabberPoseOverrideTimer");
        private static readonly FieldInfo ArmPoseNew = AccessTools.Field(typeof(PlayerAvatarRightArm), "poseNew");
        private float armLogAt = -1f;

        /// <summary>
        /// Grabs a valuable with the beam (it floats where the "camera" looks, holdDistance away) if CanGrab allows
        /// it; returns false (and grabs nothing) otherwise. Null lets go: the beam goes off and the arm returns to its
        /// resting pose (the game's PlayerAvatarRightArm does that once the beam is off; any grab-pose override
        /// timer and the 'Grabbing' animation are cleared here too).
        /// </summary>
        internal bool Grab(PhysGrabObject obj, float holdDistance, bool force = false)
        {
            if (obj != null && prop != null) HoldProp(null, 0f);
            if (obj != null && !force && !CanGrab(obj, out float d)) { R.Info($"Semibot: won't grab {obj.name}: {(d > GrabReach ? $"{d:0.##} m away (reach {GrabReach} m)" : "not in sight")}."); return false; }
            bool releasing = obj == null && held != null;
            // dropped: a moment of protection so the drop itself doesn't break it (host: breaking is decided there)
            if (releasing && R.IsHost) { try { held.OverrideIndestructible(1f); } catch { } }
            held = obj;
            Set(grabber, "physGrabBeamActive", obj != null);
            Set(grabber, "grabbedPhysGrabObject", obj);
            grabber.grabbedObjectTransform = obj != null ? obj.transform : null;
            Set(grabber, "pullerDistance", holdDistance);
            this.holdDistance = holdDistance;
            if (beam != null) beam.enabled = obj != null;
            if (releasing)
            {
                if (RightArm != null && GrabberOverrideTimer != null) GrabberOverrideTimer.SetValue(RightArm, 0f);
                Animator a = Anim;
                if (a != null) a.SetBool("Grabbing", false);
                armLogAt = Time.time + 0.6f;
            }
            return true;
        }
        private float holdDistance = 1.5f;
        internal PhysGrabObject Held => held;

        private Transform prop;
        /// <summary>The prop it holds on the beam (e.g. a gun), if any.</summary>
        internal Transform Prop => prop;

        /// <summary>
        /// Holds a prop that isn't a physics object (e.g. the Alter Mimic's gun) on the beam, as a player holds a gun:
        /// the beam and the grabbing arm are on, and the beam ends at the prop (the caller places the prop at
        /// HoldPoint). Null puts it away (beam off, arm back down). Lets go of any held valuable first.
        /// </summary>
        internal void HoldProp(Transform p, float holdDistance)
        {
            if (p != null && held != null) Grab(null, 0f);
            bool releasing = p == null && prop != null;
            prop = p;
            Set(grabber, "physGrabBeamActive", p != null);
            Set(grabber, "grabbedPhysGrabObject", null);
            grabber.grabbedObjectTransform = p;
            Set(grabber, "pullerDistance", holdDistance);
            if (p != null) this.holdDistance = holdDistance;
            if (beam != null) beam.enabled = p != null;
            if (releasing)
            {
                if (RightArm != null && GrabberOverrideTimer != null) GrabberOverrideTimer.SetValue(RightArm, 0f);
                Animator a = Anim;
                if (a != null) a.SetBool("Grabbing", false);
            }
        }

        /// <summary>Where a held thing floats: holdDistance along its camera's view (computed from Feet/LookYaw/LookPitch, so it's the same in any script order).</summary>
        internal Vector3 HoldPoint(float distance) =>
            ((tumbling && tumbleRb != null) ? tumbleRb.position : Feet) + Vector3.up * eyeHeight + Quaternion.Euler(LookPitch, LookYaw, 0f) * Vector3.forward * distance;

        // ---- hurt flash ----

        private Color flashColor = Color.white;
        private float flashStart = -1f, flashLength;

        /// <summary>Flashes the whole body a colour (the player shader's overlay), fading out over 'seconds', like a hurt flash.</summary>
        internal void Flash(Color c, float seconds)
        {
            flashColor = c;
            flashStart = Time.time;
            flashLength = Mathf.Max(0.05f, seconds);
        }

        private void FlashTick()
        {
            if (flashStart < 0f) return;
            float t = (Time.time - flashStart) / flashLength;
            float amount = t >= 1f ? 0f : 1f - t * t;
            foreach (Material m in ownMaterials)
            {
                if (m == null) continue;
                if (m.HasProperty("_ColorOverlay")) m.SetColor("_ColorOverlay", flashColor);
                if (m.HasProperty("_ColorOverlayAmount")) m.SetFloat("_ColorOverlayAmount", amount);
            }
            if (t >= 1f) flashStart = -1f;
        }

        // ---- every frame ----

        private void LateUpdate()
        {
            if (holder == null || puppet == null) return;
            FlashTick();
            Animator a = Anim;
            if (idleHash == 0 && a != null && Time.time - builtAt > 0.5f && !Moving && !tumbling && !a.IsInTransition(0))
            {
                idleHash = a.GetCurrentAnimatorStateInfo(0).fullPathHash;
                R.Info($"Semibot: idle pose = animator state {idleHash}.");
            }
            if (JumpPending && Time.time >= jumpAt)
            {
                JumpPending = false;
                if (Visuals != null) Visuals.JumpImpulse();
                PlaySound(jumpSound, Feet);
            }
            Push(Time.deltaTime);
            DriveEyes();

            // one look at the arm after letting go (it should be back in its resting pose)
            if (armLogAt > 0f && Time.time >= armLogAt && RightArm != null)
            {
                armLogAt = -1f;
                object pn = ArmPoseNew != null ? ArmPoseNew.GetValue(RightArm) : null;
                bool atBase = pn is Vector3 v3 && v3 == RightArm.basePose;
                R.Info($"Semibot: 0.6 s after letting go: arm pose {(atBase ? "resting" : pn is Vector3 v ? (v == RightArm.grabberPose ? "STILL GRABBING" : $"{v}") : "?")}, beam {(F<object>(grabber, "physGrabBeamActive") is bool on && on ? "ON" : "off")}, 'Grabbing' {(a != null ? Bool(a, "Grabbing") : "?")}, override timer {(GrabberOverrideTimer != null ? GrabberOverrideTimer.GetValue(RightArm) : "?")}.");
            }
            // one look at the Animator just after a jump starts (to check the jump animation really plays)
            if (jumpLogAt > 0f && Time.time >= jumpLogAt && a != null)
            {
                jumpLogAt = -1f;
                AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
                R.Info($"Semibot: 0.15 s into a jump: state {st.fullPathHash}{(st.fullPathHash == idleHash ? " (idle)" : "")}, in transition {a.IsInTransition(0)}, speed {a.speed:0.##}, Jumping {Bool(a, "Jumping")}, Falling {Bool(a, "Falling")}, Moving {Bool(a, "Moving")}, Sprinting {Bool(a, "Sprinting")}.");
            }
        }

        private void FixedUpdate()
        {
            if (tumbling && launchAt > 0f && Time.time >= launchAt && tumbleRb != null && CurledUp())
            {
                R.Info($"Semibot: curled up in {Time.time - curlStart:0.##} s; launched.");
                launchAt = -1f;
                tumbleRb.isKinematic = false;
                tumbleRb.linearVelocity = launchVelocity;
                tumbleRb.angularVelocity = launchSpin;
                // the visuals already sit on the ball: follow it fully from the launch (no catching up)
                if (Visuals != null && TumbleFollowLerp != null) TumbleFollowLerp.SetValue(Visuals, 1f);
            }
            // the held object floats to the hold point (host only: physics belongs to the host). A gentle pull (eased
            // towards the target speed, at most 3.5 m/s) and the game's own "can't break" override while carried:
            // setting its speed outright made every bump a hard impact, and valuables broke at once.
            if (held == null || !R.IsHost) return;
            Rigidbody rb = held.GetComponent<Rigidbody>();
            if (rb == null) return;
            try { held.OverrideIndestructible(0.3f); } catch { }
            Vector3 to = pullerPoint.position - rb.worldCenterOfMass;
            Vector3 want = Vector3.ClampMagnitude(to * 6f, 3.5f);
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, want, 0.25f);
            rb.angularVelocity *= 0.85f;
        }

        /// <summary>Writes the monster's state into the stand-ins (what the game's visuals scripts read).</summary>
        private void Push(float dt)
        {
            Quaternion body = Quaternion.Euler(0f, Yaw, 0f);
            Vector3 feet = Feet;
            if (tumbling && tumbleRb != null) feet = tumbleRb.position;
            transform.SetPositionAndRotation(feet, body);   // the rig (and the stand-in avatar under it) turns with the body

            eye.SetPositionAndRotation((tumbling ? feet : Feet) + Vector3.up * eyeHeight, Quaternion.Euler(LookPitch, LookYaw, 0f));
            if (camT != null) camT.SetPositionAndRotation(eye.position, eye.rotation);
            Vector3 vel = tumbling && tumbleRb != null ? tumbleRb.linearVelocity : Velocity;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);

            Set(puppet, "clientPosition", Feet);
            Set(puppet, "clientPositionCurrent", Feet);
            Set(puppet, "clientRotationCurrent", body);
            Set(puppet, "isMoving", !tumbling && Moving && flat.sqrMagnitude > 0.01f);
            Set(puppet, "isSprinting", !tumbling && Sprinting && Moving);
            Set(puppet, "isGrounded", tumbling || Grounded);
            Set(puppet, "isTumbling", tumbling);
            // never 0: in the Sprint state the game plays the animation at (1 + 0.1 x upgrades) x InputAnalogAmount,
            // so 0 froze the Animator mid-stride (and a frozen Animator can't blend into the jump or back to idle)
            Set(puppet, "InputAnalogAmount", Mathf.Max(0.1f, Analog));
            Set(puppet, "rbVelocity", Quaternion.Inverse(body) * vel);   // the avatar's local velocity
            Set(puppet, "rbVelocityRaw", vel);
            Set(tumble, "isTumbling", tumbling);
            if (tumbling && tumbleRb != null)
            {
                Set(tumblePgo, "rbVelocity", tumbleRb.linearVelocity);
                Set(tumblePgo, "rbAngularVelocity", tumbleRb.angularVelocity);
            }

            // the grab: puller = where the object is held, grab point = the object; the beam curves from the
            // grabber orb through the puller point to the object (PhysGrabBeam.DrawCurve's quadratic Bézier)
            if (held != null || prop != null)
            {
                pullerPoint.position = eye.position + eye.forward * holdDistance;
                Rigidbody rb = held != null ? held.GetComponent<Rigidbody>() : null;
                grabPoint.position = rb != null ? rb.worldCenterOfMass : held != null ? held.transform.position : prop.position;
                if (beam != null)
                {
                    Transform orb = RightArm != null && RightArm.grabberOrb != null ? RightArm.grabberOrb : eye;
                    Vector3 a = orb.position, b = pullerPoint.position, c = grabPoint.position;
                    for (int i = 0; i < beam.positionCount; i++)
                    {
                        float t = i / (float)(beam.positionCount - 1);
                        beam.SetPosition(i, (1 - t) * (1 - t) * a + 2f * (1 - t) * t * b + t * t * c);
                    }
                    if (beamMat != null && beamMat.HasProperty("_MainTex")) beamMat.mainTextureOffset = Time.time * beamScroll;
                }
            }
        }

        private void OnDestroy()
        {
            All.Remove(this);
            if (held != null) Grab(null, 0f);
            if (prop != null) HoldProp(null, 0f);
            if (visualsRoot != null) Destroy(visualsRoot);
            if (tumbleRb != null) Destroy(tumbleRb.gameObject);
            if (beamMat != null) Destroy(beamMat);
            foreach (Material m in ownMaterials) if (m != null) Destroy(m);
        }

        // ---- reflection helpers (many of the game's fields are internal) ----

        private static readonly Dictionary<(System.Type, string), FieldInfo> fields = new Dictionary<(System.Type, string), FieldInfo>();
        private static readonly HashSet<string> warned = new HashSet<string>();

        private static FieldInfo Field(System.Type t, string name)
        {
            if (!fields.TryGetValue((t, name), out FieldInfo f)) fields[(t, name)] = f = AccessTools.Field(t, name);
            return f;
        }

        internal static void Set(object o, string name, object value)
        {
            if (o == null) return;
            FieldInfo f = Field(o.GetType(), name);
            if (f == null) { if (warned.Add(o.GetType().Name + "." + name)) Plugin.Log.LogWarning($"Semibot: the game has no field {o.GetType().Name}.{name}."); return; }
            try { f.SetValue(o, value); }
            catch (System.Exception e) { if (warned.Add(o.GetType().Name + "." + name)) Plugin.Log.LogWarning($"Semibot: setting {o.GetType().Name}.{name} failed: {e.Message}"); }
        }

        internal static T F<T>(object o, string name) where T : class
        {
            if (o == null) return null;
            FieldInfo f = Field(o.GetType(), name);
            return f != null ? f.GetValue(o) as T : null;
        }
    }

    /// <summary>Marks a semibot rig's copied visuals (SemibotGuard uses it to tell them from real players').</summary>
    internal class SemibotTag : MonoBehaviour
    {
        internal SemibotRig Rig;
    }
}
