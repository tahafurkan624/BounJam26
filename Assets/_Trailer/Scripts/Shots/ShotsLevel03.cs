#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Level03: the piercer pipe (blob and ship slurped in, the saw spins up, two halves pop out of two exits with the
    // ship riding the left one), the crate shoved onto the button, and the freeze lock ice pit.
    //
    // Layout, read from the scene file (the map PNGs also show MainMenu geometry; there is NO floor around the pipe):
    // - Start room: floor y 0 over x -3..3, z -3..3; walls 1.5 m tall, inner faces at x +-2.5, z +2.5 and z -2.5.
    //   The blob starts at (0,0.16,-0.5), the ship on it facing +Z.
    // - Mouth (0,0,-2.5), facing +Z into the room, between wall stubs at x +-0.5..1.0. Intake: core z -2.5..-1.7, |x| < 0.55.
    // - The pipe crosses open water to the saw housing at (0,0,-6) (radius ~1.3, top ~2.1; saw on top at y 1.5) and forks
    //   to Exit 1 (ship) at (-2.5,0,-10) and Exit 2 at (2.5,0,-10). Halves appear at (-+2.82,0.13,-10.51), 2.7 s after intake.
    // - Exit platforms: x -5.5..-1.5 and 1.5..5.5, z -13..-9 (water between them). Then x -5.5..-1.5 / 1.5..5.5, z -15..-13.
    // - Crate (3.5,0.5,-12), Button (7,0,-12) on the platform x 5.5..7.5, z -13..-11. Wall x 1.5..2.0, z -13..-10.5.
    // - Ice pit: floor y 0 at x 3.5..4.5, z -17..-15, between two 1.5 m blocks (x 2.5..3.5 with Crate (1) on top, and
    //   x 4.5..6.5); freeze zone x 3.5..4.5, y 0..1.2, z -17..-15.5. Its ice model has no collider, is opaque and open on
    //   top (y 0.1..1.6), so the inside of the pit only reads from a steep camera.
    // - Harbour (dock) around (12.9,1.5,-16).
    public static partial class TrailerShots
    {
        static void ShotsLevel03(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "L03_hero",
                scene = "Level03",
                note = "close on the blob and its paper ship in the start room, then the camera lifts over them and the wall " +
                       "and flies out over the pipe, revealing the saw, the fork, the exits and the harbour beyond",
                setup = d => L03HeroSetup(d),
                action = d => L03HeroAction(d),
                tail = 0.5f,
            });
            shots.Add(new Shot
            {
                id = "L03_pipe_wide",
                scene = "Level03",
                note = "wide: the blob with the ship flows into the pipe mouth, the saw whirs, two halves pop out of the two " +
                       "exits and the ship rides out on the left one; mouth and both exits in frame the whole time",
                setup = d => L03WideSetup(d),
                action = d => L03WideAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L03_pipe_tight",
                scene = "Level03",
                note = "tight: low beside the blob as it is gulped into the mouth (slow motion), then the camera cranes up over " +
                       "the wall to the saw and the fork as the two halves pop out, the ship on the left one",
                setup = d => L03TightSetup(d),
                action = d => L03TightAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L03_crate_gameplay",
                scene = "Level03",
                note = "gameplay camera: the liquid half splats into the crate and can't move it, freezes into a crystal and " +
                       "shoves the crate onto the red button, which presses and lights up",
                setup = d => L03CrateSetup(d),
                action = d => L03CrateAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L03_freeze_lock",
                scene = "Level03",
                note = "the half slides down the floor into the ice pit and is frozen solid on the spot (slow motion), stuck " +
                       "in the pit; the camera cranes down to look into it",
                setup = d => L03LockSetup(d),
                action = d => L03LockAction(d),
                tail = 0.6f,
            });
            shots.Add(new Shot
            {
                id = "L03_pipe_v",
                scene = "Level03",
                portrait = true,
                note = "portrait: from above the start room, the blob with the ship is slurped into the mouth at the bottom of " +
                       "the frame, the camera tilts down the pipe and the two halves pop out of the exits in the middle",
                setup = d => L03PortraitSetup(d),
                action = d => L03PortraitAction(d),
                tail = 0.6f,
            });
        }

        // ---------------------------------------------------------------- Places

        static readonly Vector3 L03Start = new Vector3(0f, 0.16f, -0.5f);
        static readonly Vector3 L03Pit = new Vector3(4f, 0f, -16.25f);
        // The crate moved 0.8 m east of where the level has it, so the half fits between it and the wall at x 2.
        static readonly Vector3 L03CrateFrom = new Vector3(4.3f, 0.5f, -12f);

        // Hero: a cubic path from low in the start room, over the blob and the south wall, to high above the pipe.
        // Jelly and ship together stand 0..1.35 m; the north wall's inner face is at z 2.5.
        static readonly Vector3 L03HeroP0 = new Vector3(0.5f, 0.62f, 1.95f);
        static readonly Vector3 L03HeroP1 = new Vector3(0.5f, 1.9f, 0.1f);
        static readonly Vector3 L03HeroP2 = new Vector3(0.5f, 5.3f, -2.0f);
        static readonly Vector3 L03HeroP3 = new Vector3(0.6f, 8.0f, -4.2f);
        static readonly Vector3 L03HeroLook0 = new Vector3(0f, 0.62f, -0.5f);
        static readonly Vector3 L03HeroLook1 = new Vector3(1.9f, 0.3f, -12.6f);

        // Wide: high over the water east of the level, looking west across the whole machine.
        static readonly Vector3 L03WideFrom = new Vector3(14.6f, 11.4f, -7.4f);
        static readonly Vector3 L03WideTo = new Vector3(13.4f, 10.6f, -6.6f);
        static readonly Vector3 L03WideLook = new Vector3(0f, 0.3f, -5.6f);

        // Tight: inside the start room's north-east corner, then up over the south wall to above the water by the pipe.
        static readonly Vector3 L03TightFrom = new Vector3(2.1f, 1.0f, 2.0f);
        static readonly Vector3 L03TightLook0 = new Vector3(0f, 0.35f, -2.0f);
        static readonly Vector3 L03TightTo = new Vector3(1.6f, 4.6f, -3.6f);
        static readonly Vector3 L03TightDrift = new Vector3(1.3f, 4.3f, -4.1f);
        static readonly Vector3 L03TightLook1 = new Vector3(0.4f, 0.6f, -8.4f);

        // Portrait: above the water north of the start room, then forward over the room while tilting down the pipe.
        static readonly Vector3 L03VFrom = new Vector3(0f, 7.2f, 3.6f);
        static readonly Vector3 L03VLook0 = new Vector3(0f, 0.4f, -1.4f);
        static readonly Vector3 L03VTo = new Vector3(0f, 8.6f, 0.4f);
        static readonly Vector3 L03VDrift = new Vector3(0f, 8.3f, -0.1f);
        static readonly Vector3 L03VLook1 = new Vector3(0f, 0.3f, -9.4f);

        // Freeze lock: high over the water south of the pit, craning down and in until it looks into the pit.
        static readonly Vector3 L03LockFrom = new Vector3(5.0f, 6.8f, -20.4f);
        static readonly Vector3 L03LockTo = new Vector3(4.5f, 6.1f, -18.2f);

        static Vector3 L03CamLast;

        // ---------------------------------------------------------------- Hero

        static IEnumerator L03HeroSetup(TrailerDirector d)
        {
            // The level's own start: the ship already rests on the jelly, bow toward the camera.
            yield return d.Wait(0.6f);
            d.FaceBlob(L03HeroP0 - L03Start);
            // The path is checked clear by hand; the wall pull-in would only make it jump.
            d.Cam.avoidWalls = false;
            d.Cam.Custom(t =>
            {
                float s = L03Ease(Mathf.Clamp01((t - 0.6f) / 6.2f));
                Vector3 position = L03Bezier(L03HeroP0, L03HeroP1, L03HeroP2, L03HeroP3, s);
                Vector3 look = Vector3.Lerp(L03HeroLook0, L03HeroLook1, s);
                return (position, look, Mathf.Lerp(36f, 52f, s));
            });
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L03HeroAction(TrailerDirector d)
        {
            d.Mark("close on the blob and ship");
            yield return d.Wait(0.2f);
            d.Mark("rise");
            yield return d.Wait(3.95f);
            d.Mark("over the wall");
            yield return d.Wait(2.25f);
            d.Mark("level revealed");
            yield return d.Wait(0.5f);
        }

        // ---------------------------------------------------------------- Pipe, wide

        static IEnumerator L03WideSetup(TrailerDirector d)
        {
            yield return d.Teleport(new Vector3(0f, 0.25f, 0.9f), true, Vector3.back);
            yield return d.Wait(1.3f);
            d.Cam.avoidWalls = false;
            d.Cam.Dolly(L03WideFrom, L03WideTo, L03WideLook, 8f, 34f, 33f);
            d.Cam.Sway(0.25f);
        }

        static IEnumerator L03WideAction(TrailerDirector d)
        {
            yield return L03PipeRun(d, 0.6f, null);
            yield return d.Wait(1.8f);
            d.Mark("halves settled");
        }

        // ---------------------------------------------------------------- Pipe, tight

        static IEnumerator L03TightSetup(TrailerDirector d)
        {
            yield return d.Teleport(new Vector3(0f, 0.25f, 0.5f), true, Vector3.back);
            yield return d.Wait(1.3f);
            d.FaceBlob(Vector3.back);
            d.Cam.avoidWalls = false;
            d.Cam.Static(L03TightFrom, L03TightLook0, 40f);
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L03TightAction(TrailerDirector d)
        {
            yield return L03PipeRun(d, 0.55f, () => L03TightGulp(d));
            yield return d.Wait(1.8f);
            d.Mark("halves settled");
        }

        // At the intake: the camera move starts (keyed to now, in game time) and the gulp plays in slow motion.
        static IEnumerator L03TightGulp(TrailerDirector d)
        {
            d.Cam.Custom(t =>
            {
                // Hold through the gulp, crane up over the wall to the saw and the fork, then creep in on the exits.
                float k = L03Ease(Mathf.Clamp01((t - 0.18f) / 1.4f));
                float drift = L03Ease(Mathf.Clamp01((t - 1.58f) / 3.2f));
                Vector3 position = Vector3.Lerp(Vector3.Lerp(L03TightFrom, L03TightTo, k), L03TightDrift, drift);
                Vector3 look = Vector3.Lerp(L03TightLook0, L03TightLook1, k);
                return (position, look, Mathf.Lerp(40f, 44f, k));
            });
            yield return d.SlowTo(0.35f, 0.08f);
            yield return d.WaitClip(0.5f);
            yield return d.SlowTo(1f, 0.25f);
        }

        // ---------------------------------------------------------------- Pipe, portrait

        static IEnumerator L03PortraitSetup(TrailerDirector d)
        {
            yield return d.Teleport(new Vector3(0f, 0.25f, 0.6f), true, Vector3.back);
            yield return d.Wait(1.3f);
            d.FaceBlob(Vector3.forward);
            d.Cam.avoidWalls = false;
            d.Cam.Static(L03VFrom, L03VLook0, 52f);
            d.Cam.Sway(0.3f);
        }

        static IEnumerator L03PortraitAction(TrailerDirector d)
        {
            yield return L03PipeRun(d, 0.6f, () => L03PortraitTilt(d));
            yield return d.Wait(1.8f);
            d.Mark("halves settled");
        }

        // At the intake: forward over the room and down the pipe, arriving on the exits just before the halves pop out.
        static IEnumerator L03PortraitTilt(TrailerDirector d)
        {
            d.Cam.Custom(t =>
            {
                float k = L03Ease(Mathf.Clamp01((t - 0.15f) / 2.4f));
                float drift = L03Ease(Mathf.Clamp01((t - 2.55f) / 3f));
                Vector3 position = Vector3.Lerp(Vector3.Lerp(L03VFrom, L03VTo, k), L03VDrift, drift);
                Vector3 look = Vector3.Lerp(L03VLook0, L03VLook1, k);
                return (position, look, Mathf.Lerp(52f, 54f, k));
            });
            yield break;
        }

        // ---------------------------------------------------------------- The pipe run (wide, tight, portrait)

        /// <summary>
        /// Drives the whole blob (ship on top) straight at the mouth, lets go the moment the pipe takes it, and waits
        /// through the saw and the cut until both halves are out (about 2.7 s of game time after the intake).
        /// </summary>
        static IEnumerator L03PipeRun(TrailerDirector d, float speed, System.Func<IEnumerator> atIntake)
        {
            BlobSplit whole = d.Blob.GetComponent<BlobSplit>();
            PiercerPipe pipe = L03FindPipe(d);
            d.Mark("go");
            d.Move(new Vector2(0f, -speed));
            yield return d.WaitUntil(() => whole == null || whole.InPipe, 3f, "the pipe to take the blob");
            // Zero now, or the ship half drives off with the old stick when it comes out.
            d.Halt();
            d.Mark("slurp");
            if (atIntake != null) yield return atIntake();
            yield return d.WaitUntil(() => L03Cutting(pipe) || d.Pieces.Count >= 2, 2.5f, "the saw");
            d.Mark("saw spins up");
            yield return d.WaitUntil(() => d.Pieces.Count >= 2, 1.5f, "the cut");
            // The second half is a new reader: give it a held-still stick too.
            d.Halt();
            d.Mark("cut in two");
            yield return d.WaitUntil(() => L03AllOut(d), 2.5f, "both halves out");
            d.Mark("pop out");
        }

        static PiercerPipe L03FindPipe(TrailerDirector d)
        {
            PiercerPipe pipe = d.Get<PiercerPipe>("Piercer Pipe");
            return pipe != null ? pipe : Object.FindAnyObjectByType<PiercerPipe>();
        }

        static bool L03Cutting(PiercerPipe pipe) =>
            pipe != null && TrailerDirector.GetMember(pipe, "cutting") is bool cutting && cutting;

        static bool L03AllOut(TrailerDirector d)
        {
            if (d.Pieces.Count < 2) return false;
            foreach (BlobSplit piece in d.Pieces)
                if (piece == null || piece.InPipe) return false;
            return true;
        }

        // ---------------------------------------------------------------- Crate push (gameplay camera)

        static IEnumerator L03CrateSetup(TrailerDirector d)
        {
            Pushable crate = d.Get<Pushable>("Crate");
            if (crate != null) TrailerDirector.Place(crate.transform, L03CrateFrom);
            // Without the ship (it drops onto the start room floor): in the level a half without the ship does this job.
            yield return d.Teleport(new Vector3(2.85f, 0.25f, -12f), false);
            yield return d.Wait(0.3f);
            L03Halve(d);
            yield return d.Wait(0.9f);
            d.FaceBlob(Vector3.right);
            d.Cam.Gameplay();
            // The game's follow camera was left at the start room: let it catch up and come to rest.
            L03CamLast = new Vector3(9999f, 9999f, 9999f);
            yield return d.WaitUntil(() => L03CamSettled(d), 4f, "the game camera to catch up");
            yield return d.Wait(0.3f);
        }

        static IEnumerator L03CrateAction(TrailerDirector d)
        {
            Pushable crate = d.Get<Pushable>("Crate");
            PressureButton button = d.Get<PressureButton>("Button");
            d.Mark("liquid runs at the crate");
            d.Move(new Vector2(0.5f, 0f));
            yield return d.WaitUntil(() => d.Blob.position.x > 3.3f, 0.8f, "the crate");
            d.Mark("splat, the crate doesn't budge");
            yield return d.Wait(0.4f);
            d.Halt();
            yield return d.Wait(0.75f);
            yield return d.Freeze();
            yield return d.Wait(0.35f);
            d.Mark("push");
            d.Move(new Vector2(0.85f, 0f));
            yield return d.WaitUntil(() => button != null && button.IsPressed, 3.5f, "the button");
            d.Mark("pressed");
            // On to the middle of the plate (it is 0.5 m from the platform's far edge), then let the crystal brake.
            yield return d.WaitUntil(() => crate == null || crate.transform.position.x >= 6.5f, 1.5f, "the crate on the plate");
            d.Halt();
            d.Mark("stop");
            yield return d.Wait(1.6f);
        }

        static bool L03CamSettled(TrailerDirector d)
        {
            if (d.Cam == null || d.Cam.Camera == null) return true;
            Vector3 now = d.Cam.Camera.transform.position;
            float moved = (now - L03CamLast).magnitude;
            L03CamLast = now;
            Vector3 offset = now - d.Blob.position;
            return moved < 0.002f && Mathf.Abs(offset.x) < 1f && offset.z < -4f;
        }

        // ---------------------------------------------------------------- Freeze lock

        static IEnumerator L03LockSetup(TrailerDirector d)
        {
            // Off until the jelly is in the middle of the pit: on, it would freeze the half at the pit's edge, under the
            // ice model's rim where no camera sees it.
            FreezeLockZone zone = d.Get<FreezeLockZone>("Freeze Zone");
            if (zone != null) zone.enabled = false;
            yield return d.Teleport(new Vector3(L03Pit.x, 0.25f, -10.8f), false);
            yield return d.Wait(0.3f);
            L03Halve(d);
            yield return d.Wait(0.9f);
            d.FaceBlob(Vector3.back);
            d.Cam.avoidWalls = false;
            float lookZ = -12f;
            float lookSpeed = 0f;
            float last = 0f;
            d.Cam.Custom(t =>
            {
                float dt = Mathf.Max(0f, t - last);
                last = t;
                // Follows the half down the floor and into the pit, smoothed.
                float want = Mathf.Clamp(d.Blob.position.z, -16.15f, -12f);
                lookZ = Mathf.SmoothDamp(lookZ, want, ref lookSpeed, 0.35f, Mathf.Infinity, dt);
                float k = L03Ease(Mathf.Clamp01((t - 0.3f) / 2.8f));
                return (Vector3.Lerp(L03LockFrom, L03LockTo, k), new Vector3(L03Pit.x, 0.3f, lookZ), Mathf.Lerp(38f, 34f, k));
            });
            d.Cam.Sway(0.25f);
        }

        static IEnumerator L03LockAction(TrailerDirector d)
        {
            FreezeLockZone zone = d.Get<FreezeLockZone>("Freeze Zone");
            d.Mark("go");
            yield return L03DriveZ(d, -14.3f, 0.75f, 3f);
            // Slower into the slot between the two blocks.
            yield return L03DriveZ(d, -15.6f, 0.45f, 2f);
            d.Mark("into the ice pit");
            yield return d.SlowTo(0.3f, 0.12f);
            yield return L03DriveZ(d, -16.05f, 0.45f, 2f);
            if (zone != null) zone.enabled = true;
            d.Halt();
            yield return d.WaitUntil(() => d.Form.IsSolid, 0.6f, "the freeze lock");
            d.Mark("frozen solid");
            yield return d.WaitClip(0.9f);
            yield return d.SlowTo(1f, 0.3f);
            d.Mark("locked in the pit");
            yield return d.Wait(1.2f);
        }

        /// <summary>Holds the stick toward -Z (nudging back onto the pit's centre line) until the core is past <paramref name="z"/>.</summary>
        static IEnumerator L03DriveZ(TrailerDirector d, float z, float speed, float timeout)
        {
            for (float t = 0f; t < timeout && d.Blob.position.z > z; t += Time.deltaTime)
            {
                float steerX = Mathf.Clamp((L03Pit.x - d.Blob.position.x) * 1.5f, -0.25f, 0.25f);
                d.Move(new Vector2(steerX, -speed));
                yield return null;
            }
        }

        // ---------------------------------------------------------------- Helpers

        /// <summary>The whole blob at half its water: the size and mass of a piercer-pipe half, without the pipe.</summary>
        static void L03Halve(TrailerDirector d)
        {
            BlobSize size = d.Blob.GetComponent<BlobSize>();
            if (size != null && size.Volume > 0.75f) size.Resize(0.5f);
        }

        static float L03Ease(float k) => k * k * (3f - 2f * k);

        static Vector3 L03Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 e, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * e;
        }
    }
}
#endif
