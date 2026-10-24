using FMODUnity;
using UnityEditor;
using UnityEngine;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The FMOD sounds of the blob (<see cref="BlobMovementSound"/>, <see cref="BlobSounds"/>, <see cref="BlobIceSound"/>)
    /// and the ship (<see cref="ShipSounds"/>), with their events assigned. The level's machines and items find theirs
    /// by path (<see cref="GameSounds"/>).
    /// </summary>
    public static class SoundKit
    {
        const string MovementEventPath = "event:/Water_Movement";
        const string JumpEventPath = "event:/Water_Jump";
        const string FreezeEventPath = "event:/Water_Freeze";
        const string SplitEventPath = "event:/Water_Split";
        const string MergeEventPath = "event:/Water_Merge";
        const string IceMovementEventPath = "event:/Ice_Movement";
        const string IceOnWaterEventPath = "event:/Ice_On_Water";
        const string SplashEventPath = "event:/Water_Splash";
        const string DropEventPath = "event:/Ship_Drop";

        /// <summary>
        /// Adds the sounds a blob root doesn't have yet. True if it changed. A sound whose event isn't in the built banks
        /// is left out, with an error.
        /// </summary>
        public static bool AddBlobParts(GameObject blob)
        {
            bool changed = false;
            if (!blob.TryGetComponent(out BlobMovementSound _) && EventsExist(MovementEventPath))
            {
                var settings = new SerializedObject(blob.AddComponent<BlobMovementSound>());
                SetEvent(settings, "movementEvent", MovementEventPath);
                settings.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
            if (!blob.TryGetComponent(out BlobSounds _) && EventsExist(JumpEventPath, FreezeEventPath, SplitEventPath, MergeEventPath))
            {
                var settings = new SerializedObject(blob.AddComponent<BlobSounds>());
                SetEvent(settings, "jumpEvent", JumpEventPath);
                SetEvent(settings, "freezeEvent", FreezeEventPath);
                SetEvent(settings, "splitEvent", SplitEventPath);
                SetEvent(settings, "mergeEvent", MergeEventPath);
                settings.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
            if (!blob.TryGetComponent(out BlobIceSound _) && EventsExist(IceMovementEventPath, IceOnWaterEventPath))
            {
                var settings = new SerializedObject(blob.AddComponent<BlobIceSound>());
                SetEvent(settings, "iceMovementEvent", IceMovementEventPath);
                SetEvent(settings, "iceOnWaterEvent", IceOnWaterEventPath);
                settings.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Gives a ship root its sounds: adds them if it has none, and assigns the events of any it has but lacks. True if
        /// it changed.
        /// </summary>
        public static bool AddShipParts(GameObject ship)
        {
            if (!EventsExist(SplashEventPath, DropEventPath)) return false;
            if (!ship.TryGetComponent(out ShipSounds sounds)) sounds = ship.AddComponent<ShipSounds>();
            var settings = new SerializedObject(sounds);
            SetEvent(settings, "splashEvent", SplashEventPath);
            SetEvent(settings, "dropEvent", DropEventPath);
            return settings.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Adds those parts to the saved Blob and ship prefabs, leaving all their tuning as it is.</summary>
        [MenuItem("BounJam/Add Sounds To Blob And Ship Prefabs")]
        public static void UpgradePrefabs()
        {
            Upgrade(BlobPrefabPath, AddBlobParts, "Blob");
            Upgrade(ShipPrefabPath, AddShipParts, "Ship");
        }

        static void Upgrade(string path, System.Func<GameObject, bool> addParts, string label)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (addParts(root))
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[BounJam] {label} prefab: added its sounds.");
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        static bool EventsExist(params string[] paths)
        {
            foreach (string path in paths)
            {
                if (EventManager.EventFromPath(path) != null) continue;
                Debug.LogError($"[BounJam] FMOD event {path} not found; build the banks in FMOD Studio and refresh them (FMOD > Refresh Banks).");
                return false;
            }
            return true;
        }

        static void SetEvent(SerializedObject settings, string field, string path)
        {
            EditorEventRef found = EventManager.EventFromPath(path);
            SerializedProperty reference = settings.FindProperty(field);
            reference.FindPropertyRelative("Path").stringValue = found.Path;
            SerializedProperty guid = reference.FindPropertyRelative("Guid");
            guid.FindPropertyRelative("Data1").intValue = found.Guid.Data1;
            guid.FindPropertyRelative("Data2").intValue = found.Guid.Data2;
            guid.FindPropertyRelative("Data3").intValue = found.Guid.Data3;
            guid.FindPropertyRelative("Data4").intValue = found.Guid.Data4;
        }
    }
}
