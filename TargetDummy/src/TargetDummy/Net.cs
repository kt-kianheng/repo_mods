using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace TargetDummy
{
    /// <summary>
    /// The host sends each hit, heal and Ally Dummy health update to every player as a Photon event (no PhotonView
    /// needed, since every player builds the dummies themselves in the same spots). Offline, they're shown straight away.
    /// </summary>
    internal static class Net
    {
        private const byte EventCode = 151;
        private const byte RequestCode = 152;   // a non-host game asking the host to heal or hurt the Ally Dummy
        private const string Tag = "TDMY2";

        internal static void Init()
        {
            try { PhotonNetwork.NetworkingClient.EventReceived += OnEvent; }
            catch (System.Exception e) { Plugin.Log.LogError($"Couldn't listen for dummy hits; multiplayer numbers won't show: {e}"); }
        }

        internal static void SendHit(HitInfo h)
        {
            if (!SemiFunc.IsMultiplayer())
            {
                Show(h);
                return;
            }
            float[] d =
            {
                (float)h.Type, h.Point.x, h.Point.y, h.Point.z, h.Direction.x, h.Direction.y, h.Direction.z,
                h.Damage, h.Kill ? 1f : 0f, h.Knockback, h.Torque, h.Stun, h.Freeze, h.ImpactForce, h.Speed,
                (float)h.Dummy, h.Health, h.MaxHealth, h.Effective, h.WouldDie ? 1f : 0f,
            };
            Show(h);   // the host shows it straight away; everyone else gets the event
            try
            {
                PhotonNetwork.RaiseEvent(EventCode, new object[] { Tag, d },
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Sending a dummy hit failed: {e.Message}"); }
        }

        /// <summary>Non-host: a mod healed or hurt the Ally Dummy's pseudo-player here; the host owns its health.</summary>
        internal static void Request(HitInfo.Kind type, int amount, Vector3 direction)
        {
            try
            {
                PhotonNetwork.RaiseEvent(RequestCode, new object[] { Tag, new float[] { (float)type, amount, direction.x, direction.y, direction.z } },
                    new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, SendOptions.SendReliable);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Asking the host to update the Ally Dummy failed: {e.Message}"); }
        }

        private static void OnRequest(EventData e)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer() || Dummy.Ally == null) return;
            if (!(e.CustomData is object[] o) || o.Length < 2 || !(o[0] is string tag) || tag != Tag || !(o[1] is float[] d) || d.Length < 5) return;
            int amount = Mathf.Clamp(Mathf.RoundToInt(d[1]), 0, 100000);
            if (Settings.DebugLog.Value) Plugin.Log.LogInfo($"Ally Dummy: player {e.Sender} asks for {(HitInfo.Kind)(byte)d[0]} {amount}.");
            if ((HitInfo.Kind)(byte)d[0] == HitInfo.Kind.Heal) Dummy.Ally.HostHeal(amount, Dummy.Ally.Chest.position + Dummy.Ally.transform.forward);
            else Dummy.Ally.TargetDummyHit(new float[] { amount, d[2], d[3], d[4] });
        }

        private static void OnEvent(EventData e)
        {
            if (e.Code == RequestCode) { OnRequest(e); return; }
            if (e.Code != EventCode) return;
            if (!(e.CustomData is object[] o) || o.Length < 2 || !(o[0] is string tag) || tag != Tag || !(o[1] is float[] d) || d.Length < 20) return;
            var h = new HitInfo
            {
                Type = (HitInfo.Kind)(byte)d[0],
                Point = new Vector3(d[1], d[2], d[3]),
                Direction = new Vector3(d[4], d[5], d[6]),
                Damage = Mathf.RoundToInt(d[7]),
                Kill = d[8] > 0.5f,
                Knockback = d[9],
                Torque = d[10],
                Stun = d[11],
                Freeze = d[12],
                ImpactForce = d[13],
                Speed = d[14],
                Dummy = (DummyKind)(byte)d[15],
                Health = Mathf.RoundToInt(d[16]),
                MaxHealth = Mathf.RoundToInt(d[17]),
                Effective = Mathf.RoundToInt(d[18]),
                WouldDie = d[19] > 0.5f,
            };
            if (Settings.DebugLog.Value && h.Type != HitInfo.Kind.State) Plugin.Log.LogInfo($"Received a {h.Dummy} Dummy {h.Type} from the host.");
            Show(h);
        }

        private static void Show(HitInfo h)
        {
            try { Dummy.Get(h.Dummy)?.Show(h); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Showing a dummy hit failed: {e}"); }
        }
    }
}
