using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// An FMOD event kept going while it's wanted and faded out once it isn't, 3D and following a transform. An event
    /// built without a loop in FMOD Studio is started again each time it ends while still wanted. Asking for it again
    /// while it fades brings it straight back. The owner calls <see cref="Tick"/> every frame and <see cref="Release"/>
    /// when it's disabled.
    /// </summary>
    public class LoopingSound
    {
        readonly EventReference sound;
        readonly Transform follow;
        readonly Rigidbody body;
        readonly float fadeOut;
        readonly string parameter;
        readonly string label;
        EventInstance instance;
        bool wanted;
        float volume;

        /// <summary>
        /// <paramref name="parameter"/> and <paramref name="label"/>, when given, are set on the event before it starts.
        /// </summary>
        public LoopingSound(EventReference sound, Transform follow, float fadeOut, Rigidbody body = null,
                            string parameter = null, string label = null)
        {
            this.sound = sound;
            this.follow = follow;
            this.body = body;
            this.fadeOut = fadeOut;
            this.parameter = parameter;
            this.label = label;
        }

        /// <summary>True from <see cref="Play"/> until <see cref="Stop"/>.</summary>
        public bool IsPlaying => wanted;
        /// <summary>The playing event, for moving it by hand or changing its properties; may be invalid.</summary>
        public EventInstance Instance => instance;

        public void Play()
        {
            wanted = true;
            volume = 1f;
            if (instance.isValid())
            {
                instance.setVolume(1f);
                return;
            }
            if (sound.IsNull) return;
            instance = RuntimeManager.CreateInstance(sound);
            if (body != null) RuntimeManager.AttachInstanceToGameObject(instance, follow.gameObject, body);
            else RuntimeManager.AttachInstanceToGameObject(instance, follow.gameObject);
            if (parameter != null) instance.setParameterByNameWithLabel(parameter, label);
            instance.start();
        }

        public void Stop() => wanted = false;

        public void Tick(float deltaTime)
        {
            if (!instance.isValid()) return;
            if (wanted)
            {
                instance.getPlaybackState(out PLAYBACK_STATE state);
                if (state == PLAYBACK_STATE.STOPPED) instance.start();
                return;
            }
            volume -= fadeOut > 0f ? deltaTime / fadeOut : 1f;
            if (volume > 0f) instance.setVolume(volume);
            else Release();
        }

        /// <summary>Silences it at once and frees the event.</summary>
        public void Release()
        {
            wanted = false;
            if (!instance.isValid()) return;
            instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            instance.release();
            instance.clearHandle();
        }
    }
}
