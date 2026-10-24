using System.IO;
using BlobShip.UI;
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
    /// The BlobShip Liquid UI components (Assets/Assets/BlobShip/UI) make it move like liquid: jelly buttons and panels,
    /// the flowing form lens, bubbles, drips and splashes.
    /// </summary>
    public static class GlassUIBuilder
    {
        const string Root = "Assets/_Project/UI";
        const string PrefabPath = Root + "/Resources/GlassUI.prefab";
        const string MaterialPath = Root + "/Materials/M_UI_LiquidGlass.mat";
        const string Charset =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "·×–—…’“”çÇğĞıİöÖşŞüÜ";

        static readonly Color Ink = Hex("0B2233");
        static readonly Color Muted = Hex("22475C");
        static readonly Color Teal = Hex("00695F");
        static readonly Color Mint = Hex("1FC9AE");
        static readonly Color Aqua = Hex("46E6CF");
        static readonly Color Orange = Hex("FF8A1F");
        static readonly Color LensTint = new Color(0.75f, 1f, 0.95f);
        static readonly Color PrimaryFill = new Color(0.62f, 0.96f, 0.9f, 0.92f);
        static readonly Color SecondaryFill = new Color(1f, 1f, 1f, 0.42f);
        static readonly Color TrackFill = new Color(0.043f, 0.133f, 0.2f, 0.16f);

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

            var root = new GameObject("GlassUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.layer = LayerMask.NameToLayer("UI");
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;            canvas.sortingOrder = 10;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GlassHud hud = BuildHud(root.transform);
            GlassToast title = BuildToast(root.transform, "TitleToast", new Vector2(0.5f, 1f), new Vector2(0f, -170f), 43f, 40f);
            GlassToast message = BuildToast(root.transform, "MessageToast", new Vector2(0.5f, 0.5f), Vector2.zero, 58f, 52f);
            GlassPauseMenu pause = BuildPauseMenu(root.transform);
            GlassCompleteCard complete = BuildCompleteCard(root.transform);

            // Overlays start hidden, as their components hide them on Awake; this keeps the prefab readable.
            foreach (Component overlay in new Component[] { title, message, pause, complete })
            {
                var group = overlay.GetComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = group.blocksRaycasts = false;
            }

            var ui = root.AddComponent<GlassUI>();
            Wire(ui, ("hud", hud), ("titleToast", title), ("messageToast", message), ("pauseMenu", pause), ("completeCard", complete));

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"Glass UI built: {PrefabPath}");
        }

        // ---------------------------------------------------------------- HUD

        static GlassHud BuildHud(Transform parent)
        {
            RectTransform hudRoot = Node("HUD", parent, typeof(CanvasGroup));
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

            // Pause button, top right.
            RectTransform pauseRect = Node("PauseButton", hudRoot);
            Place(pauseRect, new Vector2(1f, 1f), new Vector2(-48f, -43f), new Vector2(86f, 86f));
            Button pauseButton = MakeButton(Glass(pauseRect, 43f));
            Icon(pauseRect, "pause", 31f, Ink);
            Liquid(pauseButton);

            // Form switch, bottom centre: the glass lens flows under the active form like a drop.
            RectTransform formSwitch = Node("FormSwitch", hudRoot);
            Place(formSwitch, new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(504f, 91f));
            Glass(formSwitch, 45.5f);
            RectTransform lens = Node("Lens", formSwitch);
            Place(lens, new Vector2(0.5f, 0.5f), new Vector2(-122.5f, 0f), new Vector2(245f, 77f));
            GlassGraphic lensGlass = Glass(lens, 38.5f, LensTint, 0.55f, 0.25f, 0.35f);
            lensGlass.color = LensTint;
            (RectTransform liquidSide, Graphic liquidIcon, Graphic liquidText) = FormSide(formSwitch, "Liquid", "drop_outline", -122.5f);
            (RectTransform solidSide, Graphic solidIcon, Graphic solidText) = FormSide(formSwitch, "Solid", "crystal", 122.5f);
            var formFlow = formSwitch.gameObject.AddComponent<UILiquidSegmented>();
            formFlow.indicator = lens;
            formFlow.options = new[] { liquidSide, solidSide };

            RectTransform hint = Node("Hint", hudRoot);
            Place(hint, new Vector2(0.5f, 0f), new Vector2(0f, 48f), Vector2.zero);
            Glass(hint, 17f, Color.white, 0.32f, 0.12f);
            Row(hint, 0f, new RectOffset(17, 17, 7, 8), TextAnchor.MiddleCenter);
            Fit(hint, true, true);
            TMP_Text hintLabel = Label(hint, "Text", "E  ·  switch form", body, 17f, Muted);

            Wire(hud, ("group", hudRoot.GetComponent<CanvasGroup>()), ("levelKicker", levelKicker), ("levelLabel", levelLabel), ("cargoPill", cargo.gameObject),
                ("cargoIcon", cargoIcon), ("cargoCount", count), ("cargoFill", fill), ("formSwitch", formFlow), ("formHint", hintLabel),
                ("pauseButton", pauseButton));
            WireArray(hud, "liquidParts", liquidIcon, liquidText);
            WireArray(hud, "solidParts", solidIcon, solidText);
            return hud;
        }

        static (RectTransform side, Graphic icon, Graphic label) FormSide(RectTransform formSwitch, string name, string icon, float x)
        {
            RectTransform side = Node(name, formSwitch);
            Place(side, new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(245f, 77f));
            Row(side, 12f, new RectOffset(), TextAnchor.MiddleCenter);
            Image image = Icon(side, icon, 28f, Ink);
            TMP_Text label = Label(side, "Label", name, body, 24f, Ink);
            return (side, image, label);
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
            RectTransform backdrop = Node("Backdrop", menuRoot);
            Stretch(backdrop);
            GlassGraphic dim = Glass(backdrop, 0f, new Color(0.024f, 0.11f, 0.19f), 0.4f, 0f, 0.6f, 0f);
            dim.raycastTarget = true;

            RectTransform panel = Node("Panel", menuRoot);
            Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(672f, 0f));
            Glass(panel, 48f, Color.white, 0.42f, 0.35f);
            Column(panel, 34f, TextAnchor.UpperLeft, new RectOffset(48, 48, 48, 48), true);
            Fit(panel, false, true);
            Jelly(panel);
            // Bubbles rise behind the contents, inside the glass; drops gather on its bottom edge and fall.
            RectTransform bubbles = Overlay(panel, "Bubbles");
            Stretch(bubbles);
            bubbles.offsetMin = new Vector2(30f, 30f);
            bubbles.offsetMax = new Vector2(-30f, -30f);
            bubbles.gameObject.AddComponent<RectMask2D>();
            RectTransform bubbleArea = Node("Emitter", bubbles);
            Stretch(bubbleArea);
            Particles(bubbleArea, LiquidPreset.AmbientBubbles);
            RectTransform drip = Overlay(panel, "Drip");
            drip.anchorMin = new Vector2(0f, 0f);
            drip.anchorMax = new Vector2(1f, 0f);
            drip.pivot = new Vector2(0.5f, 0f);
            drip.sizeDelta = new Vector2(-2f * 70f, 0f);
            Particles(drip, LiquidPreset.Drip);

            RectTransform header = Node("Header", panel);
            Row(header, 22f, new RectOffset(), TextAnchor.MiddleLeft);
            RectTransform disc = Node("Disc", header);
            Solid(disc, 36f, Ink);
            Size(disc, 72f, 72f);
            Icon(disc, "pause", 26f, Color.white);
            RectTransform titles = Node("Titles", header);
            Column(titles, 6f, TextAnchor.MiddleLeft);
            Label(titles, "Title", "Paused", display, 48f, Ink);
            TMP_Text subtitle = Label(titles, "Subtitle", "Level", body, 19f, Muted);

            RectTransform buttons = Node("Buttons", panel);
            Column(buttons, 14f, TextAnchor.UpperLeft, new RectOffset(), true);
            Button resume = MenuButton(buttons, "Resume", "play", true);
            Button restart = MenuButton(buttons, "Restart level", "restart", false);
            Button quit = MenuButton(buttons, "Quit game", "exit", false);

            RectTransform settings = Node("Settings", panel);
            Solid(settings, 31f, new Color(1f, 1f, 1f, 0.26f), 0.6f);
            Column(settings, 14f, TextAnchor.UpperLeft, new RectOffset(26, 26, 24, 26), true);
            RectTransform volumeRow = Node("VolumeRow", settings);
            Row(volumeRow, 12f, new RectOffset(), TextAnchor.MiddleLeft);
            Icon(volumeRow, "volume", 26f, Ink);
            TMP_Text volumeName = Label(volumeRow, "Name", "Volume", body, 20f, Ink);
            volumeName.GetComponent<LayoutElement>().flexibleWidth = 1f;
            TMP_Text volumeValue = Label(volumeRow, "Value", "100%", body, 20f, Teal, TextAlignmentOptions.Right);
            Slider volume = MakeSlider(settings);

            var menu = menuRoot.gameObject.AddComponent<GlassPauseMenu>();
            Wire(menu, ("subtitle", subtitle), ("resumeButton", resume), ("restartButton", restart), ("quitButton", quit),
                ("volume", volume), ("volumeValue", volumeValue));
            return menu;
        }

        static Button MenuButton(Transform parent, string text, string icon, bool primary)
        {
            RectTransform rect = Node(text, parent);
            Size(rect, -1f, 77f);
            Button button = MakeButton(Solid(rect, 26f, primary ? PrimaryFill : SecondaryFill, 0.85f, primary ? 0.2f : 0.1f));
            Row(rect, 17f, new RectOffset(26, 26, 0, 0), TextAnchor.MiddleLeft);
            Icon(rect, icon, 26f, Ink);
            Label(rect, "Label", text, body, 24f, Ink);
            Liquid(button, 3, 1.6f, 150f);
            return button;
        }

        static Slider MakeSlider(Transform parent)
        {
            RectTransform rect = Node("VolumeSlider", parent);
            Size(rect, -1f, 36f);
            var slider = rect.gameObject.AddComponent<Slider>();

            RectTransform background = Node("Background", rect);
            background.anchorMin = new Vector2(0f, 0.5f);
            background.anchorMax = new Vector2(1f, 0.5f);
            background.sizeDelta = new Vector2(0f, 17f);
            Solid(background, 8.5f, TrackFill, 0.4f);

            RectTransform fillArea = Node("Fill Area", rect);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(-36f, 17f);
            RectTransform fill = Node("Fill", fillArea);
            Stretch(fill);
            fill.offsetMin = new Vector2(-18f, 0f);
            fill.offsetMax = new Vector2(18f, 0f);
            Solid(fill, 8.5f, Mint);

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
            return slider;
        }

        // ---------------------------------------------------------------- Level complete

        static GlassCompleteCard BuildCompleteCard(Transform parent)
        {
            RectTransform cardRoot = Node("CompleteCard", parent, typeof(CanvasGroup));
            Stretch(cardRoot);
            RectTransform wash = Node("Wash", cardRoot);
            Stretch(wash);
            GlassGraphic washGlass = Glass(wash, 0f, new Color(0.47f, 0.86f, 0.9f), 0.2f, 0f, 0.35f, 0f);
            washGlass.raycastTarget = true;
            // The celebration rain falls here, over the wash and behind the card.
            RectTransform rain = Node("Rain", cardRoot);
            Stretch(rain);

            RectTransform card = Node("Card", cardRoot);
            Place(card, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(768f, 0f));
            Glass(card, 58f, Color.white, 0.44f, 0.32f);
            Column(card, 26f, TextAnchor.UpperCenter, new RectOffset(58, 58, 53, 53), true);
            Fit(card, false, true);
            Jelly(card);

            Image badge = Icon(card, "drop", 0f, Aqua);
            badge.preserveAspect = true;
            Size(badge.rectTransform, -1f, 115f);
            TMP_Text kicker = Kicker(card, "Level", 17f, TextAlignmentOptions.Center);
            TMP_Text title = Label(card, "Title", "Delivered!", display, 67f, Ink, TextAlignmentOptions.Center);

            RectTransform stats = Node("Stats", card);
            Row(stats, 17f, new RectOffset(), TextAnchor.MiddleCenter, true);
            (_, TMP_Text timeValue) = StatTile(stats, "Time", "00:00");
            (RectTransform cargoTile, TMP_Text cargoValue) = StatTile(stats, "Cargo", "0 / 0");

            RectTransform buttons = Node("Buttons", card);
            Row(buttons, 17f, new RectOffset(), TextAnchor.MiddleCenter, true);
            Button replay = PillButton(buttons, "Replay", "restart", false, 1f);
            Button next = PillButton(buttons, "Next level", "arrow", true, 1.4f);

            // As the card lands the badge splashes and droplets rain down; GlassCompleteCard starts it.
            var celebration = cardRoot.gameObject.AddComponent<UILiquidSequence>();
            celebration.dropIcons = new[] { badge.rectTransform };
            celebration.earned = 1;
            celebration.celebrationArea = rain;
            celebration.playOnEnable = false;

            var component = cardRoot.gameObject.AddComponent<GlassCompleteCard>();
            Wire(component, ("kicker", kicker), ("title", title), ("timeValue", timeValue), ("cargoTile", cargoTile.gameObject),
                ("cargoValue", cargoValue), ("replayButton", replay), ("nextButton", next),
                ("nextLabel", next.transform.Find("Label").GetComponent<TMP_Text>()), ("card", card), ("celebration", celebration));
            return component;
        }

        static (RectTransform tile, TMP_Text value) StatTile(Transform parent, string name, string value)
        {
            RectTransform tile = Node(name, parent);
            Solid(tile, 29f, new Color(1f, 1f, 1f, 0.3f), 0.7f);
            Column(tile, 7f, TextAnchor.MiddleCenter, new RectOffset(12, 12, 22, 22), true);
            Size(tile, 1f, -1f).flexibleWidth = 1f;
            TMP_Text valueText = Label(tile, "Value", value, display, 36f, Ink, TextAlignmentOptions.Center);
            Label(tile, "Name", name, body, 18f, Muted, TextAlignmentOptions.Center);
            return (tile, valueText);
        }

        static Button PillButton(Transform parent, string text, string icon, bool primary, float weight)
        {
            RectTransform rect = Node(text, parent);
            LayoutElement size = Size(rect, 1f, 82f);
            size.flexibleWidth = weight;
            Button button = MakeButton(Solid(rect, 41f, primary ? PrimaryFill : SecondaryFill, 0.85f, primary ? 0.22f : 0.12f));
            Row(rect, 14f, new RectOffset(24, 24, 0, 0), TextAnchor.MiddleCenter);
            if (primary)
            {
                Label(rect, "Label", text, body, 25f, Ink);
                Icon(rect, icon, 26f, Ink);
            }
            else
            {
                Icon(rect, icon, 26f, Ink);
                Label(rect, "Label", text, body, 25f, Ink);
            }
            Liquid(button, 3, 1.2f, 120f);
            return button;
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

        static Button MakeButton(Graphic target)
        {
            target.raycastTarget = true;
            var button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            button.colors = Tints();
            return button;
        }

        /// <summary>
        /// Makes a button a drop of liquid glass: it swells towards the pointer, squashes, ripples and splashes when
        /// pressed. A GlassGraphic is one quad, so wide buttons need more subdivisions and a broader bulge and ripple
        /// for the bend to stay smooth.
        /// </summary>
        static void Liquid(Button button, int subdivisions = 2, float bulgeRadius = 0.6f, float rippleWavelength = 70f)
        {
            var jelly = button.gameObject.AddComponent<UILiquidJelly>();
            jelly.subdivisions = subdivisions;
            jelly.bulgeRadius = bulgeRadius;
            jelly.rippleWavelength = rippleWavelength;
            button.gameObject.AddComponent<UILiquidInteractable>().jelly = jelly;
        }

        /// <summary>A big glass panel whose outline slowly waves.</summary>
        static void Jelly(RectTransform panel)
        {
            var jelly = panel.gameObject.AddComponent<UILiquidJelly>();
            jelly.subdivisions = 3;
            jelly.idleWave = 1.5f;
        }

        /// <summary>A child the parent's layout group leaves alone, for effects laid over a panel.</summary>
        static RectTransform Overlay(RectTransform parent, string name)
        {
            RectTransform rect = Node(name, parent, typeof(LayoutElement));
            rect.GetComponent<LayoutElement>().ignoreLayout = true;
            return rect;
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

        static void WireArray(Object target, string field, params Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }
    }
}
