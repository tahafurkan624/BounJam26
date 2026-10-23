using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BounJam
{
    /// <summary>
    /// Reloads the active scene on the Restart action, or when the player falls below the kill height.
    /// </summary>
    public class LevelRestarter : MonoBehaviour
    {
        [SerializeField] Transform player;
        [Tooltip("Falling below this world height restarts the level.")]
        [SerializeField] float killHeight = -10f;

        InputAction restart;

        void OnEnable()
        {
            if (InputSystem.actions == null) return;
            restart = InputSystem.actions.FindAction("Player/Restart", throwIfNotFound: true);
            restart.performed += OnRestart;
        }

        void OnDisable()
        {
            if (restart != null) restart.performed -= OnRestart;
        }

        void Update()
        {
            if (player != null && player.position.y < killHeight) Restart();
        }

        void OnRestart(InputAction.CallbackContext _) => Restart();

        public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
