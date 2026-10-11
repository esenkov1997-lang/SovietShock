using UnityEngine;

namespace FIMSpace
{
    public class FImp_ColliderData_Box : FImp_ColliderData_Base
    {
        public BoxCollider Box { get; private set; }
        public BoxCollider2D Box2D { get; private set; }

        private Vector3 boxCenter;

        private Vector3 right;
        private Vector3 up;
        private Vector3 forward;

        private Vector3 rightN;
        private Vector3 upN;
        private Vector3 forwardN;

        private Vector3 scales;

        private Quaternion boxRotation;
        private Quaternion inverseBoxRotation;
        private Vector3 halfExtents;
        private Bounds worldBounds;
        private Vector3 previousColliderSize;
        private Vector3 previousColliderCenter;
        private bool geometryInitialized;

        public Bounds WorldBounds { get { return worldBounds; } }

        // For 3D
        public FImp_ColliderData_Box(BoxCollider collider)
        {
            Is2D = false;
            Collider = collider;
            Transform = collider.transform;
            Box = collider;
            ColliderType = EFColliderType.Box;
            RefreshColliderData();
        }

        // For 2D
        public FImp_ColliderData_Box(BoxCollider2D collider2D)
        {
            Is2D = true;
            Collider2D = collider2D;
            Transform = collider2D.transform;
            Box2D = collider2D;
            ColliderType = EFColliderType.Box;
            RefreshColliderData();
        }


        #region Refreshing Data


        public override void RefreshColliderData()
        {
            if (IsStatic && geometryInitialized) return; // Static geometry is cached after one guaranteed initialization.

            if (Collider2D == null) // 3D Refresh
            {
                bool diff = !geometryInitialized
                    || !FEngineering.VIsSame(Transform.position, previousPosition)
                    || !FEngineering.QIsSame(Transform.rotation, previousRotation)
                    || !FEngineering.VIsSame(Transform.lossyScale, previousScale)
                    || !FEngineering.VIsSame(Box.size, previousColliderSize)
                    || !FEngineering.VIsSame(Box.center, previousColliderCenter);

                if (diff)
                {
                    boxRotation = Box.transform.rotation;
                    inverseBoxRotation = Quaternion.Inverse(boxRotation);
                    Vector3 scale = Abs(Box.transform.lossyScale);
                    halfExtents = Vector3.Scale(Abs(Box.size), scale) * 0.5f;

                    right = boxRotation * Vector3.right * halfExtents.x;
                    up = boxRotation * Vector3.up * halfExtents.y;
                    forward = boxRotation * Vector3.forward * halfExtents.z;

                    rightN = right.normalized;
                    upN = up.normalized;
                    forwardN = forward.normalized;

                    boxCenter = GetBoxCenter(Box);
                    worldBounds = CalculateWorldBounds(boxCenter, boxRotation, halfExtents);

                    scales = Vector3.Scale(Box.size, Box.transform.lossyScale);
                    scales.Normalize();
                }
            }
            else // 2D Refresh
            {
                Vector3 colliderSize = new Vector3(Box2D.size.x, Box2D.size.y, 0f);
                Vector3 colliderCenter = new Vector3(Box2D.offset.x, Box2D.offset.y, 0f);
                bool diff = !geometryInitialized
                    || !FEngineering.VIsSame(Transform.position, previousPosition)
                    || !FEngineering.QIsSame(Transform.rotation, previousRotation)
                    || !FEngineering.VIsSame(Transform.lossyScale, previousScale)
                    || !FEngineering.VIsSame(colliderSize, previousColliderSize)
                    || !FEngineering.VIsSame(colliderCenter, previousColliderCenter);

                if (diff)
                {
                    right = Box2D.transform.TransformVector((Vector3.right / 2f) * Box2D.size.x);
                    up = Box2D.transform.TransformVector((Vector3.up / 2f) * Box2D.size.y);

                    rightN = right.normalized;
                    upN = up.normalized;

                    boxCenter = GetBoxCenter(Box2D);
                    boxCenter.z = 0f;

                    Vector3 scale = Transform.lossyScale; scale.z = 1f;
                    scales = Vector3.Scale(Box2D.size, scale);
                    scales.Normalize();
                }
            }

            base.RefreshColliderData();

            previousPosition = Transform.position;
            previousRotation = Transform.rotation;
            previousScale = Transform.lossyScale;
            if (Collider2D == null)
            {
                previousColliderSize = Box.size;
                previousColliderCenter = Box.center;
            }
            else
            {
                previousColliderSize = new Vector3(Box2D.size.x, Box2D.size.y, 0f);
                previousColliderCenter = new Vector3(Box2D.offset.x, Box2D.offset.y, 0f);
            }

            geometryInitialized = true;
        }


