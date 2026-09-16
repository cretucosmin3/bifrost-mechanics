using UnityEngine;
using Valhicle.Components;

namespace Valhicle.Core
{
    /// <summary>
    /// Attached to every vehicle part. Parents the placed INSTANCE (never the prefab)
    /// onto the chassis without changing world scale, and restores that link after load.
    /// </summary>
    public class VehiclePiece : MonoBehaviour, IPlaced
    {
        public VehicleCore ParentVehicle { get; private set; }
        public VehicleBearing AttachedBearing { get; private set; }
        public VehicleSuspension AttachedSuspension { get; private set; }
        public Piece PieceComponent { get; private set; }
        public WearNTear WearNTearComponent { get; private set; }

        private ZNetView _nview;
        private int _restoreAttempts;
        private bool _restoreDone;

        private void Awake()
        {
            PieceComponent = GetComponent<Piece>();
            WearNTearComponent = GetComponent<WearNTear>();
            _nview = GetComponent<ZNetView>();

            if (WearNTearComponent != null)
            {
                WearNTearComponent.m_noRoofWear = true;
                WearNTearComponent.m_snowDamageImmune = true;
                WearNTearComponent.m_snow = null;
                WearNTearComponent.m_snowWorn = null;
                WearNTearComponent.m_snowBroken = null;
                WearNTearComponent.m_noSupportWear = true;
                WearNTearComponent.m_supports = true;
            }
        }

        private void Start()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;
            if (GetComponent<VehicleCore>() != null) return;
            if (GetComponent<VehicleLift>() != null) return;
            InvokeRepeating(nameof(TryRestoreAttachment), 0.15f, 0.4f);
        }

        public void OnPlaced()
        {
            TryAttachFromWorld(null, null);
        }

        public void OnPlaced(VehicleBearing targetBearing)
        {
            TryAttachFromWorld(targetBearing, null);
        }

        public void OnPlaced(VehicleBearing targetBearing, VehicleSuspension targetSuspension)
        {
            TryAttachFromWorld(targetBearing, targetSuspension);
        }

        private void TryRestoreAttachment()
        {
            _restoreAttempts++;
            if (ParentVehicle != null || _restoreDone)
            {
                CancelInvoke(nameof(TryRestoreAttachment));
                return;
            }

            if (_nview != null && _nview.IsValid())
            {
                var parentId = _nview.GetZDO().GetZDOID(VehicleUtil.ParentZdoKey);
                var bearingId = _nview.GetZDO().GetZDOID(VehicleUtil.BearingZdoKey);
                var suspensionId = _nview.GetZDO().GetZDOID(VehicleUtil.SuspensionZdoKey);

                if (parentId != ZDOID.None || bearingId != ZDOID.None || suspensionId != ZDOID.None)
                {
                    GameObject parentGo = parentId != ZDOID.None ? VehicleUtil.FindByUid(parentId) : null;
                    VehicleCore core = parentGo != null ? (parentGo.GetComponent<VehicleCore>() ?? parentGo.GetComponentInParent<VehicleCore>()) : null;

                    VehicleSuspension suspension = null;
                    if (suspensionId != ZDOID.None)
                    {
                        var sGo = VehicleUtil.FindByUid(suspensionId);
                        suspension = sGo != null ? sGo.GetComponent<VehicleSuspension>() : null;
                        if (suspension == null && _restoreAttempts < 50)
                        {
                            return; // Wait for suspension to spawn
                        }
                    }

                    VehicleBearing bearing = null;
                    if (bearingId != ZDOID.None)
                    {
                        var bGo = VehicleUtil.FindByUid(bearingId);
                        bearing = bGo != null ? bGo.GetComponent<VehicleBearing>() : null;
                        if (bearing == null && _restoreAttempts < 50)
                        {
                            return; // Wait for bearing to spawn
                        }
                    }

                    if (suspension == null && parentGo != null)
                    {
                        suspension = parentGo.GetComponent<VehicleSuspension>() ?? parentGo.GetComponentInParent<VehicleSuspension>();
                    }

                    if (bearing == null && parentGo != null)
                    {
                        bearing = parentGo.GetComponent<VehicleBearing>() ?? parentGo.GetComponentInParent<VehicleBearing>();
                    }

                    if (core == null && parentGo != null)
                    {
                        var vp = parentGo.GetComponent<VehiclePiece>() ?? parentGo.GetComponentInParent<VehiclePiece>();
                        if (vp != null && vp.ParentVehicle != null)
                        {
                            core = vp.ParentVehicle;
                        }
                    }

                    if (parentId != ZDOID.None && core == null && bearing == null && suspension == null && _restoreAttempts < 50)
                    {
                        return; // Wait for parent/core/suspension to spawn
                    }

                    if (core == null && bearing == null && suspension == null)
                    {
                        if (_restoreAttempts < 50) return;
                        CancelInvoke(nameof(TryRestoreAttachment));
                        return;
                    }

                    Vector3 localPos = _nview.GetZDO().GetVec3(VehicleUtil.LocalPosZdoKey, transform.localPosition);
                    Quaternion localRot = _nview.GetZDO().GetQuaternion(VehicleUtil.LocalRotZdoKey, transform.localRotation);

                    if (suspension != null)
                    {
                        AttachToSuspension(suspension, false);
                    }
                    else if (core != null)
                    {
                        AttachToVehicle(core, bearing, false);
                    }
                    else if (bearing != null)
                    {
                        AttachToBearingOnly(bearing, false);
                    }

                    transform.localPosition = localPos;
                    transform.localRotation = localRot;
                    VehicleUtil.ForceWorldScaleOne(transform);
                    if (suspension != null)
                    {
                        suspension.UpdateLoadPose(transform);
                    }
                    else if (bearing != null)
                    {
                        bearing.UpdateLoadPose(transform);
                    }
                    _restoreDone = true;
                    CancelInvoke(nameof(TryRestoreAttachment));
                    return;
                }
            }

            if (_restoreAttempts >= 50)
            {
                CancelInvoke(nameof(TryRestoreAttachment));
            }
        }

