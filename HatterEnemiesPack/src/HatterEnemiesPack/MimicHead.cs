using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// The Alter Mimic's head form (every game): a copy of the latched player's own death head (its meshes only, so
    /// it has their colour and cosmetics), centred on the enemy's rigidbody, whose collider is reshaped into a ball
    /// the head's size and whose rotation is freed, so it rolls and tumbles like a real dead head. Possessed: the same
    /// eye and pupil glow, legs, particles and start/stop sounds the game uses when a dead player takes control of
    /// their head (the host stands it up on its legs; see AlterMimic). Seeing it gives the same red "a teammate died"
    /// screen effect as a real dead head. It speaks with the latched player's own text-to-speech voice.
    /// </summary>
    internal class MimicHead : MonoBehaviour
    {
        private static readonly int OverlayAmount = Shader.PropertyToID("_ColorOverlayAmount");
        private static readonly int OverlayColor = Shader.PropertyToID("_ColorOverlay");
        private static readonly FieldInfo VoiceChatField = AccessTools.Field(typeof(PlayerAvatar), "voiceChat");
        private static readonly FieldInfo DeathSeenTimer = AccessTools.Field(typeof(PlayerController), "deathSeenTimer");
        private static readonly FieldInfo SeenSoundField = AccessTools.Field(typeof(PlayerDeathHead), "seenSound");

        private readonly List<Renderer> eyes = new List<Renderer>(), pupils = new List<Renderer>();
        private readonly List<GameObject> legs = new List<GameObject>();
        private GameObject particle;
        private Sound startSound, stopSound, jumpSound, longJumpSound, chargeSound;
        private Transform model;            // the copied meshes (squashed while charging)
        private Vector3 modelScale, modelPos;
        private Quaternion modelRot;
        private bool armed;

        /// <summary>Every game: shaking violently (the second before each shockwave pulse) or still.</summary>
        internal void SetArmed(bool on)
        {
            armed = on;
            if (!on && model != null) { model.localPosition = modelPos; model.localRotation = modelRot; }
        }
        private float chargeLeft, chargeTotal, stretch;
        private Rigidbody sourceBody;       // the real death head's rigidbody (mass, drag)
        private float oldMass, oldDrag, oldAngularDrag;
        private bool massTaken;
        private AudioClip seenSound;
        private Color eyeColor, pupilColor;
        private bool possessed;

        // the enemy's rigidbody and the collider reshaped into the head's ball (restored afterwards)
        private Rigidbody rb;
        private Collider shape;
        private PhysGrabObject grab;
        private RigidbodyConstraints oldConstraints;
        private Vector3 oldCenter, oldSize;
        private float oldRadius, oldHeight;
        private int oldDirection;
        private PhysicsMaterial oldMaterial;
        private static PhysicsMaterial grippy;

        // the red "a teammate is dead" effect on this game's screen
        private bool seen;
        private float seenEffect;

        internal float Radius { get; private set; } = 0.18f;

        /// <summary>Is the head's own voice still talking (this game)?</summary>
        internal bool Speaking
        {
            get
            {
                AudioSource src = voice != null ? voice.GetComponent<AudioSource>() : null;
                return src != null && src.isPlaying;
            }
        }
        internal bool Possessed => possessed;

        internal static MimicHead Create(Transform follow, PlayerAvatar target, Collider ball)
        {
            var go = new GameObject("Alter Mimic Head");
            go.transform.SetParent(follow, false);
            Vector3 s = follow.lossyScale;
            go.transform.localScale = new Vector3(1f / Mathf.Max(0.001f, s.x), 1f / Mathf.Max(0.001f, s.y), 1f / Mathf.Max(0.001f, s.z));
            var head = go.AddComponent<MimicHead>();
            try { head.Build(follow, R.HeadOf(target)); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: building the head failed: {e}"); head.Fallback(); }
            head.TakeBody(follow, ball);
            return head;
        }

        private void Build(Transform follow, PlayerDeathHead src)
        {
            if (src == null || src.meshParent == null) { Plugin.Log.LogWarning("Alter Mimic: the target has no death head to copy; using a stand-in."); Fallback(); return; }

            // copy the meshes under an inactive object, so none of the head's scripts ever run
            var temp = new GameObject("Alter Mimic Head (building)");
            temp.SetActive(false);
            GameObject copy = Instantiate(src.meshParent, temp.transform);
            StripToMeshes(copy);
            copy.transform.SetParent(transform, false);
            copy.transform.localPosition = src.transform.InverseTransformPoint(src.meshParent.transform.position);
            copy.transform.localRotation = Quaternion.Inverse(src.transform.rotation) * src.meshParent.transform.rotation;
            copy.transform.localScale = src.meshParent.transform.lossyScale;
            Destroy(temp);
            copy.SetActive(true);

            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                if (t.name == "mesh_head_leg_left" || t.name == "mesh_head_leg_right") { legs.Add(t.gameObject); t.gameObject.SetActive(false); }
            Map(src.meshParent.transform, copy.transform, src.eyeRenderers, eyes);
            Map(src.meshParent.transform, copy.transform, src.pupilRenderers, pupils);
            eyeColor = src.spectatedColorEye;
            pupilColor = src.spectatedColorPupil;
            startSound = src.spectatedSoundStart;
            stopSound = src.spectatedSoundStop;
            jumpSound = src.spectatedJumpShortSound;
            longJumpSound = src.spectatedJumpLongSound;
            chargeSound = src.spectatedJumpChargeSound;
            model = copy.transform;
            modelScale = copy.transform.localScale;
            modelPos = copy.transform.localPosition;
            modelRot = copy.transform.localRotation;
            sourceBody = src.GetComponent<Rigidbody>();
            seenSound = SeenSoundField != null ? SeenSoundField.GetValue(src) as AudioClip : null;

            if (src.spectatedParticle != null)
            {
                var temp2 = new GameObject("Alter Mimic Head Particle (building)");
                temp2.SetActive(false);
                particle = Instantiate(src.spectatedParticle, temp2.transform);
                foreach (Component c in particle.GetComponentsInChildren<Component>(true))
                    if (c is MonoBehaviour) Destroy(c);
                particle.transform.SetParent(transform, false);
                particle.transform.localPosition = src.transform.InverseTransformPoint(src.spectatedParticle.transform.position);
                Destroy(temp2);
                particle.SetActive(false);
            }

            // centre the head (without its legs) on the rigidbody, and take its size for the ball
            Bounds b = default;
            bool any = false;
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(false))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any)
            {
                transform.position += follow.position - b.center;
                Radius = Mathf.Clamp((b.extents.x + b.extents.y + b.extents.z) / 3f, 0.1f, 0.35f);
            }
            R.Info($"Alter Mimic: head copied from {R.Name(src.playerAvatar)}'s death head: {copy.GetComponentsInChildren<Renderer>(true).Length} renderer(s), {eyes.Count} eye(s), {pupils.Count} pupil(s), {legs.Count} leg(s); size {(any ? b.size.ToString() : "?")}, ball radius {Radius:0.###}.");
        }

        /// <summary>Every game: the enemy's collider becomes a ball the head's size, and the body may rotate freely.</summary>
        private void TakeBody(Transform follow, Collider ball)
        {
            rb = follow.GetComponent<Rigidbody>();
            grab = follow.GetComponent<PhysGrabObject>();
            shape = ball;
            if (rb != null)
            {
                oldConstraints = rb.constraints;
                rb.constraints = RigidbodyConstraints.None;
                // handle like a real dead head: its weight and drag
                if (sourceBody != null)
                {
                    oldMass = rb.mass; oldDrag = rb.linearDamping; oldAngularDrag = rb.angularDamping;
                    rb.mass = sourceBody.mass; rb.linearDamping = sourceBody.linearDamping; rb.angularDamping = sourceBody.angularDamping;
                    massTaken = true;
                }
            }
            if (shape == null) { Plugin.Log.LogWarning("Alter Mimic: no collider to shape into the head."); return; }
            Vector3 centre = shape.transform.InverseTransformPoint(follow.position);
            float scale = Mathf.Max(0.001f, shape.transform.lossyScale.x);
            float r = Radius / scale;
            switch (shape)
            {
                case SphereCollider sc:
                    oldCenter = sc.center; oldRadius = sc.radius;
                    sc.center = centre; sc.radius = r;
                    break;
                case CapsuleCollider cc:
                    oldCenter = cc.center; oldRadius = cc.radius; oldHeight = cc.height; oldDirection = cc.direction;
                    cc.center = centre; cc.radius = r; cc.height = 2f * r;
                    break;
                case BoxCollider bc:
                    oldCenter = bc.center; oldSize = bc.size;
                    bc.center = centre; bc.size = Vector3.one * 2f * r * 0.9f;
                    break;
                default:
                    Plugin.Log.LogWarning($"Alter Mimic: the collider '{shape.name}' is a {shape.GetType().Name}; can't reshape it into the head.");
                    shape = null;
                    return;
            }
            // a dead head doesn't slide or roll on and on (it's half a ball): grippy, a little bounce, and it
            // stops turning quickly; the centre of mass sits low, on the flat side
            oldMaterial = shape.sharedMaterial;
            if (grippy == null)
                grippy = new PhysicsMaterial("Alter Mimic Head")
                {
                    dynamicFriction = 0.9f, staticFriction = 1f, bounciness = 0.15f,
                    frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Average
                };
            shape.sharedMaterial = grippy;
            if (rb != null)
            {
                rb.ResetCenterOfMass();
                rb.centerOfMass = rb.transform.InverseTransformPoint(follow.position - follow.up * Radius * 0.45f);
                rb.angularDamping = Mathf.Max(rb.angularDamping, 2.5f);
                rb.linearDamping = Mathf.Max(rb.linearDamping, 0.3f);
            }
            R.Info($"Alter Mimic: the head's ball is the {shape.GetType().Name} '{shape.name}' (radius {Radius:0.###} m), grippy, angular damping {(rb != null ? rb.angularDamping : 0f):0.#}.");
        }

        private void OnDestroy()
        {
            // give the enemy its body back (it despawns after this, but it may spawn again)
            if (rb != null) rb.constraints = oldConstraints;
            if (rb != null && massTaken) { rb.mass = oldMass; rb.linearDamping = oldDrag; rb.angularDamping = oldAngularDrag; }
            if (shape != null) shape.sharedMaterial = oldMaterial;
            switch (shape)
            {
                case SphereCollider sc: sc.center = oldCenter; sc.radius = oldRadius; break;
                case CapsuleCollider cc: cc.center = oldCenter; cc.radius = oldRadius; cc.height = oldHeight; cc.direction = oldDirection; break;
                case BoxCollider bc: bc.center = oldCenter; bc.size = oldSize; break;
            }
            if (rb != null && shape != null) rb.ResetCenterOfMass();
        }

        /// <summary>Only Transforms, MeshFilters and renderers stay (several passes: some components need others).</summary>
        private static void StripToMeshes(GameObject root)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                bool any = false;
                foreach (Component c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                    try { DestroyImmediate(c); any = true; }
                    catch { }
                }
                if (!any) break;
            }
        }

        private static string PathOf(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        private static void Map(Transform srcRoot, Transform copyRoot, MeshRenderer[] source, List<Renderer> into)
        {
            if (source == null) return;
            foreach (MeshRenderer r in source)
            {
                if (r == null) continue;
                Transform t = copyRoot.Find(PathOf(srcRoot, r.transform));
                Renderer c = t != null ? t.GetComponent<Renderer>() : null;
                if (c != null) into.Add(c);
            }
        }

        private void Fallback()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(transform, false);
            ball.transform.localScale = Vector3.one * 0.3f;
        }

        internal void SetPossessed(bool on)
        {
            if (on == possessed) return;
            possessed = on;
            foreach (GameObject l in legs) if (l != null) l.SetActive(on);
            if (particle != null) particle.SetActive(on);
            Tint(eyes, eyeColor, on);
            Tint(pupils, pupilColor, on);
            Sound s = on ? startSound : stopSound;
            if (s != null)
            {
                try { s.Play(transform.position, 1f, 1f, 1f, 1f); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: possession sound failed: {e}"); }
            }
        }

        /// <summary>Every game: the springy charge before a jump (it squashes down, with the charge sound).</summary>
        internal void Charge(float seconds)
        {
            chargeTotal = chargeLeft = Mathf.Max(0.05f, seconds);
            Play(chargeSound);
        }

        /// <summary>Every game: the jump (it springs back out, with the short or long jump sound).</summary>
        internal void Hop(bool longJump)
        {
            chargeLeft = 0f;
            stretch = 1f;
            Play(longJump && longJumpSound != null ? longJumpSound : jumpSound);
        }

        private void Play(Sound s)
        {
            if (s == null) return;
            try { s.Play(transform.position, 1f, 1f, 1f, 1f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: head sound failed: {e}"); }
        }

        private void LateUpdate()
        {
            if (model == null) return;
            // charging: squash down (and bulge) as the spring loads; on the jump: a quick stretch back
            float squash = 0f;
            if (chargeLeft > 0f)
            {
                chargeLeft -= Time.deltaTime;
                float k = 1f - Mathf.Clamp01(chargeLeft / chargeTotal);
                squash = Mathf.SmoothStep(0f, 1f, k) * 0.3f + Mathf.Sin(Time.time * 60f) * 0.015f * k;   // a little tremble at full charge
            }
            else if (stretch > 0f)
            {
                stretch = Mathf.Max(0f, stretch - Time.deltaTime * 5f);
                squash = -0.18f * stretch;
            }
            model.localScale = new Vector3(modelScale.x * (1f + squash * 0.5f), modelScale.y * (1f - squash), modelScale.z * (1f + squash * 0.5f));
            if (armed)
            {
                // violent vibration: the model jumps around its rest pose every frame
                model.localPosition = modelPos + Random.insideUnitSphere * (Radius * 0.15f);
                model.localRotation = modelRot * Quaternion.Euler(Random.insideUnitSphere * 12f);
            }
        }

        private static readonly MethodInfo LocaleMethod = AccessTools.Method(typeof(LocalizationManager), "TTSLocaleGetCode");
        private static readonly FieldInfo VoiceNumField = AccessTools.Field(typeof(TTSVoice), "activeVoiceNum");
        private static readonly FieldInfo VoiceFreqField = AccessTools.Field(typeof(TTSVoice), "voiceBaseFrequency");
        private static readonly FieldInfo FetchField = AccessTools.Field(typeof(AudioLowPassLogic), "Fetch");

        private TTSVoice voice;            // the head's own text-to-speech speaker (the game's TTS prefab)
        private SemiWorldText words;       // the game's floating TTS text, over the head
        private bool voiceTried;

        /// <summary>
        /// Every game: the head's own speaker and floating text, made from the game's parts: the player TTS prefab
        /// (set to the latched player's voice and pitch, 3D, through the TTS mixer) and a SemiWorldText above the head.
        /// Nothing goes through the latched player, so for everyone (them included) it comes from the head.
        /// </summary>
        private void MakeVoice(PlayerAvatar who)
        {
            voiceTried = true;
            PlayerVoiceChat vc = VoiceChatField != null && who != null ? VoiceChatField.GetValue(who) as PlayerVoiceChat : null;
            if (vc == null) vc = PlayerVoiceChat.instance;
            if (vc == null || vc.TTSprefab == null) { Plugin.Log.LogWarning("Alter Mimic: no TTS prefab found; the head can't talk."); return; }
            GameObject go = Instantiate(vc.TTSprefab, transform);
            go.name = "Alter Mimic Voice";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            voice = go.GetComponent<TTSVoice>() ?? go.GetComponentInChildren<TTSVoice>();
            AudioSource src = voice != null ? voice.GetComponent<AudioSource>() : null;
            if (src != null)
            {
                src.spatialBlend = 1f;
                if (vc.mixerTTSSound != null) src.outputAudioMixerGroup = vc.mixerTTSSound;
                AudioLowPassLogic lp = src.GetComponent<AudioLowPassLogic>();
                if (lp != null)
                {
                    try { FetchField?.SetValue(lp, true); lp.Setup(); }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: voice muffling setup failed: {e}"); }
                }
            }
            // the latched player's own voice and pitch
            TTSVoice theirs = vc.ttsVoice;
            if (voice != null && theirs != null)
            {
                try
                {
                    if (VoiceNumField != null) voice.setVoice((int)VoiceNumField.GetValue(theirs));
                    if (VoiceFreqField != null) VoiceFreqField.SetValue(voice, VoiceFreqField.GetValue(theirs));
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: copying the voice failed: {e}"); }
            }

            var textObj = new GameObject("Alter Mimic Words");
            textObj.transform.SetParent(transform, false);
            textObj.transform.localPosition = Vector3.up * (Radius + 0.25f);
            words = textObj.AddComponent<SemiWorldText>();
            R.Info($"Alter Mimic: the head has its own voice ({(voice != null ? "TTS ready" : "NO TTS")}, {(src != null ? "3D sound" : "no audio source")}) and floating text.");
        }

        /// <summary>Every game: a line of text-to-speech from the head itself, with the words floating above it.</summary>
        internal void Say(string line, PlayerAvatar who)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (!voiceTried) MakeVoice(who);
            float seconds = Mathf.Max(1f + 0.45f * line.Split(' ').Length, 0.2f * line.Length);
            try
            {
                string code = "en-US";
                if (LocalizationManager.instance != null && LocaleMethod != null) code = LocaleMethod.Invoke(LocalizationManager.instance, null) as string ?? code;
                if (voice != null) voice.TTSSpeakNow(line, false, code);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: speaking failed: {e}"); }
            try { if (words != null) words.ShowText(line, seconds); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: showing the words failed: {e}"); words = null; }
        }

        private static void Tint(List<Renderer> list, Color color, bool on)
        {
            var block = new MaterialPropertyBlock();
            foreach (Renderer r in list)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(OverlayAmount, on ? 1f : 0f);
                block.SetColor(OverlayColor, color);
                r.SetPropertyBlock(block);
            }
        }

        // ---------------- the red screen when you first see it (as the game does for a real dead head) ----------------

        private void Update()
        {
            if (seenEffect > 0f) { SeenEffectTick(); return; }
            if (seen || PlayerController.instance == null || !R.Alive(PlayerAvatar.instance)) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 pos = transform.position;
            Vector3 eye = cam.transform.position;
            if (Vector3.Distance(eye, pos) > 10f || !SemiFunc.OnScreen(pos, -0.15f, -0.15f)) return;
            if (Physics.Linecast(eye, pos, out RaycastHit hit, SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore)
                && (rb == null || hit.rigidbody != rb)) return;   // something in the way
            seen = true;

            float timer = DeathSeenTimer != null ? (float)DeathSeenTimer.GetValue(PlayerController.instance) : 0f;
            if (timer > 0f) return;   // the game had a teammate's death effect just now
            DeathSeenTimer?.SetValue(PlayerController.instance, 30f);
            try
            {
                if (GameDirector.instance != null)
                {
                    GameDirector.instance.CameraImpact.Shake(2f, 0.5f);
                    GameDirector.instance.CameraShake.Shake(2f, 1f);
                }
                if (AudioScare.instance != null && seenSound != null) AudioScare.instance.PlayCustom(seenSound, 0.3f, 60f);
                if (ValuableDiscover.instance != null && grab != null) ValuableDiscover.instance.New(grab, ValuableDiscoverGraphic.State.Bad, null);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: the 'seen' effect failed: {e}"); }
            seenEffect = 2f;
            R.Info("Alter Mimic: (this game) you saw the head: the dead-teammate effect plays.");
        }

        private void SeenEffectTick()
        {
            seenEffect -= Time.deltaTime;
            try
            {
                if (CameraZoom.Instance != null) CameraZoom.Instance.OverrideZoomSet(75f, 0.1f, 0.25f, 0.25f, gameObject, 150);
                if (PostProcessing.Instance != null)
                {
                    PostProcessing.Instance.VignetteOverride(Color.black, 0.4f, 1f, 1f, 0.5f, 0.1f, gameObject, 999);
                    PostProcessing.Instance.SaturationOverride(-50f, 1f, 0.5f, 0.1f, gameObject, 999);
                    PostProcessing.Instance.ContrastOverride(5f, 1f, 0.5f, 0.1f, gameObject, 999);
                }
                if (GameDirector.instance != null)
                {
                    GameDirector.instance.CameraImpact.Shake(10f * Time.deltaTime, 0.1f);
                    GameDirector.instance.CameraShake.Shake(10f * Time.deltaTime, 1f);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: the 'seen' effect failed: {e}"); seenEffect = 0f; }
        }
    }
}
