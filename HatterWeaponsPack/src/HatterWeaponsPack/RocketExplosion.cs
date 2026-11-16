using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Rocket blast, spawned on every client at the same spot.
    ///  - Look and sound: the game's own explosion effect (visual only).
    ///  - Damage and push: two copies of the pulse grenade's hurt sphere.
    ///      outer: gravity-orb radius (4 m), 150 enemy damage, push equal to the pulse grenade
    ///      inner: half that radius (2 m), another 150 damage  -> 300 up close
    ///  Players (teammates and the shooter) take the same splash damage as enemies (the user's friendly-fire rule,
    ///  13 Nov 2026; rocket-jumping now costs health) and are pushed by the outer sphere.
    /// </summary>
    internal static class RocketExplosion
    {
        // ---- Tuning ----
        // (HatterWeaponsPack.cfg, see Settings)
        internal static float SplashRadius => Settings.RocketSplashRadius.Value;   // default 4 = the zero-gravity orb
        internal static float CloseRadius => SplashRadius / 2f;
        internal static int EdgeDamage => Settings.RocketSplashEdgeDamage.Value;
        internal static int CloseDamage => Settings.RocketSplashCloseDamage.Value; // inner sphere adds CloseDamage - EdgeDamage
        internal static float Knockback => Settings.RocketSplashKnockback.Value;    // default 60 = the pulse grenade

        private static ParticleScriptExplosion effects;
        private static GameObject hurtTemplate;

        internal static void Init()
        {
            // Explosion visuals: a ParticleScriptExplosion of our own, using the explosive grenade's preset.
            var host = new GameObject("HatterWeaponsPack.Explosions");
            Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            GameObject grenade = Resources.Load<GameObject>("Items/Item Grenade Explosive");
            ParticleScriptExplosion source = grenade != null ? grenade.GetComponentInChildren<ParticleScriptExplosion>(true) : null;
            effects = host.AddComponent<ParticleScriptExplosion>();
            if (source != null) effects.explosionPreset = source.explosionPreset;
            else Plugin.Log.LogWarning("Explosive grenade not found; rockets will explode without the usual effect.");

            // Damage/push: template copied from the pulse (shockwave) grenade's hurt sphere.
            GameObject pulse = Resources.Load<GameObject>("Items/Item Grenade Shockwave");
            ItemGrenadeShockwave sw = pulse != null ? pulse.GetComponent<ItemGrenadeShockwave>() : null;
            HurtCollider pulseHurt = sw != null && sw.shockwavePrefab != null ? sw.shockwavePrefab.GetComponentInChildren<HurtCollider>(true) : null;
            if (pulseHurt == null) { Plugin.Log.LogError("Pulse grenade hurt collider not found; rockets will do no splash damage."); return; }

            var holder = new GameObject("HatterWeaponsPack.HurtTemplate");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            holder.hideFlags = HideFlags.HideAndDontSave;
            hurtTemplate = Object.Instantiate(pulseHurt.gameObject, holder.transform);
            hurtTemplate.name = "HWP Rocket Blast";
            hurtTemplate.transform.localScale = Vector3.one;
            hurtTemplate.SetActive(false);   // copies are configured first, then switched on
        }

        /// <summary>
        /// The Rocket Launcher's blast damage and push only, with no vanilla effect and its explosion sound (the Orb
        /// Cannon's large orb draws its own pink plasma blast).
        /// </summary>
        internal static void SpawnBlastOnly(Vector3 position)
        {
            try
            {
                ExplosionPreset preset = effects != null ? effects.explosionPreset : null;
                if (preset != null)
                {
                    preset.explosionSoundBig?.Play(position, 1f, 1f, 1f, 1f);
                    preset.explosionSoundBigGlobal?.Play(position, 1f, 1f, 1f, 1f);
                }
                if (GameDirector.instance != null)
                {
                    GameDirector.instance.CameraShake?.ShakeDistance(6f, 3f, 12f, position, 0.4f);
                    GameDirector.instance.CameraImpact?.ShakeDistance(8f, 3f, 12f, position, 0.15f);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Blast sound/shake failed: {e}"); }

            if (hurtTemplate == null) { Plugin.Log.LogWarning("Blast template missing; no splash damage."); return; }
            try
            {
                Blast(position, SplashRadius, EdgeDamage, EdgeDamage, Knockback, push: true);
                int closeExtra = CloseDamage - EdgeDamage;
                if (closeExtra > 0) Blast(position, CloseRadius, closeExtra, closeExtra, 0f, push: false);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Blast failed: {e}"); }
        }

        internal static void Spawn(Vector3 position)
        {
            try
            {
                if (effects != null && effects.explosionPreset != null)
                    effects.Spawn(position, 1.4f, 0, 0, 0f, true, false, 1.2f, 12f);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Rocket explosion effect failed: {e}"); }

            if (hurtTemplate == null) { Plugin.Log.LogWarning("Rocket blast template missing; no splash damage."); return; }
            try
            {
                // the splash hurts players as it hurts enemies (friendly fire); only the outer sphere pushes
                Blast(position, SplashRadius, EdgeDamage, EdgeDamage, Knockback, push: true);
                int closeExtra = CloseDamage - EdgeDamage;
                if (closeExtra > 0) Blast(position, CloseRadius, closeExtra, closeExtra, 0f, push: false);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Rocket blast failed: {e}"); }
        }

        /// <summary>
        /// A smaller blast for other weapons (the Ember Wand): the game's explosion effect at effectSize, and one
        /// hurt sphere that damages enemies and players alike (the shooter included) and pushes with knockback.
        /// </summary>
        /// <param name="valuableBreak">0 = leaves valuables alone (the pulse grenade's way); 1-3 = the game's light, medium or heavy
        /// break on every valuable in the blast (value lost, as if knocked about).</param>
        internal static void SpawnSmall(Vector3 position, float effectSize, float radius, int damage, float knockback, int valuableBreak = 0)
        {
            try
            {
                if (effects != null && effects.explosionPreset != null)
                    effects.Spawn(position, effectSize, 0, 0, 0f, true, false, 0.4f, 6f);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Explosion effect failed: {e}"); }

            if (hurtTemplate == null) { Plugin.Log.LogWarning("Blast template missing; no splash damage."); return; }
            try { Blast(position, radius, damage, damage, knockback, push: true, valuableBreak); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Blast failed: {e}"); }
        }

        /// <summary>
        /// One small hurt sphere for enemies only (the game's own damage and knockback, as the pulse grenade's),
        /// with no effect. Used by the Orb Cannon's shockwave on each enemy it reaches.
        /// </summary>
        internal static void EnemyHit(Vector3 position, float radius, int damage, float force)
        {
            if (hurtTemplate == null) return;
            GameObject go = Object.Instantiate(hurtTemplate, position, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var sphere = go.GetComponent<SphereCollider>();
            if (sphere != null) { sphere.center = Vector3.zero; sphere.radius = radius; sphere.isTrigger = true; }
            HurtCollider h = go.GetComponent<HurtCollider>();
            h.enemyDamage = damage;
            h.enemyStun = false;
            h.enemyHitForce = force;
            h.enemyHitTorque = force * 0.5f;
            h.enemyLogic = true;
            h.playerLogic = false;
            h.playerDamage = 0;
            h.physLogic = false;
            h.hasTimer = true;
            h.timer = 0.15f;
            h.destroyOnTimerEnd = true;
            go.SetActive(true);
            Object.Destroy(go, 1f);
        }

        private static void Blast(Vector3 position, float radius, int enemyDamage, int playerDamage, float force, bool push, int valuableBreak = 0)
        {
            GameObject go = Object.Instantiate(hurtTemplate, position, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            var sphere = go.GetComponent<SphereCollider>();
            if (sphere != null) { sphere.center = Vector3.zero; sphere.radius = radius; sphere.isTrigger = true; }

            HurtCollider h = go.GetComponent<HurtCollider>();
            h.enemyDamage = enemyDamage;
            h.enemyStun = false;
            h.enemyHitForce = force;
            h.enemyHitTorque = force;
            h.enemyLogic = true;
            // players (allies and the shooter) are pushed when push is set, and hurt only when playerDamage > 0
            h.playerLogic = push || playerDamage > 0;
            h.playerKill = false;              // (true would be an instant kill)
            h.playerDamage = playerDamage;
            h.playerTumbleForce = force;
            h.playerTumbleTorque = force;
            h.playerHitForce = force;
            h.physLogic = push;
            h.physHitForce = force;
            h.physHitTorque = force;
            if (valuableBreak > 0)
            {
                h.physLogic = true;
                h.physImpact = valuableBreak >= 3 ? HurtCollider.BreakImpact.Heavy
                             : valuableBreak == 2 ? HurtCollider.BreakImpact.Medium : HurtCollider.BreakImpact.Light;
            }
            h.hasTimer = true;
            h.timer = 0.25f;
            h.destroyOnTimerEnd = true;
            go.SetActive(true);
            Object.Destroy(go, 1f);   // safety net
        }
    }
}
