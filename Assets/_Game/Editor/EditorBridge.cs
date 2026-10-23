using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Lets tools outside Unity drive the editor through command files, so changes can be imported and
    /// checked without switching windows. Write one command into Temp/EditorBridge/in/&lt;name&gt;.cmd; the result
    /// appears in Temp/EditorBridge/out/&lt;name&gt;.txt. Files are handled one per poll in name order.
    ///   refresh                                     import changed files (and recompile scripts)
    ///   menu &lt;Menu/Path&gt;                            run a menu item, e.g. "menu BounJam/Build Sandbox Level"
    ///   shot &lt;png&gt; [w h] [x y z pitch yaw]          render the main camera, or a temporary camera at that pose
    ///   status                                      compile and play mode state
    ///   wait &lt;seconds&gt;                              delay the next command (real time)
    ///   play | stop                                 enter or leave play mode
    ///   waitgame &lt;seconds&gt;                          delay the next command (game time, play mode)
    ///   stick &lt;x&gt; &lt;y&gt;                              override the player's Move input; "stick 0 0" holds still
    ///   probe &lt;GameObject name&gt;                     position and rigidbody velocity of an object
    /// An unfocused editor doesn't run the game loop, so play sessions started here stay paused and are stepped
    /// one frame per editor update during "waitgame" (0.02 s of game time per frame).
    /// </summary>
    [InitializeOnLoad]
    static class EditorBridge
    {
        const string InDir = "Temp/EditorBridge/in";
        const string OutDir = "Temp/EditorBridge/out";
        const string BridgePlayKey = "EditorBridge.PlaySession";
        const double PollInterval = 0.5;

        static double nextPoll;
        static float waitUntilGameTime;

        static EditorBridge()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) SessionState.EraseBool(BridgePlayKey);
            };
        }

        static void Poll()
        {
            // Bridge play sessions stay frozen between commands and only advance during "waitgame",
            // so game-time results are exact no matter how fast the editor updates.
            bool bridgePlay = EditorApplication.isPlaying && SessionState.GetBool(BridgePlayKey, false);
            if (bridgePlay)
            {
                EditorApplication.isPaused = true;
                if (Time.time < waitUntilGameTime)
                {
                    EditorApplication.Step();
                    return;
                }
            }

            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + (bridgePlay ? 0 : PollInterval);
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !Directory.Exists(InDir)) return;
            // Mid-transition into or out of play mode: wait until it settles.
            if (EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying) return;

            string file = Directory.GetFiles(InDir, "*.cmd").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
            if (file == null) return;

            string command = File.ReadAllText(file).Trim();
            File.Delete(file);

            Action afterReport = null;
            string result;
            try { result = Execute(command, ref afterReport); }
            catch (Exception e) { result = "error: " + e; }

            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, Path.GetFileNameWithoutExtension(file) + ".txt"), result);
            // Runs after the report so a domain reload it triggers can't swallow the result.
            afterReport?.Invoke();
        }

        static string Execute(string command, ref Action afterReport)
        {
            int space = command.IndexOf(' ');
            string verb = space < 0 ? command : command.Substring(0, space);
            string rest = space < 0 ? "" : command.Substring(space + 1).Trim();
            string[] args = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            switch (verb)
            {
                case "refresh":
                    afterReport = () => AssetDatabase.Refresh();
                    return "ok: refreshing";
                case "menu":
                    return EditorApplication.ExecuteMenuItem(rest) ? $"ok: ran {rest}" : $"error: menu item not found or disabled: {rest}";
                case "shot":
                    return Shot(args);
                case "status":
                    return $"ok: compiling={EditorApplication.isCompiling} compileFailed={EditorUtility.scriptCompilationFailed} " +
                           $"playing={EditorApplication.isPlaying} frame={Time.frameCount} " +
                           $"time={Time.time.ToString("0.00", CultureInfo.InvariantCulture)} scene={SceneManager.GetActiveScene().path}";
                case "wait":
                    nextPoll = EditorApplication.timeSinceStartup + ParseFloat(rest);
                    return $"ok: waiting {rest}s";
                case "play":
                    afterReport = () =>
                    {
                        SessionState.SetBool(BridgePlayKey, true);
                        EditorApplication.isPlaying = true;
                    };
                    return "ok: entering play mode";
                case "stop":
                    afterReport = () => EditorApplication.isPlaying = false;
                    return "ok: leaving play mode";
                case "waitgame":
                    if (!EditorApplication.isPlaying) return "error: waitgame needs play mode";
                    waitUntilGameTime = Time.time + ParseFloat(rest);
                    return $"ok: waiting {rest}s of game time";
                case "stick":
                    return Stick(args);
                case "probe":
                    return Probe(rest);
                default:
                    return $"error: unknown command '{verb}'";
            }
        }

        static string Stick(string[] args)
        {
            if (!EditorApplication.isPlaying) return "error: stick needs play mode";
            var reader = UnityEngine.Object.FindAnyObjectByType<PlayerInputReader>();
            if (reader == null) return "error: no PlayerInputReader in the scene";
            var stick = new Vector2(ParseFloat(args[0]), ParseFloat(args[1]));
            reader.MoveOverride = stick;
            return $"ok: move override {Format(stick)}";
        }

        static string Probe(string name)
        {
            GameObject target = GameObject.Find(name);
            if (target == null) return $"error: no active GameObject named '{name}'";
            string result = $"ok: {name} position={Format(target.transform.position)}";
            if (target.TryGetComponent(out Rigidbody body))
                result += $" velocity={Format(body.linearVelocity)} speed={body.linearVelocity.magnitude.ToString("0.00", CultureInfo.InvariantCulture)}";
            return result;
        }

        static string Shot(string[] args)
        {
            string path = args.Length > 0 ? args[0] : "Temp/EditorBridge/shot.png";
            int width = args.Length >= 3 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1280;
            int height = args.Length >= 3 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 720;

            Camera main = Camera.main;
            if (main == null) return "error: no camera tagged MainCamera";

            Camera camera = main;
            GameObject temporary = null;
            if (args.Length >= 8)
            {
                temporary = new GameObject("EditorBridge Shot") { hideFlags = HideFlags.HideAndDontSave };
                camera = temporary.AddComponent<Camera>();
                camera.CopyFrom(main);
                temporary.transform.SetPositionAndRotation(
                    new Vector3(ParseFloat(args[3]), ParseFloat(args[4]), ParseFloat(args[5])),
                    Quaternion.Euler(ParseFloat(args[6]), ParseFloat(args[7]), 0f));
            }

            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();

                string fullPath = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllBytes(fullPath, image.EncodeToPNG());
                return $"ok: saved {fullPath}";
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
            }
        }

        static float ParseFloat(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static string Format(Vector2 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00})", v.x, v.y);

        static string Format(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", v.x, v.y, v.z);
    }
}
