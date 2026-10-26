#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Level01 ("Level 1"), from the scene file (every floor top is y = 0, walls are 1.5 m tall and 0.5 m thick, the
    // sea is all around at y -0.02):
    //   start corridor  x -2.5..2.5, z -2.5..8.5; the blob spawns at (0, 0.3, -1.5) with the paper ship on it, bow +Z
    //   Half Gate       panel at z 8.6..8.93, opening |x| < 1.13 (posts beside it), sinks 1.55 m in ~0.5 s
    //   grate           x 2.75, z 3.5..5.5: only the liquid passes into the button room
    //   button room     x 3..8.5, z 0.5..11.5: Crate (6.5, 0.5, 7.5), Button (6.5, 0, 4.5)
    //   antechamber     x -2.5..2.5, z 9..11, exit to the pier |x| < 1.5 at z 11..11.5
    //   harbour         pier |x| < 1.15 from z 11; arch towers at (+-1.73, 14.1), ~4.4 m tall, banner/bunting from
    //                   ~1.9 m; round deck (centre ~(0, 17.75), radius ~3.6) with the Dock pad (0, 17.36), the flag
    //                   pole (3.52, 19.23) and the lighthouse (-3.6, 19.5).
    // (The Temp/Trailer map PNGs also show MainMenu props - a second harbour east of the button room, a crate at
    // (3.2, 5), cream blocks - that are not in Level01.)
    public static partial class TrailerShots
    {
        static void ShotsLevel01(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "L01_sail",
                scene = "Level01",
                note = "hero: the blob sets off with the paper ship, the camera swings round it, the gate drops ahead and it sails up the pier toward the harbour arch as the camera cranes up",
                setup = d => L01SailSetup(d),
                action = d => L01SailAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "L01_crate",
                scene = "Level01",
                note = "signature: the jelly freezes into a crystal and shoves the crate onto the button; the plate lights and the gate beyond the wall sinks",
                setup = d => L01CrateSetup(d),
                action = d => L01CrateAction(d),
            });
            shots.Add(new Shot
            {
                id = "L01_gaterun",
                scene = "Level01",
                note = "low behind the closed gate: it drops and reveals the blob with the ship charging at the lens; it zips past onto the pier",
                setup = d => L01GateRunSetup(d),
                action = d => L01GateRunAction(d),
            });
            shots.Add(new Shot
            {
                id = "L01_play",
                scene = "Level01",
                note = "gameplay camera: the liquid squeezes through the grate, freezes behind the crate, shoves it onto the button and melts again",
                setup = d => L01PlaySetup(d),
                action = d => L01PlayAction(d),
            });
            shots.Add(new Shot
            {
                id = "L01_dock",
                scene = "Level01",
                hud = true,
                note = "delivery: the blob glides onto the harbour pad, the pad turns green, the Delivered! glass card pours in and the camera cranes up as the flag hoists",
                setup = d => L01DockSetup(d),
                action = d => L01DockAction(d),
            });
            shots.Add(new Shot
            {
                id = "L01_dock_v",
                scene = "Level01",
                portrait = true,
                note = "9:16 delivery: the blob with the ship lands on the glowing pad with the flag pole right behind it, tilt up as the flag hoists",
                setup = d => L01DockVSetup(d),
                action = d => L01DockVAction(d),
            });
        }

        // ------------------------------------------------------------------ layout

        const float L01GateZ = 8.76f;
        const float L01PierZ = 11.2f;
        static readonly Vector3 L01CrateOnButton = new Vector3(6.5f, 0.5f, 4.5f);
        static readonly Vector3 L01PadStop = new Vector3(0f, 0f, 17.25f);

        // ------------------------------------------------------------------ shared helpers

        static float L01Smooth(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));

        // Level01 has exactly one of each, so the type lookup is a safe fallback for the name lookup.
        static T L01Find<T>(TrailerDirector d, string name) where T : Component
        {
            T found = d.Get<T>(name);
            return found != null ? found : Object.FindAnyObjectByType<T>();
        }

        static ButtonMover L01Gate(TrailerDirector d) => L01Find<ButtonMover>(d, "Half Gate");
        static PressureButton L01Button(TrailerDirector d) => L01Find<PressureButton>(d, "Button");
        static Pushable L01Crate(TrailerDirector d) => L01Find<Pushable>(d, "Crate");
        static ShipDock L01Dock(TrailerDirector d) => L01Find<ShipDock>(d, "Dock");

        /// <summary>Marks a beat only while recording (helpers also run in setups).</summary>
        static void L01Beat(TrailerDirector d, string what)
        {
            if (d.Recording) d.Mark(what);
        }

        /// <summary>What to aim at: between the jelly and the ship riding on it (or just above the jelly).</summary>
        static Vector3 L01Subject(TrailerDirector d)
        {
            Vector3 core = d.Blob.position;
            if (d.Ship != null && (d.Ship.transform.position - core).sqrMagnitude < 2.25f)
                return Vector3.Lerp(core, d.Ship.transform.position, 0.45f);
            return core + Vector3.up * 0.5f;
        }

        /// <summary>
        /// A look point on the line from the camera toward <paramref name="aim"/>, slid along it so its z stays in
        /// [zMin, zMax]: the view direction is unchanged, but the wall check (a sweep from the look point back to the
        /// camera) only covers the open floor near the camera, never the gate panel or the pier posts behind it.
        /// </summary>
        static Vector3 L01AimBetween(Vector3 camera, Vector3 aim, float zMin, float zMax)
        {
            float dz = aim.z - camera.z;
            if (Mathf.Abs(dz) < 1e-3f) return aim;
            float k = (Mathf.Clamp(aim.z, zMin, zMax) - camera.z) / dz;
            return camera + (aim - camera) * Mathf.Clamp(k, 0.05f, 1f);
        }

        /// <summary>The level's own way of holding the gate open: the crate on the button. Before the take.</summary>
        static IEnumerator L01OpenGateWithCrate(TrailerDirector d)
        {
            Pushable crate = L01Crate(d);
            if (crate != null) TrailerDirector.Place(crate.transform, L01CrateOnButton);
            ButtonMover gate = L01Gate(d);
            yield return d.WaitUntil(() => gate == null || gate.Progress > 0.99f, 2.5f, "the gate to sink");
        }

        /// <summary>Presses the button from out of shot (it stays pressed): the gate sinks and stays down.</summary>
        static void L01PressFromOffscreen(TrailerDirector d)
        {
            PressureButton button = L01Button(d);
            if (button != null) TrailerDirector.SetMember(button, "requiredMass", 0f);
        }

        /// <summary>
        /// Drives the blob north up the level's centre line until its core passes <paramref name="stopZ"/>. The lane
        /// is <paramref name="laneX"/> until <paramref name="laneMergeZ"/>, then it merges onto the pier centre by
        /// z 11.5. With <paramref name="brake"/> it eases off over the last metre and stops; without it the stick stays
        /// held (to keep rolling into a take). It presses the button once the core passes <paramref name="pressAtZ"/>,
        /// and it never runs into a shut gate: it waits short of it until the panel is down.
        /// </summary>
        static IEnumerator L01RunNorth(TrailerDirector d, float speed, float stopZ, float laneX, bool brake,
                                       float pressAtZ = float.PositiveInfinity, float markAtZ = float.PositiveInfinity,
                                       string markName = null, float laneMergeZ = 9.5f, float timeout = 12f)
        {
            ButtonMover gate = L01Gate(d);
            bool gateWasOpen = gate == null || gate.Progress > 0.97f;
            bool pressed = false, markedSink = gateWasOpen, markedOpen = gateWasOpen;
            bool markedGate = d.Blob.position.z > L01GateZ, markedPier = d.Blob.position.z > L01PierZ;
            bool markedExtra = d.Blob.position.z > markAtZ;
            for (float t = 0f; t < timeout; t += Time.deltaTime)
            {
                Vector3 p = d.Blob.position;
                if (!pressed && p.z >= pressAtZ)
                {
                    pressed = true;
                    L01PressFromOffscreen(d);
                    L01Beat(d, "button pressed (off screen)");
                }
                if (gate != null && !markedSink && gate.Progress > 0.02f) { markedSink = true; L01Beat(d, "gate sinking"); }
                if (gate != null && !markedOpen && gate.Progress > 0.97f) { markedOpen = true; L01Beat(d, "gate open"); }
                if (!markedGate && p.z > L01GateZ) { markedGate = true; L01Beat(d, "through the gate"); }
                if (!markedPier && p.z > L01PierZ) { markedPier = true; L01Beat(d, "onto the pier"); }
                if (!markedExtra && p.z > markAtZ) { markedExtra = true; if (markName != null) L01Beat(d, markName); }
                if (p.z >= stopZ) break;

                bool gateShut = gate != null && gate.Progress < 0.97f;
                if (gateShut && p.z > L01GateZ - 1.5f && p.z < L01GateZ + 0.3f)
                {
                    d.Halt();
                    yield return null;
                    continue;
                }
                float lane = Mathf.Lerp(laneX, 0f, Mathf.InverseLerp(Mathf.Min(laneMergeZ, 11.4f), 11.5f, p.z));
                float s = brake ? speed * Mathf.Clamp((stopZ - p.z) / 1.6f + 0.3f, 0.4f, 1f) : speed;
                d.Move(new Vector2(Mathf.Clamp((lane - p.x) * 1.5f, -0.3f, 0.3f), 1f).normalized * s);
                yield return null;
            }
            if (brake) d.Halt();
        }

        /// <summary>Like <see cref="TrailerDirector.Path"/>, marking each waypoint as it is reached.</summary>
        static IEnumerator L01Waypoints(TrailerDirector d, float speed, Vector3[] points, string[] reached)
        {
            for (int i = 0; i < points.Length; i++)
            {
                bool last = i == points.Length - 1;
                for (float t = 0f; t < 6f; t += Time.deltaTime)
                {
                    Vector3 to = points[i] - d.Blob.position;
                    to.y = 0f;
                    if (to.magnitude < (last ? 0.35f : 0.6f)) break;
                    float s = last ? Mathf.Clamp(speed * Mathf.Clamp01(to.magnitude / 1.2f + 0.35f), 0.2f, 1f) : speed;
                    d.Move(d.StickFor(to) * s);
                    yield return null;
                }
                if (reached != null && i < reached.Length && !string.IsNullOrEmpty(reached[i])) L01Beat(d, reached[i]);
            }
            d.Halt();
        }

        /// <summary>
        /// The crystal (already frozen, north of the crate) shoves the crate south onto the button, then brakes;
        /// waits for the press and the gate.
        /// </summary>
        static IEnumerator L01PushCrate(TrailerDirector d)
        {
            Pushable crate = L01Crate(d);
            PressureButton button = L01Button(d);
            ButtonMover gate = L01Gate(d);
            L01Beat(d, "push");
            bool sliding = false, pressed = false, sinking = false;
            for (float t = 0f; t < 5f && crate != null; t += Time.deltaTime)
            {
                Vector3 c = crate.transform.position;
                if (c.z <= 4.85f) break;
                Vector3 p = d.Blob.position;
                d.Move(new Vector2(Mathf.Clamp((c.x - p.x) * 1.5f, -0.3f, 0.3f), -1f).normalized);
                if (!sliding && crate.IsFree) { sliding = true; L01Beat(d, "crate sliding"); }
                if (!pressed && button != null && button.IsPressed) { pressed = true; L01Beat(d, "pressed"); }
                if (!sinking && gate != null && gate.Progress > 0.02f) { sinking = true; L01Beat(d, "gate sinking"); }
                yield return null;
            }
            d.Halt();
            yield return d.WaitUntil(() => button == null || button.IsPressed, 1f, "the button");
            if (!pressed) L01Beat(d, "pressed");
            yield return d.WaitUntil(() => gate == null || gate.Progress > 0.02f, 1f, "the gate to move");
            if (!sinking) L01Beat(d, "gate sinking");
            yield return d.WaitUntil(() => gate == null || gate.Progress > 0.98f, 1.5f, "the gate to open");
            L01Beat(d, "gate open");
        }

        /// <summary>From wherever the camera is now to a new pose, eased over <paramref name="duration"/> s.</summary>
        static void L01CraneTo(TrailerDirector d, Vector3 toPosition, Vector3 toLook, float toFov, float duration)
        {
            Vector3 fromPosition = d.Cam.transform.position;
            Vector3 fromLook = d.Cam.Look;
            float fromFov = d.Cam.Camera.fieldOfView;
            d.Cam.Custom(t =>
            {
                float k = L01Smooth(0f, duration, t);
                return (Vector3.Lerp(fromPosition, toPosition, k), Vector3.Lerp(fromLook, toLook, k), Mathf.Lerp(fromFov, toFov, k));
            });
        }

        // ------------------------------------------------------------------ L01_sail (hero)

        const float L01SailFromZ = -1.5f;
        const float L01SailToZ = 12.6f;

        static IEnumerator L01SailSetup(TrailerDirector d)
        {
            // The level as it loads: the blob on its spawn with the ship riding on it, bow +Z. No teleport needed.
            yield return d.WaitUntil(() => d.Ship == null || d.Ship.IsFloating, 2f, "the ship to float");
            d.FaceShip(Vector3.forward);
            yield return d.Wait(0.6f);
            d.FaceBlob(new Vector3(0.53f, 0f, 0.85f));   // eyes to the camera, front right
            d.Cam.Custom(t => L01SailRig(d));
            d.Cam.Sway(0.35f);
        }

        /// <summary>
        /// Driven by how far the blob has sailed (s: spawn 0 to the pier 1): starts low front-right of it, swings
        /// round its right side (inside the corridor, x &lt; 1.9) as it passes, settles behind it and cranes up to
        /// 3 m, aiming a little ahead so the arch, the pad, the lighthouse and the flag fill the top of the frame.
        /// </summary>
        static (Vector3, Vector3, float) L01SailRig(TrailerDirector d)
        {
            Vector3 b = d.Blob.position;
            float s = Mathf.InverseLerp(L01SailFromZ, L01SailToZ, b.z);
            float angle = Mathf.Deg2Rad * Mathf.Lerp(32f, 174f, L01Smooth(0f, 0.42f, s));
            float reach = Mathf.Lerp(2.6f, 5.2f, L01Smooth(0.3f, 1f, s));
            float height = Mathf.Lerp(0.72f, 3.0f, L01Smooth(0.12f, 1f, s));
            var position = new Vector3(b.x + 1.85f * Mathf.Sin(angle), height, b.z + reach * Mathf.Cos(angle));
            var look = new Vector3(b.x, Mathf.Lerp(0.7f, 1.75f, L01Smooth(0.2f, 1f, s)), b.z + 0.8f * L01Smooth(0.25f, 1f, s));
            return (position, look, Mathf.Lerp(44f, 46f, L01Smooth(0.2f, 1f, s)));
        }

        static IEnumerator L01SailAction(TrailerDirector d)
        {
            d.Mark("blob with the ship at rest");
            yield return d.Wait(0.7f);
            d.Mark("set off");
            // The gate is opened from off screen when the blob is ~5 m short of it; it is down long before it arrives.
            yield return L01RunNorth(d, 0.48f, L01SailToZ, 0f, true, 3.4f);
            d.Mark("stops before the harbour arch");
            yield return d.Wait(1.2f);
        }

        // ------------------------------------------------------------------ L01_crate (signature mechanic)

        static readonly Vector3 L01CrateBlobStart = new Vector3(6.5f, 0.3f, 9.3f);
        // With the crate-follow below this aims at ~(5.0, 0.95, 7.3) at the start and ~(5.0, 0.95, 6.7) at the end.
        static readonly Vector3 L01CrateLook = new Vector3(4.6f, 1.06f, 7.25f);

        static IEnumerator L01CrateSetup(TrailerDirector d)
        {
            // The ship can't follow through the grate, so it stays behind at the spawn (it drops onto the dry floor).
            yield return d.Teleport(L01CrateBlobStart, false);
            yield return d.Wait(1.2f);
            d.FaceBlob(Vector3.right);   // toward the camera
            Pushable crate = L01Crate(d);
            Transform box = crate != null ? crate.transform : null;
            // High over the sea east of the room: the jelly right, the crate's path to the plate lower left, and the
            // Half Gate in the upper middle, seen over the room's west wall (its top 1 m shows as it sinks).
            d.Cam.Dolly(new Vector3(10.8f, 5.1f, 6.6f), new Vector3(10.2f, 4.5f, 6.1f),
                        () => box != null ? Vector3.Lerp(L01CrateLook, box.position, 0.2f) : L01CrateLook, 6f, 48f, 47f);
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L01CrateAction(TrailerDirector d)
        {
            d.Mark("liquid behind the crate");
            yield return d.Wait(0.6f);
            yield return d.Freeze();
            yield return d.Wait(0.35f);
            yield return L01PushCrate(d);
            yield return d.Wait(0.5f);
            d.Melt();
            yield return d.Wait(0.9f);
        }

        // ------------------------------------------------------------------ L01_gaterun

        // Low in the west half of the antechamber, 1.6 m north of the gate: the shut panel fills the frame and the
        // blob (lane x +0.3) passes 1.15 m to the camera's left-hand side. Once it is past, the camera eases onto the
        // centre line behind it, so the pier's two lantern posts (x +-1.07, z 11.04) stay out of the frame.
        static readonly Vector3 L01RunCam = new Vector3(-0.85f, 0.75f, 10.5f);
        static readonly Vector3 L01RunChaseCam = new Vector3(-0.25f, 1.15f, 10.0f);
        static readonly Vector3 L01RunGateAim = new Vector3(0.2f, 0.72f, 8.5f);

        static IEnumerator L01GateRunSetup(TrailerDirector d)
        {
            yield return d.Teleport(new Vector3(0.3f, 0.3f, 3.9f), true, Vector3.forward);
            yield return d.Wait(1.3f);
            d.FaceBlob(Vector3.forward);   // facing the gate, i.e. the camera behind it
            d.Cam.Custom(t => L01GateRunRig(d));
            d.Cam.Sway(0.45f);
        }

        static (Vector3, Vector3, float) L01GateRunRig(TrailerDirector d)
        {
            // Held on the gate while the blob comes up the corridor, then on the blob as it reaches it and passes.
            float z = d.Blob.position.z;
            Vector3 camera = Vector3.Lerp(L01RunCam, L01RunChaseCam, L01Smooth(10.9f, 11.9f, z));
            Vector3 aim = Vector3.Lerp(L01RunGateAim, L01Subject(d), L01Smooth(6.6f, 9.2f, z));
            return (camera, L01AimBetween(camera, aim, 9.3f, 10.95f), 48f);
        }

        static IEnumerator L01GateRunAction(TrailerDirector d)
        {
            d.Mark("gate shut, blob behind it");
            yield return d.Wait(0.3f);
            d.Mark("charge");
            // Pressed when the blob is 3.5 m short of the panel: it is down ~1 m before the jelly gets there.
            yield return L01RunNorth(d, 0.62f, 12.6f, 0.3f, true, 5.1f, L01RunCam.z, "passes the camera", laneMergeZ: 10.6f);
            d.Mark("stops on the pier");
            yield return d.Wait(1.0f);
        }

        // ------------------------------------------------------------------ L01_play (gameplay camera)

        static IEnumerator L01PlaySetup(TrailerDirector d)
        {
            // By the grate as a player leaves it: the ship parked on the floor nearby (it can't pass the bars).
            yield return d.Teleport(new Vector3(1.0f, 0.3f, 4.5f), false);
            d.PlaceShip(new Vector3(-0.7f, 0.12f, 3.6f), Vector3.forward);
            d.Cam.Gameplay();
            yield return d.Wait(2.0f);   // the follow camera catches up, the ship settles
        }

        static IEnumerator L01PlayAction(TrailerDirector d)
        {
            d.Mark("go");
            // Through the grate (only the liquid fits between the bars), round the crate's west side to its far side.
            yield return L01Waypoints(d, 0.6f,
                new[] { new Vector3(3.9f, 0f, 4.5f), new Vector3(4.7f, 0f, 8.4f), new Vector3(6.5f, 0f, 9.4f) },
                new[] { "through the grate", null, "behind the crate" });
            yield return d.Wait(0.8f);
            yield return d.Freeze();
            yield return d.Wait(0.3f);
            yield return L01PushCrate(d);
            yield return d.Wait(0.6f);
            d.Melt();
            yield return d.Wait(1.2f);
        }

        // ------------------------------------------------------------------ L01_dock / L01_dock_v (delivery)

        static readonly Vector3 L01DockStart = new Vector3(0f, 0.3f, 10.1f);
        // Low over the sea south-west of the deck, on the line through the pad and the flag pole: the pad in the lower
        // middle, the pole rising right behind it, the blob coming in from the right past the arch.
        static readonly Vector3 L01DockCam = new Vector3(-5.0f, 1.5f, 15.2f);
        static readonly Vector3 L01DockLook = new Vector3(0f, 1.0f, 17.7f);
        static readonly Vector3 L01DockCraneCam = new Vector3(-5.3f, 3.0f, 15.5f);
        static readonly Vector3 L01DockCraneLook = new Vector3(0.4f, 1.5f, 18.0f);
        static readonly Vector3 L01DockCamV = new Vector3(-4.9f, 1.4f, 14.6f);
        static readonly Vector3 L01DockLookV = new Vector3(0f, 1.15f, 16.6f);
        static readonly Vector3 L01DockCraneCamV = new Vector3(-4.4f, 1.3f, 15.0f);
        static readonly Vector3 L01DockCraneLookV = new Vector3(0.35f, 1.85f, 17.7f);

        /// <summary>Puts the blob and ship in the antechamber (north of the shut gate), ship bow +Z.</summary>
        static IEnumerator L01DockStage(TrailerDirector d)
        {
            yield return d.Teleport(L01DockStart, true, Vector3.forward);
            yield return d.Wait(1.2f);
        }

        /// <summary>Up the pier and under the arch, glide onto the pad and stop; waits for the delivery.</summary>
        static IEnumerator L01DockArrive(TrailerDirector d)
        {
            ShipDock dock = L01Dock(d);
            yield return L01RunNorth(d, 0.5f, 15.4f, 0f, false, float.PositiveInfinity, 14.6f, "under the arch");
            yield return d.Steer(L01PadStop, 0.42f, 0.3f, 4f);
            d.Mark("on the pad");
            yield return d.WaitUntil(() => dock == null || dock.IsDelivered, 3f, "the delivery");
            d.Mark("delivered");
        }

        static IEnumerator L01DockSetup(TrailerDirector d)
        {
            // A clean arrival: the glass UI is hidden until the delivery, then the Delivered! card pours in.
            TrailerDirector.SetUi(false);
            yield return L01DockStage(d);
            // Rolling before the take (stick left held), so the blob comes into frame past the arch straight away.
            yield return L01RunNorth(d, 0.5f, 12.6f, 0f, false);
            d.Cam.Custom(t => (L01DockCam, Vector3.Lerp(L01DockLook, L01Subject(d), 0.15f), 44f));
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L01DockAction(TrailerDirector d)
        {
            yield return L01DockArrive(d);
            TrailerDirector.SetUi(true);
            d.Mark("Delivered! card pours in");
            L01CraneTo(d, L01DockCraneCam, L01DockCraneLook, 46f, 3.6f);
            d.Mark("crane up, flag hoisting");
            yield return d.Wait(4.6f);
            d.Mark("flag up");
            yield return d.Wait(0.4f);
        }

        static IEnumerator L01DockVSetup(TrailerDirector d)
        {
            yield return L01DockStage(d);
            // Rolling before the take (stick left held); the camera is placed once the blob is nearly past the arch
            // towers, so its aim never sweeps across the near tower.
            yield return L01RunNorth(d, 0.5f, 14.0f, 0f, false);
            d.Cam.Custom(t => (L01DockCamV, Vector3.Lerp(L01DockLookV, L01Subject(d), 0.5f), 56f));
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L01DockVAction(TrailerDirector d)
        {
            yield return L01DockArrive(d);
            // Push in a touch and tilt up the pole as the flag climbs: blob on the green pad low, flag high.
            L01CraneTo(d, L01DockCraneCamV, L01DockCraneLookV, 56f, 3.4f);
            d.Mark("tilt up, flag hoisting");
            yield return d.Wait(4.6f);
            d.Mark("flag up");
            yield return d.Wait(0.4f);
        }
    }
}
#endif
