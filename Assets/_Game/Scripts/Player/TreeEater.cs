using BounJam.Core;
using BounJam.Level;
using UnityEngine;

namespace BounJam.Player
{
    /// <summary>Lets the beaver eat the nearest standing tree within reach while Eat (E) is held.</summary>
    [RequireComponent(typeof(BeaverInput), typeof(PlayerMotor))]
    public class TreeEater : MonoBehaviour
    {
        [SerializeField, Tooltip("How far from the beaver's middle a trunk can be and still be eaten.")]
        float reach = 1.3f;

        readonly Collider[] nearby = new Collider[8];
        BeaverInput input;
        PlayerMotor motor;
        bool facingTree;

        /// <summary>The tree the beaver would eat right now, or null.</summary>
        public ChewableTree Target { get; private set; }
        public bool IsEating { get; private set; }

        void Awake()
        {
            input = GetComponent<BeaverInput>();
            motor = GetComponent<PlayerMotor>();
        }

        void OnDisable() => StopFacing();

        void Update()
        {
            Target = FindTarget();
            IsEating = Target != null && input.EatHeld;
            if (!IsEating)
            {
                StopFacing();
                return;
            }

            Vector3 toTree = Vector3.ProjectOnPlane(Target.transform.position - transform.position, Vector3.up);
            if (toTree.sqrMagnitude > 0.01f)
            {
                motor.FacingOverride = toTree.normalized;
                facingTree = true;
            }
            Target.Chew(Time.deltaTime, transform.position);
        }

        ChewableTree FindTarget()
        {
            Vector3 centre = transform.position + Vector3.up * 0.6f;
            int count = Physics.OverlapSphereNonAlloc(centre, reach, nearby, 1 << GameLayers.Tree, QueryTriggerInteraction.Ignore);
            ChewableTree nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var tree = nearby[i].GetComponentInParent<ChewableTree>();
                float distance = (nearby[i].ClosestPoint(centre) - centre).sqrMagnitude;
                if (tree != null && tree.IsStanding && distance < nearestDistance)
                {
                    nearest = tree;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        // Clears only the facing this component asked for, so the liquid gun can set its own.
        void StopFacing()
        {
            if (!facingTree)
                return;
            motor.FacingOverride = null;
            facingTree = false;
        }
    }
}
