using BounJam.Core;
using BounJam.Liquids;
using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Creates the liquid type assets (only when missing, so Inspector tuning is kept) and generates
    /// LiquidStream.prefab, the particle system each liquid sprays from.
    /// </summary>
    static class LiquidPrefabBuilder
    {
        public const string StreamPath = "Assets/_Game/Prefabs/Player/LiquidStream.prefab";
        const string LiquidFolder = "Assets/_Game/Data/Liquids";
        const string ParticleMaterialPath = "Assets/_Game/Art/VFX/Liquid_Particle.mat";
        // URP's transparent, soft round particle material; each liquid tints it through the start colour.
        const string UrpParticleMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

        /// <summary>Water, Sticky and Bouncy, in slot order.</summary>
        public static LiquidType[] EnsureLiquidTypes() => new[]
        {
            Liquid("Water", ("displayName", "Water"), ("kind", LiquidKind.Water),
                ("color", new Color(0.35f, 0.65f, 1f, 0.85f)), ("isInfinite", true), ("flowPerSecond", 1f),
                ("particlesPerSecond", 70f), ("launchSpeed", 15f), ("gravityModifier", 0.6f),
                ("particleSize", 0.24f), ("pushPerUnit", 12f), ("extinguishPower", 1f)),
            Liquid("Sticky", ("displayName", "Sticky Sap"), ("kind", LiquidKind.Sticky),
                ("color", new Color(0.75f, 0.85f, 0.2f, 0.95f)), ("capacity", 5f), ("flowPerSecond", 1f),
                ("particlesPerSecond", 40f), ("launchSpeed", 12f), ("gravityModifier", 0.9f),
                ("particleSize", 0.3f), ("pushPerUnit", 0f)),
            Liquid("Bouncy", ("displayName", "Bouncy Goo"), ("kind", LiquidKind.Bouncy),
                ("color", new Color(1f, 0.4f, 0.8f, 0.9f)), ("capacity", 5f), ("flowPerSecond", 1f),
                ("particlesPerSecond", 50f), ("launchSpeed", 13f), ("gravityModifier", 0.8f),
                ("particleSize", 0.28f), ("pushPerUnit", 0f)),
        };

        static LiquidType Liquid(string assetName, params (string field, object value)[] values)
        {
            string path = $"{LiquidFolder}/{assetName}.asset";
            var liquid = AssetDatabase.LoadAssetAtPath<LiquidType>(path);
            if (liquid != null)
                return liquid;
            liquid = ScriptableObject.CreateInstance<LiquidType>();
            BuilderUtility.SetValues(liquid, values);
            AssetDatabase.CreateAsset(liquid, path);
            return liquid;
        }

        public static LiquidStream BuildStreamPrefab()
        {
            Material material = ParticleMaterial();
            var root = new GameObject("LiquidStream", typeof(ParticleSystem), typeof(LiquidStream));
            try
            {
                var stream = root.GetComponent<ParticleSystem>();
                stream.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = stream.main;
                main.duration = 1f;
                main.loop = true;
                main.playOnAwake = false;
                main.startLifetime = 3f;
                main.startSpeed = 14f;      // LiquidStream.Init overrides speed, size, colour and gravity
                main.startSize = 0.15f;
                main.gravityModifier = 0.6f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 300;

                ParticleSystem.EmissionModule emission = stream.emission;
                emission.enabled = false;
                emission.rateOverTime = 60f;

                ParticleSystem.ShapeModule shape = stream.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 2f;
                shape.radius = 0.03f;

                // One hit per particle: it dies on contact and the splash shows the impact.
                ParticleSystem.CollisionModule collision = stream.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D;
                collision.quality = ParticleSystemCollisionQuality.High; // lower qualities miss moving colliders
                collision.collidesWith = GameLayers.Shootable;
                collision.enableDynamicColliders = true;
                collision.lifetimeLoss = 1f;
                collision.bounce = 0f;
                collision.radiusScale = 0.5f;
                collision.colliderForce = 0f;     // LiquidStream pushes Pushable objects itself
                collision.sendCollisionMessages = true;

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.04f;
                renderer.lengthScale = 1.5f;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                ParticleSystem splash = BuildSplash(root.transform, material);
                ParticleSystem.SubEmittersModule subEmitters = stream.subEmitters;
                subEmitters.enabled = true;
                subEmitters.AddSubEmitter(splash, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritColor);

                return PrefabUtility.SaveAsPrefabAsset(root, StreamPath).GetComponent<LiquidStream>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // A few droplets thrown up wherever a particle lands. Its own collision stays off, so splash
        // droplets never count as liquid hits.
        static ParticleSystem BuildSplash(Transform parent, Material material)
        {
            var go = new GameObject("Splash", typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            var splash = go.GetComponent<ParticleSystem>();
            splash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = splash.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            ParticleSystem.EmissionModule emission = splash.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });

            ParticleSystem.ShapeModule shape = splash.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return splash;
        }

        static Material ParticleMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
            if (material != null)
                return material;
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>(UrpParticleMaterialPath)) { name = "Liquid_Particle" };
            AssetDatabase.CreateAsset(material, ParticleMaterialPath);
            return material;
        }
    }
}
