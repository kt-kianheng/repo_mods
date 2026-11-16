using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace HatterEnemiesPack
{
    /// <summary>
    /// Step 1 of the new semibot monster: an animation test. With [Debug] SemibotAnimTest on, 3 s after a level loads
    /// a SemibotRig (a copy of a random living player's semibot, animated by the game's own player code) stands at
    /// the truck and loops through its animations, logging each:
    ///   Idle (4 s) → Panning camera (6 s) → Watching you (8 s; camera shifts when you're 30-45° off centre) → Walking (there and back) →
    ///   Sprinting (there and back) → Jump (3 jumps) → Tumble (curls up, launched, rolls, stands up) → Search (looks
    ///   around for up to 10 s; a valuable it sees, within 60° of where it looks, within 1 m and in clear sight, is
    ///   spotted) → Grabbing (the spotted valuable on the beam for 5 s; host only; skipped if nothing was spotted) → again.
    /// [Debug] SemibotAnimNextKey (F8) skips to the next one. Only on this game (no networking).
    /// </summary>
    internal class SemibotAnimTest : MonoBehaviour
    {
        private enum Phase { Idle, Pan, Pupils, Walk, Sprint, Jump, Tumble, Search, Grab }

        private const float SearchSeconds = 10f, SearchViewAngle = 60f;
        private PhysGrabObject spotted;   // what the search found (the grab takes it)
        private float nextLookCheck;

        private SemibotRig rig;
        private float generatedAt = -1f;
        private bool spawned;
        private Phase phase;
        private float phaseStart, next;
        private Vector3 lineA, lineB, goal;
        private float jumpV, groundY;
        private bool airborne, lifting;
        private float walkSpeed = 2.5f, sprintSpeed = 5f;
        // a look flick (minimum jerk), for panning
        private float watchThreshold, homeYaw;
        private float flickFromYaw, flickToYaw, flickFromPitch, flickToPitch, flickStart, flickDur;

        internal static void Create()
        {
            var go = new GameObject("HatterEnemiesPack.SemibotAnimTest");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<SemibotAnimTest>();
        }

        private void Update()
        {
            if (!Settings.DebugSemibotAnimTest.Value) { Clear(); return; }
            LevelGenerator lg = LevelGenerator.Instance;
            if (lg == null || !lg.Generated || !SemiFunc.RunIsLevel()) { Clear(); generatedAt = -1f; spawned = false; return; }
            if (generatedAt < 0f) generatedAt = Time.time;
            if (!spawned && Time.time - generatedAt > 3f) { spawned = true; Spawn(lg); }
            if (rig == null) return;
            if (Settings.DebugSemibotAnimNextKey.Value.IsDown()) NextPhase("ended (key)");
            Tick(Time.deltaTime);
        }

        private void Clear()
        {
            if (rig != null) Destroy(rig.gameObject);
            rig = null;
        }

        // ---------------- setting up ----------------

        private void Spawn(LevelGenerator lg)
        {
            var players = new List<PlayerAvatar>();
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList()) if (R.Alive(p) && p.playerAvatarVisuals != null) players.Add(p);
            if (players.Count == 0 || lg.LevelPathTruck == null) { Plugin.Log.LogWarning("Semibot test: no player or truck found."); return; }
            PlayerAvatar look = players[Random.Range(0, players.Count)];

            Vector3 truck = lg.LevelPathTruck.transform.position;
            if (!NavMesh.SamplePosition(truck, out NavMeshHit start, 3f, NavMesh.AllAreas)) { Plugin.Log.LogWarning("Semibot test: no floor at the truck."); return; }
            // the longest clear straight line on the floor (up to 8 m), among 16 directions
            Vector3 bestEnd = start.position; float best = 0f;
            for (int i = 0; i < 16; i++)
            {
                Vector3 end = start.position + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * 8f;
                if (NavMesh.Raycast(start.position, end, out NavMeshHit blocked, NavMesh.AllAreas)) end = blocked.position;
                float len = Vector3.Distance(start.position, end);
                if (len > best) { best = len; bestEnd = end; }
            }
            lineA = start.position;
            lineB = Vector3.MoveTowards(bestEnd, lineA, 0.5f);
            float yaw = Quaternion.LookRotation(Flat(lineB - lineA)).eulerAngles.y;

            // speeds: the game's own (PlayerController MoveSpeed; sprint = base + 1 m/s per speed upgrade of the copied player)
            PlayerController pc = PlayerController.instance;
            if (pc != null)
            {
                walkSpeed = pc.MoveSpeed;
                int ups = 0;
                string id = SemibotRig.F<string>(look, "steamID");
                if (!string.IsNullOrEmpty(id) && StatsManager.instance != null && StatsManager.instance.playerUpgradeSpeed != null) StatsManager.instance.playerUpgradeSpeed.TryGetValue(id, out ups);
                sprintSpeed = pc.SprintSpeed - pc.SprintSpeedUpgrades + Mathf.Max(0, ups);
            }

            rig = SemibotRig.Create(look, lineA, yaw);
            if (rig == null) return;
            Plugin.Log.LogInfo($"Semibot test: {R.Name(look)}'s semibot at the truck; line {best:0.#} m; walk {walkSpeed:0.##} m/s, sprint {sprintSpeed:0.##} m/s. {Settings.DebugSemibotAnimNextKey.Value} skips to the next animation.");
            SetPhase(Phase.Idle);
        }

        // ---------------- the loop ----------------

        private void NextPhase(string why)
        {
            if (rig != null && rig.Tumbling) rig.EndTumble();
            if (rig != null && rig.Held != null) rig.Grab(null, 0f);
            // F8 only (nothing moves on by itself): Search/Grab are one step, the next is Idle again
            Phase n = phase == Phase.Search || phase == Phase.Grab ? Phase.Idle : phase + 1;
            if (why != null) Plugin.Log.LogInfo($"Semibot test: {phase} {why}.");
            SetPhase(n);
        }

        private void SetPhase(Phase p)
        {
            phase = p;
            phaseStart = Time.time;
            homeYaw = rig.Yaw;   // looking around and glancing are around the way it faced when the step started
            next = 0f;
            rig.Moving = rig.Sprinting = false;
            rig.Velocity = Vector3.zero;
            rig.Grounded = true;
            airborne = false;
            string what = "";
            switch (p)
            {
                case Phase.Idle: what = "standing still"; rig.LookYaw = rig.Yaw; rig.LookPitch = 0f; break;
                case Phase.Pan: what = "looking around (camera flicks left/right/up/down)"; break;
                case Phase.Pupils:
                    watchThreshold = 0f;   // the first look goes straight to you
                    what = "watching you: its camera stays put until you're 30-45° off its centre, then shifts to you (its pupils are the game's own: they follow you if you talk within 6 m or land a jump within 5 m)";
                    break;
                case Phase.Walk: goal = Far(); what = $"walking back and forth ({walkSpeed:0.##} m/s)"; break;
                case Phase.Sprint: goal = Far(); what = $"sprinting back and forth ({sprintSpeed:0.##} m/s)"; break;
                case Phase.Jump: lifting = false; next = Time.time + 0.6f; what = "jumping over and over"; break;
                case Phase.Tumble:
                {
                    Vector3 fwd = Quaternion.Euler(0f, rig.Yaw, 0f) * Vector3.forward;
                    rig.StartTumble(fwd * 6f + Vector3.up * 2.5f, Quaternion.Euler(0f, rig.Yaw, 0f) * new Vector3(Random.Range(6f, 10f), Random.Range(-3f, 3f), Random.Range(-3f, 3f)));
                    what = "tumble form: curls up first (with the tumble sound), then is launched forward, rolls, stands up";
                    break;
                }
                case Phase.Search:
                    spotted = null;
                    nextLookCheck = 0f;
                    what = R.IsHost ? $"looking around for a valuable (within {SemibotRig.GrabReach} m, in sight) for up to {SearchSeconds:0} s" : "searching: host only (skipped)";
                    if (!R.IsHost) next = -1f;
                    break;
                case Phase.Grab:
                {
                    PhysGrabObject v = spotted;
                    spotted = null;
                    if (v == null) { what = "grabbing: nothing was spotted (no grab)"; next = -1f; break; }
                    Vector3 to = v.transform.position - rig.Feet;
                    rig.Yaw = rig.LookYaw = Quaternion.LookRotation(Flat(to)).eulerAngles.y;
                    homeYaw = rig.Yaw;
                    rig.LookPitch = Pitch(rig.Eye != null ? rig.Eye.position : rig.Feet + Vector3.up * 1.4f, v.transform.position);
                    if (!rig.Grab(v, 1.6f)) { what = $"grabbing: {v.name} refused"; next = -1f; break; }
                    what = $"grabbing {v.name} with the beam for 5 s, then looking again";
                    break;
                }
            }
            Plugin.Log.LogInfo($"Semibot test: {p}: {what}.");
        }

        private void Tick(float dt)
        {
            float age = Time.time - phaseStart;
            switch (phase)
            {
                case Phase.Idle:
                    break;

                case Phase.Pan:
                    if (Time.time >= next)
                    {
                        next = Time.time + Random.Range(0.8f, 1.6f);
                        StartFlick(homeYaw + Random.Range(-80f, 80f), Random.Range(-35f, 40f));   // up to 35° up, 40° down
                    }
                    Flick();
                    break;

                case Phase.Pupils:
                {
                    // the camera holds still until you're 30-45° (random each time) off its centre, then a quick
                    // shift (minimum-jerk flick) puts you back in the middle; the body turns with it (as a player's does)
                    PlayerAvatar me = PlayerAvatar.instance;
                    bool flicking = Time.time - flickStart < flickDur;
                    if (me != null && !flicking)
                    {
                        Vector3 to = R.Eyes(me) - rig.Eye.position;
                        float off = Vector3.Angle(Quaternion.Euler(rig.LookPitch, rig.LookYaw, 0f) * Vector3.forward, to);
                        if (off > watchThreshold)
                        {
                            StartFlick(Quaternion.LookRotation(Flat(to)).eulerAngles.y, Pitch(rig.Eye.position, R.Eyes(me)));
                            float upDown = Pitch(rig.Eye.position, R.Eyes(me)) - rig.LookPitch;
                            Plugin.Log.LogInfo($"Semibot test: you were {off:0}° off its centre ({Mathf.DeltaAngle(rig.LookYaw, Quaternion.LookRotation(Flat(to)).eulerAngles.y):0}° sideways, {upDown:0}° up/down; threshold {watchThreshold:0}°); its camera shifts to you.");
                            watchThreshold = Random.Range(30f, 45f);
                            flicking = true;
                        }
                    }
                    if (flicking) Flick();
                    break;
                }

                case Phase.Walk:
                case Phase.Sprint:
                    WalkTick(dt, phase == Phase.Sprint ? sprintSpeed : walkSpeed);
                    break;

                case Phase.Jump:
                    JumpTick(dt);
                    break;

                case Phase.Tumble:
                    if (rig.Tumbling)
                    {
                        float speed = rig.TumbleBody != null ? rig.TumbleBody.linearVelocity.magnitude : 0f;
                        if (!rig.Curling && ((age > 2.5f && speed < 0.5f) || age > 6f)) { rig.EndTumble(); next = Time.time + 2.5f; Plugin.Log.LogInfo($"Semibot test: stands up after tumbling {age:0.#} s."); }
                    }
                    else if (Time.time >= next)
                    {
                        // again: curl up, launched forward (the way it faces), roll, stand up
                        Vector3 fwd = Quaternion.Euler(0f, rig.Yaw, 0f) * Vector3.forward;
                        rig.StartTumble(fwd * 6f + Vector3.up * 2.5f, Quaternion.Euler(0f, rig.Yaw, 0f) * new Vector3(Random.Range(6f, 10f), Random.Range(-3f, 3f), Random.Range(-3f, 3f)));
                        phaseStart = Time.time;
                        Plugin.Log.LogInfo("Semibot test: Tumble again.");
                    }
                    break;

                case Phase.Search:
                    if (next < 0f) break;   // not the host: nothing to do
                    // looks around: flicks to random directions, mostly level or down (valuables lie low)
                    if (Time.time >= next)
                    {
                        next = Time.time + Random.Range(0.7f, 1.5f);
                        StartFlick(homeYaw + Random.Range(-110f, 110f), Random.Range(-5f, 40f));
                    }
                    Flick();
                    if (Time.time >= nextLookCheck)
                    {
                        nextLookCheck = Time.time + 0.2f;
                        PhysGrabObject v = rig.FindGrabbable();
                        if (v != null && InView(v))
                        {
                            spotted = v;
                            Plugin.Log.LogInfo($"Semibot test: spotted {v.name} after {age:0.#} s; grabbing it.");
                            SetPhase(Phase.Grab);
                            break;
                        }
                    }
                    if (age > SearchSeconds) { Plugin.Log.LogInfo($"Semibot test: found no valuable within {SemibotRig.GrabReach} m in {SearchSeconds:0} s; still looking."); phaseStart = Time.time; }
                    break;

                case Phase.Grab:
                    // holds it 5 s, lets go, then looks for one again (until the key)
                    if (next < 0f || age > 5f || rig.Held == null) { if (rig.Held != null) rig.Grab(null, 0f); SetPhase(Phase.Search); }
                    else
                    {
                        // keeps its eyes on what it holds (slowly turning)
                        rig.LookYaw = homeYaw + Mathf.Sin(age * 1.3f) * 25f;
                        rig.LookPitch = 8f + Mathf.Sin(age * 0.9f) * 10f;
                    }
                    break;
            }
        }

        private void WalkTick(float dt, float speed)
        {
            Vector3 to = Flat(goal - rig.Feet);
            float want = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to).eulerAngles.y : rig.Yaw;
            float off = Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, want));
            rig.Yaw = Mathf.MoveTowardsAngle(rig.Yaw, want, 360f * dt);   // turning (on the spot when far off)
            rig.LookYaw = rig.Yaw;
            rig.LookPitch = Mathf.MoveTowards(rig.LookPitch, 5f, 30f * dt);
            if (off > 45f) { rig.Moving = false; rig.Velocity = Vector3.zero; return; }
            if (to.magnitude < 0.15f)
            {
                goal = goal == lineA ? lineB : lineA;   // back and forth until the key
                return;
            }
            Vector3 step = to.normalized * Mathf.Min(speed * dt, to.magnitude);
            rig.Feet = Ground(rig.Feet + step);
            rig.Velocity = to.normalized * speed;
            rig.Moving = true;
            rig.Sprinting = phase == Phase.Sprint;
        }

        private void JumpTick(float dt)
        {
            if (!airborne)
            {
                if (rig.JumpPending) return;   // settling into the idle pose before the jump
                if (Time.time < next) return;
                if (lifting) { lifting = false; groundY = rig.Feet.y; jumpV = 4.5f; airborne = true; rig.Grounded = false; return; }
                rig.Jump();
                lifting = true;   // lifts off once the jump animation has started
                return;
            }
            jumpV -= 13f * dt;
            Vector3 f = rig.Feet; f.y += jumpV * dt;
            if (f.y <= groundY && jumpV < 0f) { f.y = groundY; airborne = false; rig.Grounded = true; jumpV = 0f; next = Time.time + 0.8f; rig.Land(); }
            rig.Feet = f;
            rig.Velocity = new Vector3(0f, jumpV, 0f);
        }

        // ---------------- the debug panel above it ----------------

        private static readonly System.Reflection.FieldInfo EyesActive = HarmonyLib.AccessTools.Field(typeof(PlayerEyes), "lookAtActive");
        private static readonly System.Reflection.FieldInfo EyesTalker = HarmonyLib.AccessTools.Field(typeof(PlayerEyes), "currentTalker");
        private static readonly System.Reflection.FieldInfo EyesGrabPoint = HarmonyLib.AccessTools.Field(typeof(PlayerEyes), "otherPhysGrabPoint");
        private static readonly System.Reflection.FieldInfo EyesSoft = HarmonyLib.AccessTools.Field(typeof(PlayerEyes), "overrideSoftActive");
        private static readonly System.Reflection.FieldInfo EyesSoftPos = HarmonyLib.AccessTools.Field(typeof(PlayerEyes), "overrideSoftPosition");
        private static readonly System.Reflection.FieldInfo EyesOverride = HarmonyLib.AccessTools.Field(typeof(PlayerEyes), "overrideActive");
        private static readonly System.Reflection.FieldInfo VoiceChat = HarmonyLib.AccessTools.Field(typeof(PlayerAvatar), "voiceChat");
        private static readonly System.Reflection.FieldInfo VoiceTalking = HarmonyLib.AccessTools.Field(typeof(PlayerVoiceChat), "isTalking");
        private static readonly System.Reflection.FieldInfo LightState = HarmonyLib.AccessTools.Field(typeof(FlashlightController), "currentState");
        private static readonly System.Reflection.FieldInfo LightWanted = HarmonyLib.AccessTools.Field(typeof(FlashlightController), "active");
        private GUIStyle panelStyle;

        private void OnGUI()
        {
            if (rig == null || rig.Eye == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 sp = cam.WorldToScreenPoint(rig.Eye.position + Vector3.up * 0.7f);
            if (sp.z < 0f) return;   // behind you
            if (panelStyle == null)
            {
                panelStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.UpperLeft, wordWrap = false, richText = true };
                panelStyle.normal.textColor = Color.white;
            }
            string text = PanelText();
            Vector2 size = panelStyle.CalcSize(new GUIContent(text));
            float x = Mathf.Clamp(sp.x - size.x * 0.5f, 4f, Screen.width - size.x - 4f);
            float y = Mathf.Clamp(Screen.height - sp.y - size.y, 4f, Screen.height - size.y - 4f);
            GUI.Box(new Rect(x, y, size.x + 8f, size.y + 4f), text, panelStyle);
        }

        private string PanelText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b>SEMIBOT TEST: {phase}</b>  ({Time.time - phaseStart:0.0} s)   [{Settings.DebugSemibotAnimNextKey.Value}] next\n");

            // the eyes: what the game's PlayerEyes is doing, in its own priority order
            PlayerEyes eyes = rig.Eyes;
            PlayerAvatar me = PlayerAvatar.instance;
            if (eyes == null) sb.Append("eyes: <color=red>no PlayerEyes on the copy</color>\n");
            else
            {
                bool active = EyesActive != null && (bool)EyesActive.GetValue(eyes);
                PlayerAvatar talker = EyesTalker?.GetValue(eyes) as PlayerAvatar;
                Transform grabPoint = EyesGrabPoint?.GetValue(eyes) as Transform;
                bool soft = EyesSoft != null && (bool)EyesSoft.GetValue(eyes);
                bool hard = EyesOverride != null && (bool)EyesOverride.GetValue(eyes);
                string why = hard ? "forced look (Override)"
                           : rig.BeamOn ? "what it grabs"
                           : grabPoint != null ? "someone's grabbed object (≤ 6 m)"
                           : soft ? $"soft look (OverrideSoft) at {(EyesSoftPos != null ? ((Vector3)EyesSoftPos.GetValue(eyes)).ToString("F1") : "?")}"
                           : talker != null ? $"talker {R.Name(talker)}"
                           : rig.Tumbling ? "2 m ahead (tumbling)"
                           : "idle target (straight ahead)";
                float dYou = me != null && eyes.lookAt != null ? Vector3.Distance(eyes.lookAt.position, R.Eyes(me)) : -1f;
                sb.Append($"eyes → {why}{(active ? "" : "  (lookAtActive off)")}; target {dYou:0.0} m from your eyes\n");
                if (me != null)
                {
                    float dist = Vector3.Distance(rig.Eye.position, R.Eyes(me));
                    object vc = VoiceChat?.GetValue(me);
                    bool talking = vc != null && VoiceTalking != null && (bool)VoiceTalking.GetValue(vc);
                    sb.Append($"you: {dist:0.0} m away, {(talking ? "<color=lime>talking</color>" : "not talking")} (talk ≤ 6 m or land a jump ≤ 5 m to be looked at)\n");
                }
            }

            // the camera and body
            float off = 0f, offSide = 0f, offUp = 0f;
            if (me != null)
            {
                Vector3 to = R.Eyes(me) - rig.Eye.position;
                off = Vector3.Angle(rig.Eye.forward, to);   // both directions at once
                offSide = Mathf.DeltaAngle(rig.LookYaw, Quaternion.LookRotation(Flat(to)).eulerAngles.y);
                offUp = Pitch(rig.Eye.position, R.Eyes(me)) - rig.LookPitch;
            }
            float shown = rig.Visuals != null ? rig.Visuals.transform.eulerAngles.y : rig.Yaw;
            sb.Append($"camera yaw {rig.LookYaw:0} pitch {rig.LookPitch:0}; body shown at {shown:0} ({Mathf.DeltaAngle(rig.LookYaw, shown):+0;-0;0}° behind); you are {off:0}° off its centre ({offSide:+0;-0;0}° sideways, {offUp:+0;-0;0}° {(offUp >= 0 ? "down" : "up")}){(phase == Phase.Pupils ? $" (shift at {watchThreshold:0}°)" : "")}\n");

            // the Animator
            Animator a = rig.BodyAnimator;
            if (a != null)
                sb.Append($"anim: speed {a.speed:0.##}{(a.IsInTransition(0) ? ", blending" : "")}; Moving {B(a, "Moving")} Sprinting {B(a, "Sprinting")} Jumping {B(a, "Jumping")} Falling {B(a, "Falling")} Tumbling {B(a, "Tumbling")} TumblingMove {B(a, "TumblingMove")} Grabbing {B(a, "Grabbing")} Turning {B(a, "Turning")}\n");
            sb.Append($"state: moving {rig.Moving}, sprinting {rig.Sprinting}, grounded {rig.Grounded}, tumbling {rig.Tumbling}{(rig.Curling ? " (curling)" : "")}, beam {(rig.BeamOn ? "on" : "off")}\n");

            // the flashlight
            FlashlightController fl = rig.Flashlight;
            sb.Append(fl == null ? "flashlight: <color=red>none</color>\n"
                : $"flashlight: state {LightState?.GetValue(fl)}, wanted {LightWanted?.GetValue(fl)}, lit {fl.LightActive}, mesh {(fl.mesh != null && fl.mesh.enabled ? "shown" : "hidden")}\n");

            if (rig.EyesErrors > 0) sb.Append($"<color=orange>PlayerEyes errors: {rig.EyesErrors} (see the log)</color>\n");
            string errors = SemibotGuard.Summary();
            if (errors.Length > 0) sb.Append($"<color=orange>game script errors: {errors}</color>\n");
            return sb.ToString().TrimEnd('\n');
        }

        private static string B(Animator a, string name)
        {
            foreach (AnimatorControllerParameter p in a.parameters)
                if (p.name == name) return a.GetBool(name) ? "<color=lime>on</color>" : "off";
            return "-";
        }

        // ---------------- helpers ----------------

        private void StartFlick(float yaw, float pitch)
        {
            flickFromYaw = rig.LookYaw; flickToYaw = rig.LookYaw + Mathf.DeltaAngle(rig.LookYaw, yaw);
            flickFromPitch = rig.LookPitch; flickToPitch = pitch;
            flickDur = Mathf.Clamp(0.12f + Mathf.Abs(flickToYaw - flickFromYaw) / 450f, 0.15f, 0.5f);
            flickStart = Time.time;
        }

        private void Flick()
        {
            float t = Mathf.Clamp01((Time.time - flickStart) / Mathf.Max(0.01f, flickDur));
            float s = t * t * t * (10f + t * (-15f + 6f * t));   // minimum jerk: fast in the middle, a dead stop
            rig.LookYaw = Mathf.Lerp(flickFromYaw, flickToYaw, s);
            rig.LookPitch = Mathf.Lerp(flickFromPitch, flickToPitch, s);
        }

        /// <summary>Within SearchViewAngle of where it's looking right now (its eye).</summary>
        private bool InView(PhysGrabObject v)
        {
            Transform eye = rig.Eye;
            Vector3 to = v.transform.position - eye.position;
            return to.sqrMagnitude < 0.01f || Vector3.Angle(eye.forward, to) <= SearchViewAngle;
        }

        private Vector3 Far() => Vector3.Distance(rig.Feet, lineA) < Vector3.Distance(rig.Feet, lineB) ? lineB : lineA;

        private static Vector3 Ground(Vector3 p) =>
            NavMesh.SamplePosition(p, out NavMeshHit hit, 1f, NavMesh.AllAreas) ? hit.position : p;

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v : Vector3.forward; }

        private static float Pitch(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return -Mathf.Atan2(d.y, Mathf.Max(0.01f, new Vector2(d.x, d.z).magnitude)) * Mathf.Rad2Deg;
        }

        private PhysGrabObject NearestValuable(float range)
        {
            PhysGrabObject best = null;
            float bestD = range;
            foreach (ValuableObject v in Object.FindObjectsByType<ValuableObject>(FindObjectsSortMode.None))
            {
                if (v == null || !v.gameObject.activeInHierarchy) continue;
                PhysGrabObject pgo = v.GetComponent<PhysGrabObject>();
                if (pgo == null) continue;
                float d = Vector3.Distance(rig.Feet, v.transform.position);
                if (d < bestD) { bestD = d; best = pgo; }
            }
            return best;
        }
    }
}