        #endregion


        public override bool PushIfInside(ref Vector3 segmentPosition, float segmentRadius, Vector3 segmentOffset)
        {
            if (Collider2D == null)
            {
                Vector3 correction;
                Vector3 normal;
                if (!TryGetSphereContact(segmentPosition + segmentOffset, segmentRadius, out correction, out normal))
                    return false;

                segmentPosition += correction;
                return true;
            }

            int inOrInt = 0;
            Vector3 interPlane = Vector3.zero;
            Vector3 segmentOffsetted = segmentPosition + segmentOffset;
            float planeDistance = PlaneDistance(boxCenter + up, upN, segmentOffsetted);
            if (SphereInsidePlane(planeDistance, segmentRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, segmentRadius)) { inOrInt++; interPlane = up; }

            planeDistance = PlaneDistance(boxCenter - up, -upN, segmentOffsetted);
            if (SphereInsidePlane(planeDistance, segmentRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, segmentRadius)) { inOrInt++; interPlane = -up; }

            planeDistance = PlaneDistance(boxCenter - right, -rightN, segmentOffsetted);
            if (SphereInsidePlane(planeDistance, segmentRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, segmentRadius)) { inOrInt++; interPlane = -right; }

            planeDistance = PlaneDistance(boxCenter + right, rightN, segmentOffsetted);
            if (SphereInsidePlane(planeDistance, segmentRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, segmentRadius)) { inOrInt++; interPlane = right; }

            bool insideOrIntersects = false;

            if (Collider2D == null)
            {
                planeDistance = PlaneDistance(boxCenter + forward, forwardN, segmentOffsetted);
                if (SphereInsidePlane(planeDistance, segmentRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, segmentRadius)) { inOrInt++; interPlane = forward; }

                planeDistance = PlaneDistance(boxCenter - forward, -forwardN, segmentOffsetted);
                if (SphereInsidePlane(planeDistance, segmentRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, segmentRadius)) { inOrInt++; interPlane = -forward; }

                if (inOrInt == 6) insideOrIntersects = true;
            }
            else if (inOrInt == 4) insideOrIntersects = true;

            if (insideOrIntersects)
            {
                bool inside = false;
                //Vector3 rayDirection;

                if (interPlane.sqrMagnitude == 0f) // sphere is inside the box
                {
                    //if ( Collider2D == null)
                    //    interPlane = -GetTargetPlaneNormal(Box, segmentOffsetted, right, up, forward, scales);
                    //else
                    //    interPlane = -GetTargetPlaneNormal(Box2D, segmentOffsetted, right, up, scales);
                    inside = true;
                    //rayDirection = (interPlane).normalized; // poprawić przy przeskalowanych boxach
                }
                else // sphere is intersecting box
                {
                    //rayDirection = (segmentOffsetted - boxCenter).normalized;
                    if (Collider2D == null)
                    { if (IsInsideBoxCollider(Box, segmentOffsetted)) inside = true; }
                    else if (IsInsideBoxCollider(Box2D, segmentOffsetted)) inside = true;
                }

                Vector3 pointOnPlane = GetNearestPoint(segmentOffsetted);
                Vector3 toNormal = pointOnPlane - segmentOffsetted;

                if (inside) toNormal += toNormal.normalized * segmentRadius; else toNormal -= toNormal.normalized * segmentRadius;
                //Debug.DrawRay(pointOnPlane, toNormal);

                if (inside)
                {
                    segmentPosition = segmentPosition + toNormal;
                }
                else
                    if (toNormal.sqrMagnitude > 0) segmentPosition = segmentPosition + toNormal;

                return true;
            }

            return false;
        }


        /// <summary>
        /// Exact sphere contact against the cached oriented 3D box. The returned correction moves the
        /// sphere fully outside and includes a very small skin to prevent numerical re-penetration.
        /// </summary>
        public bool TryGetSphereContact(Vector3 sphereCenter, float sphereRadius, out Vector3 correction, out Vector3 normal)
        {
            return TryGetSphereContact(sphereCenter, sphereRadius, boxCenter, boxRotation, inverseBoxRotation,
                halfExtents, out correction, out normal);
        }


