using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The Windows release for itch.io: player settings for a desktop game, then a 64-bit build of the scenes in the
    /// run (the ones on in Build Settings, without the sandbox, so the main menu goes in first once it's there), zipped
    /// ready to upload. The settings are applied before every build, so any checkout builds the same game.
    /// The scripting backend is Mono, not IL2CPP, so it builds on Windows and from the Linux editor alike (there with
    /// Windows Build Support (Mono) added in Unity Hub). From a shell, with the editor closed:
    ///   Unity -batchmode -quit -projectPath . -buildTarget Win64 -executeMethod BounJam.EditorTools.WindowsBuild.BuildFromCommandLine -logFile -
    /// </summary>
    public static class WindowsBuild
    {
        const string CompanyName = "Hangovers";
        const string ProductName = "ShapeLock";
        const string ReleaseFolder = "Builds/Windows";
        const string DevelopmentFolder = "Builds/Windows-Dev";
        const string PcQualityLevel = "PC";

        [MenuItem("BounJam/Windows Build/Build for itch.io")]
        public static void BuildRelease() => Build(development: false);

        [MenuItem("BounJam/Windows Build/Development Build")]
        public static void BuildDevelopment() => Build(development: true);

        [MenuItem("BounJam/Windows Build/Apply Player Settings")]
        public static void ApplyPlayerSettingsAndSave()
        {
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[BounJam] Windows player settings applied.");
        }

        /// <summary>The release build for -executeMethod; the editor exits with 1 if it failed.</summary>
        public static void BuildFromCommandLine() => EditorApplication.Exit(Build(development: false) != null ? 0 : 1);

        /// <summary>
        /// Borderless full screen at the desktop's resolution, with Alt+Enter for a resizable window. Direct3D 11, which
        /// every Windows GPU of the last decade runs. One copy of the game at a time, and the PC quality level in sync
        /// with the monitor.
        /// </summary>
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;

            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.forceSingleInstance = true;
            PlayerSettings.usePlayerLog = true; // %USERPROFILE%\AppData\LocalLow\Hangovers\ShapeLock\Player.log, for bug reports

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });

            SetPcVSync();
        }

        /// <summary>Builds the game and zips a release build for upload. Returns the zip (the exe for a development build), or null if it failed.</summary>
        static string Build(bool development)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                return Fail("Windows Build Support isn't installed for this editor. Add it in Unity Hub: Installs, " +
                            $"{Application.unityVersion}, Manage, Add modules, Windows Build Support (Mono).");

            string[] scenes = LevelSetup.PlayOrder().ToArray();
            if (scenes.Length == 0) return Fail("No scenes are on in Build Settings.");
            string missing = scenes.FirstOrDefault(s => !File.Exists(s));
            if (missing != null) return Fail($"{missing} is on in Build Settings, but the scene file is missing.");
            // The build reads scenes from disk, so unsaved edits wouldn't be in it.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;

            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();

            string folder = development ? DevelopmentFolder : ReleaseFolder;
            string gameFolder = Path.Combine(folder, ProductName);
            // Nothing left over from an older build ships.
            if (Directory.Exists(gameFolder)) Directory.Delete(gameFolder, true);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(gameFolder, ProductName + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.CompressWithLz4HC,
            };

            var timer = Stopwatch.StartNew();
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                return Fail($"The Windows build {report.summary.result.ToString().ToLowerInvariant()} " +
                            $"with {report.summary.totalErrors} errors. The Console has them.");

            // Burst's and IL2CPP's debug folders sit next to the exe and aren't part of the game.
            foreach (string debugFolder in Directory.GetDirectories(gameFolder).Where(d => d.EndsWith("DoNotShip") || d.EndsWith("DontShipItWithYourGame")))
                Directory.Delete(debugFolder, true);

            string summary = $"{scenes.Length} scenes ({string.Join(", ", scenes.Select(Path.GetFileNameWithoutExtension))}), " +
                             $"{timer.Elapsed.TotalMinutes:0.0} min";
            if (development)
            {
                Debug.Log($"[BounJam] Windows development build done: {options.locationPathName}, {summary}.");
                Reveal(options.locationPathName);
                return options.locationPathName;
            }

            string zip = Path.Combine(folder, $"{ProductName}-Windows-v{PlayerSettings.bundleVersion}.zip");
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(gameFolder, zip, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: true);
            Debug.Log($"[BounJam] Windows build for itch.io done: {zip}, {new FileInfo(zip).Length / (1024f * 1024f):0} MB, {summary}. " +
                      "Upload the zip to itch.io and tick Windows as its platform.");
            Reveal(zip);
            return zip;
        }

        /// <summary>V-sync on the PC quality level, so the game runs at the monitor's refresh rate instead of as fast as the GPU can.</summary>
        static void SetPcVSync()
        {
            var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            SerializedProperty levels = quality.FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                if (level.FindPropertyRelative("name").stringValue != PcQualityLevel) continue;
                level.FindPropertyRelative("vSyncCount").intValue = 1;
                quality.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
            Debug.LogWarning($"[BounJam] There's no '{PcQualityLevel}' quality level, so v-sync is left as it was.");
        }

        static void Reveal(string path)
        {
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(path);
        }

        static string Fail(string message)
        {
            Debug.LogError("[BounJam] " + message);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("Windows build", message, "OK");
            return null;
        }
    }
}