        public void TryAttachFromWorld(VehicleBearing targetBearing = null, VehicleSuspension targetSuspension = null)
        {
            if (GetComponent<VehicleLift>() != null) return;

            var selfCore = GetComponent<VehicleCore>();
            var selfBearing = GetComponent<VehicleBearing>();
            var selfSuspension = GetComponent<VehicleSuspension>();
            var nearby = VehicleUtil.FindVehicleNear(transform.position, 8.0f, selfCore);

            if (nearby == null)
            {
                var liftCols = Physics.OverlapSphere(transform.position, 8.0f);
                for (int i = 0; i < liftCols.Length; i++)
                {
                    var lift = liftCols[i].GetComponentInParent<VehicleLift>();
                    if (lift != null && lift.Core != null && lift.Core != selfCore)
                    {
                        nearby = lift.Core;
                        break;
                    }
                }
            }

            if (nearby == null)
            {
                var stackPiece = FindClosestOtherPiece();
                if (stackPiece != null)
                {
                    nearby = stackPiece.ParentVehicle ?? stackPiece.GetComponentInParent<VehicleCore>();
                    if (nearby == null)
                    {
                        var nv = stackPiece.GetComponent<ZNetView>();
                        if (nv != null && nv.IsValid())
                        {
                            var pid = nv.GetZDO().GetZDOID(VehicleUtil.ParentZdoKey);
                            if (pid != ZDOID.None)
                            {
                                var pGo = VehicleUtil.FindByUid(pid);
                                if (pGo != null) nearby = pGo.GetComponent<VehicleCore>() ?? pGo.GetComponentInParent<VehicleCore>();
                            }
                        }
                    }
                }
            }

            if (selfCore != null)
            {
                if (nearby != null)
                {
                    var extraRb = GetComponent<Rigidbody>();
                    var extraSync = GetComponent<ZSyncTransform>();
                    DestroyImmediate(selfCore);
                    if (extraRb != null) DestroyImmediate(extraRb);
                    if (extraSync != null) DestroyImmediate(extraSync);
                    AttachToVehicle(nearby, null, true);
                }
                return;
            }

            bool isSeatOrEngine = GetComponent<VehicleSeat>() != null || GetComponent<VehicleEngine>() != null;
            bool isWheel = GetComponent<VehicleWheel>() != null;

            if (selfBearing != null || selfSuspension != null)
            {
                if (nearby != null) AttachToVehicle(nearby, null, true);
                return;
            }

            if (isSeatOrEngine)
            {
                if (nearby != null) AttachToVehicle(nearby, null, true);
                return;
            }

            // Check explicit or nearby suspension
            VehicleSuspension suspension = targetSuspension;
            if (suspension == null)
            {
                var stackPiece = FindClosestOtherPiece();
                if (stackPiece != null)
                {
                    suspension = stackPiece.GetComponent<VehicleSuspension>() ?? stackPiece.GetComponentInParent<VehicleSuspension>();
                    if (suspension == null && stackPiece.AttachedSuspension != null)
                    {
                        suspension = stackPiece.AttachedSuspension;
                    }
                }
            }
            if (suspension == null)
            {
                suspension = FindSuspensionTouchingOrNear(isWheel ? 0.85f : 1.4f, selfSuspension);
            }

            if (suspension != null && !suspension.IsMountSupport(transform))
            {
                AttachToSuspension(suspension, true);
                return;
            }

            VehicleBearing bearing = targetBearing;
            if (bearing == null)
            {
                var stackPiece = FindClosestOtherPiece();
                if (stackPiece != null)
                {
                    bearing = stackPiece.GetComponent<VehicleBearing>() ?? stackPiece.GetComponentInParent<VehicleBearing>();
                    if (bearing == null && stackPiece.AttachedBearing != null)
                    {
                        bearing = stackPiece.AttachedBearing;
                    }
                    if (bearing == null)
                    {
                        var nv = stackPiece.GetComponent<ZNetView>();
                        if (nv != null && nv.IsValid())
                        {
                            var bid = nv.GetZDO().GetZDOID(VehicleUtil.BearingZdoKey);
                            if (bid != ZDOID.None)
                            {
                                var bGo = VehicleUtil.FindByUid(bid);
                                bearing = bGo != null ? bGo.GetComponent<VehicleBearing>() : null;
                            }
                        }
                    }
                }
            }
            if (bearing == null)
            {
                bearing = FindBearingTouchingOrNear(isWheel ? 0.65f : 1.6f, selfBearing);
            }

            if (bearing != null && !bearing.IsMountSupport(transform))
            {
                var core = bearing.GetComponentInParent<VehicleCore>() ?? nearby;
                if (core != null) AttachToVehicle(core, bearing, true);
                else AttachToBearingOnly(bearing, true);
                return;
            }

            if (nearby != null)
            {
                AttachToVehicle(nearby, null, true);
            }
        }

