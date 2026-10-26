#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Short close inserts of the blob itself, staged on Level01's open floor (x -5.5..4.5, z 4..8.5).
    public static partial class TrailerShots
    {
        static void ShotsInserts(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "INS_streak",
                scene = "Level01",
                note = "the blob sprints and stretches into a streak, then stops dead and flings droplets (slow motion on the stop)",
                setup = d => InsStreakSetup(d),
                action = d => InsStreakAction(d),
                tail = 1.2f,
            });
            shots.Add(new Shot
            {
                id = "INS_jumpfreeze",
                scene = "Level01",
                note = "jump, freeze into a crystal in mid-air (slow motion), crash, tumble and melt back into jelly",
                setup = d => InsJumpSetup(d),
                action = d => InsJumpAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "INS_face",
                scene = "Level01",
                note = "macro: the blob breathing with the paper ship on top, slow orbit across its face",
                setup = d => InsFaceSetup(d),
                action = d => d.Wait(4.5f),
            });
            shots.Add(new Shot
            {
                id = "INS_shipdrop",
                scene = "Level01",
                note = "the paper ship drops onto the jelly and lands in a crown of droplets (slow motion)",
                setup = d => InsDropSetup(d),
                action = d => InsDropAction(d),
                tail = 0.8f,
            });
            shots.Add(new Shot
            {
                id = "INS_jumpfreeze_v",
                scene = "Level01",
                portrait = true,
                note = "portrait: jump, mid-air freeze, crash and melt",
                setup = d => InsJumpSetup(d, true),
                action = d => InsJumpAction(d),
                tail = 0.8f,
            });
        }

        // Level01 walkable floor (Temp/Trailer/maps/Level01_heights.txt): the start corridor x -2..2 from z -2 to the
        // gate at z 8.8 (1.5 m walls at |x| 2.5..3), a doorway at z 4..5 into the button room (x 3.5..8, z 1..11).
        // The checker tiles drawn west of the corridor have no floor under them.
        static IEnumerator InsStreakSetup(TrailerDirector d)
        {
            // The blob starts here with the ship on it: a run up the corridor with the ship riding along.
            yield return d.Wait(0.6f);
            d.FaceShip(Vector3.forward);
            d.FaceBlob(Vector3.forward);
            d.Cam.Follow(() => d.Blob.position, new Vector3(1.5f, 0.55f, -1.7f), new Vector3(0f, 0.35f, 0.9f), 0.22f, 40f);
        }

        static IEnumerator InsStreakAction(TrailerDirector d)
        {
            d.Mark("go");
            d.Move(new Vector2(0f, 1f));
            yield return d.WaitUntil(() => d.Blob.position.z > 5.6f, 3f, "the far end");
            yield return d.SlowTo(0.3f, 0.12f);
            d.Halt();
            d.Mark("stop");
            yield return d.WaitClip(1.6f);
            yield return d.SlowTo(1f, 0.3f);
        }

        static IEnumerator InsJumpSetup(TrailerDirector d, bool portrait = false)
        {
            yield return d.Teleport(new Vector3(-0.3f, 0.3f, 1.2f), false);
            yield return d.Wait(0.8f);
            d.FaceBlob(Vector3.right);
            // Side-on from inside the corridor (its east wall is at x 2.5), following the whole stunt.
            if (portrait) d.Cam.Follow(() => d.Blob.position, new Vector3(1.9f, 0.8f, -0.5f), new Vector3(0f, 0.55f, 0.5f), 0.4f, 58f);
            else d.Cam.Follow(() => d.Blob.position, new Vector3(2.0f, 0.5f, -0.6f), new Vector3(0f, 0.45f, 0.6f), 0.4f, 44f);
            d.Cam.Sway(0.35f);
        }

        static IEnumerator InsJumpAction(TrailerDirector d)
        {
            d.Mark("run");
            d.Move(new Vector2(0f, 0.55f));
            yield return d.Wait(0.35f);
            d.Jump();
            yield return d.WaitUntil(() => !d.Motor.IsGrounded, 0.5f, "take-off");
            d.Mark("jump");
            yield return d.Wait(0.2f);
            yield return d.SlowTo(0.25f, 0.1f);
            yield return d.Freeze();
            yield return d.WaitClip(0.7f);
            yield return d.SlowTo(1f, 0.25f);
            d.Halt();
            d.StartRoll(Vector3.forward, 1.4f, 6f);
            yield return d.WaitUntil(() => d.Body.position.y < 0.45f, 1.5f, "landing");
            d.Mark("crash");
            // Let it tumble a moment, then give it back its brake and melt it once it rests.
            yield return d.Wait(1.1f);
            d.EndRoll();
            yield return d.WaitUntil(() => d.Body.linearVelocity.magnitude < 0.25f, 2.5f, "the crystal to stop");
            yield return d.Wait(0.3f);
            d.Melt();
            yield return d.Wait(1.0f);
        }

        static IEnumerator InsFaceSetup(TrailerDirector d)
        {
            yield return d.Teleport(new Vector3(0f, 0.3f, 5.0f), true, Vector3.left);
            yield return d.Wait(1.4f);
            d.FaceBlob(Vector3.back);
            d.Cam.Orbit(() => d.Blob.position, 2.8f, 0.45f, -20f, 25f, 6f, 30f, 0.5f);
        }

        static IEnumerator InsDropSetup(TrailerDirector d)
        {
            yield return d.Teleport(new Vector3(-1f, 0.3f, 6.6f), false);
            yield return d.Wait(1.2f);
            d.FaceBlob(Vector3.back);
            d.PlaceShip(new Vector3(-1f, 30f, 6.6f), Vector3.left);
            Vector3 at = new Vector3(-1f, 0.75f, 6.6f);
            d.Cam.Static(at + new Vector3(0.8f, 0.2f, -2.6f), at + Vector3.up * 0.35f, 40f);
        }

        static IEnumerator InsDropAction(TrailerDirector d)
        {
            // Out of frame high up until now; drop it from 1.4 m above the jelly.
            d.PlaceShip(new Vector3(-1f, 2.4f, 6.6f), Vector3.left);
            d.Mark("drop");
            yield return d.WaitUntil(() => d.Ship.transform.position.y < 1.35f, 1.5f, "the fall");
            yield return d.SlowTo(0.3f, 0.08f);
            d.Mark("splash");
            yield return d.WaitClip(1.5f);
            yield return d.SlowTo(1f, 0.3f);
            yield return d.Wait(0.5f);
        }
    }
}
#endif
