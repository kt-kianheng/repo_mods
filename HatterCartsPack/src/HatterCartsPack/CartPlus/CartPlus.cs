using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>Marks the stasis button's grabbable collider (a copy of the handle bar's collider at the cart's far end).</summary>
    public class CartPlusButton : MonoBehaviour
    {
    }

    /// <summary>
    /// The C.A.R.T. Plus's own logic, on the cart's root.
    /// The host watches the players grabbing the cart. While anyone holds a grab that started on the stasis button,
    /// the cart shrinks back to C.A.R.T. size and stays small; when the last of them lets go, it grows straight back.
    /// No timer and no recharge (user, 18 Nov). The host decides both, so every game shrinks and grows together.
    /// Everything in the basket when it starts shrinking shrinks with it and is locked in place
    /// (<see cref="CartPlusContents"/>). After letting go, the cart only grows back once there's room (<see cref="HasRoom"/>).
    /// </summary>
    public class CartPlus : MonoBehaviour
    {
        // ---- Fixed tuning ---------------------------------------------------------------------------
        private const float ShrinkSeconds = 0.4f;     // shrinking takes this long
        private const float GrowSeconds = 0.15f;      // growing back: near-instant, short enough not to jolt what's around it
        private const float ButtonPadding = 0.04f;    // metres around the button's collider that still count as the button
        private const float RoomMargin = 0.03f;       // metres the room check ignores on each side (touching a wall is fine)
        private const float RoomCheckEvery = 0.1f;    // seconds between room checks while waiting to grow

        private enum State { Big, Shrinking, Small, Growing }

        private PhysGrabObject physGrabObject;
        private PhotonView photonView;
        private BoxCollider button;
        private readonly HashSet<PhysGrabber> grabbers = new HashSet<PhysGrabber>();
        private readonly HashSet<PhysGrabber> pressing = new HashSet<PhysGrabber>();   // grabs that started on the button
        private readonly List<PhysGrabber> gone = new List<PhysGrabber>();

        private State state = State.Big;
        private float size = 1f;                       // 1 = full C.A.R.T. Plus size, 0 = C.A.R.T. size
        private readonly HashSet<Mesh> ownMeshes = new HashSet<Mesh>();
        private CartPlusContents contents;

        private readonly List<Material> buttonMaterials = new List<Material>();
        private int shownVisual = -1;

        private Transform meterFill;
        private Material meterMaterial;
        private static readonly Color MeterColor = new Color(0.3f, 1f, 0.4f);
        private static bool exportedAfterRegrow;

        private readonly List<Material> stripMaterials = new List<Material>();   // the button end's bar/frame light strips
        private float roomTimer;
        private float leaveTimer;
        private Vector3 growShift;          // host: cart-space shift spread over the growth, towards the side with room
        private float shiftApplied;         // host: how much of growShift has been applied (0..1)
        private string lastBlocker;

        private void Awake()
        {
            physGrabObject = GetComponent<PhysGrabObject>();
            photonView = GetComponent<PhotonView>();
            contents = new CartPlusContents(transform, physGrabObject);
            CartPlusButton marker = GetComponentInChildren<CartPlusButton>(true);
            button = marker != null ? marker.GetComponentInChildren<BoxCollider>(true) : null;
            if (button == null) Plugin.Log.LogWarning($"{CartPlusBuilder.DisplayName}: stasis button not found on this cart.");

            meterFill = transform.Find(CartPlusBuilder.MeterName + "/Fill");
            if (meterFill != null)
            {
                Renderer r = meterFill.GetComponent<Renderer>();
                if (r != null) meterMaterial = r.material;   // this cart's own copy
            }

            Transform bar = transform.Find("Cart Handle (Stasis Button)");
            if (bar != null)
            {
                foreach (Renderer r in bar.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material m in r.materials)   // this cart's own copies
                        buttonMaterials.Add(m);
                }
            }

            // The button end's bar and frame have the body's light strips; they follow the button's colour too
            // (user, 18 Nov). Own copies, glow colour only (moving their texture would shift the body's).
            foreach (string name in new[] { CartPlusBuilder.ButtonBarName, "Cart Handle Frame (Stasis Button)" })
            {
                Transform part = transform.Find(name);
                Renderer r = part != null ? part.GetComponent<Renderer>() : null;
                if (r == null) continue;
                foreach (Material m in r.materials) stripMaterials.Add(m);
            }
        }

        private void OnDestroy()
        {
            // Never leave items shrunk and locked if the cart goes away mid-stasis.
            contents?.Release(Vector3.zero);
            foreach (Mesh m in ownMeshes) if (m != null) Destroy(m);
            foreach (Material m in buttonMaterials) if (m != null) Destroy(m);
            foreach (Material m in stripMaterials) if (m != null) Destroy(m);
            if (meterMaterial != null) Destroy(meterMaterial);
        }

        private void Update()
        {
            if (SemiFunc.IsMasterClientOrSingleplayer()) WatchButton();
            Tick(Time.deltaTime);
        }

        // Locked items are put back at their spots after physics and after the game's own network smoothing.
        private void FixedUpdate()
        {
            if (contents != null && contents.Active) contents.Hold(Mathf.SmoothStep(0f, 1f, size));
        }

        private void LateUpdate()
        {
            if (contents != null && contents.Active) contents.Hold(Mathf.SmoothStep(0f, 1f, size));
        }

        // ---- Button (host) ----------------------------------------------------------------------------

        private void WatchButton()
        {
            if (button == null || physGrabObject == null) return;
            List<PhysGrabber> holding = physGrabObject.playerGrabbing;

            gone.Clear();
            foreach (PhysGrabber g in grabbers)
                if (g == null || holding == null || !holding.Contains(g)) gone.Add(g);
            foreach (PhysGrabber g in gone) { grabbers.Remove(g); pressing.Remove(g); }

            if (holding != null)
                foreach (PhysGrabber g in holding)
                    if (g != null && grabbers.Add(g) && g.physGrabPoint != null && OnButton(g.physGrabPoint.position))
                        pressing.Add(g);

            WatchLeavers();

            bool held = pressing.Count > 0;
            if (held && (state == State.Big || state == State.Growing)) StartShrink();
            else if (!held && (state == State.Shrinking || state == State.Small))
            {
                // Let go: grow back only if the full-size cart fits here; otherwise stay small and keep checking.
                roomTimer -= Time.deltaTime;
                if (roomTimer > 0f) return;
                roomTimer = RoomCheckEvery;
                if (FindRoom(out Vector3 shift))
                {
                    lastBlocker = null;
                    growShift = shift;
                    shiftApplied = Mathf.SmoothStep(0f, 1f, size);   // already partly grown (let go mid-shrink): only the rest moves
                    if (shift != Vector3.zero) Plugin.Log.LogInfo($"{CartPlusBuilder.DisplayName}: growing towards the open side (shift {shift}).");
                    Send(nameof(CartPlusGrowRPC));
                }
            }
            else roomTimer = 0f;
        }

        /// <summary>Host, while the cart is small: whatever has left the basket gets its size back, on every game.</summary>
        private void WatchLeavers()
        {
            if (state != State.Small || !contents.Active) return;
            leaveTimer -= Time.deltaTime;
            if (leaveTimer > 0f) return;
            leaveTimer = RoomCheckEvery;
            foreach (Component c in contents.FindLeavers())
            {
                PhotonView view = c is PlayerAvatar p ? p.photonView : c.GetComponent<PhotonView>();
                if (SemiFunc.IsMultiplayer() && photonView != null && view != null) photonView.RPC(nameof(CartPlusDropRPC), RpcTarget.All, view.ViewID);
                else contents.Drop(c);
            }
        }

        private static Component Resolve(int viewId)
        {
            PhotonView view = PhotonView.Find(viewId);
            if (view == null) return null;
            PlayerAvatar player = view.GetComponent<PlayerAvatar>();
            if (player != null) return player;
            return view.GetComponent<PhysGrabObject>();
        }

        [PunRPC]
        private void CartPlusDropRPC(int viewId)
        {
            Component c = Resolve(viewId);
            if (c != null) contents.Drop(c);
        }

        /// <summary>
        /// Host: whether the full-size cart's box (<see cref="CartPlusBuilder.RoomFull"/>, less a small margin) is free of
        /// the level. Only fixed things block it: anything that moves (doors, items, valuables, players, enemies) is
        /// pushed aside as the cart grows, as with any cart.
        /// </summary>
        /// <summary>
        /// Host: where the full-size cart fits. First in place (growing evenly); otherwise shifted across and/or along
        /// by up to as much as each side grows (user, 18 Nov: grow towards whichever side has room), so the cart only grows
        /// out on the open side(s) and the blocked side stays where it is. Smallest shift first.
        /// </summary>
        private bool FindRoom(out Vector3 shift)
        {
            shift = Vector3.zero;
            Vector3 d = CartPlusBuilder.FullDelta;
            if (HasRoom(Vector3.zero)) return true;
            var tries = new List<Vector3>();
            float[] steps = { -1f, -0.5f, 0f, 0.5f, 1f };
            foreach (float fx in steps)
                foreach (float fz in steps)
                    if (fx != 0f || fz != 0f) tries.Add(new Vector3(fx * Mathf.Abs(d.x), 0f, fz * Mathf.Abs(d.z)));
            tries.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
            foreach (Vector3 t in tries)
                if (HasRoom(t)) { shift = t; return true; }
            return false;
        }

        private bool HasRoom(Vector3 localShift)
        {
            Bounds room = CartPlusBuilder.RoomFull;
            if (room.size == Vector3.zero) return true;
            Vector3 half = Vector3.Max(room.extents - Vector3.one * RoomMargin, Vector3.one * 0.01f);
            Vector3 centre = transform.TransformPoint(room.center + localShift);
            foreach (Collider c in Physics.OverlapBox(centre, Vector3.Scale(half, transform.lossyScale), transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c == null || c.transform.IsChildOf(transform)) continue;
                Rigidbody rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                if (c.GetComponentInParent<PhysGrabObject>() != null || c.GetComponentInParent<PlayerAvatar>() != null
                    || c.GetComponentInParent<PlayerController>() != null || c.GetComponentInParent<EnemyParent>() != null) continue;
                if (c.name != lastBlocker)
                {
                    lastBlocker = c.name;
                    Plugin.Log.LogInfo($"{CartPlusBuilder.DisplayName}: no room to grow back yet (blocked by {c.name}, layer {LayerMask.LayerToName(c.gameObject.layer)}).");
                }
                return false;
            }
            return true;
        }

        /// <summary>Whether a world point is on the button: inside its collider, give or take <see cref="ButtonPadding"/>.</summary>
        private bool OnButton(Vector3 world)
        {
            Transform t = button.transform;
            Vector3 p = t.InverseTransformPoint(world) - button.center;
            Vector3 scale = t.lossyScale;
            for (int i = 0; i < 3; i++)
            {
                float pad = ButtonPadding / Mathf.Max(Mathf.Abs(scale[i]), 1e-4f);
                if (Mathf.Abs(p[i]) > Mathf.Abs(button.size[i]) * 0.5f + pad) return false;
            }
            return true;
        }

        /// <summary>Host: picks what's in the basket (unless items are still locked from before) and shrinks on every game.</summary>
        private void StartShrink()
        {
            List<Component> found = state == State.Big && !contents.Active ? contents.Gather() : new List<Component>();
            if (SemiFunc.IsMultiplayer() && photonView != null)
            {
                var ids = new List<int>();
                foreach (Component c in found)
                {
                    PhotonView view = c is PlayerAvatar p ? p.photonView : c.GetComponent<PhotonView>();
                    if (view != null) ids.Add(view.ViewID);
                }
                photonView.RPC(nameof(CartPlusShrinkRPC), RpcTarget.All, (object)ids.ToArray());
            }
            else BeginShrink(found);
        }

        private void Send(string rpc)
        {
            if (SemiFunc.IsMultiplayer() && photonView != null) photonView.RPC(rpc, RpcTarget.All);
            else SendMessage(rpc);
        }

        // ---- Timeline (every game) --------------------------------------------------------------------

        [PunRPC]
        private void CartPlusShrinkRPC(int[] itemViewIds)
        {
            var found = new List<Component>();
            if (itemViewIds != null)
                foreach (int id in itemViewIds)
                {
                    Component c = Resolve(id);
                    if (c != null) found.Add(c);
                }
            BeginShrink(found);
        }

        private void BeginShrink(List<Component> things)
        {
            if (state != State.Big && state != State.Growing) return;
            // Pressed again while growing back: what's still tracked is riding with the cart, so it just shrinks again.
            if (state == State.Big && !contents.Active) contents.Begin(things);
            state = State.Shrinking;
        }

        [PunRPC]
        private void CartPlusGrowRPC()
        {
            if (state != State.Shrinking && state != State.Small) return;
            contents.Tighten();   // what's still loose in the basket rides with the cart while it grows
            state = State.Growing;
        }

        private void Tick(float dt)
        {
            switch (state)
            {
                case State.Shrinking:
                    SetSize(size - dt / ShrinkSeconds);
                    if (size <= 0f)
                    {
                        state = State.Small;
                        Rigidbody body = physGrabObject != null ? physGrabObject.rb : GetComponent<Rigidbody>();
                        contents.Loosen(body != null ? body.linearVelocity : Vector3.zero);   // loose from now on
                    }
                    break;

                case State.Growing:
                    SetSize(size + dt / GrowSeconds);
                    ShiftWhileGrowing();
                    if (size >= 1f)
                    {
                        Rigidbody rb = physGrabObject != null ? physGrabObject.rb : GetComponent<Rigidbody>();
                        contents.Release(rb != null ? rb.linearVelocity : Vector3.zero);
                        state = State.Big;
                        if (!exportedAfterRegrow) { exportedAfterRegrow = true; CartDebugExport.Export(transform, "cart_plus_after_regrow"); CartDebugExport.ExportColliders(transform, "cart_plus_colliders_after_regrow"); }
                    }
                    break;
            }
            TintButton();
            UpdateMeter();
        }

        /// <summary>The meter under the button shows how far the cart has shrunk: empty at full size, full when small.</summary>
        private void UpdateMeter()
        {
            if (meterFill == null) return;
            float fill = 1f - size;
            float w = CartPlusBuilder.MeterWidth;
            Vector3 scale = meterFill.localScale;
            scale.x = Mathf.Max(w * fill, 0.0001f);
            meterFill.localScale = scale;
            Vector3 pos = meterFill.localPosition;
            pos.x = -w * 0.5f + w * fill * 0.5f;   // grows from the viewer's left
            meterFill.localPosition = pos;
            if (meterMaterial != null) meterMaterial.color = MeterColor;
        }

        /// <summary>Host: moves the cart along with its growth by the part of <see cref="growShift"/> due so far.</summary>
        private void ShiftWhileGrowing()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer() || growShift == Vector3.zero) return;
            float w = Mathf.SmoothStep(0f, 1f, size);
            float step = w - shiftApplied;
            if (step <= 0f) return;
            shiftApplied = w;
            Vector3 move = transform.TransformVector(growShift * step);
            Rigidbody rb = physGrabObject != null ? physGrabObject.rb : GetComponent<Rigidbody>();
            transform.position += move;
            if (rb != null) rb.position = transform.position;
            if (w >= 1f) growShift = Vector3.zero;
        }

        private void SetSize(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, size)) return;
            size = value;
            CartStretch stretch = CartPlusBuilder.Stretch;
            if (stretch != null) stretch.Apply(transform, CartPlusBuilder.FullDelta * Mathf.SmoothStep(0f, 1f, size), ownMeshes);
        }

        /// <summary>
        /// The button's knobs (the arrow lights and the shrink icon) change like the C.A.R.T. handle's do when it's grabbed
        /// (PhysGrabCart.StateSwitchRPC): their glow colour (_EmissionColor) and which half of their two-frame texture
        /// shows (mainTextureOffset x 0 or 0.5). Not in use: red, idle frame. Held (shrinking or small): green, pressed
        /// frame (the arrows pushed in).
        /// </summary>
        private void TintButton()
        {
            int visual = state == State.Shrinking || state == State.Small ? 1 : 0;
            if (visual == shownVisual) return;
            shownVisual = visual;
            Color glow = visual == 1 ? Color.green : Color.red;
            Vector2 offset = new Vector2(visual == 1 ? 0.5f : 0f, 0f);
            foreach (Material m in stripMaterials)
                if (m != null && m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", glow);
            foreach (Material m in buttonMaterials)
            {
                if (m == null) continue;
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", glow);
                if (m.name.StartsWith(CartPlusBuilder.IconMaterialName))
                {
                    // The icon's frames are fitted to the knob (CartPlusBuilder.SetShrinkIcon); the arrows keep the game's layout.
                    m.mainTextureOffset = CartPlusBuilder.IconBaseOffset + offset;
                }
                else m.mainTextureOffset = offset;
            }
        }
    }
}
