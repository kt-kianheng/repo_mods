using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace HatterMiscellaneousPack
{
    /// <summary>
    /// A pocket compass (consumable). Its needle is stuck; pressing [E] while holding it breaks it, and the player who
    /// pressed it gets an arrow on their screen, under the haul, pointing to the nearest enemy / unfinished extraction
    /// point (<see cref="CompassArrows"/>).
    ///  - The host decides, as the game's health pack does: the use button (ItemToggle) turns on, and the host either
    ///    rejects it (outside a level, or nothing to point to: it pops back off with the health pack's reject sound and a
    ///    little hop) or breaks it.
    ///  - Breaking: removed from the owned items, then everyone hears the crystal break and sees glass and brass bits
    ///    (HMPCompassUsedRPC), and the user's game starts the arrow. Then the item is destroyed.
    /// </summary>
    public class CompassItem : MonoBehaviour
    {
        // serialized, set by CompassItemBuilder
        public int kindValue;               // the kind as a serialized int (internal enum fields don't survive cloning)
        public Transform emblem;
        public Vector3 centre;
        public float size;
        public Sound rejectSound, breakSound;

        private ItemToggle toggle;
        private PhotonView photonView;
        private PhysGrabObject physGrabObject;
        private ItemAttributes attributes;
        private PhysGrabObjectImpactDetector impact;
        private bool used, emblemDone;
        private float rejectedUntil;

        private static readonly FieldInfo ToggleIdField = AccessTools.Field(typeof(ItemToggle), "playerTogglePhotonID");
        private static readonly FieldInfo InstanceNameField = AccessTools.Field(typeof(ItemAttributes), "instanceName");

        private CompassTargets.Kind Kind => (CompassTargets.Kind)kindValue;
        private string Label => Kind == CompassTargets.Kind.Enemy ? CompassItemBuilder.EnemyName : CompassItemBuilder.ExtractionName;

        private void Awake()
        {
            toggle = GetComponent<ItemToggle>();
            photonView = GetComponent<PhotonView>();
            physGrabObject = GetComponent<PhysGrabObject>();
            attributes = GetComponent<ItemAttributes>();
            impact = GetComponent<PhysGrabObjectImpactDetector>();
        }

        private void Start()
        {
            if (emblem == null || emblemDone) return;
            emblemDone = true;
            // the game's own Robe / the level's extraction screen in miniature; the built-in emblem stays if that fails
            try
            {
                bool ok = Kind == CompassTargets.Kind.Enemy
                    ? RobeMini.Spawn(emblem, CompassItemBuilder.EmblemHeight, Settings.CompassRobeYaw.Value) != null
                    : ExtractionMini.Spawn(emblem, CompassItemBuilder.EmblemHeight) != null;
                if (ok)
                {
                    Transform simple = emblem.Find(CompassItemBuilder.SimpleEmblemName);
                    if (simple != null) simple.gameObject.SetActive(false);
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"{Label}: the emblem copy failed: {e}"); }
        }

        private void Update()
        {
            if (used || toggle == null || !toggle.toggleState) return;
            if (!SemiFunc.IsMasterClientOrSingleplayer() || Time.time < rejectedUntil) return;
            int who = ToggleIdField != null ? (int)ToggleIdField.GetValue(toggle) : -1;
            PlayerAvatar user = SemiFunc.PlayerAvatarGetFromPhotonID(who);
            if (user == null && !SemiFunc.IsMultiplayer()) user = PlayerAvatar.instance;
            if (user == null) return;

            string why = null;
            if (!SemiFunc.RunIsLevel()) why = "not in a level";
            else if (!CompassTargets.Find(Kind, user.transform.position, out _)) why = $"no {CompassTargets.Describe(Kind)} to point to";
            if (why != null) { Reject(why); return; }

            used = true;
            toggle.ToggleDisable(true);
            try
            {
                string instance = InstanceNameField != null ? InstanceNameField.GetValue(attributes) as string : null;
                if (!string.IsNullOrEmpty(instance) && StatsManager.instance != null) StatsManager.instance.ItemRemove(instance);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"{Label}: removing it from the owned items failed: {e}"); }

            Vector3 at = transform.TransformPoint(centre);
            if (SemiFunc.IsMultiplayer()) photonView.RPC(nameof(HMPCompassUsedRPC), RpcTarget.All, who, at);
            else Broken(true, at);
            if (Settings.CompassDebugLog.Value) Plugin.Log.LogInfo($"{Label}: broken by {SafeName(user)}.");
            // DestroyObject only marks it dead and plays effects (for items nothing then removes it), so remove it too
            if (impact != null) impact.DestroyObject(false, true);
            if (physGrabObject != null) physGrabObject.DestroyPhysGrabObject();
        }

        /// <summary>Host: the use button pops back off, with a hop and the health pack's reject sound.</summary>
        private void Reject(string why)
        {
            rejectedUntil = Time.time + 0.5f;   // the toggle's switch-off may take a moment to come back
            toggle.ToggleItem(false, -1);
            if (physGrabObject != null && physGrabObject.rb != null)
            {
                physGrabObject.rb.AddForce(Vector3.up * 2f, ForceMode.Impulse);
                physGrabObject.rb.AddTorque(-physGrabObject.transform.right * 0.05f, ForceMode.Impulse);
            }
            if (SemiFunc.IsMultiplayer()) photonView.RPC(nameof(HMPCompassRejectRPC), RpcTarget.All);
            else Play(rejectSound, transform.position);
            if (Settings.CompassDebugLog.Value) Plugin.Log.LogInfo($"{Label}: not used ({why}).");
        }

        [PunRPC]
        public void HMPCompassRejectRPC(PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Play(rejectSound, transform.position);
        }

        [PunRPC]
        public void HMPCompassUsedRPC(int who, Vector3 at, PhotonMessageInfo info)
        {
            if (!SemiFunc.MasterOnlyRPC(info)) return;
            Broken(who == SemiFunc.PhotonViewIDPlayerAvatarLocal(), at);
        }

        /// <summary>Every game: the breaking sound and bits; the user's game starts the arrow.</summary>
        private void Broken(bool mine, Vector3 at)
        {
            used = true;
            // gone from sight at once on every game (the host removes the object itself)
            foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Play(breakSound, at);
            try { CompassShatter.Spawn(at, size); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"{Label}: the breaking effect failed: {e}"); }
            if (mine) CompassArrows.Begin(Kind);
        }

        private void Play(Sound s, Vector3 position)
        {
            if (s == null) return;
            try { s.Play(position, 1f, 1f, 1f, 1f); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"{Label}: sound failed: {e}"); }
        }

        private static string SafeName(PlayerAvatar p)
        {
            try { return SemiFunc.PlayerGetName(p); } catch { return p != null ? p.name : "?"; }
        }
    }

    /// <summary>Glass and brass bits flying out of a broken compass, with a short glint.</summary>
    internal static class CompassShatter
    {
        private static Material material;

        internal static void Spawn(Vector3 at, float size)
        {
            if (material == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) return;
                material = new Material(sh) { name = "HMP Compass Shards" };
            }
            float s = Mathf.Clamp(size / 0.23f, 0.4f, 3f);   // 1 at the default size (23 cm tall)
            var go = new GameObject("Compass Shatter");
            go.SetActive(false);
            go.transform.position = at;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f * s, 3f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f * s, 0.022f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.95f, 1f, 0.85f), new Color(0.95f, 0.75f, 0.35f, 1f));
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 45) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.04f * s;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;

            var glint = new GameObject("Glint");
            glint.transform.SetParent(go.transform, false);
            Light l = glint.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.85f, 0.95f, 1f);
            l.range = 1.2f * s;
            l.intensity = 2.5f;
            l.shadows = LightShadows.None;
            go.AddComponent<ShatterLife>().glint = l;

            go.SetActive(true);
            ps.Play();
            Object.Destroy(go, 2f);
        }

        private class ShatterLife : MonoBehaviour
        {
            public Light glint;
            private float t;
            private void Update()
            {
                t += Time.deltaTime;
                if (glint != null) glint.intensity = Mathf.Max(0f, 2.5f * (1f - t / 0.25f));
            }
        }
    }
}
