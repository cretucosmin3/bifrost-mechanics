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
        public Piece PieceComponent { get; private set; }
        public WearNTear WearNTearComponent { get; private set; }

        private ZNetView _nview;
        private int _restoreAttempts;
        private bool _restoreDone;

        private void Awake()
        {
            if (GetComponent<VehicleRope>() != null)
            {
                Destroy(this);
                return;
            }

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

            if (_nview == null) _nview = GetComponent<ZNetView>();
            var zdo = _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;
            var parentId = zdo != null ? zdo.GetZDOID(VehicleUtil.ParentZdoKey) : ZDOID.None;
            var bearingId = zdo != null ? zdo.GetZDOID(VehicleUtil.BearingZdoKey) : ZDOID.None;

            if (parentId != ZDOID.None || bearingId != ZDOID.None)
            {
                StripStrayVehicleComponents();
                InvokeRepeating(nameof(TryRestoreAttachment), 0.05f, 0.15f);
                return;
            }

            if (GetComponent<VehicleCore>() != null) return;
            if (GetComponent<VehicleLift>() != null) return;

            InvokeRepeating(nameof(TryRestoreAttachment), 0.05f, 0.15f);
        }

        public void OnPlaced()
        {
            TryAttachFromWorld(null, false);
        }

        public void OnPlaced(VehicleBearing targetBearing, bool directHubHit = false)
        {
            TryAttachFromWorld(targetBearing, directHubHit);
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

                if (parentId != ZDOID.None || bearingId != ZDOID.None)
                {
                    GameObject parentGo = parentId != ZDOID.None ? VehicleUtil.FindByUid(parentId) : null;
                    VehicleCore core = parentGo != null ? (parentGo.GetComponent<VehicleCore>() ?? parentGo.GetComponentInParent<VehicleCore>()) : null;

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

                    if (parentId != ZDOID.None && core == null && bearing == null && _restoreAttempts < 50)
                    {
                        return; // Wait for parent/core to spawn
                    }

                    if (core == null && bearing == null)
                    {
                        if (_restoreAttempts < 50) return;
                        CancelInvoke(nameof(TryRestoreAttachment));
                        return;
                    }

                    Vector3 localPos = _nview.GetZDO().GetVec3(VehicleUtil.LocalPosZdoKey, transform.localPosition);
                    Quaternion localRot = _nview.GetZDO().GetQuaternion(VehicleUtil.LocalRotZdoKey, transform.localRotation);

                    if (core != null)
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
                    if (bearing != null)
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

        public void TryAttachFromWorld(VehicleBearing targetBearing = null, bool directHubHit = false)
        {
            if (VehicleUtil.PrefabName(gameObject) == "valhicle_lift") return;
            if (GetComponent<VehicleRope>() != null) return;

            var selfCore = GetComponent<VehicleCore>();
            var selfBearing = GetComponent<VehicleBearing>();
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
                    StripStrayVehicleComponents(nearby);
                    AttachToVehicle(nearby, targetBearing, true, directHubHit);
                }
                return;
            }

            bool isSeatOrEngine = GetComponent<VehicleSeat>() != null || GetComponent<VehicleEngine>() != null;
            bool isWheel = GetComponent<VehicleWheel>() != null;

            if (selfBearing != null)
            {
                if (targetBearing != null && targetBearing != selfBearing)
                {
                    var core = targetBearing.GetComponentInParent<VehicleCore>() ?? nearby;
                    if (core != null) AttachToVehicle(core, targetBearing, true, directHubHit);
                    else AttachToBearingOnly(targetBearing, true, directHubHit);
                    return;
                }
                if (nearby != null) AttachToVehicle(nearby, null, true);
                return;
            }

            if (isSeatOrEngine)
            {
                if (nearby != null) AttachToVehicle(nearby, null, true);
                return;
            }

            // Check explicit or nearby bearing
            VehicleBearing bearing = targetBearing;
            if (bearing == null)
            {
                bearing = FindBearingTouchingHub(0.30f, selfBearing);
                if (bearing != null) directHubHit = true;
            }

            if (bearing != null && (directHubHit || targetBearing == bearing || !bearing.IsMountSupport(transform)))
            {
                var core = bearing.GetComponentInParent<VehicleCore>() ?? nearby;
                if (core != null) AttachToVehicle(core, bearing, true, directHubHit);
                else AttachToBearingOnly(bearing, true, directHubHit);
                return;
            }

            if (nearby != null)
            {
                AttachToVehicle(nearby, null, true);
            }
        }

        private VehicleBearing FindBearingTouchingHub(float maxDist, VehicleBearing exclude)
        {
            var cols = Physics.OverlapSphere(transform.position, Mathf.Max(maxDist, 0.4f));
            VehicleBearing best = null;
            float bestDist = float.MaxValue;
            var myCols = GetComponentsInChildren<Collider>();

            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                var b = cols[i].GetComponentInParent<VehicleBearing>();
                if (b == null || b == exclude || b.transform == transform) continue;
                Transform hub = b.RotatingHead != null ? b.RotatingHead : b.transform;
                Vector3 hubPos = hub.position;

                Vector3 checkPoint = transform.position;
                if (myCols != null && myCols.Length > 0)
                {
                    float minColDist = float.MaxValue;
                    for (int c = 0; c < myCols.Length; c++)
                    {
                        if (myCols[c] == null || myCols[c].isTrigger) continue;
                        Vector3 cp = myCols[c].ClosestPoint(hubPos);
                        float cd = Vector3.Distance(hubPos, cp);
                        if (cd < minColDist)
                        {
                            minColDist = cd;
                            checkPoint = cp;
                        }
                    }
                }

                // Must be on the front button face (+up), NOT on the mount plate (-up)
                Vector3 toTransform = checkPoint - b.transform.position;
                float projUp = Vector3.Dot(toTransform, b.transform.up);
                if (projUp < -0.02f) continue; // Behind or on mount plate

                Vector3 radial = toTransform - b.transform.up * projUp;
                if (radial.magnitude > 0.22f) continue; // Outside button radius

                float d = Vector3.Distance(hubPos, checkPoint);
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

        public void StripStrayVehicleComponents(VehicleCore exceptCore = null)
        {
            var childCore = GetComponent<VehicleCore>();
            if (childCore != null && childCore != exceptCore)
            {
                DestroyImmediate(childCore);
            }
            var childLift = GetComponent<VehicleLift>();
            if (childLift != null)
            {
                DestroyImmediate(childLift);
            }
            var childRb = GetComponent<Rigidbody>();
            if (childRb != null && (exceptCore == null || childRb != exceptCore.Rb))
            {
                DestroyImmediate(childRb);
            }
            foreach (var sync in GetComponentsInChildren<ZSyncTransform>(true))
            {
                if (sync != null && (exceptCore == null || sync.gameObject != exceptCore.gameObject))
                {
                    DestroyImmediate(sync);
                }
            }
        }

        public void AttachToBearingOnly(VehicleBearing bearing, bool writeZdo, bool snapToBearing = false)
        {
            if (bearing == null || bearing.RotatingHead == null) return;
            if (transform == bearing.transform) return;

            StripStrayVehicleComponents(null);

            AttachedBearing = bearing;
            bearing.RegisterAttachment(transform);
            if (GetComponent<VehicleWheel>() != null)
            {
                if (snapToBearing)
                {
                    VehiclePlacement.SnapWheelToBearing(transform, bearing);
                }
                bearing.UpdateLoadPose(transform);
            }

            if (writeZdo)
            {
                PersistAttachment(null, bearing);
            }
        }

        public void AttachToVehicle(VehicleCore vehicle, VehicleBearing bearing = null, bool writeZdo = true, bool snapToBearing = false)
        {
            if (vehicle == null) return;
            if (transform == vehicle.transform) return;

            // Child pieces must not simulate as separate bodies or fight parent sync.
            StripStrayVehicleComponents(vehicle);

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

            if (writeZdo && GetComponent<VehicleWheel>() != null && bearing != null)
            {
                if (snapToBearing)
                {
                    VehiclePlacement.SnapWheelToBearing(transform, bearing);
                }
                bearing.UpdateLoadPose(transform);
            }

            VehicleUtil.StripSnowOverlays(gameObject);

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

        private void PersistAttachment(VehicleCore vehicle, VehicleBearing bearing)
        {
            if (_nview == null) _nview = GetComponent<ZNetView>();
            if (_nview == null || !_nview.IsValid()) return;

            var zdo = _nview.GetZDO();
            ZDOID parentId = vehicle != null ? VehicleUtil.GetUid(vehicle) : (bearing != null ? VehicleUtil.GetUid(bearing) : ZDOID.None);
            zdo.Set(VehicleUtil.ParentZdoKey, parentId);
            zdo.Set(VehicleUtil.BearingZdoKey, bearing != null ? VehicleUtil.GetUid(bearing) : ZDOID.None);
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
