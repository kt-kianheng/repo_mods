using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// The Alter Mimic's behaviour (on the enemy's own object, next to its Enemy and PhotonView).
    /// Host: roams invisibly; hunts a player who has been the only living player in their room for LoneSeconds;
    /// within LatchDistance it silently latches on (the target's voice is muted for everyone else), then appears out
    /// of sight near another player as the target's dead head or (SemibotChance) as the target's walking semibot
    /// (MimicSemibot: walks up to that player, talks, roams and grabs valuables, then shoots and runs away). The head lies still until another player has seen it for HeadNoticeSeconds, then "possesses" itself (eyes
    /// glow, legs out) and hops slowly towards the nearest player. It has 1 HP; grabbing it makes it explode.
    /// It ends (unlatches) when it dies, explodes or despawns (the game's own despawn timing).
    /// Every game: the mute and the head's look, from RPCs.
    /// </summary>
    public class AlterMimic : MonoBehaviour
    {
        private enum Mode { Roam, Hunt, Head, Semibot }
        internal const int FormNone = 0, FormHead = 1, FormSemibot = 2;

        private Enemy enemy;
        private EnemyParent parent;
        private EnemyNavMeshAgent nav;
        private EnemyRigidbody body;
        private EnemyHealth health;
        private PhotonView view;
        private Rigidbody rb;
        private PhysGrabObject grab;

        // host
        private Mode mode;
        private PlayerAvatar target, loneCandidate;
        private float loneSince = -1f, notLoneSince = -1f, nextLoneCheck, nextRoam, nextHuntPath, nextFollowFix;
        private bool possessed, talking, charging, countdown;
        private float countdownNext, nextInsideCheck, nextPulse, insideSince = -1f;
        private int insideKind;
        private bool vibrating;
        private int countdownIndex;
        private bool armed;
        private float chargeTime;
        private float nextEpisode = -1f, episodeNext;
        private int episodeLeft;
        private float seenSince = -1f;
        private bool noticed, actionChosen;
        private bool targetDeathLogged;

        // every game
        private int latchedView = -1;
        private int form = FormNone;
        private bool possessedLocal;
        private MimicHead head;
        private MimicSemibot bot;
        private bool botDeathHandled, deathHandled;
        private float despawnAt = -1f, dyingSince = -1f;
        private float nextBotAgent;
        private bool wasSpawned;
        private readonly List<Collider> bodyColliders = new List<Collider>();
        private Collider headCollider;   // the one collider kept (reshaped into a ball) in head form
        private bool bodySolid = true;

        private Vector3 BodyPos => rb != null ? rb.position : transform.position;
        internal EnemyHealth Health => health;
        /// <summary>Where it really is while hidden: the nav agent (its body has no colliders then, so it may lag or drift).</summary>
        private Vector3 AgentPos => transform.position;
        private bool originalGravity;
        private float nextHuntLog;

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
            parent = GetComponentInParent<EnemyParent>();
            nav = GetComponent<EnemyNavMeshAgent>() ?? GetComponentInChildren<EnemyNavMeshAgent>(true);
            health = GetComponent<EnemyHealth>() ?? GetComponentInChildren<EnemyHealth>(true);
            view = GetComponent<PhotonView>();
            body = parent != null ? parent.GetComponentInChildren<EnemyRigidbody>(true) : null;
            rb = body != null ? body.GetComponent<Rigidbody>() : null;
            grab = body != null ? body.GetComponent<PhysGrabObject>() : null;
            if (body != null)
            {
                originalGravity = body.gravity;
                foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
                    if (!c.isTrigger) bodyColliders.Add(c);
                foreach (System.Type t in new[] { typeof(SphereCollider), typeof(CapsuleCollider), typeof(BoxCollider) })
                {
                    if (headCollider != null) break;
                    foreach (Collider c in bodyColliders) if (t.IsInstanceOfType(c)) { headCollider = c; break; }
                }
            }
        }

        private void Start()
        {
            R.Info($"Alter Mimic: started (nav {(nav != null ? "yes" : "NO")}, rigidbody {(body != null ? "yes" : "NO")}, health {(health != null ? health.health.ToString() : "NO")}, view {(view != null ? view.ViewID.ToString() : "NO")}).");
            if (body != null)
            {
                var sizes = new List<string>();
                foreach (Collider c in body.GetComponentsInChildren<Collider>(true))
                    sizes.Add($"{c.GetType().Name} '{c.name}' {c.bounds.size} layer {LayerMask.LayerToName(c.gameObject.layer)}{(c.isTrigger ? " trigger" : "")}");
                R.Info($"Alter Mimic: rigidbody colliders: {string.Join("; ", sizes)}; gravity {body.gravity}, mass {(rb != null ? rb.mass : 0f)}, grabbable {(grab != null)}.");
            }
        }

        private void OnDisable() => EndLocal("disabled (despawned or died)");

        /// <summary>
        /// Every game: while it roams and hunts it has no solid body (players walk through it, nothing bumps it);
        /// only the head form is solid (to be bumped, grabbed and shot). Re-applied every frame in case the game
        /// switches the colliders back on.
        /// </summary>
        private void LateUpdate()
        {
            bool solid = form == FormHead;   // the semibot has its own hitbox (MimicBotHitbox); the enemy body stays hidden
            // without colliders its body would fall through the floor: no gravity while hidden (the body floats
            // after the nav agent, as the Hidden's does); the head falls
            if (body != null) body.gravity = form == FormHead;
            if (solid == bodySolid && !AnyMismatch(solid)) return;
            bodySolid = solid;
            foreach (Collider c in bodyColliders) if (c != null) c.enabled = Wanted(c, solid);
        }

        /// <summary>Hidden: no colliders. Head: only the one reshaped into the head's ball.</summary>
        private bool Wanted(Collider c, bool solid) => solid && (headCollider == null || c == headCollider);

        private bool AnyMismatch(bool solid)
        {
            foreach (Collider c in bodyColliders) if (c != null && c.enabled != Wanted(c, solid)) return true;
            return false;
        }

        // ---------------- host: the head is a plain physics object ----------------

        private bool bodyFreed, oldUseGravity, oldKinematic;

        /// <summary>
        /// Host, head form: the enemy's body script (which pushes and turns the body after the nav agent, with its
        /// own gravity) is switched off, so the rigidbody is plain physics like a dead head: gravity, bumps, grabs,
        /// and the jumps. Nothing else moves it, and switching between still and possessed keeps its motion.
        /// </summary>
        private void FreeBody(bool free)
        {
            if (body == null || rb == null || free == bodyFreed) return;
            bodyFreed = free;
            if (free)
            {
                oldUseGravity = rb.useGravity;
                oldKinematic = rb.isKinematic;
                body.enabled = false;
                rb.isKinematic = false;
                rb.useGravity = true;
            }
            else
            {
                rb.useGravity = oldUseGravity;
                rb.isKinematic = oldKinematic;
                body.enabled = true;
            }
            R.Info($"Alter Mimic: the body is {(free ? "plain physics now (the enemy body script is off)" : "back to normal")}.");
        }

        // ---------------- debug: see it through walls (SoloTesting only) ----------------

        private GUIStyle markerStyle;

        private void OnGUI()
        {
            if (!Settings.DebugSolo.Value || !wasSpawned) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            bool isHead = form == FormHead && head != null;
            bool isBot = form == FormSemibot && bot != null && bot.Rig != null;
            Vector3 world = isHead ? head.transform.position : isBot ? bot.Feet + Vector3.up * 2.1f : AgentPos;
            Vector3 sp = cam.WorldToScreenPoint(world + Vector3.up * 0.2f);
            if (sp.z < 0f) { sp.x = Screen.width - sp.x; sp.y = 0f; }   // behind you: pin to the bottom edge
            float x = Mathf.Clamp(sp.x, 40f, Screen.width - 40f);
            float y = Mathf.Clamp(Screen.height - sp.y, 30f, Screen.height - 30f);
            if (markerStyle == null)
            {
                markerStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                markerStyle.normal.textColor = Color.white;
            }
            float d = PlayerAvatar.instance != null ? Vector3.Distance(PlayerAvatar.instance.transform.position, world) : 0f;
            string label = isHead ? $"MIMIC HEAD{(possessedLocal ? " (possessed)" : "")}  {d:0} m"
                         : isBot ? $"MIMIC SEMIBOT: {bot.StateLabel}  {(health != null ? R.GetHealth(health).ToString("0") + " HP  " : "")}{d:0} m"
                         : $"mimic ({(R.IsHost ? mode.ToString().ToLower() : "hidden")})  {d:0} m";
            Color old = GUI.color;
            GUI.color = isHead || isBot ? new Color(1f, 0.25f, 0.25f) : new Color(0.7f, 0.7f, 0.7f, 0.8f);
            float w = isBot ? 520f : 360f;
            GUI.Box(new Rect(x - w * 0.5f, y - 16f, w, 32f), label, markerStyle);
            GUI.color = old;
        }

        private void Update()
        {
            bool spawned = parent != null && R.Spawned(parent);
            if (!spawned)
            {
                if (wasSpawned) { EndLocal("despawned"); if (R.IsHost) ResetHost(); }
                wasSpawned = false;
                return;
            }
            if (!wasSpawned) { wasSpawned = true; Plugin.Log.LogInfo($"Alter Mimic: spawned at {AgentPos}."); }
            OverchargeTick();
            if (!R.IsHost) return;

            if (health != null && (R.EnemyDead(health) || R.EnemyDying(health)))
            {
                HostDeath();
                return;
            }
            switch (mode)
            {
                case Mode.Roam: Roam(); break;
                case Mode.Hunt: Hunt(); break;
                case Mode.Head: HeadHost(); break;
                case Mode.Semibot: BotHost(); break;
            }
        }

        // ---------------- host: roaming and hunting ----------------

        private void ResetHost()
        {
            deathHandled = false;
            despawnAt = dyingSince = -1f;
            mode = Mode.Roam;
            target = loneCandidate = null;
            loneSince = notLoneSince = -1f;
            possessed = false;
            targetDeathLogged = false;
        }

        /// <summary>Host, invisible: the nav agent's speed = HunterSpeedVsPlayer x the player's walking speed (refreshed while it lasts).</summary>
        private void HunterSpeedTick()
        {
            if (nav == null || Time.time < nextSpeedSet) return;
            nextSpeedSet = Time.time + 0.25f;
            PlayerController pc = PlayerController.instance;
            float walk = pc != null && pc.MoveSpeed > 0.1f ? pc.MoveSpeed : 2.5f;
            float speed = walk * Settings.HunterSpeed.Value;
            try { nav.OverrideAgent(speed, Mathf.Max(speed * 4f, 8f), 0.5f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: setting its speed failed: {e.Message}"); nextSpeedSet = float.MaxValue; }
            if (!speedLogged) { speedLogged = true; R.Info($"Alter Mimic: invisible speed {speed:0.##} m/s ({Settings.HunterSpeed.Value:0.##} x walking {walk:0.##})."); }
        }
        private float nextSpeedSet;
        private bool speedLogged;

        private void Roam()
        {
            HunterSpeedTick();
            if (Time.time >= nextLoneCheck)
            {
                nextLoneCheck = Time.time + 0.5f;
                PlayerAvatar lone = FindLone();
                if (lone == null) loneCandidate = null;
                else
                {
                    if (lone != loneCandidate) { loneCandidate = lone; loneSince = Time.time; }
                    if (Time.time - loneSince >= Settings.LoneSeconds.Value)
                    {
                        target = lone;
                        mode = Mode.Hunt;
                        notLoneSince = -1f;
                        R.Info($"Alter Mimic: hunting {R.Name(target)} (alone in their room).");
                        return;
                    }
                }
            }
            if (Time.time >= nextRoam && nav != null)
            {
                nextRoam = Time.time + Random.Range(4f, 9f);
                List<LevelPoint> points = SemiFunc.LevelPointsGetAll();
                if (points != null && points.Count > 0)
                {
                    LevelPoint p = points[Random.Range(0, points.Count)];
                    if (p != null) nav.SetDestination(p.transform.position);
                }
            }
        }

        private void Hunt()
        {
            HunterSpeedTick();
            if (!R.Alive(target)) { R.Info("Alter Mimic: its target is gone; roaming again."); ResetHost(); return; }
            if (Time.time >= nextLoneCheck)
            {
                nextLoneCheck = Time.time + 0.5f;
                if (IsLone(target)) notLoneSince = -1f;
                else if (notLoneSince < 0f) notLoneSince = Time.time;
                else if (Time.time - notLoneSince > 3f) { R.Info($"Alter Mimic: {R.Name(target)} isn't alone any more; roaming again."); ResetHost(); return; }
            }
            if (Time.time >= nextHuntPath && nav != null)
            {
                nextHuntPath = Time.time + 0.3f;
                nav.SetDestination(target.transform.position);
            }
            float dist = Vector3.Distance(AgentPos, target.transform.position);
            if (Time.time >= nextHuntLog)
            {
                nextHuntLog = Time.time + 2f;
                R.Info($"Alter Mimic: hunting {R.Name(target)}: {dist:0.#} m away (agent at {AgentPos}, body at {BodyPos}).");
            }
            if (dist <= Settings.LatchDistance.Value) Latch();
        }

        // SoloTesting: you're the only player (singleplayer, or a lobby of one): it copies and appears near you
        private static bool Solo => Settings.DebugSolo.Value && Living().Count <= 1;   // living real players (not the TargetDummy mod's stand-in)

        internal static List<PlayerAvatar> Living()
        {
            var list = new List<PlayerAvatar>();
            foreach (PlayerAvatar p in SemiFunc.PlayerGetList())
                if (R.Alive(p) && !IsStandIn(p)) list.Add(p);
            return list;
        }

        /// <summary>The rooms (level modules) a player is in.</summary>
        private static HashSet<Object> Rooms(PlayerAvatar p)
        {
            var set = new HashSet<Object>();
            if (p == null || p.RoomVolumeCheck == null || p.RoomVolumeCheck.CurrentRooms == null) return set;
            foreach (RoomVolume rv in p.RoomVolumeCheck.CurrentRooms)
            {
                if (rv == null) continue;
                Module m = rv.GetComponentInParent<Module>();
                set.Add(m != null ? (Object)m : rv);
            }
            return set;
        }

        /// <summary>Is this player the only living player in their room?</summary>
        private static bool IsLone(PlayerAvatar p)
        {
            if (Solo) return R.Alive(p);
            HashSet<Object> mine = Rooms(p);
            if (mine.Count == 0) return false;
            foreach (PlayerAvatar q in Living())
            {
                if (q == p) continue;
                foreach (Object r in Rooms(q)) if (mine.Contains(r)) return false;
            }
            return true;
        }

        /// <summary>The nearest lone player, if any.</summary>
        private PlayerAvatar FindLone()
        {
            PlayerAvatar best = null;
            float bestD = float.MaxValue;
            foreach (PlayerAvatar p in Living())
            {
                if (!IsLone(p)) continue;
                float d = Vector3.Distance(AgentPos, p.transform.position);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>The TargetDummy mod's Ally Dummy (a stand-in player other mods can see): never a real player here.</summary>
        private static bool IsStandIn(PlayerAvatar p) => R.ViewId(p) >= 16000000 || R.Name(p) == "Ally Dummy";

        /// <summary>Players other than the target (in SoloTesting, with nobody else: you, the target, too).</summary>
        private List<PlayerAvatar> Others()
        {
            var list = new List<PlayerAvatar>();
            foreach (PlayerAvatar p in Living()) if (p != target) list.Add(p);
            if (list.Count == 0 && Settings.DebugSolo.Value && R.Alive(target)) list.Add(target);
            return list;
        }

        // ---------------- host: latching and teleporting ----------------

        private void Latch()
        {
            List<PlayerAvatar> others = Others();
            if (others.Count == 0)
            {
                var names = new List<string>();
                foreach (PlayerAvatar p in SemiFunc.PlayerGetList()) names.Add($"{R.Name(p)} (view {R.ViewId(p)}, alive {R.Alive(p)}, stand-in {IsStandIn(p)})");
                R.Info($"Alter Mimic: nobody else to appear near; roaming again. Players: {string.Join(", ", names)}; [Debug] SoloTesting is {(Settings.DebugSolo.Value ? "on" : "OFF")}.");
                ResetHost();
                return;
            }
            PlayerAvatar near = others[Random.Range(0, others.Count)];
            if (!HiddenSpotNear(near, out Vector3 spot)) { R.Info($"Alter Mimic: no spot out of sight near {R.Name(near)}; roaming again."); ResetHost(); return; }

            Teleport(spot);
            string force = Settings.DebugForceForm.Value;
            bool asBot = force == "Semibot" || (force != "Head" && Random.value < Settings.BotChance.Value);
            if (asBot)
            {
                Vector3 toThem = near.transform.position - spot; toThem.y = 0f;
                float yaw = toThem.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toThem).eulerAngles.y : 0f;
                int hp = Settings.BotHealth.Value;
                SetupHealth(hp, 0, 0, 0);
                SetResistance(0f);
                mode = Mode.Semibot;
                botDeathHandled = false;
                R.Info($"Alter Mimic: latched on to {R.Name(target)} (muted); appears as their semibot {Vector3.Distance(spot, near.transform.position):0.#} m from {R.Name(near)}, at {spot}.");
                Send(nameof(MimicLatchRPC), R.ViewId(target), FormSemibot, spot, yaw);
                if (bot != null) bot.StartHost(near);
                else { R.Info("Alter Mimic: the semibot couldn't be built; unlatching."); Unlatch("no semibot"); RestoreHidden(); }
                return;
            }
            SetupHealth(1, 0, 0, 1);   // the head has 1 HP: a shot, a thrown object or a hard fall ends it
            mode = Mode.Head;
            possessed = false;
            charging = false;
            countdown = false;
            armed = false;
            nextPulse = 0f;
            insideSince = -1f;
            insideKind = 0;
            vibrating = false;
            nextEpisode = -1f;
            seenSince = -1f;
            noticed = false;
            actionChosen = false;
            R.Info($"Alter Mimic: latched on to {R.Name(target)} (muted); appears as their dead head {Vector3.Distance(spot, near.transform.position):0.#} m from {R.Name(near)}, at {spot}.");
            Send(nameof(MimicLatchRPC), R.ViewId(target), FormHead, spot, 0f);
        }

        private bool HiddenSpotNear(PlayerAvatar near, out Vector3 spot)
        {
            spot = Vector3.zero;
            List<LevelPoint> points = SemiFunc.LevelPointGetWithinDistance(near.transform.position, Settings.TeleportMinDistance.Value, Settings.TeleportMaxDistance.Value);
            if (points == null || points.Count == 0) return false;
            // every living player (the latched one too) must be unable to see any part of a standing body there
            List<PlayerAvatar> watchers = Living();
            Vector3[] heights = { Vector3.up * 0.3f, Vector3.up * 1.0f, Vector3.up * 1.7f };
            for (int i = 0; i < points.Count; i++)   // shuffle
            {
                int j = Random.Range(i, points.Count);
                (points[i], points[j]) = (points[j], points[i]);
            }
            foreach (LevelPoint p in points)
            {
                if (p == null) continue;
                Vector3 pos = p.transform.position;
                bool seen = false;
                foreach (PlayerAvatar w in watchers)
                {
                    Vector3 eye = R.Eyes(w);
                    foreach (Vector3 h in heights) if (Clear(eye, pos + h)) { seen = true; break; }
                    if (seen) break;
                }
                if (!seen) { spot = pos; return true; }
            }
            return false;
        }

        /// <summary>
        /// Host: its health for the form. It takes damage from anything an enemy can be hurt by (weapons, thrown
        /// objects, hard impacts and falls), and dies like a tier 2 enemy (the game's death, dropping a soul orb).
        /// </summary>
        private void SetupHealth(int hp, int light, int medium, int heavy)
        {
            if (health == null) return;
            if (originalHp < 0) originalHp = health.health;
            health.health = hp;
            R.SetHealth(health, hp);
            health.impactHurt = true;
            health.impactLightDamage = light;
            health.impactMediumDamage = medium;
            health.impactHeavyDamage = heavy;
            health.objectHurt = true;
            health.spawnValuable = true;
            if (health.spawnValuableMax < 1) health.spawnValuableMax = 1;
            R.Info($"Alter Mimic: {hp} HP; impacts hurt {light}/{medium}/{heavy}; drops a soul orb.");
        }

        private int originalHp = -1;

        /// <summary>Host: back to the invisible hunter's own health after a form (so its next form starts fresh).</summary>
        private void RestoreHidden()
        {
            if (health == null) return;
            if (originalHp > 0) { health.health = originalHp; R.SetHealth(health, originalHp); }
            if (originalResistance >= 0f) SetResistance(originalResistance);
        }

        private static readonly FieldInfo ResistanceField = HarmonyLib.AccessTools.Field(typeof(EnemyHealth), "damageResistance");
        private float originalResistance = -1f;

        /// <summary>Host: the Hidden's damage resistance (0 for the semibot, so its Health is exact).</summary>
        private void SetResistance(float value)
        {
            if (health == null || ResistanceField == null) return;
            if (originalResistance < 0f) originalResistance = (float)ResistanceField.GetValue(health);
            ResistanceField.SetValue(health, value);
        }

        private void Teleport(Vector3 pos)
        {
            try
            {
                if (nav != null) nav.Warp(pos, true);
                if (rb != null)
                {
                    rb.position = pos + Vector3.up * 0.3f;
                    body.transform.position = pos + Vector3.up * 0.3f;
                    rb.linearVelocity = Vector3.zero;
                }
                if (enemy != null) enemy.EnemyTeleported(pos);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: teleport failed: {e}"); }
        }

        // ---------------- host: head form ----------------

        private void HeadHost()
        {
            if (target == null) { Unlatch("its target left"); return; }
            if (!targetDeathLogged && R.IsDead(target))
            {
                targetDeathLogged = true;
                R.Info($"Alter Mimic: {R.Name(target)} died (it stays a head).");
            }

            // the head moves only by physics and its own jumps (the enemy's body script is switched off, see
            // FreeBody); the invisible nav agent just keeps up with it, so the game's despawn checks see it there
            if (Time.time >= nextFollowFix && nav != null)
            {
                nextFollowFix = Time.time + 0.5f;
                try { nav.Stop(0.6f); nav.Warp(BodyPos, true); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: moving the agent failed: {e}"); nextFollowFix = float.MaxValue; }
            }

            // armed and grabbed: it counts down out loud (one word per CountdownGap), then explodes
            if (countdown)
            {
                if (Time.time < countdownNext) return;
                string[] words = CountdownWordList();
                if (countdownIndex < words.Length)
                {
                    Send(nameof(MimicSayRPC), words[countdownIndex]);
                    countdownIndex++;
                    countdownNext = Time.time + Settings.CountdownGap.Value;
                }
                else Explode();
                return;
            }
            bool grabbed = grab != null && grab.playerGrabbing != null && grab.playerGrabbing.Count > 0;
            if (armed && grabbed)
            {
                countdown = true;
                countdownIndex = 0;
                countdownNext = Time.time;
                if (!possessed) { possessed = true; Send(nameof(MimicPossessRPC), true); }
                charging = false;
                R.Info($"Alter Mimic: grabbed while armed; counting down ({Settings.CountdownWords.Value}) before it explodes.");
                return;
            }

            // in a cart or an extraction point: armed (vibrates from now on), and an invisible shockwave pushes
            // the objects around it away, every ShockwavePulseSeconds while it stays in there
            InsideTick();

            // who can see it: another living player with a clear line to it within HeadSightRange
            PlayerAvatar seer = null;
            float seerD = float.MaxValue;
            foreach (PlayerAvatar p in Others())
            {
                float d = Vector3.Distance(BodyPos, R.Eyes(p));
                if (d > Settings.HeadSightRange.Value || d >= seerD) continue;
                if (!Clear(R.Eyes(p), BodyPos)) continue;
                seerD = d; seer = p;
            }
            if (seer == null) seenSince = -1f;
            else if (seenSince < 0f) seenSince = Time.time;

            // still until someone has seen it for HeadNoticeSeconds: then a first possession of HeadFirstActions
            // actions; after each possession it goes still for HeadPossessWait, then possesses itself again (1-2
            // actions) as soon as someone can see it. Each action: a jump (HeadJumpChance) or a line.
            if (!possessed)
            {
                if (!noticed)
                {
                    if (seenSince < 0f || Time.time - seenSince < Settings.HeadNoticeSeconds.Value) return;
                    noticed = true;
                    StartPossession(Settings.HeadFirstActions.Value, seer, seerD, "first");
                    return;
                }
                if (Time.time < nextEpisode || seer == null) return;
                int min = Settings.HeadLaterActionsMin.Value, max = Mathf.Max(min, Settings.HeadLaterActionsMax.Value);
                StartPossession(Random.Range(min, max + 1), seer, seerD, "again");
                return;
            }

            if (Time.time < episodeNext) return;
            if (episodeLeft <= 0)
            {
                possessed = false;
                nextEpisode = Time.time + Settings.HeadPossessWait.Value;
                R.Info($"Alter Mimic: goes still (next time in {Settings.HeadPossessWait.Value:0} s, once someone can see it).");
                Send(nameof(MimicPossessRPC), false);
                return;
            }
            if (!actionChosen)
            {
                actionChosen = true;
                talking = Random.value >= Settings.HeadJumpChance.Value;
            }
            if (talking)
            {
                string line = PickLine();
                episodeLeft--;
                actionChosen = false;
                episodeNext = Time.time + 1.2f + 0.4f * line.Split(' ').Length;
                if (line.Length > 0) { R.Info($"Alter Mimic: says \"{line}\"."); Send(nameof(MimicSayRPC), line); }
                return;
            }
            if (rb == null) { episodeLeft = 0; return; }
            if (!Physics.Raycast(BodyPos + Vector3.up * 0.1f, Vector3.down, 1.2f, SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore))
            {
                episodeNext = Time.time + 0.3f;   // still in the air: wait to land
                return;
            }
            // like a possessed head: a springy charge, then a jump whose length follows the charge time
            if (!charging)
            {
                chargeTime = Random.Range(Settings.ChargeMin.Value, Mathf.Max(Settings.ChargeMin.Value, Settings.ChargeMax.Value));
                charging = true;
                episodeNext = Time.time + chargeTime;
                Send(nameof(MimicChargeRPC), chargeTime);
                return;
            }
            charging = false;
            episodeLeft--;
            actionChosen = false;
            episodeNext = Time.time + Random.Range(0.6f, 1f);
            Vector2 flat = Random.insideUnitCircle.normalized;   // any direction
            Vector3 dir = new Vector3(flat.x, 0f, flat.y);
            float c = chargeTime;
            rb.AddForce(dir * Settings.HopSpeedPerCharge.Value * c + Vector3.up * (Settings.HopUpward.Value + Settings.HopUpwardPerCharge.Value * c), ForceMode.VelocityChange);
            Vector3 spinAxis = Vector3.Cross(Vector3.up, dir);
            rb.AddTorque(spinAxis * (3f + 4f * c), ForceMode.VelocityChange);   // the game's forward flip
            R.Info($"Alter Mimic: jumps after a {c:0.##} s charge.");
            Send(nameof(MimicHopRPC), c >= 0.7f);
        }

        private string[] CountdownWordList()
        {
            var list = new List<string>();
            foreach (string w in (Settings.CountdownWords.Value ?? "").Split('|')) { string t = w.Trim(); if (t.Length > 0) list.Add(t); }
            return list.ToArray();
        }

        private static readonly FieldInfo CartItemsField = HarmonyLib.AccessTools.Field(typeof(PhysGrabCart), "itemsInCart");

        /// <summary>Where the head is: 0 nowhere special, 1 in a cart, 2 in an extraction point.</summary>
        private int InsideKind(Vector3 at)
        {
            bool extraction = false;
            foreach (Collider c in Physics.OverlapSphere(at, 0.05f, ~0, QueryTriggerInteraction.Collide))
            {
                if (c == null || bodyColliders.Contains(c)) continue;
                if (c.GetComponentInParent<PhysGrabInCart>() != null) return 1;
                RoomVolume rv = c.GetComponentInParent<RoomVolume>();
                if (rv != null && rv.Extraction) extraction = true;
            }
            // a cart's own list of what it holds (in case its inside volume missed the head's centre)
            if (CartItemsField != null && grab != null)
                foreach (PhysGrabCart cart in Object.FindObjectsByType<PhysGrabCart>(FindObjectsSortMode.None))
                    if (CartItemsField.GetValue(cart) is List<PhysGrabObject> items && items.Contains(grab)) return 1;
            return extraction ? 2 : 0;
        }

        private void InsideTick()
        {
            if (Time.time >= nextInsideCheck) { nextInsideCheck = Time.time + 0.25f; insideKind = InsideKind(BodyPos); }
            int kind = insideKind;
            if (kind == 0)
            {
                // taken out: no shaking; the next time it goes in, the next pulse comes 1 s later
                nextPulse = 0f; insideSince = -1f;
                if (vibrating) { vibrating = false; Send(nameof(MimicArmRPC), false); }
                return;
            }
            if (!armed)
            {
                // arms only after ArmSeconds inside without a break and with nobody holding it (so a player who is
                // still holding it in the cart isn't caught by the countdown)
                bool held = grab != null && grab.playerGrabbing != null && grab.playerGrabbing.Count > 0;
                if (held) { insideSince = -1f; return; }
                if (insideSince < 0f) { insideSince = Time.time; R.Info($"Alter Mimic: left in {(kind == 1 ? "a cart" : "an extraction point")}; arms in {Settings.ArmSeconds.Value:0.#} s if nobody picks it up."); }
                if (Time.time - insideSince < Settings.ArmSeconds.Value) return;
                armed = true;
                nextPulse = 0f;
                R.Info($"Alter Mimic: armed in {(kind == 1 ? "a cart" : "an extraction point")} (pulses; grabbing it now starts the countdown).");
            }
            // each pulse: it shakes violently for the last second before it, then goes quiet until the next one
            if (nextPulse <= 0f) nextPulse = Time.time + 1f;
            if (!vibrating && Time.time >= nextPulse - 1f) { vibrating = true; Send(nameof(MimicArmRPC), true); }
            if (Time.time >= nextPulse)
            {
                nextPulse = Time.time + Settings.ShockwavePulseSeconds.Value;
                float radius = kind == 1 ? Settings.ShockwaveRadiusCart.Value : Settings.ShockwaveRadiusExtraction.Value;
                Shockwave(BodyPos, radius);
                Send(nameof(MimicPulseRPC), BodyPos, radius);
                vibrating = false;
                Send(nameof(MimicArmRPC), false);
            }
        }

        /// <summary>
        /// Host: an invisible shockwave: every loose object within radius (not players, enemies, carts or the head
        /// itself) gets the game's hurt-zone physics push (an impulse of ShockwaveForce away from the head, tilted
        /// up), no damage (they can't break for 1.5 s).
        /// </summary>
        private void Shockwave(Vector3 centre, float radius)
        {
            float force = Settings.ShockwaveForce.Value;
            var done = new HashSet<PhysGrabObject>();
            foreach (Collider c in Physics.OverlapSphere(centre, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                PhysGrabObject pgo = c != null ? c.GetComponentInParent<PhysGrabObject>() : null;
                if (pgo == null || pgo == grab || !done.Add(pgo)) continue;
                if (pgo.GetComponent<PhysGrabCart>() != null) continue;
                if (pgo.GetComponentInParent<PlayerAvatar>() != null || pgo.GetComponentInParent<PlayerTumble>() != null) continue;
                if (pgo.GetComponentInParent<EnemyRigidbody>() != null) continue;
                Rigidbody r = pgo.GetComponent<Rigidbody>();
                if (r == null || r.isKinematic) continue;
                Vector3 dir = r.worldCenterOfMass - centre;
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Random.onUnitSphere;
                dir = (dir + Vector3.up * 0.5f).normalized;
                try { pgo.OverrideIndestructible(1.5f); } catch { }
                r.AddForce(dir * force, ForceMode.Impulse);
                r.AddTorque(Random.insideUnitSphere * force * 0.05f, ForceMode.Impulse);
            }
            R.Info($"Alter Mimic: shockwave ({radius:0.#} m, force {force:0}): pushed {done.Count} object(s).");
        }

        // ---- overcharge: grabbing an enemy overheats the grabber (the game's EnemyRigidbody.Update does this,
        //      but that script is off in head form on the host) ----

        private static readonly FieldInfo BodyOverchargeField = HarmonyLib.AccessTools.Field(typeof(EnemyRigidbody), "overchargeMultiplier");
        private static readonly FieldInfo ParentOverchargeField = HarmonyLib.AccessTools.Field(typeof(EnemyParent), "overrideOverchargeMultiplier");

        private void OverchargeTick()
        {
            if (form != FormHead || grab == null || !grab.grabbedLocal || body == null || body.enabled || parent == null) return;
            float m = 1f;
            try
            {
                if (BodyOverchargeField != null) m *= (float)BodyOverchargeField.GetValue(body);
                if (ParentOverchargeField != null) m *= (float)ParentOverchargeField.GetValue(parent);
                SemiFunc.EnemyOvercharge(grab, parent.difficulty, m);
            }
            catch (System.Exception e) { if (!overchargeWarned) { overchargeWarned = true; Plugin.Log.LogWarning($"Alter Mimic: overcharge failed: {e.Message}"); } }
        }
        private bool overchargeWarned;

        private void StartPossession(int actions, PlayerAvatar seer, float seerD, string which)
        {
            possessed = true;
            episodeLeft = Mathf.Max(1, actions);
            episodeNext = Time.time + 0.8f;
            actionChosen = false;
            charging = false;
            R.Info($"Alter Mimic: possessed ({which}; {R.Name(seer)} sees it from {seerD:0.#} m): {episodeLeft} action(s).");
            Send(nameof(MimicPossessRPC), true);   // no push: waking up doesn't change how it's moving
        }

        /// <summary>The nearest living player within range (no line of sight needed).</summary>
        internal static PlayerAvatar NearestPlayer(Vector3 from, float range)
        {
            PlayerAvatar best = null;
            float bestD = range;
            foreach (PlayerAvatar p in Living())
            {
                float d = Vector3.Distance(from, p.transform.position);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>Can this player's eyes be seen from <paramref name="eye"/> within range?</summary>
        internal static bool CanSee(Vector3 eye, PlayerAvatar p, float range)
        {
            if (!R.Alive(p)) return false;
            Vector3 their = R.Eyes(p);
            return Vector3.Distance(eye, their) <= range && Clear(eye, their);
        }

        /// <summary>
        /// Is the line between two points free of walls and objects? Players (any of their colliders: the line ends
        /// inside one) and any Alter Mimic's own body don't count.
        /// </summary>
        internal static bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            foreach (RaycastHit h in Physics.RaycastAll(from, d / len, len, SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore))
            {
                Collider c = h.collider;
                if (c == null) continue;
                if (c.GetComponentInParent<PlayerAvatar>() != null || c.GetComponentInParent<PlayerController>() != null) continue;
                if (c.GetComponentInParent<MimicBotHitbox>() != null) continue;
                EnemyRigidbody er = c.GetComponentInParent<EnemyRigidbody>();
                if (er != null && er.enemy != null && er.enemy.GetComponent<AlterMimic>() != null) continue;
                return false;
            }
            return true;
        }

        /// <summary>The nearest living player whose eyes can be seen from <paramref name="eye"/> within range.</summary>
        internal static PlayerAvatar NearestVisible(Vector3 eye, float range, PlayerAvatar exclude)
        {
            PlayerAvatar best = null;
            float bestD = range;
            foreach (PlayerAvatar p in Living())
            {
                if (p == exclude) continue;
                Vector3 their = R.Eyes(p);
                float d = Vector3.Distance(eye, their);
                if (d > bestD) continue;
                if (!Clear(eye, their)) continue;
                bestD = d; best = p;
            }
            return best;
        }

        private string lastLine;

        /// <summary>A random head-form line (HeadLines and the one-word SharedLines), not the same twice in a row.</summary>
        private string PickLine()
        {
            var lines = new List<string>();
            foreach (string raw in new[] { Settings.HeadLines.Value, Settings.SharedLines.Value })
                if (!string.IsNullOrEmpty(raw))
                    foreach (string l in raw.Split('|')) { string t = l.Trim(); if (t.Length > 0) lines.Add(t); }
            if (lines.Count == 0) return "";
            if (lines.Count > 1) lines.Remove(lastLine);
            lastLine = lines[Random.Range(0, lines.Count)];
            return lastLine;
        }

        // ---------------- host: death ----------------

        /// <summary>
        /// Host: the game's death only fires EnemyHealth.onDeath; a vanilla enemy's own script answers it with
        /// EnemyParent.Despawn (which also drops the soul orb). The Hidden's scripts are gone, so the mimic does it:
        /// the semibot collapses into tumble form, then (once the game marks it dead) a puff and Despawn.
        /// </summary>
        private void HostDeath()
        {
            if (mode == Mode.Semibot && bot != null && !botDeathHandled) { botDeathHandled = true; bot.HostDied(); }
            if (deathHandled)
            {
                if (despawnAt > 0f && Time.time >= despawnAt)
                {
                    despawnAt = -1f;
                    Vector3 at = form == FormSemibot && bot != null ? bot.Chest : BodyPos;
                    try { if (nav != null) nav.Warp(at, true); } catch { }   // the soul orb drops where it died
                    R.Info($"Alter Mimic: died; despawning (soul orb) at {at}.");
                    Send(nameof(MimicDeathFxRPC), at);
                    try { parent.Despawn(); }
                    catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: despawning failed: {e}"); }
                }
                return;
            }
            if (dyingSince < 0f) dyingSince = Time.time;
            if (!R.EnemyDead(health) && Time.time - dyingSince < 3f) return;   // dying: wait for the game's death freeze to end
            deathHandled = true;
            despawnAt = Time.time + (mode == Mode.Semibot ? 1.2f : 0.2f);
        }

        /// <summary>Every game (from HurtRPC/DeathRPC): the semibot flashes like a hurt enemy.</summary>
        internal void HurtVisual()
        {
            if (bot != null && bot.Rig != null) bot.Rig.Flash(HurtColor(), 0.35f);
        }

        private static readonly FieldInfo HurtColorField = HarmonyLib.AccessTools.Field(typeof(EnemyHealth), "hurtColor");

        private Color HurtColor()
        {
            try { if (HurtColorField != null && health != null) { Color c = (Color)HurtColorField.GetValue(health); if (c.a > 0f) return c; } } catch { }
            return Color.white;
        }

        [PunRPC]
        private void MimicDeathFxRPC(Vector3 at)
        {
            try { Blast.Spawn(at, 1.2f); } catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: death puff failed: {e.Message}"); }
        }

        // ---------------- host: semibot form ----------------

        /// <summary>Host: the semibot runs itself (MimicSemibot); the invisible enemy (nav agent) just keeps up with it.</summary>
        private void BotHost()
        {
            if (target == null) { Unlatch("its target left"); RestoreHidden(); return; }
            if (bot == null || bot.Rig == null) { Unlatch("the semibot is gone"); RestoreHidden(); return; }
            if (Time.time >= nextBotAgent && nav != null)
            {
                nextBotAgent = Time.time + 0.3f;
                try { nav.Stop(0.6f); nav.Warp(bot.Feet, true); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: moving the agent failed: {e}"); nextBotAgent = float.MaxValue; }
            }
        }

        /// <summary>Host: the semibot ran off out of sight: unlatch (the target can be heard again) and despawn (no soul orb: it isn't dead; the game respawns it later).</summary>
        internal void BotVanished()
        {
            Unlatch("the semibot ran off");
            RestoreHidden();
            R.Info("Alter Mimic: despawns after running off.");
            try { parent.Despawn(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: despawning failed: {e}"); }
        }

        /// <summary>Host: TTS from the current form on every game.</summary>
        internal void SayEverywhere(string line) => Send(nameof(MimicSayRPC), line);

        /// <summary>Host: an RPC to the other games only (nothing in singleplayer: the host already did it).</summary>
        internal void SendOthers(string method, params object[] args)
        {
            if (R.Multiplayer && view != null) view.RPC(method, RpcTarget.Others, args);
        }

        /// <summary>Host: someone grabbed the head. It explodes, hurting everyone in range, and the mimic is gone.</summary>
        private void Explode()
        {
            Vector3 c = BodyPos;
            int damage = Settings.ExplosionDamage.Value;
            float radius = Settings.ExplosionRadius.Value;
            R.Info($"Alter Mimic: grabbed; exploding at {c} ({damage} damage within {radius} m).");
            Send(nameof(MimicExplodeRPC), c);

            foreach (PlayerAvatar p in Living())
            {
                Vector3 chest = p.transform.position + Vector3.up * 0.9f;
                if (Vector3.Distance(chest, c) > radius) continue;
                try { p.playerHealth.HurtOther(damage, c, false, -1, false); R.Info($"Alter Mimic: the blast hits {R.Name(p)}."); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: hurting {R.Name(p)} failed: {e}"); }
            }
            var hit = new HashSet<Enemy>();
            foreach (Collider col in Physics.OverlapSphere(c, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                EnemyRigidbody er = col.GetComponentInParent<EnemyRigidbody>();
                Enemy e = er != null ? er.enemy : null;
                if (e == null || e == enemy || !hit.Add(e)) continue;
                EnemyHealth h = e.GetComponent<EnemyHealth>() ?? e.GetComponentInChildren<EnemyHealth>();
                if (h == null) continue;
                try { h.Hurt(damage, (e.transform.position - c).normalized); R.Info($"Alter Mimic: the blast hits {e.name}."); }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Alter Mimic: hurting {e.name} failed: {ex}"); }
            }
            Unlatch("exploded");
            try { parent.Despawn(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: despawning failed: {e}"); }
        }

        private void Unlatch(string why)
        {
            R.Info($"Alter Mimic: unlatches ({why}).");
            Send(nameof(MimicUnlatchRPC));
            ResetHost();
        }

        // ---------------- every game ----------------

        private void Send(string method, params object[] args)
        {
            if (R.Multiplayer && view != null) { view.RPC(method, RpcTarget.All, args); return; }
            MethodInfo m = GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null) m.Invoke(this, args);
        }

        [PunRPC]
        private void MimicLatchRPC(int targetView, int newForm, Vector3 at, float yaw)
        {
            EndLocal(null);
            latchedView = targetView;
            form = newForm;
            MimicVoice.Mute(targetView);
            PlayerAvatar who = R.PlayerByView(targetView);
            if (form == FormHead && body != null)
            {
                foreach (Collider c in bodyColliders) if (c != null) c.enabled = Wanted(c, true);   // solid as a head
                bodySolid = true;
                head = MimicHead.Create(body.transform, who, headCollider);
                if (R.IsHost) FreeBody(true);
                R.Info($"Alter Mimic: (this game) the head is at {head.transform.position}{(PlayerAvatar.instance != null ? $", {Vector3.Distance(head.transform.position, PlayerAvatar.instance.transform.position):0.#} m from you" : "")}.");
            }
            if (form == FormSemibot)
            {
                bot = MimicSemibot.Create(this, who, at, yaw);
            }
            R.Info($"Alter Mimic: (this game) latched on to {R.Name(who)}; their voice is muted here{(who == PlayerAvatar.instance ? " for the others (it's you)" : "")}.");
        }

        [PunRPC]
        private void MimicPossessRPC(bool on)
        {
            possessedLocal = on;
            if (head != null) head.SetPossessed(on);
        }

        [PunRPC]
        private void MimicSayRPC(string line)
        {
            if (head != null) head.Say(line, R.PlayerByView(latchedView));
            else if (bot != null) bot.Speak(line);
        }

        // the semibot (the host does each thing itself and sends it to the others)

        [PunRPC]
        private void MimicBotStateRPC(Vector3 feet, float yaw, float pitch, int flags)
        {
            if (bot != null) bot.NetState(feet, yaw, pitch, flags);
        }

        [PunRPC]
        private void MimicBotTumbleRPC(Vector3 velocity, Vector3 spin)
        {
            if (bot != null) bot.TumbleLocal(velocity, spin);
        }

        [PunRPC]
        private void MimicBotStandRPC(Vector3 feet, float yaw)
        {
            if (bot != null) bot.StandLocal(feet, yaw);
        }

        [PunRPC]
        private void MimicBotGrabRPC(int viewId)
        {
            if (bot == null) return;
            PhotonView pv = viewId >= 0 ? PhotonView.Find(viewId) : null;
            bot.GrabLocal(pv != null ? pv.GetComponent<PhysGrabObject>() : null);
        }

        [PunRPC]
        private void MimicBotGunRPC(bool on)
        {
            if (bot != null) bot.ShowGun(on);
        }

        [PunRPC]
        private void MimicBotShotRPC(Vector3 to, bool hit, bool surface, int damage, bool effects)
        {
            if (bot != null) bot.Fire(to, hit, surface, damage, effects);
        }

        /// <summary>Every game: the head shakes violently (the second before each pulse) or stops.</summary>
        [PunRPC]
        private void MimicArmRPC(bool on)
        {
            if (head != null) head.SetArmed(on);
        }

        /// <summary>Every game: the shockwave's sound and faint bubble (the push itself is the host's).</summary>
        [PunRPC]
        private void MimicPulseRPC(Vector3 at, float radius) => MimicFx.Pulse(at, radius);

        [PunRPC]
        private void MimicChargeRPC(float seconds)
        {
            if (head != null) head.Charge(seconds);
        }

        [PunRPC]
        private void MimicHopRPC(bool longJump)
        {
            if (head != null) head.Hop(longJump);
        }

        [PunRPC]
        private void MimicExplodeRPC(Vector3 centre)
        {
            MimicFx.Explode(centre);
            EndLocal(null);
        }

        [PunRPC]
        private void MimicUnlatchRPC() => EndLocal("unlatched");

        /// <summary>This game: the mute ends and the head goes.</summary>
        private void EndLocal(string why)
        {
            if (latchedView >= 0)
            {
                MimicVoice.Unmute(latchedView);
                if (why != null) R.Info($"Alter Mimic: (this game) {why}; {R.Name(R.PlayerByView(latchedView))} can be heard again.");
            }
            if (R.IsHost) FreeBody(false);
            latchedView = -1;
            form = FormNone;
            possessedLocal = false;
            if (head != null) { Destroy(head.gameObject); head = null; }
            if (bot != null) { Destroy(bot.gameObject); bot = null; }
        }
    }
}
