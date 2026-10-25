using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The player's volume settings, kept between sessions: the music and sound effects volumes (pause menu and main
    /// menu options) and an overall volume. Music plays through <see cref="MusicBus"/> and the sounds through
    /// <see cref="SfxBus"/> when the FMOD project has that bus. Without an effects bus the effects volume goes on the
    /// master bus and the music bus (or, without one, the music event's own volume) is raised by as much, so the
    /// effects slider leaves the music alone.
    /// </summary>
    public static class GameAudio
    {
        public const string MusicBus = "bus:/Music";
        public const string SfxBus = "bus:/SFX";
        const string MasterKey = "BounJam.Volume";
        const string MusicKey = "BounJam.MusicVolume";
        const string SfxKey = "BounJam.SfxVolume";
        // The lowest the master bus goes for the effects volume, so the music can still be raised back from it.
        const float MinSfxGain = 1e-4f;

        /// <summary>Overall volume, 0-1.</summary>
        public static float Master
        {
            get => PlayerPrefs.GetFloat(MasterKey, 1f);
            set => Save(MasterKey, value);
        }

        /// <summary>Music volume, 0-1.</summary>
        public static float Music
        {
            get => PlayerPrefs.GetFloat(MusicKey, 1f);
            set => Save(MusicKey, value);
        }

        /// <summary>Sound effects volume, 0-1.</summary>
        public static float Sfx
        {
            get => PlayerPrefs.GetFloat(SfxKey, 1f);
            set => Save(SfxKey, value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplySaved() => Apply();

        static void Save(string key, float value)
        {
            PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));
            Apply();
        }

        /// <summary>Sets the FMOD buses (and the music event, see <see cref="GameMusic.EventVolume"/>) to the saved volumes.</summary>
        public static void Apply()
        {
            FMOD.Studio.System studio = RuntimeManager.StudioSystem;
            bool hasSfx = studio.getBus(SfxBus, out Bus sfx) == FMOD.RESULT.OK;
            bool hasMusic = studio.getBus(MusicBus, out Bus music) == FMOD.RESULT.OK;
            float sfxOnMaster = hasSfx ? 1f : Mathf.Max(Sfx, MinSfxGain);
            float musicGain = Music / sfxOnMaster;
            if (studio.getBus("bus:/", out Bus master) == FMOD.RESULT.OK) master.setVolume(Master * sfxOnMaster);
            if (hasSfx) sfx.setVolume(Sfx);
            if (hasMusic) music.setVolume(musicGain);
            GameMusic.EventVolume = hasMusic ? 1f : musicGain;
        }
    }
}
