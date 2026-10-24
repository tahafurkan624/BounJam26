using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// A belt loop standing on its edge like a low wall, running around a roller at each end, with metal spikes
    /// sticking out sideways at blob height. The liquid flows around a spike (they're on the LiquidPassable layer);
    /// freezing sets the crystal around it, and the belt carries the crystal along until it melts (see
    /// <see cref="BlobGrip"/>). The spikes shove loose crystals, crates and the ship like any moving part.
    /// This object sits on the floor in the middle of the belt, which runs along its forward axis. Seen from above the
    /// belt turns anticlockwise: forward along the +X side, back along the -X side; a negative speed reverses it.
    /// Every part is built from the settings when the game starts, so one prefab makes conveyors of any length; in the
    /// editor the gizmo shows the outline.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class Conveyor : MonoBehaviour
    {
        [Tooltip("Distance between the two rollers' centres, m.")]
        [SerializeField] float length = 6f;
        [Tooltip("Belt speed, m/s. Negative runs it the other way round.")]
        [SerializeField] float speed = 1.2f;
        [Tooltip("Roughly how far apart the spikes are along the belt, m. Rounded so they're evenly spread.")]
        [SerializeField] float spikeSpacing = 1.8f;

        [Header("Spike Glow")]
        [Tooltip("Spike glow while it's inside the jelly: freezing now grabs it.")]
        [ColorUsage(false, true)]
        [SerializeField] Color engulfedGlow = new Color(1.5f, 5f, 7f);
        [Tooltip("Spike glow while a crystal holds it.")]
        [ColorUsage(false, true)]
        [SerializeField] Color heldGlow = new Color(0.3f, 0.9f, 2.4f);

        [Header("Parts (SM_Conveyor)")]
        [SerializeField] Mesh slatMesh;
        [SerializeField] Mesh spikeMesh;
        [SerializeField] Mesh spikeTipMesh;
        [SerializeField] Mesh drumMesh;
        [SerializeField] Mesh lidMiddleMesh;
        [SerializeField] Mesh lidEndMesh;
        [SerializeField] Mesh hubMesh;
        [SerializeField] Material beltMaterial;
        [Tooltip("Needs emission enabled, so the spikes can glow.")]
        [SerializeField] Material steelMaterial;
        [Tooltip("Needs emission enabled, so the spikes can glow.")]
        [SerializeField] Material spikeTipMaterial;
        [SerializeField] Material drumMaterial;
        [SerializeField] Material lidMaterial;
        [SerializeField] Material hubMaterial;

        // The model's measurements (ArtSource/Blender/scripts/conveyor.py), m.
        const float DrumRadius = 0.35f;
        const float BeltThickness = 0.07f;
        const float BeltHeight = 0.8f;
        const float SlatPitch = 0.2f;
        const float SpikeHeight = 0.3f;
        const float SpikeReach = 0.475f;
        const float LidThickness = 0.06f;
        // The belt's underside clears the floor by this much, m.
        const float Lift = 0.03f;
        // Spike colliders are a little fatter than the rod, so a crystal pushed along doesn't slip between them, m.
        const float SpikeColliderRadius = 0.06f;

        static readonly List<Conveyor> all = new List<Conveyor>();

        readonly List<ConveyorSpike> spikes = new List<ConveyorSpike>();
        readonly List<Collider> colliders = new List<Collider>();
        readonly List<Transform> slats = new List<Transform>();
        readonly List<Transform> rollers = new List<Transform>();
        Mesh endColliderMesh;
        float loopLength;
        // Distance the belt has moved, m: before and after the last physics step, so the slats can be drawn in between
        // exactly like the spikes' interpolated rigidbodies.
        float previousTravel;
        float travel;

        /// <summary>Every conveyor in the loaded scenes.</summary>
        public static IReadOnlyList<Conveyor> All => all;
        public IReadOnlyList<ConveyorSpike> Spikes => spikes;
        /// <summary>Every collider of the machine, spikes included. A crystal holding a spike ignores them.</summary>
        public IReadOnlyList<Collider> Colliders => colliders;
        public Color EngulfedGlow => engulfedGlow;
        public Color HeldGlow => heldGlow;

        // Statics survive entering play mode with domain reload off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        void Awake()
        {
            loopLength = 2f * length + 2f * Mathf.PI * DrumRadius;
            BuildFrame();
            BuildBelt();
            MoveSpikes(true);
            PlaceBelt(travel);
        }

        void OnEnable() => all.Add(this);

        void OnDisable() => all.Remove(this);

        void OnDestroy()
        {
            if (endColliderMesh != null) Destroy(endColliderMesh);
        }

        void FixedUpdate()
        {
            previousTravel = travel;
            travel += speed * Time.fixedDeltaTime;
            MoveSpikes(false);
        }

        void LateUpdate()
        {
            float t = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            PlaceBelt(Mathf.Lerp(previousTravel, travel, t));
        }

        /// <summary>Moves the spikes to where the belt is at the end of this physics step.</summary>
        void MoveSpikes(bool teleport)
        {
            for (int i = 0; i < spikes.Count; i++)
            {
                PathPose(SpikeDistance(i, travel), out Vector3 point, out Vector3 outward, out Quaternion rotation);
                Vector3 position = transform.TransformPoint(point + outward * BeltThickness + Vector3.up * Lift);
                spikes[i].MoveTo(position, transform.rotation * rotation, teleport);
            }
        }

        void PlaceBelt(float distance)
        {
            for (int i = 0; i < slats.Count; i++)
            {
                PathPose(i * loopLength / slats.Count + distance, out Vector3 point, out _, out Quaternion rotation);
                slats[i].SetLocalPositionAndRotation(point + Vector3.up * Lift, rotation);
            }
            // The rollers turn with the belt's inner face; anticlockwise from above is negative about Y.
            Quaternion spin = Quaternion.Euler(0f, -distance / DrumRadius * Mathf.Rad2Deg, 0f);
            foreach (Transform roller in rollers) roller.localRotation = spin;
        }

        float SpikeDistance(int index, float distance) => (index + 0.5f) * loopLength / spikes.Count + distance;

        /// <summary>
        /// The belt's inner face at distance <paramref name="s"/> along it, in local space at floor height:
        /// the point, the way out of the loop and the pose of a part riding there (+X outward, +Z along the belt).
        /// Starts at the +X side's back end.
        /// </summary>
        void PathPose(float s, out Vector3 point, out Vector3 outward, out Quaternion rotation)
        {
            float half = length * 0.5f;
            float arc = Mathf.PI * DrumRadius;
            s = Mathf.Repeat(s, loopLength);
            Vector3 along;
            if (s < length)
            {
                outward = Vector3.right;
                along = Vector3.forward;
                point = new Vector3(DrumRadius, 0f, s - half);
            }
            else if (s < length + arc)
            {
                float angle = (s - length) / DrumRadius;
                outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                along = new Vector3(-outward.z, 0f, outward.x);
                point = new Vector3(0f, 0f, half) + outward * DrumRadius;
            }
            else if (s < 2f * length + arc)
            {
                outward = Vector3.left;
                along = Vector3.back;
                point = new Vector3(-DrumRadius, 0f, half - (s - length - arc));
            }
            else
            {
                float angle = Mathf.PI + (s - 2f * length - arc) / DrumRadius;
                outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                along = new Vector3(-outward.z, 0f, outward.x);
                point = new Vector3(0f, 0f, -half) + outward * DrumRadius;
            }
            rotation = Quaternion.LookRotation(along, Vector3.up);
        }

        // ---------------------------------------------------------------- Building

        /// <summary>The still parts: one solid body (a box with a round end at each roller), the lid and the rollers.</summary>
        void BuildFrame()
        {
            int environment = LayerMask.NameToLayer("Environment");
            float outer = DrumRadius + BeltThickness;
            float height = BeltHeight + LidThickness;
            var frame = new GameObject("Frame") { layer = environment };
            frame.transform.SetParent(transform, false);

            var box = frame.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, Lift + height * 0.5f, 0f);
            box.size = new Vector3(outer * 2f, height, length);
            colliders.Add(box);
            endColliderMesh = CylinderMesh(outer, Lift, Lift + height, 24);
            float half = length * 0.5f;
            foreach (float end in new[] { half, -half })
            {
                var cap = new GameObject("Round End") { layer = environment };
                cap.transform.SetParent(frame.transform, false);
                cap.transform.localPosition = new Vector3(0f, 0f, end);
                var round = cap.AddComponent<MeshCollider>();
                round.sharedMesh = endColliderMesh;
                round.convex = true;
                colliders.Add(round);

                Transform drum = Part("Roller", frame.transform, drumMesh, drumMaterial).transform;
                drum.localPosition = new Vector3(0f, Lift, end);
                rollers.Add(drum);
                Transform lid = Part("Lid End", frame.transform, lidEndMesh, lidMaterial).transform;
                lid.SetLocalPositionAndRotation(new Vector3(0f, Lift + BeltHeight, end), Quaternion.Euler(0f, end > 0f ? 0f : 180f, 0f));
                // The hub sits on a pivot of its own, so it can spin while the lid end stays turned.
                var hubPivot = new GameObject("Hub").transform;
                hubPivot.SetParent(frame.transform, false);
                hubPivot.localPosition = new Vector3(0f, Lift + height, end);
                Part("Gear", hubPivot, hubMesh, hubMaterial);
                rollers.Add(hubPivot);
            }

            Transform middle = Part("Lid", frame.transform, lidMiddleMesh, lidMaterial).transform;
            middle.localPosition = new Vector3(0f, Lift + BeltHeight, 0f);
            middle.localScale = new Vector3(1f, 1f, length);
            // Dark filler inside the loop, seen through the gaps between slats where they fan out round the rollers.
            GameObject filler = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(filler.GetComponent<Collider>());
            filler.name = "Inside";
            filler.layer = environment;
            filler.transform.SetParent(frame.transform, false);
            filler.transform.localPosition = new Vector3(0f, Lift + BeltHeight * 0.5f, 0f);
            filler.transform.localScale = new Vector3(DrumRadius * 2f - 0.04f, BeltHeight - 0.04f, length);
            filler.GetComponent<MeshRenderer>().sharedMaterial = beltMaterial;
        }

        /// <summary>The moving parts: slats all the way round, and spikes evenly spread on them.</summary>
        void BuildBelt()
        {
            var belt = new GameObject("Belt").transform;
            belt.SetParent(transform, false);
            int slatCount = Mathf.Max(8, Mathf.RoundToInt(loopLength / SlatPitch));
            // Stretched a little so the loop closes without a gap.
            var slatScale = new Vector3(1f, 1f, loopLength / slatCount / SlatPitch);
            for (int i = 0; i < slatCount; i++)
            {
                Transform slat = Part($"Slat {i}", belt, slatMesh, beltMaterial).transform;
                slat.localScale = slatScale;
                slats.Add(slat);
            }

            var spikeGroup = new GameObject("Spikes").transform;
            spikeGroup.SetParent(transform, false);
            int liquidPassable = LayerMask.NameToLayer("LiquidPassable");
            int spikeCount = Mathf.Max(1, Mathf.RoundToInt(loopLength / Mathf.Max(0.5f, spikeSpacing)));
            for (int i = 0; i < spikeCount; i++)
            {
                var spike = new GameObject($"Spike {i}") { layer = liquidPassable };
                spike.transform.SetParent(spikeGroup, false);
                var body = spike.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                var capsule = spike.AddComponent<CapsuleCollider>();
                capsule.direction = 0;
                capsule.radius = SpikeColliderRadius;
                capsule.height = SpikeReach;
                capsule.center = new Vector3(SpikeReach * 0.5f, SpikeHeight, 0f);
                colliders.Add(capsule);
                MeshRenderer rod = Part("Rod", spike.transform, spikeMesh, steelMaterial);
                MeshRenderer tip = Part("Tip", spike.transform, spikeTipMesh, spikeTipMaterial);
                var component = spike.AddComponent<ConveyorSpike>();
                component.Setup(this, new Renderer[] { rod, tip }, new Vector3(0f, SpikeHeight, 0f), SpikeReach);
                spikes.Add(component);
            }
        }

        static MeshRenderer Part(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        /// <summary>An upright cylinder from <paramref name="bottom"/> to <paramref name="top"/>, for a convex collider.</summary>
        static Mesh CylinderMesh(float radius, float bottom, float top, int segments)
        {
            var vertices = new Vector3[segments * 2];
            var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float angle = 2f * Mathf.PI * i / segments;
                var ring = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                vertices[i] = ring + Vector3.up * bottom;
                vertices[segments + i] = ring + Vector3.up * top;
                int next = (i + 1) % segments;
                triangles.AddRange(new[] { i, segments + i, next, next, segments + i, segments + next });
                if (i > 1)
                {
                    triangles.AddRange(new[] { 0, i - 1, i });
                    triangles.AddRange(new[] { segments, segments + i, segments + i - 1 });
                }
            }
            var mesh = new Mesh { name = "Conveyor Round End", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>In the editor: the belt's outline and where the spikes start.</summary>
        void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            loopLength = 2f * length + 2f * Mathf.PI * DrumRadius;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.95f, 0.35f, 0.25f);
            const int steps = 64;
            for (int i = 0; i < steps; i++)
            {
                PathPose(loopLength * i / steps, out Vector3 a, out Vector3 outA, out _);
                PathPose(loopLength * (i + 1) / steps, out Vector3 b, out Vector3 outB, out _);
                a += outA * BeltThickness;
                b += outB * BeltThickness;
                Gizmos.DrawLine(a + Vector3.up * Lift, b + Vector3.up * Lift);
                Gizmos.DrawLine(a + Vector3.up * (Lift + BeltHeight), b + Vector3.up * (Lift + BeltHeight));
            }
            Gizmos.color = new Color(0.6f, 0.85f, 1f);
            int count = Mathf.Max(1, Mathf.RoundToInt(loopLength / Mathf.Max(0.5f, spikeSpacing)));
            for (int i = 0; i < count; i++)
            {
                PathPose((i + 0.5f) * loopLength / count, out Vector3 point, out Vector3 outward, out _);
                Vector3 root = point + outward * BeltThickness + Vector3.up * (Lift + SpikeHeight);
                Gizmos.DrawLine(root, root + outward * SpikeReach);
                Gizmos.DrawSphere(root + outward * SpikeReach, 0.05f);
            }
        }
    }
}
