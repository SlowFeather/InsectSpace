using UnityEngine;

namespace InsectSpace.Rendering
{
    /// <summary>Aspect-aware camera framing for the local art study.</summary>
    [DisallowMultipleComponent]
    public sealed class HeroStudyFraming : MonoBehaviour
    {
        public enum ViewMode { Hero, Explore }
        [SerializeField] private Camera view;
        [SerializeField] private Transform character;
        [SerializeField] private ViewMode mode;
        private float appliedAspect = -1;
        public ViewMode Mode => mode;

        public void Configure(Camera camera, Transform actor)
        {
            view = camera;
            character = actor;
            Apply();
        }

        public void SetMode(ViewMode value)
        {
            mode = value;
            Apply();
        }

        private void LateUpdate()
        {
            if (view && !Mathf.Approximately(view.aspect, appliedAspect)) Apply();
        }

        public void Apply()
        {
            if (!view || !character) return;
            appliedAspect = view.aspect;
            view.orthographic = false;
            view.fieldOfView = mode == ViewMode.Hero ? 39 : 44;
            if (mode == ViewMode.Hero)
            {
                float distance = Mathf.Max(4.45f, 2.55f / Mathf.Max(.3f, appliedAspect));
                var target = character.position + Vector3.up * 1.12f;
                view.transform.position = target + new Vector3(0, .33f, -distance);
                view.transform.LookAt(target);
            }
            else
            {
                // Keep the exploration view low enough to retain the horizon and
                // cloud layer while bringing the reference character into the
                // same readable scale as the environment props.
                var target = character.position + new Vector3(0, .92f, 3.8f);
                // Portrait views need extra depth for the taller composition, but
                // the full inverse-aspect correction makes the actor too small.
                float distance = Mathf.Max(8.2f, 7.4f / Mathf.Max(.3f, appliedAspect));
                view.transform.position = target + new Vector3(.22f, .14f, -.95f).normalized * distance;
                view.transform.LookAt(target);
            }
        }
    }
}
