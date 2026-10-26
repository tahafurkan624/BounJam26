#if UNITY_EDITOR
using System;
using Unity.Cinemachine;
using UnityEngine;

namespace BounJam.Trailer
{
    /// <summary>
    /// Takes the main camera away from Cinemachine for a trailer move: static, dolly, orbit or follow, each eased over
    /// game time, with an optional handheld sway. Every move aims at a point; a camera that would end up inside or
    /// behind level geometry (seen from that point) is pulled in front of it (<see cref="avoidWalls"/>).
    /// <see cref="Gameplay"/> hands the camera back to the game's own rig, and <see cref="Release"/> undoes everything.
    /// </summary>
    public class TrailerCamera : MonoBehaviour
    {
        CinemachineBrain brain;
        Camera cam;
        float baseFov;
        Func<float, (Vector3 position, Vector3 look, float fov)> rig;
        float rigStart;
        float sway;
        Vector3 followVelocity;
        Vector3 followPosition;
        bool followPlaced;
        bool warnedWall;
        static int wallMask = -1;

        /// <summary>Pull the camera in front of walls between it and what it looks at.</summary>
        public bool avoidWalls = true;

        public Camera Camera => cam;
        /// <summary>Where the camera is looking this frame (the move's aim point).</summary>
        public Vector3 Look { get; private set; }

        public static TrailerCamera Attach()
        {
            Camera main = Camera.main;
            if (main == null) return null;
            if (!main.TryGetComponent(out TrailerCamera trailer)) trailer = main.gameObject.AddComponent<TrailerCamera>();
            trailer.cam = main;
            trailer.brain = main.GetComponent<CinemachineBrain>();
            trailer.baseFov = main.fieldOfView;
            trailer.warnedWall = false;
            trailer.avoidWalls = true;
            return trailer;
        }

        /// <summary>The game's own Cinemachine camera.</summary>
        public void Gameplay()
        {
            rig = null;
            if (brain != null) brain.enabled = true;
        }

        public void Release()
        {
            Gameplay();
            sway = 0f;
            if (cam != null) cam.fieldOfView = baseFov;
        }

        /// <summary>Small slow rotation noise, degrees, as if hand held.</summary>
        public void Sway(float degrees) => sway = degrees;

        /// <summary>Any move: a function of seconds since it started giving position, aim point and field of view.</summary>
        public void Custom(Func<float, (Vector3 position, Vector3 look, float fov)> move)
        {
            if (brain != null) brain.enabled = false;
            rig = move;
            rigStart = Time.time;
            followPlaced = false;
            Apply();
        }

        public void Static(Vector3 position, Vector3 lookAt, float fov = 40f) => Custom(_ => (position, lookAt, fov));

        /// <summary>From one point to another over <paramref name="duration"/>, eased, looking at a moving or fixed point.</summary>
        public void Dolly(Vector3 from, Vector3 to, Func<Vector3> lookAt, float duration, float fovFrom = 40f, float fovTo = 40f)
        {
            Custom(t =>
            {
                float k = Ease(Mathf.Clamp01(t / duration));
                return (Vector3.Lerp(from, to, k), lookAt(), Mathf.Lerp(fovFrom, fovTo, k));
            });
        }

        public void Dolly(Vector3 from, Vector3 to, Vector3 lookAt, float duration, float fovFrom = 40f, float fovTo = 40f) =>
            Dolly(from, to, () => lookAt, duration, fovFrom, fovTo);

        /// <summary>
        /// Round a centre at <paramref name="radius"/> and <paramref name="height"/> above it, from one angle to another
        /// (degrees: 0 = on the -Z side looking toward +Z, 90 = on the +X side), looking at the centre raised by
        /// <paramref name="lookHeight"/>.
        /// </summary>
        public void Orbit(Func<Vector3> center, float radius, float height, float fromDegrees, float toDegrees, float duration,
                          float fov = 40f, float lookHeight = 0f, bool ease = true)
        {
            Custom(t =>
            {
                float k = Mathf.Clamp01(t / duration);
                if (ease) k = Ease(k);
                float angle = Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, k);
                Vector3 c = center();
                Vector3 position = c + new Vector3(Mathf.Sin(angle) * radius, height, -Mathf.Cos(angle) * radius);
                return (position, c + Vector3.up * lookHeight, fov);
            });
        }

