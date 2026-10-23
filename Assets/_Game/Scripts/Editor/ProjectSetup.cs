using System.Linq;
using BounJam.Core;
using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Writes the game's layers and physics rules into Project Settings. Runs by itself the first time
    /// the layers are missing (and builds the sandbox scene then); afterwards use the BounJam menu.
    /// </summary>
    [InitializeOnLoad]
    static class ProjectSetup
    {
        static ProjectSetup() => EditorApplication.delayCall += SetUpIfNeeded;

        static void SetUpIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || LayersAreSetUp())
                return;
            ApplyLayersAndPhysics();
            SandboxBuilder.BuildIfMissing();
        }

        static bool LayersAreSetUp() =>
            GameLayers.Custom.All(layer => LayerMask.LayerToName(layer.index) == layer.name);

        [MenuItem("BounJam/Setup/Apply Layers and Physics")]
        public static void ApplyLayersAndPhysics()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            foreach (var (index, name) in GameLayers.Custom)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                if (!string.IsNullOrEmpty(layer.stringValue) && layer.stringValue != name)
                    Debug.LogWarning($"[BounJam] Layer {index} was '{layer.stringValue}', renaming it to '{name}'.");
                layer.stringValue = name;
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();

            // Trigger volumes only need to notice things that move by themselves or get pushed.
            int triggerContacts = GameLayers.MaskOf(GameLayers.Player, GameLayers.Enemy, GameLayers.Pushable);
            for (int layer = 0; layer < 32; layer++)
                Physics.IgnoreLayerCollision(GameLayers.Trigger, layer, (triggerContacts & (1 << layer)) == 0);

            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Layers and physics rules applied.");
        }
    }
}
