using HarmonyLib;
using UnityEngine;
using Valhicle.Components;

namespace Valhicle.Core
{
    /// <summary>
    /// Custom ghost placement for vehicle parts: bearings sit flush on the face you aim at
    /// (top, bottom, or sides), and wheels snap onto a bearing hub.
    /// Vanilla building only yaws around world-up and ignores rigidbody colliders, which
    /// hides the chassis while it is a physics object.
    /// </summary>
    internal static class VehiclePlacement
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[24];

        public static bool IsVehicleBuildPiece(GameObject ghost)
        {
            if (ghost == null) return false;
            string n = VehicleUtil.PrefabName(ghost);
            return n.StartsWith("valhicle_bearing")
                || n.StartsWith("valhicle_wheel")
                || n.StartsWith("valhicle_suspension");
        }

        public static bool TryUpdateGhost(Player player, GameObject ghost)
        {
            if (player == null || ghost == null) return false;
            if (!IsVehicleBuildPiece(ghost)) return false;

            if (!TryRaycast(player, ghost, out RaycastHit hit))
                return false;

            float twist = GetPlaceTwist(player);
            var bearing = hit.collider.GetComponentInParent<VehicleBearing>();
            bool hubAim = bearing != null && bearing.IsHubTarget(hit.point, hit.collider);

            var suspension = hit.collider.GetComponentInParent<VehicleSuspension>();
            bool suspAim = suspension != null;

            bool isWheel = ghost.GetComponent<VehicleWheel>() != null;
            bool isBearing = ghost.GetComponent<VehicleBearing>() != null;
            bool isSuspension = ghost.GetComponent<VehicleSuspension>() != null;

            if (isWheel && hubAim)
            {
                ApplyWheelOnBearing(ghost.transform, bearing, twist);
            }
            else if (isWheel && suspAim)
            {
                ApplyWheelOnSuspension(ghost.transform, suspension, twist);
            }
            else if (isWheel)
            {
                ApplyWheelOnSurface(ghost.transform, hit.point, hit.normal, twist);
            }
            else if (isBearing)
            {
                ApplyBearingOnSurface(ghost.transform, hit.point, hit.normal, twist);
            }
            else if (isSuspension)
            {
                ApplyStrutOnSurface(ghost.transform, hit.point, hit.normal, twist);
            }
            else
            {
                return false;
            }

            ghost.SetActive(true);
            Traverse.Create(player).Field("m_placementStatus").SetValue(0);
            var setValid = AccessTools.Method(typeof(Player), "SetPlacementGhostValid");
            setValid?.Invoke(player, new object[] { true });
            return true;
        }

        public static void SnapWheelToBearing(Transform wheel, VehicleBearing bearing)
        {
            if (wheel == null || bearing == null) return;
            Transform head = bearing.RotatingHead != null ? bearing.RotatingHead : bearing.transform;
            VehicleUtil.ParentKeepWorld(wheel, head, true);
            ApplyWheelOnBearing(wheel, bearing, 0f);
        }

        public static void SnapWheelToSuspension(Transform wheel, VehicleSuspension suspension)
        {
            if (wheel == null || suspension == null) return;
            Transform head = suspension.MovingHead != null ? suspension.MovingHead : suspension.transform;
            VehicleUtil.ParentKeepWorld(wheel, head, true);
            ApplyWheelOnSuspension(wheel, suspension, 0f);
        }

        private static void ApplyWheelOnSuspension(Transform wheel, VehicleSuspension suspension, float twist)
        {
            Transform head = suspension.MovingHead != null ? suspension.MovingHead : suspension.transform;
            var core = suspension.GetComponentInParent<VehicleCore>();
            Vector3 refRight = core != null ? core.transform.right : suspension.transform.right;
            Vector3 axle = Quaternion.AngleAxis(twist, suspension.transform.up) * refRight;

            wheel.rotation = RotationWithRight(axle, 0f);
            float topOffset = suspension.RestLength * 0.42f;
            wheel.position = suspension.transform.position + suspension.transform.up * topOffset;
        }

        private static void ApplyBearingOnSurface(Transform t, Vector3 point, Vector3 normal, float twist)
        {
            Vector3 n = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
            // Twist around the surface normal so wall mounts stay a round puck, not a sheared oval.
            t.rotation = Quaternion.AngleAxis(twist, n) * RotationWithUp(n, 0f);
            t.position = point + n * 0.06f;
            t.localScale = Vector3.one;
        }

