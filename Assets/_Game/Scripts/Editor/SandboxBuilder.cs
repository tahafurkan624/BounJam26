using System.IO;
using System.Linq;
using BounJam.Core;
using BounJam.Liquids;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>Builds Sandbox.unity, a flat greybox playground for testing movement and liquids.</summary>
    static class SandboxBuilder
    {
        const string ScenePath = "Assets/_Game/Scenes/Sandbox.unity";

        [MenuItem("BounJam/Setup/Rebuild Sandbox Scene")]
        static void Rebuild()
        {
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Rebuild Sandbox",
                    "Replace Sandbox.unity with a fresh copy? Any changes made to it are lost.", "Rebuild", "Cancel"))
                return;
            Build();
        }

        internal static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[BounJam] Exit Play Mode before rebuilding the sandbox.");
                return;
            }
            if (!File.Exists(PlayerPrefabBuilder.SpawnPath))
                PlayerPrefabBuilder.Build();
            if (!File.Exists(LevelPrefabBuilder.LevelPath))
                LevelPrefabBuilder.Build();
            if (!File.Exists(PlayerPrefabBuilder.SpawnPath) || !File.Exists(LevelPrefabBuilder.LevelPath))
                return; // the builders have logged why, e.g. they are waiting for the TextMeshPro import
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[BounJam] Sandbox not built. Use BounJam > Setup > Rebuild Sandbox Scene when ready.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            // The player rig brings its own camera.
            Object.DestroyImmediate(scene.GetRootGameObjects().First(go => go.GetComponent<Camera>() != null));

            Material ground = BuilderUtility.FlatMaterial("Greybox_Ground", new Color(0.42f, 0.55f, 0.36f));
            Material block = BuilderUtility.FlatMaterial("Greybox_Block", new Color(0.62f, 0.64f, 0.70f));
            Material accent = BuilderUtility.FlatMaterial("Greybox_Accent", new Color(0.85f, 0.55f, 0.30f));

            Transform environment = new GameObject("Environment").transform;
            Block("Floor", environment, new Vector3(0, -0.5f, 0), new Vector3(60, 1, 60), ground);
            for (int i = 1; i <= 3; i++)
            {
                float height = 0.5f * i;
                Block($"Step {height:0.0}m", environment, new Vector3(6, height / 2, i * 2.5f), new Vector3(2, height, 2), block);
            }
            // A 2 m rise over 8 m, leading onto a 2 m high block.
            Block("Ramp", environment, new Vector3(-6, 0.85f, 6), new Vector3(3, 0.3f, 8.25f), block, -14f);
            Block("Platform", environment, new Vector3(-6, 1, 13), new Vector3(3, 2, 6), block);
            Block("Target Wall", environment, new Vector3(4, 1.5f, 16), new Vector3(8, 3, 0.5f), accent);

            // Things to spray at: two loose objects water pushes, and a post that logs the liquid it gets.
            Material pushable = BuilderUtility.FlatMaterial("Greybox_Pushable", new Color(0.95f, 0.8f, 0.35f));
            Transform props = new GameObject("Test Props").transform;
            Pushable(PrimitiveType.Cube, "Pushable Cube", props, new Vector3(2.5f, 0.5f, -1f), 1f, pushable);
            Pushable(PrimitiveType.Sphere, "Pushable Sphere", props, new Vector3(-2.5f, 0.4f, -1f), 0.8f, pushable);
            LiquidProbe(props, new Vector3(-4f, 0.75f, -3f), accent);

            Transform gameplay = new GameObject("Gameplay").transform;
            Place(LevelPrefabBuilder.LevelPath, gameplay, Vector3.zero);
            Place(PlayerPrefabBuilder.SpawnPath, gameplay, new Vector3(0, 0, -6));
            // Two fires on the ground and one up on the platform, which needs an arcing shot.
            Place(LevelPrefabBuilder.FirePath, gameplay, new Vector3(0, 0, 3));
            Place(LevelPrefabBuilder.FirePath, gameplay, new Vector3(-4, 0, 0));
            Place(LevelPrefabBuilder.FirePath, gameplay, new Vector3(-6, 2, 13));

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            Debug.Log($"[BounJam] Built {ScenePath}.");
        }

        static void Block(string name, Transform parent, Vector3 position, Vector3 size, Material material, float pitch = 0)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = GameLayers.Ground;
            go.isStatic = true;
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, 0, 0));
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static void Place(string prefabPath, Transform parent, Vector3 position)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            instance.transform.SetParent(parent);
            instance.transform.position = position;
        }

        static void Pushable(PrimitiveType shape, string name, Transform parent, Vector3 position, float size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.layer = GameLayers.Pushable;
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<Rigidbody>().mass = 1f;
        }

        // On the LiquidHitbox layer: liquid hits it, but the player walks straight through.
        static void LiquidProbe(Transform parent, Vector3 position, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Liquid Probe";
            go.layer = GameLayers.LiquidHitbox;
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.6f, 0.75f, 0.6f);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<LiquidHitProbe>();
        }

        // Sandbox first. The URP template's sample scene is dropped so "Next Level" never loads it.
        static void AddToBuildSettings()
        {
            const string templateScenePath = "Assets/Scenes/SampleScene.unity";
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath && s.path != templateScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
