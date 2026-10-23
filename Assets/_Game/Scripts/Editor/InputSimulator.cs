using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Simulates keyboard and mouse input during Play Mode for automated play-tests, also while the
    /// editor window is not focused. Input comes from two virtual devices (a Keyboard and a Mouse that
    /// exist only while a simulation runs), so every binding such as "&lt;Keyboard&gt;/space" or
    /// "&lt;Pointer&gt;/delta" sees it and the real devices are left alone. While a simulation runs,
    /// InputSystem.settings is swapped for an in-memory copy that ignores focus. The original comes back
    /// on End(), when Play Mode exits, and before a domain reload. Nothing is written to an asset.
    /// </summary>
    [InitializeOnLoad]
    public static class InputSimulator
    {
        const string KeyboardName = "BounJamSimKeyboard";
        const string MouseName = "BounJamSimMouse";
        const string SettingsName = "BounJamSimInputSettings";
        const string SettingsConfigKey = "com.unity.input.settings"; // InputSettingsProvider.kEditorBuildSettingsConfigKey (internal)
        const string SessionActive = "BounJam.InputSimulator.Active";
        const string SessionBackground = "BounJam.InputSimulator.BackgroundBehavior";
        const string SessionEditorBehavior = "BounJam.InputSimulator.EditorBehavior";
        const double StallWarningSeconds = 2.0;

        sealed class Press
        {
            public InputControl<float> Control;
            public float Value;
            public double Until;   // realtime; used when FramesLeft < 0
            public int FramesLeft; // -1 means time based
            public bool Sent;
        }

        static readonly List<Press> presses = new();
        static Keyboard keyboard;
        static Mouse mouse;
        static InputSettings originalSettings;
        static InputSettings simSettings;
        static bool mouseMoving;
        static Vector2 mouseDelta;
        static double mouseUntil;
        static int mouseFramesLeft;
        static bool mouseSent;
        static float pendingScroll;
        static double lastPlayerUpdate;
        static bool warnedStall;
        static bool warnedSettings;

        /// <summary>True between Begin() and End().</summary>
        public static bool IsActive => simSettings != null;

        /// <summary>True while a key or button is held, the mouse is moving or a scroll is pending.</summary>
        public static bool IsBusy => presses.Count > 0 || mouseMoving || pendingScroll != 0f;

        static InputSimulator()
        {
            EditorApplication.update += EditorTick;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += End;
            // If a domain reload still caught a simulation running, undo what it left behind.
            if (SessionState.GetBool(SessionActive, false))
                EditorApplication.delayCall += CleanUpAfterLostState;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Starts simulating. Called automatically by the other methods. Play Mode only.</summary>
        public static void Begin()
        {
            if (IsActive)
                return;
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("[BounJam] InputSimulator works only in Play Mode.");

            originalSettings = InputSystem.settings;
            SessionState.SetInt(SessionBackground, (int)originalSettings.backgroundBehavior);
            SessionState.SetInt(SessionEditorBehavior, (int)originalSettings.editorInputBehaviorInPlayMode);
            SessionState.SetBool(SessionActive, true);

            // An in-memory copy: it has no asset path, so InputSystem.settings does not register it in
            // EditorBuildSettings. Properties are set before assigning, so settings apply once.
            simSettings = Object.Instantiate(originalSettings);
            simSettings.name = SettingsName;
            simSettings.hideFlags = HideFlags.HideAndDontSave;
            simSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            simSettings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = simSettings;

            RemoveSimDevices();
            keyboard = InputSystem.AddDevice<Keyboard>(KeyboardName);
            mouse = InputSystem.AddDevice<Mouse>(MouseName);

            lastPlayerUpdate = Time.realtimeSinceStartupAsDouble;
            warnedStall = false;
            warnedSettings = false;
            InputSystem.onBeforeUpdate += OnBeforeInputUpdate;
            Debug.Log("[BounJam] InputSimulator started.");
        }

        /// <summary>Releases everything, removes the virtual devices and restores the input settings.</summary>
        public static void End()
        {
            if (!IsActive)
                return;

            InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;
            presses.Clear();
            mouseMoving = false;
            pendingScroll = 0f;
            RemoveSimDevices();
            keyboard = null;
            mouse = null;

            if (InputSystem.settings == simSettings)
                InputSystem.settings = originalSettings != null ? originalSettings : RebuildOriginalSettings(simSettings);
            Object.DestroyImmediate(simSettings);
            simSettings = null;
            originalSettings = null;
            ClearSession();
            Debug.Log("[BounJam] InputSimulator stopped; input settings restored.");
        }

        /// <summary>
        /// Holds a key or button for some seconds (at least one frame). Paths: "&lt;Keyboard&gt;/space",
        /// "&lt;Mouse&gt;/leftButton", or a bare control name such as "w" or "leftButton".
        /// </summary>
        public static void Hold(string controlPath, float seconds, float value = 1f) =>
            AddPress(controlPath, value, Time.realtimeSinceStartupAsDouble + Math.Max(0f, seconds), -1);

        /// <summary>Holds a key or button for exactly this many player frames.</summary>
        public static void HoldFrames(string controlPath, int frames, float value = 1f) =>
            AddPress(controlPath, value, 0d, Math.Max(1, frames));

        /// <summary>Presses for one frame: WasPressedThisFrame is true in that frame's Update.</summary>
        public static void Tap(string controlPath) => HoldFrames(controlPath, 1);

        public static void Release(string controlPath)
        {
            if (!IsActive)
                return;
            InputControl<float> control = Resolve(controlPath);
            presses.RemoveAll(p => p.Control == control);
        }

        public static void ReleaseAll()
        {
            presses.Clear();
            mouseMoving = false;
            pendingScroll = 0f;
        }

        /// <summary>Feeds this mouse delta (pixels) into every player frame for some seconds (at least one frame).</summary>
        public static void MoveMouse(Vector2 deltaPerFrame, float seconds) =>
            StartMouse(deltaPerFrame, Time.realtimeSinceStartupAsDouble + Math.Max(0f, seconds), -1);

        /// <summary>Feeds this mouse delta into exactly this many player frames (frame-rate independent total).</summary>
        public static void MoveMouseFrames(Vector2 deltaPerFrame, int frames) =>
            StartMouse(deltaPerFrame, 0d, Math.Max(1, frames));

        /// <summary>One scroll-wheel step in the next frame; the sign is what CycleLiquid reads.</summary>
        public static void Scroll(float y)
        {
            Begin();
            pendingScroll += y;
        }

        /// <summary>
        /// Text form for a command bridge: "hold &lt;path&gt; &lt;seconds&gt;", "frames &lt;path&gt; &lt;n&gt;",
        /// "tap &lt;path&gt;", "release &lt;path&gt;", "releaseall", "mouse &lt;dx&gt; &lt;dy&gt; &lt;seconds&gt;",
        /// "mouseframes &lt;dx&gt; &lt;dy&gt; &lt;n&gt;", "scroll &lt;y&gt;", "begin", "end".
        /// </summary>
        public static void Execute(string command)
        {
            string[] a = command.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0)
                throw new ArgumentException("Empty InputSimulator command.");
            CultureInfo c = CultureInfo.InvariantCulture;
            switch (a[0].ToLowerInvariant())
            {
                case "hold": Hold(a[1], float.Parse(a[2], c)); break;
                case "frames": HoldFrames(a[1], int.Parse(a[2], c)); break;
                case "tap": Tap(a[1]); break;
                case "release": Release(a[1]); break;
                case "releaseall": ReleaseAll(); break;
                case "mouse": MoveMouse(new Vector2(float.Parse(a[1], c), float.Parse(a[2], c)), float.Parse(a[3], c)); break;
                case "mouseframes": MoveMouseFrames(new Vector2(float.Parse(a[1], c), float.Parse(a[2], c)), int.Parse(a[3], c)); break;
                case "scroll": Scroll(float.Parse(a[1], c)); break;
                case "begin": Begin(); break;
                case "end": End(); break;
                default: throw new ArgumentException($"Unknown InputSimulator command '{a[0]}'.");
            }
        }

        // ------------------------------------------------------------------ internals

        static void AddPress(string controlPath, float value, double until, int frames)
        {
            Begin();
            InputControl<float> control = Resolve(controlPath);
            presses.RemoveAll(p => p.Control == control); // re-holding extends; it does not re-press
            presses.Add(new Press { Control = control, Value = value, Until = until, FramesLeft = frames });
        }

        static void StartMouse(Vector2 deltaPerFrame, double until, int frames)
        {
            Begin();
            mouseMoving = true;
            mouseDelta = deltaPerFrame;
            mouseUntil = until;
            mouseFramesLeft = frames;
            mouseSent = false;
        }

        static InputControl<float> Resolve(string controlPath)
        {
            if (string.IsNullOrWhiteSpace(controlPath))
                throw new ArgumentException("Empty control path.", nameof(controlPath));

            // "<Keyboard>/space" matches the device by layout first; a bare "space" is a child name.
            InputControl found = controlPath[0] == '<'
                ? InputControlPath.TryFindControl(keyboard, controlPath) ?? InputControlPath.TryFindControl(mouse, controlPath)
                : keyboard.TryGetChildControl(controlPath) ?? mouse.TryGetChildControl(controlPath);
            if (found is InputControl<float> control)
                return control;
            throw new ArgumentException(
                $"'{controlPath}' is not a key, button or axis on a keyboard or mouse (use MoveMouse for deltas).",
                nameof(controlPath));
        }

        // Runs inside every input update, after the Input System has reset the mouse delta and scroll for
        // the new frame and advanced the update step, and before it reads the event queue. Events queued
        // here are processed in this same update, so presses show up in WasPressedThisFrame this frame.
        static void OnBeforeInputUpdate()
        {
            InputUpdateType type = InputState.currentUpdateType;
            if (type != InputUpdateType.Dynamic && type != InputUpdateType.Fixed && type != InputUpdateType.Manual)
                return; // skip Editor and BeforeRender updates
            if (keyboard == null || !keyboard.added || mouse == null || !mouse.added)
                return;

            double now = Time.realtimeSinceStartupAsDouble;
            lastPlayerUpdate = now;
            warnedStall = false;

            presses.RemoveAll(p => p.Sent && (p.FramesLeft < 0 ? now >= p.Until : p.FramesLeft <= 0));
            if (mouseMoving && mouseSent && (mouseFramesLeft < 0 ? now >= mouseUntil : mouseFramesLeft <= 0))
                mouseMoving = false;

            // Full device states built from defaults each frame: whatever is not held is released, and a
            // reset or a stray event on the virtual devices is corrected in the next frame.
            using (StateEvent.FromDefaultStateFor(keyboard, out InputEventPtr keyboardEvent))
            using (StateEvent.FromDefaultStateFor(mouse, out InputEventPtr mouseEvent))
            {
                foreach (Press p in presses)
                {
                    p.Control.WriteValueIntoEvent(p.Value, p.Control.device == keyboard ? keyboardEvent : mouseEvent);
                    p.Sent = true;
                    if (p.FramesLeft > 0)
                        p.FramesLeft--;
                }

                mouse.position.WriteValueIntoEvent(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), mouseEvent);
                if (mouseMoving)
                {
                    mouse.delta.WriteValueIntoEvent(mouseDelta, mouseEvent);
                    mouseSent = true;
                    if (mouseFramesLeft > 0)
                        mouseFramesLeft--;
                }
                if (pendingScroll != 0f)
                {
                    mouse.scroll.y.WriteValueIntoEvent(pendingScroll, mouseEvent);
                    pendingScroll = 0f;
                }

                InputSystem.QueueEvent(keyboardEvent);
                InputSystem.QueueEvent(mouseEvent);
            }
        }

        static void EditorTick()
        {
            if (!IsActive)
                return;
            if (!EditorApplication.isPlaying)
            {
                End();
                return;
            }
            if (InputSystem.settings != simSettings && !warnedSettings)
            {
                warnedSettings = true;
                Debug.LogWarning("[BounJam] InputSimulator: something replaced InputSystem.settings; focus may block input again.");
            }
            if (IsBusy && !EditorApplication.isPaused && !warnedStall
                && Time.realtimeSinceStartupAsDouble - lastPlayerUpdate > StallWarningSeconds)
            {
                warnedStall = true;
                Debug.LogWarning("[BounJam] InputSimulator: no player input update for "
                    + $"{StallWarningSeconds:0} s; the player loop seems paused (editor in background?).");
            }
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                End();
        }

        static void RemoveSimDevices()
        {
            for (int i = InputSystem.devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = InputSystem.devices[i];
                if (device.name.StartsWith(KeyboardName, StringComparison.Ordinal)
                    || device.name.StartsWith(MouseName, StringComparison.Ordinal))
                    InputSystem.RemoveDevice(device);
            }
        }

        static void CleanUpAfterLostState()
        {
            if (IsActive)
                return;
            RemoveSimDevices();
            InputSettings current = InputSystem.settings;
            if (current != null && current.name == SettingsName)
            {
                InputSystem.settings = RebuildOriginalSettings(current);
                Object.DestroyImmediate(current);
            }
            ClearSession();
        }

        static InputSettings RebuildOriginalSettings(InputSettings sim)
        {
            // A settings asset chosen in Project Settings is what the editor loads at startup.
            if (EditorBuildSettings.TryGetConfigObject(SettingsConfigKey, out InputSettings asset) && asset != null)
                return asset;
            // Otherwise the editor was running on an in-memory object; rebuild it with the saved values.
            InputSettings restored = Object.Instantiate(sim);
            restored.name = "InputSettings";
            restored.hideFlags = HideFlags.HideAndDontSave;
            restored.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt(SessionBackground, 0);
            restored.editorInputBehaviorInPlayMode =
                (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(SessionEditorBehavior, 0);
            return restored;
        }

        static void ClearSession()
        {
            SessionState.EraseBool(SessionActive);
            SessionState.EraseInt(SessionBackground);
            SessionState.EraseInt(SessionEditorBehavior);
        }
    }
}
