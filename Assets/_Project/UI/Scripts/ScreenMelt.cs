using System;
using System.Collections;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.UI;

namespace BounJam
{
    /// <summary>
    /// The main menu's exit effect: a full-screen graphic drawn with UI_ScreenMelt.shader melts the picture, the world
    /// slows down, and the sound drops in pitch and fades out, then <c>done</c> runs (quitting the game).
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class ScreenMelt : MonoBehaviour
    {
        [SerializeField] float duration = 1.9f;
        [Tooltip("Time scale reached at the end: the world slows down as the picture goes.")]
        [SerializeField] float slowMotion = 0.2f;
        [Tooltip("Pitch of all sound at the end.")]
        [SerializeField] float pitchAtEnd = 0.45f;

        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int MeltTimeId = Shader.PropertyToID("_MeltTime");

        Graphic graphic;

        void Awake()
        {
            graphic = GetComponent<Graphic>();
            graphic.raycastTarget = false;
            graphic.enabled = false;
        }

        public void Play(Action done) => StartCoroutine(Run(done));

        IEnumerator Run(Action done)
        {
            graphic.enabled = true;
            Material material = graphic.materialForRendering;
            RuntimeManager.CoreSystem.getMasterChannelGroup(out FMOD.ChannelGroup master);
            bool hasBus = RuntimeManager.StudioSystem.getBus("bus:/", out Bus bus) == FMOD.RESULT.OK;
            float volume = 1f;
            if (hasBus) bus.getVolume(out volume);

            float start = Time.unscaledTime;
            for (float t = 0f; t < duration; t = Time.unscaledTime - start)
            {
                float p = t / duration;
                material.SetFloat(ProgressId, p);
                material.SetFloat(MeltTimeId, t);
                Time.timeScale = Mathf.Lerp(1f, slowMotion, p);
                master.setPitch(Mathf.Lerp(1f, pitchAtEnd, p));
                if (hasBus) bus.setVolume(volume * (1f - p * p));
                yield return null;
            }
            material.SetFloat(ProgressId, 1f);
            yield return null;
            // Back to normal for the editor, which stays open after "quitting".
            master.setPitch(1f);
            if (hasBus) bus.setVolume(volume);
            done?.Invoke();
        }
    }
}
