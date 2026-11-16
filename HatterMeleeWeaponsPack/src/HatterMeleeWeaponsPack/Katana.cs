using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterMeleeWeaponsPack
{
    /// <summary>
    /// The Katana's rift and wave. Everything else (holding, turning, the scroll-out swing, the hurt box, durability) is
    /// the vanilla sword's own code; only the hurt box's damage is changed to BladeDamage, for every hit.
    ///
    /// The holder presses E (the game's ItemToggle, added by KatanaBuilder) to charge the katana: the sharp half of the
    /// blade glows red on everyone's screen. While charged, a quick stroke of the blade draws a rift in the air along
    /// the blade's path (the host follows the blade's movement round the holder's eyes, so walking draws nothing, and
    /// any direction counts). The stroke ends when the blade slows to half of RiftDrawSpeed, or when the rift reaches
    /// RiftMaxLength (it never grows longer). A rift of at least RiftMinLength then opens and hangs in the air for
    /// RiftDuration seconds, building up; then its wave (<see cref="KatanaWave"/>) launches out of its middle, with
    /// the slash sound, and the rift vanishes at once. Each rift keeps its own timing, so a chain's rifts launch one after
    /// another. A shorter rift fizzles out.
    ///
    /// One charge makes a chain of up to ChainLength rifts: after each one opens, the katana stays charged, and a
    /// stroke starting ChainWindowStart to ChainWindowEnd seconds later draws the next (sooner strokes draw nothing).
    /// The chain's later rifts are centred on the first rift's middle: drawn with their own stroke's shape and angle,
    /// but moved so their middle sits there (they grow out from it as they're drawn), and their waves come out of that
    /// same point, tilted to their own rift. Only the first rift uses energy (WaveEnergy bars). The chain ends, and the katana is uncharged (stops glowing),
    /// after its last rift, when no stroke starts within ChainWindowEnd, or if the holder lets go or presses E. Then E
    /// is switched off (no prompt) for WaveCooldown seconds from the last wave, and whenever there isn't energy.
    ///
    /// Networking: the host decides everything and sends the rift's points as they're drawn (every few centimetres)
    /// and its end (with the wave, if it opened); every game draws the rift and flies its own wave, and only the
    /// host's wave deals damage. Enemies a wave hits get a red X (<see cref="KatanaImpactMark"/>), sent from here.
    /// </summary>
    internal class Katana : MonoBehaviour
    {
        // set by KatanaBuilder on the prefab (serialized, so they survive the game cloning it)
        public Vector3 bladeBase;          // item space
        public Vector3 bladeTip;           // item space
        public ItemMelee soundTemplate;
        public MeshRenderer bladeRenderer; // the katana model
        public int[] glowSlots;            // its materials that glow when charged (the blade's sharp half)

        private const string SlashFile = "katana_slash.pcm";   // "Sword Slash and Swing" by David Dumais Audio (Pixabay, per the file name)
        private const float DrawPointAlong = 0.75f;   // the point on the blade that draws the rift (0 = guard, 1 = tip)
        private const float PointSpacing = 0.03f;     // the rift's points are sent every this many metres
        private const float EndGrace = 0.08f;         // the stroke survives this long below half the draw speed
        private const float MaxStepMetres = 0.6f;     // a bigger jump in one physics step is a teleport, not a stroke
        private const float ScrollSwingIgnore = 0.6f; // after a scroll swing starts, the blade's movement draws nothing
        private const float WaveAhead = 0.15f;        // the wave's leading edge starts this far in front of the rift

        private PhysGrabObject physGrabObject;
        private PhotonView photonView;
        private HurtCollider hurt;
        private Sound slashSound;
        private ItemBattery battery;
        private ItemToggle toggle;

        // every game: the charged look, and the rift being drawn
        private bool charged;
        private Material[] plainMaterials;
        private Light glowLight;
        private static Material glowMaterial;
        private KatanaRift rift;
        private int riftId = -1;

        // host: the stroke
        private bool haveLast;
        private Vector3 lastRel, lastWorld, lastSent;
        private bool drawing;
        private float strokeLength, slowTime;
        private int strokeId;
        private readonly List<Vector3> strokePoints = new List<Vector3>();
        private PlayerAvatar strokeHolder;
        private Transform strokeEyes;
        private float nextWave, nextRefuseLog;
        private bool chargeBlocked;
        private int chainCount;             // rifts opened so far on this charge (0 = none yet)
        private Vector3 chainCentre;        // the middle of the chain's first rift: the later ones are centred there
        private float lastRiftAt;
        internal float scrollSwingAt = -10f;

        private void Start()
        {
            physGrabObject = GetComponent<PhysGrabObject>();
            photonView = GetComponent<PhotonView>();
            hurt = GetComponentInChildren<HurtCollider>(true);
            battery = GetComponent<ItemBattery>();
            toggle = GetComponent<ItemToggle>();
            if (hurt != null) hurt.enemyDamage = Settings.KatanaBladeDamage.Value;
            slashSound = MakeSound(SlashFile, 0.9f);
            if (bladeRenderer != null) plainMaterials = bladeRenderer.sharedMaterials;

            var lightObject = new GameObject("Katana Glow Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = Vector3.Lerp(bladeBase, bladeTip, 0.6f);
            glowLight = lightObject.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = new Color(0.85f, 0.04f, 0.04f);
            glowLight.range = 1.6f;
            glowLight.intensity = 0f;
            glowLight.enabled = false;
        }

        private void OnDestroy()
        {
            if (rift != null) rift.Fizzle();
        }

        private Vector3 DrawPoint => transform.TransformPoint(Vector3.Lerp(bladeBase, bladeTip, DrawPointAlong));

        private PlayerAvatar Holder()
        {
            if (physGrabObject == null || physGrabObject.playerGrabbing == null) return null;
            foreach (PhysGrabber g in physGrabObject.playerGrabbing)
                if (g != null && g.playerAvatar != null) return g.playerAvatar;
            return null;
        }

        private static Transform Eyes(PlayerAvatar p) =>
            p != null && p.PlayerVisionTarget != null && p.PlayerVisionTarget.VisionTransform != null ? p.PlayerVisionTarget.VisionTransform : null;

        private static Vector3 EyePosition(PlayerAvatar p, Transform eyes) =>
            eyes != null ? eyes.position : p.transform.position + Vector3.up * 1.5f;

        // ---------------------------------------------------------------- the charged look (every game)

        private void Update()
        {
            bool want = toggle != null && toggle.toggleState;
            if (want != charged) { charged = want; ApplyGlow(); }
            if (charged)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
                if (GlowMaterial != null) GlowMaterial.color = Color.Lerp(new Color(0.55f, 0.01f, 0.02f), new Color(0.95f, 0.06f, 0.05f), pulse);
                if (glowLight != null) glowLight.intensity = 1.2f + 0.8f * pulse;
            }
        }

        /// <summary>Swaps the blade's sharp half to the glowing red material while charged, and back.</summary>
        private void ApplyGlow()
        {
            if (glowLight != null) glowLight.enabled = charged;
            if (bladeRenderer == null || plainMaterials == null || glowSlots == null) return;
            if (!charged || GlowMaterial == null) { bladeRenderer.sharedMaterials = plainMaterials; return; }
            var mats = (Material[])plainMaterials.Clone();
            foreach (int slot in glowSlots) if (slot >= 0 && slot < mats.Length) mats[slot] = GlowMaterial;
            bladeRenderer.sharedMaterials = mats;
        }

        /// <summary>Unlit red (Sprites/Default ignores lighting, so it glows in the dark), pulsed in Update.</summary>
        private static Material GlowMaterial
        {
            get
            {
                if (glowMaterial != null) return glowMaterial;
                Shader s = Shader.Find("Sprites/Default");
                if (s == null) return null;
                glowMaterial = new Material(s) { name = "HMWP Katana Glow", color = new Color(0.85f, 0.04f, 0.04f) };
                glowMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return glowMaterial;
            }
        }

        // ---------------------------------------------------------------- the stroke (host)

        private void FixedUpdate()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (hurt != null && hurt.enemyDamage != Settings.KatanaBladeDamage.Value) hurt.enemyDamage = Settings.KatanaBladeDamage.Value;
            UpdateChargeAllowed();

            bool armed = toggle != null && toggle.toggleState;
            PlayerAvatar holder = Holder();
            if (!armed || holder == null)
            {
                if (drawing) EndStroke(false, "the katana was let go or uncharged");
                if (chainCount > 0) EndChain(holder == null ? "let go" : "uncharged");
                haveLast = false;
                return;
            }
            if (chainCount > 0 && !drawing && Time.time > lastRiftAt + Settings.KatanaChainWindowEnd.Value)
            {
                EndChain($"no stroke within {Settings.KatanaChainWindowEnd.Value:0.0} s");
                haveLast = false;
                return;
            }
            Transform eyes = Eyes(holder);
            Vector3 world = DrawPoint;
            Vector3 rel = world - EyePosition(holder, eyes);   // round the eyes: walking moves both, so it draws nothing
            if (!haveLast) { haveLast = true; lastRel = rel; lastWorld = world; return; }

            float dt = Time.fixedDeltaTime;
            float dist = (rel - lastRel).magnitude;
            float speed = dist / dt;
            float drawSpeed = Settings.KatanaRiftDrawSpeed.Value;
            if (dist > MaxStepMetres || Time.time - scrollSwingAt < ScrollSwingIgnore)
            {
                if (drawing) EndStroke(false, "a teleport or the scroll swing");
            }
            else if (!drawing)
            {
                // in a chain, strokes right after the last rift don't count
                bool tooSoon = chainCount > 0 && Time.time < lastRiftAt + Settings.KatanaChainWindowStart.Value;
                if (speed >= drawSpeed && !tooSoon) StartStroke(holder, eyes, dist, world);
            }
            else if (speed >= drawSpeed * 0.5f)
            {
                slowTime = 0f;
                float max = Settings.KatanaRiftMaxLength.Value;
                float add = Mathf.Min(dist, max - strokeLength);
                // the rift never grows past RiftMaxLength: cut this step short where it reaches it
                Vector3 point = add < dist && dist > 1e-5f ? Vector3.Lerp(lastWorld, world, add / dist) : world;
                strokeLength += add;
                bool full = strokeLength >= max - 1e-4f;
                AddPoint(point, full);
                if (full) EndStroke(true, null);
            }
            else
            {
                slowTime += dt;
                if (slowTime > EndGrace) EndStroke(strokeLength >= Settings.KatanaRiftMinLength.Value, null);
            }
            lastRel = rel;
            lastWorld = world;
        }

        /// <summary>Host: E is only offered (and a charge only kept) when a wave could go out: not cooling down, energy enough.</summary>
        private void UpdateChargeAllowed()
        {
            if (toggle == null) return;
            // (in a chain, the charge stays: its rifts after the first are free)
            string why = chainCount > 0 ? null : Time.time < nextWave ? "cooling down" : !HasEnergy() ? "not enough energy" : null;
            if (why != null && toggle.toggleState)
            {
                toggle.ToggleItem(false, -1);
                if (Settings.KatanaDebugLog.Value && Time.time >= nextRefuseLog) { nextRefuseLog = Time.time + 1f; Plugin.Log.LogInfo($"Katana: can't charge, {why}."); }
            }
            bool blocked = why != null;
            if (blocked != chargeBlocked)
            {
                chargeBlocked = blocked;
                try { toggle.ToggleDisable(blocked); }   // hides "[E]" and ignores the key on every game
                catch (System.Exception e) { Plugin.Log.LogWarning($"Katana: switching E {(blocked ? "off" : "on")} failed: {e.Message}"); }
            }
        }

        private void StartStroke(PlayerAvatar holder, Transform eyes, float dist, Vector3 world)
        {
            drawing = true;
            slowTime = 0f;
            strokeId++;
            strokeHolder = holder;
            strokeEyes = eyes;
            strokePoints.Clear();
            strokeLength = Mathf.Min(dist, Settings.KatanaRiftMaxLength.Value);
            AddPoint(lastWorld, true);
            AddPoint(world, true);
        }

        /// <summary>Host: one more point on the rift (sent to everyone when it's PointSpacing on from the last, or forced).</summary>
        private void AddPoint(Vector3 point, bool force)
        {
            strokePoints.Add(point);
            if (!force && (point - lastSent).sqrMagnitude < PointSpacing * PointSpacing) return;
            lastSent = point;
            bool centred = chainCount > 0;   // a later rift in the chain: shown centred on the first one's middle
            if (SemiFunc.IsMultiplayer() && photonView != null)
                photonView.RPC(nameof(HMWPKatanaRiftRPC), RpcTarget.All, strokeId, point, centred, chainCentre);
            else RiftPoint(strokeId, point, centred, chainCentre);
        }

        /// <summary>Host: the stroke is over. A long enough rift opens and its wave comes out; otherwise it fizzles.</summary>
        private void EndStroke(bool release, string why)
        {
            drawing = false;
            slowTime = 0f;
            int id = strokeId;
            if (release && chainCount == 0 && (Time.time < nextWave || !HasEnergy())) { release = false; why = "cooling down or out of energy"; }
            if (!release || strokePoints.Count < 2 || strokeHolder == null)
            {
                if (Settings.KatanaDebugLog.Value)
                    Plugin.Log.LogInfo($"Katana: rift of {strokeLength:0.00} m fizzled ({why ?? $"shorter than RiftMinLength {Settings.KatanaRiftMinLength.Value:0.00} m"}).");
                SendEnd(id, false, Vector3.zero, Quaternion.identity, 0f, 0f);
                return;
            }

            // the rift's middle (halfway along its length; for later rifts in a chain, the first one's) and its line
            // (first point to last: its own stroke's angle)
            Vector3 middle = chainCount > 0 ? chainCentre : PointAlong(0.5f);
            if (chainCount == 0) chainCentre = middle;
            Vector3 line = strokePoints[strokePoints.Count - 1] - strokePoints[0];
            Vector3 eyePos = EyePosition(strokeHolder, strokeEyes);
            Vector3 look = strokeEyes != null ? strokeEyes.forward : strokeHolder.transform.forward;

            // outwards: the level direction from the eyes through the rift's middle (the look direction if that's unclear)
            Vector3 flat = Vector3.ProjectOnPlane(middle - eyePos, Vector3.up);
            if (flat.sqrMagnitude < 0.04f) flat = Vector3.ProjectOnPlane(look, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = strokeHolder.transform.forward;
            flat.Normalize();
            // up or down as the holder looks, but no steeper than 35°
            Vector3 lookFlat = Vector3.ProjectOnPlane(look, Vector3.up);
            float pitch = lookFlat.sqrMagnitude > 1e-4f ? Mathf.Clamp(Vector3.Angle(lookFlat, look) * Mathf.Sign(look.y), -35f, 35f) : 0f;
            Vector3 right = Vector3.Cross(Vector3.up, flat).normalized;
            Vector3 forward = Quaternion.AngleAxis(-pitch, right) * flat;
            // the wave's tips along the rift's line, seen from behind; level if that's unclear
            Vector3 chord = Vector3.ProjectOnPlane(line, forward);
            if (chord.sqrMagnitude < 0.0025f) chord = right;
            chord.Normalize();
            Vector3 up = Vector3.Cross(forward, chord);
            if (up.y < 0f) up = -up;
            Quaternion rotation = Quaternion.LookRotation(forward, up);
            Vector3 origin = middle + forward * WaveAhead;
            float span = Mathf.Clamp(strokeLength * Settings.KatanaWaveSpanPerMetre.Value,
                                     Settings.KatanaWaveSpanMin.Value, Mathf.Max(Settings.KatanaWaveSpanMin.Value, Settings.KatanaWaveSpanMax.Value));

            if (chainCount == 0) SpendEnergy();   // the chain's first rift pays for the whole chain
            chainCount++;
            lastRiftAt = Time.time;
            if (Settings.KatanaDebugLog.Value)
                Plugin.Log.LogInfo($"Katana: rift {chainCount} of up to {Settings.KatanaChainLength.Value}, {strokeLength:0.00} m, opened; " +
                                   $"wave {span:0.0} m wide, tilted {Vector3.SignedAngle(right, chord, forward):0}°.");
            SendEnd(id, true, origin, rotation, span, Settings.KatanaRiftDuration.Value);
            if (chainCount >= Settings.KatanaChainLength.Value) EndChain("its last rift");
        }

        /// <summary>Host: the chain is over: uncharged (the glow goes out) and the cooldown runs from its last wave.</summary>
        private void EndChain(string why)
        {
            if (Settings.KatanaDebugLog.Value) Plugin.Log.LogInfo($"Katana: chain of {chainCount} rift(s) over ({why}).");
            chainCount = 0;
            nextWave = lastRiftAt + Settings.KatanaWaveCooldown.Value;
            if (toggle != null && toggle.toggleState) toggle.ToggleItem(false, -1);
            UpdateChargeAllowed();
        }

        /// <summary>The point a fraction of the way along the stroke's length.</summary>
        private Vector3 PointAlong(float fraction)
        {
            float total = 0f;
            for (int i = 1; i < strokePoints.Count; i++) total += Vector3.Distance(strokePoints[i - 1], strokePoints[i]);
            float target = total * fraction, run = 0f;
            for (int i = 1; i < strokePoints.Count; i++)
            {
                float seg = Vector3.Distance(strokePoints[i - 1], strokePoints[i]);
                if (run + seg >= target) return seg > 1e-6f ? Vector3.Lerp(strokePoints[i - 1], strokePoints[i], (target - run) / seg) : strokePoints[i];
                run += seg;
            }
            return strokePoints[strokePoints.Count - 1];
        }

        /// <summary>Host: the stroke's end, to everyone. delay = how long the rift hangs before its wave launches (the host's setting, so all games agree).</summary>
        private void SendEnd(int id, bool opened, Vector3 origin, Quaternion rotation, float span, float delay)
        {
            if (SemiFunc.IsMultiplayer() && photonView != null)
                photonView.RPC(nameof(HMWPKatanaRiftEndRPC), RpcTarget.All, id, opened, origin, rotation, span, delay);
            else RiftEnd(id, opened, origin, rotation, span, delay);
        }

        // ---------------------------------------------------------------- the rift and the wave (every game)

        [PunRPC]
        public void HMWPKatanaRiftRPC(int id, Vector3 point, bool centred, Vector3 centre, PhotonMessageInfo info)
        {
            if (info.Sender != null && !info.Sender.IsMasterClient) return;   // only the host draws rifts
            RiftPoint(id, point, centred, centre);
        }

        [PunRPC]
        public void HMWPKatanaRiftEndRPC(int id, bool opened, Vector3 origin, Quaternion rotation, float span, float delay, PhotonMessageInfo info)
        {
            if (info.Sender != null && !info.Sender.IsMasterClient) return;
            RiftEnd(id, opened, origin, rotation, span, delay);
        }

        private void RiftPoint(int id, Vector3 point, bool centred, Vector3 centre)
        {
            if (rift == null || riftId != id)
            {
                if (rift != null) rift.Fizzle();
                rift = KatanaRift.Create();
                riftId = id;
                if (centred) rift.CentreOn(centre);
            }
            rift.Add(point);
        }

        /// <summary>
        /// Every game: the stroke is over. An opened rift hangs for `delay` seconds, then launches its wave (with the
        /// sound) and vanishes; the rift itself keeps the time, so each rift in a chain launches on its own. (If this game
        /// missed the rift's points, an invisible one still keeps the time, so the wave isn't lost.)
        /// </summary>
        private void RiftEnd(int id, bool opened, Vector3 origin, Quaternion rotation, float span, float delay)
        {
            KatanaRift r = rift != null && riftId == id ? rift : null;
            if (r != null) rift = null;   // the next stroke draws a new rift
            if (!opened) { if (r != null) r.Fizzle(); return; }
            if (r == null) r = KatanaRift.Create();

            bool host = SemiFunc.IsMasterClientOrSingleplayer();
            PlayerAvatar slasher = host ? Holder() : null;   // (never hit by their own wave)
            PhysGrabObject self = physGrabObject;
            Sound sound = slashSound;
            r.Open(delay, () =>
            {
                if (sound != null)
                {
                    try { sound.Play(origin, 1f, 1f, 1f, 1f); }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Katana sound failed: {e.Message}"); }
                }
                KatanaWave.Spawn(origin, rotation, span, host, self, slasher);
            });
        }

        // ---------------------------------------------------------------- energy (host)

        private int BarsLeft => battery == null || battery.batteryBars <= 0 ? 0
                                : Mathf.RoundToInt(battery.batteryLife / (100f / battery.batteryBars));

        private bool HasEnergy() => battery == null || Settings.KatanaWaveEnergy.Value <= 0 || BarsLeft >= Settings.KatanaWaveEnergy.Value;

        /// <summary>Host: the game's battery is host-run and sends its new level to everyone itself.</summary>
        private void SpendEnergy()
        {
            int bars = Settings.KatanaWaveEnergy.Value;
            if (battery == null || bars <= 0) return;
            try { battery.RemoveFullBar(bars); battery.OverrideBatteryShow(2f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Katana: using energy failed: {e.Message}"); }
            if (Settings.KatanaDebugLog.Value) Plugin.Log.LogInfo($"Katana: wave used {bars} energy, {BarsLeft} of {battery.batteryBars} left.");
        }

        // ---------------------------------------------------------------- the X on enemies a wave hits

        /// <summary>Host: a wave hit something at this point; everyone sees a red X there.</summary>
        internal void ShowImpact(Vector3 point, float size)
        {
            if (SemiFunc.IsMultiplayer() && photonView != null) photonView.RPC(nameof(HMWPKatanaImpactRPC), RpcTarget.All, point, size);
            else KatanaImpactMark.Spawn(point, size);
        }

        [PunRPC]
        public void HMWPKatanaImpactRPC(Vector3 point, float size, PhotonMessageInfo info)
        {
            if (info.Sender != null && !info.Sender.IsMasterClient) return;
            KatanaImpactMark.Spawn(point, size);
        }

        private Sound MakeSound(string file, float volume)
        {
            AudioClip clip = SoundLibrary.Get(file);
            ItemMelee source = soundTemplate != null ? soundTemplate : GetComponent<ItemMelee>();
            if (clip == null || source == null || source.soundSwing == null) return null;
            var s = new Sound();
            Sound.CopySound(source.soundSwing, s);
            s.Sounds = new[] { clip };
            s.Volume = volume;
            s.VolumeRandom = 0f;
            s.Pitch = 1f;
            s.PitchRandom = 0.05f;
            return s;
        }
    }

    /// <summary>The sword's scroll-out swing on the katana: noted, so its movement doesn't draw a rift.</summary>
    [HarmonyPatch(typeof(ItemMelee), "ScrollSwingStartRPC")]
    internal static class KatanaScrollPatch
    {
        private static void Postfix(ItemMelee __instance, int _direction)
        {
            Katana katana = __instance != null ? __instance.GetComponent<Katana>() : null;
            if (katana == null) return;
            katana.scrollSwingAt = Time.time;
            if (Settings.KatanaDebugLog.Value) Plugin.Log.LogInfo($"Katana: the sword's scroll swing started ({(_direction > 0 ? "out" : "in")}).");
        }
    }
}