        /// <summary>
        /// Finds the deepest contact between a tapered tail edge and this box without allocations or
        /// physics queries. The one-dimensional search is bounded and has a fixed, small cost.
        /// </summary>
        public bool TryGetSegmentContact(Vector3 start, Vector3 end, float startRadius, float endRadius,
            out float segmentTime, out Vector3 normal)
        {
            const int searchIterations = 8;
            float lower = 0f;
            float upper = 1f;

            for (int i = 0; i < searchIterations; i++)
            {
                float first = (lower * 2f + upper) / 3f;
                float second = (lower + upper * 2f) / 3f;
                float firstGap = GetSegmentSphereGap(start, end, startRadius, endRadius, first);
                float secondGap = GetSegmentSphereGap(start, end, startRadius, endRadius, second);

                if (firstGap < secondGap) upper = second;
                else if (secondGap < firstGap) lower = first;
                else { lower = first; upper = second; }
            }

            segmentTime = (lower + upper) * 0.5f;
            float bestGap = GetSegmentSphereGap(start, end, startRadius, endRadius, segmentTime);
            float startGap = GetSegmentSphereGap(start, end, startRadius, endRadius, 0f);
            if (startGap < bestGap) { bestGap = startGap; segmentTime = 0f; }
            float endGap = GetSegmentSphereGap(start, end, startRadius, endRadius, 1f);
            if (endGap < bestGap) { bestGap = endGap; segmentTime = 1f; }

            float radius = Mathf.LerpUnclamped(startRadius, endRadius, segmentTime);
            float margin = ContactSkin(radius);
            if (bestGap > margin)
            {
                normal = Vector3.zero;
                return false;
            }

            Vector3 correction;
            if (!TryGetSphereContact(Vector3.LerpUnclamped(start, end, segmentTime), radius + margin,
                out correction, out normal))
                return false;

            return true;
        }


        /// <summary>
        /// Sweeps a sphere against the box and returns a skinned first-contact position. The fixed
        /// iteration count makes the cost predictable and avoids Physics.SphereCast allocations/state.
        /// </summary>
        public bool TrySweepSphere(Vector3 start, Vector3 end, float radius,
            out Vector3 contactCenter, out Vector3 normal)
        {
            float margin = ContactSkin(radius);
            float startGap = GetSegmentSphereGap(start, end, radius, radius, 0f);
            if (startGap <= margin)
            {
                Vector3 correction;
                if (!TryGetSphereContact(start, radius + margin, out correction, out normal))
                {
                    contactCenter = Vector3.zero;
                    return false;
                }

                // Do not retain a resting contact when the predicted motion is already separating.
                if (Vector3.Dot(end - start, normal) >= 0f)
                {
                    contactCenter = Vector3.zero;
                    normal = Vector3.zero;
                    return false;
                }

                contactCenter = start + correction;
                return true;
            }

            float deepestTime;
            Vector3 deepestNormal;
            if (!TryGetSegmentContact(start, end, radius, radius, out deepestTime, out deepestNormal))
            {
                contactCenter = Vector3.zero;
                normal = Vector3.zero;
                return false;
            }

            float lower = 0f;
            float upper = deepestTime;
            for (int i = 0; i < 9; i++)
            {
                float middle = (lower + upper) * 0.5f;
                if (GetSegmentSphereGap(start, end, radius, radius, middle) <= margin) upper = middle;
                else lower = middle;
            }

            Vector3 center = Vector3.LerpUnclamped(start, end, upper);
            Vector3 contactCorrection;
            if (!TryGetSphereContact(center, radius + margin, out contactCorrection, out normal))
            {
                contactCenter = Vector3.zero;
                return false;
            }

            contactCenter = center + contactCorrection;
            return true;
        }


        /// <summary> World-space support distance of the box in the supplied normalized direction. </summary>
        public float GetSupportDistance(Vector3 normal)
        {
            Vector3 localNormal = inverseBoxRotation * normal;
            return Vector3.Dot(boxCenter, normal)
                + Mathf.Abs(localNormal.x) * halfExtents.x
                + Mathf.Abs(localNormal.y) * halfExtents.y
                + Mathf.Abs(localNormal.z) * halfExtents.z;
        }


