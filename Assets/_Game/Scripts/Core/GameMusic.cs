using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The background music (event:/Game_Music), looping from the start of the game to the end. It sets itself up before
    /// the first scene and lives through scene loads, so level restarts and the next level don't start it over. If the
    /// event comes to an end it starts again, so it loops whether or not the event has a loop region in FMOD Studio.
    /// It plays through the master bus, so the pause menu's volume turns it down too; the options' music volume reaches
    /// it through <see cref="GameAudio"/>.
    /// </summary>
    public class GameMusic : MonoBehaviour
    {
        static GameMusic current;

        EventInstance music;
        bool playing; // seen playing since the last start, so a stop now means it came to an end
        float appliedVolume = 1f;

        /// <summary>
        /// The music event's own volume. <see cref="GameAudio"/> sets it to the music setting while the FMOD project has
        /// no music bus.
        /// </summary>
        public static float EventVolume { get; set; } = 1f;

        // Statics survive entering play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => current = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (current != null) return;
            var go = new GameObject("GameMusic");
            DontDestroyOnLoad(go);
            current = go.AddComponent<GameMusic>();
        }

        void Update()
        {
            if (music.isValid())
            {
                if (appliedVolume != EventVolume)
                {
                    appliedVolume = EventVolume;
                    music.setVolume(appliedVolume);
                }
                music.getPlaybackState(out PLAYBACK_STATE state);
                if (state != PLAYBACK_STATE.STOPPED) playing = true;
                else if (playing)
                {
                    playing = false;
                    music.start();
                }
                return;
            }
            EventReference reference = GameSounds.Music.Reference;
            if (reference.IsNull)
            {
                enabled = false;
                return;
            }
            music = RuntimeManager.CreateInstance(reference);
            appliedVolume = EventVolume;
            music.setVolume(appliedVolume);
            music.start();
        }

        void OnDestroy()
        {
            if (current == this) current = null;
            if (!music.isValid()) return;
            music.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            music.release();
        }
    }
}
