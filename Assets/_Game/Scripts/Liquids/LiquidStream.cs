using System.Collections.Generic;
using BounJam.Core;
using UnityEngine;

namespace BounJam.Liquids
{
    /// <summary>
    /// The particle system for one liquid. LiquidGun creates one per liquid type, so particles already in
    /// flight keep the liquid they were fired with. Each frame it turns the particles that hit a collider
    /// into one LiquidHit, pushes Pushable rigidbodies and tells that collider's LiquidTarget.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class LiquidStream : MonoBehaviour
    {
        static readonly List<ParticleCollisionEvent> collisionEvents = new(64);

        ParticleSystem particles;

        public LiquidType Liquid { get; private set; }

        public void Init(LiquidType liquid)
        {
            Liquid = liquid;
            name = $"{liquid.DisplayName} Stream";
            particles = GetComponent<ParticleSystem>();

            // Long enough for the longest (45°) throw to land back at launch height, plus a margin for drops.
            float gravity = -Physics.gravity.y * Mathf.Max(0.05f, liquid.GravityModifier);
            float lifetime = Mathf.Min(8f, 2f * liquid.LaunchSpeed * 0.7071f / gravity + 0.5f);

            ParticleSystem.MainModule main = particles.main;
            main.startColor = liquid.Color;
            main.startSpeed = liquid.LaunchSpeed;
            main.startSize = liquid.ParticleSize;
            main.gravityModifier = liquid.GravityModifier;
            main.startLifetime = lifetime;
            main.maxParticles = Mathf.CeilToInt(liquid.ParticlesPerSecond * lifetime) + 16;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = liquid.ParticlesPerSecond;
            emission.enabled = false;
            particles.Play(false); // emission is toggled from now on; the splash child is driven as a sub-emitter
        }

        public void SetEmitting(bool emitting)
        {
            // Deactivating the beaver stops the system, and it doesn't play on awake.
            if (emitting && !particles.isPlaying)
                particles.Play(false);
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = emitting;
        }

        void OnParticleCollision(GameObject other)
        {
            int count = particles.GetCollisionEvents(other, collisionEvents);
            if (count == 0)
                return;

            Vector3 point = Vector3.zero, normal = Vector3.zero, velocity = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                point += collisionEvents[i].intersection;
                normal += collisionEvents[i].normal;
                velocity += collisionEvents[i].velocity;
            }
            var hit = new LiquidHit(Liquid, collisionEvents[0].colliderComponent as Collider,
                point / count, normal.normalized, velocity / count, count * Liquid.UnitsPerParticle);

            Push(hit);
            LiquidTarget target = other.GetComponentInParent<LiquidTarget>();
            if (target != null)
                target.Receive(hit);
        }

        // Only objects meant to be shoved: falling trunks or hanging props on other layers stay put.
        static void Push(in LiquidHit hit)
        {
            if (hit.Collider == null || hit.Collider.gameObject.layer != GameLayers.Pushable)
                return;
            Rigidbody body = hit.Collider.attachedRigidbody;
            // Flattened, or an arcing stream would mostly press things into the floor.
            Vector3 direction = Vector3.ProjectOnPlane(hit.Velocity, Vector3.up);
            if (body == null || body.isKinematic || direction.sqrMagnitude < 0.0001f)
                return;
            body.AddForceAtPosition(direction.normalized * (hit.Liquid.PushPerUnit * hit.Amount), hit.Point, ForceMode.Impulse);
        }
    }
}
