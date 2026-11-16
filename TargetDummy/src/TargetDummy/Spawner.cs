using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TargetDummy
{
    /// <summary>When the cosmetics gacha machine wakes up, a spawner rides along on it and places the dummies beside it.</summary>
    [HarmonyPatch(typeof(CosmeticShopMachine), "Awake")]
    internal static class MachineAwakePatch
    {
        private static void Postfix(CosmeticShopMachine __instance)
        {
            if ((!Settings.Enabled.Value && !Settings.AllyEnabled.Value) || __instance.GetComponent<Spawner>() != null) return;
            __instance.gameObject.AddComponent<Spawner>();
        }
    }

    /// <summary>
    /// Waits for the level to finish generating, then finds a clear spot on each side of the gacha machine and builds
    /// the dummies (Target on one side, Ally on the other).
    ///
    /// The machine's own transform can't be trusted as its position (it may sit at its room's origin), so the search
    /// is anchored on what's actually there: the machine's visible body and its "press to use" spot, where players
    /// stand. Around an ideal point beside the machine, a grid of spots is tested for floor at the players' level,
    /// room for the stand's base and body, headroom, and a clear line to where players stand; the passing spot nearest
    /// the ideal one wins. Every player runs this on their own and lands on the same spot, since it only looks at
    /// fixed level geometry.
    /// </summary>
    internal class Spawner : MonoBehaviour
    {
        private static readonly System.Reflection.FieldInfo InfoTriggerField = AccessTools.Field(typeof(CosmeticShopMachine), "infoTextTrigger");
        private const int LevelMask = 1;   // Default layer: walls, floors and props that never move
        private const float MinHeadroom = 2.0f;

        private Bounds body;               // the machine's visible body
        private bool bodyKnown;
        private Vector3 anchor;            // on the floor, where players stand to use the machine
        private Vector3 front, right;      // the machine's facing (towards the players) and its right
        private float floorY;

        private IEnumerator Start()
        {
            float waited = 0f;
            while (LevelGenerator.Instance != null && !LevelGenerator.Instance.Generated && waited < 60f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);

            if (Settings.OnlyInShop.Value && !SemiFunc.RunIsShop()) yield break;
            try { Survey(); }
            catch (System.Exception e) { Plugin.Log.LogError($"Looking around the gacha machine failed: {e}"); yield break; }
            Material template = TemplateMaterial();
            // the Target Dummy goes first; its hitboxes then keep the Ally Dummy from taking the same spot
            if (Settings.Enabled.Value && Dummy.Target == null) Place(DummyKind.Target, template);
            if (Settings.AllyEnabled.Value && Dummy.Ally == null) Place(DummyKind.Ally, template);
        }

        private void Survey()
        {
            var trigger = InfoTriggerField?.GetValue(GetComponent<CosmeticShopMachine>()) as Component;
            Vector3 reference = trigger != null ? trigger.transform.position : transform.position;

            // the machine's body: its visible parts near the "use" spot, ignoring anything room-sized and pieces
            // parked out of sight (the machine keeps some hidden far below the map)
            bodyKnown = false;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer) continue;
                Bounds b = r.bounds;
                if (b.size.x > 5f || b.size.z > 5f || b.size.y > 5f || b.size.sqrMagnitude < 1e-4f) continue;
                if (Vector3.Distance(b.center, reference) > 4f) continue;
                if (!bodyKnown) { body = b; bodyKnown = true; } else body.Encapsulate(b);
            }
            if (bodyKnown && (body.size.x > 6f || body.size.z > 6f || body.size.y > 6f)) bodyKnown = false;   // not a machine
            Vector3 centre = bodyKnown ? body.center : transform.position;
            Vector3 use = trigger != null ? trigger.transform.position : centre;

            front = use - centre; front.y = 0f;
            if (front.sqrMagnitude < 0.04f) { front = transform.forward; front.y = 0f; }
            if (front.sqrMagnitude < 1e-4f) front = Vector3.forward;
            front.Normalize();
            right = Vector3.Cross(Vector3.up, front).normalized;

            // where players stand: just in front of the body (the trigger may sit inside it)
            Vector3 stand = centre + front * ((bodyKnown ? Extent(body, front) : 0.5f) + 0.7f);
            float guessY = bodyKnown ? body.min.y : Mathf.Min(reference.y, transform.position.y + 1f);
            floorY = FloorAt(stand, guessY + 1.4f, 3f, out float y) ? y : guessY;
            anchor = new Vector3(stand.x, floorY, stand.z);

            Debug($"Gacha machine: object at {transform.position}, body {(bodyKnown ? $"centre {body.center} size {body.size}" : "not found")}, " +
                  $"use spot {use}, players stand at {anchor}, facing {front}.");
        }

        /// <summary>How far the bounds reach from their centre along a horizontal direction.</summary>
        private static float Extent(Bounds b, Vector3 dir) => Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;

        private void Place(DummyKind kind, Material template)
        {
            try
            {
                float sign = (Settings.PreferRightSide.Value ? 1f : -1f) * (kind == DummyKind.Ally ? -1f : 1f);
                Vector3 centre = bodyKnown ? new Vector3(body.center.x, floorY, body.center.z) : anchor - front * 0.7f;
                float halfWidth = bodyKnown ? Extent(body, right) : 0.6f;
                Vector3 ideal = centre + right * sign * (halfWidth + Settings.SideDistance.Value) + front * Settings.ForwardDistance.Value;

                Vector3 chosen = ideal;
                float headroom = 4f;
                if (Settings.AutoPlace.Value)
                {
                    if (!Search(centre, ideal, sign, out chosen, out headroom))
                    {
                        // nothing clear beside the machine: stand in the open in front of it, where players are
                        Vector3 open = anchor + front * 1.6f + right * sign * 1.2f;
                        chosen = FloorAt(open, floorY + 1.4f, 3f, out float y) ? new Vector3(open.x, y, open.z) : new Vector3(open.x, floorY, open.z);
                        headroom = Headroom(chosen);
                        Plugin.Log.LogWarning($"No clear spot beside the gacha machine for the {kind} Dummy; it stands in front of the machine instead. " +
                                              "Adjust SideGap / ForwardOffset in TargetDummy.cfg if it's in the way.");
                    }
                }
                else
                {
                    if (FloorAt(ideal, floorY + 1.4f, 3f, out float y)) chosen.y = y;
                    headroom = Headroom(chosen);
                }

                Quaternion facing = Quaternion.LookRotation(front, Vector3.up) * Quaternion.Euler(0f, Settings.ExtraYaw.Value, 0f);
                Dummy.Create(kind, chosen, facing, template, transform, headroom - 0.15f);
                Plugin.Log.LogInfo($"{kind} Dummy placed at {chosen} ({Vector3.Distance(chosen, ideal):0.0} m from the ideal spot, headroom {headroom:0.0} m).");
            }
            catch (System.Exception e) { Plugin.Log.LogError($"Placing the {kind} Dummy failed: {e}"); }
        }

        /// <summary>Tests a grid of spots around the ideal one; the passing spot nearest it wins (its own side preferred).</summary>
        private bool Search(Vector3 centre, Vector3 ideal, float sign, out Vector3 best, out float bestHeadroom)
        {
            best = ideal; bestHeadroom = 4f;
            float bestScore = float.MaxValue;
            var fails = new Dictionary<string, int>();
            const float step = 0.35f;
            for (int i = -10; i <= 10; i++)
            {
                for (int j = -8; j <= 10; j++)
                {
                    Vector3 p = ideal + right * (i * step) + front * (j * step);
                    string why = Check(p, centre, out Vector3 floor, out float headroom);
                    if (why != null) { fails[why] = fails.TryGetValue(why, out int n) ? n + 1 : 1; continue; }
                    float score = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(ideal.x, 0f, ideal.z));
                    if (Vector3.Dot(p - centre, right) * sign < 0.3f) score += 3f;   // the other side of the machine
                    if (score < bestScore) { bestScore = score; best = floor; bestHeadroom = headroom; }
                }
            }
            var parts = new List<string>();
            foreach (var kv in fails) parts.Add($"{kv.Key} {kv.Value}");
            Debug($"Spot search: {(bestScore < float.MaxValue ? $"best {best}, score {bestScore:0.00}" : "nothing clear")}; rejected: {string.Join(", ", parts)}.");
            return bestScore < float.MaxValue;
        }

        /// <summary>Why a spot won't do, or null if it's fine.</summary>
        private string Check(Vector3 p, Vector3 centre, out Vector3 floor, out float headroom)
        {
            headroom = 0f;
            floor = p;
            if (!FloorAt(p, floorY + 1.4f, 3f, out float y)) return "no floor";
            if (Mathf.Abs(y - floorY) > 0.3f) return "different level";
            floor = new Vector3(p.x, y, p.z);

            if (bodyKnown)
            {
                Bounds keepOut = body;
                keepOut.Expand(new Vector3(1.1f, 0f, 1.1f));   // the stand's base is about 1 m across
                if (keepOut.Contains(new Vector3(p.x, body.center.y, p.z))) return "on the machine";
            }
            foreach (Dummy d in Dummy.All)
                if (Vector3.Distance(new Vector3(d.transform.position.x, y, d.transform.position.z), floor) < 1.7f) return "next to the other dummy";

            if (Physics.CheckBox(floor + Vector3.up * 0.33f, new Vector3(0.5f, 0.25f, 0.5f), Quaternion.LookRotation(front), LevelMask, QueryTriggerInteraction.Ignore))
                return "base blocked";
            if (Physics.CheckCapsule(floor + Vector3.up * 0.95f, floor + Vector3.up * 1.6f, 0.45f, LevelMask, QueryTriggerInteraction.Ignore))
                return "body blocked";
            headroom = Headroom(floor);
            if (headroom < MinHeadroom) return "low ceiling";

            // a clear line to where players stand (so it isn't behind a wall or in another room)
            Vector3 from = floor + Vector3.up * 1.1f, to = anchor + Vector3.up * 1.1f;
            if (Physics.Linecast(from, to, out RaycastHit hit, LevelMask, QueryTriggerInteraction.Ignore)
                && hit.distance < Vector3.Distance(from, to) - 0.5f) return "behind a wall";
            if (Physics.Linecast(to, from, out hit, LevelMask, QueryTriggerInteraction.Ignore)
                && hit.distance < Vector3.Distance(from, to) - 0.5f) return "behind a wall";
            return null;
        }

        private static bool FloorAt(Vector3 p, float fromY, float distance, out float y)
        {
            // the highest walkable surface below fromY (a table top here is then rejected as "different level")
            y = 0f;
            float best = float.MinValue;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, fromY, p.z), Vector3.down, distance, LevelMask, QueryTriggerInteraction.Ignore))
                if (h.normal.y >= 0.7f && h.point.y > best) best = h.point.y;
            if (best == float.MinValue) return false;
            y = best;
            return true;
        }

        private static float Headroom(Vector3 floor)
        {
            return Physics.Raycast(floor + Vector3.up * 0.3f, Vector3.up, out RaycastHit hit, 5f, LevelMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y - floor.y : 5f;
        }

        private static void Debug(string message)
        {
            if (Settings.DebugLog.Value) Plugin.Log.LogInfo(message);
        }

        /// <summary>A material from the machine (the level's own shader) to tint for the wooden stand.</summary>
        private Material TemplateMaterial()
        {
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
                if (r.sharedMaterial != null && r.sharedMaterial.shader != null && !r.sharedMaterial.shader.name.Contains("TextMeshPro")) return r.sharedMaterial;
            return null;
        }
    }
}
