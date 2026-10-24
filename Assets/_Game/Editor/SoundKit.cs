using FMODUnity;
using UnityEditor;
using UnityEngine;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>The blob's FMOD sounds: <see cref="BlobMovementSound"/> playing the movement event.</summary>
    public static class SoundKit
    {
        const string MovementEventPath = "event:/Water_Movement";

        /// <summary>
        /// Adds the movement sound to a blob root that doesn't have it yet. True if it changed. False, with an error, when
        /// the event isn't in the built banks.
        /// </summary>
        public static bool AddBlobParts(GameObject blob)
        {
            if (blob.TryGetComponent(out BlobMovementSound _)) return false;
            EditorEventRef movementEvent = EventManager.EventFromPath(MovementEventPath);
            if (movementEvent == null)
            {
                Debug.LogError($"[BounJam] FMOD event {MovementEventPath} not found; build the banks in FMOD Studio and refresh them (FMOD > Refresh Banks).");
                return false;
            }

            var sound = blob.AddComponent<BlobMovementSound>();
            var settings = new SerializedObject(sound);
            SerializedProperty reference = settings.FindProperty("movementEvent");
            reference.FindPropertyRelative("Path").stringValue = movementEvent.Path;
            SerializedProperty guid = reference.FindPropertyRelative("Guid");
            guid.FindPropertyRelative("Data1").intValue = movementEvent.Guid.Data1;
            guid.FindPropertyRelative("Data2").intValue = movementEvent.Guid.Data2;
            guid.FindPropertyRelative("Data3").intValue = movementEvent.Guid.Data3;
            guid.FindPropertyRelative("Data4").intValue = movementEvent.Guid.Data4;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>Adds that part to the saved Blob prefab, leaving all its tuning as it is.</summary>
        [MenuItem("BounJam/Add Movement Sound To Blob Prefab")]
        public static void UpgradeBlobPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefabPath) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(BlobPrefabPath);
            if (AddBlobParts(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
                Debug.Log("[BounJam] Blob prefab: added the movement sound.");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
