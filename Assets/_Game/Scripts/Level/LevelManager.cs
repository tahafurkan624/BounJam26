using System.Collections.Generic;
using System.Linq;
using BounJam.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BounJam.Level
{
    /// <summary>
    /// Runs one level. It is complete when every Fire is out, and fails when the beaver falls below the
    /// level or something calls Fail (pits, Fire Souls). R restarts at any time. One per level scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelManager : MonoBehaviour
    {
        public enum Outcome
        {
            Playing,
            Complete,
            Failed,
        }

        [SerializeField, Tooltip("Falling below this height fails the level.")]
        float fallFailHeight = -15f;

        readonly List<Fire> fires = new();
        BeaverInput player;
        InputAction restart;

        public static LevelManager Current { get; private set; }
        public Outcome State { get; private set; }
        public string FailReason { get; private set; }
        public int FireCount => fires.Count;
        public int FiresLeft { get; private set; }
        public float PlayTime { get; private set; }
        public bool HasNextLevel
        {
            get
            {
                int index = SceneManager.GetActiveScene().buildIndex;
                return index >= 0 && index + 1 < SceneManager.sceneCountInBuildSettings;
            }
        }

        void Awake()
        {
            if (Current != null)
                Debug.LogError("[BounJam] There is more than one LevelManager in this scene.", this);
            Current = this;
            restart = InputSystem.actions.FindAction("Player/Restart", throwIfNotFound: true);
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        // The player rig is spawned in PlayerSpawn.Awake, so it exists by now.
        void Start()
        {
            player = FindAnyObjectByType<BeaverInput>();
            fires.AddRange(FindObjectsByType<Fire>(FindObjectsSortMode.None));
            foreach (Fire fire in fires)
                fire.Extinguished += OnFireExtinguished;
            FiresLeft = fires.Count;
            if (fires.Count == 0)
                Debug.LogWarning("[BounJam] This level has no fires, so it can never be completed.", this);
        }

        void Update()
        {
            if (restart.WasPressedThisFrame())
            {
                Restart();
                return;
            }
            if (State != Outcome.Playing)
                return;
            PlayTime += Time.deltaTime;
            if (player != null && player.transform.position.y < fallFailHeight)
                Fail("You fell.");
        }

        void OnFireExtinguished(Fire fire)
        {
            FiresLeft = fires.Count(f => f.IsBurning);
            if (FiresLeft == 0 && State == Outcome.Playing)
                End(Outcome.Complete);
        }

        /// <summary>Ends the level as failed, e.g. from a pit or a Fire Soul. Ignored once the level is over.</summary>
        public void Fail(string reason)
        {
            if (State != Outcome.Playing)
                return;
            FailReason = reason;
            End(Outcome.Failed);
        }

        void End(Outcome outcome)
        {
            State = outcome;
            if (player != null)
                player.enabled = false; // the beaver stops obeying input but still falls
            Cursor.lockState = CursorLockMode.None; // free the mouse for the end screen's buttons
            Debug.Log(outcome == Outcome.Complete ? "[BounJam] Level complete." : $"[BounJam] Level failed: {FailReason}");
        }

        public void Restart() => LoadLevel(SceneManager.GetActiveScene().buildIndex);

        public void LoadNextLevel()
        {
            if (HasNextLevel)
                LoadLevel(SceneManager.GetActiveScene().buildIndex + 1);
        }

        static void LoadLevel(int buildIndex)
        {
            if (buildIndex < 0)
                Debug.LogError("[BounJam] Add this scene to Build Settings so it can be reloaded.");
            else
                SceneManager.LoadScene(buildIndex);
        }
    }
}
