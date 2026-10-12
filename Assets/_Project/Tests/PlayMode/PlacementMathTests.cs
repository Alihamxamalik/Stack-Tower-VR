using NUnit.Framework;
using UnityEngine;

namespace StackTower.Tests
{
    public sealed class PlacementMathTests
    {
        [Test]
        public void SnapRotation_LeavesAnAlignedRotationAlone()
        {
            Quaternion snapped = PlacementMath.SnapRotation(Quaternion.identity, 15f);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, snapped), 0.001f);
        }

        [Test]
        public void SnapRotation_LevelsASmallTilt_WhicheverFaceIsUp()
        {
            // Lying on its side (rolled 90 degrees) and a few degrees off level.
            Quaternion onItsSide = Quaternion.Euler(4f, 0f, 90f) * Quaternion.Euler(0f, 0f, 3f);
            Quaternion snapped = PlacementMath.SnapRotation(onItsSide, 15f);

            // One of the six faces must point exactly up.
            float best = 0f;
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                best = Mathf.Max(best, Mathf.Abs((snapped * axis).y));
            }

            Assert.AreEqual(1f, best, 0.0005f);
            Assert.Less(Quaternion.Angle(onItsSide, snapped), 15f, "Snapping should be a small correction.");
        }

        [Test]
        public void SnapRotation_KeepsADeliberateTilt_InWholeSteps()
        {
            Quaternion tilted = Quaternion.Euler(32f, 0f, 0f);
            Quaternion snapped = PlacementMath.SnapRotation(tilted, 15f);
            float tilt = Vector3.Angle(snapped * Vector3.up, Vector3.up);
            Assert.AreEqual(30f, tilt, 0.05f);
        }

        [Test]
        public void NearestAligned_TreatsQuarterTurnsOfABoxAsAligned()
        {
            Quaternion frame = Quaternion.Euler(0f, 20f, 0f);
            Quaternion nearlyQuarterTurn = frame * Quaternion.Euler(0f, 93f, 0f);

            Quaternion aligned = PlacementMath.NearestAligned(BlockShape.Box, nearlyQuarterTurn, frame);

            Assert.AreEqual(3f, Quaternion.Angle(nearlyQuarterTurn, aligned), 0.05f);
            Assert.AreEqual(0f, Quaternion.Angle(aligned, frame * Quaternion.Euler(0f, 90f, 0f)), 0.05f);
        }

        [Test]
        public void NearestAligned_IgnoresSpinAroundACylindersAxis()
        {
            Quaternion spunAndTipped = Quaternion.Euler(6f, 0f, 0f) * Quaternion.Euler(0f, 41f, 0f);

            Quaternion aligned = PlacementMath.NearestAligned(BlockShape.Cylinder, spunAndTipped, Quaternion.identity);

            Assert.AreEqual(1f, (aligned * Vector3.up).y, 0.0005f, "The axis should end up vertical.");
            Assert.AreEqual(6f, Quaternion.Angle(spunAndTipped, aligned), 0.05f, "Only the tip is corrected, not the spin.");
        }

        [Test]
        public void WorldExtents_OfARotatedPlank_SwapsWidthAndDepth()
        {
            Vector3 half = new Vector3(0.18f, 0.045f, 0.09f);
            Vector3 extents = PlacementMath.WorldExtents(Quaternion.Euler(0f, 90f, 0f), half);
            Assert.AreEqual(0.09f, extents.x, 0.0005f);
            Assert.AreEqual(0.045f, extents.y, 0.0005f);
            Assert.AreEqual(0.18f, extents.z, 0.0005f);
        }
    }
}
