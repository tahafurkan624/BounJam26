#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // One establishing fly-over per level: a slow high orbit framing the whole diorama.
    public static partial class TrailerShots
    {
        static void ShotsTour(List<Shot> shots)
        {
            string[] levels = { "Level01", "Level02", "Level03", "Level04", "Level05", "Level06", "Level07" };
            for (int i = 0; i < levels.Length; i++)
            {
                string level = levels[i];
                float start = i % 2 == 0 ? -40f : 40f;
                shots.Add(new Shot
                {
                    id = "TOUR_" + level.Replace("Level", "L"),
                    scene = level,
                    note = "establishing fly-over: a slow high orbit of the whole level",
                    setup = d => TourSetup(d, start),
                    action = d => d.Wait(6.5f),
                });
            }
        }

        static IEnumerator TourSetup(TrailerDirector d, float startDegrees)
        {
            // Level06 hides its upper platforms near the blob from the gameplay camera; show them all.
            if (d.Cam.Camera.GetComponent("UpperFloorHider") is Behaviour hider) hider.enabled = false;
            Bounds bounds = TourBounds();
            float size = Mathf.Max(bounds.size.x, bounds.size.z);
            float radius = size * 0.62f + 4f;
            Vector3 center = bounds.center;
            center.y = bounds.min.y + bounds.size.y * 0.3f;
            float sweep = startDegrees < 0f ? 55f : -55f;
            d.Cam.Orbit(center, radius, radius * 0.55f, startDegrees, startDegrees + sweep, 7.5f, 40f, 0f, false);
            yield return null;
        }

        /// <summary>The level's renderers, leaving out the sea and anything huge.</summary>
        static Bounds TourBounds()
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer || r.GetComponentInParent<Water>() != null || r.bounds.size.magnitude > 80f) continue;
                if (r.gameObject.layer == LayerMask.NameToLayer("UI")) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }
    }
}
#endif
