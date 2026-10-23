using System.Collections;
using BounJam.Core;
using BounJam.Level;
using BounJam.Liquids;
using BounJam.Player;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace BounJam.Enemies
{
    /// <summary>
    /// A living flame. It wanders near where it was placed until it sees the beaver, then rushes at it;
    /// touching the beaver fails the level. Water slows it while it lands and puts it out once enough has
    /// landed. Being sprayed also gives the beaver's position away. Needs a nav mesh (Level bakes one).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(LiquidTarget))]
    public class FireSoul : MonoBehaviour, ILiquidReceiver
    {
        enum State
        {
            Wander,
            Chase,
            Search,
            Dying,
        }

        static readonly int SightBlockers = GameLayers.MaskOf(GameLayers.Default, GameLayers.Ground, GameLayers.Tree, GameLayers.Pushable);

        [Header("Senses")]
        [SerializeField] float sightRange = 10f;
        [SerializeField, Range(0f, 360f)] float fieldOfView = 140f;
        [SerializeField, Tooltip("Closer than this it notices the beaver whichever way it faces.")]
        float senseRange = 2.5f;
        [SerializeField, Tooltip("Seconds without seeing the beaver before it gives up and searches.")]
        float loseSightAfter = 3f;

        [Header("Movement")]
        [SerializeField] float wanderRadius = 5f;
        [SerializeField] float wanderSpeed = 1.8f;
        [SerializeField] float chaseSpeed = 4.2f;
        [SerializeField] float wanderPause = 1.5f;

        [Header("Water")]
        [SerializeField, Min(0.1f), Tooltip("Units of water it takes to put out. Water sprays 1 unit a second.")]
        float waterToExtinguish = 1.5f;
        [SerializeField, Range(0f, 1f), Tooltip("Speed multiplier while water is landing on it.")]
        float wetSlowdown = 0.4f;

        [Header("Parts")]
        [SerializeField] Transform body;
        [SerializeField] ParticleSystem flames;
        [SerializeField] ParticleSystem steam;
        [SerializeField] Light glow;
        [SerializeField] Collider hitbox;
        [SerializeField] Collider touch;
        [SerializeField] UnityEvent onExtinguished;

        NavMeshAgent agent;
        Transform player;
        State state;
        Vector3 home;
        Vector3 lastSeenPosition;
        float lastSeenTime = float.NegativeInfinity;
        float lastWetTime = float.NegativeInfinity;
        float nextRepathTime;
        float wanderAgainTime = -1f;
        float remaining;
        float flameSize;

        public bool IsBurning => state != State.Dying;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            home = transform.position;
            remaining = waterToExtinguish;
            flameSize = flames.main.startSizeMultiplier;
        }

        // The agent starts disabled and is switched on here, after Level has baked the nav mesh in Awake.
        void Start()
        {
            var beaver = FindAnyObjectByType<PlayerMotor>();
            player = beaver != null ? beaver.transform : null;
            agent.enabled = true;
            if (!agent.isOnNavMesh)
                Debug.LogWarning($"[BounJam] {name} is not on a nav mesh; is there a Level in this scene?", this);
        }

        void Update()
        {
            if (state == State.Dying || !agent.isOnNavMesh)
                return;

            if (player != null && CanSee(player.position))
                Spot(player.position);

            switch (state)
            {
                case State.Chase:
                    if (Time.time - lastSeenTime > loseSightAfter)
                    {
                        state = State.Search;
                        agent.SetDestination(lastSeenPosition);
                    }
                    else if (Time.time >= nextRepathTime)
                    {
                        agent.SetDestination(lastSeenPosition);
                        nextRepathTime = Time.time + 0.2f;
                    }
                    break;
                case State.Search:
                    if (Arrived())
                        state = State.Wander;
                    break;
                case State.Wander:
                    Wander();
                    break;
            }

            float speed = state == State.Wander ? wanderSpeed : chaseSpeed;
            agent.speed = Time.time - lastWetTime < 0.25f ? speed * wetSlowdown : speed;
        }

        void Spot(Vector3 position)
        {
            lastSeenTime = Time.time;
            lastSeenPosition = position;
            state = State.Chase;
        }

        // Strolls to random points near home, pausing at each.
        void Wander()
        {
            if (!Arrived())
                return;
            if (wanderAgainTime < 0f)
            {
                wanderAgainTime = Time.time + wanderPause;
            }
            else if (Time.time >= wanderAgainTime)
            {
                wanderAgainTime = -1f;
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                if (NavMesh.SamplePosition(home + new Vector3(offset.x, 0f, offset.y), out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    agent.SetDestination(hit.position);
            }
        }

        bool Arrived() => !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f;

        bool CanSee(Vector3 target)
        {
            Vector3 eye = transform.position + Vector3.up * 0.8f;
            Vector3 toTarget = target + Vector3.up * 0.6f - eye;
            float distance = toTarget.magnitude;
            if (distance > sightRange)
                return false;
            if (distance > senseRange && Vector3.Angle(transform.forward, toTarget) > fieldOfView / 2f)
                return false;
            return !Physics.Raycast(eye, toTarget, distance, SightBlockers, QueryTriggerInteraction.Ignore);
        }

        void OnTriggerEnter(Collider other)
        {
            if (IsBurning && other.gameObject.layer == GameLayers.Player && LevelManager.Current != null)
                LevelManager.Current.Fail("A Fire Soul caught you.");
        }

        public void OnLiquidHit(in LiquidHit hit)
        {
            float water = hit.Amount * hit.Liquid.ExtinguishPower;
            if (!IsBurning || water <= 0f)
                return;

            lastWetTime = Time.time;
            if (player != null)
                Spot(player.position);
            steam.Emit(new ParticleSystem.EmitParams { position = hit.Point, applyShapeToPosition = true }, 1);
            remaining -= water;
            if (remaining <= 0f)
            {
                StartCoroutine(GoOut());
                return;
            }
            ParticleSystem.MainModule main = flames.main;
            main.startSizeMultiplier = flameSize * Mathf.Lerp(0.4f, 1f, remaining / waterToExtinguish);
        }

        IEnumerator GoOut()
        {
            state = State.Dying;
            agent.isStopped = true;
            hitbox.enabled = false;
            touch.enabled = false;
            glow.enabled = false;
            flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            steam.Emit(30);
            Debug.Log($"[BounJam] {name} was put out.", this);
            onExtinguished.Invoke();

            Vector3 size = body.localScale;
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                body.localScale = size * (1f - t / 0.6f);
                yield return null;
            }
            body.gameObject.SetActive(false);
            Destroy(gameObject, 1.5f); // let the steam finish
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, sightRange);
            Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
            Gizmos.DrawWireSphere(Application.isPlaying ? home : transform.position, wanderRadius);
        }
    }
}
