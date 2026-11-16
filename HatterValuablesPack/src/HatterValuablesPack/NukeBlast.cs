using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HatterValuablesPack
{
    /// <summary>
    /// The room map and what the blast reaches.
    /// Rooms: the game splits a level into modules (tiles), each with one or more RoomVolumes; a "room" here is a module
    /// (a stand-alone RoomVolume, e.g. the truck's, counts as its own room). Two rooms are next to each other when the
    /// level's navigation points (LevelPoint.ConnectedPoints, the paths enemies walk) cross from one to the other. Rooms
    /// no path crosses into are joined to rooms whose volumes touch theirs.
    /// The map is built from the level itself, so every player's game works out the same distances.
    /// </summary>
    internal static class NukeBlast
    {
        private static readonly FieldInfo PlayerDisabled = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo PlayerDead = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static readonly FieldInfo ValuableRooms = AccessTools.Field(typeof(ValuableObject), "roomVolumeCheck");
        private static readonly FieldInfo GrabDetector = AccessTools.Field(typeof(PhysGrabObject), "impactDetector");
        private static readonly FieldInfo ValuableGrab = AccessTools.Field(typeof(ValuableObject), "physGrabObject");
        private static readonly FieldInfo SpawnedField = AccessTools.Field(typeof(EnemyParent), "Spawned");
        private static readonly FieldInfo ParentEnemyField = AccessTools.Field(typeof(EnemyParent), "Enemy");
        private static readonly FieldInfo HealthField = AccessTools.Field(typeof(Enemy), "Health");
        private static readonly FieldInfo DeadField = AccessTools.Field(typeof(EnemyHealth), "dead");

        private static bool Debug => Settings.NukeDebugLog.Value;
        private static int roomMask = -1;
        private static int RoomMask => roomMask >= 0 ? roomMask : (roomMask = LayerMask.GetMask("RoomVolume"));

        // ---------------------------------------------------------------- rooms

        /// <summary>A room's key: its module, or the RoomVolume itself if it has none.</summary>
        internal static Object Key(RoomVolume r) => r == null ? null : (r.Module != null ? (Object)r.Module : r);

        /// <summary>The rooms (keys) at a world point.</summary>
        internal static HashSet<Object> RoomsAt(Vector3 p, float radius = 0.3f)
        {
            var set = new HashSet<Object>();
            foreach (Collider c in Physics.OverlapSphere(p, radius, RoomMask, QueryTriggerInteraction.Collide))
            {
                RoomVolume r = c.GetComponentInParent<RoomVolume>();
                if (r != null) set.Add(Key(r));
            }
            return set;
        }

        internal static HashSet<Object> RoomsOf(RoomVolumeCheck check, Vector3 fallback)
        {
            var set = new HashSet<Object>();
            if (check != null && check.CurrentRooms != null)
                foreach (RoomVolume r in check.CurrentRooms) if (r != null) set.Add(Key(r));
            if (set.Count == 0) set = RoomsAt(fallback);
            if (set.Count == 0) set = RoomsAt(fallback, 2f);
            return set;
        }

        /// <summary>Which rooms are next to which.</summary>
        internal static Dictionary<Object, HashSet<Object>> BuildMap()
        {
            var map = new Dictionary<Object, HashSet<Object>>();
            void Node(Object a) { if (a != null && !map.ContainsKey(a)) map[a] = new HashSet<Object>(); }
            void Link(Object a, Object b) { if (a == null || b == null || a == b) return; Node(a); Node(b); map[a].Add(b); map[b].Add(a); }

            var volumes = Object.FindObjectsByType<RoomVolume>(FindObjectsSortMode.None);
            foreach (RoomVolume r in volumes) Node(Key(r));

            IEnumerable<LevelPoint> points = LevelGenerator.Instance != null && LevelGenerator.Instance.LevelPathPoints != null
                                                 && LevelGenerator.Instance.LevelPathPoints.Count > 0
                ? (IEnumerable<LevelPoint>)LevelGenerator.Instance.LevelPathPoints
                : Object.FindObjectsByType<LevelPoint>(FindObjectsSortMode.None);
            int pathLinks = 0;
            foreach (LevelPoint p in points)
            {
                if (p == null || p.Room == null || p.ConnectedPoints == null) continue;
                foreach (LevelPoint q in p.ConnectedPoints)
                    if (q != null && q.Room != null && Key(q.Room) != Key(p.Room)) { Link(Key(p.Room), Key(q.Room)); pathLinks++; }
            }

            // rooms no path reaches: join them to rooms whose volumes touch
            var bounds = new Dictionary<Object, List<Bounds>>();
            foreach (RoomVolume r in volumes)
            {
                Object k = Key(r);
                if (!bounds.TryGetValue(k, out var list)) bounds[k] = list = new List<Bounds>();
                foreach (Collider c in r.GetComponentsInChildren<Collider>(true)) list.Add(c.bounds);
            }
            int touchLinks = 0;
            foreach (var kv in bounds)
            {
                if (map[kv.Key].Count > 0) continue;
                foreach (var other in bounds)
                {
                    if (other.Key == kv.Key) continue;
                    if (kv.Value.Any(a => other.Value.Any(b => { var e = a; e.Expand(0.6f); return e.Intersects(b); })))
                    { Link(kv.Key, other.Key); touchLinks++; }
                }
            }
            if (Debug) Plugin.Log.LogInfo($"A-Bomb: room map: {map.Count} rooms, {pathLinks} path crossings, {touchLinks} links by touching volumes.");
            return map;
        }

        /// <summary>Rooms (keys) and how many rooms away each is from the start rooms, up to maxSteps.</summary>
        internal static Dictionary<Object, int> Distances(Dictionary<Object, HashSet<Object>> map, HashSet<Object> start, int maxSteps)
        {
            var dist = new Dictionary<Object, int>();
            var queue = new Queue<Object>();
            foreach (Object s in start) if (s != null && !dist.ContainsKey(s)) { dist[s] = 0; queue.Enqueue(s); }
            while (queue.Count > 0)
            {
                Object a = queue.Dequeue();
                if (dist[a] >= maxSteps || !map.TryGetValue(a, out var next)) continue;
                foreach (Object b in next) if (!dist.ContainsKey(b)) { dist[b] = dist[a] + 1; queue.Enqueue(b); }
            }
            return dist;
        }

        /// <summary>Fewest rooms between these rooms and the blast (int.MaxValue when out of reach).</summary>
        internal static int StepsTo(Dictionary<Object, int> dist, HashSet<Object> rooms)
        {
            int best = int.MaxValue;
            foreach (Object r in rooms) if (r != null && dist.TryGetValue(r, out int d) && d < best) best = d;
            return best;
        }

        /// <summary>The bomb's rooms, falling back to the nearest navigation point's room if it isn't inside one.</summary>
        internal static HashSet<Object> BlastRooms(Vector3 centre)
        {
            HashSet<Object> rooms = RoomsAt(centre, 0.5f);
            if (rooms.Count > 0) return rooms;
            rooms = RoomsAt(centre, 3f);
            if (rooms.Count > 0) return rooms;
            LevelPoint nearest = null;
            float best = float.MaxValue;
            foreach (LevelPoint p in Object.FindObjectsByType<LevelPoint>(FindObjectsSortMode.None))
            {
                if (p == null || p.Room == null) continue;
                float d = (p.transform.position - centre).sqrMagnitude;
                if (d < best) { best = d; nearest = p; }
            }
            if (nearest != null) rooms.Add(Key(nearest.Room));
            return rooms;
        }

        // ---------------------------------------------------------------- the blast (host)

        internal static void HostApply(Nuke bomb, Vector3 centre)
        {
            int killRooms = Settings.NukeKillRooms.Value;
            float minRadius = Settings.NukeMinKillRadius.Value;
            var map = BuildMap();
            HashSet<Object> start = BlastRooms(centre);
            var dist = Distances(map, start, killRooms);
            if (Debug) Plugin.Log.LogInfo($"A-Bomb: in {start.Count} room(s) ({string.Join(", ", start.Select(Name))}); {dist.Count} room(s) within {killRooms}: {string.Join(", ", dist.Select(kv => Name(kv.Key) + "=" + kv.Value))}.");

            bool InRange(HashSet<Object> rooms, Vector3 at) =>
                StepsTo(dist, rooms) <= killRooms || (minRadius > 0f && (at - centre).sqrMagnitude <= minRadius * minRadius);

            // players (the truck is no shelter)
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                if (PlayerDisabled != null && (bool)PlayerDisabled.GetValue(p)) continue;
                if (PlayerDead != null && (bool)PlayerDead.GetValue(p)) continue;
                Vector3 at = p.PlayerVisionTarget != null && p.PlayerVisionTarget.VisionTransform != null ? p.PlayerVisionTarget.VisionTransform.position : p.transform.position;
                var rooms = RoomsOf(p.RoomVolumeCheck, at);
                bool hit = InRange(rooms, at);
                if (Debug) Plugin.Log.LogInfo($"A-Bomb: player '{SemiFunc.PlayerGetName(p)}' {Steps(dist, rooms)}, {(at - centre).magnitude:0.0} m: {(hit ? "killed" : "safe")}.");
                if (!hit) continue;
                try { p.PlayerDeath(-1); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"A-Bomb: killing a player failed: {e}"); }
            }

            // enemies
            EnemyDirector director = EnemyDirector.instance;
            if (director != null && director.enemiesSpawned != null)
            {
                foreach (EnemyParent parent in director.enemiesSpawned.ToList())
                {
                    if (parent == null) continue;
                    if (SpawnedField != null && !(bool)SpawnedField.GetValue(parent)) continue;
                    Enemy e = (ParentEnemyField != null ? ParentEnemyField.GetValue(parent) as Enemy : null) ?? parent.GetComponentInChildren<Enemy>();
                    if (e == null || !e.gameObject.activeInHierarchy) continue;
                    EnemyHealth health = (HealthField != null ? HealthField.GetValue(e) as EnemyHealth : null) ?? e.GetComponentInChildren<EnemyHealth>();
                    if (health == null || (DeadField != null && (bool)DeadField.GetValue(health))) continue;
                    Vector3 at = e.CenterTransform != null ? e.CenterTransform.position : e.transform.position;
                    var rooms = RoomsAt(at);
                    if (rooms.Count == 0) rooms = RoomsAt(at, 2f);
                    bool hit = InRange(rooms, at);
                    if (Debug) Plugin.Log.LogInfo($"A-Bomb: enemy '{parent.name}' {Steps(dist, rooms)}: {(hit ? "killed" : "safe")}.");
                    if (!hit) continue;
                    try { health.Hurt(99999, (at - centre).normalized); }
                    catch (System.Exception ex) { Plugin.Log.LogWarning($"A-Bomb: killing an enemy failed: {ex}"); }
                }
            }

            // valuables (in the truck too)
            int destroyed = 0;
            foreach (ValuableObject v in Object.FindObjectsByType<ValuableObject>(FindObjectsSortMode.None))
            {
                if (v == null || v.gameObject == bomb.gameObject || !v.gameObject.activeInHierarchy) continue;
                Vector3 at = v.transform.position;
                var grab = ValuableGrab != null ? ValuableGrab.GetValue(v) as PhysGrabObject : null;
                if (grab != null) at = grab.centerPoint;
                var rooms = RoomsOf(ValuableRooms != null ? ValuableRooms.GetValue(v) as RoomVolumeCheck : null, at);
                if (!InRange(rooms, at)) continue;
                var detector = (grab != null && GrabDetector != null ? GrabDetector.GetValue(grab) as PhysGrabObjectImpactDetector : null)
                               ?? v.GetComponent<PhysGrabObjectImpactDetector>();
                if (detector == null) continue;
                try { detector.DestroyObject(true, true); destroyed++; }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"A-Bomb: destroying '{v.name}' failed: {ex}"); }
            }
            if (Debug) Plugin.Log.LogInfo($"A-Bomb: {destroyed} valuable(s) destroyed.");
        }

        private static string Steps(Dictionary<Object, int> dist, HashSet<Object> rooms)
        {
            int s = StepsTo(dist, rooms);
            return rooms.Count == 0 ? "in no room" : s == int.MaxValue ? $"out of reach ({string.Join(", ", rooms.Select(Name))})" : $"{s} room(s) away";
        }

        internal static string Name(Object o) => o == null ? "?" : o.name;
    }
}
