#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Authentic play with the game's own camera and HUD.
    public static partial class TrailerShots
    {
        static void ShotsGameplay(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "GP_L01_deliver",
                scene = "Level01",
                hud = true,
                note = "gameplay camera + HUD: through the open gate, along the pier, onto the harbour pad: Delivered!",
                setup = d => GpL01DeliverSetup(d),
                action = d => GpL01DeliverAction(d),
                tail = 3.5f,
            });
            shots.Add(new Shot
            {
                id = "GP_menu_look",
                scene = "MainMenu",
                hud = true,
                settle = 2f,
                note = "the main menu as the game shows it: logo, buttons, the stunt loop under the orbiting camera",
                action = d => GpMenuLook(d),
            });
        }

        static IEnumerator GpMenuLook(TrailerDirector d)
        {
            d.Mark("menu");
            yield return d.Wait(6f);
        }

        static IEnumerator GpL01DeliverSetup(TrailerDirector d)
        {
            // A crate on the button holds the half gate open.
            var crate = d.Get<Pushable>("Crate");
            if (crate != null) TrailerDirector.Place(crate.transform, new Vector3(6.5f, 0.6f, 4.5f));
            yield return d.Teleport(new Vector3(0f, 0.3f, 6.4f), true, Vector3.forward);
            yield return d.Wait(1.6f);
            d.Cam.Gameplay();
        }

        static IEnumerator GpL01DeliverAction(TrailerDirector d)
        {
            var dock = Object.FindAnyObjectByType<ShipDock>();
            d.Mark("go");
            yield return d.Path(0.6f, new Vector3(0f, 0f, 10.0f), new Vector3(0f, 0f, 13.6f), new Vector3(0f, 0f, 16.9f));
            yield return d.WaitUntil(() => dock != null && dock.IsDelivered, 3f, "the delivery");
            d.Mark("delivered");
        }
    }
}
#endif
