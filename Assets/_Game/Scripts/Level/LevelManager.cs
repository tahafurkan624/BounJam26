using System;
using System.Collections;
using System.Collections.Generic;
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
        public string LevelTitle => levelTitle;
        public bool IsFinalLevel => isFinalLevel;
        public bool HasNextLevel => !string.IsNullOrEmpty(nextScene);
        /// <summary>
        /// While set, a completed level waits for <see cref="LoadNext"/> instead of moving on by itself (the glass UI's
        /// level-complete card sets it and offers the buttons).
        /// </summary>
        public bool HoldOnComplete { get; set; }
        /// <summary>Raised when the level title is shown at the start.</summary>
        public event Action<string> TitleShown;
        /// <summary>Raised when the level ends, Complete or Failed, with the message the HUD shows.</summary>
        public event Action<State, string> Ended;

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
            TitleShown?.Invoke(levelTitle);
        }

        void Update()
        {
            if (Current != State.Playing) return;
            if (ship != null && ship.transform.position.y < killHeight) Fail("The ship was lost!");
            else if (BlobFell()) Fail("You fell!");
        }

        /// <summary>Did the blob, or either half of it once a piercer pipe has cut it, fall below the kill height?</summary>
        bool BlobFell()
        {
            IReadOnlyList<BlobSplit> pieces = BlobSplit.All;
            if (pieces.Count == 0) return blob != null && blob.position.y < killHeight;
            for (int i = 0; i < pieces.Count; i++)
                if (pieces[i].transform.position.y < killHeight) return true;
            return false;
        }

        /// <summary>Ends the level as lost (hazards call this) and restarts it after a short pause.</summary>
        public void Fail(string reason)
        {
            if (Current != State.Playing) return;
            Current = State.Failed;
            if (hud != null) hud.ShowMessage(reason);
            Ended?.Invoke(State.Failed, reason);
            StartCoroutine(LoadAfter(SceneManager.GetActiveScene().name, failDelay));
        }

        public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().name);

        /// <summary>Loads the next level now (the level-complete card's button).</summary>
        public void LoadNext()
        {
            if (HasNextLevel) SceneManager.LoadScene(nextScene);
        }

        void OnDelivered()
        {
            if (Current != State.Playing || string.IsNullOrEmpty(nextScene)) return;
            Current = State.Complete;
            string message = isFinalLevel ? "All ships delivered!\nThanks for playing" : "Delivered!";
            if (hud != null) hud.ShowMessage(message);
            Ended?.Invoke(State.Complete, message);
            if (!HoldOnComplete) StartCoroutine(LoadAfter(nextScene, isFinalLevel ? completeDelay * 2f : completeDelay));
        }

        void OnRestartPressed(InputAction.CallbackContext _) => Restart();

        IEnumerator LoadAfter(string scene, float delay)
        {
            yield return new WaitForSeconds(delay);
            SceneManager.LoadScene(scene);
        }
    }
}
