using System.Collections.Generic;
using System.IO;
using BlobShip.UI;
using BlobShip.UI.Liquid3D;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Builds the Liquid Glass UI prefab (Assets/_Project/UI/Resources/GlassUI.prefab) from code, together with what it
    /// needs: the TMP font assets made from the bundled Fredoka / Nunito fonts, the icons set up as sprites and the glass
    /// material. GlassUI loads the prefab by itself in every level, so re-running this after a layout change is all
    /// it takes. Layout is at the 1920×1080 reference resolution of the game's canvases.
    /// The prefab has two canvases. The overlay canvas holds the flat glass: HUD pills and toasts, moving like liquid
    /// through the BlobShip Liquid UI components (Assets/Assets/BlobShip/UI). The liquid canvas holds the 3D liquid
    /// kit (Assets/_Project/Art/UI/Liquid3D): thick turquoise liquid buttons with white LED labels, glass panels that
    /// pour in and drain away, and the Liquid / Solid switch. A slab keeps its labels in front of its body only when
    /// they are its direct children, so those panels are laid out by hand, not with layout groups.
    /// </summary>
    public static class GlassUIBuilder
    {
        const string Root = "Assets/_Project/UI";
        const string PrefabPath = Root + "/Resources/GlassUI.prefab";
        const string MaterialPath = Root + "/Materials/M_UI_LiquidGlass.mat";
        const string Charset =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "·×–—…’“”çÇğĞıİöÖşŞüÜ";
        // Liquid canvas draw order: its own images, then glass panels (+1), the buttons on them (+2), labels (+3).
        const int LiquidSorting = 10;

        static readonly Color Ink = Hex("0B2233");
        static readonly Color Muted = Hex("22475C");
        static readonly Color Teal = Hex("00695F");
        static readonly Color Mint = Hex("1FC9AE");
        static readonly Color Aqua = Hex("46E6CF");
        static readonly Color Orange = Hex("FF8A1F");
        static readonly Color TrackFill = new Color(0.043f, 0.133f, 0.2f, 0.16f);
        static readonly Color LiquidTrack = new Color(0.55f, 0.95f, 1f, 0.22f);
        static readonly Color SmokedGlass = new Color(0.4f, 0.52f, 0.58f);

        static Material glassMaterial;
        static TMP_FontAsset display;
        static TMP_FontAsset body;

        [MenuItem("Tools/BounJam/Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            if (AssetDatabase.IsValidFolder("Assets/TextMesh Pro")) return;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage"), false);
        }

        [MenuItem("Tools/BounJam/Build Glass UI")]
        public static void Build()
        {
            SetUpIcons();
            glassMaterial = GlassMaterial();
            display = FontAsset("Fredoka-Variable.ttf", "Fredoka SDF");
            body = FontAsset("Nunito-Variable.ttf", "Nunito SDF");
            // Fredoka has no ğ, İ or ş; Nunito fills them in.
            if (!display.fallbackFontAssetTable.Contains(body)) display.fallbackFontAssetTable.Add(body);
            // The variable fonts load at their regular instance; the mockups use 600-800, so bold is thickened in the SDF.
            Weight(display, 1.7f);
            Weight(body, 1.6f);

            var root = new GameObject("GlassUI");
            root.layer = LayerMask.NameToLayer("UI");
            Canvas overlay = MakeCanvas("Overlay", root.transform, RenderMode.ScreenSpaceOverlay, 10);
            // GlassUI puts it on the main camera at run time.
            Canvas liquid = MakeCanvas("Liquid3D", root.transform, RenderMode.ScreenSpaceCamera, LiquidSorting);

            GlassHud hud = BuildHud(overlay.transform, liquid.transform);
            GlassToast title = BuildToast(overlay.transform, "TitleToast", new Vector2(0.5f, 1f), new Vector2(0f, -170f), 43f, 40f);
            GlassToast message = BuildToast(overlay.transform, "MessageToast", new Vector2(0.5f, 0.5f), Vector2.zero, 58f, 52f);
            GlassPauseMenu pause = BuildPauseMenu(liquid.transform);
            GlassCompleteCard complete = BuildCompleteCard(liquid.transform);

            // Toasts start hidden, as their components hide them on Awake; this keeps the prefab readable.
            foreach (Component toast in new Component[] { title, message })
            {
                var group = toast.GetComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = group.blocksRaycasts = false;
            }

            var ui = root.AddComponent<GlassUI>();
            Wire(ui, ("overlayCanvas", overlay), ("liquidCanvas", liquid), ("hud", hud), ("titleToast", title), ("messageToast", message),
                ("pauseMenu", pause), ("completeCard", complete));

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"Glass UI built: {PrefabPath}");
        }

        static Canvas MakeCanvas(string name, Transform parent, RenderMode mode, int sorting)
        {
            RectTransform rect = Node(name, parent, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = rect.GetComponent<Canvas>();
            canvas.renderMode = mode;
            canvas.sortingOrder = sorting;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = rect.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        // ---------------------------------------------------------------- HUD

        static GlassHud BuildHud(Transform overlay, Transform liquid)
        {
            RectTransform hudRoot = Node("HUD", overlay, typeof(CanvasGroup));
            Stretch(hudRoot);
            var hud = hudRoot.gameObject.AddComponent<GlassHud>();

            // Level pill, top left.
            RectTransform levelPill = Node("LevelPill", hudRoot);
            Place(levelPill, new Vector2(0f, 1f), new Vector2(48f, -43f), Vector2.zero);
            Glass(levelPill, 34f);
            Row(levelPill, 17f, new RectOffset(18, 30, 17, 17), TextAnchor.MiddleLeft);
            Fit(levelPill, true, true);
            RectTransform disc = Node("Disc", levelPill);
            Solid(disc, 26.5f, Ink);
            Size(disc, 53f, 53f);
            Icon(disc, "flag", 28f, Orange);
            RectTransform levelText = Node("Text", levelPill);
            Column(levelText, 3f, TextAnchor.MiddleLeft);
            TMP_Text levelKicker = Kicker(levelText, "Level", 15.6f);
            TMP_Text levelLabel = Label(levelText, "Title", "Level", display, 29f, Ink);

            // Cargo counter, top centre.
            RectTransform cargo = Node("CargoPill", hudRoot);
            Place(cargo, new Vector2(0.5f, 1f), new Vector2(0f, -43f), new Vector2(456f, 90f));
            Glass(cargo, 41f);
            RectTransform cargoIcon = Icon(cargo, "drop", 0f, Aqua).rectTransform;
            Place(cargoIcon, new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(42f, 48f));
            TMP_Text count = Label(cargo, "Count", "0 / 0", display, 34f, Ink);
            Place(count.rectTransform, new Vector2(0f, 0.5f), new Vector2(82f, 14f), new Vector2(220f, 40f));
            TMP_Text cargoName = Label(cargo, "Name", "Cargo", body, 18f, Teal, TextAlignmentOptions.Right);
            Place(cargoName.rectTransform, new Vector2(1f, 0.5f), new Vector2(-28f, 14f), new Vector2(160f, 30f));
            RectTransform track = Node("Track", cargo);
            track.anchorMin = new Vector2(0f, 0.5f);
            track.anchorMax = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(-(82f + 28f), 17f);
            track.anchoredPosition = new Vector2((82f - 28f) * 0.5f, -17f);
            Solid(track, 8.5f, TrackFill, 0.4f);
            RectTransform fill = Node("Fill", track);
            Stretch(fill);
            fill.anchorMax = new Vector2(0.6f, 1f);
            Solid(fill, 8.5f, Mint);
            // Bubbles drift through the filled part; kept clear of the rounded ends.
            fill.gameObject.AddComponent<RectMask2D>();
            RectTransform fillBubbles = Node("Bubbles", fill);
            Stretch(fillBubbles);
            fillBubbles.offsetMin = new Vector2(6f, 0f);
            fillBubbles.offsetMax = new Vector2(-6f, 0f);
            Particles(fillBubbles, LiquidPreset.BarBubbles);

            RectTransform hint = Node("Hint", hudRoot);
            Place(hint, new Vector2(0.5f, 0f), new Vector2(0f, 44f), Vector2.zero);
            Glass(hint, 17f, Color.white, 0.32f, 0.12f);
            Row(hint, 0f, new RectOffset(17, 17, 7, 8), TextAnchor.MiddleCenter);
            Fit(hint, true, true);
            TMP_Text hintLabel = Label(hint, "Text", "E  ·  switch form", body, 17f, Muted);

            // The 3D half: liquid pause button, top right, and the Liquid / Solid switch, bottom centre.
            RectTransform liquidRoot = Node("HUD3D", liquid, typeof(CanvasGroup));
            Stretch(liquidRoot);

            RectTransform pauseRect = Node("PauseButton", liquidRoot);
            Place(pauseRect, new Vector2(1f, 1f), new Vector2(-48f, -43f), new Vector2(90f, 90f));
            Button pauseButton = LiquidButton(pauseRect, null, "pause", 1);

            RectTransform switchRect = Node("FormSwitch", liquidRoot);
            Place(switchRect, new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(400f, 100f));
            switchRect.gameObject.AddComponent<LiquidRaycastTarget>();
            var switchSlab = switchRect.gameObject.AddComponent<LiquidSlab>();
            switchSlab.colors = LiquidPalette.Turquoise;
            switchSlab.glassMilk = new Color(0.035f, 0.06f, 0.08f, 0f);
            switchSlab.glassTint = SmokedGlass;
            switchSlab.refraction = 0.026f;
            switchSlab.iridescence = 0.4f;
            switchSlab.sortingOffset = 2;
            var formSwitch = switchRect.gameObject.AddComponent<LiquidToggle>();
            formSwitch.liquidLabel = Led(switchRect, "Liquid", "Liquid", body, 30f, new Vector2(-92f, 0f), new Vector2(180f, 80f));
            formSwitch.solidLabel = Led(switchRect, "Solid", "Solid", body, 30f, new Vector2(92f, 0f), new Vector2(180f, 80f));
            switchSlab.FindLabels();

            Wire(hud, ("group", hudRoot.GetComponent<CanvasGroup>()), ("liquidGroup", liquidRoot.GetComponent<CanvasGroup>()),
                ("levelKicker", levelKicker), ("levelLabel", levelLabel), ("cargoPill", cargo.gameObject), ("cargoIcon", cargoIcon),
                ("cargoCount", count), ("cargoFill", fill), ("formSwitch", formSwitch), ("formHint", hintLabel), ("pauseButton", pauseButton));
            return hud;
        }

        static GlassToast BuildToast(Transform parent, string name, Vector2 anchor, Vector2 position, float fontSize, float radius)
        {
            RectTransform toast = Node(name, parent, typeof(CanvasGroup));
            Place(toast, anchor, position, Vector2.zero);
            Glass(toast, radius, Color.white, 0.36f, 0.26f);
            int padX = Mathf.RoundToInt(fontSize * 0.85f);
            int padY = Mathf.RoundToInt(fontSize * 0.42f);
            Row(toast, 0f, new RectOffset(padX, padX, padY, padY), TextAnchor.MiddleCenter);
            Fit(toast, true, true);
            TMP_Text text = Label(toast, "Text", name, display, fontSize, Ink, TextAlignmentOptions.Center);
            var component = toast.gameObject.AddComponent<GlassToast>();
            Wire(component, ("label", text));
            return component;
        }

        // ---------------------------------------------------------------- Pause menu

        static GlassPauseMenu BuildPauseMenu(Transform parent)
        {
            RectTransform menuRoot = Node("PauseMenu", parent, typeof(CanvasGroup));
            Stretch(menuRoot);
            // Dims the level about as much as the mockup's game backdrop (the UI draws before tone mapping).
            Image backdrop = Backdrop(menuRoot, new Color(0.12f, 0.16f, 0.2f, 0.86f));
            RectTransform tilt = Tilt(menuRoot);

            RectTransform panelRect = Node("Panel", tilt);
            Place(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(560f, 700f));
            LiquidSlab panel = GlassPanel(panelRect);

            TMP_Text title = Led(panelRect, "Title", "Paused", display, 52f, new Vector2(0f, 280f), new Vector2(520f, 66f));
            TMP_Text subtitle = Led(panelRect, "Subtitle", "Level", body, 20f, new Vector2(0f, 234f), new Vector2(520f, 32f), 0.6f);

            var size = new Vector2(450f, 96f);
            Button resume = LiquidButton(ButtonNode(panelRect, "Resume", new Vector2(0f, 120f), size), "Resume", "play", 0);
            Button restart = LiquidButton(ButtonNode(panelRect, "Restart level", Vector2.zero, size), "Restart level", "restart", 1);
            Button quit = LiquidButton(ButtonNode(panelRect, "Quit game", new Vector2(0f, -120f), size), "Quit game", "exit", 2);

            TMP_Text volumeName = Led(panelRect, "VolumeName", "Volume", body, 22f, new Vector2(-115f, -226f), new Vector2(220f, 34f), 0.85f,
                TextAlignmentOptions.Left);
            TMP_Text volumeValue = Led(panelRect, "VolumeValue", "100%", body, 22f, new Vector2(115f, -226f), new Vector2(220f, 34f), 0.85f,
                TextAlignmentOptions.Right);
            (Slider volume, Graphic[] sliderParts) = MakeSlider(panelRect, new Vector2(0f, -274f), new Vector2(450f, 36f), panel);

            // The slider has its own canvas (it would sort apart as labels), so the panel's labels are set here.
            var labels = new List<Graphic> { title, subtitle, volumeName, volumeValue };
            panel.autoFindLabels = false;
            panel.labels = labels;
            LiquidPanel3D glass = Panel3D(panel);
            glass.fadeWith = new List<Graphic>(labels);
            glass.fadeWith.AddRange(sliderParts);
            panelRect.gameObject.SetActive(false);

            var menu = menuRoot.gameObject.AddComponent<GlassPauseMenu>();
            Wire(menu, ("panel", glass), ("backdrop", backdrop), ("subtitle", subtitle), ("resumeButton", resume), ("restartButton", restart),
                ("quitButton", quit), ("volume", volume), ("volumeValue", volumeValue));
            return menu;
        }

        /// <summary>
        /// The volume slider, built from flat shapes in front of a glass panel. It has a canvas of its own, drawn after
        /// the panel, so its parts keep their order; it is set just in front of the panel's face.
        /// </summary>
        static (Slider slider, Graphic[] parts) MakeSlider(RectTransform parent, Vector2 position, Vector2 size, LiquidSlab panel)
        {
            RectTransform rect = Node("VolumeSlider", parent, typeof(Canvas), typeof(GraphicRaycaster));
            Place(rect, new Vector2(0.5f, 0.5f), position, size);
            rect.anchoredPosition3D = new Vector3(position.x, position.y, -(panel.HalfThickness + 6f));
            var canvas = rect.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = LiquidSorting + 2;
            // A nested canvas doesn't take the root's channels, and GlassGraphic packs its shape into them.
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var slider = rect.gameObject.AddComponent<Slider>();

            RectTransform background = Node("Background", rect);
            background.anchorMin = new Vector2(0f, 0.5f);
            background.anchorMax = new Vector2(1f, 0.5f);
            background.sizeDelta = new Vector2(0f, 17f);
            GlassGraphic track = Solid(background, 8.5f, LiquidTrack, 0.4f);
            // A click anywhere on the bar jumps the knob there, not only a drag of the knob.
            track.raycastTarget = true;

            RectTransform fillArea = Node("Fill Area", rect);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(-36f, 17f);
            RectTransform fill = Node("Fill", fillArea);
            Stretch(fill);
            fill.offsetMin = new Vector2(-18f, 0f);
            fill.offsetMax = new Vector2(18f, 0f);
            GlassGraphic fillShape = Solid(fill, 8.5f, LiquidPalette.Turquoise.rim);

            RectTransform slideArea = Node("Handle Slide Area", rect);
            Stretch(slideArea);
            slideArea.offsetMin = new Vector2(18f, 0f);
            slideArea.offsetMax = new Vector2(-18f, 0f);
            RectTransform handle = Node("Handle", slideArea);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.sizeDelta = new Vector2(36f, 0f);
            GlassGraphic knob = Solid(handle, 18f, new Color(0.93f, 1f, 0.98f), 1f, 0.35f);
            knob.raycastTarget = true;
            // Stretches like a drop while it slides.
            handle.gameObject.AddComponent<UILiquidStretch>();

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = knob;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            slider.colors = Tints();
            return (slider, new Graphic[] { track, fillShape, knob });
        }

        // ---------------------------------------------------------------- Level complete

        static GlassCompleteCard BuildCompleteCard(Transform parent)
        {
            RectTransform cardRoot = Node("CompleteCard", parent, typeof(CanvasGroup));
            Stretch(cardRoot);
            Image wash = Backdrop(cardRoot, new Color(0.1f, 0.17f, 0.2f, 0.84f));
            // The celebration rain falls here, over the wash and behind the card.
            RectTransform rain = Node("Rain", cardRoot);
            Stretch(rain);
            RectTransform tilt = Tilt(cardRoot);

            RectTransform cardRect = Node("Card", tilt);
            Place(cardRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 560f));
            LiquidSlab card = GlassPanel(cardRect);

            Image badge = Icon(cardRect, "drop", 0f, Aqua);
            badge.preserveAspect = true;
            Place(badge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(100f, 100f));
            FrontCanvas(badge);
            TMP_Text kicker = Led(cardRect, "Kicker", "LEVEL", body, 18f, new Vector2(0f, 134f), new Vector2(640f, 30f), 0.6f);
            kicker.characterSpacing = 12f;
            TMP_Text title = Led(cardRect, "Title", "Delivered!", display, 64f, new Vector2(0f, 78f), new Vector2(660f, 84f));

            TMP_Text timeValue = Led(cardRect, "TimeValue", "00:00", display, 42f, new Vector2(-110f, -6f), new Vector2(200f, 52f));
            TMP_Text timeName = Led(cardRect, "TimeName", "Time", body, 19f, new Vector2(-110f, -48f), new Vector2(200f, 28f), 0.6f);
            TMP_Text cargoValue = Led(cardRect, "CargoValue", "0 / 0", display, 42f, new Vector2(110f, -6f), new Vector2(200f, 52f));
            TMP_Text cargoName = Led(cardRect, "CargoName", "Cargo", body, 19f, new Vector2(110f, -48f), new Vector2(200f, 28f), 0.6f);

            Button replay = LiquidButton(ButtonNode(cardRect, "Replay", new Vector2(-160f, -172f), new Vector2(260f, 96f)), "Replay", "restart", 1);
            Button next = LiquidButton(ButtonNode(cardRect, "Next level", new Vector2(165f, -172f), new Vector2(310f, 96f)), "Next level", "arrow", 0,
                iconRight: true);
            card.FindLabels();
            Panel3D(card);
            cardRect.gameObject.SetActive(false);

            // As the card lands the badge splashes and droplets rain down; GlassCompleteCard starts it.
            var celebration = cardRoot.gameObject.AddComponent<UILiquidSequence>();
            celebration.dropIcons = new[] { badge.rectTransform };
            celebration.earned = 1;
            celebration.celebrationArea = rain;
            celebration.playOnEnable = false;

            var component = cardRoot.gameObject.AddComponent<GlassCompleteCard>();
            Wire(component, ("panel", cardRect.GetComponent<LiquidPanel3D>()), ("wash", wash), ("kicker", kicker), ("title", title),
                ("timeValue", timeValue), ("timeName", timeName), ("cargoValue", cargoValue), ("cargoName", cargoName),
                ("replayButton", replay), ("nextButton", next), ("nextLabel", next.transform.Find("Label").GetComponent<TMP_Text>()),
                ("celebration", celebration));
            return component;
        }

        // ---------------------------------------------------------------- 3D liquid blocks

        /// <summary>Full-screen tint behind a 3D panel; it catches the clicks that miss the panel.</summary>
        static Image Backdrop(RectTransform parent, Color color)
        {
            RectTransform rect = Node("Backdrop", parent);
            Stretch(rect);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>Leans what it holds back a little, so the camera sees the thick undersides of the buttons.</summary>
        static RectTransform Tilt(RectTransform parent)
        {
            RectTransform rect = Node("Tilt", parent);
            Stretch(rect);
            rect.gameObject.AddComponent<LiquidViewTilt>();
            return rect;
        }

        /// <summary>A thick pane of frosted glass that pours in and drains away (<see cref="Panel3D"/>).</summary>
        static LiquidSlab GlassPanel(RectTransform rect)
        {
            var panel = rect.gameObject.AddComponent<LiquidSlab>();
            panel.shape = LiquidSlab.SlabShape.Rounded;
            panel.cornerRadius = 62f;
            panel.edgeRadius = 20f;
            panel.thickness = 20f;
            panel.glass = true;
            panel.frost = 0.82f;
            panel.refraction = 0.012f;
            panel.iridescence = 0.55f;
            panel.glassMilk = LiquidPalette.GlassMilk;
            // Smoked, so the white LED text reads over the bright levels.
            panel.glassTint = SmokedGlass;
            panel.colors = new LiquidColors(LiquidPalette.GlassRim, LiquidPalette.GlassRim, Vector3.zero);
            panel.simRows = 170;
            panel.sortingOffset = 1;
            panel.alive = false;
            // A panel that lifted on hover would move its face past the slider in front of it.
            panel.interactive = false;
            return panel;
        }

        static LiquidPanel3D Panel3D(LiquidSlab panel)
        {
            var glass = panel.gameObject.AddComponent<LiquidPanel3D>();
            glass.showOnEnable = false;
            glass.deactivateOnHide = true;
            return glass;
        }

        static RectTransform ButtonNode(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            RectTransform rect = Node(name, parent);
            Place(rect, new Vector2(0.5f, 0.5f), position, size);
            return rect;
        }

        /// <summary>
        /// Makes the rect a thick 3D liquid button (colour <see cref="LiquidPalette.ForIndex"/>) with a white LED label
        /// and icon. A button without a label shows the icon in the middle.
        /// </summary>
        static Button LiquidButton(RectTransform rect, string text, string icon, int colour, bool iconRight = false)
        {
            var target = rect.gameObject.AddComponent<LiquidRaycastTarget>();
            var slab = rect.gameObject.AddComponent<LiquidSlab>();
            slab.colors = LiquidPalette.ForIndex(colour);
            slab.sortingOffset = 2;
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = target;
            rect.gameObject.AddComponent<LiquidButtonFocus>();

            Vector2 size = rect.sizeDelta;
            float iconX = text == null ? 0f : (size.x * 0.5f - 52f) * (iconRight ? 1f : -1f);
            Image image = Icon(rect, icon, 0f, Color.white);
            Place(image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(iconX, 0f), new Vector2(30f, 30f));
            FrontCanvas(image);
            if (text != null) Led(rect, "Label", text, body, 31f, Vector2.zero, size - new Vector2(20f, 10f));
            slab.FindLabels();
            return button;
        }

        /// <summary>
        /// White text that reads like an LED inside the liquid (<see cref="LiquidLEDLabel"/>); it goes over 1, so the
        /// bloom gives it a soft glow. It sits on a slab, as a direct child, with its own canvas to draw after it.
        /// </summary>
        static TMP_Text Led(RectTransform parent, string name, string text, TMP_FontAsset font, float size, Vector2 position, Vector2 box,
            float alpha = 1f, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            RectTransform rect = Node(name, parent);
            Place(rect, new Vector2(0.5f, 0.5f), position, box);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(1f, 1f, 1f, alpha);
            label.alignment = align;
            label.text = text;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            FrontCanvas(label);
            rect.gameObject.AddComponent<LiquidLEDLabel>();
            return label;
        }

        static void FrontCanvas(Graphic graphic)
        {
            var canvas = graphic.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = LiquidSorting + 3;
        }

        // ---------------------------------------------------------------- Building blocks

        static RectTransform Node(string name, Transform parent, params System.Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            foreach (System.Type type in components) go.AddComponent(type);
            return (RectTransform)go.transform;
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>Fixed layout size; a negative side is left to the layout group.</summary>
        static LayoutElement Size(Component target, float width, float height)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null) element = target.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) element.preferredWidth = element.minWidth = width;
            if (height >= 0f) element.preferredHeight = element.minHeight = height;
            return element;
        }

        static void Row(RectTransform rect, float spacing, RectOffset padding, TextAnchor align, bool expandWidth = false)
        {
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding;
            group.childAlignment = align;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = expandWidth;
            group.childForceExpandHeight = false;
        }

        static void Column(RectTransform rect, float spacing, TextAnchor align, RectOffset padding = null, bool expandWidth = false)
        {
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childAlignment = align;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = expandWidth;
            group.childForceExpandHeight = false;
        }

        static void Fit(RectTransform rect, bool width, bool height)
        {
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = width ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = height ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
        }

        static GlassGraphic Glass(RectTransform rect, float radius, Color? tint = null, float tintAmount = 0.32f, float shadow = 0.22f,
            float blur = 1f, float highlight = 1f)
        {
            var glass = rect.gameObject.AddComponent<GlassGraphic>();
            glass.material = glassMaterial;
            glass.color = tint ?? Color.white;
            glass.ShapeMode = GlassGraphic.Mode.Glass;
            glass.CornerRadius = radius;
            glass.TintAmount = tintAmount;
            glass.Shadow = shadow;
            glass.Blur = blur;
            glass.Highlight = highlight;
            glass.raycastTarget = false;
            return glass;
        }

        static GlassGraphic Solid(RectTransform rect, float radius, Color fill, float highlight = 1f, float shadow = 0f)
        {
            GlassGraphic solid = Glass(rect, radius, fill, 0f, shadow, 1f, highlight);
            solid.ShapeMode = GlassGraphic.Mode.Solid;
            return solid;
        }

        static UILiquidParticles Particles(RectTransform area, LiquidPreset preset)
        {
            var particles = area.gameObject.AddComponent<UILiquidParticles>();
            particles.ApplyPreset(preset);
            particles.raycastTarget = false;
            return particles;
        }

        static ColorBlock Tints()
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.86f, 1f, 0.96f);
            colors.pressedColor = new Color(0.72f, 0.88f, 0.86f);
            colors.selectedColor = new Color(0.62f, 1f, 0.9f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.45f);
            colors.fadeDuration = 0.08f;
            return colors;
        }

        static Image Icon(Transform parent, string name, float size, Color color)
        {
            RectTransform rect = Node("Icon", parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}/Icons/Icon_{name}.png");
            image.color = color;
            image.raycastTarget = false;
            if (size > 0f)
            {
                rect.sizeDelta = new Vector2(size, size);
                Size(rect, size, size);
            }
            return image;
        }

        static TMP_Text Label(Transform parent, string name, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            RectTransform rect = Node(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.alignment = align;
            label.text = text;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            rect.gameObject.AddComponent<LayoutElement>();
            return label;
        }

        static TMP_Text Kicker(Transform parent, string text, float size, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            // Upper-cased here, not with FontStyles.UpperCase, which follows the player's culture (Turkish "i" -> "İ").
            TMP_Text kicker = Label(parent, "Kicker", text.ToUpperInvariant(), body, size, Muted, align);
            kicker.characterSpacing = 12f;
            return kicker;
        }

        // ---------------------------------------------------------------- Assets

        static void SetUpIcons()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Icons" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer.textureType == TextureImporterType.Sprite) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        static Material GlassMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            material = new Material(Shader.Find("BounJam/UI/Liquid Glass"));
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        /// <summary>A dynamic SDF font asset next to the font file, pre-filled with Latin and Turkish letters.</summary>
        static TMP_FontAsset FontAsset(string fontFile, string name)
        {
            string path = $"{Root}/Fonts/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset != null) return asset;

            var font = AssetDatabase.LoadAssetAtPath<Font>($"{Root}/Fonts/{fontFile}");
            asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            asset.name = name;
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            asset.TryAddCharacters(Charset, out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning($"{name}: no glyphs for \"{missing}\"");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static void Weight(TMP_FontAsset font, float bold)
        {
            font.boldStyle = bold;
            font.boldSpacing = 2f;
            font.material.SetFloat(ShaderUtilities.ID_WeightBold, bold);
            EditorUtility.SetDirty(font);
            EditorUtility.SetDirty(font.material);
        }

        static void Wire(Object target, params (string field, Object value)[] references)
        {
            var serialized = new SerializedObject(target);
            foreach ((string field, Object value) in references)
                serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}
