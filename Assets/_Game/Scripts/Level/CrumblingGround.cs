using System.Collections;
using BounJam.Core;
using UnityEngine;

namespace BounJam.Level
{
    /// <summary>
    /// A slab that shakes once the beaver steps on it and drops shortly after, taking the beaver with it
    /// unless it keeps moving. Needs a trigger box on this object covering the slab's top surface.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class CrumblingGround : MonoBehaviour
    {
        [SerializeField, Tooltip("The solid part that shakes and falls.")]
        Transform slab;
        [SerializeField, Min(0f), Tooltip("Seconds between being stepped on and falling.")]
        float delay = 0.6f;
        [SerializeField, Min(0f)] float shakeDistance = 0.05f;
        [SerializeField, Min(0.1f), Tooltip("Seconds after falling until the slab is removed.")]
        float removeAfter = 3f;

        bool crumbling;

        void OnTriggerEnter(Collider other)
        {
            if (!crumbling && other.gameObject.layer == GameLayers.Player)
                StartCoroutine(Crumble());
        }

        IEnumerator Crumble()
        {
            crumbling = true;
            Vector3 rest = slab.localPosition;
            for (float t = 0f; t < delay; t += Time.deltaTime)
            {
                slab.localPosition = rest + Random.insideUnitSphere * (shakeDistance * t / delay);
                yield return null;
            }
            slab.localPosition = rest;

            var body = slab.gameObject.AddComponent<Rigidbody>();
            body.mass = 50f;
            body.angularVelocity = Random.insideUnitSphere;
            Destroy(slab.gameObject, removeAfter);
        }
    }
}
