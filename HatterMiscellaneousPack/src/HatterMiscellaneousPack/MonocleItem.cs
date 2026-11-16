using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// The All-Seeing Monocle. Pressing [E] while holding it (the game's ItemToggle, synced to everyone) looks through it.
    ///  - Host: charges. Turning it on in a level with at least one charge left costs a charge at once (so stopping early
    ///    still costs it), then another at the start of every further SecondsPerCharge. With no charge left at that point,
    ///    or outside a level, or once the player who switched it on lets go, it switches the toggle back off.
    ///  - The player who switched it on: <see cref="MonocleView"/> flies the monocle up to their eye until it fills the
    ///    screen, then shows the valuables through walls; switching off reverses it.
    ///  - Everyone else: the monocle flies up in front of that player's face, growing until it covers both eyes, while they look through it, and back after.
    /// </summary>
    public class MonocleItem : MonoBehaviour
    {
        // serialized, set by MonocleBuilder
        public Transform visual;
        public Vector3 lensCentre, lensNormal;     // in the visual's own space (before its scale)
        public float lensRadius;

        public int modelLayer;

        /// <summary>Set by MonocleView while the monocle is "at your eye" (its model is hidden on purpose then).</summary>
        internal bool hiddenByView;
        private ItemEquippable equippable;
        private GameObject display;                        // the model, drawn as its own object (see LateUpdate)
        private Vector3 displayPos, displayScale;
        private Quaternion displayRot;
        private static readonly FieldInfo EquippedField = AccessTools.Field(typeof(ItemEquippable), "isEquipped");

        /// <summary>What the player sees of the monocle (the separate model, or the built-in one if that failed).</summary>
        internal Transform Display => display != null ? display.transform : visual;

        private ItemToggle toggle;
        private ItemBattery battery;
        private PhysGrabObject physGrabObject;
        private bool hostActive, localActive;
        private float nextCharge, letGoTime = -1f, rejectedUntil;

        private static readonly FieldInfo ToggleIdField = AccessTools.Field(typeof(ItemToggle), "playerTogglePhotonID");

        private void Awake()
        {
            toggle = GetComponent<ItemToggle>();
            battery = GetComponent<ItemBattery>();
            physGrabObject = GetComponent<PhysGrabObject>();
            equippable = GetComponent<ItemEquippable>();
        }

        /// <summary>
        /// The model under the drone item never showed in game (twice), so it's drawn as a separate scene object instead:
        /// a copy of it, on the layer the drone's own model used, that follows the item every frame (position, turn and
        /// size, so it shrinks with the item into an inventory slot). The model under the item is switched off.
        /// </summary>
        private void Start()
        {
            if (visual == null) { Plugin.Log.LogWarning($"{MonocleBuilder.DisplayName}: no model on this copy."); return; }
            displayPos = visual.localPosition;
            displayRot = visual.localRotation;
            displayScale = visual.localScale;
            try
            {
                display = Instantiate(visual.gameObject);
                display.name = "All-Seeing Monocle (model)";
                SetLayer(display.transform, modelLayer);
                display.SetActive(true);
                foreach (Renderer r in display.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                visual.gameObject.SetActive(false);
                Follow();
                if (Settings.MonocleDebugLog.Value)
                    Plugin.Log.LogInfo($"{MonocleBuilder.DisplayName}: model drawn as its own object, {display.GetComponentsInChildren<Renderer>().Length} piece(s), " +
                                       $"layer '{LayerMask.LayerToName(modelLayer)}', size {display.transform.lossyScale.x:0.000}, at {display.transform.position}.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"{MonocleBuilder.DisplayName}: drawing the model separately failed, keeping it on the item: {e}");
                if (display != null) Destroy(display);
                display = null;
                visual.gameObject.SetActive(true);
            }
        }

        private void LateUpdate() => Follow();

        private void Follow()
        {
            if (display == null) return;
            bool equipped = equippable != null && EquippedField != null && (bool)EquippedField.GetValue(equippable);
            bool show = isActiveAndEnabled && !hiddenByView && !equipped && transform.lossyScale.sqrMagnitude > 1e-6f;
            LogWearer(Wearer(), show, equipped);
            if (display.activeSelf != show) display.SetActive(show);
            if (!show) return;
            Transform d = display.transform;
            Vector3 pos = transform.TransformPoint(displayPos);
            Quaternion rot = transform.rotation * displayRot;
            Vector3 scale = Vector3.Scale(transform.lossyScale, displayScale);

            // everyone else's game: while someone looks through it, the monocle flies up in front of that player's face, growing
            // (their own game shows the view instead; see MonocleView)
            PlayerAvatar wearer = Wearer();
            float target = wearer != null ? 1f : 0f;
            float speed = 1f / Mathf.Max(0.05f, Settings.MonocleZoomSeconds.Value);
            wornBlend = Mathf.MoveTowards(wornBlend, target, speed * Time.deltaTime);
            if (wearer != null) lastWearer = wearer;
            if (wornBlend > 0f && lastWearer != null && FacePose(lastWearer, out Vector3 facePos, out Quaternion faceRot, out Vector3 faceScale))
            {
                float k = wornBlend * wornBlend * (3f - 2f * wornBlend);   // smooth in and out
                pos = Vector3.Lerp(pos, facePos, k);
                rot = Quaternion.Slerp(rot, faceRot, k);
                scale = Vector3.Lerp(scale, faceScale, k);
            }
            d.SetPositionAndRotation(pos, rot);
            d.localScale = scale;
        }

        private const float FaceLensRadius = 0.09f;   // m: wide enough to cover both eyes
        private const float FaceDistance = 0.14f;     // m in front of the eyes, so the bigger lens clears the face
        private float wornBlend;
        private PlayerAvatar lastWearer;
        private string lastWearerLog;

        /// <summary>Logs (always, on change only) what this game sees of who's wearing the monocle.</summary>
        private void LogWearer(PlayerAvatar wearer, bool shown, bool equipped)
        {
            bool on = toggle != null && toggle.toggleState;
            int id = Toggler, local = SemiFunc.PhotonViewIDPlayerAvatarLocal();
            string s = $"on {on}, toggled by {id}, this game's player {local}, in level {SemiFunc.RunIsLevel()}, model shown {shown} (equipped {equipped}, hidden by own view {hiddenByView}), wearer " +
                       (wearer != null ? wearer.name : "none");
            if (s == lastWearerLog) return;
            lastWearerLog = s;
            string eye = wearer == null ? "" : (wearer.PlayerVisionTarget != null && wearer.PlayerVisionTarget.VisionTransform != null
                ? $", eye at {wearer.PlayerVisionTarget.VisionTransform.position}" : ", no eye transform (head height used)");
            Plugin.Log.LogInfo($"{MonocleBuilder.DisplayName} (others' view): {s}{eye}, model at {(display != null ? display.transform.position.ToString() : "none")}.");
        }

        /// <summary>The player looking through it right now, if that's not this game's own player.</summary>
        private PlayerAvatar Wearer()
        {
            if (toggle == null || !toggle.toggleState || !SemiFunc.RunIsLevel()) return null;
            int id = Toggler;
            if (id < 0 || id == SemiFunc.PhotonViewIDPlayerAvatarLocal()) return null;
            return SemiFunc.PlayerAvatarGetFromPhotonID(id);
        }

        /// <summary>
        /// In front of the player's face, centred between the eyes, lens facing out where they look, grown to about
        /// 18 cm across so it covers both eyes (it grows on the way up, see Follow).
        /// </summary>
        private bool FacePose(PlayerAvatar p, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = default; rot = Quaternion.identity; scale = Vector3.one;
            if (p == null) return false;
            Transform eye = p.PlayerVisionTarget != null ? p.PlayerVisionTarget.VisionTransform : null;
            Vector3 at = eye != null ? eye.position : p.transform.position + Vector3.up * 1.5f;
            rot = eye != null ? eye.rotation : p.transform.rotation;
            pos = at + rot * new Vector3(0f, 0f, FaceDistance);
            // the visual's lens normal is its +Z, so the item's own model turn (displayRot) isn't wanted here
            scale = Vector3.one * (FaceLensRadius / Mathf.Max(0.001f, lensRadius));
            return true;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        private int Toggler => toggle != null && ToggleIdField != null ? (int)ToggleIdField.GetValue(toggle) : -1;

        /// <summary>Whole charges left (as the game's battery counts its bars).</summary>
        internal int ChargesLeft
        {
            get
            {
                if (battery == null || battery.batteryBars <= 0) return 0;
                return Mathf.Clamp(Mathf.RoundToInt(battery.batteryLife / (100f / battery.batteryBars)), 0, battery.batteryBars);
            }
        }

        private void Update()
        {
            if (toggle == null) return;
            if (SemiFunc.IsMasterClientOrSingleplayer()) HostUpdate();
            LocalUpdate();
        }

        // ---------------- host: charges and switching off ----------------

        private void HostUpdate()
        {
            bool on = toggle.toggleState;
            if (!on)
            {
                if (hostActive && Settings.MonocleDebugLog.Value) Plugin.Log.LogInfo($"{MonocleBuilder.DisplayName}: switched off ({ChargesLeft} charges left).");
                hostActive = false;
                return;
            }
            if (Time.time < rejectedUntil) return;
            PlayerAvatar user = SemiFunc.PlayerAvatarGetFromPhotonID(Toggler);
            if (user == null && !SemiFunc.IsMultiplayer()) user = PlayerAvatar.instance;

            if (!hostActive)
            {
                string why = null;
                if (!SemiFunc.RunIsLevel()) why = "not in a level";
                else if (ChargesLeft <= 0) why = "no charge left";
                else if (user == null || !HeldBy(user)) why = "not held by the player who pressed it";
                if (why != null) { SwitchOff(why); return; }
                hostActive = true;
                letGoTime = -1f;
                SpendCharge();
                nextCharge = Time.time + Settings.MonocleSecondsPerCharge.Value;
                return;
            }

            // let go (a short grace, for grabs that flicker) or someone else's toggle: off
            if (user == null || !HeldBy(user))
            {
                if (letGoTime < 0f) letGoTime = Time.time;
                else if (Time.time - letGoTime > 0.3f) { SwitchOff("let go"); return; }
            }
            else letGoTime = -1f;
            if (!SemiFunc.RunIsLevel()) { SwitchOff("not in a level"); return; }

            if (Time.time >= nextCharge)
            {
                if (ChargesLeft <= 0) { SwitchOff("out of charge"); return; }
                SpendCharge();
                nextCharge += Settings.MonocleSecondsPerCharge.Value;
            }
        }

        private void SpendCharge()
        {
            if (battery != null) battery.RemoveFullBar(1);   // host-run; the game sends the new level to everyone
            if (Settings.MonocleDebugLog.Value) Plugin.Log.LogInfo($"{MonocleBuilder.DisplayName}: a charge used, {ChargesLeft} left.");
        }

        private void SwitchOff(string why)
        {
            rejectedUntil = Time.time + 0.5f;   // the toggle's switch-off may take a moment to come back
            hostActive = false;
            toggle.ToggleItem(false, -1);
            if (Settings.MonocleDebugLog.Value) Plugin.Log.LogInfo($"{MonocleBuilder.DisplayName}: switched off ({why}).");
        }

        private bool HeldBy(PlayerAvatar p)
        {
            if (physGrabObject == null || physGrabObject.playerGrabbing == null) return false;
            foreach (PhysGrabber g in physGrabObject.playerGrabbing)
                if (g != null && g.playerAvatar == p) return true;
            return false;
        }

        // ---------------- the user's game: the view ----------------

        private void LocalUpdate()
        {
            bool want = toggle.toggleState && Toggler == SemiFunc.PhotonViewIDPlayerAvatarLocal()
                        && SemiFunc.RunIsLevel() && (physGrabObject == null || physGrabObject.grabbedLocal);
            // don't start the show when the host is sure to refuse (it'll switch the toggle off in a moment)
            if (want && !localActive && ChargesLeft <= 0) want = false;
            if (want == localActive) return;
            localActive = want;
            if (want) MonocleView.Begin(this);
            else MonocleView.End(this);
        }

        private void OnDisable()
        {
            if (display != null) display.SetActive(false);
            if (localActive) { localActive = false; MonocleView.End(this); }
        }

        private void OnDestroy()
        {
            if (display != null) Destroy(display);
            if (localActive) { localActive = false; MonocleView.End(this); }
        }
    }
}
