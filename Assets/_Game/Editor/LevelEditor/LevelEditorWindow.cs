using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static BounJam.EditorTools.LevelKit;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The level editor (BounJam/Level Editor). Build: pick a piece and click in the Scene view to put it down, or drag
    /// to draw platforms, walls, ramps, pools, water and hot plates; [ and ] turn it, Esc stops. Placed pieces snap to
    /// the grid, go in groups under the scene's Level root and can be undone; shaped pieces resize with their handles.
    /// Selecting buttons with gates, lifts or magnets links them, and the Scene view shows every link. Level: the open
    /// level's title, next scene and references. Play Order: the levels in Build Settings, and linking each one to the
    /// next.
    /// </summary>
    public class LevelEditorWindow : EditorWindow
    {
        enum Tab { Build, Level, PlayOrder }

        static readonly string[] TabNames = { "Build", "Level", "Play Order" };
        static readonly string[] TurnNames = { "0°", "90°", "180°", "270°" };
        static readonly Color PreviewColor = new Color(0.3f, 0.9f, 1f);
        static readonly Color GateLinkColor = new Color(1f, 0.85f, 0.25f);
        static readonly Color MagnetOnLinkColor = new Color(0.35f, 0.65f, 1f);
        static readonly Color MagnetOffLinkColor = new Color(0.9f, 0.5f, 0.25f);

        Tab tab;
        Vector2 scroll;

        // Placing in the Scene view.
        Placeable armed;
        bool hasHover, dragging;
        Vector3 hover, dragStart, dragEnd;

        // When the Scene view was last clicked, so only selections made there get widened to whole pieces.
        static double lastSceneClick;

        [MenuItem("BounJam/Level Editor", priority = 0)]
        public static void Open() => GetWindow<LevelEditorWindow>("Level Editor");

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Disarm();
        }

        void OnHierarchyChange() => Repaint();

        void OnPlayModeChanged(PlayModeStateChange _) => Disarm();

        void OnGUI()
        {
            tab = (Tab)GUILayout.Toolbar((int)tab, TabNames);
            EditorGUILayout.Space(4f);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                switch (tab)
                {
                    case Tab.Build: BuildTab(); break;
                    case Tab.Level: LevelTab(); break;
                    case Tab.PlayOrder: PlayOrderTab(); break;
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- Build tab

        void BuildTab()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (LevelSetup.Find<LevelManager>(scene) == null) NotALevel(scene);

            Header("Placing");
            using (new EditorGUILayout.HorizontalScope())
            {
                LevelEditorSettings.Snapping = EditorGUILayout.ToggleLeft("Snap To Grid", LevelEditorSettings.Snapping, GUILayout.Width(110f));
                using (new EditorGUI.DisabledScope(!LevelEditorSettings.Snapping))
                    LevelEditorSettings.Grid = EditorGUILayout.FloatField(LevelEditorSettings.Grid);
                GUILayout.Label("m", GUILayout.ExpandWidth(false));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                LevelEditorSettings.BuildHeight = EditorGUILayout.FloatField(
                    new GUIContent("Build Height", "Platforms and pools are drawn with their top at this height; clicks over the void land on it."),
                    LevelEditorSettings.BuildHeight);
                if (GUILayout.Button(new GUIContent("From Selection", "The top of the selected objects."), GUILayout.ExpandWidth(false)))
                    BuildHeightFromSelection();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Turn", "Which way the next piece faces. [ and ] in the Scene view."));
                int turn = GUILayout.Toolbar(LevelEditorSettings.Turn / 90, TurnNames);
                if (turn * 90 != LevelEditorSettings.Turn)
                {
                    LevelEditorSettings.Turn = turn * 90;
                    SceneView.RepaintAll();
                }
            }
            LevelEditorSettings.ShipRidesBlob = EditorGUILayout.Toggle(
                new GUIContent("Ship Rides Blob", "Moving the blob's start takes the ship along, sat on top of it."),
                LevelEditorSettings.ShipRidesBlob);

            Header("Pieces");
            foreach (string category in Placeable.Categories) PieceButtons(category);
            if (armed != null && armed.Name == "Tile")
            {
                int look = System.Array.IndexOf(Placeable.TileLooks, LevelEditorSettings.TileLook);
                LevelEditorSettings.TileLook = Placeable.TileLooks[EditorGUILayout.Popup("Tile Look", Mathf.Max(0, look), Placeable.TileLooks)];
            }
            EditorGUILayout.HelpBox(armed == null
                ? "Pick a piece, then click in the Scene view to put it down."
                : $"{armed.Name}: {armed.Tooltip}\n{Instructions(armed)}", armed == null ? MessageType.None : MessageType.Info);

            Header("Selection");
            SelectionPanel();

            Header("View");
            bool links = EditorGUILayout.ToggleLeft("Show Button Links", LevelEditorSettings.ShowLinks);
            if (links != LevelEditorSettings.ShowLinks)
            {
                LevelEditorSettings.ShowLinks = links;
                SceneView.RepaintAll();
            }
            LevelEditorSettings.SelectWholePieces = EditorGUILayout.ToggleLeft(
                new GUIContent("Select Whole Pieces", "Clicking a part of a piece in the Scene view selects the whole piece."),
                LevelEditorSettings.SelectWholePieces);
        }

        void PieceButtons(string category)
        {
            EditorGUILayout.LabelField(category, EditorStyles.miniBoldLabel);
            Placeable[] pieces = Placeable.All.Where(p => p.Category == category).ToArray();
            const int perRow = 3;
            // Room for the scroll bar, so every button is the same width.
            float width = Mathf.Max(60f, (EditorGUIUtility.currentViewWidth - 30f) / perRow - 4f);
            for (int i = 0; i < pieces.Length; i += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < i + perRow && j < pieces.Length; j++)
                    {
                        Placeable piece = pieces[j];
                        bool on = armed == piece;
                        if (GUILayout.Toggle(on, new GUIContent(piece.Name, piece.Tooltip), "Button", GUILayout.Width(width), GUILayout.Height(24f)) != on)
                        {
                            if (on) Disarm();
                            else Arm(piece);
                        }
                    }
                }
            }
        }

        static string Instructions(Placeable piece) => piece.How switch
        {
            Placeable.Placement.Draw => "Drag in the Scene view to draw it, or click for the default size. [ ] turn, Esc stops.",
            Placeable.Placement.Start => "Click in the Scene view to move it there.",
            _ => "Click in the Scene view to put one down. [ ] turn, Esc stops.",
        };

        void NotALevel(Scene scene)
        {
            string name = string.IsNullOrEmpty(scene.name) ? "This scene" : scene.name;
            EditorGUILayout.HelpBox($"{name} isn't a level yet: it has no level logic (blob, ship, dock and LevelManager).", MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Make This Scene A Level")) LevelSetup.MakeLevel(scene);
                if (GUILayout.Button("New Level…")) SceneChange(LevelSetup.NewLevel);
            }
        }

        /// <summary>
        /// Runs something that opens dialogs or scenes, then ends this GUI pass: the layout it was in is gone afterwards.
        /// </summary>
        static void SceneChange(System.Action action)
        {
            action();
            GUIUtility.ExitGUI();
        }

        static void BuildHeightFromSelection()
        {
            float top = float.NegativeInfinity;
            foreach (GameObject go in Selection.gameObjects)
                foreach (Collider collider in go.GetComponentsInChildren<Collider>())
                    if (!collider.isTrigger) top = Mathf.Max(top, collider.bounds.max.y);
            if (!float.IsNegativeInfinity(top)) LevelEditorSettings.BuildHeight = Mathf.Round(top * 1000f) / 1000f;
        }

        // ---------------------------------------------------------------- Linking buttons

        void SelectionPanel()
        {
            GameObject[] selected = Selection.gameObjects;
            PressureButton[] buttons = Components<PressureButton>(selected);
            ButtonMover[] movers = Components<ButtonMover>(selected);
            Magnet[] magnets = Components<Magnet>(selected);

            if (buttons.Length > 0 && movers.Length + magnets.Length > 0)
            {
                EditorGUILayout.LabelField($"{Count(buttons.Length, "button")} with {Count(movers.Length + magnets.Length, "machine")}");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (movers.Length > 0 && GUILayout.Button("Link"))
                        foreach (ButtonMover mover in movers) Link(mover, "buttons", null, buttons);
                    if (magnets.Length > 0 && GUILayout.Button("Link: Switches On"))
                        foreach (Magnet magnet in magnets) Link(magnet, "onButtons", "offButtons", buttons);
                    if (magnets.Length > 0 && GUILayout.Button("Link: Switches Off"))
                        foreach (Magnet magnet in magnets) Link(magnet, "offButtons", "onButtons", buttons);
                    if (GUILayout.Button("Unlink"))
                    {
                        foreach (ButtonMover mover in movers) Unlink(mover, buttons, "buttons");
                        foreach (Magnet magnet in magnets) Unlink(magnet, buttons, "onButtons", "offButtons");
                    }
                }
            }
            else if (movers.Length == 1 && buttons.Length == 0)
            {
                LinkedButtons(movers[0], "buttons", "Opened by");
            }
            else if (magnets.Length == 1 && buttons.Length == 0)
            {
                LinkedButtons(magnets[0], "onButtons", "Switched on by");
                LinkedButtons(magnets[0], "offButtons", "Switched off by");
            }
            else if (buttons.Length > 0)
            {
                EditorGUILayout.HelpBox("Ctrl-click a gate, lift or magnet too to link this button to it.", MessageType.None);
            }
            else if (Components<LevelPiece>(selected).Length > 0)
            {
                EditorGUILayout.HelpBox("Resize it with the handles in the Scene view, or change its settings in the Inspector.", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Select buttons together with a gate, lift or magnet to link them.", MessageType.None);
            }
        }

        static void LinkedButtons(Object machine, string list, string label)
        {
            var settings = new SerializedObject(machine);
            List<PressureButton> linked = Read(settings.FindProperty(list));
            EditorGUILayout.LabelField(label, linked.Count == 0 ? "no buttons yet" : "");
            foreach (PressureButton button in linked)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(button, typeof(PressureButton), true);
                    if (GUILayout.Button("✕", GUILayout.Width(24f))) Unlink(machine, new[] { button }, list);
                }
            }
        }

        /// <summary>Adds the buttons to <paramref name="list"/> on the machine and takes them off <paramref name="other"/>.</summary>
        static void Link(Object machine, string list, string other, IEnumerable<PressureButton> buttons)
        {
            var settings = new SerializedObject(machine);
            SerializedProperty property = settings.FindProperty(list);
            List<PressureButton> linked = Read(property);
            foreach (PressureButton button in buttons)
                if (!linked.Contains(button)) linked.Add(button);
            Write(property, linked);
            if (other != null)
            {
                SerializedProperty otherProperty = settings.FindProperty(other);
                Write(otherProperty, Read(otherProperty).Except(buttons).ToList());
            }
            settings.ApplyModifiedProperties();
        }

        static void Unlink(Object machine, IEnumerable<PressureButton> buttons, params string[] lists)
        {
            var settings = new SerializedObject(machine);
            foreach (string list in lists)
            {
                SerializedProperty property = settings.FindProperty(list);
                Write(property, Read(property).Except(buttons).ToList());
            }
            settings.ApplyModifiedProperties();
        }

        static List<PressureButton> Read(SerializedProperty list)
        {
            var buttons = new List<PressureButton>();
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is PressureButton button) buttons.Add(button);
            return buttons;
        }

        static void Write(SerializedProperty list, List<PressureButton> buttons)
        {
            list.arraySize = buttons.Count;
            for (int i = 0; i < buttons.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        }

        // From the parents too, for a part of a piece selected in the Hierarchy.
        static T[] Components<T>(GameObject[] objects) where T : Component
            => objects.Select(go => go.GetComponentInParent<T>()).Where(c => c != null).Distinct().ToArray();

        static string Count(int n, string thing) => n == 1 ? $"1 {thing}" : $"{n} {thing}s";

        // ---------------------------------------------------------------- Level tab

        void LevelTab()
        {
            Scene scene = SceneManager.GetActiveScene();
            LevelManager manager = LevelSetup.Find<LevelManager>(scene);
            if (manager == null)
            {
                NotALevel(scene);
                return;
            }

            EditorGUILayout.LabelField(scene.name, EditorStyles.boldLabel);
            var settings = new SerializedObject(manager);
            settings.Update();
            EditorGUILayout.PropertyField(settings.FindProperty("levelTitle"), new GUIContent("Title"));
            NextScene(settings.FindProperty("nextScene"));
            EditorGUILayout.PropertyField(settings.FindProperty("isFinalLevel"), new GUIContent("Final Level"));
            EditorGUILayout.PropertyField(settings.FindProperty("killHeight"), new GUIContent("Fall Height"));
            settings.ApplyModifiedProperties();

            List<string> missing = LevelSetup.Missing(manager);
            if (missing.Count > 0)
            {
                EditorGUILayout.HelpBox($"The level logic is missing its {string.Join(", ", missing)}.", MessageType.Warning);
                if (GUILayout.Button("Find Them In The Scene")) LevelSetup.FixReferences(manager);
            }
            if (GUILayout.Button(new GUIContent("Use The Level Prefabs",
                                                "Rebuilds the shaped pieces from the part prefabs and swaps pieces built before the prefabs " +
                                                "existed for their prefabs, keeping their names, places, tuning and links.")))
                Debug.Log("[BounJam] " + LevelUpgrade.Upgrade(scene));

            Header("Start & Goal");
            Transform blob = LevelSetup.Blob(scene);
            ShipBuoyancy ship = LevelSetup.Find<ShipBuoyancy>(scene);
            ShipDock dock = LevelSetup.Find<ShipDock>(scene);
            using (new EditorGUILayout.HorizontalScope())
            {
                SelectButton("Blob", blob);
                SelectButton("Ship", ship);
                SelectButton("Dock", dock);
            }
            using (new EditorGUI.DisabledScope(blob == null || ship == null))
                if (GUILayout.Button(new GUIContent("Put The Ship On The Blob", "Sits the ship on top of the blob's start, so the level starts carrying it.")))
                {
                    Undo.RecordObject(ship.transform, "Put Ship On Blob");
                    ship.transform.SetPositionAndRotation(LevelSetup.OnBlob(blob.position - Vector3.up * CoreRadius), Quaternion.identity);
                }

            EditorGUILayout.Space();
            if (GUILayout.Button("▶  Play This Level", GUILayout.Height(28f))) EditorApplication.EnterPlaymode();
        }

        static void SelectButton(string label, Component target)
        {
            using (new EditorGUI.DisabledScope(target == null))
                if (GUILayout.Button(label))
                {
                    Selection.activeGameObject = target.gameObject;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
        }

        static void NextScene(SerializedProperty next)
        {
            List<string> names = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)).ToList();
            string current = next.stringValue;
            bool missing = !string.IsNullOrEmpty(current) && !names.Contains(current);
            if (missing) names.Insert(0, current);

            var options = new List<string> { "(none: never completes, like the sandbox)" };
            options.AddRange(names.Select(n => missing && n == current ? $"{n} (not in Build Settings!)" : n));
            int index = string.IsNullOrEmpty(current) ? 0 : names.IndexOf(current) + 1;
            int chosen = EditorGUILayout.Popup(new GUIContent("Next Scene", next.tooltip), index, options.ToArray());
            if (chosen != index) next.stringValue = chosen == 0 ? "" : names[chosen - 1];

            if (missing)
                EditorGUILayout.HelpBox($"{current} isn't in Build Settings, so finishing this level can't load it. Pick another " +
                                        "scene, or use Link Levels In This Order on the Play Order tab.", MessageType.Error);
        }

        // ---------------------------------------------------------------- Play order tab

        void PlayOrderTab()
        {
            EditorGUILayout.HelpBox("The scenes in Build Settings, played from the top. Unticked scenes and the sandbox aren't part of the run.",
                                    MessageType.None);
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            bool changed = false;
            for (int i = 0; i < scenes.Count; i++)
            {
                EditorBuildSettingsScene scene = scenes[i];
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    bool enabled = EditorGUILayout.Toggle(scene.enabled, GUILayout.Width(16f));
                    if (enabled != scene.enabled)
                    {
                        scene.enabled = enabled;
                        changed = true;
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        string name = Path.GetFileNameWithoutExtension(scene.path);
                        EditorGUILayout.LabelField(name, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(Describe(scene.path), EditorStyles.miniLabel);
                    }

                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button("▲", GUILayout.Width(24f)))
                        {
                            (scenes[i - 1], scenes[i]) = (scenes[i], scenes[i - 1]);
                            changed = true;
                        }
                    using (new EditorGUI.DisabledScope(i == scenes.Count - 1))
                        if (GUILayout.Button("▼", GUILayout.Width(24f)))
                        {
                            (scenes[i + 1], scenes[i]) = (scenes[i], scenes[i + 1]);
                            changed = true;
                        }
                    if (GUILayout.Button("Open", GUILayout.Width(48f)))
                        SceneChange(() =>
                        {
                            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(scene.path);
                        });
                    if (GUILayout.Button(new GUIContent("✕", "Take it out of Build Settings (the scene file stays)."), GUILayout.Width(24f)))
                    {
                        scenes.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
            if (changed)
            {
                LevelSetup.SaveBuildScenes(scenes);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New Level…")) SceneChange(LevelSetup.NewLevel);
                Scene open = SceneManager.GetActiveScene();
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(open.path) || scenes.Any(s => s.path == open.path)))
                    if (GUILayout.Button("Add Open Scene")) LevelSetup.AddToPlayOrder(open.path);
            }
            if (GUILayout.Button(new GUIContent("Link Levels In This Order",
                                                "Each level's next scene becomes the one below it; the last one is the final level and starts the run over."),
                                 GUILayout.Height(26f)))
                SceneChange(LevelSetup.LinkLevels);
        }

        static string Describe(string path)
        {
            if (path == SandboxBuilder.ScenePath) return "Sandbox: never completes, not part of the run";
            LevelSetup.LevelInfo info = LevelSetup.ReadLevel(path);
            if (!info.IsLevel) return File.Exists(path) ? "Not a level (no LevelManager)" : "Scene file is missing!";
            string next = string.IsNullOrEmpty(info.NextScene) ? "never completes"
                : LevelSetup.InBuild(info.NextScene) ? $"then {info.NextScene}"
                : $"then {info.NextScene}, which isn't in Build Settings!";
            return $"{info.Title}  ·  {next}{(info.IsFinal ? "  ·  final level" : "")}";
        }

        static void Header(string text)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        // ---------------------------------------------------------------- Scene view

        void Arm(Placeable piece)
        {
            armed = piece;
            dragging = false;
            SceneView.RepaintAll();
        }

        void Disarm()
        {
            if (armed == null) return;
            armed = null;
            dragging = hasHover = false;
            SceneView.RepaintAll();
            Repaint();
        }

        void OnSceneGUI(SceneView view)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt) lastSceneClick = EditorApplication.timeSinceStartup;
            if (LevelEditorSettings.ShowLinks && e.type == EventType.Repaint) DrawLinks();
            if (armed != null && !EditorApplication.isPlaying) Placing(view, e);
        }

        void Placing(SceneView view, Event e)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    if (dragging)
                    {
                        dragging = false;
                        GUIUtility.hotControl = 0;
                    }
                    else Disarm();
                    e.Use();
                }
                else if ((e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket) && !e.control && !e.alt)
                {
                    LevelEditorSettings.Turn += e.keyCode == KeyCode.RightBracket ? 90 : -90;
                    e.Use();
                    Repaint();
                }
                return;
            }

            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    // Clicks that no handle wants come to us instead of selecting things.
                    HandleUtility.AddDefaultControl(id);
                    break;
                case EventType.MouseMove:
                    hasHover = Target(e.mousePosition, armed.OnBuildPlane, out hover);
                    view.Repaint();
                    break;
                case EventType.MouseDown:
                    if (e.button != 0 || e.alt || HandleUtility.nearestControl != id) break;
                    if (!Target(e.mousePosition, armed.OnBuildPlane, out Vector3 point)) break;
                    GUIUtility.hotControl = id;
                    dragging = true;
                    dragStart = dragEnd = hover = point;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    if (armed.How == Placeable.Placement.Draw && OnPlane(e.mousePosition, dragStart.y, out Vector3 end)) dragEnd = end;
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    if (dragging)
                    {
                        dragging = false;
                        Place();
                    }
                    e.Use();
                    break;
                case EventType.Repaint:
                    DrawPreview(view);
                    break;
            }
        }

        /// <summary>The snapped point under the mouse: on a floor-like surface there, or on the build height.</summary>
        static bool Target(Vector2 mouse, bool onBuildPlane, out Vector3 point)
        {
            if (!onBuildPlane)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
                Physics.SyncTransforms();
                int mask = ~LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Ship");
                if (Physics.Raycast(ray, out RaycastHit hit, 1000f, mask, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.5f)
                {
                    point = Snapped(hit.point);
                    return true;
                }
            }
            return OnPlane(mouse, LevelEditorSettings.BuildHeight, out point);
        }

        static bool OnPlane(Vector2 mouse, float height, out Vector3 point)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (plane.Raycast(ray, out float distance) && distance < 2000f)
            {
                point = Snapped(ray.GetPoint(distance));
                point.y = height;
                return true;
            }
            point = default;
            return false;
        }

        static Vector3 Snapped(Vector3 point)
            => new Vector3(LevelEditorSettings.Snap(point.x), Mathf.Round(point.y * 1000f) / 1000f, LevelEditorSettings.Snap(point.z));

        static Quaternion Turn => Quaternion.Euler(0f, LevelEditorSettings.Turn, 0f);

        /// <summary>
        /// Where a shaped piece goes and how big it is: the dragged rectangle, or the default size round the click.
        /// Too thin a rectangle keeps the default along that side, so a drag along a line makes a wall.
        /// </summary>
        void Drawn(out Vector3 position, out Vector3 size)
        {
            size = armed.Size;
            position = dragStart;
            if (armed.How != Placeable.Placement.Draw) return;

            Vector3 min = Vector3.Min(dragStart, dragEnd), max = Vector3.Max(dragStart, dragEnd);
            bool quarterTurned = LevelEditorSettings.Turn % 180 != 0;
            float across = quarterTurned ? max.z - min.z : max.x - min.x;
            float along = quarterTurned ? max.x - min.x : max.z - min.z;
            float smallest = LevelEditorSettings.Smallest;
            if (across >= smallest) size.x = across;
            if (along >= smallest) size.z = along;
            position = new Vector3((min.x + max.x) * 0.5f, dragStart.y, (min.z + max.z) * 0.5f);
        }

        void Place()
        {
            if (armed.How == Placeable.Placement.Start)
            {
                MoveStart(armed.Start, dragStart);
                Disarm();
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            Transform parent = LevelSetup.Group(scene, armed.Group);
            GameObject made;
            if (armed.Piece is LevelPiece.Kind kind)
            {
                Drawn(out Vector3 position, out Vector3 size);
                made = LevelPieces.Create(kind, armed.Name, parent, position, Turn, size).gameObject;
            }
            else made = Spawn(armed, parent, dragStart, Turn);
            Selection.activeGameObject = made;
        }

        /// <summary>Puts down a fixed piece's prefab, moved and turned into place.</summary>
        static GameObject Spawn(Placeable piece, Transform parent, Vector3 point, Quaternion turn)
        {
            GameObject prefab = piece.Prefab();
            GameObject made = LevelParts.Instance(prefab, parent, GameObjectUtility.GetUniqueNameForSibling(parent, prefab.name));
            // The prefab keeps its root where it stands on a floor at the origin.
            Transform root = prefab.transform;
            made.transform.SetPositionAndRotation(point + turn * (root.localPosition + piece.Raise), turn * root.localRotation);
            Undo.RegisterCreatedObjectUndo(made, $"Place {piece.Name}");
            return made;
        }

        static void MoveStart(Placeable.StartPoint which, Vector3 point)
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform blob = LevelSetup.Blob(scene);
            ShipBuoyancy ship = LevelSetup.Find<ShipBuoyancy>(scene);
            switch (which)
            {
                case Placeable.StartPoint.Blob:
                    if (blob == null)
                    {
                        Debug.LogWarning("[BounJam] There's no blob in this scene; make it a level first.");
                        return;
                    }
                    Undo.RecordObject(blob, "Move Blob Start");
                    blob.position = point + Vector3.up * CoreRadius;
                    if (LevelEditorSettings.ShipRidesBlob && ship != null)
                    {
                        Undo.RecordObject(ship.transform, "Move Blob Start");
                        ship.transform.SetPositionAndRotation(LevelSetup.OnBlob(point), Quaternion.identity);
                    }
                    Selection.activeGameObject = blob.gameObject;
                    break;
                case Placeable.StartPoint.Ship:
                    if (ship == null)
                    {
                        Debug.LogWarning("[BounJam] There's no ship in this scene; make it a level first.");
                        return;
                    }
                    Undo.RecordObject(ship.transform, "Move Ship Start");
                    // Set down just above the floor, so it settles onto it.
                    ship.transform.SetPositionAndRotation(point + Vector3.up * 0.02f, Turn);
                    Selection.activeGameObject = ship.gameObject;
                    break;
                case Placeable.StartPoint.Dock:
                    ShipDock dock = LevelSetup.Find<ShipDock>(scene);
                    if (dock == null)
                    {
                        dock = LevelSetup.PlaceDock(scene, point, ship);
                        Undo.RegisterCreatedObjectUndo(dock.gameObject, "Place Dock");
                        LevelManager manager = LevelSetup.Find<LevelManager>(scene);
                        if (manager != null) LevelSetup.FixReferences(manager);
                    }
                    else
                    {
                        Undo.RecordObject(dock.transform, "Move Dock");
                        dock.transform.position = point;
                    }
                    Selection.activeGameObject = dock.gameObject;
                    break;
            }
        }

        void DrawPreview(SceneView view)
        {
            Vector3 point = dragging ? dragStart : hover;
            if (dragging || hasHover)
            {
                using (new Handles.DrawingScope(PreviewColor))
                {
                    Vector3 position = point;
                    Bounds bounds = armed.Footprint;
                    string label = null;
                    if (armed.Piece is LevelPiece.Kind kind)
                    {
                        Vector3 size = armed.Size;
                        if (dragging) Drawn(out position, out size);
                        else if (armed.How == Placeable.Placement.Draw) label = "drag to draw";
                        bounds = LevelPieces.LocalBounds(kind, size);
                        if (dragging && armed.How == Placeable.Placement.Draw) label = $"{size.x:0.##} × {size.z:0.##} m";
                    }
                    using (new Handles.DrawingScope(Matrix4x4.TRS(position, Turn, Vector3.one)))
                    {
                        Handles.DrawWireCube(bounds.center, bounds.size);
                        if (armed.Directional)
                        {
                            float reach = Mathf.Max(1f, bounds.max.z + 0.5f);
                            Handles.DrawLine(Vector3.up * 0.05f, new Vector3(0f, 0.05f, reach), 2f);
                            Handles.ConeHandleCap(0, new Vector3(0f, 0.05f, reach), Quaternion.identity, 0.35f, EventType.Repaint);
                        }
                    }
                    Handles.DrawWireDisc(point, Vector3.up, 0.15f);
                    if (armed.OnBuildPlane) GridPatch(point);
                    if (label != null) Handles.Label(position + Vector3.up * (bounds.max.y + 0.4f), label, EditorStyles.whiteBoldLabel);
                }
            }

            Handles.BeginGUI();
            float height = view.camera.pixelRect.height / EditorGUIUtility.pixelsPerPoint;
            GUI.Label(new Rect(10f, height - 52f, 460f, 42f), $"Placing {armed.Name}. {Instructions(armed)}", EditorStyles.helpBox);
            Handles.EndGUI();
        }

        /// <summary>A patch of grid lines round the point on its height, so you can see where the build plane is.</summary>
        static void GridPatch(Vector3 center)
        {
            float step = Mathf.Max(1f, LevelEditorSettings.Grid);
            const int lines = 4;
            float reach = step * lines;
            using (new Handles.DrawingScope(new Color(PreviewColor.r, PreviewColor.g, PreviewColor.b, 0.25f)))
            {
                for (int i = -lines; i <= lines; i++)
                {
                    Handles.DrawLine(center + new Vector3(i * step, 0f, -reach), center + new Vector3(i * step, 0f, reach));
                    Handles.DrawLine(center + new Vector3(-reach, 0f, i * step), center + new Vector3(reach, 0f, i * step));
                }
            }
        }

        /// <summary>A dotted line from each button to the gate, lift or magnet it drives.</summary>
        static void DrawLinks()
        {
            foreach (ButtonMover mover in FindObjectsByType<ButtonMover>(FindObjectsSortMode.None))
                DrawLinks(mover, "buttons", GateLinkColor);
            foreach (Magnet magnet in FindObjectsByType<Magnet>(FindObjectsSortMode.None))
            {
                DrawLinks(magnet, "onButtons", MagnetOnLinkColor);
                DrawLinks(magnet, "offButtons", MagnetOffLinkColor);
            }
        }

        static void DrawLinks(Component machine, string list, Color color)
        {
            List<PressureButton> buttons = Read(new SerializedObject(machine).FindProperty(list));
            if (buttons.Count == 0) return;
            using (new Handles.DrawingScope(color))
            {
                Vector3 to = machine.transform.position;
                foreach (PressureButton button in buttons)
                {
                    Vector3 from = button.transform.position + Vector3.up * 0.1f;
                    Handles.DrawDottedLine(from, to, 4f);
                    Handles.DrawWireDisc(from, Vector3.up, 0.6f);
                }
            }
        }

        // ---------------------------------------------------------------- Whole pieces

        void OnSelectionChanged()
        {
            Repaint();
            if (!LevelEditorSettings.SelectWholePieces || EditorApplication.timeSinceStartup - lastSceneClick > 1.0) return;
            GameObject[] selected = Selection.gameObjects;
            GameObject[] widened = selected.Select(PieceRoot).Distinct().ToArray();
            if (!widened.SequenceEqual(selected)) Selection.objects = widened;
        }

        /// <summary>
        /// The whole piece a clicked part belongs to: its <see cref="LevelPiece"/>, else the nearest object up the
        /// hierarchy that's a gameplay piece (a game script, a rigidbody or a prefab), else, for a part that's only
        /// seen, the object holding its collider.
        /// </summary>
        static GameObject PieceRoot(GameObject go)
        {
            var piece = go.GetComponentInParent<LevelPiece>(true);
            if (piece != null) return piece.gameObject;
            for (Transform t = go.transform; t != null; t = t.parent)
                if (IsGamePiece(t.gameObject)) return t.gameObject;
            if (go.GetComponent<Collider>() == null && go.GetComponents<MonoBehaviour>().Length == 0 && go.transform.parent != null)
            {
                Collider holder = go.transform.parent.GetComponentInParent<Collider>(true);
                if (holder != null) return holder.gameObject;
            }
            return go;
        }

        static bool IsGamePiece(GameObject go)
            => go.GetComponent<Rigidbody>() != null
               || PrefabUtility.IsAnyPrefabInstanceRoot(go)
               || go.GetComponents<MonoBehaviour>().Any(b => b != null && b.GetType().Namespace == nameof(BounJam));
    }
}
