using UnityEngine;

namespace BounJam.Liquids
{
    /// <summary>
    /// Put on the root of anything that reacts to liquid (fires, buttons, Fire Souls). Hits on any collider
    /// under it go to every ILiquidReceiver on it or its children. Its colliders must not be triggers, since
    /// particles pass through triggers; use the LiquidHitbox layer for a hitbox that blocks nothing else.
    /// </summary>
    [DisallowMultipleComponent]
    public class LiquidTarget : MonoBehaviour
    {
        ILiquidReceiver[] receivers;

        void Awake() => receivers = GetComponentsInChildren<ILiquidReceiver>(true);

        public void Receive(in LiquidHit hit)
        {
            foreach (ILiquidReceiver receiver in receivers)
            {
                // Skip receivers destroyed, disabled or deactivated since Awake, e.g. a fire already put out.
                if (receiver is Behaviour behaviour && (behaviour == null || !behaviour.isActiveAndEnabled))
                    continue;
                receiver.OnLiquidHit(hit);
            }
        }
    }
}