        private static void ApplyStrutOnSurface(Transform t, Vector3 point, Vector3 normal, float twist)
        {
            Vector3 n = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
            var susp = t.GetComponent<VehicleSuspension>();
            float halfHeight = (susp != null ? susp.RestLength : 0.70f) * 0.42f + 0.02f;
            t.rotation = Quaternion.AngleAxis(twist, n) * RotationWithUp(n, 0f);
            t.position = point + n * halfHeight;
            t.localScale = Vector3.one;
        }

        private static void ApplyWheelOnSurface(Transform t, Vector3 point, Vector3 normal, float twist)
        {
            // Wheels stay upright and rotate around world up (same as vanilla piece spin).
            t.rotation = Quaternion.Euler(0f, twist, 0f);
            Vector3 n = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
            if (n.y > 0.4f)
            {
                t.position = point + Vector3.up * 0.02f;
            }
            else
            {
                t.position = point + n * 0.05f;
            }
        }

        private static void ApplyWheelOnBearing(Transform wheel, VehicleBearing bearing, float twist)
        {
            Transform head = bearing.RotatingHead != null ? bearing.RotatingHead : bearing.transform;
            Vector3 axis = head.up;
            var core = bearing.GetComponentInParent<VehicleCore>();
            Vector3 refRight = core != null ? core.transform.right : bearing.transform.right;

            Vector3 axle;
            if (Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.65f)
            {
                axle = Quaternion.AngleAxis(twist, axis) * refRight;
                wheel.rotation = RotationWithRight(axle, 0f);
            }
            else
            {
                axle = axis;
                wheel.rotation = RotationWithRight(axle, twist);
            }
            wheel.position = head.position;
        }

        /// <summary>transform.up = up, twist around that axis.</summary>
        public static Quaternion RotationWithUp(Vector3 up, float twistDegrees)
        {
            up = up.normalized;
            Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.ProjectOnPlane(Vector3.right, up);
            }
            return Quaternion.LookRotation(forward.normalized, up) * Quaternion.Euler(0f, twistDegrees, 0f);
        }

        /// <summary>transform.right = right (wheel axle), twist around that axis.</summary>
        public static Quaternion RotationWithRight(Vector3 right, float twistDegrees)
        {
            right = right.normalized;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, right);
            if (up.sqrMagnitude < 0.001f)
            {
                up = Vector3.ProjectOnPlane(Vector3.forward, right);
            }
            up.Normalize();
            Vector3 forward = Vector3.Cross(right, up);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }
            return Quaternion.LookRotation(forward.normalized, up) * Quaternion.Euler(twistDegrees, 0f, 0f);
        }

        public static float GetPlaceTwist(Player player)
        {
            if (player == null) return 0f;
            var tr = Traverse.Create(player);
            int steps = tr.Field<int>("m_placeRotation").Value;
            float deg = tr.Field<float>("m_placeRotationDegrees").Value;
            if (deg <= 0.01f) deg = 22.5f;
            return steps * deg;
        }

        public static bool TryRaycast(Player player, GameObject ghost, out RaycastHit best)
        {
            best = default;
            if (GameCamera.instance == null) return false;

            Vector3 origin = GameCamera.instance.transform.position;
            Vector3 dir = GameCamera.instance.transform.forward;
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "piece_nonsolid", "viewblock");
            int count = Physics.RaycastNonAlloc(origin, dir, Hits, 16f, mask, QueryTriggerInteraction.Ignore);

            float maxDist = 12f;
            if (player != null)
            {
                try
                {
                    maxDist = Traverse.Create(player).Field<float>("m_maxPlaceDistance").Value;
                    if (maxDist < 1f) maxDist = 12f;
                }
                catch
                {
                    maxDist = 12f;
                }
            }

            int ghostLayer = VehicleUtil.GhostLayer;
            float bestDist = float.MaxValue;
            bool found = false;
            Vector3 eye = player != null ? player.GetEyePoint() : origin;

            for (int i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider == null) continue;
                if (ghost != null && (hit.collider.transform == ghost.transform || hit.collider.transform.IsChildOf(ghost.transform)))
                    continue;
                if (ghostLayer >= 0 && hit.collider.gameObject.layer == ghostLayer)
                    continue;
                if (VehicleUtil.IsPlacementGhost(hit.collider.gameObject))
                    continue;
                if (Vector3.Distance(eye, hit.point) > maxDist + 4f)
                    continue;

                if (hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    best = hit;
                    found = true;
                }
            }

            return found;
        }
    }
}
