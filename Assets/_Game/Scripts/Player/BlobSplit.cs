using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// The blob as one of its pieces. A <see cref="PiercerPipe"/> cuts a whole blob into two halves: this object goes on
    /// as one half and a copy of it becomes the other, each with half the volume and mass (see <see cref="BlobSize"/>).
    /// Every half reads the same input, so the halves move, jump and freeze together. Halves can't be cut again, but two
    /// liquid halves that touch flow back into one whole blob. While a pipe carries a piece it's out of the player's
    /// hands: no control, no collisions, no gravity, and hidden once inside.
    /// </summary>
    [RequireComponent(typeof(BlobMotor), typeof(BlobForm), typeof(BlobSkin))]
    public class BlobSplit : MonoBehaviour
    {
        [Tooltip("Set on the halves a piercer pipe made; they can't go through one again.")]
        [SerializeField] bool isHalf;
        [Tooltip("Two liquid halves flow together once the gap between their cores is smaller than this, m.")]
        [SerializeField] float mergeGap = 0.3f;
        [Tooltip("How long the merged blob takes to swell back to its whole size, s. Swelling at once would toss the ship.")]
        [SerializeField] float swellTime = 0.5f;

        static readonly List<BlobSplit> all = new List<BlobSplit>();
        static int nextSerial;

        BlobMotor motor;
        BlobForm form;
        BlobSkin skin;
        BlobCargo cargo;
        BlobWater water;
        BlobSize size;
        Rigidbody body;
        SphereCollider core;
        ShipBuoyancy ship;
        // Everything that steers or pushes the body; switched off while a pipe carries it.
        readonly List<Behaviour> steering = new List<Behaviour>();
        string wholeName;
        int serial;
        bool absorbed;

        /// <summary>Every piece of the blob in the loaded scenes: the whole blob, or both halves.</summary>
        public static IReadOnlyList<BlobSplit> All => all;
        public bool IsHalf => isHalf;
        /// <summary>True while a pipe carries this piece: the player can't control it.</summary>
        public bool InPipe { get; private set; }
        public Rigidbody Body => body;
        public BlobMotor Motor => motor;
        public BlobForm Form => form;
        public BlobSkin Skin => skin;
        /// <summary>Can a piercer pipe take this piece in now? Only the whole blob, as liquid, done swelling.</summary>
        public bool CanBeCut => !isHalf && Free && !form.IsSolid && !size.IsSwelling;
        bool Free => !InPipe && !absorbed && skin.IsReady && !(water != null && water.IsDissolved);
        bool CanMerge => isHalf && Free && !form.IsSolid;

        /// <summary>True while the ship floats on this piece.</summary>
        public bool CarriesShip => ship != null && ship.isActiveAndEnabled && ship.IsFloating
                                   && skin.TrySampleSurface(ship.Body.worldCenterOfMass, out _);

        // Fast play mode keeps statics between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            all.Clear();
            nextSerial = 0;
        }

        void Awake()
        {
            motor = GetComponent<BlobMotor>();
            form = GetComponent<BlobForm>();
            skin = GetComponent<BlobSkin>();
            cargo = GetComponent<BlobCargo>();
            water = GetComponent<BlobWater>();
            // Blobs saved before sizes existed get one here.
            if (!TryGetComponent(out size)) size = gameObject.AddComponent<BlobSize>();
            body = GetComponent<Rigidbody>();
            core = GetComponent<SphereCollider>();
            ship = FindAnyObjectByType<ShipBuoyancy>();
            steering.Add(motor);
            steering.Add(form);
            if (water != null) steering.Add(water);
            if (TryGetComponent(out BlobMagnetism magnetism)) steering.Add(magnetism);
            if (TryGetComponent(out BlobGrip grip)) steering.Add(grip);
            if (TryGetComponent(out GroundGuard guard)) steering.Add(guard);
            wholeName = name;
            serial = nextSerial++;
        }

        void OnEnable() => all.Add(this);

        void OnDisable() => all.Remove(this);

        /// <summary>A pipe takes the piece in: from now on only the pipe moves it, through its velocity.</summary>
        public void EnterPipe()
        {
            InPipe = true;
            foreach (Behaviour behaviour in steering) behaviour.enabled = false;
            core.enabled = false;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
        }

        /// <summary>Out of the pipe, moving at <paramref name="velocity"/>, in the player's hands again.</summary>
        public void LeavePipe(Vector3 velocity)
        {
            SetHidden(false);
            core.enabled = true;
            body.useGravity = true;
            body.linearVelocity = velocity;
            foreach (Behaviour behaviour in steering) behaviour.enabled = true;
            InPipe = false;
        }

        /// <summary>Hides the skin and everything else drawn as part of the blob, without touching their own switches.</summary>
        public void SetHidden(bool hidden)
        {
            foreach (Renderer part in GetComponentsInChildren<Renderer>(true)) part.forceRenderingOff = hidden;
        }

        /// <summary>
        /// Cuts the whole blob in two while a pipe carries it. This piece goes on as the first half; the returned copy,
        /// the second half, starts out in the pipe and hidden, just like this one.
        /// </summary>
        public BlobSplit Split()
        {
            // Copied before shrinking, so both halves shrink from the same whole size.
            GameObject copy = Instantiate(gameObject, body.position, body.rotation);
            var half = copy.GetComponent<BlobSplit>();
            half.size.MatchCopiedFrom(size);
            half.wholeName = wholeName;
            copy.name = wholeName + " Half";
            half.InPipe = true;
            half.SetHidden(true);

            size.Resize(0.5f);
            half.size.Resize(0.5f);
            isHalf = half.isHalf = true;
            return half;
        }

        void FixedUpdate()
        {
            if (!CanMerge) return;
            for (int i = 0; i < all.Count; i++)
            {
                BlobSplit other = all[i];
                if (other == this || !other.CanMerge) continue;
                float reach = motor.CoreRadius + other.motor.CoreRadius + mergeGap;
                if ((other.body.position - body.position).sqrMagnitude > reach * reach) continue;

                if (Keeps(other)) Absorb(other);
                else other.Absorb(this);
                return;
            }
        }

        /// <summary>Which of two touching halves goes on as the whole blob: the one with the ship, otherwise the older one.</summary>
        bool Keeps(BlobSplit other)
        {
            bool mine = CarriesShip, theirs = other.CarriesShip;
            return mine != theirs ? mine : serial < other.serial;
        }

        /// <summary>
        /// The other half flows into this one: it disappears with whatever it carried handed over, and this one is the
        /// whole blob again where it is, with both halves' water (droplets either soaked up included). It gets the whole
        /// mass at once, and swells to the whole size over <see cref="swellTime"/>.
        /// </summary>
        void Absorb(BlobSplit other)
        {
            other.absorbed = true;
            all.Remove(other);
            if (cargo != null && other.cargo != null) cargo.TakeOver(other.cargo);
            Destroy(other.gameObject);

            isHalf = false;
            name = wholeName;
            size.Grow((size.Volume + other.size.Volume) / size.Volume, swellTime);
        }
    }
}
