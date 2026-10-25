using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BounJam
{
    /// <summary>
    /// Hides the top platforms (<see cref="LevelPiece.Kind.TopPlatform"/>) that get between the camera and the blob
    /// while it's on a floor below them: any above one of its pieces that the line from the camera to that piece passes
    /// through, or that the piece is under. A hidden platform still casts its shadow and still holds its collider, so
    /// it's only out of sight. Every loaded scene with top platforms gets one on the main camera, so the level scenes
    /// need no changes.
    /// </summary>
    public class UpperFloorHider : MonoBehaviour
    {
        [Tooltip("How far a platform's underside must be above a blob piece's centre before it can be hidden, m.")]
        [SerializeField] float clearance = 0.3f;
        [Tooltip("Room kept round each platform, m: it hides a little before the blob or the view reaches it.")]
        [SerializeField] float margin = 1f;
        [Tooltip("How long a platform stays hidden after it's no longer in the way, s, so it doesn't flicker at the edge.")]
        [SerializeField] float showDelay = 0.3f;

        sealed class Floor
        {
            public Bounds Bounds;
            public Renderer[] Renderers;
            public ShadowCastingMode[] Shadows;
            public bool Hidden;
            public float InTheWayUntil;
        }

        readonly List<Floor> floors = new List<Floor>();
        static bool hooked;

        // Statics survive entering play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => hooked = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (!hooked)
            {
                SceneManager.sceneLoaded += (_, _) => Install();
                hooked = true;
            }
            Install();
        }

        static void Install()
        {
            Camera main = Camera.main;
            if (main == null) return;
            if (main.TryGetComponent(out UpperFloorHider hider)) hider.Rescan();
            else if (HasTopPlatforms()) main.gameObject.AddComponent<UpperFloorHider>();
        }

        static bool HasTopPlatforms()
        {
            foreach (LevelPiece piece in FindObjectsByType<LevelPiece>(FindObjectsSortMode.None))
                if (piece.PieceKind == LevelPiece.Kind.TopPlatform) return true;
            return false;
        }

        void Start() => Rescan();

        void OnDisable()
        {
            foreach (Floor floor in floors) SetHidden(floor, false);
        }

        /// <summary>Finds the top platforms in the loaded scenes again, showing the ones it had hidden.</summary>
        public void Rescan()
        {
            foreach (Floor floor in floors) SetHidden(floor, false);
            floors.Clear();
            foreach (LevelPiece piece in FindObjectsByType<LevelPiece>(FindObjectsSortMode.None))
            {
                if (piece.PieceKind != LevelPiece.Kind.TopPlatform) continue;
                var collider = piece.GetComponentInChildren<Collider>();
                var renderers = piece.GetComponentsInChildren<Renderer>();
                if (collider == null || renderers.Length == 0) continue;
                var shadows = new ShadowCastingMode[renderers.Length];
                for (int i = 0; i < renderers.Length; i++) shadows[i] = renderers[i].shadowCastingMode;
                floors.Add(new Floor { Bounds = collider.bounds, Renderers = renderers, Shadows = shadows });
            }
        }

        // After the camera has moved this frame.
        void LateUpdate()
        {
            Vector3 eye = transform.position;
            float now = Time.time;
            IReadOnlyList<BlobSplit> pieces = BlobSplit.All;
            foreach (Floor floor in floors)
            {
                foreach (BlobSplit piece in pieces)
                {
                    if (piece != null && InTheWay(floor.Bounds, eye, piece.transform.position))
                    {
                        floor.InTheWayUntil = now + showDelay;
                        break;
                    }
                }
                SetHidden(floor, now < floor.InTheWayUntil);
            }
        }

        bool InTheWay(Bounds bounds, Vector3 eye, Vector3 blob)
        {
            if (bounds.min.y < blob.y + clearance) return false;
            Bounds wide = bounds;
            wide.Expand(new Vector3(margin * 2f, 0f, margin * 2f));
            // Under it.
            if (blob.x > wide.min.x && blob.x < wide.max.x && blob.z > wide.min.z && blob.z < wide.max.z) return true;
            // Or the view down to it goes through it.
            Vector3 toBlob = blob - eye;
            float distance = toBlob.magnitude;
            return distance > 0f && wide.IntersectRay(new Ray(eye, toBlob / distance), out float hit) && hit < distance;
        }

        static void SetHidden(Floor floor, bool hidden)
        {
            if (floor.Hidden == hidden) return;
            floor.Hidden = hidden;
            for (int i = 0; i < floor.Renderers.Length; i++)
            {
                if (floor.Renderers[i] == null) continue;
                floor.Renderers[i].shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : floor.Shadows[i];
            }
        }
    }
}
