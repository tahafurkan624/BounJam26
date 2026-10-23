using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BounJam.Liquids
{
    /// <summary>Testing aid: logs the total liquid this object has received, once a second while it changes.</summary>
    [RequireComponent(typeof(LiquidTarget))]
    public class LiquidHitProbe : MonoBehaviour, ILiquidReceiver
    {
        readonly Dictionary<LiquidType, float> received = new();
        float total;
        float loggedTotal;
        float nextLogTime;

        public void OnLiquidHit(in LiquidHit hit)
        {
            received[hit.Liquid] = received.GetValueOrDefault(hit.Liquid) + hit.Amount;
            total += hit.Amount;
        }

        void Update()
        {
            if (total == loggedTotal || Time.time < nextLogTime)
                return;
            loggedTotal = total;
            nextLogTime = Time.time + 1f;
            string amounts = string.Join(", ", received.Select(pair => $"{pair.Value:0.00} {pair.Key.DisplayName}"));
            Debug.Log($"[BounJam] {name} has received {amounts}", this);
        }
    }
}
