using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace InsectSpace.Gameplay.Demo
{
    // Standalone local navigation prototype. It is not a world server or battle simulation.
    public sealed class LocalNavigationMotor : MonoBehaviour
    {
        public Transform Geometry;
        public NavMeshAgent Actor;
        public bool Ready { get; private set; }
        public bool AutoTravel { get; private set; }
        public bool Arrived { get; private set; }
        public string Error { get; private set; }
        public Vector3 Destination { get; private set; }
        public Vector3 Position => Actor == null ? Vector3.zero : Actor.transform.position;
        private NavMeshData data;
        private NavMeshDataInstance surface;
        private Vector3 manual;
        private void Start() => Initialize();
        public void Initialize()
        {
            if (Ready || Error != null) return;
            if (Geometry == null || Actor == null) { Error = "缺少导航场景对象"; return; }
            Actor.enabled = false;
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(Geometry, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            var settings = NavMesh.GetSettingsByID(Actor.agentTypeID);
            data = NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, new Vector3(80, 20, 80)), Vector3.zero, Quaternion.identity);
            if (data == null) { Error = "导航网格生成失败"; return; }
            surface = NavMesh.AddNavMeshData(data);
            if (!NavMesh.SamplePosition(Position, out var start, 2, NavMesh.AllAreas)) { Error = "角色不在可通行区域"; return; }
            Actor.transform.position = start.position; Actor.enabled = true;
            Actor.speed = 3.4f; Actor.acceleration = 18; Actor.angularSpeed = 720; Actor.stoppingDistance = .12f; Ready = Actor.isOnNavMesh;
        }
        public bool TravelTo(Vector3 target, bool automatic)
        {
            if (!Ready || !NavMesh.SamplePosition(target, out var hit, 1, NavMesh.AllAreas)) return false;
            var path = new NavMeshPath(); if (!Actor.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            CancelTravel(); Destination = hit.position;
            if (Vector3.Distance(Position, Destination) <= Actor.stoppingDistance + .05f) { Arrived = automatic; return true; }
            if (!Actor.SetPath(path)) return false;
            AutoTravel = automatic; return true;
        }
        public void CancelTravel()
        {
            AutoTravel = false; Arrived = false; manual = Vector3.zero;
            if (Ready && Actor.isOnNavMesh) { Actor.ResetPath(); Actor.velocity = Vector3.zero; }
        }
        public void ManualMove(Vector3 direction)
        {
            direction.y = 0; if (direction.sqrMagnitude > .0001f) { CancelTravel(); manual = Vector3.ClampMagnitude(direction, 1); } else manual = Vector3.zero;
        }
        private void Update()
        {
            if (!Ready) return;
            if (manual.sqrMagnitude > .0001f)
            {
                var next = Position + manual * (Actor.speed * Mathf.Min(Time.deltaTime, .1f));
                if (NavMesh.Raycast(Position, next, out var hit, NavMesh.AllAreas)) next = hit.position;
                Actor.Move(next - Position); Actor.transform.rotation = Quaternion.RotateTowards(Actor.transform.rotation, Quaternion.LookRotation(manual), 720 * Time.deltaTime);
            }
            else if (!Actor.pathPending && (Actor.hasPath && Actor.remainingDistance <= Actor.stoppingDistance + .05f ||
                AutoTravel && Vector3.Distance(Position, Destination) <= Actor.stoppingDistance + .05f))
            { bool arrived = AutoTravel; CancelTravel(); Arrived = arrived; }
        }
        private void OnApplicationFocus(bool focused) { if (!focused) ManualMove(Vector3.zero); }
        private void OnDestroy() { if (Actor != null) Actor.enabled = false; if (surface.valid) surface.Remove(); if (data != null) Destroy(data); }
    }
}
