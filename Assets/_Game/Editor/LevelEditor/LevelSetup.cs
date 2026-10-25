using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// What makes a scene a playable level, for the level editor: new level scenes, turning an open scene into one,
    /// wiring the level logic to the blob, ship and dock, and the play order in Build Settings with each level's next
    /// scene following it.
    /// </summary>
    public static class LevelSetup
    {
        public const string ScenesFolder = "Assets/_Game/Scenes";
        public const string LevelRootName = "Level";
        static readonly Vector3 BlobSpawn = new Vector3(0f, 0f, -1.5f);

        // ---------------------------------------------------------------- Finding things

        public static T Find<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        public static Transform Blob(Scene scene) => Find<BlobMotor>(scene)?.transform;

        /// <summary>The group under the scene's Level root that pieces of one sort go in, made if it's missing.</summary>
        public static Transform Group(Scene scene, string name)
        {
            Transform level = LevelRoot(scene);
            Transform group = level.Find(name);
            if (group != null) return group;
            group = new GameObject(name).transform;
            group.SetParent(level, false);
            Undo.RegisterCreatedObjectUndo(group.gameObject, $"Add {name}");
            return group;
        }

        public static Transform LevelRoot(Scene scene)
        {
            GameObject level = scene.GetRootGameObjects().FirstOrDefault(root => root.name == LevelRootName);
            if (level != null) return level.transform;
            level = new GameObject(LevelRootName);
            SceneManager.MoveGameObjectToScene(level, scene);
            Undo.RegisterCreatedObjectUndo(level, $"Add {LevelRootName}");
            return level.transform;
        }

        // ---------------------------------------------------------------- Making levels

        /// <summary>
        /// Asks where to save a new level, then makes it: a start platform with the blob and the ship on it and a goal
        /// platform with the dock, 12 m apart for the course to go between. It's added to the play order and opened.
        /// </summary>
        public static void NewLevel()
        {
            string path = EditorUtility.SaveFilePanelInProject("New Level", NextLevelName(), "unity",
                                                               "Where should the new level be saved?", ScenesFolder);
            if (string.IsNullOrEmpty(path) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            NewLevel(path);
        }

        /// <summary>Makes a new level and saves it at <paramref name="path"/>, replacing the open scenes without asking.</summary>
        public static void NewLevel(string path)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SetupEnvironment();
            Palette m = CreatePalette();

            Transform ground = Group(scene, "Ground");
            LevelPieces.Create(LevelPiece.Kind.Platform, "Start", ground, Vector3.zero, Quaternion.identity, new Vector3(6f, 1f, 6f));
            LevelPieces.Create(LevelPiece.Kind.Platform, "Goal", ground, new Vector3(0f, 0f, 12f), Quaternion.identity, new Vector3(6f, 1f, 6f));
            var (blob, ship) = PlacePlayer(m, BlobSpawn, OnBlob(BlobSpawn));
            ShipDock dock = PlaceDock(scene, new Vector3(0f, 0f, 12.5f), ship);
            BuildCamera(blob.transform);

            string name = Path.GetFileNameWithoutExtension(path);
            string first = PlayOrder().Select(Path.GetFileNameWithoutExtension).FirstOrDefault() ?? name;
            BuildGame(ObjectNames.NicifyVariableName(name), first, false, blob, ship, dock);

            EditorSceneManager.SaveScene(scene, path);
            AddToPlayOrder(path);
            Debug.Log($"[BounJam] New level {path}. It loops back to {first} when completed until you link the levels.");
        }

        /// <summary>
        /// Adds whatever the open scene is missing to be a level: lighting, a Level root, the blob and the ship, the dock,
        /// the follow camera and the level logic. Nothing that's already there is replaced.
        /// </summary>
        public static void MakeLevel(Scene scene)
        {
            var before = new HashSet<GameObject>(scene.GetRootGameObjects());
            Palette m = CreatePalette();
            if (Find<Volume>(scene) == null) SetupEnvironment();
            Transform level = LevelRoot(scene);

            Transform blob = Blob(scene);
            ShipBuoyancy ship = Find<ShipBuoyancy>(scene);
            if (blob == null && ship == null)
            {
                var placed = PlacePlayer(m, BlobSpawn, OnBlob(BlobSpawn));
                blob = placed.blob.transform;
                ship = placed.ship;
            }
            else if (blob == null)
            {
                blob = Instantiate(BlobPrefabPath, BlobSpawn + Vector3.up * CoreRadius).transform;
            }
            else if (ship == null)
            {
                ship = Instantiate(ShipPrefabPath, OnBlob(blob.position - Vector3.up * CoreRadius)).GetComponent<ShipBuoyancy>();
            }

            ShipDock dock = Find<ShipDock>(scene);
            if (dock == null) dock = PlaceDock(scene, BlobSpawn + new Vector3(0f, 0f, 8f), ship);
            if (Find<CinemachineCamera>(scene) == null) BuildCamera(blob);

            LevelManager manager = Find<LevelManager>(scene);
            if (manager == null)
            {
                string first = PlayOrder().Select(Path.GetFileNameWithoutExtension).FirstOrDefault() ?? scene.name;
                BuildGame(ObjectNames.NicifyVariableName(scene.name), first, false, blob.gameObject, ship, dock);
            }
            else FixReferences(manager);

            // The Level root records its own undo when it's made.
            foreach (GameObject root in scene.GetRootGameObjects())
                if (!before.Contains(root) && root != level.gameObject) Undo.RegisterCreatedObjectUndo(root, "Make Level");
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>The dock prefab on the floor at <paramref name="position"/>, under the Level root, pointed at the ship.</summary>
        public static ShipDock PlaceDock(Scene scene, Vector3 position, ShipBuoyancy ship)
        {
            GameObject go = LevelParts.Instance(LevelParts.Dock, LevelRoot(scene), "Dock");
            go.transform.position = position;
            var dock = go.GetComponent<ShipDock>();
            var settings = new SerializedObject(dock);
            settings.FindProperty("ship").objectReferenceValue = ship;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return dock;
        }

        static GameObject Instantiate(string prefabPath, Vector3 position)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            go.transform.position = position;
            return go;
        }

        /// <summary>Where the ship goes to ride a blob standing at <paramref name="blobFloor"/>.</summary>
        public static Vector3 OnBlob(Vector3 blobFloor) => blobFloor + Vector3.up * CoreRadius + ShipOnBlobOffset;

        /// <summary>Points the level logic at the scene's blob, ship, dock and HUD, and the dock at the ship.</summary>
        public static void FixReferences(LevelManager manager)
        {
            Scene scene = manager.gameObject.scene;
            ShipBuoyancy ship = Find<ShipBuoyancy>(scene);
            ShipDock dock = Find<ShipDock>(scene);

            var settings = new SerializedObject(manager);
            Fill(settings.FindProperty("blob"), Blob(scene));
            Fill(settings.FindProperty("ship"), ship);
            Fill(settings.FindProperty("dock"), dock);
            Fill(settings.FindProperty("hud"), Find<LevelHud>(scene));
            settings.ApplyModifiedProperties();

            if (dock != null)
            {
                var dockSettings = new SerializedObject(dock);
                Fill(dockSettings.FindProperty("ship"), ship);
                dockSettings.ApplyModifiedProperties();
            }
        }

        static void Fill(SerializedProperty property, Object value)
        {
            if (property.objectReferenceValue == null && value != null) property.objectReferenceValue = value;
        }

        /// <summary>The level's missing references, by name; empty when it's ready to play.</summary>
        public static List<string> Missing(LevelManager manager)
        {
            var missing = new List<string>();
            var settings = new SerializedObject(manager);
            foreach (string field in new[] { "blob", "ship", "dock" })
                if (settings.FindProperty(field).objectReferenceValue == null) missing.Add(field);
            var dock = (ShipDock)settings.FindProperty("dock").objectReferenceValue;
            if (dock != null && new SerializedObject(dock).FindProperty("ship").objectReferenceValue == null) missing.Add("dock's ship");
            return missing;
        }

        // ---------------------------------------------------------------- Play order

        /// <summary>The levels in the run, in play order: the scenes on in Build Settings, without the sandbox.</summary>
        public static List<string> PlayOrder()
            => EditorBuildSettings.scenes.Where(s => s.enabled && s.path != SandboxBuilder.ScenePath).Select(s => s.path).ToList();

        public static void AddToPlayOrder(string path)
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            // Before the sandbox, which stays last.
            int sandbox = scenes.FindIndex(s => s.path == SandboxBuilder.ScenePath);
            scenes.Insert(sandbox < 0 ? scenes.Count : sandbox, new EditorBuildSettingsScene(path, true));
            SaveBuildScenes(scenes);
        }

        public static void SaveBuildScenes(IEnumerable<EditorBuildSettingsScene> scenes)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets(); // writes the build settings to ProjectSettings now, not at editor shutdown
        }

        /// <summary>
        /// Points each level in the play order at the one after it, and makes the last one the final level, which
        /// starts over from the first. Scenes with unsaved edits get the change but are left for you to save.
        /// </summary>
        public static void LinkLevels()
        {
            // Levels only: the main menu comes first in the build, but the last level starts over at the first level, not the menu.
            List<string> order = PlayOrder().Where(path => ReadLevel(path).IsLevel).ToList();
            if (order.Count == 0 || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var unsaved = new List<string>();
            for (int i = 0; i < order.Count; i++)
            {
                string path = order[i];
                Scene scene = SceneManager.GetSceneByPath(path);
                bool wasOpen = scene.IsValid() && scene.isLoaded;
                if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                bool hadUnsavedEdits = scene.isDirty;

                LevelManager manager = Find<LevelManager>(scene);
                if (manager == null) Debug.LogWarning($"[BounJam] {path} has no level logic (LevelManager); skipped.");
                else
                {
                    var settings = new SerializedObject(manager);
                    settings.FindProperty("nextScene").stringValue = Path.GetFileNameWithoutExtension(order[(i + 1) % order.Count]);
                    settings.FindProperty("isFinalLevel").boolValue = i == order.Count - 1;
                    settings.ApplyModifiedProperties();
                    // Someone's unsaved work in the scene is theirs to save.
                    if (hadUnsavedEdits) unsaved.Add(scene.name);
                    else EditorSceneManager.SaveScene(scene);
                }
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
            levelInfo.Clear();
            Debug.Log("[BounJam] Levels linked: " + string.Join(" -> ", order.Select(Path.GetFileNameWithoutExtension)) +
                      (unsaved.Count > 0 ? $". Not saved, they had unsaved edits: {string.Join(", ", unsaved)}." : "."));
        }

        // ---------------------------------------------------------------- Reading levels without opening them

        public struct LevelInfo
        {
            public bool IsLevel;
            public string Title, NextScene;
            public bool IsFinal;
        }

        static readonly Dictionary<string, (System.DateTime written, LevelInfo info)> levelInfo =
            new Dictionary<string, (System.DateTime, LevelInfo)>();

        /// <summary>A saved level's title, next scene and final flag, read straight from the scene file.</summary>
        public static LevelInfo ReadLevel(string path)
        {
            if (!File.Exists(path)) return default;
            System.DateTime written = File.GetLastWriteTimeUtc(path);
            if (levelInfo.TryGetValue(path, out var cached) && cached.written == written) return cached.info;

            string text = File.ReadAllText(path);
            Match title = Regex.Match(text, @"^  levelTitle: (.*)$", RegexOptions.Multiline);
            Match next = Regex.Match(text, @"^  nextScene: ?(.*)$", RegexOptions.Multiline);
            Match final = Regex.Match(text, @"^  isFinalLevel: (\d)", RegexOptions.Multiline);
            var info = new LevelInfo
            {
                IsLevel = title.Success,
                Title = title.Success ? title.Groups[1].Value.Trim().Trim('\'', '"') : "",
                NextScene = next.Success ? next.Groups[1].Value.Trim() : "",
                IsFinal = final.Success && final.Groups[1].Value == "1",
            };
            levelInfo[path] = (written, info);
            return info;
        }

        public static bool InBuild(string sceneName)
            => EditorBuildSettings.scenes.Any(s => s.enabled && Path.GetFileNameWithoutExtension(s.path) == sceneName);

        static string NextLevelName()
        {
            for (int i = 1; ; i++)
            {
                string name = $"Level{i:00}";
                if (!File.Exists($"{ScenesFolder}/{name}.unity")) return name;
            }
        }
    }
}
