using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BounJam
{
    /// <summary>
    /// Runs one level: shows its title, completes it when the ship is delivered to the dock, fails it when the ship
    /// or the blob is lost (fell below the kill height, or a hazard reported it), and moves on by itself: the next
    /// level after a short celebration, or a restart after a short pause. Restart (R) reloads at any time.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public enum State { Playing, Complete, Failed }

        [SerializeField] string levelTitle = "Level";
        [Tooltip("Scene loaded after this level is completed. Empty: this level never completes (sandbox).")]
        [SerializeField] string nextScene;
        [Tooltip("True on the last level: completing it shows the ending, then starts over from the next scene.")]
        [SerializeField] bool isFinalLevel;

        [SerializeField] Transform blob;
        [SerializeField] ShipBuoyancy ship;
        [SerializeField] ShipDock dock;
        [SerializeField] LevelHud hud;

        [Tooltip("Falling below this world height loses the blob or the ship.")]
        [SerializeField] float killHeight = -5f;
        [Tooltip("Celebration time before the next level loads, s.")]
        [SerializeField] float completeDelay = 2f;
        [Tooltip("Pause on a lost ship before the level restarts, s.")]
        [SerializeField] float failDelay = 1.5f;

        InputAction restart;

        public State Current { get; private set; } = State.Playing;

        void OnEnable()
        {
            if (dock != null) dock.Delivered += OnDelivered;
            if (InputSystem.actions == null) return;
            restart = InputSystem.actions.FindAction("Player/Restart", throwIfNotFound: true);
            restart.performed += OnRestartPressed;
        }

        void OnDisable()
        {
            if (dock != null) dock.Delivered -= OnDelivered;
            if (restart != null) restart.performed -= OnRestartPressed;
        }

        void Start()
        {
            if (hud != null) hud.ShowTitle(levelTitle);
        }

        void Update()
        {
            if (Current != State.Playing) return;
            if (ship != null && ship.transform.position.y < killHeight) Fail("The ship was lost!");
            else if (blob != null && blob.position.y < killHeight) Fail("You fell!");
        }

        /// <summary>Ends the level as lost (hazards call this) and restarts it after a short pause.</summary>
        public void Fail(string reason)
        {
            if (Current != State.Playing) return;
            Current = State.Failed;
            if (hud != null) hud.ShowMessage(reason);
            StartCoroutine(LoadAfter(SceneManager.GetActiveScene().name, failDelay));
        }

        public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);

        void OnDelivered()
        {
            if (Current != State.Playing || string.IsNullOrEmpty(nextScene)) return;
            Current = State.Complete;
            if (hud != null) hud.ShowMessage(isFinalLevel ? "All ships delivered!\nThanks for playing" : "Delivered!");
            StartCoroutine(LoadAfter(nextScene, isFinalLevel ? completeDelay * 2f : completeDelay));
        }

        void OnRestartPressed(InputAction.CallbackContext _) => Restart();

        IEnumerator LoadAfter(string scene, float delay)
        {
            yield return new WaitForSeconds(delay);
            SceneManager.LoadScene(scene);
        }
    }
}
