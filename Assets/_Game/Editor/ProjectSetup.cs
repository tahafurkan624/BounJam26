using System.IO;
using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Project setup for layers, the physics collision matrix and the sandbox scene.
    /// Runs automatically while a layer slot is still empty or the template scene still exists,
    /// and can be re-run any time from the BounJam menu. Never renames a layer someone else set.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        const string TemplateScenePath = "Assets/Scenes/SampleScene.unity";
        const string ScenesFolder = "Assets/_Game/Scenes";
        const string SandboxScenePath = ScenesFolder + "/Sandbox.unity";

        static readonly (int index, string name)[] Layers =
        {
            (6, "Environment"),
            (7, "PlayerLiquid"),
            (8, "PlayerSolid"),
            (9, "Ship"),
            (10, "Pushable"),
            (11, "Interactable"),
            (12, "LiquidPassable"),
            (13, "Triggers"),
        };

        static readonly (int a, int b)[] IgnoredCollisions =
        {
            (7, 9),   // PlayerLiquid - Ship: the ship floats on the liquid through buoyancy, not contacts
            (7, 12),  // PlayerLiquid - LiquidPassable: liquid flows through grates, solid and ship don't
            (13, 6),  // Triggers - Environment
            (13, 13), // Triggers - Triggers
        };

        static ProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (NeedsSetup()) Run();
            };
        }

        [MenuItem("BounJam/Run Project Setup")]
        public static void Run()
        {
            SetupLayers();
            SetupCollisionMatrix();
            SetupSandboxScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Project setup applied: layers, collision matrix, sandbox scene.");
        }

        static bool NeedsSetup()
        {
            foreach (var (index, _) in Layers)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(index))) return true;
            return File.Exists(TemplateScenePath);
        }

        static void SetupLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            foreach (var (index, name) in Layers)
            {
                var layer = layers.GetArrayElementAtIndex(index);
                if (string.IsNullOrEmpty(layer.stringValue))
                    layer.stringValue = name;
                else if (layer.stringValue != name)
                    Debug.LogWarning($"[BounJam] Layer {index} is '{layer.stringValue}', expected '{name}'. Left unchanged.");
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetupCollisionMatrix()
        {
            var dynamicsManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
            var matrix = dynamicsManager.FindProperty("m_LayerCollisionMatrix");
            foreach (var (a, b) in IgnoredCollisions)
            {
                var rowA = matrix.GetArrayElementAtIndex(a);
                var rowB = matrix.GetArrayElementAtIndex(b);
                rowA.uintValue &= ~(1u << b);
                rowB.uintValue &= ~(1u << a);
            }
            dynamicsManager.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetupSandboxScene()
        {
            if (!File.Exists(TemplateScenePath) || File.Exists(SandboxScenePath)) return;

            if (!AssetDatabase.IsValidFolder(ScenesFolder))
                AssetDatabase.CreateFolder("Assets/_Game", "Scenes");

            string error = AssetDatabase.MoveAsset(TemplateScenePath, SandboxScenePath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[BounJam] Could not move the template scene: {error}");
                return;
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SandboxScenePath, true) };

            if (AssetDatabase.IsValidFolder("Assets/Scenes") && Directory.GetFileSystemEntries("Assets/Scenes").Length == 0)
                AssetDatabase.DeleteAsset("Assets/Scenes");
        }
    }
}
