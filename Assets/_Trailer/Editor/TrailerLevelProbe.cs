using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BounJam.Trailer
{
    /// <summary>
    /// Trailer/Dump Levels: writes where everything that matters for filming sits in each build scene (the blob, ship,
    /// dock, machines, pickups, cameras) to Temp/Trailer/levels/&lt;scene&gt;.txt, world space, with render bounds.
    /// Scenes are opened additively and closed without saving.
    /// </summary>
    static class TrailerLevelProbe
    {
        const string OutDir = "Temp/Trailer/levels";

        static readonly Type[] Interesting =
        {
            typeof(LevelManager), typeof(BlobMotor), typeof(ShipBuoyancy), typeof(ShipDock), typeof(PressureButton),
            typeof(Drawbridge), typeof(ButtonMover), typeof(Magnet), typeof(Conveyor), typeof(ConveyorSpike),
            typeof(PiercerPipe), typeof(Water), typeof(WaterDroplet), typeof(Seed), typeof(IronBall), typeof(Collectible),
            typeof(Soil), typeof(FlowerLift), typeof(FloorGrate), typeof(FreezeLockZone), typeof(HazardZone),
            typeof(FinishFlag), typeof(Pushable), typeof(Camera), typeof(Light), typeof(MenuBlobStunt), typeof(MenuCameraOrbit),
            typeof(Unity.Cinemachine.CinemachineCamera), typeof(Unity.Cinemachine.CinemachineBrain),
        };

        [MenuItem("Trailer/Dump Levels")]
        static void DumpAll()
        {
            Directory.CreateDirectory(OutDir);
            var report = new StringBuilder();
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            {
                if (!entry.enabled) continue;
                report.AppendLine(Dump(entry.path));
            }
            string sandbox = "Assets/_Game/Scenes/Sandbox.unity";
            if (File.Exists(sandbox) && EditorBuildSettings.scenes.All(s => s.path != sandbox)) report.AppendLine(Dump(sandbox));
            File.WriteAllText(Path.Combine(OutDir, "_index.txt"), report.ToString());
        }

        const string MapDir = "Temp/Trailer/maps";

        /// <summary>
        /// Trailer/Map Levels: a straight-down orthographic picture of each level (&lt;scene&gt;_top.png, with the world
        /// rectangle it covers in &lt;scene&gt;_top.txt) and one from the level's own main camera (&lt;scene&gt;_game.png).
        /// </summary>
        [MenuItem("Trailer/Map Levels")]
        static void MapAll()
        {
            Directory.CreateDirectory(MapDir);
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
                if (entry.enabled) Map(entry.path);
        }

        static void Map(string path)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var bounds = new Bounds();
                bool any = false;
                Camera main = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                    {
                        // The sea is far bigger than the level; leave it out of the framing.
                        if (renderer is ParticleSystemRenderer || renderer.GetComponentInParent<Water>() != null ||
                            renderer.bounds.size.magnitude > 80f) continue;
                        if (!any) { bounds = renderer.bounds; any = true; }
                        else bounds.Encapsulate(renderer.bounds);
                    }
                    if (main == null) main = root.GetComponentsInChildren<Camera>().FirstOrDefault(c => c.CompareTag("MainCamera"));
                }
                if (!any) return;
                bounds.Expand(new Vector3(2f, 0f, 2f));
                string name = Path.GetFileNameWithoutExtension(path);

                const int longSide = 1800;
                float aspect = bounds.size.x / bounds.size.z;
                int width = aspect >= 1f ? longSide : Mathf.RoundToInt(longSide * aspect);
                int height = aspect >= 1f ? Mathf.RoundToInt(longSide / aspect) : longSide;
                var go = new GameObject("Trailer Map Camera") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    Camera camera = go.AddComponent<Camera>();
                    if (main != null) camera.CopyFrom(main);
                    camera.scene = scene;
                    camera.orthographic = true;
                    camera.orthographicSize = bounds.extents.z;
                    camera.aspect = aspect;
                    camera.nearClipPlane = 0.1f;
                    camera.farClipPlane = bounds.size.y + 60f;
                    camera.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.max.y + 30f, bounds.center.z),
                                                            Quaternion.Euler(90f, 0f, 0f));
                    Render(camera, width, height, Path.Combine(MapDir, name + "_top.png"));
                    File.WriteAllText(Path.Combine(MapDir, name + "_top.txt"), string.Format(CultureInfo.InvariantCulture,
                        "image {0}x{1} px, straight down, +X right, +Z up\nworld x {2:0.##} .. {3:0.##} (left to right)\nworld z {4:0.##} .. {5:0.##} (bottom to top)\n{6:0.###} px per metre\n",
                        width, height, bounds.min.x, bounds.max.x, bounds.min.z, bounds.max.z, width / bounds.size.x));

                    if (main != null)
                    {
                        camera.CopyFrom(main);
                        camera.scene = scene;
                        camera.aspect = 16f / 9f;
                        Render(camera, 1280, 720, Path.Combine(MapDir, name + "_game.png"));
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Trailer/Height Maps: the height of the top solid surface on a 0.5 m grid over each level
        /// (&lt;scene&gt;_heights.txt): rows from +Z (top) to -Z, columns from -X to +X; "." where nothing is under (the sea).
        /// </summary>
        [MenuItem("Trailer/Height Maps")]
        static void HeightAll()
        {
            Directory.CreateDirectory(MapDir);
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
                if (entry.enabled) Heights(entry.path);
        }

        static void Heights(string path)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                Physics.SyncTransforms();
                var bounds = new Bounds();
                bool any = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Collider c in root.GetComponentsInChildren<Collider>())
                    {
                        if (c.isTrigger || c.bounds.size.magnitude > 80f) continue;
                        if (!any) { bounds = c.bounds; any = true; }
                        else bounds.Encapsulate(c.bounds);
                    }
                if (!any) return;
                const float step = 0.5f;
                float x0 = Mathf.Floor(bounds.min.x) - 1f, x1 = Mathf.Ceil(bounds.max.x) + 1f;
                float z0 = Mathf.Floor(bounds.min.z) - 1f, z1 = Mathf.Ceil(bounds.max.z) + 1f;
                var text = new StringBuilder();
                text.AppendLine($"# {scene.name}: top solid surface height (m) every {step} m; '.' = nothing below (sea). Row label = z, column header = x.");
                text.Append("z\\x");
                for (float x = x0; x <= x1 + 0.01f; x += step) text.Append(string.Format(CultureInfo.InvariantCulture, " {0,5:0.0}", x));
                text.AppendLine();
                for (float z = z1; z >= z0 - 0.01f; z -= step)
                {
                    text.Append(string.Format(CultureInfo.InvariantCulture, "{0,5:0.0}", z));
                    for (float x = x0; x <= x1 + 0.01f; x += step)
                    {
                        float best = float.NegativeInfinity;
                        foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(x, bounds.max.y + 5f, z), Vector3.down, bounds.size.y + 30f,
                                                                       ~0, QueryTriggerInteraction.Ignore))
                            if (hit.collider.gameObject.scene == scene && hit.point.y > best) best = hit.point.y;
                        text.Append(float.IsNegativeInfinity(best) ? "     ." : string.Format(CultureInfo.InvariantCulture, " {0,5:0.0}", best));
                    }
                    text.AppendLine();
                }
                File.WriteAllText(Path.Combine(MapDir, scene.name + "_heights.txt"), text.ToString());
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void Render(Camera camera, int width, int height, string file)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(file, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        static string Dump(string path)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var text = new StringBuilder();
            try
            {
                text.AppendLine($"# {scene.name} ({path})");
                var bounds = new Bounds();
                bool any = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer is ParticleSystemRenderer || renderer.bounds.size.magnitude > 500f) continue;
                        if (!any) { bounds = renderer.bounds; any = true; }
                        else bounds.Encapsulate(renderer.bounds);
                    }
                text.AppendLine($"level bounds: center={V(bounds.center)} size={V(bounds.size)}");

                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        Component[] found = Interesting.Select(type => t.GetComponent(type)).Where(c => c != null).ToArray();
                        if (found.Length == 0) continue;
                        // Collectible is the base of the pickups; list a pickup once.
                        string kinds = string.Join(",", found.Select(c => c.GetType().Name).Distinct());
                        text.Append($"{PathOf(t)} [{kinds}] active={t.gameObject.activeInHierarchy} pos={V(t.position)} rot={V(t.eulerAngles)} scale={V(t.lossyScale)}");
                        Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
                        if (renderers.Length > 0)
                        {
                            Bounds b = renderers[0].bounds;
                            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                            text.Append($" renderBounds(center={V(b.center)} size={V(b.size)})");
                        }
                        foreach (Component c in found) text.Append(Details(c));
                        text.AppendLine();
                    }
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
            File.WriteAllText(Path.Combine(OutDir, Path.GetFileNameWithoutExtension(path) + ".txt"), text.ToString());
            return text.ToString();
        }

        /// <summary>The serialized fields that tell how a machine is wired (references by name, numbers, flags).</summary>
        static string Details(Component component)
        {
            if (component is Light) return "";
            if (component is Camera camera) return $" {{Camera: fov={camera.fieldOfView:0.#}}}";
            var text = new StringBuilder();
            var serialized = new SerializedObject(component);
            SerializedProperty property = serialized.GetIterator();
            var parts = new List<string>();
            if (property.NextVisible(true))
            {
                do
                {
                    if (property.name == "m_Script") continue;
                    string value = Value(property);
                    if (value != null) parts.Add($"{property.name}={value}");
                } while (property.NextVisible(false) && parts.Count < 24);
            }
            if (parts.Count > 0) text.Append($" {{{component.GetType().Name}: {string.Join("; ", parts)}}}");
            return text.ToString();
        }

        static string Value(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean: return property.boolValue ? "true" : "false";
                case SerializedPropertyType.Integer: return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Float: return property.floatValue.ToString("0.###", CultureInfo.InvariantCulture);
                case SerializedPropertyType.String: return string.IsNullOrEmpty(property.stringValue) ? null : $"\"{property.stringValue}\"";
                case SerializedPropertyType.Enum: return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                    ? property.enumDisplayNames[property.enumValueIndex] : property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Vector3: return V(property.vector3Value);
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue == null ? null : property.objectReferenceValue.name;
                case SerializedPropertyType.Generic when property.isArray && property.arraySize > 0 && property.arraySize <= 12:
                    var items = new List<string>();
                    for (int i = 0; i < property.arraySize; i++)
                    {
                        SerializedProperty item = property.GetArrayElementAtIndex(i);
                        items.Add(item.propertyType == SerializedPropertyType.ObjectReference
                            ? item.objectReferenceValue != null ? item.objectReferenceValue.name : "null"
                            : Value(item) ?? "?");
                    }
                    return "[" + string.Join(",", items) + "]";
                default: return null;
            }
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##},{2:0.##})", v.x, v.y, v.z);
    }
}