        private VehicleSuspension FindSuspensionTouchingOrNear(float maxDist, VehicleSuspension exclude)
        {
            var cols = Physics.OverlapSphere(transform.position, Mathf.Max(maxDist, 2.5f));
            VehicleSuspension best = null;
            float bestDist = float.MaxValue;
            var myCols = GetComponentsInChildren<Collider>();

            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                var s = cols[i].GetComponentInParent<VehicleSuspension>();
                if (s == null || s == exclude || s.transform == transform) continue;
                Transform head = s.MovingHead != null ? s.MovingHead : s.transform;
                Vector3 headPos = head.position + s.transform.up * (s.RestLength * 0.42f);

                float d = float.MaxValue;
                if (myCols.Length > 0)
                {
                    for (int c = 0; c < myCols.Length; c++)
                    {
                        if (myCols[c] == null || myCols[c].isTrigger) continue;
                        float cd = Vector3.Distance(headPos, myCols[c].ClosestPoint(headPos));
                        if (cd < d) d = cd;
                    }
                }
                else
                {
                    d = Vector3.Distance(headPos, transform.position);
                }

                if (d < maxDist && d < bestDist)
                {
                    bestDist = d;
                    best = s;
                }
            }
            return best;
        }

        private VehicleBearing FindBearingTouchingOrNear(float maxDist, VehicleBearing exclude)
        {
            var cols = Physics.OverlapSphere(transform.position, Mathf.Max(maxDist, 2.5f));
            VehicleBearing best = null;
            float bestDist = float.MaxValue;
            var myCols = GetComponentsInChildren<Collider>();

            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                var b = cols[i].GetComponentInParent<VehicleBearing>();
                if (b == null || b == exclude) continue;
                Transform hub = b.RotatingHead != null ? b.RotatingHead : b.transform;
                Vector3 hubPos = hub.position;

                float d = float.MaxValue;
                if (myCols.Length > 0)
                {
                    for (int c = 0; c < myCols.Length; c++)
                    {
                        if (myCols[c] == null || myCols[c].isTrigger) continue;
                        float cd = Vector3.Distance(hubPos, myCols[c].ClosestPoint(hubPos));
                        if (cd < d) d = cd;
                    }
                }
                else
                {
                    d = Vector3.Distance(hubPos, transform.position);
                }

                if (d < maxDist && d < bestDist)
                {
                    bestDist = d;
                    best = b;
                }
            }
            return best;
        }

        private VehiclePiece FindClosestOtherPiece()
        {
            var cols = Physics.OverlapSphere(transform.position, 2.2f);
            VehiclePiece best = null;
            float bestDist = float.MaxValue;
            var myCols = GetComponentsInChildren<Collider>();

            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                var p = cols[i].GetComponentInParent<VehiclePiece>();
                if (p == null || p == this) continue;
                if (p.GetComponent<VehicleLift>() != null) continue;

                Vector3 otherPoint = cols[i].ClosestPoint(transform.position);
                float d = Vector3.SqrMagnitude(otherPoint - transform.position);

                if (myCols.Length > 0)
                {
                    for (int c = 0; c < myCols.Length; c++)
                    {
                        if (myCols[c] == null || myCols[c].isTrigger) continue;
                        Vector3 pt = myCols[c].ClosestPoint(otherPoint);
                        float cd = Vector3.SqrMagnitude(pt - otherPoint);
                        if (cd < d) d = cd;
                    }
                }

                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }
            return best;
        }

        public void AttachToSuspension(VehicleSuspension suspension, bool writeZdo)
        {
            if (suspension == null || suspension.MovingHead == null) return;
            if (transform == suspension.transform) return;

            AttachedSuspension = suspension;
            ParentVehicle = suspension.GetComponentInParent<VehicleCore>();

            suspension.RegisterAttachment(transform);

            if (writeZdo && GetComponent<VehicleWheel>() != null
                && Vector3.Distance(transform.position, suspension.transform.position) < 0.85f)
            {
                VehiclePlacement.SnapWheelToSuspension(transform, suspension);
                suspension.UpdateLoadPose(transform);
            }

            var childCore = GetComponent<VehicleCore>();
            if (childCore != null && childCore != ParentVehicle)
            {
                DestroyImmediate(childCore);
            }
            var childRb = GetComponent<Rigidbody>();
            if (childRb != null && (ParentVehicle == null || childRb != ParentVehicle.Rb))
            {
                DestroyImmediate(childRb);
            }

            if (WearNTearComponent != null)
            {
                WearNTearComponent.m_noRoofWear = true;
                WearNTearComponent.m_snowDamageImmune = true;
                WearNTearComponent.m_snow = null;
                WearNTearComponent.m_snowWorn = null;
                WearNTearComponent.m_snowBroken = null;
                WearNTearComponent.m_noSupportWear = true;
                WearNTearComponent.m_supports = true;
            }

            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                if (col != null && !col.isTrigger)
                {
                    col.sharedMaterial = VehicleUtil.SmoothPhysicMaterial;
                }
            }

            if (ParentVehicle != null)
            {
                ParentVehicle.RegisterPiece(this);
            }

            if (writeZdo)
            {
                PersistAttachment(ParentVehicle, null, suspension);
            }

            Plugin.Log.LogInfo($"Attached '{name}' to suspension '{suspension.name}'");
        }

        public void AttachToBearingOnly(VehicleBearing bearing, bool writeZdo)
        {
            if (bearing == null || bearing.RotatingHead == null) return;
            if (transform == bearing.transform) return;

            AttachedBearing = bearing;
            bearing.RegisterAttachment(transform);
            if (GetComponent<VehicleWheel>() != null
                && Vector3.Distance(transform.position, bearing.transform.position) < 0.65f)
            {
                VehiclePlacement.SnapWheelToBearing(transform, bearing);
                bearing.UpdateLoadPose(transform);
            }

            if (writeZdo)
            {
                PersistAttachment(null, bearing);
            }
        }

        public void AttachToVehicle(VehicleCore vehicle, VehicleBearing bearing = null, bool writeZdo = true)
        {
            if (vehicle == null) return;
            if (transform == vehicle.transform) return;

            ParentVehicle = vehicle;
            AttachedBearing = bearing;

            Transform parent = vehicle.transform;
            if (bearing != null && bearing.RotatingHead != null)
            {
                parent = bearing.RotatingHead;
                bearing.RegisterAttachment(transform);
            }
            else
            {
                VehicleUtil.ParentKeepWorld(transform, parent);
            }

            if (writeZdo && GetComponent<VehicleWheel>() != null && bearing != null
                && Vector3.Distance(transform.position, bearing.transform.position) < 0.65f)
            {
                VehiclePlacement.SnapWheelToBearing(transform, bearing);
                bearing.UpdateLoadPose(transform);
            }

            // Child pieces must not simulate as separate bodies.
            var childCore = GetComponent<VehicleCore>();
            if (childCore != null && childCore != vehicle)
            {
                DestroyImmediate(childCore);
            }
            var childRb = GetComponent<Rigidbody>();
            if (childRb != null && childRb != vehicle.Rb)
            {
                DestroyImmediate(childRb);
            }

            if (WearNTearComponent != null)
            {
                WearNTearComponent.m_noRoofWear = true;
                WearNTearComponent.m_snowDamageImmune = true;
                WearNTearComponent.m_snow = null;
                WearNTearComponent.m_snowWorn = null;
                WearNTearComponent.m_snowBroken = null;
                WearNTearComponent.m_noSupportWear = true;
                WearNTearComponent.m_supports = true;
            }

            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                if (col != null && !col.isTrigger)
                {
                    col.sharedMaterial = VehicleUtil.SmoothPhysicMaterial;
                }
            }

            vehicle.RegisterPiece(this);

            if (writeZdo)
            {
                PersistAttachment(vehicle, bearing);
            }

            Plugin.Log.LogInfo($"Attached '{name}' to vehicle '{vehicle.name}' parent='{parent.name}'");
        }

        private void PersistAttachment(VehicleCore vehicle, VehicleBearing bearing, VehicleSuspension suspension = null)
        {
            if (_nview == null) _nview = GetComponent<ZNetView>();
            if (_nview == null || !_nview.IsValid()) return;

            var zdo = _nview.GetZDO();
            ZDOID parentId = vehicle != null ? VehicleUtil.GetUid(vehicle) : (bearing != null ? VehicleUtil.GetUid(bearing) : (suspension != null ? VehicleUtil.GetUid(suspension) : ZDOID.None));
            zdo.Set(VehicleUtil.ParentZdoKey, parentId);
            zdo.Set(VehicleUtil.BearingZdoKey, bearing != null ? VehicleUtil.GetUid(bearing) : ZDOID.None);
            zdo.Set(VehicleUtil.SuspensionZdoKey, suspension != null ? VehicleUtil.GetUid(suspension) : ZDOID.None);
            zdo.SetPosition(transform.position);
            zdo.SetRotation(transform.rotation);
            zdo.Set(VehicleUtil.LocalPosZdoKey, transform.localPosition);
            zdo.Set(VehicleUtil.LocalRotZdoKey, transform.localRotation);
        }

        public void SyncWorldZdo()
        {
            if (_nview == null) _nview = GetComponent<ZNetView>();
            if (_nview == null || !_nview.IsValid()) return;
            if (!_nview.IsOwner())
            {
                if (ParentVehicle != null && ParentVehicle.NetView != null && ParentVehicle.NetView.IsOwner())
                {
                    _nview.ClaimOwnership();
                }
                else
                {
                    return;
                }
            }
            var zdo = _nview.GetZDO();
            zdo.SetPosition(transform.position);
            zdo.SetRotation(transform.rotation);
            zdo.Set(VehicleUtil.LocalPosZdoKey, transform.localPosition);
            zdo.Set(VehicleUtil.LocalRotZdoKey, transform.localRotation);
        }

        public void DetachFromVehicle()
        {
            if (AttachedSuspension != null)
            {
                AttachedSuspension.UnregisterAttachment(transform);
                AttachedSuspension = null;
            }
            if (AttachedBearing != null)
            {
                AttachedBearing.UnregisterAttachment(transform);
                AttachedBearing = null;
            }
            if (ParentVehicle != null)
            {
                ParentVehicle.UnregisterPiece(this);
                ParentVehicle = null;
            }
        }

        private void OnDestroy()
        {
            DetachFromVehicle();
        }
    }
}
