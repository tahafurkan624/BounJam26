using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The player's volume settings, kept between sessions: the overall volume (pause menu) and the music and sound
    /// effects volumes (main menu options). Music plays through <see cref="MusicBus"/> and the sounds through
    /// <see cref="SfxBus"/> once the FMOD project has those buses. Until then the music event's own volume stands in
    /// for the music bus and the master bus for the effects bus, so the effects slider turns the music down with it.
    /// </summary>
    public static class GameAudio
    {
        public const string MusicBus = "bus:/Music";
        public const string SfxBus = "bus:/SFX";
        const string MasterKey = "BounJam.Volume";
        const string MusicKey = "BounJam.MusicVolume";
        const string SfxKey = "BounJam.SfxVolume";

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
            if (studio.getBus("bus:/", out Bus master) == FMOD.RESULT.OK) master.setVolume(Master * (hasSfx ? 1f : Sfx));
            if (hasSfx) sfx.setVolume(Sfx);
            if (hasMusic) music.setVolume(Music);
            GameMusic.EventVolume = hasMusic ? 1f : Music;
        }
    }
}
