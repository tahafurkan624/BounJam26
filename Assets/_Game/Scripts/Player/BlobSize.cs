using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// How much water this piece of the blob holds. A piercer pipe cuts it in half (<see cref="BlobSplit"/>), two halves
    /// that flow together add up, and every <see cref="WaterDroplet"/> soaked up adds its water for the rest of the level.
    /// Lengths follow the cube root of the volume: the jelly, the core and the heights the motor measures against it, so
    /// a bigger blob also stretches farther and spans wider gaps. The mass changes at once; growing swells in smoothly.
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobSkin), typeof(BlobForm))]
    public class BlobSize : MonoBehaviour
    {
        [Tooltip("How long the jelly takes to swell by a droplet's water, s.")]
        [SerializeField] float dropletSwellTime = 0.6f;

        static readonly List<BlobSize> all = new List<BlobSize>();

        BlobMotor motor;
        BlobSkin skin;
        BlobForm form;
        BlobSplit split;
        BlobWater water;
        Rigidbody body;
        // Length factor still to swell by, and the time left to do it in.
        float swellLeft = 1f;
        float swellTime;

        /// <summary>Every piece of the blob in the loaded scenes.</summary>
        public static IReadOnlyList<BlobSize> All => all;
        /// <summary>Water held, as a share of the blob the level starts with: 1 = whole, 0.5 = a half, more = grown.</summary>
        public float Volume { get; private set; } = 1f;
        /// <summary>Droplets this piece has soaked up.</summary>
        public int Droplets { get; private set; }
        public bool IsSwelling => swellTime > 0f;
        public BlobSkin Skin => skin;
        /// <summary>Only the liquid soaks up water, and only while the player has it: not frozen, piped or dissolving.</summary>
        public bool CanSoak => skin.IsReady && !form.IsSolid && !(split != null && split.InPipe)
                               && !(water != null && water.IsDissolved);

        // Fast play mode keeps statics between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        void Awake()
        {
            motor = GetComponent<BlobMotor>();
            skin = GetComponent<BlobSkin>();
            form = GetComponent<BlobForm>();
            split = GetComponent<BlobSplit>();
            water = GetComponent<BlobWater>();
            body = GetComponent<Rigidbody>();
        }

        void OnEnable() => all.Add(this);

        void OnDisable() => all.Remove(this);

        /// <summary>
        /// Is <paramref name="point"/> touching the jelly: inside it, or within <paramref name="radius"/> of the core?
        /// </summary>
        public bool Touches(Vector3 point, float radius)
        {
            float reach = motor.CoreRadius + radius;
            return (point - body.position).sqrMagnitude <= reach * reach || skin.Contains(point);
        }

        /// <summary>Takes in a droplet's water (a share of the starting blob) for good, and swells into it.</summary>
        public void Soak(float dropletVolume)
        {
            Droplets++;
            Grow((Volume + dropletVolume) / Volume, dropletSwellTime);
        }

        /// <summary>Changes the volume by <paramref name="factor"/> at once (a blob cut in half).</summary>
        public void Resize(float factor)
        {
            Volume *= factor;
            form.ScaleMass(factor);
            ScaleLengths(Mathf.Pow(factor, 1f / 3f));
        }

        /// <summary>
        /// Changes the volume by <paramref name="factor"/>: the mass at once, the size swelling in over
        /// <paramref name="duration"/> (on top of any swelling still going on). Swelling at once would toss the ship.
        /// </summary>
        public void Grow(float factor, float duration)
        {
            Volume *= factor;
            form.ScaleMass(factor);
            swellLeft *= Mathf.Pow(factor, 1f / 3f);
            swellTime = Mathf.Max(swellTime, duration);
            if (swellTime <= 0f) FinishSwelling();
        }

        /// <summary>
        /// For a copy of <paramref name="original"/> (a piercer pipe's second half): the copy's jelly was built at the
        /// starting size, while everything else was copied at the original's size. Matches the jelly and the volume.
        /// </summary>
        public void MatchCopiedFrom(BlobSize original)
        {
            Volume = original.Volume;
            Droplets = 0;
            skin.Resize(Mathf.Pow(Volume, 1f / 3f));
        }

        /// <summary>One physics step of swelling; the steps multiply up to exactly the size left to swell by.</summary>
        void FixedUpdate()
        {
            if (swellTime <= 0f) return;
            float dt = Mathf.Min(Time.fixedDeltaTime, swellTime);
            float step = Mathf.Pow(swellLeft, dt / swellTime);
            swellTime -= dt;
            swellLeft /= step;
            ScaleLengths(step);
            if (swellTime <= 0f) FinishSwelling();
        }

        void FinishSwelling()
        {
            ScaleLengths(swellLeft);
            swellLeft = 1f;
            swellTime = 0f;
        }

        void ScaleLengths(float factor)
        {
            if (Mathf.Approximately(factor, 1f)) return;
            skin.Resize(factor);
            motor.Resize(factor);
        }
    }
}
