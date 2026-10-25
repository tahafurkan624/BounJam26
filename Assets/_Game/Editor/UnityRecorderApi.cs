using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The parts of Unity Recorder (com.unity.recorder) the FMOD Recorder uses, reached by reflection so the project
    /// still compiles on machines without the package; there <see cref="Installed"/> is false and the rest isn't called.
    /// The window and settings objects are passed around as their Unity base types.
    /// </summary>
    static class UnityRecorderApi
    {
        const string Assembly = ", Unity.Recorder.Editor";
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly Type WindowType = Type.GetType("UnityEditor.Recorder.RecorderWindow" + Assembly);
        static readonly Type ControllerSettingsType = Type.GetType("UnityEditor.Recorder.RecorderControllerSettings" + Assembly);
        static readonly Type MovieType = Type.GetType("UnityEditor.Recorder.MovieRecorderSettings" + Assembly);

        public static bool Installed => WindowType != null && ControllerSettingsType != null && MovieType != null;

        /// <summary>The open Recorder window, or null.</summary>
        public static EditorWindow FindWindow() => Resources.FindObjectsOfTypeAll(WindowType).FirstOrDefault() as EditorWindow;

        public static EditorWindow OpenWindow() => EditorWindow.GetWindow(WindowType, false, "Recorder", false);

        public static bool IsRecording(EditorWindow window) => (bool)Call(window, "IsRecording");

        public static void StopRecording(EditorWindow window) => Call(window, "StopRecording");

        public static void StartRecording(EditorWindow window)
        {
            // A take that failed leaves the window in an error state that only its own record button clears, and
            // until then StartRecording does nothing.
            FieldInfo state = WindowType.GetField("m_State", Instance);
            if (state != null && state.FieldType.IsEnum && state.GetValue(window).ToString() == "Error")
                state.SetValue(window, Enum.Parse(state.FieldType, "Idle"));
            Call(window, "StartRecording");
        }

        /// <summary>A fresh copy of the settings the Recorder window saved (Library/Recorder/recorder.pref).</summary>
        public static ScriptableObject LoadSettings() =>
            (ScriptableObject)ControllerSettingsType.GetMethod("GetGlobalSettings", BindingFlags.Static | BindingFlags.Public).Invoke(null, null);

        public static void SaveSettings(ScriptableObject settings) => Call(settings, "Save");

        /// <summary>Hands the window <paramref name="settings"/>, which it then records with.</summary>
        public static void UseSettings(EditorWindow window, ScriptableObject settings) =>
            Call(window, "SetRecorderControllerSettings", settings);

        /// <summary>Destroys a copy from <see cref="LoadSettings"/>, with its recorders.</summary>
        public static void Discard(ScriptableObject settings)
        {
            foreach (ScriptableObject recorder in Recorders(settings).ToList()) UnityEngine.Object.DestroyImmediate(recorder);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        /// <summary>The enabled movie recorders in <paramref name="settings"/>.</summary>
        public static IEnumerable<ScriptableObject> Movies(ScriptableObject settings) =>
            Recorders(settings).Where(recorder => MovieType.IsInstanceOfType(recorder) && (bool)Get(recorder, "Enabled"));

        /// <summary>The recorder's output path, folder and file name; the name may hold wildcards like &lt;Take&gt;.</summary>
        public static string OutputFile(ScriptableObject recorder) => (string)Get(recorder, "OutputFile");

        public static bool CaptureAudio(ScriptableObject movie) => (bool)Get(movie, "CaptureAudio");

        public static void SetCaptureAudio(ScriptableObject movie, bool on) =>
            movie.GetType().GetProperty("CaptureAudio", Instance).SetValue(movie, on);

        static IEnumerable<ScriptableObject> Recorders(ScriptableObject settings) =>
            ((IEnumerable)Get(settings, "RecorderSettings")).Cast<ScriptableObject>();

        static object Get(object target, string property) => target.GetType().GetProperty(property, Instance).GetValue(target);

        static object Call(object target, string method, params object[] arguments)
        {
            Type[] types = arguments.Select(argument => argument.GetType()).ToArray();
            MethodInfo info = target.GetType().GetMethod(method, Instance, null, types, null)
                              ?? throw new MissingMethodException(target.GetType().Name, method);
            return info.Invoke(target, arguments);
        }
    }
}
