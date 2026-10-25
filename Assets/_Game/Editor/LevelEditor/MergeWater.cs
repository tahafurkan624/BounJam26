using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Kind = BounJam.LevelPiece.Kind;

namespace BounJam.EditorTools
{
    /// <summary>
    /// Replaces every water piece in the open scene with one that covers them all, so the level has a single body of
    /// water (the level editor's water pieces have their top at the pivot, so all of them must share a surface height).
    /// </summary>
    static class MergeWater
    {
        [MenuItem("BounJam/Level/Merge Water Pieces Into One")]
        static void Merge()
        {
            List<LevelPiece> pieces = Object.FindObjectsByType<LevelPiece>(FindObjectsSortMode.None)
                                            .Where(p => p.PieceKind == Kind.Water).ToList();
            if (pieces.Count < 2)
            {
                Debug.Log($"[MergeWater] {pieces.Count} water piece(s) in the scene, nothing to merge.");
                return;
            }

            Bounds all = default;
            for (int i = 0; i < pieces.Count; i++)
            {
                Transform t = pieces[i].transform;
                Bounds local = LevelPieces.LocalBounds(pieces[i]);
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((c & 1) * 2 - 1, (c >> 1 & 1) * 2 - 1, (c >> 2 & 1) * 2 - 1));
                    Vector3 world = t.TransformPoint(corner);
                    if (i == 0 && c == 0) all = new Bounds(world, Vector3.zero);
                    else all.Encapsulate(world);
                }
            }

            Transform parent = pieces[0].transform.parent;
            int group = Undo.GetCurrentGroup();
            foreach (LevelPiece piece in pieces) Undo.DestroyObjectImmediate(piece.gameObject);
            LevelPiece merged = LevelPieces.Create(Kind.Water, "Water", parent,
                                                   new Vector3(all.center.x, all.max.y, all.center.z), Quaternion.identity, all.size);
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(merged.gameObject.scene);
            Debug.Log($"[MergeWater] merged {pieces.Count} water pieces into one: min {all.min} max {all.max}");
        }
    }
}
