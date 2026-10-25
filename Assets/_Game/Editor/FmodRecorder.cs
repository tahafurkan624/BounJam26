using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Runs the FMOD Recorder (<see cref="FmodCapture"/>) in the editor, whether or not its window is open. It starts a
    /// take as Play mode starts, by hand, or as Unity Recorder starts one (from its window or its F10 Quick Recording),
    /// and stops it with them. A take that followed Unity Recorder is then added to the video Recorder wrote: once
    /// that file is finished, ffmpeg copies the video into "&lt;name&gt;_withSound" with the WAV as its sound.
    /// </summary>
    [InitializeOnLoad]
    static class FmodRecorder
    {
        const string LogPrefix = "[FMOD Recorder] ";
        const string VideoSuffix = "_withSound";
        static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov" };
        // How long to wait for Unity Recorder to finish its video, s.
        const double VideoTimeout = 30.0;

        static bool followsRecorder;
        static DateTime startedUtc;
        static int startFrame;
        static double startTime;

        static RecorderWindow recorderWindow;
        static double nextWindowSearch;

        // A take waiting for its video.
        static string pendingWav;
        static DateTime pendingSinceUtc;
        static List<string> videoFolders;
        static double giveUpAt, nextLook;
        static string candidate;
        static long candidateSize;

        static Process ffmpegRun;
        static StringBuilder ffmpegLog;
        static string ffmpegOutput;
        static string ffmpegFound, ffmpegFoundFor;

        static FmodRecorder()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            // A recompile mid-take would leave FMOD calling into the old scripts.
            AssemblyReloadEvents.beforeAssemblyReload += StopTake;
        }

        public static string Status
        {
            get => SessionState.GetString("FmodRecorder.Status", "");
            private set => SessionState.SetString("FmodRecorder.Status", value);
        }

        public static string LastWav
        {
            get => SessionState.GetString("FmodRecorder.LastWav", "");
            private set => SessionState.SetString("FmodRecorder.LastWav", value);
        }

        public static string LastVideo
        {
            get => SessionState.GetString("FmodRecorder.LastVideo", "");
            private set => SessionState.SetString("FmodRecorder.LastVideo", value);
        }

        /// <summary>The take running now started with a Unity Recorder take, and stops with it.</summary>
        public static bool FollowsRecorder => followsRecorder && FmodCapture.IsRecording;
        public static bool IsBusy => pendingWav != null || ffmpegRun != null;

        /// <summary>
        /// How far the sound has run ahead of the video, s. Unity Recorder at a constant frame rate steps the game by a
        /// fixed time per frame, so a game slower than that frame rate makes video time fall behind real time.
        /// </summary>
        public static double Drift
        {
            get
            {
                double video = Time.captureDeltaTime > 0f
                    ? (Time.frameCount - startFrame) * (double)Time.captureDeltaTime
                    : Time.realtimeSinceStartupAsDouble - startTime;
                return FmodCapture.Seconds - video;
            }
        }

        /// <summary>The window's record button.</summary>
        public static void Toggle()
        {
            FmodRecorderSettings settings = FmodRecorderSettings.instance;
            if (FmodCapture.IsRecording)
            {
                // Unity Recorder stopping stops the take too.
                if (FollowsRecorder && UnityRecorderIsRecording()) recorderWindow.StopRecording();
                else StopTake();
            }
            else if (settings.start == FmodRecorderSettings.StartMode.WithUnityRecorder)
            {
                if (RecorderAudioFails())
                {
                    TurnOffRecorderAudio();
                    Debug.Log(LogPrefix + "Turned off Capture Audio in Unity Recorder: Unity's audio is disabled in this " +
                              "project, so the take would fail. The sound comes from FMOD Recorder instead.");
                }
                StartUnityRecorder();
            }
            else if (EditorApplication.isPlaying) StartTake(false);
            else if (settings.start == FmodRecorderSettings.StartMode.EnteringPlayMode) EditorApplication.isPlaying = true;
        }

        public static RecorderWindow OpenUnityRecorder()
        {
            recorderWindow = (RecorderWindow)EditorWindow.GetWindow(typeof(RecorderWindow), false, "Recorder", false);
            return recorderWindow;
        }

        static void StartUnityRecorder()
        {
            RecorderWindow window = OpenUnityRecorder();
            // A take that failed leaves the window in an error state that only its own record button clears, and
            // until then StartRecording does nothing.
            FieldInfo state = typeof(RecorderWindow).GetField("m_State", BindingFlags.Instance | BindingFlags.NonPublic);
            if (state != null && state.FieldType.IsEnum && state.GetValue(window).ToString() == "Error")
                state.SetValue(window, Enum.Parse(state.FieldType, "Idle"));
            window.StartRecording();
        }

        /// <summary>
        /// A Unity Recorder movie recorder has Capture Audio on while Unity's own audio is disabled (FMOD plays the
        /// sound): its encoder then finds no sample rate and the take fails as it starts.
        /// </summary>
        public static bool RecorderAudioFails()
        {
            if (!UnityAudioDisabled()) return false;
            RecorderControllerSettings saved = RecorderControllerSettings.GetGlobalSettings();
            bool captures = saved.RecorderSettings.OfType<MovieRecorderSettings>().Any(movie => movie.Enabled && movie.CaptureAudio);
            Discard(saved);
            return captures;
        }

        public static void TurnOffRecorderAudio()
        {
            RecorderControllerSettings saved = RecorderControllerSettings.GetGlobalSettings();
            foreach (MovieRecorderSettings movie in saved.RecorderSettings.OfType<MovieRecorderSettings>()) movie.CaptureAudio = false;
            saved.Save();
            // The window records from its own copy, so it takes this one.
            OpenUnityRecorder().SetRecorderControllerSettings(saved);
        }

        static bool UnityAudioDisabled()
        {
            UnityEngine.Object audioManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset").FirstOrDefault();
            return audioManager != null && new SerializedObject(audioManager).FindProperty("m_DisableAudio").boolValue;
        }

        /// <summary>Destroys a copy of the Recorder window's saved settings, with its recorders.</summary>
        static void Discard(RecorderControllerSettings saved)
        {
            foreach (RecorderSettings recorder in saved.RecorderSettings.ToList()) UnityEngine.Object.DestroyImmediate(recorder);
            UnityEngine.Object.DestroyImmediate(saved);
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode &&
                FmodRecorderSettings.instance.start == FmodRecorderSettings.StartMode.EnteringPlayMode)
                StartTake(false);
            else if (state == PlayModeStateChange.ExitingPlayMode) StopTake();
        }

        static void Update()
        {
            if (EditorApplication.isPlaying && FmodRecorderSettings.instance.start == FmodRecorderSettings.StartMode.WithUnityRecorder)
            {
                bool video = UnityRecorderIsRecording();
                if (video && !FmodCapture.IsRecording) StartTake(true);
                else if (!video && FollowsRecorder) StopTake();
            }
            if (pendingWav != null) LookForVideo();
            if (ffmpegRun != null && ffmpegRun.HasExited) FinishCombine();
        }

        static bool UnityRecorderIsRecording()
        {
            if (recorderWindow == null && EditorApplication.timeSinceStartup >= nextWindowSearch)
            {
                nextWindowSearch = EditorApplication.timeSinceStartup + 1.0;
                recorderWindow = Resources.FindObjectsOfTypeAll<RecorderWindow>().FirstOrDefault();
            }
            return recorderWindow != null && recorderWindow.IsRecording();
        }

        public static void StartTake(bool withRecorder)
        {
            FmodRecorderSettings settings = FmodRecorderSettings.instance;
            string path = settings.NextPath();
            try
            {
                FmodCapture.Start(path, settings.format);
            }
            catch (Exception e)
            {
                Status = "Couldn't record: " + e.Message;
                Debug.LogError(LogPrefix + Status);
                return;
            }
            settings.take++;
            settings.Store();
            followsRecorder = withRecorder;
            startedUtc = DateTime.UtcNow;
            startFrame = Time.frameCount;
            startTime = Time.realtimeSinceStartupAsDouble;
            Status = "Recording " + Path.GetFileName(path);
        }

        public static void StopTake()
        {
            if (!FmodCapture.IsRecording) return;
            double seconds = FmodCapture.Seconds;
            bool withRecorder = followsRecorder;
            followsRecorder = false;
            string path = FmodCapture.Stop();
            LastWav = path;
            if (FmodCapture.LastError != null)
            {
                Status = FmodCapture.LastError;
                Debug.LogWarning(LogPrefix + Status + "\n" + path);
            }
            else
            {
                Status = $"Saved {Path.GetFileName(path)} ({seconds:0.0} s)";
                Debug.Log(LogPrefix + Status + "\n" + path);
            }
            if (withRecorder && FmodRecorderSettings.instance.addToVideo) WaitForVideo(path);
        }

        // ---------------------------------------------------------------- Adding the sound to the video

        static void WaitForVideo(string wav)
        {
            videoFolders = VideoFolders();
            pendingWav = wav;
            pendingSinceUtc = startedUtc;
            giveUpAt = EditorApplication.timeSinceStartup + VideoTimeout;
            nextLook = 0.0;
            candidate = null;
            Status = "Waiting for Unity Recorder to finish its video…";
        }

        /// <summary>The folders Unity Recorder's movie recorders write to, and its default one.</summary>
        static List<string> VideoFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "Recordings"),
            };
            // A fresh copy of what the Recorder window saved; it's ours to destroy.
            RecorderControllerSettings saved = RecorderControllerSettings.GetGlobalSettings();
            foreach (RecorderSettings recorder in saved.RecorderSettings)
            {
                if (!recorder.Enabled || !(recorder is MovieRecorderSettings)) continue;
                // Split by hand: the file name has wildcards like <Take>, which Path won't take.
                string file = recorder.OutputFile;
                int slash = Math.Max(file.LastIndexOf('/'), file.LastIndexOf('\\'));
                string folder = slash > 0 ? file.Substring(0, slash) : "";
                // A folder named by a wildcard changes per take; the newest-file search can't follow it.
                if (folder.Length > 0 && folder.IndexOf('<') < 0) folders.Add(Path.GetFullPath(folder));
            }
            Discard(saved);
            return folders.ToList();
        }

        static void LookForVideo()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextLook) return;
            nextLook = now + 0.5;

            string video = NewestVideo();
            if (video != null && IsFinished(video))
            {
                string wav = pendingWav;
                pendingWav = null;
                Combine(video, wav);
            }
            else if (now > giveUpAt)
            {
                pendingWav = null;
                Status = "Found no new video from Unity Recorder; the sound is in " + Path.GetFileName(LastWav);
                Debug.LogWarning(LogPrefix + Status + "\nLooked in: " + string.Join(", ", videoFolders));
            }
        }

        static string NewestVideo()
        {
            string newest = null;
            DateTime newestTime = pendingSinceUtc;
            foreach (string folder in videoFolders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder))
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (!VideoExtensions.Contains(extension) || Path.GetFileNameWithoutExtension(file).EndsWith(VideoSuffix)) continue;
                    DateTime written = File.GetLastWriteTimeUtc(file);
                    if (written < newestTime) continue;
                    newest = file;
                    newestTime = written;
                }
            }
            return newest;
        }

        /// <summary>The same size as half a second ago and no longer open for writing.</summary>
        static bool IsFinished(string video)
        {
            long size = new FileInfo(video).Length;
            bool steady = video == candidate && size == candidateSize && size > 0;
            candidate = video;
            candidateSize = size;
            if (!steady) return false;
            try
            {
                using (File.Open(video, FileMode.Open, FileAccess.Read, FileShare.None)) return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>Writes a copy of <paramref name="video"/> with <paramref name="wav"/> as its sound, next to it.</summary>
        public static void Combine(string video, string wav)
        {
            string ffmpeg = FindFfmpeg();
            if (ffmpeg == null)
            {
                Status = "ffmpeg not found: set its path in the window to add the sound to videos.";
                Debug.LogWarning(LogPrefix + Status);
                return;
            }
            if (ffmpegRun != null)
            {
                Debug.LogWarning(LogPrefix + "Still adding sound to the last video; try again in a moment.");
                return;
            }

            string extension = Path.GetExtension(video).ToLowerInvariant();
            string codec = extension == ".webm" ? "libopus -b:a 160k" : extension == ".mov" ? "pcm_s16le" : "aac -b:a 192k";
            double offset = FmodRecorderSettings.instance.offsetMs / 1000.0;
            // A later sound starts after a gap; an earlier one loses its start.
            string shift = offset > 0.0 ? string.Format(CultureInfo.InvariantCulture, "-itsoffset {0:0.###} ", offset)
                         : offset < 0.0 ? string.Format(CultureInfo.InvariantCulture, "-ss {0:0.###} ", -offset)
                         : "";
            ffmpegOutput = Path.Combine(Path.GetDirectoryName(video),
                                        Path.GetFileNameWithoutExtension(video) + VideoSuffix + Path.GetExtension(video));
            string arguments = $"-y -hide_banner -loglevel error -i \"{video}\" {shift}-i \"{wav}\" " +
                               $"-map 0:v:0 -map 1:a:0 -c:v copy -c:a {codec} -shortest \"{ffmpegOutput}\"";

            ffmpegLog = new StringBuilder();
            ffmpegRun = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpeg, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                },
            };
            StringBuilder log = ffmpegLog;
            ffmpegRun.ErrorDataReceived += (_, line) =>
            {
                if (line.Data == null) return;
                lock (log) log.AppendLine(line.Data);
            };
            try
            {
                ffmpegRun.Start();
                ffmpegRun.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                ffmpegRun.Dispose();
                ffmpegRun = null;
                Status = "Couldn't run ffmpeg: " + e.Message;
                Debug.LogError(LogPrefix + Status);
                return;
            }
            Status = "Adding the sound to " + Path.GetFileName(video) + "…";
        }

        static void FinishCombine()
        {
            // Waits for the last lines of its output too.
            ffmpegRun.WaitForExit();
            int code = ffmpegRun.ExitCode;
            ffmpegRun.Dispose();
            ffmpegRun = null;
            if (code == 0)
            {
                LastVideo = ffmpegOutput;
                Status = "Video with sound: " + Path.GetFileName(ffmpegOutput);
                Debug.Log(LogPrefix + Status + "\n" + ffmpegOutput);
            }
            else
            {
                Status = "ffmpeg couldn't add the sound (see the Console).";
                string log;
                lock (ffmpegLog) log = ffmpegLog.ToString();
                Debug.LogError($"{LogPrefix}ffmpeg exited with {code}:\n{log}");
            }
        }

        /// <summary>The window's ffmpeg path, or ffmpeg on the PATH; null when there's none.</summary>
        public static string FindFfmpeg()
        {
            string custom = FmodRecorderSettings.instance.ffmpeg.Trim();
            if (custom.Length > 0) return File.Exists(custom) ? custom : null;
            if (ffmpegFoundFor == custom) return ffmpegFound;

            ffmpegFoundFor = custom;
            ffmpegFound = null;
            string exe = Application.platform == RuntimePlatform.WindowsEditor ? "ffmpeg.exe" : "ffmpeg";
            // The editor keeps the PATH it started with, so also look at the user's, which may have gained ffmpeg since.
            var targets = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine };
            foreach (EnvironmentVariableTarget target in targets)
            {
                string variable = Environment.GetEnvironmentVariable("PATH", target);
                if (variable == null) continue;
                foreach (string folder in variable.Split(Path.PathSeparator))
                {
                    try
                    {
                        string path = Path.Combine(Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"')), exe);
                        if (!File.Exists(path)) continue;
                        ffmpegFound = path;
                        return path;
                    }
                    catch (ArgumentException)
                    {
                        // A malformed PATH entry.
                    }
                }
            }
            return null;
        }
    }
}
