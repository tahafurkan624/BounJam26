using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BounJam
{
    /// <summary>
    /// The flower a planted seed grows into (SK_LeafLift): a lift up to the tile above its soil bed. It sprouts into a
    /// closed bud whose leaf is a low step, waits there, and rises once something has stood on the leaf for a moment.
    /// At the top it opens and its two petal ramps unfold toward the upper tile; once nobody has been on it for a while
    /// it closes and goes back down. It never crushes anything: while going down it waits for whatever is under the
    /// leaf (the blob, the ship, a crate) to move away.
    /// The clips are sampled by hand every physics step, and kinematic bodies follow the model's collider nodes with
    /// MovePosition, so whatever stands on the leaf rides along. Every frame the model is sampled again between the
    /// last two steps, the way interpolated rigidbodies are drawn, so it moves smoothly with its riders.
    /// </summary>
    public class FlowerLift : MonoBehaviour
    {
        enum Stage { Dormant, Sprouting, Bottom, Rising, Top, Descending }

        /// <summary>A kinematic body that follows a node of the animated model.</summary>
        [System.Serializable]
        struct Follower
        {
            public Transform node;
            public Rigidbody body;
            public Collider shape;
        }

        [Header("Model")]
        [SerializeField] Animator animator;
        [SerializeField] AnimationClip sproutClip;
        [SerializeField] AnimationClip idleBottomClip;
        [SerializeField] AnimationClip riseClip;
        [SerializeField] AnimationClip idleTopClip;
        [SerializeField] AnimationClip descendClip;

        [Header("Colliders")]
        [Tooltip("Follows the leaf the riders stand on.")]
        [SerializeField] Follower leaf;
        [Tooltip("Follow the petal ramps; solid only while the flower is open at the top.")]
        [SerializeField] Follower[] ramps;
        [Tooltip("Capsule along the stem, from its foot to the leaf.")]
        [SerializeField] Rigidbody stemBody;
        [SerializeField] CapsuleCollider stemShape;
        [SerializeField] Transform stemFoot;
        [SerializeField] Transform stemTop;

        [Header("Riding")]
        [Tooltip("How long something has to stand on the leaf at the bottom before it rises, s.")]
        [SerializeField] float boardDelay = 0.6f;
        [Tooltip("How long the leaf has to be empty at the top before it goes back down, s.")]
        [SerializeField] float returnDelay = 3f;
        [Tooltip("What rides it up, and what it waits for instead of crushing it on the way down.")]
        [SerializeField] LayerMask riderMask;

        // Riders are looked for in a slab this tall above the leaf, m...
        const float RiderReach = 0.4f;
        // ...and have to rest on it: their bottom no more than this far below its top, m.
        const float RiderSink = 0.15f;
        // Extra look-ahead below the leaf beyond one step on the way down, m.
        const float SafetyMargin = 0.05f;

        readonly Collider[] found = new Collider[16];
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables;
        AnimationClip[] clips;
        Stage stage = Stage.Dormant;
        // Time into the current stage's clip, s; loops wrap it only when sampled, so it can be interpolated.
        float time;
        // Stage and time at the previous physics step, for drawing in between.
        Stage previousStage;
        float previousTime;
        float riderTime;
        float emptyTime;

        void Reset() => riderMask = LayerMask.GetMask("PlayerLiquid", "PlayerSolid", "Ship", "Pushable");

        void Awake()
        {
            // Indexed by stage - 1.
            clips = new[] { sproutClip, idleBottomClip, riseClip, idleTopClip, descendClip };
            graph = PlayableGraph.Create(name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            playables = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                graph.Connect(playables[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, name, animator).SetSourcePlayable(mixer);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        /// <summary>
        /// Starts it growing: from a sprout, playing event:/Tree once, or (<paramref name="sprout"/> false) as a bud
        /// already waiting at the bottom.
        /// </summary>
        public void Grow(bool sprout)
        {
            if (sprout) GameSounds.Tree.PlayAt(transform.position);
            Enter(sprout ? Stage.Sprouting : Stage.Bottom);
            previousStage = stage;
            previousTime = time;
            Sample(stage, time);
            MoveBodies(teleport: true);
        }

        void Enter(Stage next)
        {
            stage = next;
            time = 0f;
            riderTime = 0f;
            emptyTime = 0f;
            foreach (Follower ramp in ramps)
                if (ramp.shape != null) ramp.shape.enabled = next == Stage.Top;
        }

        void FixedUpdate()
        {
            if (stage == Stage.Dormant) return;

            float dt = Time.fixedDeltaTime;
            previousStage = stage;
            previousTime = time;
            bool ridden = HasRiders();
            riderTime = ridden ? riderTime + dt : 0f;
            emptyTime = ridden ? 0f : emptyTime + dt;

            switch (stage)
            {
                case Stage.Sprouting:
                    PlayOnce(dt, Stage.Bottom);
                    break;
                case Stage.Bottom:
                    time += dt;
                    if (riderTime >= boardDelay) Enter(Stage.Rising);
                    break;
                case Stage.Rising:
                    PlayOnce(dt, Stage.Top);
                    break;
                case Stage.Top:
                    time += dt;
                    if (emptyTime >= returnDelay) Enter(Stage.Descending);
                    break;
                case Stage.Descending:
                    // Look where the next step would put the leaf; stay put if that lowers it onto something.
                    Sample(stage, Mathf.Min(time + dt, descendClip.length));
                    float drop = leaf.body.position.y - leaf.node.position.y;
                    if (drop <= 0f || !IsBlocked(drop)) PlayOnce(dt, Stage.Bottom);
                    break;
            }

            Sample(stage, time);
            MoveBodies(teleport: false);
        }

        void PlayOnce(float dt, Stage next)
        {
            time += dt;
            if (time >= clips[(int)stage - 1].length) Enter(next);
        }

        void Update()
        {
            if (stage == Stage.Dormant) return;
            // Between the last two physics steps, like interpolated rigidbodies; a stage change just shows the new one.
            float k = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            Sample(stage, previousStage == stage ? Mathf.Lerp(previousTime, time, k) : time);
        }

        void Sample(Stage at, float t)
        {
            int index = (int)at - 1;
            AnimationClip clip = clips[index];
            bool loops = at == Stage.Bottom || at == Stage.Top;
            for (int i = 0; i < playables.Length; i++) mixer.SetInputWeight(i, i == index ? 1f : 0f);
            playables[index].SetTime(loops ? Mathf.Repeat(t, clip.length) : Mathf.Min(t, clip.length));
            graph.Evaluate();
        }

        void MoveBodies(bool teleport)
        {
            Follow(leaf, teleport);
            foreach (Follower ramp in ramps) Follow(ramp, teleport);

            if (stemBody == null) return;
            Vector3 foot = stemFoot.position, top = stemTop.position;
            Vector3 axis = top - foot;
            float length = axis.magnitude;
            stemShape.height = Mathf.Max(length, stemShape.radius * 2f);
            Vector3 center = (foot + top) * 0.5f;
            Quaternion rotation = length > 1e-4f ? Quaternion.FromToRotation(Vector3.up, axis) : Quaternion.identity;
            Place(stemBody, center, rotation, teleport);
        }

        static void Follow(Follower follower, bool teleport)
        {
            if (follower.body == null || follower.node == null) return;
            Transform t = follower.body.transform;
            Vector3 scale = follower.node.lossyScale;
            if (t.parent != null)
            {
                Vector3 parent = t.parent.lossyScale;
                scale = new Vector3(scale.x / parent.x, scale.y / parent.y, scale.z / parent.z);
            }
            if ((t.localScale - scale).sqrMagnitude > 1e-8f) t.localScale = scale;
            Place(follower.body, follower.node.position, follower.node.rotation, teleport);
        }

        static void Place(Rigidbody body, Vector3 position, Quaternion rotation, bool teleport)
        {
            if (teleport)
            {
                body.transform.SetPositionAndRotation(position, rotation);
                body.position = position;
                body.rotation = rotation;
                return;
            }
            body.MovePosition(position);
            body.MoveRotation(rotation);
        }

        /// <summary>Is something resting on the leaf?</summary>
        bool HasRiders()
        {
            Bounds bounds = leaf.shape.bounds;
            float top = bounds.max.y;
            var center = new Vector3(bounds.center.x, top + RiderReach * 0.5f, bounds.center.z);
            var halfExtents = new Vector3(bounds.extents.x, RiderReach * 0.5f, bounds.extents.z);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, found, Quaternion.identity, riderMask,
                                                   QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (found[i].bounds.min.y > top - RiderSink) return true;
            return false;
        }

        /// <summary>Is a body it mustn't crush under the leaf, within <paramref name="drop"/> of it?</summary>
        bool IsBlocked(float drop)
        {
            // A touch smaller, so bodies merely brushing its rim don't count. Riders already touching it are ignored by
            // the sweep.
            Bounds bounds = leaf.shape.bounds;
            var halfExtents = new Vector3(bounds.extents.x * 0.95f, bounds.extents.y, bounds.extents.z * 0.95f);
            return Physics.BoxCast(bounds.center, halfExtents, Vector3.down, Quaternion.identity, drop + SafetyMargin,
                                   riderMask, QueryTriggerInteraction.Ignore);
        }
    }
}