        public void Orbit(Vector3 center, float radius, float height, float fromDegrees, float toDegrees, float duration,
                          float fov = 40f, float lookHeight = 0f, bool ease = true) =>
            Orbit(() => center, radius, height, fromDegrees, toDegrees, duration, fov, lookHeight, ease);

        /// <summary>
        /// Chases a target from <paramref name="offset"/> (world space), smoothed over <paramref name="smoothTime"/> s,
        /// looking at the target plus <paramref name="lookOffset"/>. The offset can drift to <paramref name="offsetTo"/>
        /// over <paramref name="duration"/>.
        /// </summary>
        public void Follow(Func<Vector3> target, Vector3 offset, Vector3 lookOffset, float smoothTime = 0.35f, float fov = 40f,
                           Vector3? offsetTo = null, float duration = 1f)
        {
            Custom(t =>
            {
                Vector3 off = offsetTo.HasValue ? Vector3.Lerp(offset, offsetTo.Value, Ease(Mathf.Clamp01(t / duration))) : offset;
                Vector3 goal = target() + off;
                if (!followPlaced)
                {
                    followPosition = goal;
                    followVelocity = Vector3.zero;
                    followPlaced = true;
                }
                followPosition = Vector3.SmoothDamp(followPosition, goal, ref followVelocity, smoothTime, Mathf.Infinity, Time.deltaTime);
                return (followPosition, target() + lookOffset, fov);
            });
        }

        public void Follow(Transform target, Vector3 offset, Vector3 lookOffset, float smoothTime = 0.35f, float fov = 40f,
                           Vector3? offsetTo = null, float duration = 1f) =>
            Follow(() => target.position, offset, lookOffset, smoothTime, fov, offsetTo, duration);

        // After Cinemachine and after everything moved this frame.
        void LateUpdate() => Apply();

        void Apply()
        {
            if (rig == null || cam == null) return;
            (Vector3 position, Vector3 look, float fov) = rig(Time.time - rigStart);
            if (avoidWalls) position = ClearOfWalls(position, look);
            Look = look;
            Vector3 view = look - position;
            Quaternion rotation = view.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(view) : transform.rotation;
            if (sway > 0f)
            {
                float t = Time.time * 0.35f;
                rotation *= Quaternion.Euler((Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * sway,
                                             (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * sway,
                                             (Mathf.PerlinNoise(t, 2.1f) - 0.5f) * sway);
            }
            transform.SetPositionAndRotation(position, rotation);
            cam.fieldOfView = fov;
        }

        Vector3 ClearOfWalls(Vector3 position, Vector3 look)
        {
            if (wallMask == -1) wallMask = LayerMask.GetMask("Default", "Environment");
            const float radius = 0.2f;
            Vector3 toCamera = position - look;
            float distance = toCamera.magnitude;
            if (distance < 0.3f) return position;
            Vector3 direction = toCamera / distance;
            // Only a camera that is itself inside geometry is moved (something in between just partly hides the view):
            // it steps toward its aim point until it is clear.
            if (!Physics.CheckSphere(position, radius, wallMask, QueryTriggerInteraction.Ignore)) return position;
            Collider inside = null;
            var overlaps = Physics.OverlapSphere(position, radius, wallMask, QueryTriggerInteraction.Ignore);
            if (overlaps.Length > 0) inside = overlaps[0];
            Vector3 clear = position;
            for (float back = 0.1f; back < distance - 0.3f; back += 0.1f)
            {
                clear = position - direction * back;
                if (!Physics.CheckSphere(clear, radius, wallMask, QueryTriggerInteraction.Ignore)) break;
            }
            if (!warnedWall)
            {
                warnedWall = true;
                string shot = TrailerDirector.Current != null && TrailerDirector.Current.Shot != null ? TrailerDirector.Current.Shot.id : "?";
                string name = inside != null ? inside.name : "geometry";
                Debug.LogWarning($"[Trailer] {shot}: camera was inside {name}; moved {Vector3.Distance(clear, position):0.0} m toward its subject");
                TrailerDirector.Current?.Mark("camera inside " + name);
            }
            return clear;
        }

        static float Ease(float k) => k * k * (3f - 2f * k);
    }
}
#endif
