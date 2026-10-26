#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam.Trailer
{
    // Second-pass shots for moments the first takes didn't show well.
    public static partial class TrailerShots
    {
        static void ShotsFixes(List<Shot> shots)
        {
            shots.Add(new Shot
            {
                id = "FIX_L03_split",
                scene = "Level03",
                note = "the blob and its ship dive into the piercer pipe; two halves shoot out of the two exits toward the camera",
                setup = d => FixSplitSetup(d),
                action = d => FixSplitAction(d),
                tail = 1.0f,
            });
            shots.Add(new Shot
            {
                id = "FIX_L03_split_v",
                scene = "Level03",
                portrait = true,
                note = "portrait: the piercer pipe splits the blob in two",
                setup = d => FixSplitSetup(d, true),
                action = d => FixSplitAction(d),
                tail = 1.0f,
            });
            shots.Add(new Shot
            {
                id = "FIX_L03_freezepit",
                scene = "Level03",
                note = "the jelly flows into the icy pit and freezes solid by itself, locked in",
                setup = d => FixPitSetup(d),
                action = d => FixPitAction(d),
                tail = 0.8f,
            });
        }

        // Level03 (Temp/Trailer/maps/Level03_heights.txt): the pipe's mouth is at (0,0,-2.5) facing +Z; the halves come
        // out at about (-2.8,_,-10.5) and (2.8,_,-10.5) into two rooms (x -4.5..-1.5 and 3..5, z -10..-14.5) with water
        // between them, walled to the south at z -15 (1.5 m) and again at z -17.
        static IEnumerator FixSplitSetup(TrailerDirector d, bool portrait = false)
        {
            yield return d.Wait(0.6f);
            d.FaceShip(Vector3.back);
            // Over the water channel between the two exit rooms, low, so the halves shoot out toward the lens.
            if (portrait)
                d.Cam.Dolly(new Vector3(-1.6f, 2.5f, -14.6f), new Vector3(-1.8f, 2.2f, -14.1f), new Vector3(-2.7f, 0.45f, -10.7f), 6f, 66f, 62f);
            else
                d.Cam.Dolly(new Vector3(0f, 2.7f, -14.7f), new Vector3(0f, 2.35f, -14.1f), new Vector3(0f, 0.4f, -10.3f), 6f, 58f, 55f);
        }

        static IEnumerator FixSplitAction(TrailerDirector d)
        {
            d.Mark("go");
            d.Move(new Vector2(0f, -1f));
            BlobSplit whole = d.Blob.GetComponent<BlobSplit>();
            yield return d.WaitUntil(() => whole.InPipe, 2.5f, "the slurp");
            d.Halt();
            d.Mark("slurp");
            yield return d.WaitUntil(() => BlobSplit.All.Count == 2, 3f, "the cut");
            d.Mark("cut");
            yield return d.WaitUntil(() => BlobSplit.All.Count == 2 && !BlobSplit.All[0].InPipe && !BlobSplit.All[1].InPipe, 3f, "both halves out");
            d.Halt();
            d.Mark("pop out");
            yield return d.Wait(1.6f);
        }

        static IEnumerator FixPitSetup(TrailerDirector d)
        {
            // South of the crate that blocks the east room, in line with the pit's opening at x 4.
            yield return d.Teleport(new Vector3(4f, 0.16f, -13.6f), false);
            yield return d.Wait(0.9f);
            d.FaceBlob(Vector3.forward);
            d.Cam.Dolly(new Vector3(4.05f, 1.95f, -12.7f), new Vector3(4.0f, 1.7f, -13.2f), new Vector3(4f, 0.3f, -16.1f), 4f, 40f, 36f);
        }

        static IEnumerator FixPitAction(TrailerDirector d)
        {
            d.Mark("into the pit");
            yield return d.Steer(new Vector3(4f, 0f, -16.2f), 0.55f, 0.3f, 4f);
            yield return d.WaitUntil(() => d.Form.IsFreezeLocked, 2f, "the freeze lock");
            d.Mark("locked in ice");
            // It can't move: the stick does nothing now.
            d.Move(new Vector2(0f, 1f));
            yield return d.Wait(1.6f);
            d.Halt();
        }
    }
}
#endif
