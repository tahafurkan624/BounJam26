using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The water the liquid blob throws off on sharp moves: a droplet particle system that <see cref="BlobSplash"/>
    /// emits into and that dies on the level's surfaces, with a child system for the rings the droplets leave where
    /// they land (<see cref="DropletSplats"/>). Materials are only created when missing; like the jelly, the droplet
    /// shader's property defaults are the tuned look.
    /// </summary>
    public static class SplashKit
    {
        const string DropletShaderName = "BounJam/Droplet";
        const string DropletsName = "Droplets";
        // What droplets burst on: the level, crates and buttons. They fly through the player, the ship, triggers and grates.
        static readonly string[] SurfaceLayers = { "Default", "Environment", "Pushable", "Interactable" };
        // The droplet shader builds its quads from these, packed in this order into TEXCOORD0..2 (see Droplet.shader).
        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.Center,
            ParticleSystemVertexStream.SizeX, ParticleSystemVertexStream.Velocity,
        };

        /// <summary>
        /// Adds the splash to the saved Blob prefab in place. Its own contents are loaded and saved back, so every
        /// object already in it keeps its file ID (which the scenes' instances point at) and all its tuning; only the
        /// new objects get new IDs. Does nothing when the prefab already has the splash.
        /// </summary>
        [MenuItem("BounJam/Add Blob Splash")]
        public static void UpgradeBlobPrefab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[BounJam] Leave play mode before adding the splash to the Blob prefab.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefabPath) == null)
            {
                Debug.LogError($"[BounJam] No Blob prefab at {BlobPrefabPath}; use BounJam/Rebuild Blob Prefab.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(BlobPrefabPath);
            try
            {
                if (!AddBlobParts(root))
                {
                    Debug.Log("[BounJam] Blob prefab already has the splash.");
                    return;
                }
                PrefabUtility.SaveAsPrefabAsset(root, BlobPrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[BounJam] Splash added to the Blob prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Gives a blob (a new prefab root, or opened prefab contents) its splash: a "Droplets" child with a "Splats"
        /// child, and <see cref="BlobSplash"/> on the root. Replaces an earlier, incomplete copy. False when the blob
        /// already has the splash.
        /// </summary>
        public static bool AddBlobParts(GameObject root)
        {
            if (root.TryGetComponent(out BlobSplash _)) return false;
            // First, so a missing shader stops this before anything has changed.
            Material dropletMaterial = DropletMaterial("Blob_Droplet", false);
            Material splatMaterial = DropletMaterial("Blob_Splat", true);

            Transform stale = root.transform.Find(DropletsName);
            if (stale != null) Object.DestroyImmediate(stale.gameObject);
            int surfaces = LayerMask.GetMask(SurfaceLayers);

            // The last child, after the skin, so the skin stays the blob's first renderer.
            ParticleSystem droplets = Particles(DropletsName, root.transform, dropletMaterial, 96, 1f);
            // A droplet dies where it hits the level; DropletSplats leaves the ring.
            ParticleSystem.CollisionModule collision = droplets.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            // Small, fast droplets slip through the cheaper cached collision; there are few enough to trace each one.
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.collidesWith = surfaces;
            collision.enableDynamicColliders = true;
            collision.radiusScale = 0.5f;
            collision.bounce = 0f;
            collision.dampen = 1f;
            collision.lifetimeLoss = 1f;
            collision.colliderForce = 0f;
            collision.sendCollisionMessages = true;

            ParticleSystem splats = Particles("Splats", droplets.transform, splatMaterial, 48, 0f);
            var rings = droplets.gameObject.AddComponent<DropletSplats>();
            var ringSettings = new SerializedObject(rings);
            ringSettings.FindProperty("splats").objectReferenceValue = splats;
            ringSettings.ApplyModifiedPropertiesWithoutUndo();

            var splash = root.AddComponent<BlobSplash>();
            var settings = new SerializedObject(splash);
            settings.FindProperty("droplets").objectReferenceValue = droplets;
            settings.FindProperty("wallMask").intValue = surfaces;
            // The skin renderer BlobForm hardens, so both work on the same material copy.
            if (root.TryGetComponent(out BlobForm form))
                settings.FindProperty("skinRenderer").objectReferenceValue = new SerializedObject(form).FindProperty("skinRenderer").objectReferenceValue;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>
        /// A particle system that only code emits into: world space, no emission or shape of its own, always simulated,
        /// and sending the vertex streams the droplet shader builds its quads from.
        /// </summary>
        static ParticleSystem Particles(string name, Transform parent, Material material, int maxParticles, float gravity)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            // Opened prefab contents live in their own preview scene, but new objects start in the active one.
            if (go.scene != parent.gameObject.scene) SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene);
            go.transform.SetParent(parent, false);
            var particles = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = 0.1f;
            main.startColor = Color.white;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = maxParticles;
            // Always right next to the player; never pause them for being off screen.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.sharedMaterial = material;
            // A few centimetres across: a 2-3 texel caster in the shadow map, which the normal bias erases anyway.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.SetActiveVertexStreams(Streams);
            return particles;
        }

        // No stand-in shader: it would be saved into the material and stay after the real one imports.
        static Material DropletMaterial(string name, bool splat)
            => LoadOrCreate($"{MaterialsFolder}/{name}.mat", () =>
            {
                Shader shader = Shader.Find(DropletShaderName);
                if (shader == null) throw new System.InvalidOperationException($"Shader {DropletShaderName} not found; import Droplet.shader first.");
                var material = new Material(shader);
                // The splat material only switches the shader to rings lying on the surface.
                if (splat) material.SetFloat("_Splat", 1f);
                return material;
            });
    }
}
