using Unity.Cinemachine;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Keeps the camera's target group in step with the blob's pieces (<see cref="BlobSplit.All"/>): the whole blob, or
    /// both halves once a piercer pipe has cut it, so the camera follows and frames all of it. With no pieces in the
    /// scene the group is left as it was built.
    /// </summary>
    [RequireComponent(typeof(CinemachineTargetGroup))]
    public class BlobCameraGroup : MonoBehaviour
    {
        [Tooltip("Room the camera keeps around each piece's centre, m.")]
        [SerializeField] float pieceRadius = 1f;

        CinemachineTargetGroup group;

        void Awake() => group = GetComponent<CinemachineTargetGroup>();

        // In Update, so the group and the camera, which both update in LateUpdate, see this frame's pieces.
        void Update()
        {
            var pieces = BlobSplit.All;
            if (pieces.Count == 0) return;

            for (int i = group.Targets.Count - 1; i >= 0; i--)
            {
                Transform member = group.Targets[i].Object;
                if (member == null || !member.TryGetComponent(out BlobSplit piece) || !IsPiece(piece))
                    group.Targets.RemoveAt(i);
            }
            foreach (BlobSplit piece in pieces)
                if (group.FindMember(piece.transform) < 0) group.AddMember(piece.transform, 1f, pieceRadius);
        }

        static bool IsPiece(BlobSplit piece)
        {
            foreach (BlobSplit listed in BlobSplit.All)
                if (listed == piece) return true;
            return false;
        }
    }
}