        private float GetSegmentSphereGap(Vector3 start, Vector3 end, float startRadius, float endRadius, float time)
        {
            Vector3 point = Vector3.LerpUnclamped(start, end, time);
            float radius = Mathf.LerpUnclamped(startRadius, endRadius, time);
            Vector3 local = inverseBoxRotation * (point - boxCenter);
            Vector3 outside = Abs(local) - halfExtents;
            Vector3 outsidePositive = new Vector3(Mathf.Max(0f, outside.x), Mathf.Max(0f, outside.y), Mathf.Max(0f, outside.z));
            float signedDistance = outsidePositive.magnitude
                + Mathf.Min(Mathf.Max(outside.x, Mathf.Max(outside.y, outside.z)), 0f);
            return signedDistance - radius;
        }


        public static void PushOutFromBoxCollider(BoxCollider box, Collision collision, float segmentColliderRadius, ref Vector3 segmentPosition, bool is2D = false)
        {
            PushOutFromBoxCollider(box, segmentColliderRadius, ref segmentPosition, is2D);
        }

        public static void PushOutFromBoxCollider(BoxCollider box, float segmentColliderRadius, ref Vector3 segmentPosition, bool is2D = false)
        {
            Vector3 center;
            Quaternion rotation;
            Quaternion inverseRotation;
            Vector3 extents;
            GetBoxWorldGeometry(box, out center, out rotation, out inverseRotation, out extents);

            Vector3 correction;
            Vector3 normal;
            if (TryGetSphereContact(segmentPosition, segmentColliderRadius, center, rotation, inverseRotation,
                extents, out correction, out normal))
                segmentPosition += correction;
        }


        public static void PushOutFromBoxCollider(BoxCollider box, Collision collision, float segmentColliderRadius, ref Vector3 pos, Vector3 right, Vector3 up, Vector3 forward, Vector3 scales, bool is2D = false)
        {
            PushOutFromBoxCollider(box, segmentColliderRadius, ref pos, is2D);
        }

        #region Push out from box 2D

        public static void PushOutFromBoxCollider(BoxCollider2D box2D, float segmentColliderRadius, ref Vector3 segmentPosition)
        {
            Vector2 right = box2D.transform.TransformVector((Vector3.right / 2f) * box2D.size.x + box2D.offset.x * Vector3.right);
            Vector2 up = box2D.transform.TransformVector((Vector3.up / 2f) * box2D.size.y + box2D.offset.y * Vector3.up);

            Vector3 scale2D = box2D.transform.lossyScale; scale2D.z = 1f;
            Vector2 scales = Vector3.Scale(box2D.size, scale2D);
            scales.Normalize();

            Vector2 boxCenter = GetBoxCenter(box2D);

            float pointRadius = segmentColliderRadius;
            Vector2 upN = up.normalized; Vector2 rightN = right.normalized;

            int inOrInt = 0;
            Vector3 interPlane = Vector3.zero;
            float planeDistance = PlaneDistance(boxCenter + up, upN, segmentPosition);
            if (SphereInsidePlane(planeDistance, pointRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, pointRadius)) { inOrInt++; interPlane = up; }

            planeDistance = PlaneDistance(boxCenter - up, -upN, segmentPosition);
            if (SphereInsidePlane(planeDistance, pointRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, pointRadius)) { inOrInt++; interPlane = -up; }

            planeDistance = PlaneDistance(boxCenter - right, -rightN, segmentPosition);
            if (SphereInsidePlane(planeDistance, pointRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, pointRadius)) { inOrInt++; interPlane = -right; }

            planeDistance = PlaneDistance(boxCenter + right, rightN, segmentPosition);
            if (SphereInsidePlane(planeDistance, pointRadius)) inOrInt++; else if (SphereIntersectsPlane(planeDistance, pointRadius)) { inOrInt++; interPlane = right; }

            // Collision occured - sphere intersecting box shape volume or is inside of it
            if (inOrInt == 4)
            {
                bool inside = false;

                if (interPlane.sqrMagnitude == 0f) // sphere is inside the box
                {
                    //interPlane = -GetTargetPlaneNormal(box2D, segmentPosition, right, up, scales);
                    inside = true;
                }
                else // sphere is intersecting box
                {
                    if (IsInsideBoxCollider(box2D, segmentPosition)) inside = true;
                }

                Vector3 pointOnPlane = GetNearestPoint2D(segmentPosition, boxCenter, right, up);

                Vector3 toNormal = pointOnPlane - segmentPosition;
                if (inside) toNormal += toNormal.normalized * pointRadius * 1.01f; else toNormal -= toNormal.normalized * pointRadius * 1.01f;

                if (inside)
                    segmentPosition = segmentPosition + toNormal;
                else
                    if (toNormal.sqrMagnitude > 0) segmentPosition = segmentPosition + toNormal;
            }
        }


