#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Level02, the conveyor level. World coordinates, read from Assets/_Game/Scenes/Level02.unity (the Level root is at
    // the origin):
    // - Upper-left deck (Tropical Floor (1)): x -14.69..-9.19, z -1.65..2.85, top y 0.87. The paper ship is parked on it at
    //   (-13.5, 0.89, -0.5); the Button that opens the Half Gate is at (-12, 0.87, 0.5).
    // - North of the deck a 30 degree slope (z 2.89..8.52) runs down to a sunken pit (Platform (2): x -14.67..-9.17,
    //   z 8.5..12.5, top y -2.2, with the Crate). The Conveyor (centre (-12, -0.738, 6), tilted 30 degrees, belt faces at
    //   x -12.42 / -11.58) lies on the slope. Its west (-X) run moves uphill, so a crystal holding a spike there is carried
    //   from the pit up to the deck and round the top roller.
    // - From the deck's south-west corner a 13 m plank walkway (Wood Wall (3), 1.83 m wide, open sea on both sides) slopes
    //   down ESE to the Half Gate at (-2.78, -4.76) and the start room (x -3..3, z -6..-3, top y 0).
    // - The start room opens east (x 2.75, z -5.5..-3) onto the harbour bridge and the Dock pad at (9.38, 0, -4.27).
    // - Water: the sea's surface is at y -2.53 (40 x 40 m round (-1.6, 5.4)); a pool box x -4..4, z 0.5..8.5 (surface y 0)
    //   lies north of the start room. No shot here comes near either.
    // - Invisible capsule colliders (Environment layer, they block cameras) stand at (-10.55, y 0..2, 4.24) r 0.5 and at
    //   (-8.05 / -7.05, y 0..2, 3.74) r 0.3; the Half Gate's posts stand at z -6.11..-5.76 and -3.76..-3.41 (x -3.11..-2.45).
    // Note: maps/Level02_grid.png and Level02_game.png were rendered with the MainMenu diorama still loaded: the checker
    // floors round x -5..5, the yellow ramp, the second harbour, the crate at (3.2, 5) and the blob at (0, -8) are MainMenu's.
    public static partial class TrailerShots
    {
        static void ShotsLevel02(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "L02_hero_dive",
                scene = "Level02",
                note = "drone dive: straight down over the whole diorama, swooping down to the blob with the paper ship on the upper deck; it hops",
                setup = d => L02DiveSetup(d),
                action = d => L02DiveAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L02_plough",
                scene = "Level02",
                note = "the blob charges in under the parked paper ship and scoops it up in a spray of jelly (slow motion), carrying it off",
                setup = d => L02PloughSetup(d),
                action = d => L02PloughAction(d),
            });
            shots.Add(new Shot
            {
                id = "L02_conveyor",
                scene = "Level02",
                note = "signature: a conveyor spike glows inside the jelly, the blob freezes round it, rides the 30 degree belt up out of the pit, swings over the top roller, melts and is flung onto the deck",
                setup = d => L02BeltSetup(d, false),
                action = d => L02BeltAction(d, false),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L02_conveyor_v",
                scene = "Level02",
                portrait = true,
                note = "portrait: looking up the belt from the pit as the crystal grabs a spike, rides the conveyor up, swings over the top roller and drops onto the deck",
                setup = d => L02BeltSetup(d, true),
                action = d => L02BeltAction(d, true),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L02_bridge_run",
                scene = "Level02",
                note = "side tracking shot over the sea: the blob carries the ship down the long sloped plank bridge, the Half Gate sinks ahead and it slips through (slow motion)",
                setup = d => L02RunSetup(d),
                action = d => L02RunAction(d),
            });
            shots.Add(new Shot
            {
                id = "L02_gameplay_deliver",
                scene = "Level02",
                note = "the game's own camera: the blob runs the ship down the bridge, through the open gate and the start room, across the harbour bridge onto the pad: delivered, the flag rises",
                setup = d => L02PlaySetup(d),
                action = d => L02PlayAction(d),
                tail = 0.5f,
            });
        }

        // ---------------------------------------------------------------- Level geometry

        const float L02DeckTop = 0.87f;
        // The sloped plank bridge (Wood Wall (3)): its top centre, the unit direction along it down toward the gate (it
        // drops 3.8 degrees), that direction made level, and the level direction out over the sea on its south side.
        static readonly Vector3 L02BridgeMid = new Vector3(-8.384f, 0.447f, -3.038f);
        static readonly Vector3 L02BridgeDir = new Vector3(0.9521f, -0.0661f, -0.2987f);
        static readonly Vector3 L02BridgeFlat = new Vector3(0.9542f, 0f, -0.2993f);
        static readonly Vector3 L02BridgeSouth = new Vector3(-0.2985f, 0f, -0.9543f);
        static readonly Vector3 L02DockPad = new Vector3(9.38f, 0f, -4.27f);

        // The liquid waits beside the belt's uphill (west) run, on the slope near its foot: conveyor-local (-0.78, 0.16,
        // 1.8), core 0.25 m above the slope, 0.36 m out from the belt face, right where the spikes pass at blob height.
        static readonly Vector3 L02BeltStart = new Vector3(-12.78f, -1.50f, 7.64f);
        // Follow-camera offsets (world) and aim offsets: landscape from the open sea to the west, portrait from above the
        // pit looking up the belt. Near while it grabs, drifting out to far during the ride.
        // (First take: 2.5 m landscape / 3.2 m portrait were too tight, and the portrait saw the pit's crate below it; the
        // portrait camera now starts above the crate (z 10.9), so the crate is under the bottom of the frame.)
        static readonly Vector3 L02BeltNear = new Vector3(-3.2f, 0.9f, 0.9f);
        static readonly Vector3 L02BeltFar = new Vector3(-5f, 1.8f, 0.6f);
        static readonly Vector3 L02BeltLook = new Vector3(0f, 0.2f, -0.4f);
        static readonly Vector3 L02BeltNearV = new Vector3(-0.9f, 1.9f, 3.3f);
        static readonly Vector3 L02BeltFarV = new Vector3(-1.3f, 2.9f, 4.4f);
        static readonly Vector3 L02BeltLookV = new Vector3(0f, 0.35f, -0.5f);

        static readonly Vector3 L02DiveBlob = new Vector3(-13.3f, 1.12f, -0.5f);
        static readonly Vector3 L02PloughShip = new Vector3(-12f, 0.93f, -0.7f);

        /// <summary>A point on the bridge's top centre line, <paramref name="s"/> m from its middle toward the gate.</summary>
        static Vector3 L02Bridge(float s) => L02BridgeMid + L02BridgeDir * s;

        /// <summary>How far along the bridge (from its middle, toward the gate) a point is, m.</summary>
        static float L02BridgeAlong(Vector3 p) => Vector3.Dot(p - L02BridgeMid, L02BridgeDir);

        /// <summary><paramref name="x"/>, easing smoothly into a ceiling at <paramref name="max"/> over the last <paramref name="knee"/>.</summary>
        static float L02SoftMax(float x, float max, float knee)
        {
            float start = max - knee;
            return x <= start ? x : max - knee * Mathf.Exp(-(x - start) / knee);
        }

        /// <summary>Sinks the Half Gate and keeps it down (as if its button stayed pressed), with its own Doors sound.</summary>
        static void L02OpenGate(TrailerDirector d)
        {
            ButtonMover gate = d.Get<ButtonMover>("Half Gate");
            if (gate != null) TrailerDirector.SetMember(gate, "activeUntil", float.PositiveInfinity);
            else Debug.LogWarning("[Trailer] L02: no Half Gate found");
        }

        static Conveyor L02Belt(TrailerDirector d) => Conveyor.All.Count > 0 ? Conveyor.All[0] : d.Get<Conveyor>("Conveyor");

        /// <summary>
        /// The spike the jelly holds is on the belt's uphill run and has reached the core: freezing now sets the crystal
        /// round it mid-rod, on the straight (not on the roller, where the crystal would turn into the floor).
        /// </summary>
        static bool L02GrabReady(BlobGrip grip, Conveyor belt)
        {
            if (grip == null || grip.Engulfed == null) return false;
            if (belt == null) return true;
            Vector3 local = belt.transform.InverseTransformPoint(grip.Engulfed.PointAlong(0f));
            return local.x < -0.3f && local.z < 1.7f && local.z > 1.1f;
        }

        /// <summary>
        /// Drives through the points like <see cref="TrailerDirector.Path"/> (slowing only into the last one), calling
        /// <paramref name="beat"/> every frame for marks and triggers; whatever it returns is run in place (slow motion).
        /// </summary>
        static IEnumerator L02Drive(TrailerDirector d, float speed, Vector3[] route, System.Func<IEnumerator> beat)
        {
            for (int i = 0; i < route.Length; i++)
            {
                bool last = i == route.Length - 1;
                for (float t = 0f; t < 8f; t += Time.deltaTime)
                {
                    Vector3 to = route[i] - d.Blob.position;
                    to.y = 0f;
                    if (to.magnitude < (last ? 0.3f : 0.6f)) break;
                    float s = last ? Mathf.Clamp(speed * Mathf.Clamp01(to.magnitude / 1.2f + 0.35f), 0.2f, 1f) : speed;
                    d.Move(d.StickFor(to) * s);
                    IEnumerator extra = beat != null ? beat() : null;
                    if (extra != null) yield return extra;
                    yield return null;
                }
            }
            d.Halt();
        }

        // ---------------------------------------------------------------- L02_hero_dive

        static IEnumerator L02DiveSetup(TrailerDirector d)
        {
            // Bow east, toward the harbour.
            yield return d.Teleport(L02DiveBlob, true, Vector3.right);
            yield return d.Wait(1.4f);
            d.FaceBlob(new Vector3(-0.6f, 0f, -0.8f));
            // Straight down over the middle of the sea (the map view), curving out south-west and down to a 3/4 view of
            // the blob from the south-west, looking north-east across the whole level. The aim slides from the level's
            // middle onto the blob. (The blob sits near the sea's west edge, x -21.6: looking west or north-west from
            // low down shows where the sea ends, as the first take's ending did.)
            Vector3 start = new Vector3(-1.6f, 21f, 2.8f);
            Vector3 bend = new Vector3(-14f, 13f, -10f);
            Vector3 end = L02DiveBlob + new Vector3(-2f, 2.9f, -2.7f);
            Vector3 lookFrom = new Vector3(-1.6f, 0.5f, 4f);
            Vector3 lookTo = L02DiveBlob + Vector3.up * 0.3f;
            const float duration = 7f;
            d.Cam.Custom(t =>
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                float aim = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.4f) / (duration - 1.6f)));
                Vector3 a = Vector3.Lerp(start, bend, k);
                Vector3 b = Vector3.Lerp(bend, end, k);
                return (Vector3.Lerp(a, b, k), Vector3.Lerp(lookFrom, lookTo, aim), Mathf.Lerp(50f, 38f, k));
            });
            d.Cam.Sway(0.2f);
        }

        static IEnumerator L02DiveAction(TrailerDirector d)
        {
            d.Mark("dive");
            // The move started in setup and lands 7 s after it, about 6.6 s into the action.
            yield return d.Wait(6.6f);
            d.Mark("settled on the blob");
            yield return d.Wait(0.5f);
            d.Jump();
            yield return d.WaitUntil(() => !d.Motor.IsGrounded, 0.5f);
            d.Mark("hop");
            yield return d.WaitUntil(() => d.Motor.IsGrounded, 2f, "the landing");
            d.Mark("land");
            yield return d.Wait(1.0f);
        }

        // ---------------------------------------------------------------- L02_plough

        static IEnumerator L02PloughSetup(TrailerDirector d)
        {
            // Blob at the deck's east end, the ship parked 2 m west of it on the run line (z -0.7 keeps the jelly off the
            // button at z 0.5 and 1 m in from the deck's south edge), bow west so it is carried bow first.
            yield return d.Teleport(new Vector3(-10.15f, L02DeckTop + 0.25f, -0.7f), false);
            d.PlaceShip(L02PloughShip, Vector3.left);
            yield return d.Wait(1.2f);
            d.FaceBlob(Vector3.left);
            // Medium side view from over the sea 3.8 m south of the run (the first take, 2.4 m away, lost the ship off the
            // top of the frame): the blob charges in from frame right, the aim pans with it from the ship to where it stops.
            d.Cam.Dolly(new Vector3(-12.9f, 2.2f, -4.5f), new Vector3(-12.75f, 2.05f, -4.15f), () => new Vector3(
                            Mathf.Lerp(-11.6f, -12.6f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-10.15f, -13.2f, d.Blob.position.x))),
                            1.2f, -0.7f), 5.5f, 34f, 32f);
            d.Cam.Sway(0.35f);
        }

        static IEnumerator L02PloughAction(TrailerDirector d)
        {
            float shipX = d.Ship != null ? d.Ship.transform.position.x : L02PloughShip.x;
            d.Mark("charge");
            d.Move(new Vector2(-0.85f, 0f));
            // The jelly's front reaches the hull about 1.1 m before the core does.
            yield return d.WaitUntil(() => d.Blob.position.x < shipX + 1.15f, 2f, "the ship");
            yield return d.SlowTo(0.3f, 0.12f);
            d.Mark("scoop");
            yield return d.WaitUntil(() => d.Blob.position.x < shipX - 0.4f, 3f, "the blob under the ship");
            // Ease off with the ship aboard, then stop well short of the deck's west edge (x -14.69).
            d.Move(new Vector2(-0.3f, 0f));
            yield return d.WaitClip(0.4f);
            yield return d.SlowTo(1f, 0.35f);
            yield return d.WaitUntil(() => d.Blob.position.x < -13f, 2f, "the stop");
            d.Halt();
            d.Mark("carried off");
            yield return d.Wait(0.9f);
            d.FaceBlob(new Vector3(-0.23f, 0f, -0.97f));
            yield return d.Wait(0.8f);
        }

        // ---------------------------------------------------------------- L02_conveyor / L02_conveyor_v

        static IEnumerator L02BeltSetup(TrailerDirector d, bool portrait)
        {
            // Without the ship: it stays parked on the deck above, dry.
            yield return d.Teleport(L02BeltStart, false);
            yield return d.Wait(1.0f);
            d.FaceBlob(portrait ? Vector3.forward : Vector3.left);
            if (portrait) d.Cam.Follow(() => d.Blob.position, L02BeltNearV, L02BeltLookV, 0.35f, 56f);
            else d.Cam.Follow(() => d.Blob.position, L02BeltNear, L02BeltLook, 0.35f, 38f);
        }

        static IEnumerator L02BeltAction(TrailerDirector d, bool portrait)
        {
            Conveyor belt = L02Belt(d);
            BlobGrip grip = d.Blob.GetComponent<BlobGrip>();
            d.Mark("liquid beside the belt");
            // A spike comes up the belt every ~1.5 s at its own 1.2 m/s; it glows while it is inside the jelly.
            yield return d.WaitUntil(() => grip != null && grip.Engulfed != null, 4f, "a spike inside the jelly");
            d.Mark("spike glows");
            yield return d.WaitUntil(() => L02GrabReady(grip, belt), 1.5f, "the spike to reach the core");
            // A beat of slow motion on the grab (the spike moves only ~8 cm meanwhile, still inside the jelly).
            yield return d.SlowTo(0.35f, 0.1f);
            yield return d.Freeze();
            bool held = grip != null && grip.Held != null;
            if (held) d.Mark("grabbed the spike");
            else Debug.LogWarning("[Trailer] L02 conveyor: the crystal missed the spike");

            // Same offset as now (no jump), drifting out wider while it rides.
            if (portrait) d.Cam.Follow(() => d.Blob.position, L02BeltNearV, L02BeltLookV, 0.35f, 56f, L02BeltFarV, 3f);
            else d.Cam.Follow(() => d.Blob.position, L02BeltNear, L02BeltLook, 0.35f, 38f, L02BeltFar, 3f);
            yield return d.WaitClip(0.5f);
            yield return d.SlowTo(1f, 0.3f);

            // Ride up the slope, the belt speeding up to 1.9 m/s for pace. The core passes z 3.48 at the top of the
            // straight and reaches z 2.81 at the top of the roller; melting past z 3.0 (45 degrees round) flings the
            // liquid south-east onto the deck.
            float speed = 1.2f;
            bool roller = false;
            for (float t = 0f; held && t < 7f && d.Blob.position.z > 3.0f; t += Time.deltaTime)
            {
                if (belt != null && speed < 1.9f)
                {
                    speed = Mathf.MoveTowards(speed, 1.9f, 1.4f * Time.deltaTime);
                    TrailerDirector.SetMember(belt, "speed", speed);
                }
                if (!roller && d.Blob.position.z < 3.6f)
                {
                    roller = true;
                    d.Mark("over the top roller");
                }
                yield return null;
            }
            d.Melt();
            yield return d.WaitUntil(() => d.Motor.IsGrounded && d.Blob.position.y < 1.1f, 1.5f, "the landing on the deck");
            d.Mark("lands on the deck");
            yield return d.Wait(0.6f);
            d.FaceBlob(portrait ? Vector3.forward : Vector3.left);
            yield return d.Wait(0.9f);
        }

        // ---------------------------------------------------------------- L02_bridge_run

        static IEnumerator L02RunSetup(TrailerDirector d)
        {
            // On the bridge's centre line, 2 m above its middle, the ship's bow pointing down the bridge.
            yield return d.Teleport(L02Bridge(-2f) + Vector3.up * 0.25f, true, L02BridgeFlat);
            yield return d.Wait(1.3f);
            d.FaceBlob(L02BridgeFlat);
            d.Cam.Custom(t => L02RailCam(d));
            d.Cam.Sway(0.3f);
        }

        /// <summary>
        /// A camera on a rail 3.9 m out over the sea, parallel to the bridge, trailing the blob and aiming ahead of it.
        /// Both ease to a stop short of the gate: aimed past the gate the line of sight would cross its south post.
        /// </summary>
        static (Vector3 position, Vector3 look, float fov) L02RailCam(TrailerDirector d)
        {
            float s = L02BridgeAlong(d.Blob.position);
            Vector3 position = L02Bridge(L02SoftMax(s - 1.2f, 3.2f, 1.2f)) + L02BridgeSouth * 3.9f + Vector3.up * 1.9f;
            Vector3 look = L02Bridge(L02SoftMax(s + 1.2f, 5.4f, 1f)) + Vector3.up * 0.45f;
            return (position, look, 40f);
        }

        static IEnumerator L02RunAction(TrailerDirector d)
        {
            bool gate = false, slow = false, through = false;
            Vector3[] route = { L02Bridge(0f), L02Bridge(3f), L02Bridge(6.3f), new Vector3(-0.8f, 0f, -4.95f) };
            d.Mark("run");
            yield return L02Drive(d, 0.7f, route, () =>
            {
                float s = L02BridgeAlong(d.Blob.position);
                if (!gate && s > 1.8f)
                {
                    // 4 m (1.2 s) ahead of the blob: it is down (0.5 s) well before the blob gets there.
                    gate = true;
                    L02OpenGate(d);
                    d.Mark("gate sinks");
                }
                if (!slow && s > 4.6f)
                {
                    slow = true;
                    d.Mark("slip through");
                    return d.SlowTo(0.4f, 0.25f);
                }
                if (!through && s > 6f)
                {
                    through = true;
                    d.Mark("through the gate");
                }
                return null;
            });
            yield return d.SlowTo(1f, 0.3f);
            yield return d.Wait(0.4f);
        }

        // ---------------------------------------------------------------- L02_gameplay_deliver

        static IEnumerator L02PlaySetup(TrailerDirector d)
        {
            // The delivery would otherwise load Level04 a second later, in the middle of the take.
            if (d.Level != null) d.Level.HoldOnComplete = true;
            L02OpenGate(d);
            yield return d.Teleport(L02Bridge(-2f) + Vector3.up * 0.25f, true, L02BridgeFlat);
            d.Cam.Gameplay();
            // The follow camera catches up from the start room, the ship settles, the gate is down.
            yield return d.Wait(2.2f);
            d.FaceBlob(L02BridgeFlat);
        }

        static IEnumerator L02PlayAction(TrailerDirector d)
        {
            ShipDock dock = d.Get<ShipDock>("Dock");
            if (dock == null) dock = UnityEngine.Object.FindAnyObjectByType<ShipDock>();
            bool gate = false, harbour = false;
            Vector3[] route =
            {
                L02Bridge(1.5f), L02Bridge(4.5f), L02Bridge(6.3f),
                new Vector3(0.3f, 0f, -4.55f), new Vector3(4.2f, 0f, -4.32f), L02DockPad,
            };
            d.Mark("go");
            yield return L02Drive(d, 0.65f, route, () =>
            {
                float x = d.Blob.position.x;
                if (!gate && x > -2.8f)
                {
                    gate = true;
                    d.Mark("through the gate");
                }
                if (!harbour && x > 3f)
                {
                    harbour = true;
                    d.Mark("onto the harbour");
                }
                return null;
            });
            d.Mark("on the pad");
            yield return d.WaitUntil(() => dock == null || dock.IsDelivered, 3f, "the delivery");
            d.Mark("delivered");
            yield return d.Wait(2.2f);
        }
    }
}
#endif
