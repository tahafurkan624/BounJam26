using UnityEngine;

namespace BounJam.Liquids
{
    public enum LiquidKind
    {
        Water,
        Sticky,
        Bouncy,
    }

    /// <summary>
    /// One kind of liquid the beaver can spray. Amounts are in liquid units: ammo, what each hit delivers
    /// and every effect threshold use the same units, so changing particle density only changes the look.
    /// </summary>
    [CreateAssetMenu(menuName = "BounJam/Liquid Type", fileName = "Liquid")]
    public class LiquidType : ScriptableObject
    {
        [SerializeField] string displayName = "Water";
        [SerializeField] LiquidKind kind;
        [SerializeField] Color color = new(0.35f, 0.65f, 1f, 0.85f);

        [Header("Supply")]
        [SerializeField] bool isInfinite;
        [SerializeField, Min(0f), Tooltip("Most units the beaver can carry. Ignored when infinite.")]
        float capacity = 5f;
        [SerializeField, Min(0.01f), Tooltip("Units sprayed per second while firing.")]
        float flowPerSecond = 1f;

        [Header("Stream")]
        [SerializeField, Min(1f), Tooltip("Visual density only; does not change how much liquid lands.")]
        float particlesPerSecond = 60f;
        [SerializeField, Min(0.1f)] float launchSpeed = 14f;
        [SerializeField, Tooltip("Multiplies Physics.gravity for the particles; lower flies flatter and further.")]
        float gravityModifier = 0.6f;
        [SerializeField, Min(0.01f)] float particleSize = 0.15f;

        [Header("Effects")]
        [SerializeField, Min(0f), Tooltip("Impulse per unit given to rigidbodies on the Pushable layer.")]
        float pushPerUnit = 12f;

        public string DisplayName => displayName;
        public LiquidKind Kind => kind;
        public Color Color => color;
        public bool IsInfinite => isInfinite;
        public float Capacity => capacity;
        public float FlowPerSecond => flowPerSecond;
        public float ParticlesPerSecond => particlesPerSecond;
        public float LaunchSpeed => launchSpeed;
        public float GravityModifier => gravityModifier;
        public float ParticleSize => particleSize;
        public float PushPerUnit => pushPerUnit;
        public float UnitsPerParticle => flowPerSecond / particlesPerSecond;
    }
}