        #endregion


        #region Box Calculations Helpers


        private static bool TryGetSphereContact(Vector3 sphereCenter, float sphereRadius, Vector3 center,
            Quaternion rotation, Quaternion inverseRotation, Vector3 extents,
            out Vector3 correction, out Vector3 normal)
        {
            sphereRadius = Mathf.Max(0f, sphereRadius);
            Vector3 localPoint = inverseRotation * (sphereCenter - center);
            Vector3 closest = new Vector3(
                Mathf.Clamp(localPoint.x, -extents.x, extents.x),
                Mathf.Clamp(localPoint.y, -extents.y, extents.y),
                Mathf.Clamp(localPoint.z, -extents.z, extents.z));
            Vector3 localDelta = localPoint - closest;
            float distanceSq = localDelta.sqrMagnitude;
            float radiusSq = sphereRadius * sphereRadius;

            if (distanceSq > radiusSq)
            {
                correction = Vector3.zero;
                normal = Vector3.zero;
                return false;
            }

            float skin = ContactSkin(sphereRadius);
            Vector3 localNormal;
            Vector3 localCorrection;
            if (distanceSq > 1e-12f)
            {
                float distance = Mathf.Sqrt(distanceSq);
                localNormal = localDelta / distance;
                localCorrection = localNormal * (sphereRadius + skin - distance);
            }
            else
            {
                // The center is inside the box (or exactly on its surface). Select the shortest
                // deterministic exit; fixed tie ordering prevents normals alternating at corners.
                Vector3 faceDistance = extents - Abs(localPoint);
                int axis = faceDistance.x <= faceDistance.y && faceDistance.x <= faceDistance.z ? 0
                    : faceDistance.y <= faceDistance.z ? 1 : 2;
                float sign = localPoint[axis] < 0f ? -1f : 1f;
                localNormal = Vector3.zero;
                localNormal[axis] = sign;
                localCorrection = localNormal * (Mathf.Max(0f, faceDistance[axis]) + sphereRadius + skin);
            }

            normal = rotation * localNormal;
            correction = rotation * localCorrection;
            return true;
        }


        private static void GetBoxWorldGeometry(BoxCollider box, out Vector3 center, out Quaternion rotation,
            out Quaternion inverseRotation, out Vector3 extents)
        {
            rotation = box.transform.rotation;
            inverseRotation = Quaternion.Inverse(rotation);
            center = box.transform.TransformPoint(box.center);
            extents = Vector3.Scale(Abs(box.size), Abs(box.transform.lossyScale)) * 0.5f;
        }


        private static Bounds CalculateWorldBounds(Vector3 center, Quaternion rotation, Vector3 extents)
        {
            Vector3 axisX = Abs(rotation * Vector3.right) * extents.x;
            Vector3 axisY = Abs(rotation * Vector3.up) * extents.y;
            Vector3 axisZ = Abs(rotation * Vector3.forward) * extents.z;
            return new Bounds(center, (axisX + axisY + axisZ) * 2f);
        }


        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }


        private static float ContactSkin(float radius)
        {
            return Mathf.Max(radius * 0.001f, 0.000001f);
        }


        /// <summary>
        /// Getting nearest plane normal fitting to given point position
        /// </summary>
        private Vector3 GetNearestPoint(Vector3 point)
        {
            Vector3 pointOnBox = point;

            Vector3 distancesPositive = Vector3.one;
            distancesPositive.x = PlaneDistance(boxCenter + right, rightN, point);
            distancesPositive.y = PlaneDistance(boxCenter + up, upN, point);
            if (Collider2D == null) distancesPositive.z = PlaneDistance(boxCenter + forward, forwardN, point);

            Vector3 distancesNegative = Vector3.one;
            distancesNegative.x = PlaneDistance(boxCenter - right, -rightN, point);
            distancesNegative.y = PlaneDistance(boxCenter - up, -upN, point);
            if (Collider2D == null) distancesNegative.z = PlaneDistance(boxCenter - forward, -forwardN, point);

            float nearestX, nearestY, nearestZ;
            float negX = 1f, negY = 1f, negZ = 1f;

            if (distancesPositive.x > distancesNegative.x) { nearestX = distancesPositive.x; negX = -1f; } else { nearestX = distancesNegative.x; negX = 1f; }
            if (distancesPositive.y > distancesNegative.y) { nearestY = distancesPositive.y; negY = -1f; } else { nearestY = distancesNegative.y; negY = 1f; }

            if (Collider2D == null)
            {
                if (distancesPositive.z > distancesNegative.z) { nearestZ = distancesPositive.z; negZ = -1f; } else { nearestZ = distancesNegative.z; negZ = 1f; }
                if (nearestX > nearestZ)
                {
                    if (nearestX > nearestY) { pointOnBox = ProjectPointOnPlane(right * negX, point, nearestX); }
                    else
                        pointOnBox = ProjectPointOnPlane(up * negY, point, nearestY);
                }
                else
                {
                    if (nearestZ > nearestY) { pointOnBox = ProjectPointOnPlane(forward * negZ, point, nearestZ); }
                    else
                        pointOnBox = ProjectPointOnPlane(up * negY, point, nearestY);
                }
            }
            else
            {
                if (nearestX > nearestY) { pointOnBox = ProjectPointOnPlane(right * negX, point, nearestX); }
                else
                    pointOnBox = ProjectPointOnPlane(up * negY, point, nearestY);
            }


            return pointOnBox;
        }

