using System.Collections;
using InsectSpace.Gameplay.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace InsectSpace.Tests
{
    public sealed class WorldNavigationTests
    {
        private GameObject root;
        private LocalNavigationMotor motor;
        [SetUp] public void Setup()
        {
            root = new GameObject("Navigation test fixture");
            var geometry = new GameObject("Geometry").transform; geometry.SetParent(root.transform);
            Box(geometry, new Vector3(0, -.25f, 0), new Vector3(16, .5f, 16));
            Box(geometry, new Vector3(0, 1.5f, 0), new Vector3(2, 3, 5));
            var actor = new GameObject("Actor"); actor.transform.SetParent(root.transform); actor.transform.position = new Vector3(-4, 0, 0);
            var agent = actor.AddComponent<NavMeshAgent>(); agent.enabled = false;
            motor = root.AddComponent<LocalNavigationMotor>(); motor.Geometry = geometry; motor.Actor = agent; motor.Initialize();
            Assert.IsTrue(motor.Ready, motor.Error);
        }
        private static void Box(Transform parent, Vector3 position, Vector3 size)
        {
            var o = new GameObject("Collider"); o.transform.SetParent(parent); o.transform.position = position; o.AddComponent<BoxCollider>().size = size;
        }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(root); }
        [UnityTest] public IEnumerator AutomaticTravelIsContinuousAndRoutesAroundObstacles()
        {
            Vector3 start = motor.Position; Assert.IsTrue(motor.TravelTo(new Vector3(4, 0, 0), true));
            Assert.Less(Vector3.Distance(start, motor.Position), .01f);
            var path = motor.Actor.path; Assert.GreaterOrEqual(path.corners.Length, 3);
            float deadline = Time.realtimeSinceStartup + 10; bool detoured = false;
            while (!motor.Arrived && Time.realtimeSinceStartup < deadline)
            {
                Vector3 p = motor.Position; Assert.IsFalse(Mathf.Abs(p.x) < .95f && Mathf.Abs(p.z) < 2.45f, "Traversed blocking wall");
                detoured |= Mathf.Abs(p.z) > 2.5f; yield return null;
            }
            Assert.IsTrue(detoured); Assert.IsTrue(motor.Arrived); Assert.IsFalse(motor.AutoTravel);
            Assert.Less(Vector3.Distance(motor.Position, new Vector3(4, 0, 0)), .3f);
            var stopped = motor.Position; yield return new WaitForSeconds(.25f);
            Assert.Less(Vector3.Distance(stopped, motor.Position), .03f, "Must stop at target instead of cycling NPCs");
        }
        [UnityTest] public IEnumerator ManualTakeoverCancelsOldDestinationWithoutJumping()
        {
            Assert.IsTrue(motor.TravelTo(new Vector3(4, 0, 0), true)); yield return new WaitForSeconds(.2f);
            var before = motor.Position; motor.ManualMove(Vector3.back);
            Assert.IsFalse(motor.AutoTravel); Assert.IsFalse(motor.Actor.hasPath); Assert.AreEqual(before, motor.Position);
            yield return new WaitForSeconds(.2f); motor.ManualMove(Vector3.zero);
            Assert.Less(motor.Position.z, before.z); Assert.Less(Vector3.Distance(before, motor.Position), 1.2f);
            yield return new WaitForSeconds(.2f); Assert.IsFalse(motor.AutoTravel);
        }
        [Test] public void UnreachableTargetsAreRejectedAndCurrentPositionArrivesImmediately()
        {
            Assert.IsFalse(motor.TravelTo(new Vector3(100, 0, 100), true)); Assert.IsFalse(motor.AutoTravel);
            Assert.IsTrue(motor.TravelTo(motor.Position, true)); Assert.IsTrue(motor.Arrived); Assert.IsFalse(motor.AutoTravel);
        }
        [UnityTest] public IEnumerator ManualMovementCannotPassThroughNavigationWall()
        {
            motor.ManualMove(Vector3.right); yield return new WaitForSeconds(1.4f); motor.ManualMove(Vector3.zero);
            Assert.Less(motor.Position.x, -1); Assert.Greater(motor.Position.x, -3);
        }
        [Test] public void NpcArrivalRequiresExplicitInteractionAndReturningPreservesPosition()
        {
            var panel = root.AddComponent<LocalWorldNavigationPanel>(); panel.Motor = motor;
            var station = new GameObject("Shop approach").transform; station.SetParent(root.transform);
            station.position = new Vector3(4, 0, 0); panel.Stations = new[] { station };
            Assert.IsFalse(panel.OpenNearbyStation(), "Distant NPC must not open its panel");
            Assert.IsNull(panel.Workshop);
            station.position = motor.Position;
            Assert.IsTrue(panel.TravelToStation(0)); Assert.IsTrue(motor.Arrived);
            Assert.IsNull(panel.Workshop, "Arrival must not start workshop/network operations");
            var position = motor.Position;
            Assert.IsTrue(panel.OpenNearbyStation()); Assert.IsTrue(panel.WorkshopVisible);
            Assert.AreEqual(0, panel.Workshop.Tab); Assert.IsFalse(panel.TravelToStation(0));
            panel.Workshop.ReturnToWorld();
            Assert.IsFalse(panel.WorkshopVisible); Assert.AreEqual(position, motor.Position);
            Assert.IsNull(panel.Workshop.Transport, "Opening UI must not synchronously buy, feed or refine");
        }
    }
}
