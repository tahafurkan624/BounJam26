using System.Collections;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Throws water droplets off the liquid blob on sharp moves: braking from speed, reversing or turning hard,
    /// smacking into a wall, landing and taking off. After every physics step it compares the core's velocity with the
    /// one before. The water in the skin wants to keep the velocity the core just lost, so whatever speed is lost faster
    /// than the threshold is flung off the side the water is thrown toward; where the skin is pressed against a wall it
    /// splashes out along the wall instead. Speeding up and smooth turns keep their speed, so steady running and gentle
    /// steering stay dry apart from the odd drip off the back. Liquid form only. Droplets are decided after the physics
    /// step and thrown in LateUpdate, from where the skin is drawn. The same moves stir up the water's surface: the skin
    /// shader's _Splash jumps with them (choppier ripples, drip rings, wider foam) and settles back.
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobSkin))]
    public class BlobSplash : MonoBehaviour
    {
        enum Kind { None, Fling, Wall, Landing, TakeOff, Drip }

        struct Burst
        {
            public Kind Kind;
            public int Count;
            // Fling and drip: the way the water is thrown. Wall and landing: out of the surface it hits.
            public Vector3 Direction;
            // The velocity the water had before the change, m/s.
            public Vector3 Carried;
            // Wall and landing: speed into the surface. Take-off: the launch speed. m/s.
            public float Speed;
            // 0..1, how hard the move was; harder moves throw slightly bigger droplets.
            public float Hardness;
        }

        [Tooltip("Droplet particles: world space, emitted only from here. Taken out from under the blob on start, so " +
                 "thrown water stays in the level instead of moving with the body or counting toward its bounds.")]
        [SerializeField] ParticleSystem droplets;
        [Tooltip("Walls and obstacles the water splashes out along when the skin is thrown against them.")]
        [SerializeField] LayerMask wallMask = ~0;
        [Tooltip("Skin renderer, for the water shader's _Splash. Shares the material copy BlobForm makes.")]
        [SerializeField] Renderer skinRenderer;

        [Header("Sharp Moves")]
        [Tooltip("Losing speed faster than this throws water off, m/s². Braking is 25, a reversal 12, a hard turn " +
                 "about 8-13; smooth steering keeps its speed and stays dry.")]
        [SerializeField] float sharpThreshold = 6.5f;
        [Tooltip("How much the sideways part of a velocity change counts next to losing speed. At 0.5 turning alone " +
                 "(at most 12 m/s²) never splashes; turning while losing speed does.")]
        [Range(0f, 1f)]
        [SerializeField] float turnWeight = 0.5f;
        [Tooltip("Droplets per m/s of speed lost beyond the threshold. A stop from top speed loses about 3.")]
        [SerializeField] float dropletsPerSpeed = 8f;
        [Tooltip("Below this speed along the ground, stopping and turning throw nothing, m/s.")]
        [SerializeField] float minSpeed = 1.5f;

        [Header("Throw")]
        [Tooltip("Share of the velocity before the change that thrown droplets keep.")]
        [Range(0f, 1f)]
        [SerializeField] float inheritVelocity = 0.7f;
        [Tooltip("Extra speed in the direction the water is thrown, m/s.")]
        [SerializeField] float flingSpeed = 1.2f;
        [Tooltip("Upward speed of flung droplets, so they arc instead of skimming the floor, m/s.")]
        [SerializeField] float flingLift = 1.6f;
        [Tooltip("How widely flung droplets fan out: 0 = a straight line, 1 = 45° to either side.")]
        [Range(0f, 1f)]
        [SerializeField] float flingSpread = 0.5f;
        [Tooltip("Random speed added to every droplet, m/s.")]
        [SerializeField] float jitter = 0.4f;

        [Header("Impacts")]
        [Tooltip("A wall this far past the skin, in the direction the water is thrown, turns the fling into a splash " +
                 "along it, m. The skin keeps about 3 cm off surfaces it's pressed against.")]
        [SerializeField] float wallProbe = 0.1f;
        [Tooltip("Speed droplets bounce back off a wall or the floor with, per m/s of impact.")]
        [SerializeField] float impactRebound = 0.4f;
        [Tooltip("Speed droplets spray out along a wall or the floor with, per m/s of impact.")]
        [SerializeField] float impactSpread = 0.55f;
        [Tooltip("Landing faster than this splashes a crown around the base, m/s. A full jump lands at about 4, a fall " +
                 "off a 0.6 m ledge at 3.4; smaller drops are snapped down without a landing.")]
        [SerializeField] float landingMinSpeed = 2.5f;
        [Tooltip("Crown droplets at the slowest landing that splashes.")]
        [SerializeField] int landingDroplets = 6;
        [Tooltip("Extra crown droplets per m/s of landing speed above the minimum.")]
        [SerializeField] float landingPerSpeed = 5f;

        [Header("Take-off")]
        [Tooltip("Droplets squeezed out from under the blob by a full jump.")]
        [SerializeField] int takeOffDroplets = 4;
        [Tooltip("Share of the jump's upward speed those droplets get. Below 1, so they fall away under the blob.")]
        [Range(0f, 1f)]
        [SerializeField] float takeOffLift = 0.45f;

        [Header("Drips")]
        [Tooltip("Drips per second off the back at top speed on the ground; none at all below half speed.")]
        [SerializeField] float dripsPerSecond = 0.5f;

        [Header("Droplets")]
        [Tooltip("Most droplets one splash throws. 0 turns the splash off.")]
        [SerializeField] int maxBurst = 20;
        [Tooltip("Smallest (x) and largest (y) droplet diameter, m. Most are small.")]
        [SerializeField] Vector2 dropletSize = new Vector2(0.07f, 0.14f);
        [Tooltip("How long a droplet that never hits anything (one thrown off the level) lives, s.")]
        [SerializeField] float dropletLifetime = 1.5f;
        [Tooltip("Droplets start this far outside the skin, m, so they aren't hidden inside the jelly.")]
        [SerializeField] float surfaceOffset = 0.02f;
        [Tooltip("Moving this much further in one physics step than the velocity explains is a teleport or respawn, " +
                 "which never splashes, m.")]
        [SerializeField] float teleportDistance = 0.5f;

        [Header("Surface")]
        [Tooltip("How stirred up the water's surface gets from the gentlest (x) and the hardest (y) sharp move, 0..1.")]
        [SerializeField] Vector2 surfaceSplash = new Vector2(0.35f, 1f);
        [Tooltip("How long the surface takes to calm down from fully stirred up, s.")]
        [SerializeField] float surfaceSettleTime = 0.8f;

        // Droplets owed by a sharp move fade over about this long, s, so a gentle one that never adds up to a whole
        // droplet stays dry.
        const float ChargeMemory = 0.2f;
        // Losing speed this fast beyond the threshold counts as the hardest move (full braking is close, wall hits are
        // far past it), m/s².
        const float HardDeceleration = 25f;
        // The same for landings (a 2 m drop), m/s.
        const float HardLanding = 6f;
        // A full jump's take-off speed (0.8 m high), m/s.
        const float FullJumpSpeed = 4f;
        // Where on the skin water leaves from, as the cosine from the surface it hits: round the rim of the patch
        // pressed against a wall, just under the middle when landing. Take-off: low on the sides, from straight up.
        const float WallBand = 0.45f;
        const float LandingBand = 0.25f;
        const float TakeOffBand = -0.5f;
        // Outward push off the skin, and the sideways spread of take-off droplets, m/s.
        const float OutwardSpeed = 0.5f;
        const float TakeOffSpread = 0.8f;
        // Random skin points tried per droplet; the one closest to the wanted part of the skin wins.
        const int PointTries = 8;
        // Splashes waiting for LateUpdate; a slow frame runs several physics steps.
        const int MaxPending = 8;
        static readonly int SplashProperty = Shader.PropertyToID("_Splash");

        BlobMotor motor;
        BlobSkin skin;
        BlobForm form;
        Rigidbody body;
        // The skin's own material copy (see BlobForm), so stirring it up only touches this blob.
        Material skinMaterial;
        Coroutine tracking;
        ParticleSystem.EmitParams emit;
        readonly Burst[] pending = new Burst[MaxPending];
        int pendingCount;

        bool tracked;
        bool wasGrounded;
        Vector3 previousVelocity;
        Vector3 previousPosition;
        float charge;
        float stirred;
        Kind lastKind;
        int lastCount;

        /// <summary>The droplet particles this blob throws its water into; others splashing its water (the ship) use them too.</summary>
        public ParticleSystem Droplets => droplets;
        /// <summary>Droplets thrown since the blob started (for testing).</summary>
        public int Thrown { get; private set; }
        /// <summary>What threw the last droplets and how many, e.g. "Landing 10" (for testing).</summary>
        public string LastSplash => $"{lastKind} {lastCount}";

        /// <summary>Throws a landing crown and a fling straight ahead right now, to check the look standing still (for testing).</summary>
        public void TestSplash()
        {
            if (!IsLiquid) return;
            Queue(Kind.Landing, 12, Vector3.up, Vector3.down * 4f, 4f, 0.7f);
            Queue(Kind.Fling, 10, Vector3.forward, Vector3.forward * 4f, 0f, 0.7f);
        }

        bool IsLiquid => skin.IsReady && !skin.IsFrozen && (form == null || !form.IsSolid);

        void Reset() => wallMask = LayerMask.GetMask("Default", "Environment", "Pushable", "Interactable");

        void Awake()
        {
            motor = GetComponent<BlobMotor>();
            skin = GetComponent<BlobSkin>();
            form = GetComponent<BlobForm>();
            body = GetComponent<Rigidbody>();
            // Renderer.material hands back the copy BlobForm makes (or makes it first, which BlobForm then gets).
            if (skinRenderer != null) skinMaterial = skinRenderer.material;
            if (droplets != null) droplets.transform.SetParent(null, true);
        }

        void OnEnable()
        {
            motor.Jumped += OnJumped;
            tracked = false;
            tracking = StartCoroutine(TrackAfterPhysics());
        }

        void OnDisable()
        {
            motor.Jumped -= OnJumped;
            if (tracking != null) StopCoroutine(tracking);
            pendingCount = 0;
            stirred = 0f;
            if (skinMaterial != null) skinMaterial.SetFloat(SplashProperty, 0f);
        }

        void OnDestroy()
        {
            // No longer under the blob, so it would outlive it.
            if (droplets != null && droplets.transform.parent == null) Destroy(droplets.gameObject);
        }

        // After the physics step, like the skin: the velocity then includes this step's steering, braking and contacts.
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
            Vector3 velocity = body.linearVelocity;
            Vector3 position = body.position;
            bool grounded = motor.IsGrounded;
            bool liquid = IsLiquid && droplets != null;
            bool teleported = (position - previousPosition - velocity * dt).sqrMagnitude > teleportDistance * teleportDistance;
            if (liquid && tracked && !teleported)
            {
                Fling(velocity, position, dt);
                // The physics step has already stopped the fall, so the impact speed comes from the step before.
                if (grounded && !wasGrounded) Land(previousVelocity);
                if (grounded) Drip(velocity, dt);
            }
            else
            {
                // Out of the solid form or across a teleport: start over from the current motion.
                charge = 0f;
            }

            previousVelocity = velocity;
            previousPosition = position;
            wasGrounded = grounded;
            tracked = liquid;
        }

        /// <summary>
        /// Braking, reversing, turning hard and hitting walls, along the ground: the water keeps the velocity the core
        /// just lost, and whatever is lost faster than the threshold is thrown off the leading side, or against the wall
        /// the skin is pressed into.
        /// </summary>
        void Fling(Vector3 velocity, Vector3 core, float dt)
        {
            charge *= Mathf.Exp(-dt / ChargeMemory);

            // Along the ground only: the ground soaks up what's lost into it (landings splash on their own, tilting onto
            // a ramp doesn't), and in the air gravity pulls the water as hard as the core.
            Vector3 up = motor.IsGrounded ? motor.GroundNormal : Vector3.up;
            Vector3 before = Vector3.ProjectOnPlane(previousVelocity, up);
            float speed = before.magnitude;
            if (speed < minSpeed) return;
            Vector3 heading = before / speed;

            Vector3 kick = before - Vector3.ProjectOnPlane(velocity, up);
            float slowing = Vector3.Dot(kick, heading);
            // Speeding up never splashes: setting off accelerates as hard as a reversal brakes.
            if (slowing <= 0f) return;
            float turning = (kick - heading * slowing).magnitude;
            float lost = slowing + turnWeight * turning - sharpThreshold * dt;
            if (lost <= 0f) return;

            charge += lost * dropletsPerSpeed;
            int count = Mathf.Min((int)charge, maxBurst);
            if (count <= 0) return;
            // A hit too hard for one splash is capped, not saved up for the steps after.
            charge = count == maxBurst ? 0f : charge - count;

            Vector3 direction = kick.normalized;
            float hardness = Mathf.Clamp01(lost / dt / HardDeceleration);
            if (Physics.SphereCast(core, motor.CoreRadius * 0.5f, direction, out RaycastHit hit, SkinReach(core, direction) + wallProbe,
                                   wallMask, QueryTriggerInteraction.Ignore)
                && Vector3.Dot(hit.normal, direction) < -0.5f)
            {
                float into = Mathf.Max(-Vector3.Dot(before, hit.normal), minSpeed);
                Queue(Kind.Wall, count, hit.normal, before, into, hardness);
            }
            else Queue(Kind.Fling, count, direction, before, 0f, hardness);
        }

        void Land(Vector3 impactVelocity)
        {
            Vector3 up = motor.GroundNormal;
            float speed = -Vector3.Dot(impactVelocity, up);
            if (speed < landingMinSpeed) return;
            int count = landingDroplets + Mathf.RoundToInt((speed - landingMinSpeed) * landingPerSpeed);
            Queue(Kind.Landing, count, up, impactVelocity, speed, Mathf.Clamp01(speed / HardLanding));
        }

        /// <summary>An occasional drip off the back while running fast; nothing at all below half speed.</summary>
        void Drip(Vector3 velocity, float dt)
        {
            float fast = Mathf.Clamp01(motor.NormalizedSpeed * 2f - 1f);
            if (dripsPerSecond <= 0f || Random.value >= dripsPerSecond * fast * fast * dt) return;
            Vector3 back = -new Vector3(velocity.x, 0f, velocity.z).normalized;
            Queue(Kind.Drip, 1, back, velocity, 0f, 0f);
        }

        /// <summary>The whole blob pushes off the ground; a little water is squeezed out from under it.</summary>
        void OnJumped(float launch)
        {
            if (!tracked) return;
            int count = Mathf.RoundToInt(takeOffDroplets * Mathf.Clamp01(launch / FullJumpSpeed));
            Queue(Kind.TakeOff, count, Vector3.up, body.linearVelocity, launch, 0.3f);
        }

        void Queue(Kind kind, int count, Vector3 direction, Vector3 carried, float speed, float hardness)
        {
            count = Mathf.Min(count, maxBurst);
            if (count <= 0 || pendingCount == pending.Length) return;
            pending[pendingCount++] = new Burst
            {
                Kind = kind, Count = count, Direction = direction, Carried = carried, Speed = speed, Hardness = hardness,
            };
            lastKind = kind;
            lastCount = count;
            // A drip off the back is no upheaval.
            if (kind != Kind.Drip) Stir(hardness);
        }

        /// <summary>Stirs up the water's surface like a sharp move this hard (0..1), for something splashing into it.</summary>
        public void Stir(float hardness)
        {
            stirred = Mathf.Max(stirred, Mathf.Lerp(surfaceSplash.x, surfaceSplash.y, hardness));
        }

        void LateUpdate()
        {
            // Down to exactly 0 on the last write, then left alone.
            if (skinMaterial != null && stirred > 0f)
            {
                stirred = Mathf.MoveTowards(stirred, 0f, Time.deltaTime / Mathf.Max(surfaceSettleTime, 0.01f));
                skinMaterial.SetFloat(SplashProperty, stirred);
            }

            if (pendingCount == 0) return;
            if (droplets != null && skin.IsReady)
            {
                // The skin points are where the last physics step left them, but the blob is drawn interpolated
                // between steps: throw the water from where it's seen.
                Vector3 shift = transform.position - body.position;
                for (int i = 0; i < pendingCount; i++) Throw(pending[i], shift);
            }
            pendingCount = 0;
        }

        void Throw(Burst burst, Vector3 shift)
        {
            Vector3[] points = skin.Points;
            Vector3 center = skin.ShapeCenter;
            for (int k = 0; k < burst.Count; k++)
            {
                // Mostly small droplets with the odd big one; harder moves throw bigger ones.
                float size = Mathf.Lerp(dropletSize.x, dropletSize.y, Random.value * Random.value) * Mathf.Lerp(0.85f, 1.15f, burst.Hardness);
                float spread = jitter;
                int index;
                Vector3 outward, velocity, offset;
                switch (burst.Kind)
                {
                    case Kind.Fling:
                    {
                        // Off the upper rim on the thrown side, carrying on about the way the water was going, fanned out.
                        index = PickPoint(points, center, (burst.Direction + Vector3.up * 0.4f).normalized, 1f, out outward);
                        Vector3 thrown = burst.Carried * inheritVelocity
                                         + burst.Direction * (flingSpeed * Random.Range(0.5f, 1.2f) * Mathf.Lerp(0.7f, 1.2f, burst.Hardness));
                        thrown = Quaternion.AngleAxis(Random.Range(-45f, 45f) * flingSpread, Vector3.up) * thrown;
                        velocity = thrown + Vector3.up * (flingLift * Random.Range(0.5f, 1.1f)) + outward * (OutwardSpeed * Random.value);
                        offset = Vector3.zero;
                        break;
                    }
                    case Kind.Wall:
                    case Kind.Landing:
                    {
                        // Water thrown against a surface splashes off it: back out a little, and sprayed out along it
                        // from round the spot it hit, still sliding the way it was going.
                        Vector3 normal = burst.Direction;
                        index = PickPoint(points, center, -normal, burst.Kind == Kind.Wall ? WallBand : LandingBand, out outward);
                        Vector3 along = Vector3.ProjectOnPlane(outward, normal);
                        if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(Random.onUnitSphere, normal);
                        along.Normalize();
                        velocity = Vector3.ProjectOnPlane(burst.Carried, normal) * inheritVelocity
                                   + normal * (burst.Speed * impactRebound * Random.Range(0.4f, 1f))
                                   + along * (burst.Speed * impactSpread * Random.Range(0.5f, 1f))
                                   + Vector3.up * (flingLift * 0.5f * Random.value);
                        // Off the surface too, so the droplet doesn't start inside what it splashed against.
                        offset = normal * (size * 0.5f);
                        break;
                    }
                    case Kind.TakeOff:
                    {
                        // Squeezed out sideways from low on the blob, with less lift than the jump: left behind.
                        index = PickPoint(points, center, Vector3.up, TakeOffBand, out outward);
                        Vector3 sideways = new Vector3(outward.x, 0f, outward.z).normalized;
                        velocity = new Vector3(burst.Carried.x, 0f, burst.Carried.z) * inheritVelocity
                                   + Vector3.up * (burst.Speed * takeOffLift * Random.Range(0.6f, 1.2f))
                                   + sideways * (TakeOffSpread * Random.Range(0.3f, 1f));
                        offset = Vector3.zero;
                        break;
                    }
                    default:
                    {
                        // A small drip off the lower back, falling behind the blob.
                        index = PickPoint(points, center, (burst.Direction + Vector3.down * 0.7f).normalized, 1f, out outward);
                        velocity = burst.Carried * Random.Range(0.6f, 0.9f) + Vector3.down * 0.3f;
                        size *= 0.6f;
                        spread *= 0.3f;
                        offset = Vector3.zero;
                        break;
                    }
                }

                emit.position = points[index] + outward * surfaceOffset + offset + shift;
                emit.velocity = velocity + Random.insideUnitSphere * spread;
                emit.startSize = size;
                emit.startLifetime = dropletLifetime * Random.Range(0.8f, 1f);
                droplets.Emit(emit, 1);
                Thrown++;
            }
        }

        /// <summary>
        /// A random skin point, the best of a few tried: the one whose direction from the centre is closest to
        /// <paramref name="band"/> as a cosine from <paramref name="axis"/> (1 = the side facing it, 0 = the rim at right
        /// angles). Spreads the droplets over that part of the skin instead of stacking them. Also returns that direction.
        /// </summary>
        static int PickPoint(Vector3[] points, Vector3 center, Vector3 axis, float band, out Vector3 outward)
        {
            int best = 0;
            float bestScore = float.PositiveInfinity;
            outward = axis;
            for (int t = 0; t < PointTries; t++)
            {
                int i = Random.Range(0, points.Length);
                Vector3 direction = (points[i] - center).normalized;
                float score = Mathf.Abs(Vector3.Dot(direction, axis) - band);
                if (score >= bestScore) continue;
                bestScore = score;
                best = i;
                outward = direction;
            }
            return best;
        }

        /// <summary>How far the skin reaches from the core in a direction, m.</summary>
        float SkinReach(Vector3 core, Vector3 direction)
        {
            float reach = 0f;
            Vector3[] points = skin.Points;
            for (int i = 0; i < points.Length; i++) reach = Mathf.Max(reach, Vector3.Dot(points[i] - core, direction));
            return reach;
        }
    }
}
