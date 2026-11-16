using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace HatterEnemiesPack
{
    /// <summary>
    /// The Alter Mimic's semibot form: a copy of the latched player's semibot (SemibotRig: their look, animated by the
    /// game's own player code), speaking in their TTS voice, with a hitbox and a shotgun. One per game; the host runs
    /// the state machine and streams its position and view to the others (AlterMimic's MimicBot* RPCs).
    ///
    /// Host state machine:
    ///   Approach   walks (path on the nav mesh) to the player it appeared near, until GreetDistance away and in sight
    ///   Greet      stands still watching them; after GreetIdle s says a GreetLines line
    ///   LookAround looks around; after LookAround s says a WarnLines line
    ///   Plead      keeps looking around until that line has been said, then says a HelpLines line
    ///   Roam       RoamSeconds of moving around the room: walk or sprint to a spot (or to a valuable it can see),
    ///              then search (looks around; a valuable within 1 m, in clear sight, within 60° of its view is
    ///              grabbed and carried for CarrySeconds)
    ///   GunIdle    stands still facing the nearest player (drops what it carries)
    ///   GunDraw    the shotgun comes out on its grab beam
    ///   Shoot      walks after the nearest player within ChaseRange (10 m) until ChaseGap (2 m) away, shooting
    ///              (Shots shots, ShotGap s apart) whenever it can see them; nobody within ChaseRange → Flee
    ///   Flee       sprints away from the players; once nobody has seen it for a moment the mimic unlatches and despawns
    ///              (no soul orb; the game respawns it later)
    ///   Tumble     hurt before GunIdle: knocked into tumble form, stands up, then GunIdle
    /// Health: [Alter Mimic Semibot] Health (100; set by AlterMimic). It dies like a tier 2 enemy (soul orb); it collapses first.
    /// </summary>
    internal class MimicSemibot : MonoBehaviour
    {
        internal enum State { Approach, Greet, LookAround, Plead, Roam, Tumble, GunIdle, GunDraw, Shoot, Flee, Gone, Dead }
        private enum Step { Pick, Move, Search, Pause }

        internal AlterMimic Owner;
        internal SemibotRig Rig;
        internal PlayerAvatar LookOf;
        private MimicSpeaker speaker;
        private MimicGun gun;
        private MimicBotHitbox hitbox;
        private bool gunOut;
        private float walkSpeed = 2.5f, sprintSpeed = 5f;
        private const float GunHold = 0.9f;

        // ---------------- building (every game) ----------------

        internal static MimicSemibot Create(AlterMimic owner, PlayerAvatar look, Vector3 feet, float yaw)
        {
            if (look == null) { Plugin.Log.LogWarning("Alter Mimic: no player to copy for the semibot."); return null; }
            feet = Ground(feet);
            SemibotRig rig = SemibotRig.Create(look, feet, yaw);
            if (rig == null) return null;
            var go = new GameObject("Alter Mimic Semibot");
            var bot = go.AddComponent<MimicSemibot>();
            bot.Owner = owner;
            bot.Rig = rig;
            bot.LookOf = look;
            bot.speaker = MimicSpeaker.Create(rig.transform, Vector3.up * rig.EyeHeight, 0.45f, look);
            bot.gun = MimicGun.Create();
            bot.hitbox = MimicBotHitbox.Create(bot, rig.EyeHeight + 0.25f);
            Collider ball = rig.TumbleCollider;
            if (ball != null && bot.hitbox.Capsule != null) Physics.IgnoreCollision(ball, bot.hitbox.Capsule, true);
            bot.Speeds(look);
            bot.netFeet = bot.netPrevFeet = feet; bot.netYaw = yaw;
            R.Info($"Alter Mimic: the semibot ({R.Name(look)}'s look) is at {feet}; walk {bot.walkSpeed:0.##} m/s, sprint {bot.sprintSpeed:0.##} m/s; gun {(bot.gun != null ? "ready" : "MISSING")}.");
            return bot;
        }

        /// <summary>The game's own speeds: walk = PlayerController.MoveSpeed; sprint = the base sprint speed + the copied player's speed upgrades.</summary>
        private void Speeds(PlayerAvatar look)
        {
            PlayerController pc = PlayerController.instance;
            if (pc == null) return;
            walkSpeed = pc.MoveSpeed;
            int ups = 0;
            string id = SemibotRig.F<string>(look, "steamID");
            if (!string.IsNullOrEmpty(id) && StatsManager.instance != null && StatsManager.instance.playerUpgradeSpeed != null) StatsManager.instance.playerUpgradeSpeed.TryGetValue(id, out ups);
            sprintSpeed = pc.SprintSpeed - pc.SprintSpeedUpgrades + Mathf.Max(0, ups);
        }

        private void OnDestroy()
        {
            if (Rig != null) Destroy(Rig.gameObject);
            if (gun != null) Destroy(gun.gameObject);
            if (hitbox != null) Destroy(hitbox.gameObject);
        }

        // ---------------- every game ----------------

        internal Vector3 Feet => Rig != null ? Rig.Feet : transform.position;
        private Vector3 EyePos => (Rig.Tumbling && Rig.TumbleBody != null ? Rig.TumbleBody.position : Rig.Feet) + Vector3.up * Rig.EyeHeight;
        internal Vector3 Chest => (Rig.Tumbling && Rig.TumbleBody != null ? Rig.TumbleBody.position : Rig.Feet + Vector3.up * (Rig.EyeHeight * 0.7f));
        private Vector3 LookDir => Quaternion.Euler(Rig.LookPitch, Rig.LookYaw, 0f) * Vector3.forward;

        internal void Speak(string line)
        {
            if (speaker != null) speaker.Say(line);
        }

        internal bool IsHolding(PhysGrabObjectImpactDetector d) => d != null && Rig != null && Rig.Held != null && d.transform.IsChildOf(Rig.Held.transform);

        private void LateUpdate()
        {
            if (Rig == null) return;
            if (hitbox != null) hitbox.Place(Rig.Tumbling && Rig.TumbleBody != null ? Rig.TumbleBody.position : Rig.Feet, Rig.Tumbling, Rig.EyeHeight + 0.25f);
            if (gunOut && gun != null) gun.Place(Rig.HoldPoint(GunHold) + Vector3.down * 0.08f, LookDir);
        }

        internal void ShowGun(bool on)
        {
            if (gun == null || Rig == null || gunOut == on) return;
            gunOut = on;
            if (on) { gun.Place(Rig.HoldPoint(GunHold), LookDir); gun.Show(true); Rig.HoldProp(gun.transform, GunHold); }
            else { Rig.HoldProp(null, 0f); gun.Show(false); }
        }

        internal void Fire(Vector3 to, bool hit, bool surface, int damage, bool effects)
        {
            if (gun == null) return;
            HurtCollider hc = gun.Fire(to, hit, surface, damage, effects);
            if (hc != null && hitbox != null) hitbox.NextHit[hc] = float.MaxValue;   // its own bullet never hurts it
        }

        internal void GrabLocal(PhysGrabObject obj)
        {
            if (Rig == null) return;
            if (obj == null) { Rig.Grab(null, 0f); return; }
            if (hitbox != null && hitbox.Capsule != null)
                foreach (Collider c in obj.GetComponentsInChildren<Collider>()) if (c != null) Physics.IgnoreCollision(c, hitbox.Capsule, true);
            Rig.Grab(obj, 1.6f, true);
        }

        internal void TumbleLocal(Vector3 velocity, Vector3 spin)
        {
            if (Rig == null || Rig.Tumbling) return;
            if (Rig.Held != null) Rig.Grab(null, 0f);
            Rig.Moving = Rig.Sprinting = false;
            Rig.Velocity = Vector3.zero;
            Rig.StartTumble(velocity, spin);
        }

        internal void StandLocal(Vector3 feet, float yaw)
        {
            if (Rig == null) return;
            if (Rig.Tumbling) Rig.EndTumble();
            Rig.Feet = feet;
            Rig.Yaw = Rig.LookYaw = yaw;
            netFeet = feet; netYaw = yaw;
        }

        // ---------------- clients: following the host's stream ----------------

        private Vector3 netFeet, netPrevFeet;
        private float netYaw, netPitch, netAt, netGap = 0.1f;
        private int netFlags;

        internal void NetState(Vector3 feet, float yaw, float pitch, int flags)
        {
            netPrevFeet = netFeet;
            netFeet = feet; netYaw = yaw; netPitch = pitch; netFlags = flags;
            if (netAt > 0f) netGap = Mathf.Clamp(Time.time - netAt, 0.05f, 0.5f);
            netAt = Time.time;
        }

        private void ClientTick(float dt)
        {
            if (Rig == null || Rig.Tumbling) return;
            Vector3 vel = (netFeet - netPrevFeet) / netGap;
            Vector3 target = netFeet + vel * Mathf.Min(Time.time - netAt, 0.2f);   // a little ahead, as the stream lags
            if (Vector3.Distance(Rig.Feet, target) > 4f) Rig.Feet = target;
            else Rig.Feet = Vector3.Lerp(Rig.Feet, target, 1f - Mathf.Exp(-12f * dt));
            Rig.LookYaw = Mathf.LerpAngle(Rig.LookYaw, netYaw, 1f - Mathf.Exp(-15f * dt));
            Rig.LookPitch = Mathf.Lerp(Rig.LookPitch, netPitch, 1f - Mathf.Exp(-15f * dt));
            Rig.Moving = (netFlags & 1) != 0;
            Rig.Sprinting = (netFlags & 2) != 0;
            Rig.Velocity = Rig.Moving ? new Vector3(vel.x, 0f, vel.z) : Vector3.zero;
        }

        // ---------------- host ----------------

        private bool host;
        private float pleadGapUntil = -1f;
        internal State Current { get; private set; } = State.Approach;
        private float stateStart, stateUntil;
        private PlayerAvatar greet;
        private int lastHp = -1;
        private Vector3 lastHurtDir;
        private float nextNet;

        internal string StateLabel => host ? $"{Current}{(Current == State.Roam ? $" {step} ({Settings.BotRoamSeconds.Value - (Time.time - roamStart):0}s left)" : "")}" : "semibot";

        /// <summary>Host: start the state machine, walking up to this player.</summary>
        internal void StartHost(PlayerAvatar near)
        {
            host = true;
            greet = near;
            lastHp = Owner != null && Owner.Health != null ? (int)R.GetHealth(Owner.Health) : -1;
            SetState(State.Approach);
        }

        private void SetState(State s)
        {
            Current = s;
            stateStart = Time.time;
            homeYaw = Rig.Yaw;
            string what = "";
            switch (s)
            {
                case State.Approach: what = $"walks up to {R.Name(greet)}"; nextPath = 0f; break;
                case State.Greet:
                    StopMoving();
                    watchThreshold = 0f;   // the first look goes straight to them
                    stateUntil = Time.time + Range(Settings.BotGreetIdleMin, Settings.BotGreetIdleMax);
                    what = $"stands in front of {R.Name(greet)}; first line in {stateUntil - Time.time:0.#} s";
                    break;
                case State.LookAround:
                    StopMoving();
                    nextFlick = 0f;
                    stateUntil = Time.time + Range(Settings.BotLookAroundMin, Settings.BotLookAroundMax);
                    what = $"looks around for {stateUntil - Time.time:0.#} s";
                    break;
                case State.Plead:
                    pleadGapUntil = -1f;
                    what = "waits for its warning line to finish, then asks for help";
                    break;
                case State.Roam:
                    roamStart = Time.time;
                    roamAnchor = Rig.Feet;
                    step = Step.Pick;
                    what = $"roams the room for {Settings.BotRoamSeconds.Value:0} s";
                    break;
                case State.Tumble: what = "knocked into tumble form"; break;
                case State.GunIdle:
                    StopMoving();
                    Release();
                    watchThreshold = 0f;
                    stateUntil = Time.time + 1.5f;
                    what = "stands still";
                    break;
                case State.GunDraw:
                    StopMoving();
                    ShowGun(true);
                    Owner.SendOthers("MimicBotGunRPC", true);
                    stateUntil = Time.time + 0.8f;
                    what = "takes out a shotgun";
                    break;
                case State.Shoot:
                    shotsLeft = Settings.BotShots.Value;
                    chaseTarget = null;
                    nextPath = 0f;
                    nextShot = Time.time + 0.3f;
                    what = $"shoots ({shotsLeft} shots, {Settings.BotShotGapMin.Value:0.#}-{Settings.BotShotGapMax.Value:0.#} s apart)";
                    break;
                case State.Flee:
                    ShowGun(false);
                    Owner.SendOthers("MimicBotGunRPC", false);
                    unseenSince = -1f;
                    fleeTries = 0;
                    PickFleeSpot();
                    what = "runs away";
                    break;
                case State.Gone: StopMoving(); what = "vanished (nobody could see it)"; break;
                case State.Dead: StopMoving(); what = "died"; break;
            }
            R.Info($"Alter Mimic semibot: {s}: {what}.");
        }

        private void Update()
        {
            if (Rig == null) return;
            if (!host) { ClientTick(Time.deltaTime); return; }
            if (Current == State.Gone || Current == State.Dead) return;
            float dt = Time.deltaTime;
            HurtCheck();
            switch (Current)
            {
                case State.Approach: Approach(dt); break;
                case State.Greet:
                    Watch(greet);
                    if (Time.time >= stateUntil) { Say(Settings.BotGreetLines.Value); SetState(State.LookAround); }
                    break;
                case State.LookAround:
                    Pan(80f, -35f, 40f, 0.8f, 1.6f);
                    if (Time.time >= stateUntil) { Say(Settings.BotWarnLines.Value); SetState(State.Plead); }
                    break;
                case State.Plead:
                {
                    Pan(80f, -35f, 40f, 0.8f, 1.6f);
                    // the warning line has finished (the speech takes a moment to start), or 8 s at most
                    float age = Time.time - stateStart;
                    bool done = age >= 8f || (age >= 1.5f && (speaker == null || !speaker.Speaking));
                    if (done && pleadGapUntil < 0f) pleadGapUntil = Time.time + 0.4f;
                    if (pleadGapUntil > 0f && Time.time >= pleadGapUntil) { Say(Settings.BotHelpLines.Value); SetState(State.Roam); }
                    break;
                }
                case State.Roam: Roam(dt); break;
                case State.Tumble: TumbleTick(); break;
                case State.GunIdle:
                {
                    PlayerAvatar p = AlterMimic.NearestPlayer(Rig.Feet, Settings.BotChaseRange.Value);
                    if (p == null) { R.Info($"Alter Mimic semibot: nobody within {Settings.BotChaseRange.Value:0} m; runs away."); SetState(State.Flee); break; }
                    Watch(p);
                    if (Time.time >= stateUntil) SetState(State.GunDraw);
                    break;
                }
                case State.GunDraw:
                {
                    if (gun == null) { R.Info("Alter Mimic semibot: no gun; runs away."); SetState(State.Flee); break; }
                    PlayerAvatar p = AlterMimic.NearestPlayer(Rig.Feet, Settings.BotChaseRange.Value);
                    if (p == null) { R.Info($"Alter Mimic semibot: nobody within {Settings.BotChaseRange.Value:0} m; runs away."); SetState(State.Flee); break; }
                    Aim(dt, p);
                    if (Time.time >= stateUntil) SetState(State.Shoot);
                    break;
                }
                case State.Shoot: Shoot(dt); break;
                case State.Flee: Flee(dt); break;
            }
            if (R.Multiplayer && Time.time >= nextNet)
            {
                nextNet = Time.time + 0.1f;
                int flags = (Rig.Moving ? 1 : 0) | (Rig.Sprinting ? 2 : 0);
                Owner.SendOthers("MimicBotStateRPC", Rig.Feet, Rig.LookYaw, Rig.LookPitch, flags);
            }
        }

        // ---------------- hurt and death ----------------

        /// <summary>Host: a hit from MimicHitWatch (or another mod): the game's own enemy damage on the mimic's health.</summary>
        internal void TakeHit(int damage, Vector3 dir, string what)
        {
            if (!host || Owner == null || Owner.Health == null || Current == State.Dead || Current == State.Gone) return;
            if (R.EnemyDead(Owner.Health)) return;
            lastHurtDir = dir;
            R.Info($"Alter Mimic semibot: hit by {what} for {damage}.");
            try { Owner.Health.Hurt(damage, dir); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic semibot: hurting it failed: {e}"); }
        }

        /// <summary>Host: its health dropped (any route): before the gun phase it is knocked into tumble form.</summary>
        private void HurtCheck()
        {
            if (Owner == null || Owner.Health == null) return;
            int hp = (int)R.GetHealth(Owner.Health);
            if (lastHp < 0) lastHp = hp;
            if (hp >= lastHp) { lastHp = hp; return; }
            lastHp = hp;
            R.Info($"Alter Mimic semibot: hurt ({hp} HP left) while {Current}.");
            if (Current > State.Roam) return;   // the gun phase (and tumbling) carry on
            Vector3 dir = lastHurtDir; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
            {
                PlayerAvatar p = AlterMimic.NearestPlayer(Rig.Feet, 100f);
                dir = p != null ? Rig.Feet - p.transform.position : -(Quaternion.Euler(0f, Rig.Yaw, 0f) * Vector3.forward);
                dir.y = 0f;
            }
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            Vector3 vel = dir * 5f + Vector3.up * 2.5f;
            Vector3 spin = Vector3.Cross(Vector3.up, dir) * Random.Range(6f, 10f) + Random.insideUnitSphere * 2f;
            Release();
            StopMoving();
            TumbleLocal(vel, spin);
            Owner.SendOthers("MimicBotTumbleRPC", vel, spin);
            lastHurtDir = Vector3.zero;
            SetState(State.Tumble);
        }

        private void TumbleTick()
        {
            float age = Time.time - stateStart;
            float speed = Rig.TumbleBody != null ? Rig.TumbleBody.linearVelocity.magnitude : 0f;
            if (Rig.Curling || !((age > 2.5f && speed < 0.5f) || age > 6f)) return;
            Rig.EndTumble();
            Owner.SendOthers("MimicBotStandRPC", Rig.Feet, Rig.Yaw);
            R.Info($"Alter Mimic semibot: stands up after {age:0.#} s.");
            SetState(State.GunIdle);
        }

        /// <summary>Host: the mimic died (0 HP): it collapses into tumble form until the game removes it.</summary>
        internal void HostDied()
        {
            if (Current == State.Dead) return;
            Release();
            if (gunOut) { ShowGun(false); Owner.SendOthers("MimicBotGunRPC", false); }
            Vector3 vel = Vector3.up * 1.5f + Random.insideUnitSphere * 0.5f;
            Vector3 spin = Random.insideUnitSphere * 6f;
            TumbleLocal(vel, spin);
            Owner.SendOthers("MimicBotTumbleRPC", vel, spin);
            SetState(State.Dead);
        }

        // ---------------- approach ----------------

        private float nextPath;

        private void Approach(float dt)
        {
            if (!R.Alive(greet))
            {
                PlayerAvatar other = AlterMimic.NearestPlayer(Rig.Feet, 500f);
                if (other == null) { R.Info("Alter Mimic semibot: nobody to walk up to."); SetState(State.Greet); return; }
                greet = other;
                R.Info($"Alter Mimic semibot: walks up to {R.Name(greet)} instead.");
            }
            Vector3 them = greet.transform.position;
            Vector3 flat = them - Rig.Feet; float dy = Mathf.Abs(flat.y); flat.y = 0f;
            if (flat.magnitude <= Settings.BotGreetDistance.Value && dy < 1.6f && AlterMimic.Clear(EyePos, R.Eyes(greet)))
            {
                R.Info($"Alter Mimic semibot: reached {R.Name(greet)} ({flat.magnitude:0.##} m) after {Time.time - stateStart:0.#} s.");
                SetState(State.Greet);
                return;
            }
            if (Time.time - stateStart > 60f) { R.Info("Alter Mimic semibot: couldn't reach them in 60 s; stops where it is."); SetState(State.Greet); return; }
            if (Time.time >= nextPath)
            {
                nextPath = Time.time + 0.5f;
                if (!PathTo(Ground(them), 9999f, true)) StopMoving();
            }
            if (path.Count == 0) { Watch(greet); return; }
            int r = Follow(dt, walkSpeed, false, 0.05f);
            if (r != 0) { StopMoving(); Watch(greet); nextPath = Mathf.Min(nextPath, Time.time + 0.2f); }
        }

        // ---------------- roaming ----------------

        private Step step;
        private float roamStart, stepUntil, carryUntil;
        private Vector3 roamAnchor;
        private PhysGrabObject goalValuable;
        private bool moveSprint;

        private void Roam(float dt)
        {
            CarryCheck();
            if (Time.time - roamStart >= Settings.BotRoamSeconds.Value)
            {
                R.Info($"Alter Mimic semibot: {Settings.BotRoamSeconds.Value:0} s of roaming are up.");
                SetState(State.GunIdle);
                return;
            }
            switch (step)
            {
                case Step.Pick:
                {
                    goalValuable = null;
                    moveSprint = false;
                    bool picked = false;
                    if (Rig.Held == null && Random.value < 0.5f)
                    {
                        PhysGrabObject v = SeenValuable(8f);
                        if (v != null && PathTo(NearValuable(v), Settings.BotRoamRadius.Value * 3f, false))
                        {
                            goalValuable = v;
                            picked = true;
                            R.Info($"Alter Mimic semibot: heads for {v.name} ({Vector3.Distance(Rig.Feet, v.transform.position):0.#} m).");
                        }
                    }
                    if (!picked) picked = RandomSpot();
                    if (!picked) { step = Step.Pause; stepUntil = Time.time + 1f; break; }
                    moveSprint = PathLength() > 4f && Random.value < Settings.BotSprintChance.Value;
                    step = Step.Move;
                    stepUntil = Time.time + PathLength() / (moveSprint ? sprintSpeed : walkSpeed) * 2f + 3f;   // safety net
                    break;
                }
                case Step.Move:
                {
                    int r = Follow(dt, moveSprint ? sprintSpeed : walkSpeed, moveSprint, goalValuable != null ? 0.6f : 0.1f);
                    if (r == 0 && Time.time < stepUntil) break;
                    StopMoving();
                    if (Rig.Held != null) { step = Step.Pause; stepUntil = Time.time + Random.Range(1f, 2.5f); nextFlick = 0f; break; }
                    step = Step.Search;
                    stepUntil = Time.time + Range(Settings.BotSearchMin, Settings.BotSearchMax);
                    nextFlick = 0f; nextLookCheck = 0f;
                    homeYaw = Rig.Yaw;
                    if (goalValuable != null)   // first glance: straight at what it came for
                    {
                        StartFlick(YawTo(goalValuable.transform.position), Pitch(EyePos, goalValuable.transform.position));
                        nextFlick = Time.time + 0.8f;
                    }
                    break;
                }
                case Step.Search:
                {
                    Pan(110f, -5f, 40f, 0.7f, 1.5f);
                    if (Time.time >= nextLookCheck)
                    {
                        nextLookCheck = Time.time + 0.2f;
                        PhysGrabObject v = Rig.FindGrabbable();
                        if (v != null && FreeToTake(v) && InView(v.transform.position))
                        {
                            Take(v);
                            step = Step.Pause;
                            stepUntil = Time.time + 0.8f;
                            break;
                        }
                    }
                    if (Time.time >= stepUntil) step = Step.Pick;
                    break;
                }
                case Step.Pause:
                    if (Rig.Held != null)
                    {
                        // eyes on what it carries, slowly
                        float age = Time.time - stateStart;
                        Rig.LookYaw = Mathf.MoveTowardsAngle(Rig.LookYaw, homeYaw + Mathf.Sin(age * 1.3f) * 25f, 90f * dt);
                        Rig.LookPitch = Mathf.MoveTowards(Rig.LookPitch, 8f + Mathf.Sin(age * 0.9f) * 10f, 40f * dt);
                    }
                    else Pan(70f, -10f, 30f, 0.8f, 1.6f);
                    if (Time.time >= stepUntil) step = Step.Pick;
                    break;
            }
        }

        private float nextLookCheck;

        private void Take(PhysGrabObject v)
        {
            Rig.Yaw = Rig.LookYaw = YawTo(v.transform.position);
            homeYaw = Rig.Yaw;
            Rig.LookPitch = Pitch(EyePos, v.transform.position);
            if (!Rig.Grab(v, 1.6f)) return;
            if (hitbox != null && hitbox.Capsule != null)
                foreach (Collider c in v.GetComponentsInChildren<Collider>()) if (c != null) Physics.IgnoreCollision(c, hitbox.Capsule, true);
            carryUntil = Time.time + Range(Settings.BotCarryMin, Settings.BotCarryMax);
            Photon.Pun.PhotonView pv = v.GetComponent<Photon.Pun.PhotonView>();
            if (pv != null) Owner.SendOthers("MimicBotGrabRPC", pv.ViewID);
            R.Info($"Alter Mimic semibot: grabs {v.name}; carries it for {carryUntil - Time.time:0.#} s.");
        }

        /// <summary>Drops what it carries when the time is up, or when a player takes it.</summary>
        private void CarryCheck()
        {
            PhysGrabObject h = Rig.Held;
            if (h == null) return;
            bool taken = h.playerGrabbing != null && h.playerGrabbing.Count > 0;
            if (Time.time < carryUntil && !taken) return;
            R.Info($"Alter Mimic semibot: {(taken ? "a player took" : "drops")} {h.name}.");
            Release();
        }

        private void Release()
        {
            if (Rig.Held == null) return;
            Rig.Grab(null, 0f);
            Owner.SendOthers("MimicBotGrabRPC", -1);
        }

        private static bool FreeToTake(PhysGrabObject v) => v != null && (v.playerGrabbing == null || v.playerGrabbing.Count == 0);

        /// <summary>A valuable within range, not held by a player, that its eyes can see.</summary>
        private PhysGrabObject SeenValuable(float range)
        {
            PhysGrabObject best = null;
            float bestD = range;
            Vector3 eye = EyePos;
            foreach (ValuableObject v in Object.FindObjectsByType<ValuableObject>(FindObjectsSortMode.None))
            {
                if (v == null || !v.gameObject.activeInHierarchy) continue;
                PhysGrabObject pgo = v.GetComponent<PhysGrabObject>();
                if (!FreeToTake(pgo)) continue;
                float d = Vector3.Distance(Rig.Feet, v.transform.position);
                if (d > bestD || d < 0.3f) continue;
                if (!Sees(eye, v.transform.position, v.transform)) continue;
                bestD = d; best = pgo;
            }
            return best;
        }

        /// <summary>A floor point next to a valuable (it walks there, then searches).</summary>
        private static Vector3 NearValuable(PhysGrabObject v) => Ground(v.transform.position);

        /// <summary>A random reachable spot on the same floor within RoamRadius of where it started roaming.</summary>
        private bool RandomSpot()
        {
            float radius = Settings.BotRoamRadius.Value;
            for (int i = 0; i < 12; i++)
            {
                Vector2 c = Random.insideUnitCircle * radius;
                Vector3 p = roamAnchor + new Vector3(c.x, 0f, c.y);
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - roamAnchor.y) > 1.5f) continue;
                if (Vector3.Distance(hit.position, Rig.Feet) < 1.5f) continue;
                if (PathTo(hit.position, radius * 2.5f, false)) return true;
            }
            return false;
        }

        // ---------------- the gun ----------------

        private int shotsLeft;
        private float nextShot;
        private PlayerAvatar chaseTarget;

        private void Aim(float dt, PlayerAvatar p)
        {
            if (p == null) return;
            Vector3 chest = R.Eyes(p) + Vector3.down * 0.35f;
            Rig.Yaw = Rig.LookYaw = Mathf.MoveTowardsAngle(Rig.LookYaw, YawTo(chest), 300f * dt);
            Rig.LookPitch = Mathf.MoveTowards(Rig.LookPitch, Pitch(EyePos, chest), 200f * dt);
        }

        private void Shoot(float dt)
        {
            if (shotsLeft <= 0)
            {
                if (Time.time >= nextShot) SetState(State.Flee);
                return;
            }
            // always goes after the nearest player within ChaseRange (no sight needed), until ChaseGap away; nobody
            // within range: it runs away and despawns
            PlayerAvatar p = AlterMimic.NearestPlayer(Rig.Feet, Settings.BotChaseRange.Value);
            if (p == null) { R.Info($"Alter Mimic semibot: nobody within {Settings.BotChaseRange.Value:0} m; runs away."); SetState(State.Flee); return; }
            if (p != chaseTarget) { chaseTarget = p; nextPath = 0f; R.Info($"Alter Mimic semibot: goes after {R.Name(p)}."); }
            Vector3 gap = p.transform.position - Rig.Feet; gap.y = 0f;
            if (gap.magnitude > Settings.BotChaseGap.Value)
            {
                if (Time.time >= nextPath) { nextPath = Time.time + 0.4f; if (!PathTo(Ground(p.transform.position), 9999f, true)) path.Clear(); }
                bool seen = AlterMimic.CanSee(EyePos, p, 40f);
                // in sight: walks while aiming at them (no turning to the path); out of sight: walks facing the way
                if (path.Count >= 2) { if (Follow(dt, walkSpeed, false, 0.05f, !seen) != 0) { StopMoving(); nextPath = 0f; } }
                else StopMoving();
            }
            else StopMoving();
            if (!AlterMimic.CanSee(EyePos, p, 40f)) return;
            Aim(dt, p);
            Vector3 chest = R.Eyes(p) + Vector3.down * 0.35f;
            float off = Vector3.Angle(LookDir, chest - EyePos);
            if (Time.time < nextShot || off > 8f || gun == null) return;

            Vector3 from = gun.Muzzle;
            Vector3 aimDir = (chest - from).normalized;
            float s = Settings.BotSpreadDegrees.Value;
            Vector3 right = Vector3.Cross(Vector3.up, aimDir);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            aimDir = Quaternion.AngleAxis(Random.Range(-s, s), Vector3.up) * Quaternion.AngleAxis(Random.Range(-s, s), right.normalized) * aimDir;
            int damage = Settings.BotShotDamage.Value > 0 ? Settings.BotShotDamage.Value : MimicGun.PelletPlayerDamage;
            int pellets = MimicGun.Pellets;
            var hitNames = new List<string>();
            for (int i = 0; i < pellets; i++)
            {
                // each pellet: the shotgun's own random spread inside a cone around the aim (as ItemGun does)
                Vector3 dir = aimDir;
                float cone = MimicGun.Spread;
                if (cone > 0f)
                {
                    Vector3 side = Quaternion.AngleAxis(Random.Range(0f, 360f), aimDir) * right.normalized;
                    dir = Quaternion.AngleAxis(Random.Range(0f, cone * 0.5f), side) * aimDir;
                }
                string what = Pellet(from, dir, damage, i == 0);
                hitNames.Add(what);
            }
            shotsLeft--;
            nextShot = Time.time + (shotsLeft > 0 ? Range(Settings.BotShotGapMin, Settings.BotShotGapMax) : 0.8f);
            R.Info($"Alter Mimic semibot: shoots at {R.Name(p)} ({pellets} pellets, {damage} each): {string.Join(", ", hitNames)} ({shotsLeft} left).");
        }

        /// <summary>Host: one pellet from the muzzle along dir (raycast like ItemGun), shown on every game.</summary>
        private string Pellet(Vector3 from, Vector3 dir, int damage, bool effects)
        {
            float range = MimicGun.Range;
            Vector3 to = from + dir * range;
            bool hit = false, surface = false;
            string what = "nothing";
            int mask = SemiFunc.LayerMaskGetVisionObstruct() | LayerMask.GetMask("Enemy");
            RaycastHit[] hits = Physics.RaycastAll(from, dir, range, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Collider ball = Rig.TumbleCollider;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.GetComponentInParent<MimicBotHitbox>() != null || h.collider == ball) continue;
                if (Rig.Held != null && h.collider.transform.IsChildOf(Rig.Held.transform)) continue;
                to = h.point;
                hit = true;
                PlayerAvatar who = h.collider.GetComponentInParent<PlayerAvatar>();
                bool isEnemy = h.collider.GetComponentInParent<EnemyRigidbody>() != null;
                surface = who == null && h.collider.GetComponentInParent<PlayerController>() == null && !isEnemy;
                what = who != null ? R.Name(who) : isEnemy ? "an enemy" : h.collider.name;
                break;
            }
            Fire(to, hit, surface, damage, effects);
            Owner.SendOthers("MimicBotShotRPC", to, hit, surface, damage, effects);
            return what;
        }

        // ---------------- running away ----------------

        private float unseenSince, nextFleePick;
        private int fleeTries;

        private void PickFleeSpot()
        {
            fleeTries++;
            List<LevelPoint> points = SemiFunc.LevelPointsGetAll();
            Vector3 bestSpot = Vector3.zero;
            float bestScore = -1f;
            if (points != null && points.Count > 0)
            {
                for (int i = 0; i < 24; i++)
                {
                    LevelPoint lp = points[Random.Range(0, points.Count)];
                    if (lp == null) continue;
                    Vector3 p = lp.transform.position;
                    float d = Vector3.Distance(p, Rig.Feet);
                    if (d < 8f || d > 40f) continue;
                    float score = float.MaxValue;
                    foreach (PlayerAvatar pl in AlterMimic.Living()) score = Mathf.Min(score, Vector3.Distance(pl.transform.position, p));
                    if (score > bestScore && PathTo(Ground(p), 80f, false)) { bestScore = score; bestSpot = Ground(p); }
                }
            }
            if (bestScore < 0f || !PathTo(bestSpot, 80f, false))
            {
                roamAnchor = Rig.Feet;
                if (!RandomSpot()) { path.Clear(); R.Info("Alter Mimic semibot: nowhere to run."); return; }
            }
            R.Info($"Alter Mimic semibot: runs {PathLength():0} m away (try {fleeTries}).");
        }

        private void Flee(float dt)
        {
            float age = Time.time - stateStart;
            bool seen = AnyoneSees();
            if (seen) unseenSince = -1f;
            else if (unseenSince < 0f) unseenSince = Time.time;
            float unseen = unseenSince < 0f ? 0f : Time.time - unseenSince;
            if ((age > 1.5f && unseen >= 1.5f) || age > 25f)
            {
                R.Info($"Alter Mimic semibot: {(age > 25f ? "25 s up" : "out of everyone's sight")}; vanishes.");
                SetState(State.Gone);
                if (Owner != null) Owner.BotVanished();
                return;
            }
            int r = path.Count > 0 ? Follow(dt, sprintSpeed, true, 0.3f) : 1;
            if (r != 0)
            {
                StopMoving();
                if (unseen >= 0.5f) { SetState(State.Gone); if (Owner != null) Owner.BotVanished(); return; }
                if (Time.time >= nextFleePick) { nextFleePick = Time.time + 1f; PickFleeSpot(); }
            }
        }

        /// <summary>Can any living player see it (clear line from their eyes to its chest, within 40 m)?</summary>
        private bool AnyoneSees()
        {
            Vector3 c = Chest;
            foreach (PlayerAvatar p in AlterMimic.Living())
            {
                Vector3 e = R.Eyes(p);
                if (Vector3.Distance(e, c) <= 40f && AlterMimic.Clear(e, c)) return true;
            }
            return false;
        }

        // ---------------- moving ----------------

        private readonly List<Vector3> path = new List<Vector3>();
        private int corner;
        private NavMeshPath navPath;
        private Vector3 progressAt;
        private float progressCheck;

        /// <summary>A nav-mesh path from its feet to dest (no longer than maxLength); partial paths only if allowed.</summary>
        private bool PathTo(Vector3 dest, float maxLength, bool allowPartial)
        {
            if (navPath == null) navPath = new NavMeshPath();
            if (!NavMesh.CalculatePath(Ground(Rig.Feet), dest, NavMesh.AllAreas, navPath)) return false;
            if (navPath.status == NavMeshPathStatus.PathInvalid) return false;
            if (navPath.status == NavMeshPathStatus.PathPartial && !allowPartial) return false;
            Vector3[] c = navPath.corners;
            if (c == null || c.Length < 2) return false;
            float len = 0f;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            if (len > maxLength) return false;
            path.Clear();
            path.AddRange(c);
            corner = 1;
            progressAt = Rig.Feet;
            progressCheck = Time.time + 1.5f;
            return true;
        }

        private float PathLength()
        {
            float len = 0f;
            for (int i = Mathf.Max(1, corner); i < path.Count; i++) len += Vector3.Distance(i == corner ? Rig.Feet : path[i - 1], path[i]);
            return len;
        }

        /// <summary>One frame along the path: 0 = moving, 1 = arrived (within stopShort of the end), 2 = stuck or no path.</summary>
        private int Follow(float dt, float speed, bool sprint, float stopShort, bool face = true)
        {
            if (path.Count < 2 || corner >= path.Count) { StopMoving(); return path.Count < 2 ? 2 : 1; }
            Vector3 end = path[path.Count - 1];
            if (Flat(end - Rig.Feet).magnitude <= stopShort && corner == path.Count - 1) { StopMoving(); return 1; }
            Vector3 to = Flat(path[corner] - Rig.Feet);
            if (to.magnitude < 0.12f)
            {
                corner++;
                if (corner >= path.Count) { StopMoving(); return 1; }
                to = Flat(path[corner] - Rig.Feet);
            }
            if (face)
            {
                float want = to.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(to).eulerAngles.y : Rig.Yaw;
                float off = Mathf.Abs(Mathf.DeltaAngle(Rig.Yaw, want));
                Rig.Yaw = Rig.LookYaw = Mathf.MoveTowardsAngle(Rig.Yaw, want, (sprint ? 540f : 360f) * dt);
                Rig.LookPitch = Mathf.MoveTowards(Rig.LookPitch, 5f, 30f * dt);
                if (off > 45f) { Rig.Moving = false; Rig.Velocity = Vector3.zero; return 0; }   // turns on the spot first
            }
            Vector3 stepV = to.normalized * Mathf.Min(speed * dt, to.magnitude);
            Rig.Feet = Ground(Rig.Feet + stepV + Vector3.up * (path[corner].y - Rig.Feet.y) * Mathf.Clamp01(stepV.magnitude / Mathf.Max(0.01f, to.magnitude)));
            Rig.Velocity = to.normalized * speed;
            Rig.Moving = true;
            Rig.Sprinting = sprint;
            if (Time.time >= progressCheck)
            {
                bool stuck = Vector3.Distance(progressAt, Rig.Feet) < 0.25f;
                progressAt = Rig.Feet;
                progressCheck = Time.time + 1.5f;
                if (stuck) { R.Info("Alter Mimic semibot: stuck; gives up this move."); StopMoving(); return 2; }
            }
            return 0;
        }

        private void StopMoving()
        {
            if (Rig == null) return;
            Rig.Moving = Rig.Sprinting = false;
            Rig.Velocity = Vector3.zero;
        }

        // ---------------- looking (minimum-jerk flicks, as in the approved animation test) ----------------

        private float homeYaw, watchThreshold, nextFlick;
        private float flickFromYaw, flickToYaw, flickFromPitch, flickToPitch, flickStart, flickDur;

        /// <summary>Looks around: a flick to a random direction (±yawRange around where it faced, pitch in range) every gapMin-gapMax s.</summary>
        private void Pan(float yawRange, float pitchMin, float pitchMax, float gapMin, float gapMax)
        {
            if (Time.time >= nextFlick)
            {
                nextFlick = Time.time + Random.Range(gapMin, gapMax);
                StartFlick(homeYaw + Random.Range(-yawRange, yawRange), Random.Range(pitchMin, pitchMax));
            }
            Flick();
        }

        /// <summary>Watches a player: holds still until they're 30-45° off its centre, then a quick shift puts them back in the middle.</summary>
        private void Watch(PlayerAvatar p)
        {
            bool flicking = Time.time - flickStart < flickDur;
            if (p != null && !flicking)
            {
                Vector3 their = R.Eyes(p);
                float off = Vector3.Angle(LookDir, their - EyePos);
                if (off > watchThreshold)
                {
                    StartFlick(YawTo(their), Pitch(EyePos, their));
                    watchThreshold = Random.Range(30f, 45f);
                    flicking = true;
                }
            }
            if (flicking) Flick();
        }

        private void StartFlick(float yaw, float pitch)
        {
            flickFromYaw = Rig.LookYaw; flickToYaw = Rig.LookYaw + Mathf.DeltaAngle(Rig.LookYaw, yaw);
            flickFromPitch = Rig.LookPitch; flickToPitch = Mathf.Clamp(pitch, -60f, 60f);
            flickDur = Mathf.Clamp(0.12f + Mathf.Abs(flickToYaw - flickFromYaw) / 450f, 0.15f, 0.5f);
            flickStart = Time.time;
        }

        private void Flick()
        {
            float t = Mathf.Clamp01((Time.time - flickStart) / Mathf.Max(0.01f, flickDur));
            float s = t * t * t * (10f + t * (-15f + 6f * t));   // minimum jerk: fast in the middle, a dead stop
            Rig.LookYaw = Mathf.Lerp(flickFromYaw, flickToYaw, s);
            Rig.LookPitch = Mathf.Lerp(flickFromPitch, flickToPitch, s);
        }

        private bool InView(Vector3 at)
        {
            Vector3 to = at - EyePos;
            return to.sqrMagnitude < 0.01f || Vector3.Angle(LookDir, to) <= 60f;
        }

        /// <summary>Is the line from its eyes to a point clear (the target object itself, players and its own hitbox don't count)?</summary>
        private bool Sees(Vector3 eye, Vector3 at, Transform target)
        {
            Vector3 d = at - eye;
            float len = d.magnitude;
            if (len < 0.05f) return true;
            foreach (RaycastHit h in Physics.RaycastAll(eye, d / len, len - 0.02f, SemiFunc.LayerMaskGetVisionObstruct(), QueryTriggerInteraction.Ignore))
            {
                Collider c = h.collider;
                if (c == null) continue;
                if (target != null && c.transform.IsChildOf(target)) continue;
                if (c.GetComponentInParent<PlayerAvatar>() != null || c.GetComponentInParent<MimicBotHitbox>() != null) continue;
                return false;
            }
            return true;
        }

        // ---------------- helpers ----------------

        private float YawTo(Vector3 at) => Quaternion.LookRotation(Flat(at - Rig.Feet)).eulerAngles.y;

        private static float Range(Fixed<float> min, Fixed<float> max) =>
            Random.Range(min.Value, Mathf.Max(min.Value, max.Value));

        private void Say(string raw)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(raw)) foreach (string l in raw.Split('|')) { string t = l.Trim(); if (t.Length > 0) lines.Add(t); }
            if (lines.Count == 0) return;
            string line = lines[Random.Range(0, lines.Count)];
            R.Info($"Alter Mimic semibot: says \"{line}\".");
            Owner.SayEverywhere(line);
        }

        internal static Vector3 Ground(Vector3 p) =>
            NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) ? hit.position : p;

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v : Vector3.forward * 0.001f; }

        private static float Pitch(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return -Mathf.Atan2(d.y, Mathf.Max(0.01f, new Vector2(d.x, d.z).magnitude)) * Mathf.Rad2Deg;
        }
    }
}
