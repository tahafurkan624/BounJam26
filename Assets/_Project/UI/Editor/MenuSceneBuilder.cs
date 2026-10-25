using BlobShip.UI.Liquid3D;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Kind = BounJam.LevelPiece.Kind;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Builds the main menu scene (Assets/_Game/Scenes/MainMenu.unity) from code and puts it first in the build: a small
    /// island over the sea with the harbour, a ramp up to a wall for the blob's stunt (<see cref="MenuBlobStunt"/>), the
    /// slowly swinging camera (<see cref="MenuCameraOrbit"/>) and the menu itself on the liquid canvas
    /// (<see cref="MainMenu"/>, <see cref="MenuOptions"/>). Re-run it after changing the layout.
    /// </summary>
    public static partial class GlassUIBuilder
    {
        const string MenuScenePath = "Assets/_Game/Scenes/MainMenu.unity";
        const string WipeMaterialPath = Root + "/Materials/M_UI_LiquidWipe.mat";
        const string MeltMaterialPath = Root + "/Materials/M_UI_ScreenMelt.mat";
        const string LogoPath = Root + "/Icons/Logo_Hangovers.png";
        const string GameTitle = "ShapeLock";
        const string StudioName = "HANGOVERS";

        [MenuItem("BounJam/Build Menu Scene")]
        public static void BuildMenuScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogError($"[BounJam] Menu scene not built: {SceneManager.GetSceneAt(i).path} has unsaved changes.");
                    return;
                }

            SetUpIcons();
            glassMaterial = GlassMaterial();
            display = FontAsset("Fredoka-Variable.ttf", "Fredoka SDF");
            body = FontAsset("Nunito-Variable.ttf", "Nunito SDF");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            LevelKit.SetupEnvironment();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.58f, 0.72f, 0.86f);
            RenderSettings.fogStartDistance = 28f;
            RenderSettings.fogEndDistance = 70f;

            (Transform pivot, MenuBlobStunt stunt) = BuildDiorama();

            Camera main = Camera.main;
            main.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            main.fieldOfView = 40f;
            main.farClipPlane = 200f;
            var orbit = main.gameObject.AddComponent<MenuCameraOrbit>();
            Wire(orbit, ("pivot", pivot));

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            BuildMenu(main);

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            AddToBuildFirst(MenuScenePath);
            Debug.Log($"[BounJam] Menu scene built: {MenuScenePath}");
        }

        static void AddToBuildFirst(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------------------------------------------------------------- Diorama

        static (Transform pivot, MenuBlobStunt stunt) BuildDiorama()
        {
            var level = new GameObject("Level").transform;
            // The stunt runs up the ramp toward +Z: run-up from z -9, ramp foot at z -2.5, top (y 2) at z 2.5, wall at z 3.
            LevelPieces.Create(Kind.Platform, "Island", level, new Vector3(0f, 0f, -4f), Quaternion.identity, new Vector3(10f, 2f, 14f));
            LevelPieces.Create(Kind.Ramp, "Ramp", level, Vector3.zero, Quaternion.identity, new Vector3(2.5f, 2f, 5f));
            LevelPieces.Create(Kind.Platform, "Upper Deck", level, new Vector3(0f, 2f, 5.5f), Quaternion.identity, new Vector3(10f, 4f, 6f));
            LevelPieces.Create(Kind.Wall, "Wall", level, new Vector3(0f, 2f, 3f), Quaternion.identity, new Vector3(4f, 1.5f, 0.5f));
            // Side islands and the harbour around it.
            LevelPieces.Create(Kind.Platform, "Log Island", level, new Vector3(-10f, -0.5f, 2f), Quaternion.identity, new Vector3(6f, 2f, 8f),
                LevelPiece.FloorType.Tropical);
            LevelPieces.Create(Kind.Ramp, "Log Ramp", level, new Vector3(-6f, -0.5f, -3.5f), Quaternion.Euler(0f, 90f, 0f), new Vector3(2f, 0.5f, 2f),
                LevelPiece.FloorType.Tropical);
            LevelPieces.Create(Kind.Platform, "Islet", level, new Vector3(9f, -0.5f, -8f), Quaternion.identity, new Vector3(4f, 2f, 4f));
            LevelPieces.Create(Kind.Platform, "Far Islet", level, new Vector3(-14f, 0.5f, 14f), Quaternion.identity, new Vector3(4f, 3f, 4f));
            var dock = (GameObject)PrefabUtility.InstantiatePrefab(LevelParts.Dock, level);
            dock.transform.SetPositionAndRotation(new Vector3(11f, 0f, 4f), Quaternion.Euler(0f, -90f, 0f));
            var crate = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Level/Crate.prefab"), level);
            crate.transform.position = new Vector3(3.2f, 2f, 5f);
            var sea = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LevelKit.WaterPrefabPath), level);
            sea.name = "Sea";
            sea.transform.position = new Vector3(0f, -1.5f, 0f);
            sea.transform.localScale = new Vector3(90f, 1f, 90f);
            var ship = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LevelKit.ShipPrefabPath), level);
            ship.transform.SetPositionAndRotation(new Vector3(6f, -0.9f, -9f), Quaternion.Euler(0f, 30f, 0f));

            // Invisible rails keep the blob and the rolling crystal on the run-up and the ramp. Ignore Raycast: the bodies
            // hit them, but the jelly skin, the ground probe and the splash droplets don't see them.
            var rails = new GameObject("Stunt Rails").transform;
            rails.SetParent(level, false);
            foreach (float x in new[] { -1.55f, 1.55f })
                Rail(rails, new Vector3(x, 1.5f, -4f), new Vector3(0.3f, 4f, 13.5f));
            Rail(rails, new Vector3(0f, 1f, -10.6f), new Vector3(3.4f, 2f, 0.3f));

            var stuntRoot = new GameObject("Stunt").transform;
            var heading = new GameObject("Heading").transform;
            heading.SetParent(stuntRoot, false);
            var start = new GameObject("Start").transform;
            start.SetParent(stuntRoot, false);
            start.position = new Vector3(0f, LevelKit.CoreRadius, -8f);
            var wallTarget = new GameObject("Wall Target").transform;
            wallTarget.SetParent(stuntRoot, false);
            wallTarget.position = new Vector3(0f, 2.2f, 3f);
            var pivot = new GameObject("Camera Pivot").transform;
            pivot.SetParent(stuntRoot, false);
            pivot.position = new Vector3(0.5f, 0.8f, -2.5f);

            var blob = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LevelKit.BlobPrefabPath));
            blob.transform.position = start.position;
            Wire(blob.GetComponent<BlobMotor>(), ("cameraTransform", heading));
            var input = blob.GetComponent<PlayerInputReader>();
            input.enabled = false;
            var stunt = stuntRoot.gameObject.AddComponent<MenuBlobStunt>();
            Wire(stunt, ("input", input), ("motor", blob.GetComponent<BlobMotor>()), ("form", blob.GetComponent<BlobForm>()),
                ("skin", blob.GetComponent<BlobSkin>()), ("split", blob.GetComponent<BlobSplit>()), ("guard", blob.GetComponent<GroundGuard>()),
                ("heading", heading), ("start", start), ("wall", wallTarget));
            return (pivot, stunt);
        }

        static void Rail(Transform parent, Vector3 center, Vector3 size)
        {
            var rail = new GameObject("Rail", typeof(BoxCollider)) { layer = LayerMask.NameToLayer("Ignore Raycast") };
            rail.transform.SetParent(parent, false);
            rail.transform.position = center;
            rail.GetComponent<BoxCollider>().size = size;
        }

        // ---------------------------------------------------------------- Menu

        static void BuildMenu(Camera main)
        {
            var root = new GameObject("MenuUI");
            root.layer = LayerMask.NameToLayer("UI");
            Canvas liquid = MakeCanvas("Liquid3D", root.transform, RenderMode.ScreenSpaceCamera, LiquidSorting);
            liquid.worldCamera = main;
            liquid.planeDistance = 0.5f;
            Canvas effects = MakeCanvas("Effects", root.transform, RenderMode.ScreenSpaceOverlay, 100);
            Object.DestroyImmediate(effects.GetComponent<GraphicRaycaster>());

            // Title, Play, Options, Exit and the studio.
            RectTransform main3D = Node("Main", liquid.transform, typeof(CanvasGroup));
            Stretch(main3D);

            TMP_Text title = Led(main3D, "Title", GameTitle, display, 120f, Vector2.zero, new Vector2(1200f, 150f));
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1200f, 150f));

            RectTransform playRect = Node("PlayButton", main3D);
            Place(playRect, new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(440f, 140f));
            Button play = LiquidButton(playRect, "Play", "play", 0);
            play.transform.Find("Label").GetComponent<TMP_Text>().fontSize = 58f;
            Place(play.transform.Find("Icon").GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(-150f, 0f), new Vector2(46f, 46f));
            playRect.gameObject.AddComponent<LiquidButtonPulse>();

            RectTransform optionsRect = Node("OptionsButton", main3D);
            Place(optionsRect, new Vector2(1f, 1f), new Vector2(-56f, -50f), new Vector2(120f, 120f));
            Button optionsButton = LiquidButton(optionsRect, null, "gear", 1);
            Place(optionsButton.transform.Find("Icon").GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58f, 58f));

            RectTransform exitRect = Node("ExitButton", main3D);
            Place(exitRect, new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(300f, 96f));
            Button exit = LiquidButton(exitRect, "Exit", "exit", 2);
            exitRect.gameObject.AddComponent<LiquidHoverTint>();

            RectTransform studio = Node("Studio", main3D);
            Place(studio, new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(420f, 150f));
            var logo = Node("Logo", studio).gameObject.AddComponent<Image>();
            logo.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
            logo.preserveAspect = true;
            logo.raycastTarget = false;
            Place(logo.rectTransform, new Vector2(0f, 0f), Vector2.zero, new Vector2(84f, 150f));
            TMP_Text kicker = Kicker(studio, "a game by", 15f);
            kicker.color = new Color(1f, 1f, 1f, 0.75f);
            Place(kicker.rectTransform, new Vector2(0f, 0f), new Vector2(100f, 82f), new Vector2(300f, 24f));
            TMP_Text name = Label(studio, "Name", StudioName, display, 40f, Color.white);
            name.characterSpacing = 6f;
            Place(name.rectTransform, new Vector2(0f, 0f), new Vector2(100f, 34f), new Vector2(320f, 50f));
            TMP_Text sub = Kicker(studio, "game studio", 14f);
            sub.color = new Color(1f, 1f, 1f, 0.75f);
            Place(sub.rectTransform, new Vector2(0f, 0f), new Vector2(100f, 12f), new Vector2(300f, 22f));

            MenuOptions options = BuildOptions(liquid.transform);

            // Full-screen effects over everything: the Play wipe and the Exit melt.
            RawImage wipe = FullScreen(effects.transform, "Wipe", MaterialFor(WipeMaterialPath, "BounJam/UI/Liquid Wipe"));
            RawImage melt = FullScreen(effects.transform, "ExitMelt", MaterialFor(MeltMaterialPath, "BounJam/UI/Screen Melt"));
            var screenMelt = melt.gameObject.AddComponent<ScreenMelt>();

            var menu = root.AddComponent<MainMenu>();
            Wire(menu, ("liquidCanvas", liquid), ("mainGroup", main3D.GetComponent<CanvasGroup>()), ("playButton", play),
                ("optionsButton", optionsButton), ("exitButton", exit), ("options", options), ("exitMelt", screenMelt), ("wipe", wipe));
        }

        static MenuOptions BuildOptions(Transform parent)
        {
            RectTransform optionsRoot = Node("Options", parent, typeof(CanvasGroup));
            Stretch(optionsRoot);
            Image backdrop = Backdrop(optionsRoot, new Color(0.12f, 0.16f, 0.2f, 0.7f));
            RectTransform tilt = Tilt(optionsRoot);

            RectTransform panelRect = Node("Panel", tilt);
            Place(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(600f, 700f));
            LiquidSlab panel = GlassPanel(panelRect);

            TMP_Text title = Led(panelRect, "Title", "Options", display, 52f, new Vector2(0f, 290f), new Vector2(520f, 66f));
            TMP_Text musicName = Led(panelRect, "MusicName", "Music", body, 24f, new Vector2(-115f, 200f), new Vector2(240f, 34f), 0.9f,
                TextAlignmentOptions.Left);
            TMP_Text musicValue = Led(panelRect, "MusicValue", "100%", body, 24f, new Vector2(115f, 200f), new Vector2(240f, 34f), 0.9f,
                TextAlignmentOptions.Right);
            (Slider music, Graphic[] musicParts) = MakeSlider(panelRect, new Vector2(0f, 154f), new Vector2(480f, 36f), panel);
            music.name = "MusicSlider";
            TMP_Text sfxName = Led(panelRect, "SfxName", "Sound effects", body, 24f, new Vector2(-115f, 86f), new Vector2(240f, 34f), 0.9f,
                TextAlignmentOptions.Left);
            TMP_Text sfxValue = Led(panelRect, "SfxValue", "100%", body, 24f, new Vector2(115f, 86f), new Vector2(240f, 34f), 0.9f,
                TextAlignmentOptions.Right);
            (Slider sfx, Graphic[] sfxParts) = MakeSlider(panelRect, new Vector2(0f, 40f), new Vector2(480f, 36f), panel);
            sfx.name = "SfxSlider";

            Button reset = LiquidButton(ButtonNode(panelRect, "ResetButton", new Vector2(0f, -90f), new Vector2(480f, 96f)), "Reset progress", "restart", 2);
            Button back = LiquidButton(ButtonNode(panelRect, "BackButton", new Vector2(0f, -220f), new Vector2(300f, 96f)), "Back", "arrow", 0);
            back.transform.Find("Icon").localEulerAngles = new Vector3(0f, 0f, 180f);

            var labels = new System.Collections.Generic.List<Graphic> { title, musicName, musicValue, sfxName, sfxValue };
            panel.autoFindLabels = false;
            panel.labels = labels;
            LiquidPanel3D glass = Panel3D(panel);
            glass.fadeWith = new System.Collections.Generic.List<Graphic>(labels);
            glass.fadeWith.AddRange(musicParts);
            glass.fadeWith.AddRange(sfxParts);
            panelRect.gameObject.SetActive(false);

            var component = optionsRoot.gameObject.AddComponent<MenuOptions>();
            Wire(component, ("panel", glass), ("backdrop", backdrop), ("music", music), ("musicValue", musicValue), ("sfx", sfx),
                ("sfxValue", sfxValue), ("resetButton", reset), ("resetLabel", reset.transform.Find("Label").GetComponent<TMP_Text>()),
                ("backButton", back));
            return component;
        }

        static RawImage FullScreen(Transform parent, string name, Material material)
        {
            RectTransform rect = Node(name, parent);
            Stretch(rect);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.material = material;
            image.raycastTarget = false;
            return image;
        }

        static Material MaterialFor(string path, string shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
