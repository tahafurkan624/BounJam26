using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace BounJam.Level
{
    /// <summary>
    /// A tree the beaver can eat through. Once chewed for long enough, the trunk falls away from the beaver
    /// and stays down, where it blocks liquid (it is on the Tree layer) and the player.
    /// </summary>
    public class ChewableTree : MonoBehaviour
    {
        enum State
        {
            Standing,
            Falling,
            Fallen,
        }

        [SerializeField, Min(0.1f)] float secondsToEat = 1.5f;
        [SerializeField, Tooltip("The part that falls. Its origin sits at the centre of the cut.")]
        Transform trunk;
        [SerializeField, Min(0f), Tooltip("The trunk tips over its edge, so it lands on the ground rather than in it.")]
        float trunkRadius = 0.25f;
        [SerializeField, Tooltip("Degrees from upright where the trunk comes to rest; tune it so the canopy touches the ground.")]
        float fallAngle = 76f;
        [SerializeField, Min(0.1f)] float fallTime = 1.2f;
        [SerializeField] ParticleSystem chips;
        [SerializeField] UnityEvent onFelled;

        State state;
        float eaten;
        float lastChewTime = float.NegativeInfinity;
        Quaternion trunkRestRotation;

        public bool IsStanding => state == State.Standing;
        public UnityEvent Felled => onFelled;

        void Awake() => trunkRestRotation = trunk.localRotation;

        /// <summary>Chews for some seconds. When eaten through, the tree falls away from <paramref name="eaterPosition"/>.</summary>
        public void Chew(float seconds, Vector3 eaterPosition)
        {
            if (!IsStanding)
                return;
            eaten += seconds;
            lastChewTime = Time.time;
            chips.Emit(1);
            if (eaten < secondsToEat)
                return;

            Vector3 away = Vector3.ProjectOnPlane(transform.position - eaterPosition, Vector3.up);
            StartCoroutine(Fall(away.sqrMagnitude > 0.0001f ? away.normalized : transform.forward));
        }

        // A wobble that grows as the beaver gets through the trunk.
        void Update()
        {
            if (!IsStanding)
                return;
            bool beingChewed = Time.time - lastChewTime < 0.1f;
            float wobble = beingChewed ? 2.5f * (eaten / secondsToEat) * Mathf.Sin(Time.time * 40f) : 0f;
            trunk.localRotation = trunkRestRotation * Quaternion.Euler(wobble, 0f, wobble * 0.5f);
        }

        IEnumerator Fall(Vector3 direction)
        {
            state = State.Falling;
            trunk.localRotation = trunkRestRotation;
            chips.Emit(15);
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            Vector3 hinge = trunk.position + direction * trunkRadius;

            // Speeds up as it goes over, like something toppling under gravity.
            float angle = 0f;
            for (float t = 0f; t < fallTime; t += Time.deltaTime)
            {
                float progress = t / fallTime;
                float next = fallAngle * progress * progress;
                trunk.RotateAround(hinge, axis, next - angle);
                angle = next;
                yield return null;
            }
            trunk.RotateAround(hinge, axis, fallAngle - angle);

            state = State.Fallen;
            Debug.Log($"[BounJam] {name} fell.", this);
            onFelled.Invoke();
        }
    }
}
