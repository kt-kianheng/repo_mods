using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using REPOLib.Objects.Sdk;
using UnityEngine;

namespace HatterEnemiesPack
{
    /// <summary>
    /// Builds the Alter Mimic at startup from the game's Hidden enemy (already invisible: spawning, despawning,
    /// pathing, rigidbody, health, networking). Hidden's own behaviour scripts are removed, every renderer, light,
    /// particle system and sound is switched off, and AlterMimic is added next to its Enemy component. Registered with
    /// REPOLib as a tier 2 enemy (EnemyContent wrapping our own EnemySetup).
    /// </summary>
    internal static class MimicBuilder
    {
        internal const string DisplayName = "Alter Mimic";
        private const string SourcePath = "Enemies/Enemy - Hidden";

        internal static EnemySetup Setup;   // for the debug spawn key
        private static GameObject holder;

        internal static void BuildAndRegister()
        {
            GameObject source = Resources.Load<GameObject>(SourcePath);
            if (source == null) { Plugin.Log.LogError($"{DisplayName}: '{SourcePath}' not found; it will not spawn."); return; }

            if (holder == null)
            {
                holder = new GameObject("HatterEnemiesPack.Prefabs");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject go = Object.Instantiate(source, holder.transform);
            go.name = "Enemy - Alter Mimic";
            Plugin.Log.LogInfo($"{DisplayName}: the Hidden enemy's parts:\n{Describe(go.transform, 0)}");

            // Hidden's own behaviour goes (its state machine, animation, sounds); the shared enemy parts stay
            var removed = new List<string>();
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string type = mb.GetType().Name;
                if (type.StartsWith("EnemyHidden"))
                {
                    removed.Add($"{type} on '{mb.name}'");
                    Object.DestroyImmediate(mb);
                }
            }
            Plugin.Log.LogInfo($"{DisplayName}: removed {string.Join(", ", removed)}");

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var em = ps.emission; em.enabled = false;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true)) { a.volume = 0f; a.mute = true; }

            EnemyParent parent = go.GetComponent<EnemyParent>() ?? go.GetComponentInChildren<EnemyParent>(true);
            Enemy enemy = go.GetComponentInChildren<Enemy>(true);
            if (parent == null || enemy == null) { Plugin.Log.LogError($"{DisplayName}: the Hidden enemy has no EnemyParent/Enemy; it will not spawn."); return; }
            parent.enemyName = DisplayName;
            parent.difficulty = EnemyParent.Difficulty.Difficulty2;
            enemy.gameObject.AddComponent<AlterMimic>();
            Plugin.Log.LogInfo($"{DisplayName}: Hidden's spawn times {parent.SpawnedTimeMin}-{parent.SpawnedTimeMax} s, despawn {parent.DespawnedTimeMin}-{parent.DespawnedTimeMax} s; " +
                               $"behaviour on '{enemy.name}'; rigidbody '{go.GetComponentInChildren<EnemyRigidbody>(true)?.name}'.");

            var setup = ScriptableObject.CreateInstance<EnemySetup>();
            setup.name = "Enemy - Alter Mimic";
            setup.status = EnemySetup.Status.Finalized;   // the game skips "WIP" setups
            setup.spawnObjects = new List<PrefabRef>();
            Setup = setup;

            var content = ScriptableObject.CreateInstance<EnemyContent>();
            content.name = "Alter Mimic Content";
            AccessTools.Field(typeof(EnemyContent), "_setup").SetValue(content, setup);
            AccessTools.Field(typeof(EnemyContent), "_spawnObjects").SetValue(content, new List<GameObject> { go });
            REPOLib.Modules.Enemies.RegisterEnemy(content);
            Plugin.Log.LogInfo($"{DisplayName}: registered as a tier 2 enemy ({setup.spawnObjects.Count} spawn object(s)).");
        }

        private static string Describe(Transform t, int depth)
        {
            var sb = new StringBuilder();
            sb.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)").Append(": ");
            var names = new List<string>();
            foreach (Component c in t.GetComponents<Component>()) if (c != null && !(c is Transform)) names.Add(c.GetType().Name);
            sb.Append(string.Join(", ", names)).Append('\n');
            if (depth < 6) foreach (Transform child in t) sb.Append(Describe(child, depth + 1));
            return sb.ToString();
        }
    }

    /// <summary>Keeps the Alter Mimic out of singleplayer games (unless SoloTesting is on).</summary>
    [HarmonyPatch(typeof(EnemyDirector), "PickEnemies")]
    internal static class MimicPickPatch
    {
        private static void Prefix(ref List<EnemySetup> _enemiesList)
        {
            if (MimicBuilder.Setup == null || _enemiesList == null || !_enemiesList.Contains(MimicBuilder.Setup)) return;
            if (R.Multiplayer || Settings.DebugSolo.Value) return;
            var copy = new List<EnemySetup>(_enemiesList);
            copy.Remove(MimicBuilder.Setup);
            _enemiesList = copy;
        }
    }
}