        /// <summary>
        /// Getting nearest plane normal fitting to given point position
        /// </summary>
        private static Vector3 GetNearestPoint(Vector3 point, Vector3 boxCenter, Vector3 right, Vector3 up, Vector3 forward, bool is2D = false)
        {
            Vector3 pointOnBox = point;

            Vector3 distancesPositive = Vector3.one;
            distancesPositive.x = PlaneDistance(boxCenter + right, right.normalized, point);
            distancesPositive.y = PlaneDistance(boxCenter + up, up.normalized, point);
            if (is2D == false) distancesPositive.z = PlaneDistance(boxCenter + forward, forward.normalized, point);

            Vector3 distancesNegative = Vector3.one;
            distancesNegative.x = PlaneDistance(boxCenter - right, -right.normalized, point);
            distancesNegative.y = PlaneDistance(boxCenter - up, -up.normalized, point);
            if (is2D == false) distancesNegative.z = PlaneDistance(boxCenter - forward, -forward.normalized, point);

            float nearestX, nearestY, nearestZ;
            float negX = 1f, negY = 1f, negZ = 1f;

            if (distancesPositive.x > distancesNegative.x) { nearestX = distancesPositive.x; negX = -1f; } else { nearestX = distancesNegative.x; negX = 1f; }
            if (distancesPositive.y > distancesNegative.y) { nearestY = distancesPositive.y; negY = -1f; } else { nearestY = distancesNegative.y; negY = 1f; }

            if (is2D == false)
            {
                if (distancesPositive.z > distancesNegative.z) { nearestZ = distancesPositive.z; negZ = -1f; } else { nearestZ = distancesNegative.z; negZ = 1f; }

                if (nearestX > nearestZ)
                {
                    if (nearestX > nearestY) { pointOnBox = ProjectPointOnPlane(right * negX, point, nearestX); }
                    else
                        pointOnBox = ProjectPointOnPlane(up * negY, point, nearestY);
                }
                else
                {
                    if (nearestZ > nearestY) { pointOnBox = ProjectPointOnPlane(forward * negZ, point, nearestZ); }
                    else
                        pointOnBox = ProjectPointOnPlane(up * negY, point, nearestY);
                }
            }
            else
            {
                if (nearestX > nearestY) { pointOnBox = ProjectPointOnPlane(right * negX, point, nearestX); }
                else
                    pointOnBox = ProjectPointOnPlane(up * negY, point, nearestY);
            }

            return pointOnBox;
        }

        /// <summary>
        /// Getting nearest plane normal fitting to given point position
        /// </summary>
        private static Vector3 GetNearestPoint2D(Vector2 point, Vector2 boxCenter, Vector2 right, Vector2 up)
        {
            Vector3 pointOnBox = point;

            Vector3 distancesPositive = Vector3.one;
            distancesPositive.x = PlaneDistance(boxCenter + right, right.normalized, point);
            distancesPositive.y = PlaneDistance(boxCenter + up, up.normalized, point);

            Vector3 distancesNegative = Vector3.one;
            distancesNegative.x = PlaneDistance(boxCenter - right, -right.normalized, point);
            distancesNegative.y = PlaneDistance(boxCenter - up, -up.normalized, point);

            float nearestX, nearestY;
            float negX = 1f, negY = 1f;

            if (distancesPositive.x > distancesNegative.x) { nearestX = distancesPositive.x; negX = -1f; } else { nearestX = distancesNegative.x; negX = 1f; }
            if (distancesPositive.y > distancesNegative.y) { nearestY = distancesPositive.y; negY = -1f; } else { nearestY = distancesNegative.y; negY = 1f; }

            if (nearestX > nearestY) { pointOnBox = ProjectPointOnPlane(right * negX, point, nearestX); }
            else
                pointOnBox = ProjectPointOnPlane(up * negY, point, nearestY);

            return pointOnBox;
        }


