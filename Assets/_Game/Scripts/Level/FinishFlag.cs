using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The harbor's finish flag: it waits lowered on its pole and is hoisted, then waves, once the level is
    /// completed. Drives the Raise trigger of the flag's animator (FinishFlag.controller).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FinishFlag : MonoBehaviour
    {
        [Tooltip("The level whose completion raises the flag. Empty: the one in the scene.")]
        [SerializeField] LevelManager level;

        static readonly int Raise = Animator.StringToHash("Raise");

        Animator animator;

        void Awake()
        {
            animator = GetComponent<Animator>();
            if (level == null) level = FindAnyObjectByType<LevelManager>();
        }

        void OnEnable()
        {
            if (level != null) level.Ended += OnLevelEnded;
        }

        void OnDisable()
        {
            if (level != null) level.Ended -= OnLevelEnded;
        }

        void OnLevelEnded(LevelManager.State state, string message)
        {
            if (state == LevelManager.State.Complete) animator.SetTrigger(Raise);
        }
    }
}
