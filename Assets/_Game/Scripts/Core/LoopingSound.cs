using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// An FMOD event kept going while it's wanted and faded out once it isn't, 3D and following a transform. An event
    /// built without a loop in FMOD Studio is started again each time it ends while still wanted. Asking for it again
    /// while it fades brings it straight back. Given an ending parameter, it doesn't fade: stopping sets that parameter
    /// to 1 and lets the event play its ending and stop by itself; asking for it again before the ending is over cuts
    /// the ending and starts a fresh event. The owner calls <see cref="Tick"/> every frame and <see cref="Release"/>
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
        readonly string endParameter;
        EventInstance instance;
        EventInstance ending; // the last instance let go of, which may still be playing its ending
        bool wanted;
        float volume;

        /// <summary>
        /// <paramref name="parameter"/> and <paramref name="label"/>, when given, are set on the event before it starts.
        /// <paramref name="endParameter"/>, when given, is set to 1 on stopping to play the event's ending.
        /// </summary>
        public LoopingSound(EventReference sound, Transform follow, float fadeOut, Rigidbody body = null,
                            string parameter = null, string label = null, string endParameter = null)
        {
            this.endParameter = endParameter;
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
            StopEnding(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            instance = RuntimeManager.CreateInstance(sound);
            if (body != null) RuntimeManager.AttachInstanceToGameObject(instance, follow.gameObject, body);
            else RuntimeManager.AttachInstanceToGameObject(instance, follow.gameObject);
            if (parameter != null) instance.setParameterByNameWithLabel(parameter, label);
            instance.start();
        }

        public void Stop()
        {
            wanted = false;
            if (endParameter == null || !instance.isValid()) return;
            // On to the ending. Released, so FMOD frees the instance once the ending has played.
            instance.setParameterByName(endParameter, 1f);
            instance.release();
            ending = instance;
            instance.clearHandle();
        }

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
            StopEnding(FMOD.Studio.STOP_MODE.IMMEDIATE);
            if (!instance.isValid()) return;
            instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            instance.release();
            instance.clearHandle();
        }

        // Already released, so stopping it is all it needs.
        void StopEnding(FMOD.Studio.STOP_MODE mode)
        {
            if (ending.isValid()) ending.stop(mode);
            ending.clearHandle();
        }
    }
}
