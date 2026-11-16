using System.Collections.Generic;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// The Alter Mimic semibot's gun: a copy of the game's shotgun (Items/Item Gun Shotgun), meshes only, held on
    /// the semibot's grab beam. A shot is the game's own: its shoot sounds, muzzle flash and bullet prefab
    /// (ItemGunBullet: tracer line, hit sparks, and its HurtCollider, which on the host hurts the players and enemies
    /// it lands on, like a player's shotgun pellet). A shot is the shotgun's own number of pellets and spread. Every game
    /// shows the shot; only the host's bullets do damage.
    /// </summary>
    internal class MimicGun : MonoBehaviour
    {
        internal const string GunPath = "Items/Item Gun Shotgun";

        private static bool read;
        private static GameObject bulletPrefab, flashPrefab;
        private static Sound soundShoot, soundShootGlobal, soundHit;
        private static AnimationCurve lineCurve;
        private static int enemyDamage = 25, playerDamage = 25, pellets = 6;
        private static float spread = 10f, range = 50f;

        private Transform muzzle;
        private Quaternion muzzleToRoot = Quaternion.identity;   // root rotation = aim rotation * this
        private Vector3 muzzleLocal;                              // the muzzle's position in the root's space

        /// <summary>The shotgun's own pellet damage to players (used when ShotDamage is 0).</summary>
        internal static int PelletPlayerDamage { get { Read(); return playerDamage; } }
        /// <summary>The shotgun's pellets per shot, random spread (degrees) and range.</summary>
        internal static int Pellets { get { Read(); return pellets; } }
        internal static float Spread { get { Read(); return spread; } }
        internal static float Range { get { Read(); return range; } }

        private static void Read()
        {
            if (read) return;
            read = true;
            GameObject prefab = Resources.Load<GameObject>(GunPath);
            ItemGun gun = prefab != null ? prefab.GetComponent<ItemGun>() : null;
            if (gun == null) { Plugin.Log.LogWarning($"Alter Mimic: no shotgun at {GunPath}; the semibot can't shoot."); return; }
            pellets = Mathf.Max(1, gun.numberOfBullets);
            spread = gun.gunRandomSpread;
            range = gun.gunRange > 1f ? gun.gunRange : 50f;
            bulletPrefab = gun.bulletPrefab;
            flashPrefab = gun.muzzleFlashPrefab;
            soundShoot = gun.soundShoot; soundShootGlobal = gun.soundShootGlobal; soundHit = gun.soundHit;
            lineCurve = gun.shootLineWidthCurve;
            HurtCollider hc = bulletPrefab != null ? bulletPrefab.GetComponentInChildren<HurtCollider>(true) : null;
            if (hc != null && hc.enemyDamage > 0) enemyDamage = hc.enemyDamage;
            if (hc != null && hc.playerDamage > 0) playerDamage = hc.playerDamage;
            R.Info($"Alter Mimic: the shotgun: bullet {(bulletPrefab != null ? bulletPrefab.name : "NONE")}, {pellets} pellet(s), spread {spread:0.#}°, range {range:0} m, per pellet: enemy damage {enemyDamage}, player damage {playerDamage}; muzzle flash {(flashPrefab != null ? "yes" : "no")}.");
        }

        /// <summary>A copy of the shotgun's look (no scripts, colliders or physics), switched off until held.</summary>
        internal static MimicGun Create()
        {
            Read();
            GameObject prefab = Resources.Load<GameObject>(GunPath);
            ItemGun src = prefab != null ? prefab.GetComponent<ItemGun>() : null;
            if (src == null) return null;
            string muzzlePath = Path(prefab.transform, src.gunMuzzle);
            Vector3 mLocal = src.gunMuzzle != null ? prefab.transform.InverseTransformPoint(src.gunMuzzle.position) : Vector3.forward * 0.25f;
            Quaternion mRot = src.gunMuzzle != null ? Quaternion.Inverse(prefab.transform.rotation) * src.gunMuzzle.rotation : Quaternion.identity;

            var holder = new GameObject("Alter Mimic Gun (building)");
            holder.SetActive(false);
            GameObject copy = Instantiate(prefab, holder.transform);
            copy.name = "Alter Mimic Gun";
            // scripts first (some need the physics parts), then joints, colliders and the rigidbody
            for (int pass = 0; pass < 4; pass++)
                foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is MeshFilter || c is Renderer) continue;
                    if (pass < 2 && !(c is MonoBehaviour)) continue;
                    if (pass == 2 && !(c is Joint) && !(c is Collider)) continue;
                    try { DestroyImmediate(c); } catch { }
                }
            int defaultLayer = LayerMask.NameToLayer("Default");
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = defaultLayer;
            // only the gun's own visible meshes: renderers the prefab keeps switched off (grab outlines, helper
            // shapes), anything that isn't a plain mesh (particles, lines, trails) and anything far bigger than a
            // shotgun are removed; nothing is forced on
            var kept = new List<string>();
            var dropped = new List<string>();
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string why = null;
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) why = r.GetType().Name;
                else if (!r.enabled || !r.gameObject.activeSelf || !ActiveUnder(r.transform, copy.transform)) why = "off in the prefab";
                else
                {
                    Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    Vector3 size = mesh != null ? Vector3.Scale(mesh.bounds.size, Abs(r.transform.lossyScale)) : Vector3.zero;
                    if (mesh == null) why = "no mesh";
                    else if (size.magnitude > 2f) why = $"too big ({size.magnitude:0.#} m)";
                }
                if (why != null) { dropped.Add($"{r.name} [{why}]"); try { DestroyImmediate(r); } catch { r.enabled = false; } continue; }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                kept.Add(r.name);
            }
            R.Info($"Alter Mimic: the gun copy shows [{string.Join(", ", kept)}]; removed [{string.Join(", ", dropped)}].");
            copy.SetActive(false);
            copy.transform.SetParent(null, false);
            Destroy(holder);
            var g = copy.AddComponent<MimicGun>();
            g.muzzle = muzzlePath != null ? (muzzlePath.Length == 0 ? copy.transform : copy.transform.Find(muzzlePath)) : null;
            g.muzzleLocal = mLocal;
            g.muzzleToRoot = Quaternion.Inverse(mRot);
            return g;
        }

        private static bool ActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private static string Path(Transform root, Transform t)
        {
            if (t == null) return null;
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return t == root ? string.Join("/", parts) : null;
        }

        internal Vector3 Muzzle => muzzle != null ? muzzle.position : transform.TransformPoint(muzzleLocal);

        /// <summary>Shows it (or hides it).</summary>
        internal void Show(bool on) => gameObject.SetActive(on);

        /// <summary>Places it so its muzzle points along aim, its body at the hold point.</summary>
        internal void Place(Vector3 holdPoint, Vector3 aim)
        {
            if (aim.sqrMagnitude < 1e-6f) aim = transform.forward;
            Quaternion rot = Quaternion.LookRotation(aim) * muzzleToRoot;
            transform.SetPositionAndRotation(holdPoint, rot);
        }

        /// <summary>
        /// Every game: one shot from the muzzle to 'to' (sound, flash, the game's bullet). On the host the bullet's
        /// hurt zone does the damage (players: ShotDamage; enemies: the handgun's own damage).
        /// </summary>
        internal HurtCollider Fire(Vector3 to, bool hit, bool surfaceHit, int pelletDamage, bool effects)
        {
            HurtCollider hc = null;
            Vector3 from = Muzzle;
            Quaternion dir = Quaternion.LookRotation((to - from).sqrMagnitude > 1e-6f ? to - from : transform.forward);
            if (effects)
            try { soundShoot?.Play(from, 1f, 1f, 1f, 1f); soundShootGlobal?.Play(from, 1f, 1f, 1f, 1f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: gun sound failed: {e.Message}"); }
            try
            {
                if (effects && flashPrefab != null)
                {
                    GameObject f = Instantiate(flashPrefab, from, dir, muzzle != null ? muzzle : transform);
                    ItemGunMuzzleFlash mf = f.GetComponent<ItemGunMuzzleFlash>();
                    if (mf != null) mf.ActivateAllEffects();
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: muzzle flash failed: {e.Message}"); }
            try
            {
                if (bulletPrefab == null) return null;
                GameObject b = Instantiate(bulletPrefab, from, dir);
                ItemGunBullet bullet = b.GetComponent<ItemGunBullet>();
                if (bullet == null) return null;
                SemibotRig.Set(bullet, "hitPosition", to);
                SemibotRig.Set(bullet, "bulletHit", hit);
                SemibotRig.Set(bullet, "surfaceHit", surfaceHit);
                if (lineCurve != null) SemibotRig.Set(bullet, "shootLineWidthCurve", lineCurve);
                hc = b.GetComponentInChildren<HurtCollider>(true);
                if (hc != null)
                {
                    hc.playerDamage = pelletDamage;
                    SemibotRig.Set(hc, "playerCausingHurt", null);   // an enemy's shot
                }
                if (hit) soundHit?.Play(to, 1f, 1f, 1f, 1f);
                bullet.ActivateAll();
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Alter Mimic: the bullet failed: {e}"); }
            return hc;
        }
    }
}
