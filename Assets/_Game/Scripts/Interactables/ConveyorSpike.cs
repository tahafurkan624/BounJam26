using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// One metal spike on a <see cref="Conveyor"/>'s belt; the conveyor creates and moves it. The rod points along this
    /// object's +X from its base. The whole spike glows while it's inside the jelly (the cue to freeze now; the rod
    /// shows through the clear jelly where the ship on top hides the tip) and stays lit while a crystal holds on to it
    /// (see <see cref="BlobGrip"/>).
    /// </summary>
    public class ConveyorSpike : MonoBehaviour
    {
        [Tooltip("How quickly the glow fades in and out, 1/s.")]
        [SerializeField] float glowRate = 10f;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        Renderer[] glowing;
        MaterialPropertyBlock block;
        Vector3 localBase;
        Color glow = Color.black;
        float lastEngulfedTime = float.NegativeInfinity;
        int holders;

        public Conveyor Conveyor { get; private set; }
        public Rigidbody Body { get; private set; }
        /// <summary>Length of the rod from its base to the end of the tip, m.</summary>
        public float Reach { get; private set; }
        /// <summary>Where the belt puts the spike at the end of the current physics step.</summary>
        public Vector3 TargetPosition { get; private set; }
        public Quaternion TargetRotation { get; private set; }
        /// <summary>True while it's inside a liquid blob (reported every physics step, see <see cref="ReportEngulfed"/>).</summary>
        public bool IsEngulfed => Time.fixedTime - lastEngulfedTime <= Time.fixedDeltaTime * 1.5f;
        public bool IsHeld => holders > 0;

        /// <summary>Called once by the conveyor that builds it.</summary>
        public void Setup(Conveyor conveyor, Renderer[] renderers, Vector3 rodBase, float reach)
        {
            Conveyor = conveyor;
            glowing = renderers;
            localBase = rodBase;
            Reach = reach;
            Body = GetComponent<Rigidbody>();
        }

        /// <summary>A point on the rod where the physics has it now: 0 = base, 1 = the end of the tip.</summary>
        public Vector3 PointAlong(float share)
            => Body.position + Body.rotation * (localBase + Vector3.right * (Reach * share));

        /// <summary>Moves it with the belt during the next physics step, or straight there.</summary>
        public void MoveTo(Vector3 position, Quaternion rotation, bool teleport)
        {
            TargetPosition = position;
            TargetRotation = rotation;
            if (teleport)
            {
                Body.position = position;
                Body.rotation = rotation;
                transform.SetPositionAndRotation(position, rotation);
                return;
            }
            Body.MovePosition(position);
            Body.MoveRotation(rotation);
        }

        /// <summary>A liquid blob found the spike inside it this physics step.</summary>
        public void ReportEngulfed() => lastEngulfedTime = Time.fixedTime;

        /// <summary>A crystal grabbed it (true) or let go (false).</summary>
        public void SetHeld(bool held) => holders = Mathf.Max(0, holders + (held ? 1 : -1));

        void Awake() => block = new MaterialPropertyBlock();

        void Update()
        {
            if (glowing == null) return;
            float flicker = 0.8f + 0.2f * Mathf.Sin(Time.time * 12f);
            Color target = IsHeld ? Conveyor.HeldGlow : IsEngulfed ? Conveyor.EngulfedGlow * flicker : Color.black;
            glow = Color.Lerp(glow, target, 1f - Mathf.Exp(-glowRate * Time.deltaTime));
            foreach (Renderer part in glowing)
            {
                part.GetPropertyBlock(block);
                block.SetColor(EmissionColor, glow);
                part.SetPropertyBlock(block);
            }
        }
    }
}
