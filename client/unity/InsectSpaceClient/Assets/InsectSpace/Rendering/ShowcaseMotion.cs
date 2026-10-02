using UnityEngine;

namespace InsectSpace.Rendering
{
    /// <summary>Small presentation-only motion for the local rendering showcase.</summary>
    [DisallowMultipleComponent]
    public sealed class ShowcaseMotion : MonoBehaviour
    {
        [SerializeField] private float bobHeight = 0.035f;
        [SerializeField] private float bobSpeed = 2.1f;
        [SerializeField] private float yawDegrees = 2.5f;
        private Vector3 basePosition;
        private Quaternion baseRotation;

        private void Awake()
        {
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
        }

        private void Update()
        {
            float phase = Time.time * bobSpeed;
            transform.localPosition = basePosition + Vector3.up * (Mathf.Sin(phase) * bobHeight);
            transform.localRotation = baseRotation * Quaternion.Euler(0f, Mathf.Sin(phase * 0.71f) * yawDegrees, 0f);
        }
    }
}
