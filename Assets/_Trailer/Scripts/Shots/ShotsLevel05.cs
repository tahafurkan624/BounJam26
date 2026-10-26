#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Level05: a high diorama on the sea (water surface y 10.79). The blob starts on the Start platform
    // (x 3.5..9.5, z -2.58..3.42, floor y 19.81). Three tilted spike conveyors (built at runtime, so they are missing from
    // the edit-mode maps) climb from there toward the summit platform (x -6.72..-4.72, z 15..18.5, y 24.46):
    //   Conveyor     pivot (6.2, 21.45, 6.9), pitched 28 deg up toward +Z: foot (6.2, 20.05, 4.25) just past the Start
    //                platform's north edge, top end (6.2, 22.85, 9.55). Its east (+X) side runs uphill.
    //   Conveyor (1) foot (5.0, 21.2, 11.0) .. top (2.4, 24.2, 15.5); Conveyor (2) foot (-0.04, 23.0, 16.0) .. top (-5.24, 26.0, 16.0).
    // Spikes come round the first conveyor's bottom roller and sweep over the Start platform's north edge (tips at about
    // (6.2, 19.92, 3.3)): a liquid blob parked at (6.2, 20, 2.85) swallows each one (it glows), and freezing there sets the
    // crystal round it. The crystal whips round the roller (it hangs about 1 m out from the belt, so it swings at ~4 m/s)
    // and rides the east side up 6 m in 5 s, then over the top roller ~13 m above the sea and back down the west side.
    // From the summit a curved chute (grate floor in the middle) drops south to the ship's cage, Platform (2)/(3)
    // (x -7.5..-3.47, z 4.5..6.5, y 15.78) between two tall grates at z 6.39 and 4.62, where the PaperShip waits; below
    // its west end is Platform (1) (x -10.5..-6.5, z 3.5..7.5, y 10.81, only 2 cm above the water) and the pier
    // (z 4.75..6.25, rails at 4.5 / 6.5, an arch at x -13.5) runs west into the harbour, pad at the Dock (-16.87, 10.81, 5.46).
    // The conveyor-to-conveyor hops and the chute are physics stunts that can't be staged reliably, so these shots film
    // the first conveyor (from the blob's own start) and the delivery on the pier, which are robust.
    public static partial class TrailerShots
    {
        static void ShotsLevel05(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "L05_climb",
                scene = "Level05",
                note = "hero: the crystal rides the tilted spike belt up into the sky; the camera pulls back and up to reveal " +
                       "the whole conveyor staircase, the summit, the ship's cage and the harbour far below",
                setup = d => L05ClimbSetup(d),
                action = d => L05ClimbAction(d),
            });
            shots.Add(new Shot
            {
                id = "L05_grab",
                scene = "Level05",
                note = "the jelly waits at the foot of the belt; a spike sweeps into it and glows, it freezes (slow motion) " +
                       "and the crystal is whipped round the roller and carried up the belt",
                setup = d => L05GrabSetup(d),
                action = d => L05GrabAction(d),
            });
            shots.Add(new Shot
            {
                id = "L05_roller",
                scene = "Level05",
                note = "high above the sea, the crystal reaches the top of the belt, swings over the roller and rides back down",
                setup = d => L05RollerSetup(d),
                action = d => L05RollerAction(d),
            });
            shots.Add(new Shot
            {
                id = "L05_play",
                scene = "Level05",
                note = "gameplay camera: the blob flows from its start to the belt, catches a spike, freezes and rides up",
                setup = d => L05PlaySetup(d),
                action = d => L05PlayAction(d),
            });
            shots.Add(new Shot
            {
                id = "L05_climb_v",
                scene = "Level05",
                portrait = true,
                note = "portrait: the spike glows inside the jelly, it freezes and rides up; the camera cranes up with it " +
                       "until it looks down past the crystal at the sea far below",
                setup = d => L05ClimbVSetup(d),
                action = d => L05ClimbVAction(d),
            });
            shots.Add(new Shot
            {
                id = "L05_harbour",
                scene = "Level05",
                note = "the blob carries the paper ship down the pier, under the harbour arch and onto the pad: delivered, " +
                       "the pad turns green and the flag is hoisted",
                setup = d => L05HarbourSetup(d),
                action = d => L05HarbourAction(d),
                tail = 0.8f,
            });
        }

        // ---------------------------------------------------------------- Places (world coordinates)

        // Where the liquid parks to catch the spikes: on the Start platform's north edge (z 3.42), on the belt's axis.
        static readonly Vector3 L05GrabSpot = new Vector3(6.2f, 20.01f, 2.85f);
        // Between the parked blob and the bottom roller: aimed at, the spike's sweep and the jelly share the frame.
        static readonly Vector3 L05GrabLook = new Vector3(6.3f, 20.2f, 3.3f);
        // Low over the Start platform's north-east corner, then craning up and right as the crystal climbs.
        static readonly Vector3 L05GrabCamFrom = new Vector3(8.4f, 20.5f, 1.4f);
        static readonly Vector3 L05GrabCamTo = new Vector3(9.3f, 21.3f, 2.3f);
        // The hero's last frame: high south of the Start platform (inside the sea's footprint, so no near edge of the water
        // shows), everything from the Start platform to the summit and the harbour in view.
        static readonly Vector3 L05WideCam = new Vector3(7f, 30f, -12f);
        static readonly Vector3 L05WideLook = new Vector3(-3f, 19.5f, 8f);
        // Open air just east of and above the first conveyor's top end, for the roller orbit.
        static readonly Vector3 L05TopFocus = new Vector3(6.9f, 24.2f, 9.2f);
        // The crystal only gets this far north while it swings round the first conveyor's top roller.
        const float L05TopZ = 9.7f;
        // Portrait follow: low beside the parked blob, craning to high above the climbing crystal.
        static readonly Vector3 L05VFrom = new Vector3(2.0f, 0.6f, -1.6f);
        static readonly Vector3 L05VTo = new Vector3(1.7f, 3.6f, -1.2f);
        static readonly Vector3 L05VLook = new Vector3(-0.05f, 0.12f, 0.3f);
        // Gameplay: where the blob flows to from its spawn (5, 19.97, 0.42).
        static readonly Vector3 L05PlayStop = new Vector3(6.2f, 20f, 2.85f);
        // Platform (1)'s west end, 1.4 m clear of the pier's bollards, and the dock pad (zone 3 x 3 m round it).
        static readonly Vector3 L05PierStart = new Vector3(-9.6f, 11.0f, 5.46f);
        static readonly Vector3 L05DockPad = new Vector3(-16.87f, 11.0f, 5.46f);
        // Over the water south-east of the harbour, looking north-west: pad centred, flag pole behind it, arch at the right.
        static readonly Vector3 L05HarbourCamFrom = new Vector3(-14.0f, 15.4f, -4.0f);
        static readonly Vector3 L05HarbourCamTo = new Vector3(-14.5f, 14.7f, -2.6f);
        static readonly Vector3 L05HarbourLook = new Vector3(-17.2f, 12.6f, 6.3f);

        // Game time the current camera move started (Custom's own clock), for timing marks against it.
        static float L05Clock;

        static float L05Ease(float k) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));

        static bool L05OnStartFloor(TrailerDirector d)
        {
            Vector3 p = d.Blob.position;
            return p.x > 3.7f && p.x < 9.3f && p.z > -2.4f && p.z < 3.3f && p.y > 19.85f && p.y < 20.7f;
        }

        // ---------------------------------------------------------------- Catching a spike

        /// <summary>
        /// Lets the spike now sweeping through the parked jelly go by, so the next one comes round during the take
        /// (one every ~1.5 s: 8 spikes on a 14.2 m loop at 1.2 m/s).
        /// </summary>
        static IEnumerator L05LetSpikePass(TrailerDirector d)
        {
            BlobGrip grip = d.Blob.GetComponent<BlobGrip>();
            if (grip == null) yield break;
            yield return d.WaitUntil(() => grip.Engulfed != null, 2.5f);
            yield return d.WaitUntil(() => grip.Engulfed == null, 1.5f);
        }

        /// <summary>
        /// Waits for a spike to come round inside the liquid, holds a moment on its glow and freezes on it, so the crystal
        /// rides the belt. Retries on the next spike; if none reaches the jelly it edges the liquid a little toward the
        /// roller (never past z ~3.1; the platform ends at 3.42).
        /// </summary>
        static IEnumerator L05Grab(TrailerDirector d, float glowHold, bool mark)
        {
            BlobGrip grip = d.Blob.GetComponent<BlobGrip>();
            if (grip == null)
            {
                Debug.LogWarning($"[Trailer] {d.Shot.id}: the blob has no BlobGrip");
                yield break;
            }
            if (grip.Held == null && d.Form.IsSolid)
            {
                Debug.LogWarning($"[Trailer] {d.Shot.id}: the blob is already a loose crystal; no spike to grab");
                yield break;
            }
            for (int attempt = 0; attempt < 4 && grip.Held == null; attempt++)
            {
                yield return d.WaitUntil(() => grip.Engulfed != null, 2.6f);
                if (grip.Engulfed == null)
                {
                    if (d.Blob.position.z < 2.95f) yield return d.MoveFor(new Vector2(0f, 0.25f), 0.12f);
                    yield return d.Wait(0.3f);
                    continue;
                }
                if (mark) d.Mark("spike sweeps into the jelly and glows");
                for (float t = 0f; t < glowHold && grip.Engulfed != null; t += Time.deltaTime) yield return null;
                if (grip.Engulfed == null) continue;
                yield return d.Freeze();
                if (grip.Held != null) break;
                // Set loose beside the belt instead of round a spike: back to jelly (only while still on the platform)
                // and wait for the next spike.
                if (!d.Form.IsSolid) continue;
                if (!L05OnStartFloor(d))
                {
                    Debug.LogWarning($"[Trailer] {d.Shot.id}: the crystal froze loose off the platform; left frozen");
                    yield break;
                }
                d.Melt();
                yield return d.Wait(0.6f);
            }
            if (grip.Held != null)
            {
                if (mark) d.Mark("grabbed: the crystal rides the belt");
            }
            else Debug.LogWarning($"[Trailer] {d.Shot.id}: no spike was grabbed");
        }

        // ---------------------------------------------------------------- L05_climb (hero)

        static IEnumerator L05ClimbSetup(TrailerDirector d)
        {
            // The ship stays parked in its cage on Platform (2): it isn't needed up here.
            yield return d.Teleport(L05GrabSpot, false);
            yield return d.Wait(1.0f);
            yield return L05Grab(d, 0f, false);
            // The swing round the bottom roller onto the belt's east side takes about half a second.
            yield return d.Wait(0.5f);
            L05Clock = Time.time;
            d.Cam.Custom(t => L05ClimbCamera(d, t));
            d.Cam.Sway(0.25f);
        }

        /// <summary>
        /// Starts as a close side shot riding along with the crystal (east of the belt, out over the water), then cranes up
        /// and back over the Start platform to the wide view from the south. The aim stays on the crystal until the
        /// camera is ~7 m back, then slides to the level's middle passing up over the belt, so the sight line never
        /// crosses the conveyor's frame (checked against the frame's box for crystals hanging 0.75-1.25 m off the belt).
        /// </summary>
        static (Vector3 position, Vector3 look, float fov) L05ClimbCamera(TrailerDirector d, float t)
        {
            Vector3 crystal = d.Blob.position;
            float kp = L05Ease((t - 0.8f) / 5.7f);
            float kl = L05Ease((t - 2.6f) / 4.0f);
            Vector3 position = Vector3.Lerp(crystal + new Vector3(2.4f, -0.15f, -1.0f), L05WideCam, kp);
            Vector3 look = Vector3.Lerp(crystal + new Vector3(0f, 0.35f, 0.6f), L05WideLook, kl)
                           + Vector3.up * (2.0f * Mathf.Sin(Mathf.PI * Mathf.Sqrt(kl)));
            return (position, look, Mathf.Lerp(36f, 46f, kp));
        }

        static IEnumerator L05ClimbAction(TrailerDirector d)
        {
            d.Mark("crystal rides up the belt");
            yield return d.WaitUntil(() => Time.time - L05Clock > 0.8f, 2f);
            d.Mark("camera pulls back");
            yield return d.WaitUntil(() => d.Blob.position.z > L05TopZ, 6f, "the top roller");
            d.Mark("over the top roller");
            yield return d.WaitUntil(() => Time.time - L05Clock > 6.6f, 7f);
            d.Mark("the whole level in frame");
            yield return d.Wait(0.4f);
        }

        // ---------------------------------------------------------------- L05_grab (signature mechanic)

        static IEnumerator L05GrabSetup(TrailerDirector d)
        {
            yield return d.Teleport(L05GrabSpot, false);
            // The jelly swells and settles at the edge.
            yield return d.Wait(1.0f);
            d.FaceBlob(L05GrabCamFrom - L05GrabSpot);
            yield return L05LetSpikePass(d);
            d.Cam.Custom(t =>
            {
                float k = L05Ease(t / 5f);
                Vector3 look = Vector3.Lerp(L05GrabLook, d.Blob.position + Vector3.up * 0.1f, 0.6f);
                return (Vector3.Lerp(L05GrabCamFrom, L05GrabCamTo, k), look, Mathf.Lerp(34f, 38f, k));
            });
            d.Cam.Sway(0.35f);
        }

        static IEnumerator L05GrabAction(TrailerDirector d)
        {
            BlobGrip grip = d.Blob.GetComponent<BlobGrip>();
            d.Mark("jelly waits at the foot of the belt");
            if (grip != null)
            {
                yield return d.WaitUntil(() => grip.Engulfed != null, 3f, "a spike inside the jelly");
                if (grip.Engulfed != null)
                {
                    d.Mark("spike sweeps into the jelly and glows");
                    // Slow motion over the glow and the freeze (its 0.1 s of game time also lets the glow show).
                    yield return d.SlowTo(0.4f, 0.15f);
                    if (grip.Engulfed != null) yield return d.Freeze();
                }
                if (grip.Held == null)
                {
                    // Missed that one: normal speed for the next spike, then slow down again for the whip.
                    yield return d.SlowTo(1f, 0.1f);
                    if (d.Form.IsSolid && L05OnStartFloor(d)) d.Melt();
                    yield return L05Grab(d, 0.12f, true);
                    yield return d.SlowTo(0.4f, 0.12f);
                }
                else d.Mark("grabbed: the crystal rides the belt");
            }
            // The crystal whips round the roller onto the belt in slow motion, then climbs at full speed.
            yield return d.WaitClip(0.8f);
            yield return d.SlowTo(1f, 0.5f);
            d.Mark("swept up the belt");
            yield return d.Wait(2.2f);
        }

        // ---------------------------------------------------------------- L05_roller (over the top)

        static IEnumerator L05RollerSetup(TrailerDirector d)
        {
            yield return d.Teleport(L05GrabSpot, false);
            yield return d.Wait(1.0f);
            yield return L05Grab(d, 0f, false);
            // Ride up (unrecorded) until about a second below the top roller.
            yield return d.WaitUntil(() => d.Blob.position.y > 22.7f, 8f, "the crystal near the top");
            // Slow orbit from east-south-east to north-east of the top end, looking down at the roller with the sea below.
            d.Cam.Orbit(L05TopFocus, 3.8f, 1.9f, 70f, 120f, 4.6f, 44f, -0.5f);
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L05RollerAction(TrailerDirector d)
        {
            d.Mark("crystal nears the top roller");
            yield return d.WaitUntil(() => d.Blob.position.z > L05TopZ, 3f, "the top roller");
            d.Mark("swings over the top roller");
            yield return d.WaitUntil(() => d.Blob.position.x < 5.7f, 2f, "the far side of the belt");
            d.Mark("rides back down the far side");
            yield return d.Wait(1.5f);
        }

        // ---------------------------------------------------------------- L05_play (gameplay camera)

        static IEnumerator L05PlaySetup(TrailerDirector d)
        {
            // Straight from the level's start: the blob on its spawn, the game's own follow camera.
            d.Cam.Gameplay();
            yield return d.Wait(0.3f);
        }

        static IEnumerator L05PlayAction(TrailerDirector d)
        {
            d.Mark("sets off for the belt");
            yield return d.Steer(L05PlayStop, 0.55f, 0.15f, 3f);
            d.Mark("at the foot of the belt");
            // Let the jelly swell so the next spike sweeps right into it.
            yield return d.Wait(0.45f);
            yield return L05Grab(d, 0.15f, true);
            yield return d.Wait(1.8f);
            d.Mark("riding up over the sea");
            yield return d.Wait(1.8f);
        }

        // ---------------------------------------------------------------- L05_climb_v (portrait)

        static IEnumerator L05ClimbVSetup(TrailerDirector d)
        {
            yield return d.Teleport(L05GrabSpot, false);
            yield return d.Wait(1.0f);
            d.FaceBlob(L05VFrom);
            yield return L05LetSpikePass(d);
            d.Cam.Follow(() => d.Blob.position, L05VFrom, L05VLook, 0.45f, 54f);
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L05ClimbVAction(TrailerDirector d)
        {
            d.Mark("jelly waits at the foot of the belt");
            yield return L05Grab(d, 0.15f, true);
            // Crane up with the crystal as it climbs, ending high above it looking down at the sea (the follow restarts
            // from the same offset, so there's no jump).
            d.Cam.Follow(() => d.Blob.position, L05VFrom, L05VLook, 0.45f, 54f, L05VTo, 5.5f);
            yield return d.Wait(2.5f);
            d.Mark("climbing high above the sea");
            yield return d.Wait(2.6f);
        }

        // ---------------------------------------------------------------- L05_harbour (delivery)

        static IEnumerator L05HarbourSetup(TrailerDirector d)
        {
            // Wait on the delivered level instead of loading the next one during the take.
            if (d.Level != null) d.Level.HoldOnComplete = true;
            // Blob and ship onto Platform (1)'s west end (floor 10.81, the water is 2 cm lower), bow pointing down the pier.
            yield return d.Teleport(L05PierStart, true, Vector3.left);
            yield return d.Wait(1.3f);
            d.FaceBlob(Vector3.left);
            d.Cam.Custom(t =>
            {
                float k = L05Ease(t / 9f);
                Vector3 look = Vector3.Lerp(L05HarbourLook, d.Blob.position + Vector3.up * 0.4f, 0.2f);
                return (Vector3.Lerp(L05HarbourCamFrom, L05HarbourCamTo, k), look, 42f);
            });
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L05HarbourAction(TrailerDirector d)
        {
            ShipDock dock = d.Get<ShipDock>("Dock");
            if (dock == null) dock = Object.FindAnyObjectByType<ShipDock>();
            d.Mark("sets off down the pier with the ship");
            // Straight down the middle of the pier (z 5.46) at a ship-safe speed; the arch's banner hangs ~1.9 m up.
            d.Move(d.StickFor(Vector3.left) * 0.55f);
            yield return d.WaitUntil(() => d.Blob.position.x < -13.5f, 4f, "the harbour arch");
            d.Mark("through the harbour arch");
            yield return d.Steer(L05DockPad, 0.45f, 0.3f, 4f);
            d.Mark("stops on the pad");
            yield return d.WaitUntil(() => dock == null || dock.IsDelivered, 3f, "the delivery");
            d.Mark("delivered: the pad turns green, the flag goes up");
            yield return d.Wait(0.6f);
            d.FaceBlob(L05HarbourCamTo - d.Blob.position);
            // The flag takes about 4.5 s to reach the top of its pole.
            yield return d.Wait(3.9f);
            d.Mark("flag up");
            yield return d.Wait(0.3f);
        }
    }
}
#endif
