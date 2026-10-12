using UnityEngine;

namespace StackTower
{
    /// <summary>Rotation and bounds maths shared by blocks, the tower monitor and the placement assists.</summary>
    public static class PlacementMath
    {
        private static readonly Vector3[] LocalAxes =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };

        /// <summary>
        /// Snaps a free rotation to steps: first the tilt of whichever face points most upward, then the
        /// heading. With the usual 15 degree step a block held roughly level becomes exactly level.
        /// Working on tilt and heading separately avoids the jitter Euler rounding has near the poles.
        /// </summary>
        public static Quaternion SnapRotation(Quaternion rotation, float stepDegrees)
        {
            if (stepDegrees <= 0.01f)
            {
                return rotation;
            }

            int best = 0;
            float bestDot = -2f;
            for (int i = 0; i < LocalAxes.Length; i++)
            {
                float dot = (rotation * LocalAxes[i]).y;
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }

            Vector3 upAxis = rotation * LocalAxes[best];
            float tilt = Mathf.Acos(Mathf.Clamp(bestDot, -1f, 1f)) * Mathf.Rad2Deg;

            // "level" is the same rotation with the tilt taken out.
            Quaternion level = Quaternion.FromToRotation(upAxis, Vector3.up) * rotation;

            // Any local axis perpendicular to the up-facing one is horizontal now; use it to read the heading.
            Vector3 reference = level * LocalAxes[(best + 2) % LocalAxes.Length];
            float yaw = Mathf.Atan2(reference.x, reference.z) * Mathf.Rad2Deg;
            float snappedYaw = Mathf.Round(yaw / stepDegrees) * stepDegrees;
            level = Quaternion.AngleAxis(snappedYaw - yaw, Vector3.up) * level;

            float snappedTilt = Mathf.Round(tilt / stepDegrees) * stepDegrees;
            Vector3 tiltAxis = Vector3.Cross(Vector3.up, upAxis);
            if (snappedTilt < 0.01f || tiltAxis.sqrMagnitude < 1e-8f)
            {
                return level;
            }

            float heading = Mathf.Atan2(tiltAxis.x, tiltAxis.z) * Mathf.Rad2Deg;
            float snappedHeading = Mathf.Round(heading / stepDegrees) * stepDegrees;
            Vector3 snappedAxis = Quaternion.AngleAxis(snappedHeading, Vector3.up) * Vector3.forward;
            return Quaternion.AngleAxis(snappedTilt, snappedAxis) * level;
        }

        /// <summary>
        /// The rotation closest to <paramref name="rotation"/> that lines up with <paramref name="frame"/>,
        /// treating the block's symmetry as "already aligned": a box matches in any of its 24 grid
        /// orientations, a cylinder only needs its axis on a grid direction.
        /// </summary>
        public static Quaternion NearestAligned(BlockShape shape, Quaternion rotation, Quaternion frame)
        {
            Quaternion toFrame = Quaternion.Inverse(frame);

            if (shape == BlockShape.Cylinder)
            {
                Vector3 axis = rotation * Vector3.up;
                Vector3 target = frame * DominantAxis(toFrame * axis);
                return Quaternion.FromToRotation(axis, target) * rotation;
            }

            Quaternion relative = toFrame * rotation;
            Vector3 x = DominantAxis(relative * Vector3.right);
            Vector3 y = relative * Vector3.up;
            y = DominantAxis(y - x * Vector3.Dot(y, x));
            Vector3 z = Vector3.Cross(x, y);
            return frame * Quaternion.LookRotation(z, y);
        }

        /// <summary>Half extents of the world-space box around an oriented box.</summary>
        public static Vector3 WorldExtents(Quaternion rotation, Vector3 halfSize)
        {
            Vector3 r = rotation * Vector3.right;
            Vector3 u = rotation * Vector3.up;
            Vector3 f = rotation * Vector3.forward;
            return new Vector3(
                Mathf.Abs(r.x) * halfSize.x + Mathf.Abs(u.x) * halfSize.y + Mathf.Abs(f.x) * halfSize.z,
                Mathf.Abs(r.y) * halfSize.x + Mathf.Abs(u.y) * halfSize.y + Mathf.Abs(f.y) * halfSize.z,
                Mathf.Abs(r.z) * halfSize.x + Mathf.Abs(u.z) * halfSize.y + Mathf.Abs(f.z) * halfSize.z);
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static Vector3 DominantAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x);
            float ay = Mathf.Abs(v.y);
            float az = Mathf.Abs(v.z);

            if (ax >= ay && ax >= az)
            {
                return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            }

            return ay >= az ? new Vector3(0f, Mathf.Sign(v.y), 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }
    }
}
