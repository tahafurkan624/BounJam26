using UnityEngine;

namespace BounJam.Liquids
{
    /// <summary>All the particles of one liquid that hit one collider in one frame, combined.</summary>
    public readonly struct LiquidHit
    {
        public readonly LiquidType Liquid;
        public readonly Collider Collider;
        public readonly Vector3 Point;     // average impact point
        public readonly Vector3 Normal;    // average surface normal, normalised
        public readonly Vector3 Velocity;  // average particle velocity at impact
        public readonly float Amount;      // liquid units delivered this frame

        public LiquidHit(LiquidType liquid, Collider collider, Vector3 point, Vector3 normal, Vector3 velocity, float amount)
        {
            Liquid = liquid;
            Collider = collider;
            Point = point;
            Normal = normal;
            Velocity = velocity;
            Amount = amount;
        }
    }

    /// <summary>Reacts to liquid hitting its object. Needs a LiquidTarget on the same object or a parent.</summary>
    public interface ILiquidReceiver
    {
        void OnLiquidHit(in LiquidHit hit);
    }
}
