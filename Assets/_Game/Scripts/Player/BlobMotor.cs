using System;
using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Moves the blob's core rigidbody. Input is camera-relative on the ground plane, and the body is
    /// steered toward a target velocity (accelerate while there is input, brake when there isn't),
    /// so it reacts quickly but still carries momentum. The liquid can jump, the solid swims when it floats in water
    /// (<see cref="BlobWater"/> decides when). The core never rotates; the skin does the visuals.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class BlobMotor : MonoBehaviour
    {
        [Serializable]
        public struct MoveSettings
        {
            [Tooltip("Top speed on flat ground, m/s.")]
            public float maxSpeed;
            [Tooltip("How quickly it reaches top speed while there is input, m/s².")]
            public float acceleration;
            [Tooltip("How quickly it stops once input is released, m/s².")]
            public float braking;
        }

        [SerializeField] PlayerInputReader input;
        [Tooltip("Movement is relative to this camera's facing. Defaults to the main camera.")]
        [SerializeField] Transform cameraTransform;
        [SerializeField] MoveSettings liquid = new MoveSettings { maxSpeed = 5f, acceleration = 12f, braking = 25f };
        [Tooltip("Solid form: pushes itself along the input direction (its frozen shape decides whether it rolls or " +
                 "slides) and brakes, spin included, when there's no input.")]
        [SerializeField] MoveSettings solid = new MoveSettings { maxSpeed = 1.8f, acceleration = 30f, braking = 6f };
        [Tooltip("Solid form floating in water: paddles along the input direction, no footing needed, and drifts to a " +
                 "stop when there's no input.")]
        [SerializeField] MoveSettings swim = new MoveSettings { maxSpeed = 2.2f, acceleration = 5f, braking = 1.5f };
        [Tooltip("Fraction of acceleration and braking available while airborne.")]
        [Range(0f, 1f)]
        [SerializeField] float airControl = 0.3f;
        [Tooltip("Share of gravity pulling the blob down while it falls through the air, so it drops slower than it " +
                 "rises. Not while something carries it or it swims.")]
        [Range(0f, 1f)]
        [SerializeField] float fallGravity = 0.6f;

        [Header("Jump")]
        [Tooltip("How high a jump from flat ground lifts the blob, m. Liquid form only.")]
        [SerializeField] float jumpHeight = 0.8f;
        [Tooltip("A jump pressed this long before landing still happens when it lands, s.")]
        [SerializeField] float jumpBuffer = 0.15f;
        [Tooltip("A jump is still allowed this long after flowing off an edge, s.")]
        [SerializeField] float coyoteTime = 0.1f;

        [Header("Ground")]
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("How far below the core the ground is still detected, m.")]
        [SerializeField] float groundProbeDistance = 0.2f;
        [SerializeField] float maxGroundAngle = 50f;
        [Tooltip("Extra pull into the ground while grounded, keeps the blob hugging ramps and crests, m/s².")]
        [SerializeField] float groundStick = 15f;
        [Tooltip("Right after leaving the ground, ground within this distance below pulls the blob back down " +
                 "instead of letting it launch off bumps and crests, m. Larger drops still fall.")]
        [SerializeField] float snapDistance = 0.6f;
        [Tooltip("Above this speed the blob may leave the ground over crests, m/s.")]
        [SerializeField] float snapMaxSpeed = 9f;

        [Header("Steps")]
        [Tooltip("Edges up to this height above the bottom of the core are flowed up, m. Only edges with a top to " +
                 "flow onto within this height count; a taller wall doesn't, even where it touches the core this low.")]
        [SerializeField] float maxStepHeight = 0.25f;
        [Tooltip("Upward speed while flowing up a step, m/s.")]
        [SerializeField] float stepClimbSpeed = 2.5f;

        [Header("Shore")]
        [Tooltip("Swimming into the edge of the water climbs out onto a ledge whose top is at most this far above the " +
                 "crystal's centre, m. Higher walls can't be climbed.")]
        [SerializeField] float shoreClimbHeight = 0.6f;
        [Tooltip("Upward speed while climbing out of the water, m/s.")]
        [SerializeField] float shoreClimbSpeed = 1.5f;

        [Header("Bridging")]
        [Tooltip("The liquid flows across a gap its jelly is long enough to span: the core is held up while ground lies " +
                 "under the jelly behind it and the far side is within the jelly's length ahead, or under its front.")]
        [SerializeField] bool bridgeGaps = true;
        [Tooltip("Each end has to lie this far over ground to hold on, m.")]
        [SerializeField] float bridgeGrip = 0.15f;
        [Tooltip("Ground this far below the bottom of the core still holds the jelly up (a far side a little lower), m.")]
        [SerializeField] float bridgeDrop = 0.3f;
        [Tooltip("Spacing of the looks down for ground along the jelly, m.")]
        [SerializeField] float bridgeSampleSpacing = 0.2f;

        // Below this gap the core counts as touching the ground rather than skimming over it.
        const float ContactGap = 0.02f;
        // A step's top is looked for this far past where the core touches its edge, m.
        const float StepTopInset = 0.05f;
        // Physics steps after a step climb during which hugging and snapping stay off, so they don't undo the lift.
        const int ClimbGraceSteps = 4;
        // Physics steps after a jump during which the ground is ignored: the probe still reaches it for a moment, and
        // hugging, snapping or sticking to it would pull the blob straight back down.
        const int JumpGraceSteps = 6;
        // A climb out of the water gives up after this many physics steps, in case something holds the crystal back.
        const int MaxClimbSteps = 75;
        // Climbing out lifts the crystal's underside this far above the edge before moving it over, m.
        const float ShoreClearance = 0.05f;
        // A bridging core glides with its bottom this far above the line between the two sides, so it slides onto the
        // far side instead of catching its edge, m.
        const float BridgeClearance = 0.03f;

        Rigidbody body;
        SphereCollider core;
        BlobSkin skin;
        // Height the bottom of a bridging core is held at.
        float bridgeHeight;
        int stepsSinceGrounded;
        int stepsSinceClimb = ClimbGraceSteps;
        int stepsSinceJump = JumpGraceSteps;
        bool climbingOut;
        float shoreTop;
        Vector3 climbDirection;
        int climbSteps;
        // True from a jump until the blob is back on the ground, so coyote time can't give a second jump.
        bool hasJumped;
        float jumpRequestTime = float.NegativeInfinity;
        bool stepContact;
        Vector3 stepNormal;
        float groundGap;
        bool isSolid;
        bool solidGroundContact;
        bool solidCrateContact;
        Vector3 solidGroundNormal = Vector3.up;
        bool shoreContact;
        Vector3 shoreNormal;
        Vector3 shorePoint;
        IReadOnlyList<Collider> solidShapes;
        int pushableLayer;
        // The ground mask without grates: the liquid flows through them, so it never stands on one.
        LayerMask liquidGroundMask;

        public Rigidbody Body => body;
        public bool IsGrounded { get; private set; }
        /// <summary>
        /// True while the stretched liquid spans a gap: nothing is under the core, but the jelly holds on to both sides
        /// (or to the far side with its front). Counts as grounded.
        /// </summary>
        public bool IsBridging { get; private set; }
        /// <summary>True while the solid floats in water and swims instead of pushing itself along the ground.</summary>
        public bool IsSwimming { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        public Vector3 Velocity => body.linearVelocity;
        public MoveSettings ActiveSettings => isSolid ? (IsSwimming ? swim : solid) : liquid;
        /// <summary>
        /// While true, something else steers the body (a magnet carrying the blob): the motor doesn't move, brake
        /// or jump, and the blob counts as airborne.
        /// </summary>
        public bool IsDriven { get; set; }
        /// <summary>
        /// While true, the solid ignores the move input (a freeze zone holds it): it brakes on the ground and drifts to a
        /// stop in water, but gravity, conveyors and pushes still move it.
        /// </summary>
        public bool IgnoresInput { get; set; }
        /// <summary>Raised on the physics step the blob takes off, with the upward speed the jump added, m/s.</summary>
        public event Action<float> Jumped;

        /// <summary>Jumps on the next physics step it can: now if on the ground, or on landing within the jump buffer.</summary>
        public void RequestJump() => jumpRequestTime = Time.time;

        /// <summary>
        /// Switches between liquid movement (core sphere) and solid movement (whatever shape is frozen;
        /// <paramref name="shapes"/> are its colliders).
        /// </summary>
        public void SetSolid(bool value, IReadOnlyList<Collider> shapes = null)
        {
            isSolid = value;
            solidShapes = shapes;
            IsSwimming = false;
            climbingOut = false;
            solidGroundContact = false;
            stepContact = false;
            shoreContact = false;
        }

        /// <summary>
        /// Scales the core and the heights it measures against it (a blob cut in half, or two halves flowed back
        /// together), so a smaller piece flows up only proportionally lower steps.
        /// </summary>
        public void Resize(float factor)
        {
            core.radius *= factor;
            maxStepHeight *= factor;
            shoreClimbHeight *= factor;
        }

        /// <summary>Solid form only: true while floating in water.</summary>
        public void SetSwimming(bool value) => IsSwimming = value && isSolid;
        /// <summary>Speed along the ground relative to the current top speed; 0 at rest, about 1 at full speed.</summary>
        public float NormalizedSpeed => Vector3.ProjectOnPlane(body.linearVelocity, GroundNormal).magnitude / Mathf.Max(0.01f, ActiveSettings.maxSpeed);
        /// <summary>World radius of the core collider.</summary>
        public float CoreRadius => core.radius * MaxAbsComponent(transform.lossyScale);

        void Reset()
        {
            input = GetComponent<PlayerInputReader>();
            groundMask = ~LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Triggers", "Ignore Raycast");
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            core = GetComponent<SphereCollider>();
            skin = GetComponent<BlobSkin>();
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            pushableLayer = LayerMask.NameToLayer("Pushable");
            liquidGroundMask = groundMask & ~LayerMask.GetMask("LiquidPassable");
        }

        void OnEnable()
        {
            if (input != null) input.JumpPressed += RequestJump;
        }

        void OnDisable()
        {
            if (input != null) input.JumpPressed -= RequestJump;
        }

        void FixedUpdate()
        {
            IsBridging = false;
            if (IsDriven)
            {
                // No coyote-time jump once let go in mid-air either: that waits for the next landing.
                ClearGround();
                stepsSinceGrounded++;
                hasJumped = true;
                jumpRequestTime = float.NegativeInfinity;
                stepContact = false;
                solidGroundContact = false;
                solidCrateContact = false;
                return;
            }

            if (isSolid)
            {
                MoveSolid();
                SoftenFall();
                return;
            }

            ProbeGround();
            stepsSinceJump++;
            if (stepsSinceJump < JumpGraceSteps) ClearGround();
            else
            {
                if (!IsGrounded && bridgeGaps) ProbeBridge();
                if (IsGrounded) hasJumped = false;
            }
            stepsSinceGrounded = IsGrounded ? 0 : stepsSinceGrounded + 1;
            stepsSinceClimb++;

            if (Time.time - jumpRequestTime <= jumpBuffer && !hasJumped && stepsSinceGrounded * Time.fixedDeltaTime <= coyoteTime)
                Jump();

            bool climbing = stepsSinceClimb < ClimbGraceSteps;
            bool takingOff = stepsSinceJump < JumpGraceSteps;
            if (!climbing && !takingOff)
            {
                if (!IsGrounded && stepsSinceGrounded <= 1) SnapToGround();
                else if (IsGrounded && groundGap > ContactGap) HugGround();
            }

            Vector3 up = IsGrounded ? GroundNormal : Vector3.up;
            Vector3 wish = WishDirection();
            if (IsGrounded && wish.sqrMagnitude > 0f)
                wish = Vector3.ProjectOnPlane(wish, up).normalized * wish.magnitude;

            MoveSettings settings = ActiveSettings;
            float rate = wish.sqrMagnitude > 0.0001f ? settings.acceleration : settings.braking;
            if (!IsGrounded) rate *= airControl;

            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, up);
            Vector3 steered = Vector3.MoveTowards(planar, wish * settings.maxSpeed, rate * Time.fixedDeltaTime);
            body.AddForce(steered - planar, ForceMode.VelocityChange);

            if (IsBridging) HoldBridge();
            else if (IsGrounded) body.AddForce(-GroundNormal * groundStick, ForceMode.Acceleration);
            else SoftenFall();

            // Pressing into a low edge: flow up it instead of relying on a bounce off the corner.
            if (stepContact && wish.sqrMagnitude > 0.01f && Vector3.Dot(wish.normalized, -stepNormal) > 0.3f)
            {
                Vector3 velocity = body.linearVelocity;
                if (velocity.y < stepClimbSpeed) body.linearVelocity = new Vector3(velocity.x, stepClimbSpeed, velocity.z);
                stepsSinceClimb = 0;
            }
            stepContact = false;
        }

        /// <summary>Straight up, keeping the speed along the ground; a blob already rising faster (a ramp, a lift) keeps that.</summary>
        void Jump()
        {
            Vector3 velocity = body.linearVelocity;
            float before = velocity.y;
            velocity.y = Mathf.Max(velocity.y, Mathf.Sqrt(2f * -Physics.gravity.y * jumpHeight));
            body.linearVelocity = velocity;

            jumpRequestTime = float.NegativeInfinity;
            hasJumped = true;
            stepsSinceJump = 0;
            ClearGround();
            Jumped?.Invoke(velocity.y - before);
        }

        /// <summary>Takes part of gravity's pull off the blob while it drops through the air.</summary>
        void SoftenFall()
        {
            if (IsGrounded || IsSwimming || !body.useGravity || body.linearVelocity.y >= 0f) return;
            body.AddForce(-Physics.gravity * (1f - fallGravity), ForceMode.Acceleration);
        }

        void ClearGround()
        {
            IsGrounded = false;
            GroundNormal = Vector3.up;
            groundGap = float.PositiveInfinity;
        }

        /// <summary>
        /// Solid form: a heavy push along the input direction (up or down the slope it rests on) at the centre of mass,
        /// only while resting on something and below top speed; the frozen shape rolls, tips or slides on its own while
        /// pushed. Without input it brakes, so it doesn't roll or slide on (and off edges) by itself. No liquid ground
        /// helpers.
        /// </summary>
        void MoveSolid()
        {
            IsGrounded = solidGroundContact;
            GroundNormal = solidGroundContact ? solidGroundNormal : Vector3.up;
            bool pressingCrate = solidCrateContact;
            bool pressingShore = shoreContact;
            solidGroundContact = false;
            solidCrateContact = false;
            shoreContact = false;

            Vector3 wish = IgnoresInput ? Vector3.zero : WishDirection();
            // Still wet but resting on something (a ramp out of the water, a shallow bottom): push along it as on land.
            // Paddling only moves level with the water and can't climb a slope.
            if (climbingOut || (IsSwimming && !IsGrounded))
            {
                Swim(wish, pressingShore);
                return;
            }
            if (wish.sqrMagnitude < 0.0001f)
            {
                if (IsGrounded) BrakeSolid();
                return;
            }
            // A resting solid falls asleep and stops reporting contacts; wake it so the next step sees the ground.
            if (body.IsSleeping()) body.WakeUp();
            // Leaning on a crate counts as footing too: a round crystal shoving a crate can lift off the floor a little.
            if (!IsGrounded && !pressingCrate) return;

            // Along the ground, not level: a level push into a ramp partly presses the crystal into it (more friction)
            // instead of up it.
            Vector3 up = IsGrounded ? GroundNormal : Vector3.up;
            Vector3 push = Vector3.ProjectOnPlane(wish, up).normalized * wish.magnitude;
            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, up);
            float speedAlongWish = Vector3.Dot(planar, push.normalized);
            if (speedAlongWish < solid.maxSpeed * wish.magnitude)
                body.AddForce(push * solid.acceleration, ForceMode.Acceleration);
        }

        /// <summary>
        /// Floating: steered like the liquid, along the water's surface and without needing footing, and drifting to a
        /// stop without input. Swimming into a low enough edge starts a climb out onto it.
        /// </summary>
        void Swim(Vector3 wish, bool pressingShore)
        {
            if (climbingOut)
            {
                ClimbOut(wish);
                return;
            }

            float rate = wish.sqrMagnitude > 0.0001f ? swim.acceleration : swim.braking;
            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 steered = Vector3.MoveTowards(planar, wish * swim.maxSpeed, rate * Time.fixedDeltaTime);
            body.AddForce(steered - planar, ForceMode.VelocityChange);

            if (!pressingShore || wish.sqrMagnitude < 0.01f || Vector3.Dot(wish.normalized, -shoreNormal) < 0.3f) return;
            // The edge's top, looked for just past the contact. From inside a wall taller than the reach the ray
            // starts inside it and finds nothing, so only low enough edges are climbed.
            float reach = body.worldCenterOfMass.y + shoreClimbHeight;
            Vector3 probe = shorePoint - shoreNormal * 0.1f;
            probe.y = reach;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit top, reach - shorePoint.y, groundMask, QueryTriggerInteraction.Ignore)
                || top.normal.y < 0.7f)
                return;

            climbingOut = true;
            shoreTop = top.point.y;
            climbDirection = -shoreNormal;
            climbSteps = 0;
        }

        /// <summary>
        /// Climbing out of the water onto the edge it swam into: rise until the underside clears the edge, move over it
        /// until the centre is above the ledge, then drop on. Letting go of the input drops it back into the water.
        /// </summary>
        void ClimbOut(Vector3 wish)
        {
            Vector3 center = body.worldCenterOfMass;
            float bottom = SolidBottom(center.y - CoreRadius);
            bool overLedge = Physics.Raycast(center, Vector3.down, out RaycastHit below, center.y - bottom + 0.1f, groundMask,
                                             QueryTriggerInteraction.Ignore)
                             && below.normal.y > 0.7f && below.point.y > shoreTop - 0.05f;
            if (wish.sqrMagnitude < 0.01f || overLedge || ++climbSteps > MaxClimbSteps)
            {
                climbingOut = false;
                return;
            }

            // Rising: lean on the edge so it doesn't drift off. Clear of it: hold the height and move over. No spin:
            // rubbing up the edge would otherwise roll the crystal over backwards.
            bool rising = bottom < shoreTop + ShoreClearance;
            Vector3 velocity = climbDirection * (rising ? 0.3f : shoreClimbSpeed);
            velocity.y = rising ? Mathf.Max(body.linearVelocity.y, shoreClimbSpeed) : Mathf.Max(body.linearVelocity.y, 0f);
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>Lowest point of the frozen shape's colliders, or <paramref name="fallback"/> without any.</summary>
        float SolidBottom(float fallback)
        {
            if (solidShapes == null || solidShapes.Count == 0) return fallback;
            float bottom = float.PositiveInfinity;
            for (int i = 0; i < solidShapes.Count; i++) bottom = Mathf.Min(bottom, solidShapes[i].bounds.min.y);
            return bottom;
        }

        void BrakeSolid()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 planar = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            Vector3 slowed = Vector3.MoveTowards(planar, Vector3.zero, solid.braking * dt);
            body.AddForce(slowed - planar, ForceMode.VelocityChange);
            // A rolling shape would spin itself back up to speed through friction, so slow the spin as well
            // (spin rate ~ speed / radius, with a radius of about half a metre).
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, solid.braking * 2f * dt);
        }

        void OnCollisionStay(Collision collision)
        {
            if ((groundMask.value & (1 << collision.gameObject.layer)) == 0) return;

            if (isSolid)
            {
                if (collision.gameObject.layer == pushableLayer) solidCrateContact = true;
                // The level itself (fixed, or a door or lift) can be a shore to climb out of the water onto.
                bool partOfLevel = collision.rigidbody == null || collision.rigidbody.isKinematic;
                // Anything holding the solid up from below counts as ground for pushing.
                for (int i = 0; i < collision.contactCount; i++)
                {
                    ContactPoint contact = collision.GetContact(i);
                    Vector3 normal = contact.normal;
                    if (normal.y > 0.5f)
                    {
                        solidGroundContact = true;
                        solidGroundNormal = normal;
                    }
                    else if (partOfLevel && normal.y > -0.3f)
                    {
                        shoreContact = true;
                        shoreNormal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
                        shorePoint = contact.point;
                    }
                }
                return;
            }

            float bottom = transform.TransformPoint(core.center).y - CoreRadius;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                float height = contact.point.y - bottom;
                // A steep surface touched low on the core, with a top to flow onto, is a step edge; anything else is a wall.
                if (contact.normal.y < 0.7f && height > ContactGap && height < maxStepHeight && HasStepTop(contact, bottom))
                {
                    stepContact = true;
                    stepNormal = Vector3.ProjectOnPlane(contact.normal, Vector3.up).normalized;
                }
            }
        }

        /// <summary>
        /// Is there ground to flow onto just past this contact, no higher than the step height above the bottom of the
        /// core? Looked for straight down from that height: on a wall taller than a step the look starts inside the wall
        /// and finds nothing. The core is small enough to touch a wall below the step height. Past a wall thinner than
        /// the look's inset it finds the floor behind, which is below the contact rather than on top of it.
        /// </summary>
        bool HasStepTop(ContactPoint contact, float bottom)
        {
            Vector3 into = -Vector3.ProjectOnPlane(contact.normal, Vector3.up).normalized;
            Vector3 origin = contact.point + into * StepTopInset;
            origin.y = bottom + maxStepHeight + ContactGap;
            return Physics.Raycast(origin, Vector3.down, out RaycastHit top, maxStepHeight + ContactGap, liquidGroundMask,
                                   QueryTriggerInteraction.Ignore)
                   && top.point.y >= contact.point.y - ContactGap
                   && Vector3.Angle(top.normal, Vector3.up) <= maxGroundAngle;
        }

        void ProbeGround()
        {
            float radius = CoreRadius;
            float castRadius = radius * 0.9f;
            Vector3 origin = transform.TransformPoint(core.center);
            float distance = radius - castRadius + groundProbeDistance;

            IsGrounded = Physics.SphereCast(origin, castRadius, Vector3.down, out RaycastHit hit, distance, liquidGroundMask, QueryTriggerInteraction.Ignore)
                         && Vector3.Angle(hit.normal, Vector3.up) <= maxGroundAngle;
            GroundNormal = IsGrounded ? hit.normal : Vector3.up;
            groundGap = IsGrounded ? hit.distance - (radius - castRadius) : float.PositiveInfinity;
        }

        /// <summary>
        /// Nothing under the core: does the stretched jelly span a gap here? Looks down along it for the nearest ground
        /// behind the core and the nearest ahead. It holds when the far side is under its front, or when its tail still
        /// lies over the near side and the far side is within the jelly's whole length. Only ground about level with the
        /// bottom of the core counts: a step up at most, or a little lower.
        /// </summary>
        void ProbeBridge()
        {
            if (skin == null || !skin.IsReady || skin.IsFrozen) return;
            Vector3 forward = Vector3.ProjectOnPlane(skin.Forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) return;
            forward.Normalize();

            Vector3 center = transform.TransformPoint(core.center);
            float bottom = center.y - CoreRadius;
            // Ground just under the core is a landing, not a gap.
            if (Physics.Raycast(center, Vector3.down, CoreRadius + bridgeDrop, liquidGroundMask, QueryTriggerInteraction.Ignore)) return;
            // Over a drain the jelly drips through the grate instead of spanning it.
            if (FloorGrate.IsOver(center, CoreRadius + maxStepHeight)) return;
            bool behind = FindGround(center, -forward, skin.ReachBehind - bridgeGrip, bottom, out float behindDistance, out float behindHeight);
            float reach = skin.ReachAhead - bridgeGrip;
            if (behind) reach = Mathf.Max(reach, skin.ReachAhead + skin.ReachBehind - 2f * bridgeGrip - behindDistance);
            if (!FindGround(center, forward, reach, bottom, out float aheadDistance, out float aheadHeight)) return;

            // On the line between the two sides, so a far side a little higher or lower is reached smoothly.
            bridgeHeight = behind
                ? Mathf.Lerp(behindHeight, aheadHeight, behindDistance / (behindDistance + aheadDistance))
                : aheadHeight;
            IsBridging = true;
            IsGrounded = true;
            GroundNormal = Vector3.up;
            groundGap = 0f;
        }

        /// <summary>
        /// The nearest ground about level with <paramref name="bottom"/> along a flat direction from the core, looked for
        /// every sample spacing up to <paramref name="range"/> away.
        /// </summary>
        bool FindGround(Vector3 center, Vector3 direction, float range, float bottom, out float distance, out float height)
        {
            float spacing = Mathf.Max(0.05f, bridgeSampleSpacing);
            float minNormalY = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
            for (distance = spacing; distance <= range; distance += spacing)
            {
                Vector3 origin = center + direction * distance;
                origin.y = bottom + maxStepHeight;
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxStepHeight + bridgeDrop, liquidGroundMask,
                                    QueryTriggerInteraction.Ignore)
                    && hit.normal.y >= minNormalY)
                {
                    height = hit.point.y;
                    return true;
                }
            }
            height = 0f;
            return false;
        }

        /// <summary>Glides the bridging core along the line between the two sides instead of letting it fall.</summary>
        void HoldBridge()
        {
            float bottom = transform.TransformPoint(core.center).y - CoreRadius;
            Vector3 velocity = body.linearVelocity;
            velocity.y = Mathf.Clamp((bridgeHeight + BridgeClearance - bottom) * 10f, -stepClimbSpeed, stepClimbSpeed);
            body.linearVelocity = velocity;
            body.AddForce(-Physics.gravity, ForceMode.Acceleration);
        }

        /// <summary>Skimming just above the ground: turn any velocity away from it along the surface, keeping speed.</summary>
        void HugGround()
        {
            Vector3 velocity = body.linearVelocity;
            float away = Vector3.Dot(velocity, GroundNormal);
            if (away > 0f) body.linearVelocity = (velocity - GroundNormal * away).normalized * velocity.magnitude;
        }

        void SnapToGround()
        {
            Vector3 velocity = body.linearVelocity;
            float speed = velocity.magnitude;
            if (speed > snapMaxSpeed) return;

            float castRadius = CoreRadius * 0.9f;
            Vector3 origin = transform.TransformPoint(core.center);
            if (!Physics.SphereCast(origin, castRadius, Vector3.down, out RaycastHit hit, snapDistance, liquidGroundMask, QueryTriggerInteraction.Ignore)
                || Vector3.Angle(hit.normal, Vector3.up) > maxGroundAngle)
                return;

            IsGrounded = true;
            GroundNormal = hit.normal;
            stepsSinceGrounded = 0;
            groundGap = hit.distance - (CoreRadius - castRadius);
            // Keep the speed but turn it along the surface, so the blob flows over the crest.
            float away = Vector3.Dot(velocity, hit.normal);
            if (away > 0f) body.linearVelocity = (velocity - hit.normal * away).normalized * speed;
        }

        Vector3 WishDirection()
        {
            Vector2 move = input != null ? input.Move : Vector2.zero;
            if (move.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 forward = Vector3.forward;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
                // Looking straight down: the camera's up vector is the screen's forward.
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
                forward.Normalize();
            }
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return forward * move.y + right * move.x;
        }

        static float MaxAbsComponent(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
    }
}
