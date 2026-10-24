using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Splashes the liquid blob's water up around the ship's hull. Coming down hard into the jelly (falling onto it,
    /// dropping in as the blob flattens to set off, or the blob landing under it) throws a crown of droplets out from all
    /// round the hull, more and faster the harder the hit; ploughing through it (the blob running into the ship, or
    /// stopping dead against a wall while the ship carries on) sprays water off the leading side. Both stir up the
    /// jelly's surface like the blob's own splashes. The droplets go into the blob's droplet particles (see <see cref="BlobSplash"/>), so they look the same
    /// and leave the same rings where they land. Works from what <see cref="ShipBuoyancy"/> found at its float points:
    /// decided after the physics step and thrown in LateUpdate, from where the ship is drawn.
    /// </summary>
    [RequireComponent(typeof(ShipBuoyancy))]
    public class ShipSplash : MonoBehaviour
    {
        enum Kind { None, Impact, Plough }

        struct Burst
        {
            public Kind Kind;
            public int Count;
            // Impact: out of the jelly's surface. Plough: the way the hull moves through the jelly, along the surface.
            public Vector3 Direction;
            // The jelly's velocity where the hull is in it, m/s.
            public Vector3 Carried;
            // Impact: speed into the surface. Plough: speed through the jelly. m/s.
            public float Speed;
            // 0..1, how hard the hit was; harder hits throw slightly bigger droplets.
            public float Hardness;
            // The jelly's surface round the hull, m (last physics step).
            public float Height;
        }

        [Header("Impacts")]
        [Tooltip("Coming down into the jelly faster than this throws a crown of droplets round the hull, m/s. Bobbing " +
                 "reaches about 0.9; the drop onto the blob as it flattens to set off about 4.7.")]
        [SerializeField] float impactMinSpeed = 1.5f;
        [Tooltip("Crown droplets at the slowest impact that splashes.")]
        [SerializeField] int impactDroplets = 4;
        [Tooltip("Extra crown droplets per m/s of impact above the minimum.")]
        [SerializeField] float impactPerSpeed = 2.5f;
        [Tooltip("Upward speed of crown droplets, per m/s of impact.")]
        [SerializeField] float impactRebound = 0.5f;
        [Tooltip("Speed crown droplets spray out from the hull with, per m/s of impact.")]
        [SerializeField] float impactSpread = 0.45f;

        [Header("Ploughing")]
        [Tooltip("Moving through the jelly faster than this sprays water off the leading side, m/s. Setting off, running " +
                 "and braking stay under about 1; the blob running into the ship reaches 10.")]
        [SerializeField] float ploughMinSpeed = 1.2f;
        [Tooltip("Droplets per metre the hull ploughs through the jelly faster than the minimum.")]
        [SerializeField] float dropletsPerMetre = 40f;
        [Tooltip("Share of the speed through the jelly the spray is pushed ahead with.")]
        [Range(0f, 1f)]
        [SerializeField] float ploughPush = 0.6f;
        [Tooltip("Upward and sideways speed of the spray, per m/s through the jelly.")]
        [SerializeField] float ploughLift = 0.8f;

        [Header("Throw")]
        [Tooltip("Share of the jelly's own velocity thrown droplets keep.")]
        [Range(0f, 1f)]
        [SerializeField] float inheritVelocity = 0.8f;
        [Tooltip("Random speed added to every droplet, m/s.")]
        [SerializeField] float jitter = 0.3f;

        [Header("Droplets")]
        [Tooltip("Most droplets one splash throws. 0 turns the splash off.")]
        [SerializeField] int maxBurst = 16;
        [Tooltip("Smallest (x) and largest (y) droplet diameter, m. Most are small.")]
        [SerializeField] Vector2 dropletSize = new Vector2(0.05f, 0.11f);
        [Tooltip("How long a droplet that never hits anything (one thrown off the level) lives, s.")]
        [SerializeField] float dropletLifetime = 1.5f;
        [Tooltip("Droplets start this far outside the hull, m, so they aren't hidden behind it.")]
        [SerializeField] float hullMargin = 0.03f;
        [Tooltip("Droplets start this far above the jelly's surface, m, so they aren't hidden inside it.")]
        [SerializeField] float surfaceOffset = 0.02f;

        // Droplets owed by ploughing fade over about this long, s, so a gentle nudge that never adds up to a whole
        // droplet stays dry.
        const float ChargeMemory = 0.2f;
        // An impact this fast (a fall of about 0.8 m) counts as the hardest, m/s.
        const float HardImpact = 4f;
        // The same for ploughing (stopping dead from top speed), m/s.
        const float HardPlough = 3f;
        // Once it has splashed, an impact has to ease off to this share of the minimum before the next one counts, so
        // a hit that takes a few physics steps to stop splashes once.
        const float RearmShare = 0.5f;
        // How far round the hull from the leading side the spray comes off, either way, degrees.
        const float PloughArc = 70f;
        // Splashes waiting for LateUpdate; a slow frame runs several physics steps.
        const int MaxPending = 4;

        ShipBuoyancy buoyancy;
        Rigidbody body;
        // The hull's outline from above, in the ship's local space.
        Bounds hull;
        BlobSkin splashedSkin;
        BlobSplash splashed;
        Coroutine tracking;
        ParticleSystem.EmitParams emit;
        readonly Burst[] pending = new Burst[MaxPending];
        int pendingCount;

        bool impactArmed;
        float charge;
        Kind lastKind;
        int lastCount;
        float lastSpeed;

        /// <summary>Droplets thrown since the ship started (for testing).</summary>
        public int Thrown { get; private set; }
        /// <summary>What threw the last droplets, how many and how fast, e.g. "Impact 12 3.4" (for testing).</summary>
        public string LastSplash => $"{lastKind} {lastCount} {lastSpeed:0.0}";

        /// <summary>Throws a crown round the hull right now, to check the look standing still (for testing).</summary>
        public void TestSplash()
        {
            if (Splashed() == null) return;
            Queue(Kind.Impact, 12, Vector3.up, Vector3.zero, 3f, 0.7f, body.position.y + hull.min.y);
        }

        void Awake()
        {
            buoyancy = GetComponent<ShipBuoyancy>();
            body = GetComponent<Rigidbody>();
            MeasureHull();
        }

        void OnEnable()
        {
            // The ship starts in the jelly, which is still settling round it: no impact until that has calmed down.
            impactArmed = false;
            tracking = StartCoroutine(TrackAfterPhysics());
        }

        void OnDisable()
        {
            if (tracking != null) StopCoroutine(tracking);
            pendingCount = 0;
        }

        /// <summary>
        /// The hull's outline from above: the bounds of the ship's mesh colliders (the modelled hull and cabin), or of
        /// all its colliders without any.
        /// </summary>
        void MeasureHull()
        {
            bool found = false;
            foreach (MeshCollider shape in GetComponentsInChildren<MeshCollider>())
            {
                if (shape.sharedMesh == null) continue;
                Encapsulate(shape.sharedMesh.bounds, shape.transform, ref found);
            }
            if (found) return;
            // World bounds, so a little wider than the colliders when the ship starts turned.
            foreach (Collider shape in GetComponentsInChildren<Collider>()) Encapsulate(shape.bounds, null, ref found);
        }

        /// <summary>Grows <see cref="hull"/> by the corners of bounds in <paramref name="space"/> (world when null).</summary>
        void Encapsulate(Bounds bounds, Transform space, ref bool found)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                if (space != null) point = space.TransformPoint(point);
                point = transform.InverseTransformPoint(point);
                if (found) hull.Encapsulate(point);
                else hull = new Bounds(point, Vector3.zero);
                found = true;
            }
        }

        /// <summary>The splash of the blob the ship is on, kept up to date as it moves from piece to piece.</summary>
        BlobSplash Splashed()
        {
            BlobSkin skin = buoyancy.Blob;
            if (skin != splashedSkin)
            {
                splashedSkin = skin;
                splashed = skin != null ? skin.GetComponent<BlobSplash>() : null;
            }
            // A half that flowed back into the other is gone, and its droplets with it.
            return splashed != null && splashed.Droplets != null ? splashed : null;
        }

        // After the physics step, like the blob's splash: the float points then hold what this step's buoyancy saw.
        IEnumerator TrackAfterPhysics()
        {
            var afterPhysics = new WaitForFixedUpdate();
            while (true)
            {
                yield return afterPhysics;
                Track(Time.fixedDeltaTime);
            }
        }

        void Track(float dt)
        {
            charge *= Mathf.Exp(-dt / ChargeMemory);
            // Not placed yet, so not out of the jelly either.
            if (buoyancy.Blob == null || !buoyancy.Blob.IsReady) return;

            // What the hull is in the jelly with: only points below the surface; those clinging above it aren't in it.
            IReadOnlyList<ShipBuoyancy.Contact> contacts = buoyancy.Contacts;
            int submerged = 0;
            float into = 0f, height = 0f;
            Vector3 normal = Vector3.zero, carried = Vector3.zero, through = Vector3.zero;
            for (int i = 0; i < contacts.Count; i++)
            {
                ShipBuoyancy.Contact contact = contacts[i];
                if (!contact.Touching || contact.Depth <= 0f) continue;
                submerged++;
                Vector3 up = contact.Surface.Normal;
                // Only as fast as the hull itself comes down: jelly rising under it (balling up again after a stop)
                // lifts the ship rather than hitting it.
                Vector3 hull = contact.Relative + contact.Surface.Velocity;
                into = Mathf.Max(into, Mathf.Min(-Vector3.Dot(contact.Relative, up), -Vector3.Dot(hull, up)));
                through += Vector3.ProjectOnPlane(contact.Relative, up);
                normal += up;
                carried += contact.Surface.Velocity;
                height += contact.Surface.Height;
            }

            if (submerged == 0 || Splashed() == null)
            {
                // Out of the jelly: the next time in is a fresh hit.
                impactArmed = true;
                charge = 0f;
                return;
            }
            normal.Normalize();
            carried /= submerged;
            through /= submerged;
            height /= submerged;

            if (into > 0.3f || through.magnitude > 0.2f)
                Debug.Log($"[ShipSplashDebug] t={Time.time:0.00} into={into:0.00} through={through.magnitude:0.00} submerged={submerged} armed={impactArmed} charge={charge:0.00}");
            if (into < impactMinSpeed * RearmShare) impactArmed = true;
            if (impactArmed && into >= impactMinSpeed)
            {
                impactArmed = false;
                int count = impactDroplets + Mathf.RoundToInt((into - impactMinSpeed) * impactPerSpeed);
                Queue(Kind.Impact, count, normal, carried, into, Mathf.Clamp01(into / HardImpact), height);
            }

            float speed = through.magnitude;
            if (speed <= ploughMinSpeed) return;
            charge += (speed - ploughMinSpeed) * dt * dropletsPerMetre;
            int sprayed = Mathf.Min((int)charge, maxBurst);
            if (sprayed <= 0) return;
            // A hit too hard for one splash is capped, not saved up for the steps after.
            charge = sprayed == maxBurst ? 0f : charge - sprayed;
            Queue(Kind.Plough, sprayed, through / speed, carried, speed, Mathf.Clamp01(speed / HardPlough), height);
        }

        void Queue(Kind kind, int count, Vector3 direction, Vector3 carried, float speed, float hardness, float height)
        {
            count = Mathf.Min(count, maxBurst);
            if (count <= 0 || pendingCount == pending.Length) return;
            pending[pendingCount++] = new Burst
            {
                Kind = kind, Count = count, Direction = direction, Carried = carried, Speed = speed, Hardness = hardness,
                Height = height,
            };
            lastKind = kind;
            lastCount = count;
            lastSpeed = speed;
            splashed.Stir(hardness);
        }

        void LateUpdate()
        {
            if (pendingCount == 0) return;
            BlobSplash target = Splashed();
            if (target != null)
            {
                // The ship is drawn interpolated between physics steps: throw the water from where it's seen.
                Vector3 shift = transform.position - body.position;
                for (int i = 0; i < pendingCount; i++) Throw(pending[i], target.Droplets, shift.y);
            }
            pendingCount = 0;
        }

        void Throw(Burst burst, ParticleSystem droplets, float shift)
        {
            float radiusX = hull.extents.x + hullMargin, radiusZ = hull.extents.z + hullMargin;
            // Ploughing: round the side the hull moves toward. The angle is around the outline, whose outward direction
            // at angle a is (cos a / radiusX, sin a / radiusZ).
            Vector3 lead = transform.InverseTransformDirection(burst.Direction);
            float leadAngle = Mathf.Atan2(lead.z * radiusZ, lead.x * radiusX);

            for (int k = 0; k < burst.Count; k++)
            {
                // Mostly small droplets with the odd big one; harder hits throw bigger ones.
                float size = Mathf.Lerp(dropletSize.x, dropletSize.y, Random.value * Random.value) * Mathf.Lerp(0.85f, 1.15f, burst.Hardness);
                float angle = burst.Kind == Kind.Plough
                    ? leadAngle + Random.Range(-PloughArc, PloughArc) * Mathf.Deg2Rad
                    : Random.Range(0f, 2f * Mathf.PI);
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);

                Vector3 rim = transform.TransformPoint(hull.center + new Vector3(cos * radiusX, 0f, sin * radiusZ));
                rim.y = burst.Height + shift + surfaceOffset + size * 0.5f;
                Vector3 outward = transform.TransformDirection(new Vector3(cos / radiusX, 0f, sin / radiusZ));
                outward.y = 0f;
                outward.Normalize();

                Vector3 velocity = burst.Carried * inheritVelocity;
                if (burst.Kind == Kind.Plough)
                {
                    // Pushed ahead of the hull and peeling off to the sides, like a bow wave.
                    velocity += burst.Direction * (burst.Speed * ploughPush * Random.Range(0.5f, 1f))
                                + outward * (burst.Speed * ploughLift * Random.Range(0.3f, 1f))
                                + Vector3.up * (burst.Speed * ploughLift * Random.Range(0.6f, 1.2f));
                }
                else
                {
                    // The hull shoves the water out from under it: up out of the surface and away from the hull.
                    velocity += burst.Direction * (burst.Speed * impactRebound * Random.Range(0.6f, 1.1f))
                                + outward * (burst.Speed * impactSpread * Random.Range(0.5f, 1f));
                }

                emit.position = rim;
                emit.velocity = velocity + Random.insideUnitSphere * jitter;
                emit.startSize = size;
                emit.startLifetime = dropletLifetime * Random.Range(0.8f, 1f);
                droplets.Emit(emit, 1);
                Thrown++;
            }
        }
    }
}
