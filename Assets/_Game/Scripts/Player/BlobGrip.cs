using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Lets the blob hold on to a conveyor's spike. Every physics step in liquid form it tells the spikes nearby whether
    /// they're inside the jelly (their tips glow); freezing with one inside sets the crystal around it, and from then on
    /// the crystal rides the belt, turning with the spike round the rollers, until it melts and drops off with the
    /// belt's speed. While it holds on, the motor lets go (as for a magnet's pull) and the crystal ignores the
    /// conveyor's own colliders, so it doesn't fight the spike it's set around. Runs after magnets, before the motor.
    /// </summary>
    [DefaultExecutionOrder(-5)]
    [RequireComponent(typeof(BlobForm), typeof(BlobSkin), typeof(BlobMotor))]
    public class BlobGrip : MonoBehaviour
    {
        [Tooltip("Spikes whose base is closer than this to the core are checked against the jelly, m.")]
        [SerializeField] float checkRadius = 1.5f;
        [Tooltip("Fastest the hold moves the crystal, m/s: catches it up after something knocked it off its place, " +
                 "without flinging it.")]
        [SerializeField] float maxFollowSpeed = 10f;

        // Shares of the rod, from its base, that have to be inside the jelly to catch it: most of it, tip included.
        static readonly float[] Samples = { 0.4f, 0.9f };

        BlobForm form;
        BlobSkin skin;
        BlobMotor motor;
        Rigidbody body;
        // The engulfed spike nearest the core at the last physics step: the one freezing grabs.
        ConveyorSpike candidate;
        // The body's pose in the held spike's space.
        Vector3 heldPosition;
        Quaternion heldRotation;
        // Colliders the crystal was told to ignore; cleared on the next freeze.
        readonly List<Collider> ignored = new List<Collider>();

        /// <summary>The spike the crystal is set around, if any.</summary>
        public ConveyorSpike Held { get; private set; }
        /// <summary>The spike freezing now would grab: the engulfed one nearest the core, if any.</summary>
        public ConveyorSpike Engulfed => candidate;

        void Awake()
        {
            form = GetComponent<BlobForm>();
            skin = GetComponent<BlobSkin>();
            motor = GetComponent<BlobMotor>();
            body = GetComponent<Rigidbody>();
        }

        void OnEnable()
        {
            form.Hardening += OnHardening;
            form.FormChanged += OnFormChanged;
        }

        void OnDisable()
        {
            form.Hardening -= OnHardening;
            form.FormChanged -= OnFormChanged;
            Release();
            candidate = null;
        }

        void FixedUpdate()
        {
            if (Held != null)
            {
                Follow();
                return;
            }

            candidate = null;
            if (form.IsSolid || !skin.IsReady) return;
            float best = checkRadius * checkRadius;
            foreach (Conveyor conveyor in Conveyor.All)
            foreach (ConveyorSpike spike in conveyor.Spikes)
            {
                float distance = (spike.PointAlong(0f) - body.position).sqrMagnitude;
                if (distance > checkRadius * checkRadius || !IsInside(spike)) continue;
                spike.ReportEngulfed();
                if (distance > best) continue;
                best = distance;
                candidate = spike;
            }
        }

        bool IsInside(ConveyorSpike spike)
        {
            foreach (float share in Samples)
                if (!skin.Contains(spike.PointAlong(share))) return false;
            return true;
        }

        /// <summary>
        /// The crystal has its shape but hasn't been checked for overlaps yet: set it around the spike instead of being
        /// pushed off it.
        /// </summary>
        void OnHardening()
        {
            foreach (Collider other in ignored)
                if (other != null) Physics.IgnoreCollision(form.SolidCollider, other, false);
            ignored.Clear();
            if (candidate == null) return;
            foreach (Collider other in candidate.Conveyor.Colliders)
            {
                if (other == null) continue;
                Physics.IgnoreCollision(form.SolidCollider, other);
                ignored.Add(other);
            }
        }

        void OnFormChanged(bool solid)
        {
            if (!solid)
            {
                Release();
                return;
            }
            if (candidate == null) return;

            Held = candidate;
            candidate = null;
            Held.SetHeld(true);
            Quaternion inverse = Quaternion.Inverse(Held.Body.rotation);
            heldPosition = inverse * (body.position - Held.Body.position);
            heldRotation = inverse * body.rotation;
            motor.IsDriven = true;
        }

        /// <summary>
        /// Sets the velocities that put the crystal exactly where the spike will hold it at the end of this physics
        /// step, gravity cancelled. Unlike a kinematic body it still has a real speed, so the ship riding on top is
        /// carried along and the melted liquid keeps the belt's speed.
        /// </summary>
        void Follow()
        {
            if (!Held.isActiveAndEnabled || !form.IsSolid)
            {
                Release();
                return;
            }

            float dt = Time.fixedDeltaTime;
            Quaternion rotation = Held.TargetRotation * heldRotation;
            Vector3 position = Held.TargetPosition + Held.TargetRotation * heldPosition;
            // Rigidbody velocities are those of the centre of mass.
            Vector3 centerOfMass = position + rotation * body.centerOfMass;
            body.linearVelocity = Vector3.ClampMagnitude((centerOfMass - body.worldCenterOfMass) / dt, maxFollowSpeed);
            if (body.useGravity) body.AddForce(-Physics.gravity, ForceMode.Acceleration);

            (rotation * Quaternion.Inverse(body.rotation)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            body.angularVelocity = Mathf.Abs(angle) > 0.01f ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
            motor.IsDriven = true;
        }

        void Release()
        {
            if (Held == null) return;
            Held.SetHeld(false);
            Held = null;
            if (motor != null) motor.IsDriven = false;
        }
    }
}
