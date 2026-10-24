using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Brings a level up to date with the level prefabs (see <see cref="LevelParts"/>): shaped pieces are rebuilt from
    /// the part prefabs, and fixed pieces built straight from the kits (buttons, gates, lifts, crates, magnets, pickups,
    /// tiles, the dock) are swapped for their prefabs in the same place. What made each one different stays: its name,
    /// transform and tuning, and every link to it or from it, like the buttons on a gate or the level's dock.
    /// </summary>
    public static class LevelUpgrade
    {
        // Bookkeeping every object has; never copied from the old piece to its prefab.
        static readonly HashSet<string> Untouched = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Script", "m_EditorHideFlags", "m_EditorClassIdentifier", "m_Name",
        };

        /// <summary>Upgrades the scene's pieces, recorded for undo (rebuilt shapes aside). Returns what it did.</summary>
        public static string Upgrade(Scene scene)
        {
            List<LevelPiece> shaped = InScene<LevelPiece>(scene).ToList();
            foreach (LevelPiece piece in shaped) LevelPieces.Rebuild(piece);

            // Every replacement first, so links between replaced pieces can follow both ends.
            var map = new Dictionary<Object, Object>();
            var tuned = new List<(Component from, Component to)>();
            var swaps = new List<(GameObject old, GameObject fresh)>();
            foreach (GameObject old in InScene<Transform>(scene).Select(t => t.gameObject).Where(IsKitBuilt).ToList())
            {
                GameObject prefab = PrefabFor(old);
                if (prefab == null) continue;
                Transform o = old.transform;
                var fresh = (GameObject)PrefabUtility.InstantiatePrefab(prefab, o.parent);
                if (o.parent == null) SceneManager.MoveGameObjectToScene(fresh, scene);
                fresh.name = old.name;
                fresh.transform.SetSiblingIndex(o.GetSiblingIndex());
                fresh.transform.SetLocalPositionAndRotation(o.localPosition, o.localRotation);
                fresh.transform.localScale = o.localScale;
                if (fresh.activeSelf != old.activeSelf) fresh.SetActive(old.activeSelf);
                Undo.RegisterCreatedObjectUndo(fresh, "Use Level Prefabs");
                Pair(o, fresh.transform, map, tuned);
                swaps.Add((old, fresh));
            }

            var olds = new HashSet<GameObject>(swaps.Select(s => s.old));
            foreach (var (from, to) in tuned) CopyTuning(from, to, map, olds);
            // Whatever else pointed at an old piece (the level logic at its dock, say) points at its prefab now.
            foreach (Component component in InScene<Component>(scene).ToList())
                if (component != null && !(component is Transform) && !Inside(component, olds)) Repoint(component, map);
            foreach (GameObject old in olds) Undo.DestroyObjectImmediate(old);

            string swapped = string.Join(", ", swaps.GroupBy(s => PrefabUtility.GetCorrespondingObjectFromSource(s.fresh).name)
                                                    .Select(g => g.Count() == 1 ? g.Key : $"{g.Count()} x {g.Key}"));
            return $"Rebuilt {shaped.Count} shaped piece(s) from the part prefabs; swapped {swaps.Count} piece(s) for prefabs" +
                   (swaps.Count > 0 ? $": {swapped}." : ".");
        }

        static IEnumerable<T> InScene<T>(Scene scene) where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

        /// <summary>Built by the kits rather than placed as a prefab, and not part of a shaped piece.</summary>
        static bool IsKitBuilt(GameObject go)
            => !PrefabUtility.IsPartOfPrefabInstance(go) && go.GetComponentInParent<LevelPiece>(true) == null && PieceName(go) != null;

        const string DockName = "Dock", TilePrefix = "Tile ";

        /// <summary>Which of the level editor's pieces this is (its Placeable's name, the dock or "Tile &lt;look&gt;"), or null.</summary>
        static string PieceName(GameObject go)
        {
            if (go.TryGetComponent(out PressureButton button))
            {
                Object idle = new SerializedObject(button).FindProperty("idleMaterial").objectReferenceValue;
                string material = idle != null ? idle.name : "";
                return material == "Button_MagnetOn" ? "Magnet On" : material == "Button_MagnetOff" ? "Magnet Off" : "Button";
            }
            if (go.TryGetComponent(out ButtonMover mover))
                return new SerializedObject(mover).FindProperty("carriesRiders").boolValue ? "Lift" : "Gate";
            if (go.TryGetComponent(out Pushable _)) return "Crate";
            if (go.TryGetComponent(out Magnet _)) return "Magnet";
            if (go.TryGetComponent(out WaterDroplet _)) return "Droplet";
            if (go.TryGetComponent(out IronBall _)) return "Pocket Magnet";
            if (go.TryGetComponent(out ShipDock _)) return DockName;
            return TileLook(go) is string look ? TilePrefix + look : null;
        }

        static GameObject PrefabFor(GameObject go)
        {
            string name = PieceName(go);
            if (name == null) return null;
            if (name == DockName) return LevelParts.Dock;
            if (name.StartsWith(TilePrefix)) return Placeable.TilePrefab(name.Substring(TilePrefix.Length));
            return Placeable.Named(name).Prefab();
        }

        /// <summary>The SM_Tile_* look of a concrete tile GardenKit.Tile built, or null.</summary>
        static string TileLook(GameObject go)
        {
            if (!go.TryGetComponent(out BoxCollider _) || go.GetComponents<MonoBehaviour>().Length > 0) return null;
            Transform model = go.transform.Find("Model");
            if (model == null || !model.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return null;
            string mesh = filter.sharedMesh.name;
            const string prefix = "SM_Tile_";
            return mesh.StartsWith(prefix) && mesh != prefix + "Soil" ? mesh.Substring(prefix.Length) : null;
        }

        /// <summary>
        /// Matches the old piece's objects and components with the prefab's: children by name (in order among siblings
        /// with the same name), components by type. Game scripts and rigidbodies get their tuning copied over later.
        /// </summary>
        static void Pair(Transform old, Transform fresh, Dictionary<Object, Object> map, List<(Component, Component)> tuned)
        {
            map[old] = fresh;
            map[old.gameObject] = fresh.gameObject;

            Component[] candidates = fresh.GetComponents<Component>();
            var used = new HashSet<Component>();
            foreach (Component component in old.GetComponents<Component>())
            {
                if (component == null || component is Transform) continue;
                Component match = candidates.FirstOrDefault(c => c != null && c.GetType() == component.GetType() && !used.Contains(c));
                if (match == null) continue;
                used.Add(match);
                map[component] = match;
                if (component is MonoBehaviour || component is Rigidbody) tuned.Add((component, match));
            }

            var taken = new HashSet<Transform>();
            foreach (Transform child in old)
            {
                Transform same = null;
                foreach (Transform candidate in fresh)
                    if (candidate.name == child.name && !taken.Contains(candidate))
                    {
                        same = candidate;
                        break;
                    }
                if (same == null) continue;
                taken.Add(same);
                Pair(child, same, map, tuned);
            }
        }

        /// <summary>
        /// Copies every setting that differs from the old component to its prefab's, so only real tuning becomes a
        /// prefab override. References follow the swap; ones to parts of the old piece the prefab doesn't have are left.
        /// </summary>
        static void CopyTuning(Component from, Component to, Dictionary<Object, Object> map, HashSet<GameObject> olds)
        {
            var source = new SerializedObject(from);
            var target = new SerializedObject(to);
            SerializedProperty property = source.GetIterator();
            bool enter = true;
            while (property.Next(enter))
            {
                enter = false;
                if (property.depth == 0 && Untouched.Contains(property.name)) continue;
                SerializedProperty into = target.FindProperty(property.propertyPath);
                if (into == null) continue;
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Generic:
                        if (property.isArray && into.arraySize != property.arraySize) into.arraySize = property.arraySize;
                        enter = true;
                        break;
                    case SerializedPropertyType.ObjectReference:
                        Object value = property.objectReferenceValue;
                        if (value != null && map.TryGetValue(value, out Object swapped)) value = swapped;
                        else if (value != null && Inside(value, olds)) break;
                        if (into.objectReferenceValue != value) into.objectReferenceValue = value;
                        break;
                    case SerializedPropertyType.ManagedReference:
                        break;
                    default:
                        if (!SerializedProperty.DataEquals(property, into)) target.CopyFromSerializedProperty(property);
                        break;
                }
            }
            target.ApplyModifiedProperties();
        }

        static void Repoint(Component component, Dictionary<Object, Object> map)
        {
            var settings = new SerializedObject(component);
            SerializedProperty property = settings.GetIterator();
            bool changed = false;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null) continue;
                if (!map.TryGetValue(property.objectReferenceValue, out Object swapped)) continue;
                property.objectReferenceValue = swapped;
                changed = true;
            }
            if (changed) settings.ApplyModifiedProperties();
        }

        static bool Inside(Object value, HashSet<GameObject> olds)
        {
            GameObject go = value is GameObject g ? g : value is Component c ? c.gameObject : null;
            for (Transform t = go != null ? go.transform : null; t != null; t = t.parent)
                if (olds.Contains(t.gameObject)) return true;
            return false;
        }
    }
}
