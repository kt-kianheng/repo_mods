using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HatterCartsPack
{
    /// <summary>
    /// What a C.A.R.T. Plus shrinks along with itself: the valuables, items, death heads and players in its basket when
    /// the button is first held (user, 18 Nov: "shrink everything, including players"; held items are skipped).
    /// The host picks them (<see cref="Gather"/>) and every game tracks the same ones (<see cref="Begin"/>).
    ///
    /// Objects: while the cart shrinks or grows they ride with it at a fixed spot (kinematic, ungrabbable, unbreakable,
    /// not touching the cart's colliders; <see cref="Hold"/>), so the walls can't crush them. Once the cart is small they
    /// are let loose at the small size (<see cref="Loosen"/>): they can roll, fall or be taken out. Before growing back,
    /// the ones still inside ride again (<see cref="Tighten"/>), and at full size they get their size back (<see cref="Release"/>).
    ///
    /// Players are never held: only their model shrinks (the PlayerAvatarVisuals transform), and the local player's
    /// camera drops by as much (<see cref="LocalCameraDrop"/>, applied by a patch on CameraCrouchPosition).
    ///
    /// Anything that leaves the basket while the cart is small (host: <see cref="FindLeavers"/>) gets its size back
    /// at once on every game (<see cref="Drop"/>).
    /// </summary>
    internal class CartPlusContents
    {
        private const float LockRefresh = 0.25f;   // seconds; the host keeps renewing the game's own lock-out timers
        private const float SettleSeconds = 1f;    // still unbreakable for this long after being let go
        private const float GatherMargin = 0.1f;   // metres around the basket that still count as in it, when picking
        private const float LeaveMargin = 0.3f;    // metres outside the basket before something counts as having left
        private const float PlayerUp = 1.2f;       // metres above the basket a standing player's feet may be checked from

        /// <summary>The local player's camera drop (metres) while their model is shrunk; read by the camera patch.</summary>
        internal static float LocalCameraDrop;

        // Game fields that are internal (no publicized assembly here).
        private static readonly AccessTools.FieldRef<PlayerAvatar, PlayerTumble> TumbleRef = AccessTools.FieldRefAccess<PlayerAvatar, PlayerTumble>("tumble");
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> IsLocalRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("isLocal");
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> DeadRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("deadSet");
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> DisabledRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("isDisabled");
        private static readonly AccessTools.FieldRef<PlayerTumble, PhysGrabObject> TumbleObjectRef = AccessTools.FieldRefAccess<PlayerTumble, PhysGrabObject>("physGrabObject");

        private static bool IsLocal(PlayerAvatar p) => IsLocalRef(p);
        private static bool Gone(PlayerAvatar p) => DeadRef(p) || DisabledRef(p);

        private class Item
        {
            internal PhysGrabObject obj;
            internal Transform t;
            internal Rigidbody rb;
            internal PhysGrabObjectImpactDetector impact;
            internal Vector3 full, small;           // cart-space positions at full and at C.A.R.T. size
            internal Quaternion rotation;           // relative to the cart
            internal Vector3 scale;                 // original local scale
            internal Collider[] colliders;
            internal bool riding;                   // held at its spot (while the cart resizes) or loose
        }

        private class Rider
        {
            internal PlayerAvatar player;
            internal Transform visuals;
            internal Vector3 scale;                 // the model's original local scale
            internal float eyeHeight;               // local player only: camera height above their feet
        }

        private readonly Transform cart;
        private readonly PhysGrabObject self;
        private readonly List<Collider> cartColliders = new List<Collider>();
        private readonly List<Item> items = new List<Item>();
        private readonly List<Rider> riders = new List<Rider>();
        private float itemScale = 1f;
        private Vector3 anchorFull, anchorSmall;

        internal bool Active => items.Count > 0 || riders.Count > 0;
        internal int Count => items.Count + riders.Count;
        internal float ItemScale => itemScale;

        internal CartPlusContents(Transform cart, PhysGrabObject self)
        {
            this.cart = cart;
            this.self = self;
            foreach (Collider c in cart.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger) cartColliders.Add(c);
        }

        // ---- Picking (host) ---------------------------------------------------------------------------

        /// <summary>
        /// Host: the valuables, items and death heads in the basket now (the same box the game uses for the haul), and the
        /// players standing or tumbled in it. Not: anything a player is holding, doors and other parts of the level
        /// (user, 18 Nov: a door above the cart shrank), enemies. Every candidate and why it was skipped goes to the log.
        /// </summary>
        internal List<Component> Gather()
        {
            var found = new List<Component>();
            var seen = new HashSet<PhysGrabObject>();
            if (!TryBasket(out Transform inCart, out BoxCollider box)) return found;

            Vector3 center = inCart.TransformPoint(box.center);
            Vector3 half = Vector3.Scale(box.size, inCart.lossyScale) * 0.5f;
            half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
            var log = new System.Text.StringBuilder();
            foreach (Collider c in Physics.OverlapBox(center, half, inCart.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                PhysGrabObject obj = c.GetComponentInParent<PhysGrabObject>();
                if (obj == null || obj == self || !seen.Add(obj)) continue;
                if (obj.transform.IsChildOf(cart)) continue;
                if (obj.GetComponentInParent<PlayerTumble>() != null) continue;   // players are picked below
                string why = SkipReason(obj, inCart, box);
                log.Append(why == null ? $" +{obj.name}" : $" -{obj.name} ({why})");
                if (why == null) found.Add(obj);
            }
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (p == null || p.playerAvatarVisuals == null || p.photonView == null || p.photonView.ViewID >= 16000000) continue;   // TargetDummy's Ally Dummy
                if (Gone(p)) continue;
                if (!PlayerInBasket(p, inCart, box, GatherMargin)) continue;
                log.Append($" +player {p.name}");
                found.Add(p);
            }
            Plugin.Log.LogInfo($"{CartPlusBuilder.DisplayName}: basket check:{(log.Length > 0 ? log.ToString() : " empty")}.");
            return found;
        }

        private static string SkipReason(PhysGrabObject obj, Transform inCart, BoxCollider box)
        {
            if (obj.GetComponentInParent<PhysGrabHinge>() != null) return "door/hinge";
            if (obj.GetComponentInParent<EnemyRigidbody>() != null) return "enemy";
            bool kind = obj.GetComponentInParent<ValuableObject>() != null || obj.GetComponentInParent<ItemAttributes>() != null
                     || obj.GetComponentInParent<PlayerDeathHead>() != null;
            if (!kind) return "not a valuable or item";
            if (obj.playerGrabbing != null && obj.playerGrabbing.Count > 0) return "held";
            if (!Inside(Middle(obj), inCart, box, GatherMargin)) return "middle outside the basket";
            return null;
        }

        /// <summary>
        /// Host, while the cart is small: what has left the basket and should get its size back. Objects only count once
        /// they're loose (not while riding), and something a player is carrying counts by where it is.
        /// </summary>
        internal List<Component> FindLeavers()
        {
            var gone = new List<Component>();
            if (!TryBasket(out Transform inCart, out BoxCollider box)) return gone;
            foreach (Item it in items)
            {
                if (it.obj == null) continue;
                if (!it.riding && !Inside(Middle(it.obj), inCart, box, LeaveMargin)) gone.Add(it.obj);
            }
            foreach (Rider r in riders)
            {
                if (r.player == null) continue;
                if (Gone(r.player) || !PlayerInBasket(r.player, inCart, box, LeaveMargin)) gone.Add(r.player);
            }
            return gone;
        }

        private bool TryBasket(out Transform inCart, out BoxCollider box)
        {
            inCart = cart.Find("In Cart");
            box = inCart != null ? inCart.GetComponent<BoxCollider>() : null;
            return box != null;
        }

        private static Vector3 Middle(PhysGrabObject obj)
        {
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            return rb != null ? rb.worldCenterOfMass : obj.transform.position;
        }

        /// <summary>Whether a world point is in the basket box, give or take <paramref name="margin"/> metres (and up to
        /// <paramref name="extraUp"/> metres above it).</summary>
        private static bool Inside(Vector3 world, Transform inCart, BoxCollider box, float margin, float extraUp = 0f)
        {
            // Work in the cart's space (y up), where the basket is an upright box.
            Transform cartT = inCart.parent != null ? inCart.parent : inCart;
            Bounds b = CartStretch.CartBounds(cartT, box);
            Vector3 p = cartT.InverseTransformPoint(world);
            Vector3 lo = b.min - Vector3.one * margin, hi = b.max + Vector3.one * margin;
            hi.y += extraUp;
            return p.x >= lo.x && p.x <= hi.x && p.y >= lo.y && p.y <= hi.y && p.z >= lo.z && p.z <= hi.z;
        }

        /// <summary>A standing player's feet, or a tumbled player's ball, in the basket.</summary>
        private static bool PlayerInBasket(PlayerAvatar p, Transform inCart, BoxCollider box, float margin)
        {
            PlayerTumble tumble = TumbleRef(p);
            PhysGrabObject ball = tumble != null && tumble.isActiveAndEnabled ? TumbleObjectRef(tumble) : null;
            if (ball != null && Inside(Middle(ball), inCart, box, margin)) return true;
            // Feet: the avatar's position is at their feet; only the bottom part of the box needs to hold them.
            return Inside(p.transform.position, inCart, box, margin, 0f) || Inside(p.transform.position + Vector3.up * 0.3f, inCart, box, margin, 0f);
        }

        // ---- Every game -----------------------------------------------------------------------------------

        /// <summary>Every game, when the cart starts shrinking from full size: starts tracking these, at full size.</summary>
        internal void Begin(List<Component> things)
        {
            Bounds full = CartPlusBuilder.BasketFull, small = CartPlusBuilder.BasketSmall;
            // One size for everything, so it all keeps its layout: the basket shrinks this much across and along.
            itemScale = Mathf.Min(small.size.x / Mathf.Max(full.size.x, 1e-3f), small.size.z / Mathf.Max(full.size.z, 1e-3f));
            // Heights are kept relative to the floor items stand on (the "In Cart" box reaches ~11 cm below it, and
            // scaling from there made flat items sink: user, 18 Nov). The floor moves up as much as the box bottom does.
            float floorFull = FloorTop(full.min.y);
            float floorSmall = floorFull + (small.min.y - full.min.y);
            anchorFull = new Vector3(full.center.x, floorFull, full.center.z);
            anchorSmall = new Vector3(small.center.x, floorSmall, small.center.z);

            foreach (Component c in things)
            {
                if (c is PlayerAvatar p) AddRider(p);
                else if (c is PhysGrabObject obj) AddItem(obj);
            }
            Hold(1f);
            Plugin.Log.LogInfo($"{CartPlusBuilder.DisplayName}: shrinking {items.Count} object(s) and {riders.Count} player(s) to {itemScale:P0}.");
        }

        private void AddItem(PhysGrabObject obj)
        {
            if (obj == null || items.Exists(i => i.obj == obj)) return;
            Transform t = obj.transform;
            Vector3 local = cart.InverseTransformPoint(t.position);
            var item = new Item
            {
                obj = obj,
                t = t,
                rb = obj.GetComponent<Rigidbody>(),
                impact = obj.GetComponent<PhysGrabObjectImpactDetector>(),
                full = local,
                small = anchorSmall + (local - anchorFull) * itemScale,
                rotation = Quaternion.Inverse(cart.rotation) * t.rotation,
                scale = t.localScale,
                colliders = obj.GetComponentsInChildren<Collider>(true)
            };
            items.Add(item);
            Ride(item, true);
        }

        private void AddRider(PlayerAvatar p)
        {
            if (p == null || p.playerAvatarVisuals == null || riders.Exists(r => r.player == p)) return;
            var r = new Rider { player = p, visuals = p.playerAvatarVisuals.transform };
            r.scale = r.visuals.localScale;
            if (IsLocal(p))
            {
                Camera cam = Camera.main;
                r.eyeHeight = cam != null ? Mathf.Clamp(cam.transform.position.y - p.transform.position.y, 0.5f, 2f) : 1.4f;
            }
            riders.Add(r);
        }

        /// <summary>
        /// Cart space: the top of the basket's floor ("Inside" box, which items rest on), at the cart's current size
        /// (always full size here: things are only picked when the cart is big).
        /// </summary>
        private float FloorTop(float fallback)
        {
            Transform inside = cart.Find("Inside");
            BoxCollider floor = inside != null ? inside.GetComponentInChildren<BoxCollider>(true) : null;
            if (floor == null) return fallback;
            return CartStretch.CartBounds(cart, floor).max.y;
        }

        private void Ride(Item it, bool on)
        {
            it.riding = on;
            foreach (Collider a in it.colliders)
                foreach (Collider b in cartColliders)
                    if (a != null && b != null) Physics.IgnoreCollision(a, b, on);
        }

        /// <summary>
        /// Every game, every frame (<paramref name="w"/>: 1 = full size, 0 = C.A.R.T. size): riding objects are put at their
        /// spot for this size; tracked players' models are scaled to match.
        /// </summary>
        internal void Hold(float w)
        {
            bool host = SemiFunc.IsMasterClientOrSingleplayer();
            float s = Mathf.Lerp(itemScale, 1f, w);
            foreach (Item it in items)
            {
                if (it.obj == null || it.t == null || !it.riding) continue;
                Vector3 pos = cart.TransformPoint(Vector3.Lerp(it.small, it.full, w));
                Quaternion rot = cart.rotation * it.rotation;
                it.t.SetPositionAndRotation(pos, rot);
                it.t.localScale = it.scale * s;
                if (it.rb != null)
                {
                    it.rb.position = pos;
                    it.rb.rotation = rot;
                    if (!it.rb.isKinematic) { it.rb.linearVelocity = Vector3.zero; it.rb.angularVelocity = Vector3.zero; }
                }
                if (host)
                {
                    it.obj.OverrideKinematic(LockRefresh);
                    it.obj.OverrideGrabDisable(LockRefresh);
                    it.obj.OverrideIndestructible(LockRefresh);
                    if (it.impact != null) it.impact.ImpactDisable(LockRefresh);
                }
            }
            foreach (Rider r in riders)
            {
                if (r.player == null || r.visuals == null) continue;
                r.visuals.localScale = r.scale * s;
                if (IsLocal(r.player)) LocalCameraDrop = r.eyeHeight * (1f - s);
            }
        }

        /// <summary>Every game, once the cart is small: the riding objects are let loose, at the small size.</summary>
        internal void Loosen(Vector3 cartVelocity)
        {
            bool host = SemiFunc.IsMasterClientOrSingleplayer();
            foreach (Item it in items)
            {
                if (it.obj == null || !it.riding) continue;
                Ride(it, false);
                if (host)
                {
                    it.obj.OverrideIndestructible(SettleSeconds);
                    if (it.impact != null) it.impact.ImpactDisable(SettleSeconds * 0.5f);
                    if (it.rb != null) { it.rb.linearVelocity = cartVelocity; it.rb.angularVelocity = Vector3.zero; }
                }
            }
        }

        /// <summary>
        /// Every game, before the cart grows back: the loose objects still tracked ride again, from where they are now,
        /// mapped out to where they'll be at full size.
        /// </summary>
        internal void Tighten()
        {
            foreach (Item it in items)
            {
                if (it.obj == null || it.t == null || it.riding) continue;
                Vector3 local = cart.InverseTransformPoint(it.t.position);
                it.small = local;
                it.full = anchorFull + (local - anchorSmall) / Mathf.Max(itemScale, 1e-3f);
                it.rotation = Quaternion.Inverse(cart.rotation) * it.t.rotation;
                Ride(it, true);
            }
        }

        /// <summary>Every game: one tracked object or player left the basket; it gets its size back now.</summary>
        internal void Drop(Component c)
        {
            int i = items.FindIndex(x => x.obj == c);
            if (i >= 0) { RestoreItem(items[i], Vector3.zero, false); items.RemoveAt(i); Plugin.Log.LogInfo($"{CartPlusBuilder.DisplayName}: {c.name} left the basket, back to full size."); return; }
            int j = riders.FindIndex(x => x.player == c);
            if (j >= 0) { RestoreRider(riders[j]); riders.RemoveAt(j); Plugin.Log.LogInfo($"{CartPlusBuilder.DisplayName}: player {c.name} left the basket, back to full size."); }
        }

        /// <summary>Every game: everything gets its size back (the cart is full size again, or it's gone).</summary>
        internal void Release(Vector3 cartVelocity)
        {
            foreach (Item it in items) RestoreItem(it, cartVelocity, true);
            foreach (Rider r in riders) RestoreRider(r);
            if (Count > 0) Plugin.Log.LogInfo($"{CartPlusBuilder.DisplayName}: released {items.Count} object(s) and {riders.Count} player(s).");
            items.Clear();
            riders.Clear();
        }

        private void RestoreItem(Item it, Vector3 velocity, bool setVelocity)
        {
            if (it.obj == null || it.t == null) return;
            it.t.localScale = it.scale;
            if (it.riding) Ride(it, false);
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            it.obj.OverrideIndestructible(SettleSeconds);
            if (it.impact != null) it.impact.ImpactDisable(SettleSeconds * 0.5f);
            if (setVelocity && it.rb != null) { it.rb.linearVelocity = velocity; it.rb.angularVelocity = Vector3.zero; }
        }

        private void RestoreRider(Rider r)
        {
            if (r.visuals != null) r.visuals.localScale = r.scale;
            if (r.player != null && IsLocal(r.player)) LocalCameraDrop = 0f;
        }
    }
}
