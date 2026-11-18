using Photon.Pun;
using TMPro;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>Marker on the Orb Box's inside-box checker; Patches swaps its filter to EnemyValuable.</summary>
    public class OrbCartChecker : MonoBehaviour
    {
    }

    /// <summary>
    /// Lives on the grafted Hauler box (same GameObject as its PhotonView and ItemValuableBox).
    /// Applies the configured capacity and keeps it in sync from the host to clients.
    /// </summary>
    public class OrbCartBox : MonoBehaviour
    {
        internal ItemValuableBox Box { get; private set; }
        private PhotonView photonView;

        // Pose fitted by OrbCartBuilder (copied from the prefab). The game only ever rotates/scales the hatches,
        // so any change to these is something else moving the box; it's put back every frame.
        private Vector3 fittedPosition, fittedScale;
        private Quaternion fittedRotation;
        private Transform hatch1, hatch2;
        private Vector3 hatch1Position, hatch2Position;
        private bool driftLogged;

        private void Awake()
        {
            Box = GetComponent<ItemValuableBox>();
            photonView = GetComponent<PhotonView>();
            // Read the live config so REPOConfig edits apply to newly spawned carts.
            if (Box != null) Box.valueLimit = Settings.OrbCartCapacity.Value;

            fittedPosition = transform.localPosition;
            fittedRotation = transform.localRotation;
            fittedScale = transform.localScale;
            if (Box != null)
            {
                hatch1 = Box.hatch1Transform;
                hatch2 = Box.hatch2Transform;
                if (hatch1 != null) hatch1Position = hatch1.localPosition;
                if (hatch2 != null) hatch2Position = hatch2.localPosition;
            }
        }

        private void LateUpdate() => HoldPose("LateUpdate");

        private void FixedUpdate() => HoldPose("FixedUpdate");

        private void HoldPose(string when)
        {
            Transform t = transform;
            bool moved = (t.localPosition - fittedPosition).sqrMagnitude > 1e-6f
                         || Quaternion.Angle(t.localRotation, fittedRotation) > 0.01f
                         || (t.localScale - fittedScale).sqrMagnitude > 1e-6f;
            bool hatchMoved = (hatch1 != null && (hatch1.localPosition - hatch1Position).sqrMagnitude > 1e-6f)
                              || (hatch2 != null && (hatch2.localPosition - hatch2Position).sqrMagnitude > 1e-6f);
            if (!moved && !hatchMoved) return;

            if (!driftLogged)
            {
                driftLogged = true;
                Plugin.Log.LogWarning(
                    $"Orb Box moved off its fitted spot (seen in {when}, host: {SemiFunc.IsMasterClientOrSingleplayer()}): " +
                    $"box pos {t.localPosition} (fitted {fittedPosition}), scale {t.localScale} (fitted {fittedScale}), " +
                    $"rot {t.localEulerAngles} (fitted {fittedRotation.eulerAngles}), parent '{(t.parent != null ? t.parent.name : "none")}'" +
                    (hatchMoved ? $", hatches {hatch1?.localPosition} / {hatch2?.localPosition} (fitted {hatch1Position} / {hatch2Position})" : "") +
                    ". Putting it back.");
            }

            t.localPosition = fittedPosition;
            t.localRotation = fittedRotation;
            t.localScale = fittedScale;
            if (hatch1 != null) hatch1.localPosition = hatch1Position;
            if (hatch2 != null) hatch2.localPosition = hatch2Position;
        }

        private void Start()
        {
            // Only the host decides what fits; tell everyone else the host's capacity so their screens agree.
            // Buffered so players who join later (or receive the cart later) still get it.
            if (Box != null && photonView != null && SemiFunc.IsMultiplayer() && SemiFunc.IsMasterClient())
                photonView.RPC(nameof(OrbCartSetLimitRPC), RpcTarget.OthersBuffered, Box.valueLimit);
        }

        [PunRPC]
        private void OrbCartSetLimitRPC(float limit, PhotonMessageInfo info)
        {
            if (info.Sender != null && !info.Sender.IsMasterClient) return;
            if (Box != null && limit > 0f) Box.valueLimit = limit;
        }

        /// <summary>0..1 share of capacity currently stored.</summary>
        internal float Fill => Box != null && Box.valueLimit > 0f ? Mathf.Clamp01(Box.CurrentValue / Box.valueLimit) : 0f;

        internal bool IsFull => Box != null && Box.CurrentValue >= Box.valueLimit - 0.5f;
    }

    /// <summary>
    /// Drives one of the Orb C.A.R.T.'s screens: the handle screen (vanilla ValueScreen hardware) shows the total value
    /// of the stored orbs in the game's own "$" style; the far-end screen shows how full the cart is.
    /// </summary>
    public class OrbCartScreen : MonoBehaviour
    {
        public TextMeshPro text;
        public bool showValue;
        private OrbCartBox orbBox;
        private string shown;

        private void Start()
        {
            orbBox = GetComponentInChildren<OrbCartBox>(true);
        }

        private void LateUpdate()
        {
            if (text == null || orbBox == null || orbBox.Box == null) return;

            // Floor so the screen only says 100% when it is genuinely full.
            int percent = Mathf.Clamp(Mathf.FloorToInt(orbBox.Fill * 100f + 0.001f), 0, 100);
            string label = showValue
                ? "<color=#bd4300>$</color>" + SemiFunc.DollarGetString(Mathf.Max(0, Mathf.RoundToInt(orbBox.Box.CurrentValue)))
                : (orbBox.IsFull ? "FULL" : percent + "%");

            if (label == shown && text.text == label) return;
            shown = label;
            text.text = label;
            // Value: white like the vanilla screen. Fill: the same green → red ramp the Hauler screen uses.
            text.color = showValue ? Color.white : SemiFunc.ColorDifficultyGet(0f, 100f, percent);
        }
    }
}
