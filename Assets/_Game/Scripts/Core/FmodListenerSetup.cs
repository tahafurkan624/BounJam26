using FMODUnity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BounJam
{
    /// <summary>
    /// FMOD hears the game through a <see cref="StudioListener"/>; without one it listens from the world origin and 3D
    /// sounds fade out with their distance from there. Every loaded scene that has none gets one on the main camera, so
    /// the level scenes need no changes. Sounds pan from the camera but fade with their distance from the blob (the
    /// camera's target group, which frames all its pieces), so the camera's height doesn't make the blob quiet.
    /// </summary>
    public static class FmodListenerSetup
    {
        static bool hooked;

        // Statics survive entering play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => hooked = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (!hooked)
            {
                SceneManager.sceneLoaded += (_, _) => Install();
                hooked = true;
            }
            Install();
        }

        static void Install()
        {
            Camera main = Camera.main;
            if (main == null || StudioListener.ListenerCount > 0) return;
            var listener = main.gameObject.AddComponent<StudioListener>();
            var pieces = Object.FindAnyObjectByType<BlobCameraGroup>();
            if (pieces != null) listener.AttenuationObject = pieces.gameObject;
        }
    }
}