        /// <summary>
        /// Getting nearest plane point on box collider
        /// </summary>
        public static Vector3 GetNearestPointOnBox(BoxCollider boxCollider, Vector3 point, bool is2D = false)
        {
            Vector3 center;
            Quaternion rotation;
            Quaternion inverseRotation;
            Vector3 extents;
            GetBoxWorldGeometry(boxCollider, out center, out rotation, out inverseRotation, out extents);
            if (is2D) extents.z = 0f;

            Vector3 localPoint = inverseRotation * (point - center);
            Vector3 closest = new Vector3(
                Mathf.Clamp(localPoint.x, -extents.x, extents.x),
                Mathf.Clamp(localPoint.y, -extents.y, extents.y),
                is2D ? 0f : Mathf.Clamp(localPoint.z, -extents.z, extents.z));

            if ((!is2D && localPoint.x >= -extents.x && localPoint.x <= extents.x
                && localPoint.y >= -extents.y && localPoint.y <= extents.y
                && localPoint.z >= -extents.z && localPoint.z <= extents.z)
                || (is2D && localPoint.x >= -extents.x && localPoint.x <= extents.x
                    && localPoint.y >= -extents.y && localPoint.y <= extents.y))
            {
                Vector3 faceDistance = extents - Abs(localPoint);
                int axis = is2D
                    ? (faceDistance.x <= faceDistance.y ? 0 : 1)
                    : (faceDistance.x <= faceDistance.y && faceDistance.x <= faceDistance.z ? 0
                        : faceDistance.y <= faceDistance.z ? 1 : 2);
                closest = localPoint;
                closest[axis] = (localPoint[axis] < 0f ? -1f : 1f) * extents[axis];
                if (is2D) closest.z = 0f;
            }

            return center + rotation * closest;
        }


        private static float PlaneDistance(Vector3 planeCenter, Vector3 planeNormal, Vector3 point)
        {
            return Vector3.Dot(point - planeCenter, planeNormal);
        }

        private static Vector3 ProjectPointOnPlane(Vector3 planeNormal, Vector3 point, float distance)
        {
            Vector3 translationVector = planeNormal.normalized * distance;
            return point + translationVector;
        }

        private static bool SphereInsidePlane(float planeDistance, float pointRadius) { return -planeDistance > pointRadius; }
        private static bool SphereOutsidePlane(float planeDistance, float pointRadius) { return planeDistance > pointRadius; }
        private static bool SphereIntersectsPlane(float planeDistance, float pointRadius) { return Mathf.Abs(planeDistance) <= pointRadius; }


        public static bool IsInsideBoxCollider(BoxCollider collider, Vector3 point, bool is2D = false)
        {
            point = collider.transform.InverseTransformPoint(point) - collider.center;

            float xExtend = (collider.size.x * 0.5f);
            float yExtend = (collider.size.y * 0.5f);
            float zExtend = (collider.size.z * 0.5f);
            return (point.x < xExtend && point.x > -xExtend && point.y < yExtend && point.y > -yExtend && point.z < zExtend && point.z > -zExtend);
        }

        // 2D Version
        public static bool IsInsideBoxCollider(BoxCollider2D collider, Vector3 point)
        {
            point = (Vector2)collider.transform.InverseTransformPoint(point) - collider.offset;

            float xExtend = (collider.size.x * 0.5f);
            float yExtend = (collider.size.y * 0.5f);

            return (point.x < xExtend && point.x > -xExtend && point.y < yExtend && point.y > -yExtend);
        }


        /// <summary>
        /// Getting average scale of box's dimensions
        /// </summary>
        protected static float GetBoxAverageScale(BoxCollider box)
        {
            Vector3 scales = box.transform.lossyScale;
            scales = Vector3.Scale(scales, box.size);
            return (scales.x + scales.y + scales.z) / 3f;
        }

        protected static Vector3 GetBoxCenter(BoxCollider box)
        {
            return box.transform.position + box.transform.TransformVector(box.center);
        }

