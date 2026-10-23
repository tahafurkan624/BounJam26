using System;
using BounJam.Liquids;
using UnityEngine;
using UnityEngine.Events;

namespace BounJam.Level
{
    /// <summary>
    /// A fire to put out. Liquid that fights fire (water) shrinks it as it lands on the hitbox, and it goes
    /// out once enough has landed. The level is complete when every fire in it is out.
    /// </summary>
    [RequireComponent(typeof(LiquidTarget))]
    public class Fire : MonoBehaviour, ILiquidReceiver
    {
        [SerializeField, Min(0.1f), Tooltip("Units of water it takes to put out. Water sprays 1 unit a second.")]
        float waterToExtinguish = 2f;
        [SerializeField] ParticleSystem flames;
        [SerializeField] ParticleSystem steam;
        [SerializeField] Light glow;
        [SerializeField, Tooltip("Turned off once the fire is out, so liquid passes through.")]
        Collider hitbox;
        [SerializeField] UnityEvent onExtinguished;

        float remaining;
        float flameSize;
        float flameRate;
        float glowIntensity;
        float flickerSeed;

        public event Action<Fire> Extinguished;
        public bool IsBurning => remaining > 0f;
        /// <summary>1 at full strength, falling to 0 as it goes out.</summary>
        public float Strength => remaining / waterToExtinguish;

        void Awake()
        {
            remaining = waterToExtinguish;
            flameSize = flames.main.startSizeMultiplier;
            flameRate = flames.emission.rateOverTimeMultiplier;
            glowIntensity = glow.intensity;
            flickerSeed = UnityEngine.Random.value * 100f;
        }

        void Update()
        {
            if (!IsBurning)
                return;
            float flicker = 0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 6f, flickerSeed);
            glow.intensity = glowIntensity * Mathf.Lerp(0.3f, 1f, Strength) * flicker;
        }

        public void OnLiquidHit(in LiquidHit hit)
        {
            float water = hit.Amount * hit.Liquid.ExtinguishPower;
            if (!IsBurning || water <= 0f)
                return;

            remaining = Mathf.Max(0f, remaining - water);
            steam.Emit(new ParticleSystem.EmitParams { position = hit.Point, applyShapeToPosition = true }, 1);
            if (IsBurning)
                ScaleFlames();
            else
                Extinguish();
        }

        void ScaleFlames()
        {
            float scale = Mathf.Lerp(0.35f, 1f, Strength);
            ParticleSystem.MainModule main = flames.main;
            main.startSizeMultiplier = flameSize * scale;
            ParticleSystem.EmissionModule emission = flames.emission;
            emission.rateOverTimeMultiplier = flameRate * scale;
        }

        void Extinguish()
        {
            flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            steam.Emit(30);
            glow.enabled = false;
            hitbox.enabled = false;
            Extinguished?.Invoke(this);
            onExtinguished.Invoke();
        }
    }
}
