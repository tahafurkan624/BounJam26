using BounJam.Core;
using BounJam.Level;
using BounJam.Liquids;
using BounJam.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Generates the level building blocks: Fire.prefab and Level.prefab (LevelManager, fire counter, end
    /// screen and EventSystem; one per level). Rebuilding overwrites hand edits made to those prefabs.
    /// </summary>
    static class LevelPrefabBuilder
    {
        public const string FirePath = "Assets/_Game/Prefabs/Level/Fire.prefab";
        public const string LevelPath = "Assets/_Game/Prefabs/Level/Level.prefab";
        public const string TreePath = "Assets/_Game/Prefabs/Level/Tree.prefab";
        public const string PitPath = "Assets/_Game/Prefabs/Level/Pit.prefab";
        public const string CrumblingPath = "Assets/_Game/Prefabs/Level/CrumblingGround.prefab";
        public const string GroundPath = "Assets/_Game/Prefabs/Level/GroundBlock.prefab";
        const string ParticleMaterialPath = "Assets/_Game/Art/VFX/Fire_Particle.mat";
        const string UrpParticleMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

        [MenuItem("BounJam/Setup/Rebuild Level Prefabs")]
        internal static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[BounJam] Exit Play Mode before rebuilding the level prefabs.");
                return;
            }
            if (!ProjectSetup.TextMeshProReady)
            {
                Debug.LogError("[BounJam] Import TextMeshPro essentials first (BounJam > Setup).");
                return;
            }
            BuildPrefab(FirePath, new GameObject("Fire", typeof(LiquidTarget), typeof(Fire)), FillFire);
            BuildPrefab(LevelPath, new GameObject("Level", typeof(LevelManager)), FillLevel);
            BuildPrefab(TreePath, new GameObject("Tree", typeof(ChewableTree)), FillTree);
            BuildPrefab(PitPath, new GameObject("Pit", typeof(BoxCollider), typeof(KillZone)), FillPit);
            BuildPrefab(CrumblingPath, new GameObject("Crumbling Ground", typeof(BoxCollider), typeof(CrumblingGround)), FillCrumbling);
            BuildPrefab(GroundPath, new GameObject("Ground Block"), FillGround);
            Debug.Log("[BounJam] Built level prefabs.");
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

        // Crossed logs (solid, so the beaver can't walk through), flames, steam, a flickering light and a
        // liquid-only hitbox a little larger than the flames.
        static void FillFire(GameObject root)
        {
            Material particles = ParticleMaterial();
            Material bark = BuilderUtility.FlatMaterial("Greybox_Logs", new Color(0.35f, 0.22f, 0.12f));
            for (int i = 0; i < 2; i++)
            {
                GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                log.name = "Log";
                log.layer = GameLayers.Ground;
                log.transform.SetParent(root.transform, false);
                log.transform.localPosition = new Vector3(0f, 0.15f + 0.1f * i, 0f);
                log.transform.localRotation = Quaternion.Euler(0f, 45f + 90f * i, 90f);
                log.transform.localScale = new Vector3(0.3f, 0.5f, 0.3f);
                log.GetComponent<MeshRenderer>().sharedMaterial = bark;
            }

            var hitbox = new GameObject("Hitbox", typeof(CapsuleCollider));
            hitbox.layer = GameLayers.LiquidHitbox;
            hitbox.transform.SetParent(root.transform, false);
            var capsule = hitbox.GetComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.8f, 0f);
            capsule.radius = 0.6f;
            capsule.height = 1.8f;

            var glow = new GameObject("Glow", typeof(Light));
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            var light = glow.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 6f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;

            BuilderUtility.SetValues(root.GetComponent<Fire>(),
                ("flames", BuildFlames(root.transform, particles)), ("steam", BuildSteam(root.transform, particles)),
                ("glow", light), ("hitbox", capsule));
        }

        // A stump that stays, and a trunk with a round canopy that falls; everything solid is on the Tree
        // layer so standing and fallen trees block liquid. The trunk's origin is the centre of the cut.
        static void FillTree(GameObject root)
        {
            const float stumpHeight = 0.3f, trunkRadius = 0.25f, logLength = 3.4f, canopyRadius = 1.2f;
            Material bark = BuilderUtility.FlatMaterial("Greybox_Logs", new Color(0.35f, 0.22f, 0.12f));
            Material leaves = BuilderUtility.FlatMaterial("Greybox_Leaves", new Color(0.2f, 0.45f, 0.18f));

            TreePart(PrimitiveType.Cylinder, "Stump", root.transform, new Vector3(0f, stumpHeight / 2f, 0f),
                new Vector3(trunkRadius * 2f, stumpHeight / 2f, trunkRadius * 2f), bark);
            var trunk = new GameObject("Trunk").transform;
            trunk.SetParent(root.transform, false);
            trunk.localPosition = new Vector3(0f, stumpHeight, 0f);
            TreePart(PrimitiveType.Cylinder, "Log", trunk, new Vector3(0f, logLength / 2f, 0f),
                new Vector3(trunkRadius * 2f, logLength / 2f, trunkRadius * 2f), bark);
            float canopyHeight = logLength + canopyRadius * 0.25f;
            TreePart(PrimitiveType.Sphere, "Canopy", trunk, new Vector3(0f, canopyHeight, 0f), Vector3.one * canopyRadius * 2f, leaves);

            ParticleSystem chips = NewParticles("Chips", root.transform, new Vector3(0f, stumpHeight, 0f), ParticleMaterial());
            ParticleSystem.MainModule main = chips.main;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.55f, 0.3f), new Color(0.5f, 0.32f, 0.15f));
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 100;
            ParticleSystem.EmissionModule emission = chips.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = chips.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = trunkRadius;

            // At rest the canopy should touch the ground: cos(angle) = (canopy radius - cut height) / canopy distance.
            float fallAngle = Mathf.Acos((canopyRadius - stumpHeight) / canopyHeight) * Mathf.Rad2Deg;
            BuilderUtility.SetValues(root.GetComponent<ChewableTree>(),
                ("trunk", trunk), ("trunkRadius", trunkRadius), ("fallAngle", fallAngle), ("chips", chips));
        }

        // An invisible 6 x 2 x 6 m kill volume hanging below its origin: place the origin about 2 m under
        // the gap in the ground and scale it to cover the gap.
        static void FillPit(GameObject root)
        {
            root.layer = GameLayers.Trigger;
            var box = root.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, -1f, 0f);
            box.size = new Vector3(6f, 2f, 6f);

            // Unlit black just under the ground, so the pit reads as a deep hole instead of showing the sky.
            GameObject darkness = BuilderUtility.VisualPrimitive(PrimitiveType.Quad, "Darkness", root.transform,
                new Vector3(0f, 0.8f, 0f), new Vector3(6f, 6f, 1f), AbyssMaterial());
            darkness.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // A 2 x 2 m slab whose top sits at the origin, with a trigger just above it to notice the beaver.
        static void FillCrumbling(GameObject root)
        {
            root.layer = GameLayers.Trigger;
            var trigger = root.GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 0.25f, 0f);
            trigger.size = new Vector3(1.9f, 0.5f, 1.9f);

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            slab.layer = GameLayers.Ground;
            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            slab.transform.localScale = new Vector3(2f, 0.3f, 2f);
            slab.GetComponent<MeshRenderer>().sharedMaterial =
                BuilderUtility.FlatMaterial("Greybox_Crumbling", new Color(0.6f, 0.5f, 0.4f));
            BuilderUtility.SetValues(root.GetComponent<CrumblingGround>(), ("slab", slab.transform));
        }

        // A 4 x 1 x 4 m floor piece whose top sits at the origin, for building levels with gaps in them.
        static void FillGround(GameObject root)
        {
            root.layer = GameLayers.Ground;
            root.isStatic = true;
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Block";
            block.layer = GameLayers.Ground;
            block.isStatic = true;
            block.transform.SetParent(root.transform, false);
            block.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            block.transform.localScale = new Vector3(4f, 1f, 4f);
            block.GetComponent<MeshRenderer>().sharedMaterial =
                BuilderUtility.FlatMaterial("Greybox_Ground", new Color(0.42f, 0.55f, 0.36f));
        }

        static Material AbyssMaterial()
        {
            const string path = "Assets/_Game/Art/Materials/Abyss.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Abyss" };
            material.SetColor("_BaseColor", Color.black);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void TreePart(PrimitiveType shape, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.layer = GameLayers.Tree;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static ParticleSystem BuildFlames(Transform parent, Material material)
        {
            ParticleSystem flames = NewParticles("Flames", parent, new Vector3(0f, 0.2f, 0f), material);
            ParticleSystem.MainModule main = flames.main;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.gravityModifier = -0.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 100;

            ParticleSystem.EmissionModule emission = flames.emission;
            emission.rateOverTime = 35f;
            ParticleSystem.ShapeModule shape = flames.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;
            shape.radius = 0.3f;

            ParticleSystem.ColorOverLifetimeModule color = flames.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(
                new[] { (0f, new Color(1f, 0.9f, 0.4f)), (0.4f, new Color(1f, 0.5f, 0.1f)), (1f, new Color(0.6f, 0.12f, 0.05f)) },
                new[] { (0f, 0f), (0.15f, 0.9f), (0.6f, 0.6f), (1f, 0f) });
            ParticleSystem.SizeOverLifetimeModule size = flames.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));
            return flames;
        }

        // Driven by Fire.Emit: a puff where water lands and a big cloud when the fire goes out.
        static ParticleSystem BuildSteam(Transform parent, Material material)
        {
            ParticleSystem steam = NewParticles("Steam", parent, new Vector3(0f, 0.6f, 0f), material);
            ParticleSystem.MainModule main = steam.main;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startColor = new Color(0.92f, 0.92f, 0.95f, 0.45f);
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            ParticleSystem.EmissionModule emission = steam.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = steam.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.2f;

            ParticleSystem.ColorOverLifetimeModule color = steam.colorOverLifetime;
            color.enabled = true;
            color.color = FadeGradient(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 1f), (1f, 0f) });
            ParticleSystem.SizeOverLifetimeModule size = steam.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1.5f, AnimationCurve.Linear(0f, 0.5f, 1f, 1f));
            return steam;
        }

        // A looping system pointing up, with shared settings; callers fill in the rest.
        static ParticleSystem NewParticles(string name, Transform parent, Vector3 localPosition, Material material)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone shapes emit along +Z
            var particles = go.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.duration = 1f;
            main.loop = true;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        static ParticleSystem.MinMaxGradient FadeGradient((float time, Color color)[] colors, (float time, float alpha)[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                System.Array.ConvertAll(colors, key => new GradientColorKey(key.color, key.time)),
                System.Array.ConvertAll(alphas, key => new GradientAlphaKey(key.alpha, key.time)));
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        static Material ParticleMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
            if (material != null)
                return material;
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>(UrpParticleMaterialPath)) { name = "Fire_Particle" };
            AssetDatabase.CreateAsset(material, ParticleMaterialPath);
            return material;
        }

        static void FillLevel(GameObject root)
        {
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(root.transform, false);

            var canvas = new GameObject("Level UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(root.transform, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder = 10; // above the player's HUD
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            TMP_Text counter = BuilderUtility.UiLabel("Fire Counter", canvas.transform, "Fires left: 0", 34f, TextAlignmentOptions.Top);
            RectTransform counterRect = counter.rectTransform;
            counterRect.anchorMin = new Vector2(0f, 1f);
            counterRect.anchorMax = Vector2.one;
            counterRect.pivot = new Vector2(0.5f, 1f);
            counterRect.sizeDelta = new Vector2(0f, 60f);
            counterRect.anchoredPosition = new Vector2(0f, -24f);

            RectTransform endScreen = BuilderUtility.UiObject("End Screen", canvas.transform);
            BuilderUtility.Stretch(endScreen, 0f);
            BuilderUtility.UiImage(endScreen, new Color(0f, 0f, 0f, 0.6f));
            TMP_Text title = CentredLabel("Title", endScreen, "Level Complete!", 72f, new Vector2(0f, 120f));
            TMP_Text message = CentredLabel("Message", endScreen, "", 36f, new Vector2(0f, 30f));
            // A layout row, so the restart button centres itself when Next Level is hidden.
            RectTransform buttons = BuilderUtility.UiObject("Buttons", endScreen);
            buttons.sizeDelta = new Vector2(800f, 64f);
            buttons.anchoredPosition = new Vector2(0f, -80f);
            var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 40f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = false;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            Button restart = UiButton("Restart Button", buttons, "Restart (R)");
            Button next = UiButton("Next Button", buttons, "Next Level");

            BuilderUtility.SetValues(canvas.AddComponent<LevelHud>(),
                ("level", root.GetComponent<LevelManager>()), ("fireCounter", counter), ("endScreen", endScreen.gameObject),
                ("endTitle", title), ("endMessage", message), ("restartButton", restart), ("nextButton", next));
            BuilderUtility.SetLayerRecursively(canvas, LayerMask.NameToLayer("UI"));
        }

        static TMP_Text CentredLabel(string name, Transform parent, string text, float size, Vector2 position)
        {
            TMP_Text label = BuilderUtility.UiLabel(name, parent, text, size, TextAlignmentOptions.Center);
            label.rectTransform.sizeDelta = new Vector2(1400f, size * 1.6f);
            label.rectTransform.anchoredPosition = position;
            return label;
        }

        static Button UiButton(string name, Transform parent, string text)
        {
            RectTransform rect = BuilderUtility.UiObject(name, parent);
            rect.sizeDelta = new Vector2(260f, 64f);
            Image background = BuilderUtility.UiImage(rect, new Color(1f, 1f, 1f, 0.92f));
            background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            TMP_Text label = BuilderUtility.UiLabel("Label", rect, text, 30f, TextAlignmentOptions.Center);
            label.color = new Color(0.12f, 0.12f, 0.15f);
            BuilderUtility.Stretch(label.rectTransform, 0f);
            return button;
        }
    }
}