        protected static Vector3 GetBoxCenter(BoxCollider2D box)
        {
            return box.transform.position + box.transform.TransformVector(box.offset);
        }

        protected static Vector3 GetTargetPlaneNormal(BoxCollider boxCollider, Vector3 point, bool is2D = false)
        {
            Vector3 right = boxCollider.transform.TransformVector((Vector3.right / 2f) * boxCollider.size.x);
            Vector3 up = boxCollider.transform.TransformVector((Vector3.up / 2f) * boxCollider.size.y);
            Vector3 forward = Vector3.forward; if (is2D == false) forward = boxCollider.transform.TransformVector((Vector3.forward / 2f) * boxCollider.size.z);

            Vector3 scales = Vector3.Scale(boxCollider.size, boxCollider.transform.lossyScale);
            scales.Normalize();

            return GetTargetPlaneNormal(boxCollider, point, right, up, forward, scales, is2D);
        }

        /// <summary>
        /// Getting nearest plane normal fitting to given point position
        /// </summary>
        protected static Vector3 GetTargetPlaneNormal(BoxCollider boxCollider, Vector3 point, Vector3 right, Vector3 up, Vector3 forward, Vector3 scales, bool is2D = false)
        {
            Vector3 rayDirection = (GetBoxCenter(boxCollider) - point).normalized;

            // Finding proper box's plane
            Vector3 dots;
            dots.x = Vector3.Dot(rayDirection, right.normalized);
            dots.y = Vector3.Dot(rayDirection, up.normalized);
            dots.x = dots.x * scales.y * scales.z;
            dots.y = dots.y * scales.x * scales.z;

            if (is2D == false)
            {
                dots.z = Vector3.Dot(rayDirection, forward.normalized);
                dots.z = dots.z * scales.y * scales.x;
            }
            else dots.z = 0;

            dots.Normalize();

            Vector3 dotsAbs = dots;
            if (dots.x < 0) dotsAbs.x = -dots.x;
            if (dots.y < 0) dotsAbs.y = -dots.y;
            if (dots.z < 0) dotsAbs.z = -dots.z;

            Vector3 planeNormal;
            if (dotsAbs.x > dotsAbs.y)
            {
                if (dotsAbs.x > dotsAbs.z || is2D) planeNormal = right * Mathf.Sign(dots.x); else planeNormal = forward * Mathf.Sign(dots.z);
            }
            else
            {
                if (dotsAbs.y > dotsAbs.z || is2D) planeNormal = up * Mathf.Sign(dots.y); else planeNormal = forward * Mathf.Sign(dots.z);
            }

            return planeNormal;
        }


        // 2D Version
        protected static Vector3 GetTargetPlaneNormal(BoxCollider2D boxCollider, Vector2 point, Vector2 right, Vector2 up, Vector2 scales)
        {
            Vector2 rayDirection = ((Vector2)GetBoxCenter(boxCollider) - point).normalized;

            // Finding proper box's plane
            Vector2 dots;
            dots.x = Vector3.Dot(rayDirection, right.normalized);
            dots.y = Vector3.Dot(rayDirection, up.normalized);
            dots.x = dots.x * scales.y;
            dots.y = dots.y * scales.x;

            dots.Normalize();

            Vector2 dotsAbs = dots;
            if (dots.x < 0) dotsAbs.x = -dots.x;
            if (dots.y < 0) dotsAbs.y = -dots.y;

            Vector3 planeNormal;
            if (dotsAbs.x > dotsAbs.y) planeNormal = right * Mathf.Sign(dots.x);
            else
                planeNormal = up * Mathf.Sign(dots.y);

            return planeNormal;
        }


        /// <summary>
        /// Calculating cheap ray on box plane to detect position from inside
        /// </summary>
        protected static Vector3 GetIntersectOnBoxFromInside(BoxCollider boxCollider, Vector3 from, Vector3 to, Vector3 planeNormal)
        {
            Vector3 rayDirection = (to - from);

            // Creating box's plane and casting cheap ray on it to detect intersection position
            Plane plane = new Plane(-planeNormal, GetBoxCenter(boxCollider) + planeNormal);
            Vector3 intersectionPoint = to;

            float enter = 0f;
            Ray ray = new Ray(from, rayDirection);
            if (plane.Raycast(ray, out enter)) intersectionPoint = ray.GetPoint(enter);

            return intersectionPoint;
        }



        #endregion

    }
}
