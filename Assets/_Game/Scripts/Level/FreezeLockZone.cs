using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A trigger region that keeps the blob frozen: a piece whose core is inside freezes (as soon as it can) and the
    /// player can't melt or move it: it stays put unless something else carries it out, and being stuck there doesn't
    /// lose the level. It counts each piece on its own, so when the blob was cut in two, only the half that is inside
    /// is held; the twin outside moves, freezes and melts as usual. Leaving doesn't melt the crystal.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class FreezeLockZone : MonoBehaviour
    {
        Collider area;
        readonly HashSet<BlobForm> held = new HashSet<BlobForm>();
        readonly List<BlobForm> released = new List<BlobForm>();

        void Awake()
        {
            area = GetComponent<Collider>();
            area.isTrigger = true;
        }

        void OnDisable()
        {
            foreach (BlobForm form in held)
                if (form != null) form.SetFreezeLock(false);
            held.Clear();
        }

        void FixedUpdate()
        {
            // Checked by position rather than trigger events: the blob swaps its colliders when it freezes and melts,
            // and halves come and go.
            released.Clear();
            foreach (BlobForm form in held)
                if (form == null || !form.isActiveAndEnabled || !Contains(form)) released.Add(form);
            foreach (BlobForm form in released)
            {
                if (form != null) form.SetFreezeLock(false);
                held.Remove(form);
            }

            IReadOnlyList<BlobSplit> pieces = BlobSplit.All;
            for (int i = 0; i < pieces.Count; i++)
            {
                BlobForm form = pieces[i].Form;
                if (form == null || held.Contains(form) || pieces[i].InPipe || !Contains(form)) continue;
                held.Add(form);
                form.SetFreezeLock(true);
            }

            // Freezing can be refused for a moment (partway through a grate, skin still settling), so keep trying.
            foreach (BlobForm form in held)
                if (!form.IsSolid) form.Freeze();
        }

        bool Contains(BlobForm form)
        {
            Vector3 core = form.transform.position;
            return (area.ClosestPoint(core) - core).sqrMagnitude < 0.0001f;
        }

        void OnDrawGizmos()
        {
            if (!TryGetComponent(out BoxCollider box)) return;
            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
