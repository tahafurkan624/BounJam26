#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    public static partial class TrailerShots
    {
        // A worked example for the shot files: stage, film, mark.
        static void ShotsReference(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "ref_L01_weight",
                scene = "Level01",
                note = "liquid on the button does nothing; it freezes, the plate slams down and the gate sinks",
                setup = d => RefWeightSetup(d),
                action = d => RefWeightAction(d),
            });
        }

        static readonly Vector3 RefButton = new Vector3(6.5f, 0f, 4.5f);

        static IEnumerator RefWeightSetup(TrailerDirector d)
        {
            yield return d.Teleport(RefButton + Vector3.up * 0.3f, true, Vector3.left);
            // Let the jelly swell and the ship settle on it.
            yield return d.Wait(1.2f);
            d.FaceBlob(new Vector3(1f, 0f, -1f));
            d.Cam.Dolly(RefButton + new Vector3(2.4f, 1.3f, -2.9f), RefButton + new Vector3(1.7f, 0.9f, -2.0f),
                        () => RefButton + Vector3.up * 0.35f, 4f, 36f, 34f);
            d.Cam.Sway(0.4f);
        }

        static IEnumerator RefWeightAction(TrailerDirector d)
        {
            d.Mark("liquid on the plate");
            yield return d.Wait(1.0f);
            yield return d.Freeze();
            var button = d.Get<PressureButton>("Button");
            yield return d.WaitUntil(() => button != null && button.IsPressed, 1f, "the button");
            d.Mark("pressed");
            yield return d.Wait(1.8f);
        }
    }
}
#endif
