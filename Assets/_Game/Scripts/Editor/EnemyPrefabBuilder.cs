using BounJam.Core;
using BounJam.Enemies;
using BounJam.Liquids;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace BounJam.EditorTools
{
    /// <summary>Generates FireSoul.prefab. Rebuilding overwrites hand edits made to it.</summary>
    static class EnemyPrefabBuilder
    {
        public const string FireSoulPath = "Assets/_Game/Prefabs/Enemies/FireSoul.prefab";

        [MenuItem("BounJam/Setup/Rebuild Enemy Prefabs")]
        internal static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[BounJam] Exit Play Mode before rebuilding the enemy prefabs.");
                return;
            }
            var root = new GameObject("Fire Soul", typeof(NavMeshAgent), typeof(Rigidbody), typeof(LiquidTarget), typeof(FireSoul));
            try
            {
                FillFireSoul(root);
                PrefabUtility.SaveAsPrefabAsset(root, FireSoulPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log("[BounJam] Built enemy prefabs.");
        }

        // A glowing core trailing flames, a liquid-only hitbox and a slightly larger trigger that catches
        // the beaver. The agent starts disabled; FireSoul enables it once the level's nav mesh is baked.
        static void FillFireSoul(GameObject root)
        {
            root.layer = GameLayers.Enemy;
            var agent = root.GetComponent<NavMeshAgent>();
            agent.radius = 0.4f;
            agent.height = 1.4f;
            agent.acceleration = 20f;
            agent.angularSpeed = 540f;
            agent.stoppingDistance = 0f;
            agent.enabled = false;
            root.GetComponent<Rigidbody>().isKinematic = true; // moved by the agent; lets its trigger notice the beaver

            Material particles = LevelPrefabBuilder.ParticleMaterial();
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            body.localPosition = new Vector3(0f, 0.8f, 0f);
            BuilderUtility.VisualPrimitive(PrimitiveType.Sphere, "Core", body, Vector3.zero, Vector3.one * 0.55f,
                BuilderUtility.UnlitMaterial("FireSoul_Core", new Color(1f, 0.6f, 0.15f)));

            ParticleSystem flames = LevelPrefabBuilder.BuildFlames(body, particles);
            flames.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            ParticleSystem.MainModule main = flames.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World; // trails behind it as it moves
            ParticleSystem.EmissionModule emission = flames.emission;
            emission.rateOverTime = 45f;
            ParticleSystem.ShapeModule shape = flames.shape;
            shape.radius = 0.25f;

            var glow = new GameObject("Glow", typeof(Light));
            glow.transform.SetParent(body, false);
            var light = glow.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.5f, 0.15f);
            light.range = 5f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;

            ParticleSystem steam = LevelPrefabBuilder.BuildSteam(root.transform, particles);

            var hitbox = new GameObject("Hitbox", typeof(CapsuleCollider));
            hitbox.layer = GameLayers.LiquidHitbox;
            hitbox.transform.SetParent(root.transform, false);
            var capsule = hitbox.GetComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.8f, 0f);
            capsule.radius = 0.5f;
            capsule.height = 1.6f;

            var touch = new GameObject("Touch", typeof(SphereCollider));
            touch.layer = GameLayers.Trigger;
            touch.transform.SetParent(root.transform, false);
            var sphere = touch.GetComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.center = new Vector3(0f, 0.8f, 0f);
            sphere.radius = 0.55f;

            BuilderUtility.SetValues(root.GetComponent<FireSoul>(), ("body", body), ("flames", flames), ("steam", steam),
                ("glow", light), ("hitbox", capsule), ("touch", sphere));
        }
    }
}
