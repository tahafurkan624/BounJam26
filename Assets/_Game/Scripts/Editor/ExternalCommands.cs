using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Lets tools outside Unity (such as Claude Code) drive the open editor. Each *.cmd file dropped in
    /// Library/BounJamCommands holds one command; files run in name order and are deleted as they start.
    /// Commands: refresh | call Namespace.Type.Method | open Assets/Scene.unity | play | stop |
    /// wait seconds | screenshot path | hierarchy. Results go to the Console and Editor.log.
    /// </summary>
    [InitializeOnLoad]
    static class ExternalCommands
    {
        const string Folder = "Library/BounJamCommands";
        const string ResumeAtKey = "BounJam.ExternalCommands.ResumeAt";
        static double nextPoll;

        static ExternalCommands()
        {
            Directory.CreateDirectory(Folder);
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll || now < SessionState.GetFloat(ResumeAtKey, 0f)
                || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            nextPoll = now + 0.25;

            string file = Directory.GetFiles(Folder, "*.cmd").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
            if (file == null)
                return;
            string command = File.ReadAllText(file).Trim();
            File.Delete(file); // before running, since a command may reload the domain
            Debug.Log($"[BounJam] External command: {command}");
            try
            {
                Run(command);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void Run(string command)
        {
            int space = command.IndexOf(' ');
            string verb = space < 0 ? command : command.Substring(0, space);
            string argument = space < 0 ? "" : command.Substring(space + 1);
            switch (verb)
            {
                case "refresh":
                    AssetDatabase.Refresh();
                    break;
                case "call":
                    Call(argument);
                    break;
                case "open":
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        EditorSceneManager.OpenScene(argument);
                    break;
                case "play":
                    EditorApplication.EnterPlaymode();
                    break;
                case "stop":
                    EditorApplication.ExitPlaymode();
                    break;
                case "wait":
                    float seconds = float.Parse(argument, CultureInfo.InvariantCulture);
                    SessionState.SetFloat(ResumeAtKey, (float)(EditorApplication.timeSinceStartup + seconds));
                    break;
                case "screenshot":
                    ScreenCapture.CaptureScreenshot(argument);
                    break;
                case "hierarchy":
                    LogHierarchy();
                    break;
                default:
                    Debug.LogError($"[BounJam] Unknown external command '{verb}'.");
                    break;
            }
        }

        static void LogHierarchy()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                string roots = string.Join(", ", scene.GetRootGameObjects().Select(go => go.name));
                Debug.Log($"[BounJam] Scene {scene.path} (unsaved changes: {scene.isDirty}): {roots}");
            }
        }

        static void Call(string qualifiedMethod)
        {
            int dot = qualifiedMethod.LastIndexOf('.');
            string typeName = qualifiedMethod.Substring(0, dot);
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).First(t => t != null);
            type.GetMethod(qualifiedMethod.Substring(dot + 1), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(null, null);
        }
    }
}
