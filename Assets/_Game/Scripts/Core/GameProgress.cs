using UnityEngine;
using UnityEngine.SceneManagement;

namespace BounJam
{
    /// <summary>
    /// How far the player has got: the furthest level reached, kept between sessions. Every level in the build records
    /// itself as it loads, so the main menu's Play carries on from there; resetting forgets it, and Play starts over at
    /// the first level. A level counts as further by its place in the build settings.
    /// </summary>
    public static class GameProgress
    {
        const string LevelKey = "BounJam.Progress.Level";

        static bool hooked;

        // Statics survive entering play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => hooked = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (!hooked)
            {
                SceneManager.sceneLoaded += (scene, _) => Record(scene);
                hooked = true;
            }
            Record(SceneManager.GetActiveScene());
        }

        /// <summary>
        /// The scene Play should load (a path or name for SceneManager.LoadScene): the furthest level reached while it's
        /// still in the build, otherwise <paramref name="firstLevel"/>.
        /// </summary>
        public static string LevelToPlay(string firstLevel)
        {
            string saved = PlayerPrefs.GetString(LevelKey, "");
            return saved.Length > 0 && SceneUtility.GetBuildIndexByScenePath(saved) >= 0 ? saved : firstLevel;
        }

        /// <summary>True once any level has been reached.</summary>
        public static bool HasProgress => PlayerPrefs.GetString(LevelKey, "").Length > 0;

        /// <summary>Forgets the levels reached; Play starts at the first level again.</summary>
        public static void Reset()
        {
            PlayerPrefs.DeleteKey(LevelKey);
            PlayerPrefs.Save();
        }

        /// <summary>Saves a loaded level when it's further than the one saved. Scenes without a LevelManager don't count.</summary>
        static void Record(Scene scene)
        {
            if (scene.buildIndex < 0 || Object.FindAnyObjectByType<LevelManager>() == null) return;
            string saved = PlayerPrefs.GetString(LevelKey, "");
            int savedIndex = saved.Length > 0 ? SceneUtility.GetBuildIndexByScenePath(saved) : -1;
            if (scene.buildIndex <= savedIndex) return;
            PlayerPrefs.SetString(LevelKey, scene.path);
            PlayerPrefs.Save();
        }
    }
}
