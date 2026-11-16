using UnityEngine;

namespace HatterWeaponsPack
{
    /// <summary>
    /// Shared steps for building a weapon prefab at startup from the game's shotgun: it keeps the shotgun's grabbing,
    /// trigger, battery, muzzle flash, sound and networking; each weapon then swaps in its own model and behaviour.
    /// </summary>
    internal static class WeaponBuilder
    {
        private const string ShotgunPath = "Items/Item Gun Shotgun";
        private static GameObject holder;

        /// <summary>An inactive copy of the shotgun to turn into a new weapon (null if the shotgun is missing).</summary>
        internal static GameObject CloneShotgun(string prefabName) => CloneItem(ShotgunPath, prefabName);

        /// <summary>An inactive copy of a vanilla item prefab (Resources path) to turn into a new item.</summary>
        internal static GameObject CloneItem(string path, string prefabName)
        {
            GameObject shotgun = Resources.Load<GameObject>(path);
            if (shotgun == null) { Plugin.Log.LogError($"'{path}' not found."); return null; }
            if (holder == null)
            {
                holder = new GameObject("HatterWeaponsPack.PrefabHolder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
                holder.hideFlags = HideFlags.HideAndDontSave;
            }
            GameObject go = Object.Instantiate(shotgun, holder.transform);
            go.name = prefabName;
            return go;
        }

        /// <summary>New shop entry: name, description and price (a multiple of the shotgun's).</summary>
        internal const string HandgunPath = "Items/Item Gun Handgun";

        /// <param name="priceFrom">Prefab whose shop price the multiplier applies to (default: the shotgun this is cloned from).</param>
        internal static void SetupItem(GameObject go, string prefabName, string displayName, string description, float priceMultiplier,
                                       string priceFrom = null)
        {
            ItemAttributes attributes = go.GetComponent<ItemAttributes>();
            Item source = attributes.item;
            Item item = Object.Instantiate(source);
            item.name = prefabName;
            item.itemName = displayName;
            item.itemNameLocalized = null;
            item.description = description;
            Value baseValue = source.value;
            if (priceFrom != null)
            {
                ItemAttributes other = Resources.Load<GameObject>(priceFrom)?.GetComponent<ItemAttributes>();
                if (other != null && other.item != null && other.item.value != null) baseValue = other.item.value;
                else Plugin.Log.LogWarning($"'{priceFrom}' price not found; {displayName} is priced from the shotgun instead.");
            }
            if (baseValue != null)
            {
                Value price = Object.Instantiate(baseValue);
                price.name = prefabName + " Value";
                price.valueMin *= priceMultiplier;
                price.valueMax *= priceMultiplier;
                item.value = price;
            }
            attributes.item = item;
            attributes.icon = null; // rendered by the game from the new model
        }

        /// <summary>Hides the shotgun's mesh (its transforms stay for the trigger animation); returns its material as a template.</summary>
        internal static Material HideShotgunMesh(Transform root)
        {
            Material template = null;
            Transform shotgunMesh = root.Find("Mesh");
            if (shotgunMesh != null)
            {
                foreach (Renderer r in shotgunMesh.GetComponentsInChildren<Renderer>(true))
                {
                    if (template == null && r.sharedMaterial != null) template = r.sharedMaterial;
                    r.enabled = false;
                }
            }
            return template;
        }

        /// <summary>Re-fits the shotgun's grab colliders (boxes in weapon space); extra colliders are switched off.</summary>
        internal static void FitColliders(Transform root, (Vector3 pos, Vector3 size)[] fit)
        {
            Transform colliders = root.Find("Colliders");
            if (colliders == null) return;
            var boxes = colliders.GetComponentsInChildren<BoxCollider>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                if (i >= fit.Length) { boxes[i].gameObject.SetActive(false); continue; }
                Transform t = boxes[i].transform;
                t.localRotation = Quaternion.identity;
                t.position = root.TransformPoint(fit[i].pos);
                Vector3 parentScale = t.parent.lossyScale;
                t.localScale = new Vector3(fit[i].size.x / parentScale.x, fit[i].size.y / parentScale.y, fit[i].size.z / parentScale.z);
                boxes[i].center = Vector3.zero;
                boxes[i].size = Vector3.one;
            }
        }

        internal static void SetForceGrabPoint(Transform root, Vector3 localPosition)
        {
            Transform forceGrab = root.Find("Force Grab Point");
            if (forceGrab != null) forceGrab.localPosition = localPosition;
        }
    }
}
