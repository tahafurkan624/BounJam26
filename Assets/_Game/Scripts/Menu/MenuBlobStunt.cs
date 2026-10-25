using System.Collections;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The main menu's background loop: the liquid blob runs up the ramp, smashes into the wall at the top, hops off it
    /// and freezes in the air, then the crystal falls onto the ramp and rolls down to the bottom, where it melts and goes
    /// again. The blob's own controls are off; this drives it through <see cref="PlayerInputReader.MoveOverride"/>, whose
    /// directions the blob reads in <c>heading</c>'s frame (BlobMotor's camera), which faces up the ramp. If the blob
    /// ever ends up somewhere it shouldn't, it is put back at the start.
    /// </summary>
    public class MenuBlobStunt : MonoBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] BlobMotor motor;
        [SerializeField] BlobForm form;
        [SerializeField] BlobSkin skin;
        [SerializeField] BlobSplit split;
        [SerializeField] GroundGuard guard;
        [Tooltip("Faces up the ramp; the blob's movement is read in its frame.")]
        [SerializeField] Transform heading;
        [Tooltip("Where the blob waits before each run.")]
        [SerializeField] Transform start;
        [Tooltip("The foot of the wall at the top of the ramp: the blob runs at it.")]
        [SerializeField] Transform wall;
        [SerializeField] float idleTime = 1.2f;
        [SerializeField] float hopDelay = 0.1f;
        [SerializeField] float freezeDelay = 0.2f;
        [Tooltip("Spin given to the crystal as it starts down the ramp, rad/s.")]
        [SerializeField] float rollSpin = 5f;
        [SerializeField] float rollAngularDamping = 0.5f;
        [Tooltip("Extra pull down the slope while the crystal is on the ramp, m/s², so it never stalls there.")]
        [SerializeField] float rampAssist = 7f;
        [Tooltip("Where the ramp starts, m along the heading from it.")]
        [SerializeField] float rampFoot = -2.5f;
        [Tooltip("Longest a run or a roll may take before the blob is put back at the start, s.")]
        [SerializeField] float timeout = 7f;
        [Tooltip("Further than this from the start, the blob is put back.")]
        [SerializeField] float leash = 14f;

        Rigidbody body;

        void Awake() => body = motor.Body != null ? motor.Body : motor.GetComponent<Rigidbody>();

        void OnEnable()
        {
            input.enabled = false;
            input.MoveOverride = Vector2.zero;
            StartCoroutine(Loop());
        }

        IEnumerator Loop()
        {
            yield return new WaitForFixedUpdate();
            while (true)
            {
                yield return new WaitForSeconds(idleTime);
                yield return Run();
                yield return Roll();
                if (Lost()) yield return ResetToStart();
            }
        }

        // Up the ramp at full speed until the wall stops it.
        IEnumerator Run()
        {
            float began = Time.time;
            float fastest = 0f;
            while (Time.time - began < timeout)
            {
                Vector3 toWall = heading.InverseTransformDirection(wall.position - body.position);
                input.MoveOverride = new Vector2(toWall.x, toWall.z).normalized;
                float speed = Vector3.Dot(body.linearVelocity, heading.forward);
                fastest = Mathf.Max(fastest, speed);
                if (fastest > 3f && speed < fastest * 0.35f) break;
                yield return new WaitForFixedUpdate();
            }
            input.MoveOverride = Vector2.zero;
        }

        // Hop off the wall, freeze in the air (the roundest shape), and roll down.
        IEnumerator Roll()
        {
            yield return new WaitForSeconds(hopDelay);
            motor.RequestJump();
            yield return new WaitForSeconds(freezeDelay);
            for (int i = 0; i < 20 && !form.IsSolid; i++)
            {
                form.Freeze();
                if (!form.IsSolid) yield return new WaitForFixedUpdate();
            }
            if (!form.IsSolid) yield break;

            // Pure physics while solid: the motor would brake the crystal to a stop on the ramp.
            motor.enabled = false;
            body.angularDamping = rollAngularDamping;
            Vector3 down = -heading.forward;
            body.linearVelocity += down * 1.2f;
            body.angularVelocity = Vector3.Cross(Vector3.up, down) * rollSpin;

            float began = Time.time;
            float still = 0f;
            while (Time.time - began < timeout && still < 0.6f)
            {
                if (heading.InverseTransformPoint(body.position).z > rampFoot)
                {
                    body.AddForce(down * rampAssist, ForceMode.Acceleration);
                    body.AddTorque(Vector3.Cross(Vector3.up, down) * rampAssist, ForceMode.Acceleration);
                }
                still = body.linearVelocity.magnitude < 0.25f ? still + Time.fixedDeltaTime : 0f;
                if (Lost()) break;
                yield return new WaitForFixedUpdate();
            }
            yield return new WaitForSeconds(0.6f);
            form.Melt();
            motor.enabled = true;
        }

        bool Lost() => body.position.y < start.position.y - 2f || (body.position - start.position).sqrMagnitude > leash * leash;

        IEnumerator ResetToStart()
        {
            split.SetHidden(true);
            if (form.IsSolid) form.Melt();
            motor.enabled = true;
            skin.enabled = false;
            if (guard != null) guard.enabled = false;
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.position = start.position;
            body.transform.position = start.position;
            Physics.SyncTransforms();
            skin.enabled = true;
            if (guard != null) guard.enabled = true;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            split.SetHidden(false);
        }
    }
}
