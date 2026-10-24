using System.Collections.Generic;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// What happens when a droplet thrown off the blob lands: the collision module kills it where it hits, and this
    /// leaves a small ring lying on that surface in its place, bigger for a harder hit, which bursts outward and dries
    /// up. Sits on the droplet particle system, which must send collision messages.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class DropletSplats : MonoBehaviour
    {
        [Tooltip("Particles for the rings: world space, no gravity or emission of their own, drawn flat on the surface " +
                 "by the splat material.")]
        [SerializeField] ParticleSystem splats;
        [Tooltip("Ring diameter for the softest (x) and the hardest (y) hit, m.")]
        [SerializeField] Vector2 ringSize = new Vector2(0.14f, 0.34f);
        [Tooltip("Impact speed that gives the biggest ring, m/s.")]
        [SerializeField] float fullRingSpeed = 6f;
        [Tooltip("How long a ring takes to spread out and dry up, s.")]
        [SerializeField] float ringLifetime = 0.35f;
        [Tooltip("Rings sit this far off the surface so they don't flicker into it, m.")]
        [SerializeField] float surfaceLift = 0.005f;
        [Tooltip("Most rings started per frame, so a whole splash landing at once can't use up the ring pool.")]
        [SerializeField] int maxRingsPerFrame = 10;

        // A ring's velocity only tells the droplet shader which way the surface faces; this slow, it never visibly drifts.
        const float NormalSpeed = 0.001f;
        // A ring left on something moving faster than this (a lift, a pushed crate) would slide off it, m/s.
        const float MovingSurfaceSpeed = 0.2f;

        ParticleSystem droplets;
        readonly List<ParticleCollisionEvent> hits = new List<ParticleCollisionEvent>(32);
        ParticleSystem.EmitParams ring;
        int ringFrame = -1;
        int ringsThisFrame;

        void Awake()
        {
            droplets = GetComponent<ParticleSystem>();
        }

        void OnParticleCollision(GameObject other)
        {
            if (splats == null) return;
            int count = droplets.GetCollisionEvents(other, hits);
            for (int i = 0; i < count; i++) Splat(hits[i]);
        }

        void Splat(ParticleCollisionEvent hit)
        {
            if (ringFrame != Time.frameCount)
            {
                ringFrame = Time.frameCount;
                ringsThisFrame = 0;
            }
            if (ringsThisFrame >= maxRingsPerFrame) return;

            Rigidbody surface = hit.colliderComponent is Collider collider ? collider.attachedRigidbody : null;
            if (surface != null && surface.GetPointVelocity(hit.intersection).sqrMagnitude > MovingSurfaceSpeed * MovingSurfaceSpeed)
                return;

            ringsThisFrame++;
            float hardness = Mathf.Clamp01(hit.velocity.magnitude / fullRingSpeed);
            ring.position = hit.intersection + hit.normal * surfaceLift;
            ring.velocity = hit.normal * NormalSpeed;
            ring.startSize = Mathf.Lerp(ringSize.x, ringSize.y, hardness) * Random.Range(0.85f, 1.15f);
            ring.startLifetime = ringLifetime * Random.Range(0.85f, 1.15f);
            splats.Emit(ring, 1);
        }
    }
}
