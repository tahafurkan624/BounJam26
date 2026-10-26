using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace BounJam.Trailer
{
    /// <summary>
    /// Films the trailer shots (<see cref="TrailerShots"/>) from the editor. Trailer/Record Queue reads shot ids from
    /// Temp/Trailer/queue.txt (one per line, or "all"), enters Play mode, has <see cref="TrailerDirector"/> stage each
    /// one and records it: Recordings/Trailer/clips/&lt;id&gt;.mp4 (Unity Recorder, constant frame rate), &lt;id&gt;.wav (the
    /// FMOD mix, 32-bit float) and &lt;id&gt;.txt (frames, sound length, drift, marked moments). When the queue is done it
    /// leaves Play mode and writes Temp/Trailer/status.txt.
    /// </summary>
    [InitializeOnLoad]
    static class TrailerSession
    {
        const string QueueFile = "Temp/Trailer/queue.txt";
        const string StatusFile = "Temp/Trailer/status.txt";
        const string PendingKey = "Trailer.PendingRun";
        public const float FrameRate = 60f;

        static RecorderController controller;
        static int startFrame;
        static double startRealtime;
        static string clipBase;
        static readonly List<string> log = new List<string>();

        public static string ClipDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Trailer", "clips"));

        static TrailerSession()
        {
            TrailerDirector.StartRecording = Begin;
            TrailerDirector.StopRecording = End;
            TrailerDirector.FrameRate = FrameRate;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Update;
        }

        // Loading a level saves it as the furthest one reached (GameProgress); the developer's own value is put back
        // after every session. The snapshot is taken once and kept, so it survives crashes and later sessions.
        const string ProgressKey = "BounJam.Progress.Level";
        const string SnapshotKey = "Trailer.ProgressSnapshot";
        const string NoProgress = "\u0000none";

        static void SnapshotProgress()
        {
            if (EditorPrefs.HasKey(SnapshotKey)) return;
            EditorPrefs.SetString(SnapshotKey, PlayerPrefs.HasKey(ProgressKey) ? PlayerPrefs.GetString(ProgressKey) : NoProgress);
        }

        static void RestoreProgress()
        {
            if (!EditorPrefs.HasKey(SnapshotKey)) return;
            string saved = EditorPrefs.GetString(SnapshotKey);
            if (saved == NoProgress) PlayerPrefs.DeleteKey(ProgressKey);
            else PlayerPrefs.SetString(ProgressKey, saved);
            PlayerPrefs.Save();
        }

        [MenuItem("Trailer/Record Queue")]
        static void RecordQueue()
        {
            SnapshotProgress();
            Directory.CreateDirectory(Path.GetDirectoryName(StatusFile));
            File.WriteAllText(StatusFile, "running");
            log.Clear();
            if (EditorApplication.isPlaying) StartDirector();
            else
            {
                SessionState.SetBool(PendingKey, true);
                EditorApplication.isPlaying = true;
            }
        }

        [MenuItem("Trailer/Stop")]
        static void StopAll()
        {
            if (TrailerDirector.Current != null) UnityEngine.Object.Destroy(TrailerDirector.Current.gameObject);
            if (controller != null) End(null, "stopped by hand");
            EditorApplication.isPlaying = false;
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
            {
                SessionState.EraseBool(PendingKey);
                StartDirector();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && controller != null) End(null, "play mode ended mid-take");
            else if (state == PlayModeStateChange.EnteredEditMode) RestoreProgress();
        }

        static void StartDirector()
        {
            List<Shot> shots = Resolve();
            if (shots.Count == 0)
            {
                Finish("error: no shots in the queue");
                return;
            }
            log.Add($"shots: {string.Join(", ", shots.Select(s => s.id))}");
            running = true;
            TrailerDirector.Run(shots);
        }

        static bool running;

        static void Update()
        {
            if (!running || !EditorApplication.isPlaying) return;
            if (TrailerDirector.Busy) return;
            running = false;
            Finish(TrailerDirector.LastError == null ? "done" : "done with errors: " + TrailerDirector.LastError);
            EditorApplication.isPlaying = false;
        }

        static void Finish(string status)
        {
            log.Add(status);
            File.WriteAllText(StatusFile, string.Join("\n", log));
        }

        static List<Shot> Resolve()
        {
            string[] ids = File.Exists(QueueFile)
                ? File.ReadAllLines(QueueFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray()
                : new[] { "all" };
            Dictionary<string, Shot> all = TrailerShots.All().ToDictionary(s => s.id);
            var shots = new List<Shot>();
            foreach (string id in ids)
            {
                if (id == "all") shots.AddRange(all.Values);
                else if (id.EndsWith("*")) shots.AddRange(all.Values.Where(s => s.id.StartsWith(id.TrimEnd('*'))));
                else if (all.TryGetValue(id, out Shot shot)) shots.Add(shot);
                else log.Add($"unknown shot: {id}");
            }
            return shots;
        }

        // ---------------------------------------------------------------- Takes

        static void Begin(Shot shot)
        {
            Directory.CreateDirectory(ClipDir);
            clipBase = Path.Combine(ClipDir, shot.id);
            if (!shot.audioOnly)
            {
                var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "Trailer " + shot.id;
                movie.Enabled = true;
                movie.EncoderSettings = new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Custom,
                    TargetBitRate = 60f,
                };
                movie.CaptureAudio = false;
                movie.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = shot.portrait ? 1080 : 1920,
                    OutputHeight = shot.portrait ? 1920 : 1080,
                };
                movie.OutputFile = clipBase;
                settings.AddRecorderSettings(movie);
                settings.SetRecordModeToManual();
                settings.FrameRate = FrameRate;
                settings.FrameRatePlayback = FrameRatePlayback.Constant;
                settings.CapFrameRate = true;
                RecorderOptions.VerboseMode = false;
                controller = new RecorderController(settings);
                controller.PrepareRecording();
                controller.StartRecording();
            }
            else controller = null;
            FmodCapture.Start(clipBase + ".wav", FmodCapture.Format.Float32);
            startFrame = Time.frameCount;
            startRealtime = Time.realtimeSinceStartupAsDouble;
        }

        static void End(Shot shot, string marks)
        {
            int frames = Time.frameCount - startFrame;
            double real = Time.realtimeSinceStartupAsDouble - startRealtime;
            if (controller != null)
            {
                controller.StopRecording();
                controller = null;
            }
            double audio = FmodCapture.Seconds;
            FmodCapture.Stop();
            double video = frames / (double)FrameRate;
            var text = new StringBuilder();
            text.AppendLine($"id: {shot?.id}");
            text.AppendLine($"scene: {shot?.scene}");
            text.AppendLine($"note: {shot?.note}");
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "video: {0:0.000} s ({1} frames at {2} fps)", video, frames, FrameRate));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "audio: {0:0.000} s", audio));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "real: {0:0.000} s (game ran at {1:0.0} fps)", real, frames / Math.Max(real, 1e-3)));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "drift: {0:+0.000;-0.000} s (sound longer than video)", audio - video));
            if (FmodCapture.LastError != null) text.AppendLine("audio error: " + FmodCapture.LastError);
            text.AppendLine("marks:");
            text.Append(marks);
            if (clipBase != null) File.WriteAllText(clipBase + ".txt", text.ToString());
            log.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.00}s video, drift {2:+0.000;-0.000}s, {3:0.0} fps",
                                  shot?.id, video, audio - video, frames / Math.Max(real, 1e-3)));
            File.WriteAllText(StatusFile, string.Join("\n", log));
        }
    }
}
