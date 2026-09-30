using System;
using System.Collections.Generic;
using InsectSpace.Contracts;
using UnityEngine;

namespace InsectSpace.Rendering
{
    // Owns presentation instances, never simulation state or resource handles.
    public sealed class WorldActorPresenter : MonoBehaviour
    {
        private sealed class ActorView
        {
            public GameObject Object;
            public int Appearance;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private readonly Dictionary<long, ActorView> actors = new Dictionary<long, ActorView>();
        private IWorldPresence presence;
        private Func<int, GameObject> create;
        private Action<GameObject> release;
        public int VisibleCount => actors.Count;
        public float InterpolationSpeed { get; set; } = 12;

        public void Bind(IWorldPresence source, Func<int, GameObject> createView, Action<GameObject> releaseView)
        {
            if (source == null || createView == null || releaseView == null) throw new ArgumentNullException();
            Unbind();
            presence = source;
            create = createView;
            release = releaseView;
            presence.Changed += Reconcile;
            Reconcile();
        }

        public Transform FindActor(long actorId) =>
            actors.TryGetValue(actorId, out var actor) ? actor.Object.transform : null;

        public void Unbind()
        {
            if (presence != null) presence.Changed -= Reconcile;
            presence = null;
            foreach (var actor in actors.Values)
                if (actor.Object != null) release(actor.Object);
            actors.Clear();
            create = null;
            release = null;
        }

        private void Reconcile()
        {
            var visible = new HashSet<long>();
            foreach (var snapshot in presence.Actors)
            {
                visible.Add(snapshot.ActorId);
                if (actors.TryGetValue(snapshot.ActorId, out var actor) && actor.Appearance != snapshot.AppearanceId)
                {
                    release(actor.Object);
                    actors.Remove(snapshot.ActorId);
                    actor = null;
                }
                var position = new Vector3(snapshot.XMillimeters / 1000f, 0, snapshot.ZMillimeters / 1000f);
                var rotation = Quaternion.Euler(0, snapshot.FacingMilliDegrees / 1000f, 0);
                if (actor == null)
                {
                    var view = create(snapshot.AppearanceId);
                    if (view == null) throw new InvalidOperationException("World appearance factory returned no view.");
                    view.transform.SetParent(transform, false);
                    view.transform.SetPositionAndRotation(position, rotation);
                    actor = new ActorView { Object = view, Appearance = snapshot.AppearanceId };
                    actors.Add(snapshot.ActorId, actor);
                }
                actor.Position = position;
                actor.Rotation = rotation;
            }
            var removed = new List<long>();
            foreach (var pair in actors)
                if (!visible.Contains(pair.Key)) removed.Add(pair.Key);
            foreach (long id in removed)
            {
                release(actors[id].Object);
                actors.Remove(id);
            }
        }

        private void LateUpdate()
        {
            float blend = 1 - Mathf.Exp(-Mathf.Max(0, InterpolationSpeed) * Time.unscaledDeltaTime);
            foreach (var actor in actors.Values)
            {
                actor.Object.transform.position = Vector3.Lerp(actor.Object.transform.position, actor.Position, blend);
                actor.Object.transform.rotation = Quaternion.Slerp(actor.Object.transform.rotation, actor.Rotation, blend);
            }
        }

        private void OnDestroy() => Unbind();
    }
}
