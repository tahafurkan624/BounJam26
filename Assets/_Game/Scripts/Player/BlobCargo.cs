using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// What the blob carries inside. Every physics step in liquid form it tells the waiting collectibles nearby
    /// whether they are fully inside the jelly; on freezing, every one that is gets stuck in the crystal and stays with
    /// the blob from then on. In the crystal an item keeps its place and turns with it; in the liquid it sinks to the
    /// bottom of the jelly and sways a little behind the blob's speed changes.
    /// </summary>
    [RequireComponent(typeof(BlobForm), typeof(BlobSkin))]
    public class BlobCargo : MonoBehaviour
    {
        [Tooltip("Waiting collectibles closer than this to the core are checked against the jelly, m.")]
        [SerializeField] float checkRadius = 1.5f;
        [Tooltip("Gap between a carried item and the bottom of the core in the liquid, m.")]
        [SerializeField] float sinkGap = 0.03f;
        [Tooltip("Carried items after the first rest around it, this far out, m.")]
        [SerializeField] float spread = 0.17f;
        [Tooltip("How long carried items take to settle in the liquid, s.")]
        [SerializeField] float settleTime = 0.15f;
        [Tooltip("How far items lag behind the blob's speed changes, m per m/s².")]
        [SerializeField] float sway = 0.006f;
        [Tooltip("Most an item lags behind, m.")]
        [SerializeField] float maxSway = 0.08f;

        // Share of an item's radius, out from its centre, that has to be inside the jelly: checked to the sides and
        // above (below is the floor it rests on).
        const float SampleReach = 0.7f;

        class Item
        {
            public Collectible Collectible;
            public Transform Transform;
            // Solid: the item's place in the crystal's local space.
            public Vector3 Local;
            public Quaternion LocalRotation;
            // Liquid: world offset from the core, eased toward the item's slot.
            public Vector3 Offset;
            public Vector3 OffsetVelocity;
        }

        readonly List<Item> carried = new List<Item>();
        BlobForm form;
        BlobSkin skin;
        BlobMotor motor;
        Rigidbody body;
        Vector3 previousVelocity;
        Vector3 acceleration;

        public int Count => carried.Count;
        /// <summary>Raised when the crystal catches a collectible.</summary>
        public event Action<Collectible> Collected;

        /// <summary>Is the blob carrying a collectible with this component (for example <see cref="IronBall"/>)?</summary>
        public bool Has<T>() where T : Component
        {
            foreach (Item item in carried)
                if (item.Collectible.TryGetComponent(out T _)) return true;
            return false;
        }

        void Awake()
        {
            form = GetComponent<BlobForm>();
            skin = GetComponent<BlobSkin>();
            motor = GetComponent<BlobMotor>();
            body = GetComponent<Rigidbody>();
        }

        void OnEnable() => form.FormChanged += OnFormChanged;

        void OnDisable() => form.FormChanged -= OnFormChanged;

        void FixedUpdate()
        {
            Vector3 velocity = body.linearVelocity;
            acceleration = Vector3.Lerp(acceleration, (velocity - previousVelocity) / Time.fixedDeltaTime, 0.3f);
            previousVelocity = velocity;

            bool liquid = !form.IsSolid && skin.IsReady;
            IReadOnlyList<Collectible> waiting = Collectible.Waiting;
            for (int i = 0; i < waiting.Count; i++)
                waiting[i].SetEngulfed(liquid && IsInside(waiting[i]));
        }

        void OnFormChanged(bool solid)
        {
            if (solid)
            {
                // Collecting takes an item off the waiting list, so walk it backwards.
                for (int i = Collectible.Waiting.Count - 1; i >= 0; i--)
                {
                    Collectible collectible = Collectible.Waiting[i];
                    if (IsInside(collectible)) Catch(collectible);
                }
                // The crystal settles onto its lowest point, a little lower than the jelly hung: lift items resting
                // on the floor with it, so they don't poke out of its underside.
                float bottom = float.PositiveInfinity;
                foreach (Vector3 point in form.HullPoints) bottom = Mathf.Min(bottom, point.y);
                foreach (Item item in carried)
                {
                    Vector3 local = transform.InverseTransformPoint(item.Transform.position);
                    local.y = Mathf.Max(local.y, bottom + item.Collectible.Radius + sinkGap);
                    item.Local = local;
                    item.LocalRotation = Quaternion.Inverse(transform.rotation) * item.Transform.rotation;
                }
            }
            else
            {
                // Melting reset the rotation; carry on from wherever the crystal had the items.
                foreach (Item item in carried)
                {
                    item.Offset = item.Transform.position - transform.position;
                    item.OffsetVelocity = Vector3.zero;
                }
            }
        }

        /// <summary>Takes over everything another blob carries (two halves flowing back together), from where it is now.</summary>
        public void TakeOver(BlobCargo other)
        {
            foreach (Item item in other.carried)
            {
                item.Offset = item.Transform.position - transform.position;
                item.OffsetVelocity = Vector3.zero;
                carried.Add(item);
            }
            other.carried.Clear();
        }

        void Catch(Collectible collectible)
        {
            collectible.Collect();
            // Off whatever it was placed under: from now on only the blob moves it.
            collectible.transform.SetParent(null, true);
            carried.Add(new Item { Collectible = collectible, Transform = collectible.transform });
            Collected?.Invoke(collectible);
        }

        bool IsInside(Collectible collectible)
        {
            Vector3 center = collectible.transform.position;
            if ((center - body.position).sqrMagnitude > checkRadius * checkRadius) return false;
            float reach = collectible.Radius * SampleReach;
            return skin.Contains(center)
                   && skin.Contains(center + Vector3.up * reach)
                   && skin.Contains(center + Vector3.right * reach)
                   && skin.Contains(center - Vector3.right * reach)
                   && skin.Contains(center + Vector3.forward * reach)
                   && skin.Contains(center - Vector3.forward * reach);
        }

        void LateUpdate()
        {
            if (carried.Count == 0) return;
            Vector3 lag = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(-acceleration * sway, Vector3.up), maxSway);
            for (int i = 0; i < carried.Count; i++)
            {
                Item item = carried[i];
                if (form.IsSolid)
                {
                    item.Transform.SetPositionAndRotation(transform.TransformPoint(item.Local), transform.rotation * item.LocalRotation);
                    continue;
                }
                Vector3 slot = Slot(i, item.Collectible.Radius) + lag;
                item.Offset = Vector3.SmoothDamp(item.Offset, slot, ref item.OffsetVelocity, settleTime, Mathf.Infinity, Time.deltaTime);
                item.Transform.position = transform.position + item.Offset;
            }
        }

        /// <summary>Where an item rests in the liquid, from the core: on the bottom, the first in the middle, the rest around it.</summary>
        Vector3 Slot(int index, float itemRadius)
        {
            Vector3 slot = Vector3.up * (itemRadius + sinkGap - motor.CoreRadius);
            if (index == 0) return slot;
            float angle = (index - 1) * 2.4f;
            return slot + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spread;
        }
    }
}
