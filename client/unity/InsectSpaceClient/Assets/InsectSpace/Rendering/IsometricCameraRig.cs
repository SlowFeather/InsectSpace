using UnityEngine;

namespace InsectSpace.Rendering
{
    [DisallowMultipleComponent]
    public sealed class IsometricCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(10, 13, -10);
        [SerializeField, Min(0.01f)] private float damping = 8;

        public void Follow(Transform value) { target = value; }

        private void LateUpdate()
        {
            Vector3 focus = target == null ? Vector3.zero : target.position;
            transform.position = Vector3.Lerp(transform.position, focus + offset,
                1 - Mathf.Exp(-damping * Time.unscaledDeltaTime));
            transform.LookAt(focus);
        }
    }
}
