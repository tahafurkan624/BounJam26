#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Level06, the sky diorama (no water at all: the pieces float on the sky colour; falling off is the only danger).
    //   Top Platform: top y 4, x -1..1, z -1..5; the blob starts on it with the ship. Button Magnet On at (0,4,2).
    //   Grate Bridge grate: y 4, x -1..1, z 5..7 (liquid drips through it, the ship and the crystal stand on it).
    //   Top Platform (1): y 4, z 7..9, then the harbour pier along x 0 to the Dock pad at (0,4,15.29); flag at (3.5,4,17.2).
    //   Magnet at (-4.5,4.7,4) facing +X, hold point (-4,4.7,4) over the Grate Floor (x -5..-3, z 3..5, y 4) with
    //   Button Magnet Off at (-3.73,4,4) under it. Platform (4) with Latch Button (1) at (-4,4,6).
    //   Lower corridor: y 1, x -1..7, z 5..7, Pocket Magnet (iron) at (6,1.15,6). Ramp up to Platform (3): y 2.5,
    //   x 5..7, z -1..1 with the Latch Button at (6,2.5,0); the Drawbridge foot is at x 5 (y 2.5), it grows -X up to
    //   the Top Platform edge at x 1 (y 4).
    public static partial class TrailerShots
    {
        static void ShotsLevel06(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "L06_hero",
                scene = "Level06",
                note = "eye-level close-up of the blob and its paper ship high on the top platform, then a long crane out " +
                       "that reveals the whole floating sky diorama: magnet, bridges, lower floor and the harbour with its flag",
                setup = d => L06HeroSetup(d),
                action = d => L06HeroAction(d),
                tail = 1.0f,
            });
            shots.Add(new Shot
            {
                id = "L06_magnet",
                scene = "Level06",
                note = "the jelly slides onto the blue magnet button and swallows the iron ball (orange glow); it freezes, " +
                       "the button clunks, the magnet lights up and yanks the crystal across the gap along its beam, it hangs " +
                       "at the magnet's face, then the off-button drops it onto the grate",
                setup = d => L06MagnetSetup(d, false),
                action = d => L06MagnetAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L06_magnet_v",
                scene = "Level06",
                portrait = true,
                note = "portrait, over the shoulder: the jelly takes the iron on the blue button, freezes and is yanked away " +
                       "from the lens into the glowing magnet, hangs there and drops onto the grate",
                setup = d => L06MagnetSetup(d, true),
                action = d => L06MagnetAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L06_drawbridge",
                scene = "Level06",
                note = "the blob with the ship sits on the latch (nothing), freezes: clunk, the drawbridge springs out up to " +
                       "the top platform with a wobble; it melts and flows up the brand-new bridge carrying the ship",
                setup = d => L06BridgeSetup(d),
                action = d => L06BridgeAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L06_gameplay",
                scene = "Level06",
                note = "the game's own camera: the blob carries the ship onto the grate, drips through it (the ship stays on " +
                       "top), splashes down 3 m onto the lower floor as the upper floor turns to x-ray, sprints to the iron " +
                       "ball, swallows it and freezes around it",
                setup = d => L06PlaySetup(d),
                action = d => L06PlayAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L06_deliver",
                scene = "Level06",
                note = "chasing the blob and ship down the sky-high pier into the harbour, the camera cranes up as the ship " +
                       "settles on the pad: the pad turns green and the flag is hoisted (clean frame, no card)",
                setup = d => L06DeliverSetup(d),
                action = d => L06DeliverAction(d),
                tail = 1.0f,
            });
        }

        // ---------------------------------------------------------------- Places (world, from the scene)

        static readonly Vector3 L06Start = new Vector3(0f, 4.25f, 0.4f);
        static readonly Vector3 L06MagnetOnButton = new Vector3(0f, 4f, 2f);
        static readonly Vector3 L06ShipParking = new Vector3(-0.35f, 4.25f, -0.55f);
        static readonly Vector3 L06LatchButton = new Vector3(6f, 2.5f, 0f);
        static readonly Vector3 L06Iron = new Vector3(6f, 1.15f, 6f);
        static readonly Vector3 L06Pad = new Vector3(0f, 4f, 15.29f);

        // ---------------------------------------------------------------- Helpers

        static float L06Ease(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }

        static Vector3 L06Bezier(Vector3 a, Vector3 b, Vector3 c, float k)
        {
            float u = 1f - k;
            return u * u * a + 2f * u * k * b + k * k * c;
        }

        /// <summary>Shows every upper platform for the take (the game hides the ones over or near the blob).</summary>
        static void L06ShowAllFloors()
        {
            Camera main = Camera.main;
            if (main == null) return;
            UpperFloorHider hider = main.GetComponent<UpperFloorHider>();
            if (hider != null) hider.enabled = false;
        }

        // ---------------------------------------------------------------- L06_hero

        static IEnumerator L06HeroSetup(TrailerDirector d)
        {
            L06ShowAllFloors();
            yield return d.Teleport(L06Start, true, new Vector3(1f, 0f, 0.5f));
            yield return d.Wait(1.4f);
            d.FaceBlob(new Vector3(1.6f, 0f, -1.9f));

            // Eye level 2.5 m from the blob, then a curve back and up to a high three-quarter view of the whole level.
            Vector3 p0 = new Vector3(1.6f, 4.75f, -1.5f);
            Vector3 p1 = new Vector3(5.8f, 5.8f, -5.8f);
            Vector3 p2 = new Vector3(10.4f, 11.0f, -7.6f);
            Vector3 l0 = L06Start + new Vector3(0f, 0.35f, 0f);
            Vector3 l2 = new Vector3(0.8f, 4.4f, 5.8f);
            const float hold = 1.1f, crane = 6.4f;
            d.Cam.Custom(t =>
            {
                float k = L06Ease((t - hold) / crane);
                float lk = L06Ease((k - 0.1f) / 0.9f);
                return (L06Bezier(p0, p1, p2, k), Vector3.Lerp(l0, l2, lk), Mathf.Lerp(32f, 50f, k));
            });
            d.Cam.Sway(0.25f);
        }

        static IEnumerator L06HeroAction(TrailerDirector d)
        {
            d.Mark("close-up: blob and ship breathing");
            yield return d.Wait(0.7f);
            d.Mark("crane out");
            yield return d.Wait(3.2f);
            d.Mark("the floating diorama opens up");
            yield return d.Wait(3.4f);
            d.Mark("wide: magnet, bridges, harbour and flag");
        }

        // ---------------------------------------------------------------- L06_magnet / L06_magnet_v

        static IEnumerator L06MagnetSetup(TrailerDirector d, bool portrait)
        {
            L06ShowAllFloors();
            // The jelly goes alone (a ship riding it would be flung off by the yank); the ship waits at the far end.
            yield return d.Teleport(new Vector3(0f, 4.25f, portrait ? 2.95f : 3.3f), false);
            d.PlaceShip(L06ShipParking, Vector3.right);
            // The iron ball waits on the blue on-button, so catching it and pressing the button are one freeze.
            Collectible iron = d.Get<Collectible>("Pocket Magnet");
            if (iron != null) TrailerDirector.Place(iron.transform, L06MagnetOnButton + Vector3.up * 0.15f);
            // The off-button under the hold point would drop the crystal mid-flight; it is given back its weight
            // during the take, once the crystal hangs at the magnet.
            PressureButton off = d.Get<PressureButton>("Button Magnet Off");
            if (off != null) TrailerDirector.SetMember(off, "requiredMass", 999f);
            yield return d.Wait(1.2f);
            d.FaceBlob(Vector3.back);

            if (portrait)
            {
                // Behind the button along the pull line: the crystal flies away from the lens into the magnet's face.
                d.Cam.Dolly(new Vector3(3.05f, 6.7f, 0.45f), new Vector3(2.6f, 6.4f, 0.7f), new Vector3(-2.3f, 4.5f, 3.15f),
                            8.5f, 54f, 50f);
            }
            else
            {
                // Front-right three-quarter: button in the right foreground, magnet and grate floor upper left.
                d.Cam.Dolly(new Vector3(0.91f, 6.65f, -2.47f), new Vector3(0.44f, 6.31f, -1.59f), new Vector3(-2.0f, 4.55f, 3.0f),
                            8.5f, 38f, 36f);
            }
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L06MagnetAction(TrailerDirector d)
        {
            Collectible iron = d.Get<Collectible>("Pocket Magnet");
            PressureButton on = d.Get<PressureButton>("Button Magnet On");
            PressureButton off = d.Get<PressureButton>("Button Magnet Off");
            Magnet magnet = UnityEngine.Object.FindAnyObjectByType<Magnet>();
            BlobMagnetism pull = d.Blob.GetComponent<BlobMagnetism>();

            d.Mark("jelly slides onto the blue button");
            yield return d.Steer(new Vector3(L06MagnetOnButton.x, 4.25f, L06MagnetOnButton.z), 0.4f, 0.08f, 3f);
            yield return d.WaitUntil(() => iron == null || iron.IsEngulfed, 2.5f, "the iron inside the jelly");
            d.Mark("iron glows inside the jelly");
            yield return d.Wait(0.6f);
            yield return d.Freeze();
            yield return d.WaitUntil(() => on == null || on.IsPressed, 0.5f, "the magnet's on-button");
            d.Mark("button clunks, magnet on");
            yield return d.WaitUntil(() => pull == null || pull.Current != null, 1f, "the magnet to pull");
            d.Mark("yank");
            float yanked = Time.time;
            yield return d.WaitUntil(() => Time.time - yanked > 0.5f && d.Body.linearVelocity.magnitude < 0.3f, 3f,
                                     "the crystal to hang at the magnet");
            d.Mark("hangs at the magnet");
            yield return d.Wait(1.0f);
            if (off != null) TrailerDirector.SetMember(off, "requiredMass", 3f);
            yield return d.WaitUntil(() => magnet == null || !magnet.IsActive, 0.4f);
            if (magnet != null && magnet.IsActive) magnet.Deactivate();
            d.Mark("off-button: drop");
            yield return d.WaitUntil(() => d.Motor.IsGrounded, 1.5f, "the crystal to land on the grate");
            d.Mark("clunk on the grate");
            yield return d.Wait(1.1f);
        }

        // ---------------------------------------------------------------- L06_drawbridge

        static IEnumerator L06BridgeSetup(TrailerDirector d)
        {
            L06ShowAllFloors();
            yield return d.Teleport(L06LatchButton + new Vector3(0f, 0.28f, 0f), true, Vector3.left);
            yield return d.Wait(1.3f);
            d.FaceBlob(new Vector3(-0.3f, 0f, -1f));

            // From the open side (-Z), a little above the bridge: latch on the right, the Top Platform edge on the left.
            // The aim follows the blob a little, so the frame pans with it up the bridge.
            Vector3 from = new Vector3(5.0f, 4.7f, -6.0f);
            Vector3 to = new Vector3(4.4f, 4.9f, -5.1f);
            Vector3 aim = new Vector3(3.5f, 3.9f, 0.2f);
            d.Cam.Custom(t =>
            {
                float k = L06Ease(t / 10f);
                Vector3 look = Vector3.Lerp(aim, d.Blob.position + Vector3.up * 0.45f, 0.3f);
                return (Vector3.Lerp(from, to, k), look, Mathf.Lerp(42f, 40f, k));
            });
            d.Cam.Sway(0.35f);
        }

        static IEnumerator L06BridgeAction(TrailerDirector d)
        {
            PressureButton latch = d.Get<PressureButton>("Latch Button");
            Drawbridge bridge = d.Get<Drawbridge>("Drawbridge");

            d.Mark("jelly on the latch: nothing");
            yield return d.Wait(0.9f);
            yield return d.Freeze();
            yield return d.WaitUntil(() => latch == null || latch.IsPressed, 0.5f, "the latch");
            d.Mark("latch clunks");
            yield return d.WaitUntil(() => bridge == null || bridge.Progress > 0.3f, 1f, "the drawbridge to move");
            d.Mark("drawbridge springs out");
            yield return d.WaitUntil(() => bridge == null || bridge.Progress >= 0.99f, 2f, "the drawbridge at full length");
            d.Mark("drawbridge reaches the top");
            yield return d.Wait(0.9f);
            d.Melt();
            yield return d.Wait(0.5f);
            d.Mark("flows up the new bridge");
            yield return d.Steer(new Vector3(0.15f, 4.25f, 0f), 0.45f, 0.25f, 6f);
            d.Mark("on top");
            yield return d.Wait(1.0f);
        }

        // ---------------------------------------------------------------- L06_gameplay

        static IEnumerator L06PlaySetup(TrailerDirector d)
        {
            // The level's own start, re-seeded so the jelly and the ship are settled; the upper-floor x-ray stays on.
            yield return d.Teleport(new Vector3(0f, 4.25f, 0.2f), true, Vector3.forward);
            yield return d.Wait(1.4f);
            d.Cam.Gameplay();
        }

        static IEnumerator L06PlayAction(TrailerDirector d)
        {
            Collectible iron = d.Get<Collectible>("Pocket Magnet");
            BlobCargo cargo = d.Blob.GetComponent<BlobCargo>();

            d.Mark("go");
            // Slow enough (below half speed) that the jelly doesn't stretch long enough to span the grate.
            d.Move(new Vector2(0f, 0.45f));
            yield return d.WaitUntil(() => d.Blob.position.z > 5.45f, 5f, "the grate");
            // Over the grate: let go so it drops straight down onto the 2x2 m floor 3 m below instead of sailing past it.
            d.Halt();
            yield return d.WaitUntil(() => d.Blob.position.y < 3.6f, 1f, "the drip through the grate");
            d.Mark("drips through the grate, ship stays on top");
            yield return d.WaitUntil(() => d.Blob.position.y < 1.6f && d.Motor.IsGrounded, 2.5f, "the landing below");
            d.Mark("splash down");
            yield return d.Wait(0.6f);
            d.Mark("sprint to the iron");
            yield return d.Path(0.85f, new Vector3(2.8f, 1.2f, 6f), new Vector3(L06Iron.x, 1.2f, L06Iron.z));
            d.Mark("brake");
            yield return d.Steer(new Vector3(L06Iron.x, 1.2f, L06Iron.z), 0.3f, 0.08f, 1.5f);
            yield return d.WaitUntil(() => iron == null || iron.IsEngulfed, 2f, "the iron inside the jelly");
            d.Mark("iron glows");
            yield return d.Wait(0.5f);
            yield return d.Freeze();
            yield return d.WaitUntil(() => cargo == null || cargo.Has<IronBall>(), 0.5f, "the iron caught");
            d.Mark("iron caught in the crystal");
            yield return d.Wait(1.2f);
        }

        // ---------------------------------------------------------------- L06_deliver

        static IEnumerator L06DeliverSetup(TrailerDirector d)
        {
            L06ShowAllFloors();
            if (d.Level != null) d.Level.HoldOnComplete = true;
            yield return d.Teleport(new Vector3(0f, 4.25f, 7.9f), true, Vector3.forward);
            yield return d.Wait(1.3f);
            L06DeliverCamera(d);
            d.Cam.Sway(0.3f);
        }

        /// <summary>
        /// Chases the blob from behind and to the right down the pier; once it enters the harbour the camera eases up
        /// and out to a crane view of the pad with the flag, and keeps drifting up slowly.
        /// </summary>
        static void L06DeliverCamera(TrailerDirector d)
        {
            // High enough that the line back to the camera clears the pier's rails and end posts.
            Vector3 offset = new Vector3(1.8f, 1.9f, -3.2f);
            Vector3 aimAhead = new Vector3(0f, 0.45f, 1.6f);
            Vector3 cranePosition = new Vector3(5.4f, 8.6f, 9.0f);
            Vector3 craneLook = new Vector3(0.9f, 5.9f, 15.6f);
            Vector3 drift = new Vector3(0.04f, 0.07f, -0.04f);
            Vector3 chase = d.Blob.position + offset;
            Vector3 chaseVelocity = Vector3.zero;
            float blendStart = -1f;
            const float blendTime = 3.2f;
            d.Cam.Custom(t =>
            {
                Vector3 blob = d.Blob.position;
                chase = Vector3.SmoothDamp(chase, blob + offset, ref chaseVelocity, 0.45f, Mathf.Infinity, Time.deltaTime);
                if (blendStart < 0f && blob.z > 11.4f) blendStart = t;
                float w = blendStart < 0f ? 0f : L06Ease((t - blendStart) / blendTime);
                Vector3 crane = cranePosition + drift * Mathf.Max(0f, blendStart < 0f ? 0f : t - blendStart - blendTime);
                return (Vector3.Lerp(chase, crane, w), Vector3.Lerp(blob + aimAhead, craneLook, w), Mathf.Lerp(38f, 46f, w));
            });
        }

        static IEnumerator L06DeliverAction(TrailerDirector d)
        {
            ShipDock dock = UnityEngine.Object.FindAnyObjectByType<ShipDock>();
            Vector3 pad = dock != null ? dock.transform.position : L06Pad;

            d.Mark("down the pier");
            d.Move(new Vector2(0f, 0.5f));
            yield return d.WaitUntil(() => d.Blob.position.z > 11.4f, 4f, "the end of the pier");
            d.Mark("into the harbour, crane up");
            yield return d.Steer(new Vector3(pad.x, 4.25f, pad.z), 0.5f, 0.2f, 5f);
            d.Mark("on the pad");
            yield return d.WaitUntil(() => dock == null || dock.IsDelivered, 3f, "the delivery");
            d.Mark("delivered: pad green, flag goes up");
            yield return d.Wait(3.6f);
            d.Mark("flag flying");
        }
    }
}
#endif
