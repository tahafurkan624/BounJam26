using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BounJam.EditorTools
{
    /// <summary>
    /// The FMOD Recorder window's options, kept per machine in UserSettings (like Unity Recorder's own, in Library).
    /// </summary>
    [FilePath("UserSettings/FmodRecorder.asset", FilePathAttribute.Location.ProjectFolder)]
    class FmodRecorderSettings : ScriptableSingleton<FmodRecorderSettings>
    {
        public enum StartMode
        {
            [InspectorName("With Unity Recorder takes")] WithUnityRecorder,
            [InspectorName("When entering Play mode")] EnteringPlayMode,
            [InspectorName("Manually")] Manual,
        }

        public StartMode start = StartMode.WithUnityRecorder;
        [Tooltip("Relative to the project folder, or absolute. Unity Recorder writes to Recordings too by default.")]
        public string folder = "Recordings";
        public string fileName = "<Scene>_Sound_<Take>";
        public int take = 1;
        public FmodCapture.Format format = FmodCapture.Format.Pcm16;
        public bool addToVideo = true;
        public int offsetMs;
        [Tooltip("Empty: found on the PATH.")]
        public string ffmpeg = "";

        public void Store() => Save(true);

        public string FolderPath => Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(ProjectRoot, folder));

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>Where the next take goes; past a take number already on disk when the name has &lt;Take&gt;.</summary>
        public string NextPath()
        {
            string path = PathFor(take);
            while (File.Exists(path) && fileName.Contains("<Take>")) path = PathFor(++take);
            return path;
        }

        string PathFor(int number)
        {
            DateTime now = DateTime.Now;
            string name = fileName.Replace("<Scene>", SceneManager.GetActiveScene().name)
                                  .Replace("<Take>", number.ToString("000"))
                                  .Replace("<Date>", now.ToString("yyyy-MM-dd"))
                                  .Replace("<Time>", now.ToString("HH-mm-ss"));
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length == 0) name = "Sound";
            return Path.Combine(FolderPath, name + ".wav");
        }
    }
}
