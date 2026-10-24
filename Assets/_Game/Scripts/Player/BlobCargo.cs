using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// What the crystal holds. Every physics step in liquid form it tells the waiting collectibles nearby whether they
    /// are fully inside the jelly; on freezing, every one that is gets stuck in the crystal, keeps its place in it and
    /// turns with it. Melting lets go of everything: each item drops from where the crystal had it, right inside the
    /// fresh jelly, so freezing again straight away picks it back up.
    /// </summary>
    [RequireComponent(typeof(BlobForm), typeof(BlobSkin))]
    public class BlobCargo : MonoBehaviour
    {
        [Tooltip("Waiting collectibles closer than this to the core are checked against the jelly, m.")]
        [SerializeField] float checkRadius = 1.5f;
        [Tooltip("Gap kept between a held item and the underside of the crystal, m.")]
        [SerializeField] float sinkGap = 0.03f;

        // Share of an item's radius, out from its centre, that has to be inside the jelly: checked to the sides and
        // above (below is the floor it rests on).
        const float SampleReach = 0.7f;

        class Item
        {
            public Collectible Collectible;
            public Transform Transform;
            // The item's place in the crystal's local space.
            public Vector3 Local;
            public Quaternion LocalRotation;
        }

        readonly List<Item> carried = new List<Item>();
        BlobForm form;
        BlobSkin skin;
        Rigidbody body;

        public int Count => carried.Count;
        /// <summary>Raised when the crystal catches a collectible.</summary>
        public event Action<Collectible> Collected;

        /// <summary>Is the crystal holding a collectible with this component (for example <see cref="IronBall"/>)?</summary>
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
            body = GetComponent<Rigidbody>();
        }

        void OnEnable() => form.FormChanged += OnFormChanged;

        void OnDisable() => form.FormChanged -= OnFormChanged;

        void FixedUpdate()
        {
            bool liquid = !form.IsSolid && skin.IsReady;
            IReadOnlyList<Collectible> waiting = Collectible.Waiting;
            for (int i = 0; i < waiting.Count; i++)
                waiting[i].SetEngulfed(liquid && IsInside(waiting[i]));
        }

        void OnFormChanged(bool solid)
        {
            if (!solid)
            {
                foreach (Item item in carried) item.Collectible.Drop();
                carried.Clear();
                return;
            }

            // Collecting takes an item off the waiting list, so walk it backwards.
            for (int i = Collectible.Waiting.Count - 1; i >= 0; i--)
            {
                Collectible collectible = Collectible.Waiting[i];
                if (IsInside(collectible)) Catch(collectible);
            }
            // The crystal settles onto its lowest point, a little lower than the jelly hung: lift items resting on the
            // floor with it, so they don't poke out of its underside.
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

        /// <summary>Takes over everything another blob holds (two halves flowing back together), from where it is now.</summary>
        public void TakeOver(BlobCargo other)
        {
            foreach (Item item in other.carried)
            {
                item.Local = transform.InverseTransformPoint(item.Transform.position);
                item.LocalRotation = Quaternion.Inverse(transform.rotation) * item.Transform.rotation;
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
            foreach (Item item in carried)
                item.Transform.SetPositionAndRotation(transform.TransformPoint(item.Local), transform.rotation * item.LocalRotation);
        }
    }
}
