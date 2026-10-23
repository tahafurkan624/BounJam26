using System.Collections.Generic;
using BounJam.Liquids;
using UnityEngine;

namespace BounJam.Player
{
    /// <summary>
    /// Sprays the selected liquid from the nozzle while Fire is held. The stream is aimed along an arc that
    /// lands on the crosshair (PlayerAim.AimPoint), and the beaver turns to face where it is spraying.
    /// </summary>
    // After PlayerMotor has turned the beaver, so the nozzle (its child) is aimed from this frame's final
    // pose. The motor picks up FacingOverride a frame later, which nobody can see.
    [DefaultExecutionOrder(1)]
    [RequireComponent(typeof(LiquidInventory), typeof(PlayerAim), typeof(PlayerMotor))]
    public class LiquidGun : MonoBehaviour
    {
        [SerializeField, Tooltip("Where the liquid leaves the beaver. Keep it inside the CharacterController.")]
        Transform nozzle;
        [SerializeField] LiquidStream streamPrefab;
        [SerializeField, Tooltip("Seconds the beaver keeps facing its aim after it stops spraying.")]
        float faceAimLinger = 0.3f;

        readonly Dictionary<LiquidType, LiquidStream> streams = new();
        BeaverInput input;
        PlayerLook look;
        LiquidInventory inventory;
        PlayerAim aim;
        PlayerMotor motor;
        LiquidStream activeStream;
        float lastSprayTime = float.NegativeInfinity;
        bool facingAim;

        public bool IsSpraying => activeStream != null;

        void Awake()
        {
            input = GetComponent<BeaverInput>();
            look = GetComponent<PlayerLook>();
            inventory = GetComponent<LiquidInventory>();
            aim = GetComponent<PlayerAim>();
            motor = GetComponent<PlayerMotor>();
        }

        void OnDisable()
        {
            SetActiveStream(null);
            ReleaseFacing();
        }

        void Update()
        {
            bool spraying = input.FireHeld && look.FireAllowed && inventory.CanFire;
            LiquidType liquid = inventory.Current;
            SetActiveStream(spraying ? StreamFor(liquid) : null);
            if (!spraying)
            {
                if (Time.time - lastSprayTime > faceAimLinger)
                    ReleaseFacing();
                return;
            }

            lastSprayTime = Time.time;
            Vector3 toTarget = aim.AimPoint - nozzle.position;
            float gravity = -Physics.gravity.y * liquid.GravityModifier;
            nozzle.rotation = Quaternion.LookRotation(LaunchDirection(toTarget, liquid.LaunchSpeed, gravity));

            Vector3 facing = Vector3.ProjectOnPlane(aim.AimPoint - transform.position, Vector3.up);
            if (facing.sqrMagnitude > 0.01f)
            {
                motor.FacingOverride = facing.normalized;
                facingAim = true;
            }
            inventory.Consume(liquid.FlowPerSecond * Time.deltaTime);
        }

        // Clears only the facing this gun asked for, so other systems can set their own.
        void ReleaseFacing()
        {
            if (!facingAim)
                return;
            motor.FacingOverride = null;
            facingAim = false;
        }

        void SetActiveStream(LiquidStream stream)
        {
            if (stream == activeStream)
                return;
            if (activeStream != null)
                activeStream.SetEmitting(false);
            if (stream != null)
                stream.SetEmitting(true);
            activeStream = stream;
        }

        LiquidStream StreamFor(LiquidType liquid)
        {
            if (!streams.TryGetValue(liquid, out LiquidStream stream))
            {
                stream = Instantiate(streamPrefab, nozzle, false);
                stream.Init(liquid);
                streams.Add(liquid, stream);
            }
            return stream;
        }

        /// <summary>
        /// The lower of the two launch directions that land a particle on the target, or 45° toward it
        /// (the longest throw) when the target is out of range.
        /// </summary>
        static Vector3 LaunchDirection(Vector3 toTarget, float speed, float gravity)
        {
            var flat = new Vector3(toTarget.x, 0f, toTarget.z);
            float distance = flat.magnitude;
            if (distance < 0.01f || gravity < 0.01f)
                return toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector3.forward;

            float speedSquared = speed * speed;
            float discriminant = speedSquared * speedSquared
                - gravity * (gravity * distance * distance + 2f * toTarget.y * speedSquared);
            float angle = discriminant >= 0f
                ? Mathf.Atan((speedSquared - Mathf.Sqrt(discriminant)) / (gravity * distance))
                : 45f * Mathf.Deg2Rad;
            return flat / distance * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
        }
    }
}
