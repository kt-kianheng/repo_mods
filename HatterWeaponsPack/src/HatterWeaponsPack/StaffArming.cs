using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>A marker on the floor showing where a staff's charged spell will land (the holder's screen only).</summary>
    internal abstract class FloorMarker : MonoBehaviour
    {
        /// <param name="yaw">The holder's facing (degrees round the vertical), for shapes that turn with them.</param>
        internal abstract void Show(Vector3 point, Vector3 normal, float yaw);

        internal void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        protected void Appear()
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        protected LineRenderer Line(string name, int points, float width, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var lr = obj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = points;
            lr.widthMultiplier = width;
            lr.alignment = LineAlignment.TransformZ;    // lies flat on the floor
            if (FireballFx.Glow != null) lr.sharedMaterial = FireballFx.Glow;
            lr.startColor = color; lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }
    }

    /// <summary>
    /// The staves' charged spell, decided on the holder's game: pointing the staff up at the sky (past LookUpAngle)
    /// charges it, and keeping it there for the charge time readies it (lowering it sooner cancels the charge). Once
    /// it's ready, a new look up puts it away again. While it's ready and the holder isn't looking up, the floor under
    /// the crosshair (a surface facing up, not a player or enemy, within range) is the target, shown by a marker on the
    /// holder's screen. With walls allowed (the Ember Staff), a wall (a surface facing sideways) can be the target too.
    /// The holder's game reports [ready, target valid, x, y, z, yaw, charge 0..1, surface normal xyz] to everyone; the
    /// host casts from that.
    /// </summary>
    internal class StaffArming
    {
        private const float ReportMaxAge = 0.6f;
        internal const float ReportInterval = 0.1f;

        private readonly System.Func<FloorMarker> makeMarker;
        private readonly bool allowWalls;
        private FloorMarker marker;
        private bool armed, lookingUp, valid, charging;
        private float chargeStart, chargeSeconds = 1f;

        /// <summary>True on the frame the holder starts charging (for the incantation).</summary>
        internal bool ChargeStarted { get; private set; }
        private Vector3 point, normal = Vector3.up;
        private float yaw;
        private float[] reported;
        private float reportedAt = -10f;
        private float nextReport;

        /// <param name="makeMarker">Makes the floor marker; null for a spell that isn't aimed at the floor.</param>
        /// <param name="allowWalls">Walls can be the target as well as the floor.</param>
        internal StaffArming(System.Func<FloorMarker> makeMarker, bool allowWalls = false) { this.makeMarker = makeMarker; this.allowWalls = allowWalls; }

        /// <summary>The target surface's normal from a reported state (straight up for older or floor-only reports).</summary>
        internal static Vector3 NormalOf(float[] s) =>
            s != null && s.Length >= 10 && new Vector3(s[7], s[8], s[9]).sqrMagnitude > 0.25f ? new Vector3(s[7], s[8], s[9]).normalized : Vector3.up;

        private float LocalProgress => armed ? 1f : charging ? Mathf.Clamp01((Time.time - chargeStart) / chargeSeconds) : 0f;

        internal float[] State() => new[] { armed ? 1f : 0f, valid ? 1f : 0f, point.x, point.y, point.z, yaw, LocalProgress, normal.x, normal.y, normal.z };

        /// <summary>How far charged (0..1; 1 once ready), as this game sees it.</summary>
        internal float Progress(bool heldByLocal, bool hasHolder)
        {
            if (heldByLocal) return LocalProgress;
            if (!hasHolder || !ReportFresh) return 0f;
            return reported[0] > 0.5f ? 1f : reported.Length >= 7 ? Mathf.Clamp01(reported[6]) : 0f;
        }

        private bool ReportFresh => reported != null && reported.Length >= 6 && Time.time - reportedAt <= ReportMaxAge;

        /// <summary>Ready, as this game sees it: its own state for the holder, the latest report for everyone else.</summary>
        internal bool Armed(bool heldByLocal, bool hasHolder) => heldByLocal ? armed : hasHolder && ReportFresh && reported[0] > 0.5f;

        /// <summary>Host: the holder's state (its own if the host holds it, else a recent report), or null.</summary>
        internal float[] HostState(bool heldByLocal) => heldByLocal ? State() : ReportFresh ? reported : null;

        internal void Receive(float[] state)
        {
            if (state == null || state.Length < 6) return;
            reported = state;
            reportedAt = Time.time;
        }

        internal void Disarm()
        {
            armed = false;
            charging = false;
            valid = false;
            if (marker != null) marker.Hide();
        }

        /// <summary>Every frame on every game. Returns true when the holder's game should send its state now.</summary>
        internal bool Update(bool heldByLocal, float pitch, Transform view, PhysGrabObject self, PlayerAvatar holder,
                             float lookUpAngle, float range, float chargeSeconds)
        {
            ChargeStarted = false;
            this.chargeSeconds = Mathf.Max(0.05f, chargeSeconds);
            if (!heldByLocal)
            {
                armed = false; lookingUp = false; valid = false; charging = false;
                if (marker != null) marker.Hide();
                return false;
            }

            bool up = pitch <= -lookUpAngle;
            if (armed)
            {
                if (up && !lookingUp) armed = false;         // a new look up puts it away
            }
            else if (up)
            {
                if (!charging && !lookingUp) { charging = true; chargeStart = Time.time; ChargeStarted = true; }
                if (charging && Time.time - chargeStart >= this.chargeSeconds) { armed = true; charging = false; }
            }
            else charging = false;                           // lowered too soon: the charge is lost
            lookingUp = up;

            // no marker: the spell needs no floor (the Lightning Staff's orb flies at the crosshair)
            valid = makeMarker == null ? armed && !up : armed && !up && FindFloor(view, self, holder, range, allowWalls, out point, out normal);
            if (view != null)
            {
                Vector3 f = view.forward;
                if (new Vector2(f.x, f.z).sqrMagnitude > 1e-4f) yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }
            if (valid && makeMarker != null)
            {
                if (marker == null) marker = makeMarker();
                marker.Show(point, normal, yaw);
            }
            else if (marker != null) marker.Hide();

            if (!SemiFunc.IsMultiplayer() || Time.time < nextReport) return false;
            nextReport = Time.time + ReportInterval;
            return true;
        }

        private static bool FindFloor(Transform view, PhysGrabObject self, PlayerAvatar holder, float range, bool walls, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = Vector3.up;
            if (view == null) return false;
            float best = range;
            RaycastHit nearest = default;
            bool any = false;
            foreach (RaycastHit h in Physics.RaycastAll(view.position, view.forward, range, SemiFunc.LayerMaskGetVisionObstruct().value, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == null || h.distance >= best) continue;
                if (self != null && h.collider.GetComponentInParent<PhysGrabObject>() == self) continue;
                if (holder != null && h.collider.GetComponentInParent<PlayerAvatar>() == holder) continue;
                best = h.distance; nearest = h; any = true;
            }
            if (!any) return false;
            bool floor = nearest.normal.y >= 0.7f;
            bool wall = walls && Mathf.Abs(nearest.normal.y) <= 0.35f;   // within about 20 degrees of upright
            if (!floor && !wall) return false;
            if (nearest.collider.GetComponentInParent<PlayerAvatar>() != null || LightningTargets.Of(nearest.collider) != null) return false;
            point = nearest.point;
            normal = nearest.normal;
            return true;
        }

        internal void Destroy()
        {
            if (marker != null) Object.Destroy(marker.gameObject);
            marker = null;
        }
    }
}
