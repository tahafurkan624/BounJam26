using System.IO;
using UnityEditor;
using UnityEngine;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Tools/BounJam/FMOD Recorder: records the game's FMOD sound to WAV files, laid out like Unity Recorder's window.
    /// By default each Unity Recorder take records its sound alongside, and the sound is then added to the take's video
    /// (see <see cref="FmodRecorder"/>). F9 starts and stops a take.
    /// </summary>
    class FmodRecorderWindow : EditorWindow
    {
        static readonly Color RecordColor = new Color(1f, 0.38f, 0.33f);
        static readonly GUIContent[] Formats = { new GUIContent("16-bit PCM"), new GUIContent("32-bit float") };
        // How fast the level meter falls back, per second.
        const float MeterFall = 1.5f;

        float meter;
        double lastPaint;
        Vector2 scroll;
        // Checked as the window opens or comes back into focus, after a change in the Recorder window, say.
        bool recorderAudioFails;

        [MenuItem("Tools/BounJam/FMOD Recorder")]
        static void Open() => GetWindow<FmodRecorderWindow>("FMOD Recorder");

        [MenuItem("Tools/BounJam/Start or Stop FMOD Recording _F9")]
        static void ToggleRecording() => FmodRecorder.Toggle();

        [MenuItem("Tools/BounJam/Start or Stop FMOD Recording _F9", true)]
        static bool CanToggleRecording() => CanRecord(FmodRecorderSettings.instance);

        static bool CanRecord(FmodRecorderSettings settings) =>
            FmodCapture.IsRecording || EditorApplication.isPlaying || settings.start != FmodRecorderSettings.StartMode.Manual;

        void OnEnable() => recorderAudioFails = FmodRecorder.RecorderAudioFails();

        void OnFocus() => recorderAudioFails = FmodRecorder.RecorderAudioFails();

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            FmodRecorderSettings settings = FmodRecorderSettings.instance;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();

            DrawRecordButton(settings);
            DrawLiveStatus();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Recording", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(FmodCapture.IsRecording))
            {
                settings.start = (FmodRecorderSettings.StartMode)EditorGUILayout.EnumPopup(
                    new GUIContent("Start", "When a take starts and stops."), settings.start);
                settings.format = (FmodCapture.Format)EditorGUILayout.Popup(
                    new GUIContent("Format", "WAV sample format."), (int)settings.format, Formats);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Output File", EditorStyles.boldLabel);
            DrawFolder(settings);
            settings.fileName = EditorGUILayout.TextField(
                new GUIContent("File Name", "Wildcards: <Scene>, <Take>, <Date>, <Time>."), settings.fileName);
            settings.take = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("Take Number", "Goes up after each take."), settings.take));
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Next File", Path.GetFileName(settings.NextPath()));

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Unity Recorder Video", EditorStyles.boldLabel);
            DrawVideoOptions(settings);

            if (EditorGUI.EndChangeCheck()) settings.Store();

            EditorGUILayout.Space(8f);
            DrawResults();
            EditorGUILayout.EndScrollView();
        }

        void DrawRecordButton(FmodRecorderSettings settings)
        {
            bool recording = FmodCapture.IsRecording;
            string label = recording ? "Stop Recording"
                : settings.start == FmodRecorderSettings.StartMode.WithUnityRecorder ? "Start Recording (video + sound)"
                : !EditorApplication.isPlaying && settings.start == FmodRecorderSettings.StartMode.EnteringPlayMode ? "Play and Record"
                : "Start Recording";

            EditorGUILayout.Space(6f);
            Color background = GUI.backgroundColor;
            if (recording) GUI.backgroundColor = RecordColor;
            using (new EditorGUI.DisabledScope(!CanRecord(settings)))
                if (GUILayout.Button(new GUIContent(label, "F9"), GUILayout.Height(32f)))
                {
                    FmodRecorder.Toggle();
                    recorderAudioFails = false;
                }
            GUI.backgroundColor = background;

            if (!CanRecord(settings)) EditorGUILayout.HelpBox("Enter Play mode to record.", MessageType.None);
        }

        void DrawLiveStatus()
        {
            double now = EditorApplication.timeSinceStartup;
            float elapsed = Mathf.Min((float)(now - lastPaint), 0.25f);
            lastPaint = now;
            meter = FmodCapture.IsRecording ? Mathf.Max(FmodCapture.Peak, meter - MeterFall * elapsed) : 0f;

            Rect bar = EditorGUILayout.GetControlRect(false, 8f);
            EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.3f));
            Color level = meter >= 1f ? RecordColor : meter > 0.7f ? new Color(1f, 0.8f, 0.2f) : new Color(0.3f, 0.85f, 0.45f);
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(meter), bar.height), level);

            if (!FmodCapture.IsRecording)
            {
                EditorGUILayout.LabelField("Not recording", EditorStyles.miniLabel);
                return;
            }
            double seconds = FmodCapture.Seconds;
            string channels = FmodCapture.Channels == 1 ? "mono" : FmodCapture.Channels == 2 ? "stereo" : FmodCapture.Channels + " channels";
            EditorGUILayout.LabelField($"● {(int)(seconds / 60):00}:{seconds % 60:00.0}   {FmodCapture.SampleRate / 1000f:0.#} kHz {channels}" +
                                       $"   {Path.GetFileName(FmodCapture.FilePath)}", EditorStyles.boldLabel);
            if (!FmodRecorder.FollowsRecorder) return;

            double drift = FmodRecorder.Drift;
            string text = $"Sound ahead of the video by {drift:+0.00;-0.00} s";
            if (System.Math.Abs(drift) > 0.1)
                EditorGUILayout.HelpBox(text + ". The game is slower than Unity Recorder's frame rate; set its Playback to " +
                                        "Variable, or lower its frame rate or resolution.", MessageType.Warning);
            else EditorGUILayout.LabelField(text, EditorStyles.miniLabel);
        }

        static void DrawFolder(FmodRecorderSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                settings.folder = EditorGUILayout.TextField(new GUIContent("Folder", "Relative to the project folder, or absolute."),
                                                            settings.folder);
                if (GUILayout.Button("…", GUILayout.Width(24f)))
                {
                    string picked = EditorUtility.OpenFolderPanel("FMOD recordings folder", settings.FolderPath, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        settings.folder = picked;
                        GUI.changed = true;
                    }
                }
                if (GUILayout.Button("Open", GUILayout.Width(48f)))
                {
                    Directory.CreateDirectory(settings.FolderPath);
                    EditorUtility.OpenWithDefaultApp(settings.FolderPath);
                }
            }
        }

        void DrawVideoOptions(FmodRecorderSettings settings)
        {
            if (!UnityRecorderApi.Installed)
            {
                EditorGUILayout.HelpBox("Unity Recorder isn't installed; add com.unity.recorder in the Package Manager to " +
                                        "record videos with their sound. Start \"When entering Play mode\" or \"Manually\" " +
                                        "records the sound on its own.", MessageType.Warning);
            }
            else if (settings.start != FmodRecorderSettings.StartMode.WithUnityRecorder)
            {
                EditorGUILayout.HelpBox("Set Start to \"With Unity Recorder takes\" to record the sound of Unity Recorder's " +
                                        "videos and add it to them.", MessageType.None);
            }
            else if (recorderAudioFails)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox("Unity Recorder has Capture Audio on, but Unity's audio is disabled in this project " +
                                            "(FMOD plays the sound), so its takes fail as they start. The record button above " +
                                            "turns it off for you; F10 doesn't.", MessageType.Warning);
                    if (GUILayout.Button("Turn Off", GUILayout.Width(64f), GUILayout.Height(38f)))
                    {
                        FmodRecorder.TurnOffRecorderAudio();
                        recorderAudioFails = false;
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Start a take here, in the Recorder window or with F10; the sound records along with it.",
                                        MessageType.None);
            }
            settings.addToVideo = EditorGUILayout.Toggle(
                new GUIContent("Add Sound to Video", "Once Unity Recorder finishes a take, writes a copy of its video with the " +
                                                     "sound in, named <video>_withSound."), settings.addToVideo);
            settings.offsetMs = EditorGUILayout.IntField(
                new GUIContent("Sound Offset (ms)", "Moves the sound later (+) or earlier (−) against the video."), settings.offsetMs);

            settings.ffmpeg = EditorGUILayout.TextField(new GUIContent("ffmpeg", "Empty: found on the PATH."), settings.ffmpeg);
            string ffmpeg = FmodRecorder.FindFfmpeg();
            if (ffmpeg == null)
                EditorGUILayout.HelpBox("ffmpeg not found. Install it (winget install ffmpeg) or set its path above.", MessageType.Warning);
            else if (settings.ffmpeg.Trim().Length == 0)
                EditorGUILayout.LabelField(" ", "Using " + ffmpeg, EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!UnityRecorderApi.Installed))
                    if (GUILayout.Button("Open Unity Recorder")) FmodRecorder.OpenUnityRecorder();
                using (new EditorGUI.DisabledScope(ffmpeg == null || FmodRecorder.IsBusy))
                    if (GUILayout.Button(new GUIContent("Add Sound to a Video…", "Pick a video, then the WAV to put in it.")))
                        PickAndCombine();
            }
        }

        static void PickAndCombine()
        {
            FmodRecorderSettings settings = FmodRecorderSettings.instance;
            string video = EditorUtility.OpenFilePanelWithFilters("Video", settings.FolderPath,
                                                                  new[] { "Video", "mp4,webm,mov", "All files", "*" });
            if (string.IsNullOrEmpty(video)) return;
            string wavFolder = File.Exists(FmodRecorder.LastWav) ? Path.GetDirectoryName(FmodRecorder.LastWav) : settings.FolderPath;
            string wav = EditorUtility.OpenFilePanel("Sound for " + Path.GetFileName(video), wavFolder, "wav");
            if (!string.IsNullOrEmpty(wav)) FmodRecorder.Combine(video, wav);
        }

        static void DrawResults()
        {
            string status = FmodRecorder.Status;
            if (status.Length > 0) EditorGUILayout.HelpBox(status, MessageType.Info);
            DrawFile("Last Sound", FmodRecorder.LastWav);
            DrawFile("Last Video", FmodRecorder.LastVideo);
        }

        static void DrawFile(string label, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, Path.GetFileName(path));
                if (GUILayout.Button("Show", GUILayout.Width(48f))) EditorUtility.RevealInFinder(path);
                if (GUILayout.Button("Play", GUILayout.Width(48f))) EditorUtility.OpenWithDefaultApp(path);
            }
        }
    }
}
