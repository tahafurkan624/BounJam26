using BounJam.Core;
using BounJam.Interactables;
using BounJam.Liquids;
using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Generates WaterButton.prefab and the pushable PushableCube / PushableSphere prefabs.
    /// Rebuilding overwrites hand edits made to those prefabs.
    /// </summary>
    static class InteractablePrefabBuilder
    {
        public const string ButtonPath = "Assets/_Game/Prefabs/Interactables/WaterButton.prefab";
        public const string CubePath = "Assets/_Game/Prefabs/Interactables/PushableCube.prefab";
        public const string SpherePath = "Assets/_Game/Prefabs/Interactables/PushableSphere.prefab";

        [MenuItem("BounJam/Setup/Rebuild Interactable Prefabs")]
        internal static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[BounJam] Exit Play Mode before rebuilding the interactable prefabs.");
                return;
            }
            Material pushable = BuilderUtility.FlatMaterial("Greybox_Pushable", new Color(0.95f, 0.8f, 0.35f));
            BuildPrefab(ButtonPath, new GameObject("Water Button", typeof(LiquidTarget), typeof(LiquidButton)), FillButton);
            BuildPrefab(CubePath, GameObject.CreatePrimitive(PrimitiveType.Cube), cube => FillPushable(cube, "Pushable Cube", 1f, pushable));
            BuildPrefab(SpherePath, GameObject.CreatePrimitive(PrimitiveType.Sphere), sphere => FillPushable(sphere, "Pushable Sphere", 0.8f, pushable));
            Debug.Log("[BounJam] Built interactable prefabs.");
        }

        // The temporary root is removed from the open scene even if filling it in fails.
        static void BuildPrefab(string path, GameObject root, System.Action<GameObject> fill)
        {
            try
            {
                fill(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // A pedestal with a round cap facing +Z, and a liquid-only hitbox in front of the cap that is a bit
        // bigger than it, so a stream doesn't have to be pixel-perfect.
        static void FillButton(GameObject root)
        {
            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = "Pedestal";
            pedestal.layer = GameLayers.Ground;
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            pedestal.transform.localScale = new Vector3(0.6f, 1f, 0.3f);
            pedestal.GetComponent<MeshRenderer>().sharedMaterial =
                BuilderUtility.FlatMaterial("Greybox_Block", new Color(0.62f, 0.64f, 0.70f));

            GameObject cap = BuilderUtility.VisualPrimitive(PrimitiveType.Cylinder, "Cap", root.transform,
                new Vector3(0f, 0.75f, 0.2f), new Vector3(0.4f, 0.05f, 0.4f),
                BuilderUtility.FlatMaterial("Greybox_ButtonCap", Color.white));
            cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var hitbox = new GameObject("Hitbox", typeof(BoxCollider));
            hitbox.layer = GameLayers.LiquidHitbox;
            hitbox.transform.SetParent(root.transform, false);
            var box = hitbox.GetComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.75f, 0.25f);
            box.size = new Vector3(0.6f, 0.6f, 0.3f);

            BuilderUtility.SetValues(root.GetComponent<LiquidButton>(),
                ("cap", cap.transform), ("capRenderer", cap.GetComponent<MeshRenderer>()));
        }

        static void FillPushable(GameObject go, string name, float size, Material material)
        {
            go.name = name;
            go.layer = GameLayers.Pushable;
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            var body = go.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.linearDamping = 0.2f;
            body.angularDamping = 0.5f;
        }
    }
}
