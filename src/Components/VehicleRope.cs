using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Valhicle.Core;
using Valhicle.Patches;
using Valhicle.Prefabs;

namespace Valhicle.Components
{
    public enum RopeMode
    {
        Stiff = 0,    // Rigid Cable / Solid Tie-Down: Locks structures rock-solid, zero sag
        Elastic = 1   // Springy Bungee: Dynamic vehicle suspension & shock absorption
    }

    /// <summary>
    /// Interactive 2-point rope that can tie down cargo, tether carts to hitching posts, tow trailers,
    /// or mechanically steer/rotate bearings.
    /// The rope is a separate physical entity that is NEVER parented to or merged into vehicle chassis hierarchies.
    /// It exerts spring-damper tension forces on rigidbodies and rotational torques on bearings.
    /// </summary>
    [DefaultExecutionOrder(20002)]
    public class VehicleRope : MonoBehaviour, Interactable, Hoverable, TextReceiver
    {
        public static VehicleRope ActivePendingRope;
        public static Material CleatMaterial;

        public RopeMode Mode = RopeMode.Stiff;
        public float RestLength = 2.0f;
        public float CurrentTension { get; private set; }
        public float CurrentDistance { get; private set; }
        public bool IsConfigured { get; private set; }

        public Transform AnchorBTransform;
        public LineRenderer RopeLine;

        // Attachment info Anchor A
        public Transform ParentA;
        public Vector3 LocalPosA;
        public Quaternion LocalRotA = Quaternion.identity;
        public VehicleBearing BearingA;
        public VehicleCore CoreA;
        public Rigidbody RbA;
        private ZDOID _targetZdoA = ZDOID.None;
        private bool _isBearingA;

        // Attachment info Anchor B
        public Transform ParentB;
        public Vector3 LocalPosB;
        public Quaternion LocalRotB = Quaternion.identity;
        public VehicleBearing BearingB;
        public VehicleCore CoreB;
        public Rigidbody RbB;
        private ZDOID _targetZdoB = ZDOID.None;
        private bool _isBearingB;
        private Vector3 _worldPosB;

        private ZNetView _nview;
        private bool _restoreDone;
        private int _restoreAttempts;
        private float _arrowHoldTimer;
        private float _arrowRepeatTimer;

        private const int LineSegments = 20;
        private readonly Vector3[] _linePoints = new Vector3[LineSegments];

        private static Material _cachedHarpoonMaterial;

        public static Material GetHarpoonMaterial()
        {
            if (_cachedHarpoonMaterial != null) return _cachedHarpoonMaterial;

            try
            {
                // 1. Check ObjectDB for SpearChitin (Abyssal Harpoon) attack status effect
                if (ObjectDB.instance != null)
                {
                    var spear = ObjectDB.instance.GetItemPrefab("SpearChitin");
                    if (spear != null)
                    {
                        var itemDrop = spear.GetComponent<ItemDrop>();
                        var se = itemDrop?.m_itemData?.m_shared?.m_attackStatusEffect as SE_Harpooned;
                        if (se != null && se.m_startEffects?.m_effectPrefabs != null)
                        {
                            foreach (var ep in se.m_startEffects.m_effectPrefabs)
                            {
                                if (ep.m_prefab == null) continue;
                                var lr = ep.m_prefab.GetComponentInChildren<LineRenderer>(true);
                                if (lr != null && lr.sharedMaterial != null)
                                {
                                    _cachedHarpoonMaterial = lr.sharedMaterial;
                                    Plugin.Log.LogInfo($"Found harpoon rope material from SpearChitin: {_cachedHarpoonMaterial.name}");
                                    return _cachedHarpoonMaterial;
                                }
                            }
                        }
                    }
                }

                // 2. Check ZNetScene for prefabs with LineRenderer
                if (ZNetScene.instance != null)
                {
                    string[] candidates = { "vfx_Harpooned", "harpoon_wire", "fishingfloat", "projectile_chitinharpoon", "vfx_harpoon_pull" };
                    foreach (var c in candidates)
                    {
                        var p = ZNetScene.instance.GetPrefab(c);
                        if (p != null)
                        {
                            var lr = p.GetComponentInChildren<LineRenderer>(true);
                            if (lr != null && lr.sharedMaterial != null)
                            {
                                _cachedHarpoonMaterial = lr.sharedMaterial;
                                Plugin.Log.LogInfo($"Found line material from {c}: {_cachedHarpoonMaterial.name}");
                                return _cachedHarpoonMaterial;
                            }
                        }
                    }

                    // Scan scene prefabs for any line / harpoon LineRenderer
                    if (ZNetScene.instance.m_prefabs != null)
                    {
                        foreach (var go in ZNetScene.instance.m_prefabs)
                        {
                            if (go == null) continue;
                            string name = go.name;
                            if (name.IndexOf("harpoon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("wire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("line", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var lr = go.GetComponentInChildren<LineRenderer>(true);
                                if (lr != null && lr.sharedMaterial != null)
                                {
                                    _cachedHarpoonMaterial = lr.sharedMaterial;
                                    Plugin.Log.LogInfo($"Found line material from {name}: {_cachedHarpoonMaterial.name}");
                                    return _cachedHarpoonMaterial;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"VehicleRope.GetHarpoonMaterial error: {ex.Message}");
            }

            // 3. Fallback: double-sided unlit rope material
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Custom/Line")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Standard");

            if (shader != null)
            {
                var mat = new Material(shader)
                {
                    name = "VehicleRope_MaterialFallback"
                };
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // Double-sided (no backface culling)
                if (mat.HasProperty("_Color")) mat.color = new Color(0.55f, 0.44f, 0.30f); // Hemp rope tone
                _cachedHarpoonMaterial = mat;
                return _cachedHarpoonMaterial;
            }

            return null;
        }

        public static void CreateCleatVisual(Transform parent, Material metalMat)
        {
            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            basePlate.name = "CleatPlate";
            basePlate.transform.SetParent(parent, false);
            basePlate.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            basePlate.transform.localScale = new Vector3(0.18f, 0.015f, 0.18f);
            var colBase = basePlate.GetComponent<Collider>();
            if (colBase != null) UnityEngine.Object.DestroyImmediate(colBase);
            if (metalMat != null) VehicleUtil.ApplySharedMaterial(basePlate, metalMat);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "CleatRing";
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(0.11f, 0.022f, 0.11f);
            var colRing = ring.GetComponent<Collider>();
            if (colRing != null) UnityEngine.Object.DestroyImmediate(colRing);
            if (metalMat != null) VehicleUtil.ApplySharedMaterial(ring, metalMat);

            var col = parent.GetComponent<Collider>();
            if (col == null)
            {
                var box = parent.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.25f, 0.15f, 0.25f);
                box.center = new Vector3(0f, 0.04f, 0f);
                box.isTrigger = true;
            }
            else
            {
                col.isTrigger = true;
            }
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>() ?? GetComponentInParent<ZNetView>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c != null) c.isTrigger = true;
            }
            EnsureVisuals();

            var wnt = GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.m_noRoofWear = true;
                wnt.m_noSupportWear = true;
                wnt.m_supports = true;
                wnt.m_snowDamageImmune = true;
            }
        }

        private void Start()
        {
            if (_nview != null && _nview.IsValid())
            {
                var zdo = _nview.GetZDO();
                int modeInt = zdo.GetInt(GetZdoKey("Mode"), -1);
                if (modeInt >= 0)
                {
                    Mode = (RopeMode)modeInt;
                }
                else
                {
                    // Backward compatibility: old Elasticity enum (0: Stiff, 1: Tow, 2: Bungee, 3: Loose)
                    int oldElast = zdo.GetInt(GetZdoKey("Elasticity"), 0);
                    Mode = (oldElast == 2) ? RopeMode.Elastic : RopeMode.Stiff;
                }
                RestLength = zdo.GetFloat(GetZdoKey("RestLength"), RestLength);
                IsConfigured = zdo.GetBool(GetZdoKey("Configured"), false);

                if (IsConfigured && !_restoreDone)
                {
                    InvokeRepeating(nameof(TryRestore), 0.1f, 0.25f);
                }
            }
        }

        private void OnDestroy()
        {
            if (ActivePendingRope == this)
            {
                ActivePendingRope = null;
            }
            if (AnchorBTransform != null && AnchorBTransform.gameObject != gameObject)
            {
                Destroy(AnchorBTransform.gameObject);
            }
        }

        public void EnsureAnchorB()
        {
            if (AnchorBTransform != null) return;

            var existing = transform.Find("AnchorB");
            if (existing != null)
            {
                AnchorBTransform = existing;
                foreach (var c in AnchorBTransform.GetComponentsInChildren<Collider>(true))
                {
                    if (c != null) c.isTrigger = true;
                }
            }
            else
            {
                var go = new GameObject("AnchorB");
                AnchorBTransform = go.transform;
                AnchorBTransform.position = IsConfigured ? GetAnchorBPosition() : transform.position;
                AnchorBTransform.rotation = transform.rotation;

                Material mat = CleatMaterial ?? PrefabRegistry.IronMaterial ?? PrefabRegistry.BronzeMaterial ?? PrefabRegistry.WoodMaterial;
                CreateCleatVisual(AnchorBTransform, mat);

                foreach (var c in AnchorBTransform.GetComponentsInChildren<Collider>(true))
                {
                    if (c != null) c.isTrigger = true;
                }

                var proxy = go.AddComponent<VehicleRopeAnchorProxy>();
                proxy.Rope = this;
            }
        }

        public void EnsureVisuals()
        {
            if (RopeLine == null)
            {
                RopeLine = GetComponent<LineRenderer>();
                if (RopeLine == null)
                {
                    RopeLine = gameObject.AddComponent<LineRenderer>();
                }
            }

            if (RopeLine != null)
            {
                RopeLine.useWorldSpace = true;
                RopeLine.alignment = LineAlignment.View;
                RopeLine.textureMode = LineTextureMode.Tile;
                RopeLine.startWidth = 0.035f;
                RopeLine.endWidth = 0.035f;
                RopeLine.numCapVertices = 4;
                RopeLine.numCornerVertices = 4;
                RopeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                RopeLine.receiveShadows = true;

                if (RopeLine.sharedMaterial == null || RopeLine.sharedMaterial.name.Contains("Wood") || RopeLine.sharedMaterial.name.Contains("Piece"))
                {
                    var harpoonMat = GetHarpoonMaterial();
                    if (harpoonMat != null)
                    {
                        RopeLine.sharedMaterial = harpoonMat;
                    }
                }
            }

            if (IsConfigured)
            {
                EnsureAnchorB();
            }
        }

        private void IgnorePieceCollisions(Transform targetTransform, VehicleCore core)
        {
            if (targetTransform == null && core == null) return;
            var myCols = GetComponentsInChildren<Collider>(true);
            var targetCols = core != null ? core.GetComponentsInChildren<Collider>(true) : targetTransform.GetComponentsInChildren<Collider>(true);
            if (myCols == null || targetCols == null) return;
            for (int i = 0; i < myCols.Length; i++)
            {
                if (myCols[i] == null) continue;
                for (int j = 0; j < targetCols.Length; j++)
                {
                    if (targetCols[j] == null) continue;
                    Physics.IgnoreCollision(myCols[i], targetCols[j], true);
                }
            }
            if (AnchorBTransform != null)
            {
                var bCols = AnchorBTransform.GetComponentsInChildren<Collider>(true);
                if (bCols != null)
                {
                    for (int i = 0; i < bCols.Length; i++)
                    {
                        if (bCols[i] == null) continue;
                        for (int j = 0; j < targetCols.Length; j++)
                        {
                            if (targetCols[j] == null) continue;
                            Physics.IgnoreCollision(bCols[i], targetCols[j], true);
                        }
                    }
                }
            }
        }

        public static void ResolveAnchorTarget(
            Collider hitCol, Vector3 worldPos,
            out Transform parentTransform,
            out VehicleCore core,
            out VehicleBearing bearing,
            out Rigidbody rb,
            out ZDOID targetZdo,
            out bool isBearing)
        {
            parentTransform = null;
            core = null;
            bearing = null;
            rb = null;
            targetZdo = ZDOID.None;
            isBearing = false;

            if (hitCol == null) return;

            // 1. Check if hit belongs to a VehicleBearing rotating head
            var b = hitCol.GetComponentInParent<VehicleBearing>();
            var vp = hitCol.GetComponentInParent<VehiclePiece>();
            if (b == null && vp != null && vp.AttachedBearing != null)
            {
                b = vp.AttachedBearing;
            }

            if (b != null && (hitCol.transform == b.RotatingHead || hitCol.transform.IsChildOf(b.RotatingHead) || b.Owns(hitCol.transform)))
            {
                bearing = b;
                parentTransform = b.RotatingHead;
                isBearing = true;
                core = b.GetComponentInParent<VehicleCore>() ?? (vp != null ? vp.ParentVehicle : null);
                rb = core != null ? core.Rb : b.GetComponentInParent<Rigidbody>();
                var nv = b.GetComponent<ZNetView>() ?? b.GetComponentInParent<ZNetView>();
                if (nv != null && nv.IsValid()) targetZdo = nv.GetZDO().m_uid;
                return;
            }

            // 2. Check if hit belongs to a VehicleCore / VehicleLift / VehiclePiece on a vehicle
            var c = hitCol.GetComponentInParent<VehicleCore>();
            if (c == null)
            {
                var lift = hitCol.GetComponentInParent<VehicleLift>();
                if (lift != null) c = lift.Core;
            }
            if (c == null && vp != null && vp.ParentVehicle != null)
            {
                c = vp.ParentVehicle;
            }
            if (c == null && b != null)
            {
                c = b.GetComponentInParent<VehicleCore>();
            }

            if (c != null)
            {
                core = c;
                parentTransform = c.transform;
                rb = c.Rb;
                var nv = c.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid()) targetZdo = nv.GetZDO().m_uid;
                return;
            }

            // 3. Dynamic Rigidbody (e.g. vanilla cart or boat)
            var r = hitCol.GetComponentInParent<Rigidbody>();
            if (r != null && !r.isKinematic)
            {
                parentTransform = r.transform;
                rb = r;
                var nv = r.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid()) targetZdo = nv.GetZDO().m_uid;
                return;
            }

            // 4. Static piece (wall, floor, hitching post)
            var piece = hitCol.GetComponentInParent<Piece>();
            if (piece != null)
            {
                parentTransform = piece.transform;
                var nv = piece.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid()) targetZdo = nv.GetZDO().m_uid;
                return;
            }
        }

        public void InitAnchorA(Vector3 worldPos, Collider hitCol, Vector3 hitPoint)
        {
            transform.position = worldPos;
            transform.SetParent(null); // Keep completely independent entity in world root

            if (hitCol == null || Vector3.Distance(worldPos, hitPoint) > 1.8f)
            {
                var nearby = Physics.OverlapSphere(worldPos, 0.40f, LayerMask.GetMask("Default", "piece", "piece_nonsolid", "static_solid"), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < nearby.Length; i++)
                {
                    if (nearby[i].gameObject == gameObject) continue;
                    if (AnchorBTransform != null && nearby[i].gameObject == AnchorBTransform.gameObject) continue;
                    if (nearby[i].GetComponentInParent<VehicleBearing>() != null
                        || nearby[i].GetComponentInParent<VehicleCore>() != null
                        || nearby[i].GetComponentInParent<VehicleLift>() != null
                        || nearby[i].GetComponentInParent<VehiclePiece>() != null
                        || nearby[i].GetComponentInParent<Piece>() != null)
                    {
                        hitCol = nearby[i];
                        break;
                    }
                }
            }

            ResolveAnchorTarget(hitCol, worldPos, out ParentA, out CoreA, out BearingA, out RbA, out _targetZdoA, out _isBearingA);

            if (ParentA != null)
            {
                LocalPosA = ParentA.InverseTransformPoint(worldPos);
                LocalRotA = Quaternion.Inverse(ParentA.rotation) * transform.rotation;
            }
            else
            {
                LocalPosA = worldPos;
                LocalRotA = transform.rotation;
            }

            IgnorePieceCollisions(ParentA, CoreA);
            SaveZdo();
        }

        public void InitAnchorB(Vector3 worldPos, Collider hitCol, Vector3 hitPoint)
        {
            _worldPosB = worldPos;

            EnsureAnchorB();
            if (AnchorBTransform != null)
            {
                AnchorBTransform.position = worldPos;
                AnchorBTransform.SetParent(null); // Keep completely independent entity in world root
            }

            if (hitCol == null || Vector3.Distance(worldPos, hitPoint) > 1.8f)
            {
                var nearby = Physics.OverlapSphere(worldPos, 0.40f, LayerMask.GetMask("Default", "piece", "piece_nonsolid", "static_solid"), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < nearby.Length; i++)
                {
                    if (nearby[i].gameObject == gameObject) continue;
                    if (AnchorBTransform != null && nearby[i].gameObject == AnchorBTransform.gameObject) continue;
                    if (nearby[i].GetComponentInParent<VehicleBearing>() != null
                        || nearby[i].GetComponentInParent<VehicleCore>() != null
                        || nearby[i].GetComponentInParent<VehicleLift>() != null
                        || nearby[i].GetComponentInParent<VehiclePiece>() != null
                        || nearby[i].GetComponentInParent<Piece>() != null)
                    {
                        hitCol = nearby[i];
                        break;
                    }
                }
            }

            ResolveAnchorTarget(hitCol, worldPos, out ParentB, out CoreB, out BearingB, out RbB, out _targetZdoB, out _isBearingB);

            if (ParentB != null)
            {
                LocalPosB = ParentB.InverseTransformPoint(worldPos);
                LocalRotB = Quaternion.Inverse(ParentB.rotation) * (AnchorBTransform != null ? AnchorBTransform.rotation : Quaternion.identity);
            }
            else
            {
                LocalPosB = worldPos;
                LocalRotB = AnchorBTransform != null ? AnchorBTransform.rotation : Quaternion.identity;
            }

            IgnorePieceCollisions(ParentB, CoreB);

            Vector3 posA = GetAnchorAPosition();
            RestLength = Mathf.Max(0.20f, (float)Math.Round(Vector3.Distance(posA, worldPos), 2));
            IsConfigured = true;

            EnsureVisuals();
            SaveZdo();
        }

        public static void CancelPendingRope()
        {
            if (ActivePendingRope != null)
            {
                var target = ActivePendingRope;
                ActivePendingRope = null;
                if (target != null && !target.IsConfigured)
                {
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Rope placement cancelled.");

                    var piece = target.GetComponent<Piece>();
                    if (piece != null && piece.m_resources != null && Player.m_localPlayer != null)
                    {
                        foreach (var req in piece.m_resources)
                        {
                            if (req != null && req.m_resItem != null && req.m_amount > 0)
                            {
                                Player.m_localPlayer.GetInventory()?.AddItem(req.m_resItem.gameObject, req.m_amount);
                            }
                        }
                    }

                    if (target._nview != null && target._nview.IsValid())
                    {
                        target._nview.ClaimOwnership();
                        ZNetScene.instance?.Destroy(target.gameObject);
                    }
                    else
                    {
                        Destroy(target.gameObject);
                    }
                }
            }
        }

        public Vector3 GetAnchorAPosition()
        {
            if (ParentA != null)
            {
                return ParentA.TransformPoint(LocalPosA);
            }
            return LocalPosA != Vector3.zero ? LocalPosA : transform.position;
        }

        public Vector3 GetAnchorBPosition()
        {
            if (ParentB != null)
            {
                return ParentB.TransformPoint(LocalPosB);
            }
            if (IsConfigured)
            {
                return _worldPosB;
            }
            if (AnchorBTransform != null)
            {
                return AnchorBTransform.position;
            }
            return transform.position + transform.forward * RestLength;
        }

        public float EffectiveRestLength => Mathf.Max(0.2f, RestLength);

        public void GetPhysicalProperties(out float stiffness, out float damping, out float maxTension)
        {
            switch (Mode)
            {
                case RopeMode.Elastic:
                    stiffness = 1800f;
                    damping = 120f;
                    maxTension = 3500f;
                    break;
                case RopeMode.Stiff:
                default:
                    stiffness = 6000f;
                    damping = 220f;
                    maxTension = 6500f;
                    break;
            }
        }

        private void FixedUpdate()
        {
            if (!IsConfigured)
            {
                CurrentTension = 0f;
                return;
            }

            // Do not apply forces if vehicles are frozen or on lift
            if (CoreA != null && (CoreA.IsOnLift || CoreA.Rb == null || CoreA.Rb.isKinematic)) return;
            if (CoreB != null && (CoreB.IsOnLift || CoreB.Rb == null || CoreB.Rb.isKinematic)) return;

            Vector3 posA = GetAnchorAPosition();
            Vector3 posB = GetAnchorBPosition();
            Vector3 delta = posB - posA;
            CurrentDistance = delta.magnitude;
            if (CurrentDistance < 0.001f) return;

            Vector3 dir = delta / CurrentDistance;
            float targetLen = EffectiveRestLength;
            float stretch = CurrentDistance - targetLen;

            // Ropes only exert tension forces when pulled beyond their rest length (cannot push)
            if (stretch <= 0f)
            {
                CurrentTension = 0f;
                return;
            }

            GetPhysicalProperties(out float stiffness, out float damping, out float maxTension);

            // Relative velocity along rope axis (including bearing arm rotational velocity)
            Vector3 velA = Vector3.zero;
            Vector3 velB = Vector3.zero;
            if (RbA != null && !RbA.isKinematic) velA = RbA.GetPointVelocity(posA);
            if (RbB != null && !RbB.isKinematic) velB = RbB.GetPointVelocity(posB);

            if (BearingA != null && BearingA.RotatingHead != null)
            {
                Vector3 omegaVecA = BearingA.transform.up * (BearingA.CurrentAngularVelocity * Mathf.Deg2Rad);
                velA += Vector3.Cross(omegaVecA, posA - BearingA.RotatingHead.position);
            }
            if (BearingB != null && BearingB.RotatingHead != null)
            {
                Vector3 omegaVecB = BearingB.transform.up * (BearingB.CurrentAngularVelocity * Mathf.Deg2Rad);
                velB += Vector3.Cross(omegaVecB, posB - BearingB.RotatingHead.position);
            }

            float relVel = Vector3.Dot(velB - velA, dir);

            float springForce = stretch * stiffness;
            float damperForce = relVel * damping;
            float totalTension = Mathf.Clamp(springForce + damperForce, 0f, maxTension);
            CurrentTension = totalTension;

            Vector3 forceOnA = dir * totalTension;   // Pulls A towards B
            Vector3 forceOnB = -dir * totalTension;  // Pulls B towards A

            // 1. Apply linear forces to bodies (ONLY if on different rigidbodies)
            // If both anchors are on the same vehicle, this is an internal spring/suspension.
            // Applying opposing forces on the same Rigidbody causes severe torque couples and PhysX explosions.
            bool sameVehicle = (RbA != null && RbB != null && RbA == RbB) || (CoreA != null && CoreB != null && CoreA == CoreB);

            if (!sameVehicle)
            {
                if (RbA != null && !RbA.isKinematic)
                {
                    // Clamp max linear force so vehicle/platform is never accelerated beyond 25 m/s² (2.5g)
                    float maxBodyForceA = Mathf.Min(maxTension, RbA.mass * 25f);
                    Vector3 clampedForceA = Vector3.ClampMagnitude(forceOnA, maxBodyForceA);
                    RbA.AddForceAtPosition(clampedForceA, posA, ForceMode.Force);
                }

                if (RbB != null && !RbB.isKinematic)
                {
                    float maxBodyForceB = Mathf.Min(maxTension, RbB.mass * 25f);
                    Vector3 clampedForceB = Vector3.ClampMagnitude(forceOnB, maxBodyForceB);
                    RbB.AddForceAtPosition(clampedForceB, posB, ForceMode.Force);
                }
            }

            // 2. Exert mechanical torque on rotating bearing A (if attached to bearing head)
            if (BearingA != null && BearingA.RotatingHead != null)
            {
                Vector3 rA = posA - BearingA.RotatingHead.position;
                Vector3 torqueVecA = Vector3.Cross(rA, forceOnA);
                float torqueA = Vector3.Dot(BearingA.transform.up, torqueVecA);
                BearingA.AddTorque(torqueA);
            }

            // 3. Exert mechanical torque on rotating bearing B (if attached to bearing head)
            if (BearingB != null && BearingB.RotatingHead != null)
            {
                Vector3 rB = posB - BearingB.RotatingHead.position;
                Vector3 torqueVecB = Vector3.Cross(rB, forceOnB);
                float torqueB = Vector3.Dot(BearingB.transform.up, torqueVecB);
                BearingB.AddTorque(torqueB);
            }
        }

        private void DrawCurve(Vector3 posA, Vector3 posB, float slack)
        {
            if (RopeLine == null) return;
            RopeLine.enabled = true;
            if (RopeLine.positionCount != LineSegments)
            {
                RopeLine.positionCount = LineSegments;
            }

            float dist = Vector3.Distance(posA, posB);
            CurrentDistance = dist;

            // Maximum sag depth proportional to slack
            float sagDepth = Mathf.Clamp(slack * 0.70f, 0f, 3.0f);

            for (int i = 0; i < LineSegments; i++)
            {
                float t = (float)i / (LineSegments - 1);
                Vector3 pt = Vector3.Lerp(posA, posB, t);

                // Parabolic droop: 4 * t * (1 - t) peaks at t = 0.5
                float droop = 4f * t * (1f - t);
                pt += Vector3.down * (sagDepth * droop);

                _linePoints[i] = pt;
            }

            RopeLine.SetPositions(_linePoints);
        }

        private void LateUpdate()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;

            if (!IsConfigured)
            {
                if (ActivePendingRope == this)
                {
                    var player = Player.m_localPlayer;
                    if (player == null || !player.InPlaceMode() || Input.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))
                    {
                        CancelPendingRope();
                        return;
                    }

                    var selected = player.GetSelectedPiece();
                    if (selected == null || selected.GetComponent<VehicleRope>() == null)
                    {
                        CancelPendingRope();
                        return;
                    }

                    var ghost = BuildingPatches.CurrentPlacementGhost ?? Traverse.Create(player).Field<GameObject>("m_placementGhost")?.Value;
                    if (ghost != null)
                    {
                        EnsureVisuals();
                        Vector3 posA = GetAnchorAPosition();
                        Vector3 posB = ghost.transform.position;
                        DrawCurve(posA, posB, 0.05f);
                    }
                }
                return;
            }

            EnsureVisuals();

            Vector3 aPos = GetAnchorAPosition();
            Vector3 bPos = GetAnchorBPosition();
            float distance = Vector3.Distance(aPos, bPos);
            CurrentDistance = distance;

            // Update cleat positions and orientations smoothly from their tracked attachments
            transform.position = aPos;
            if (ParentA != null)
            {
                transform.rotation = ParentA.rotation * LocalRotA;
            }

            if (AnchorBTransform != null)
            {
                AnchorBTransform.position = bPos;
                if (ParentB != null)
                {
                    AnchorBTransform.rotation = ParentB.rotation * LocalRotB;
                }
                else if (distance > 0.01f)
                {
                    AnchorBTransform.rotation = Quaternion.LookRotation(aPos - bPos, Vector3.up);
                }
            }

            float targetLen = EffectiveRestLength;
            float slackAmt = Mathf.Max(0f, targetLen - distance);
            DrawCurve(aPos, bPos, slackAmt);

            HandleInput();
        }

        private void HandleInput()
        {
            var player = Player.m_localPlayer;
            if (player == null || player.InPlaceMode()) return;
            var hover = player.GetHoverObject();
            if (hover == null) return;

            bool isMe = hover == gameObject || hover.transform.IsChildOf(transform)
                     || (AnchorBTransform != null && (hover == AnchorBTransform.gameObject || hover.transform.IsChildOf(AnchorBTransform)));
            if (!isMe)
            {
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
                return;
            }
            if (VehicleUtil.UiBlocksInput()) return;

            // [L] opens text dialog to set exact rest length in meters
            if (Input.GetKeyDown(KeyCode.L))
            {
                if (TextInput.instance != null)
                {
                    TextInput.instance.RequestText(this, "Set Rope Length (meters)", 12);
                    return;
                }
            }

            float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 0.50f : 0.05f;
            float delta = 0f;
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                delta -= step;
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                delta += step;
            }
            else if (Input.GetKey(KeyCode.LeftArrow))
            {
                _arrowHoldTimer += Time.deltaTime;
                if (_arrowHoldTimer > 0.4f)
                {
                    _arrowRepeatTimer += Time.deltaTime;
                    if (_arrowRepeatTimer >= 0.08f)
                    {
                        _arrowRepeatTimer = 0f;
                        delta -= step;
                    }
                }
            }
            else if (Input.GetKey(KeyCode.RightArrow))
            {
                _arrowHoldTimer += Time.deltaTime;
                if (_arrowHoldTimer > 0.4f)
                {
                    _arrowRepeatTimer += Time.deltaTime;
                    if (_arrowRepeatTimer >= 0.08f)
                    {
                        _arrowRepeatTimer = 0f;
                        delta += step;
                    }
                }
            }
            else
            {
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
            }

            if (Mathf.Abs(delta) > 0.001f)
            {
                RestLength = Mathf.Clamp((float)Math.Round(RestLength + delta, 2), 0.20f, 250f);
                SaveZdo();
                player.Message(MessageHud.MessageType.Center, $"Rope Length: {RestLength:0.00}m (Span: {CurrentDistance:0.00}m)");
            }
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            var player = user as Player;
            if (player == null) return false;

            if (hold)
            {
                return false;
            }

            // [E] toggles between Stiff (Rigid Cable) and Elastic (Spring Suspension)
            Mode = (Mode == RopeMode.Stiff) ? RopeMode.Elastic : RopeMode.Stiff;
            SaveZdo();
            player.Message(MessageHud.MessageType.Center, $"Rope Mode: {GetModeName()}");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
        {
            string tensionStr = CurrentTension > 5f ? $"<color=cyan>{CurrentTension:0} N</color>" : "<color=grey>Slack</color>";

            return Localization.instance.Localize(
                $"<b>Rope Tie-Down</b>\n" +
                $"Span: <color=yellow>{CurrentDistance:0.00}m / {EffectiveRestLength:0.00}m</color>   Mode: <color=yellow>{GetModeName()}</color>\n" +
                $"Tension: {tensionStr}\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] Toggle Stiff / Elastic   " +
                "[<color=yellow><b>L</b></color>] Set Exact Length\n" +
                "[<color=yellow><b>Left / Right arrows</b></color>] Nudge (±0.05m, Shift ±0.50m)"
            );
        }

        public string GetHoverName() => "Rope Tie-Down";
        public float GetHoverOffset() => 0f;

        public string GetText() => RestLength.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        public void SetText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string clean = System.Text.RegularExpressions.Regex.Replace(text.Trim().Replace(',', '.'), @"[^0-9.]", "");
            if (float.TryParse(clean, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                RestLength = Mathf.Clamp((float)Math.Round(val, 2), 0.20f, 250f);
                SaveZdo();
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Rope Rest Length: {RestLength:0.00}m");
            }
        }

        public string GetModeName()
        {
            switch (Mode)
            {
                case RopeMode.Elastic: return "Elastic (Spring Suspension)";
                case RopeMode.Stiff:
                default: return "Stiff (Rigid Cable)";
            }
        }

        private void SaveZdo()
        {
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            zdo.Set(GetZdoKey("Configured"), IsConfigured);
            zdo.Set(GetZdoKey("Mode"), (int)Mode);
            zdo.Set(GetZdoKey("RestLength"), RestLength);

            zdo.Set(GetZdoKey("TargetA"), _targetZdoA);
            zdo.Set(GetZdoKey("IsBearingA"), _isBearingA);
            zdo.Set(GetZdoKey("LocalPosA"), LocalPosA);
            zdo.Set(GetZdoKey("LocalRotA"), LocalRotA);

            zdo.Set(GetZdoKey("TargetB"), _targetZdoB);
            zdo.Set(GetZdoKey("IsBearingB"), _isBearingB);
            zdo.Set(GetZdoKey("LocalPosB"), LocalPosB);
            zdo.Set(GetZdoKey("LocalRotB"), LocalRotB);
            zdo.Set(GetZdoKey("WorldPosB"), _worldPosB);
        }

        private void TryRestore()
        {
            _restoreAttempts++;
            if (_restoreDone || _nview == null || !_nview.IsValid())
            {
                CancelInvoke(nameof(TryRestore));
                return;
            }

            var zdo = _nview.GetZDO();
            _targetZdoA = zdo.GetZDOID(GetZdoKey("TargetA"));
            _isBearingA = zdo.GetBool(GetZdoKey("IsBearingA"), false);
            LocalPosA = zdo.GetVec3(GetZdoKey("LocalPosA"), LocalPosA);
            LocalRotA = zdo.GetQuaternion(GetZdoKey("LocalRotA"), LocalRotA);

            _targetZdoB = zdo.GetZDOID(GetZdoKey("TargetB"));
            _isBearingB = zdo.GetBool(GetZdoKey("IsBearingB"), false);
            LocalPosB = zdo.GetVec3(GetZdoKey("LocalPosB"), LocalPosB);
            LocalRotB = zdo.GetQuaternion(GetZdoKey("LocalRotB"), LocalRotB);
            _worldPosB = zdo.GetVec3(GetZdoKey("WorldPosB"), _worldPosB);

            if (_targetZdoA != ZDOID.None && ParentA == null)
            {
                var goA = VehicleUtil.FindByUid(_targetZdoA);
                if (goA != null)
                {
                    if (_isBearingA)
                    {
                        BearingA = goA.GetComponent<VehicleBearing>() ?? goA.GetComponentInParent<VehicleBearing>();
                        if (BearingA != null)
                        {
                            ParentA = BearingA.RotatingHead;
                            CoreA = BearingA.GetComponentInParent<VehicleCore>() ?? goA.GetComponent<VehiclePiece>()?.ParentVehicle;
                            RbA = CoreA != null ? CoreA.Rb : BearingA.GetComponentInParent<Rigidbody>();
                        }
                    }
                    else
                    {
                        CoreA = goA.GetComponent<VehicleCore>() ?? goA.GetComponentInParent<VehicleCore>() ?? goA.GetComponent<VehiclePiece>()?.ParentVehicle;
                        ParentA = CoreA != null ? CoreA.transform : goA.transform;
                        RbA = CoreA != null ? CoreA.Rb : goA.GetComponentInParent<Rigidbody>();
                    }
                }
            }

            if (_targetZdoB != ZDOID.None && ParentB == null)
            {
                var goB = VehicleUtil.FindByUid(_targetZdoB);
                if (goB != null)
                {
                    if (_isBearingB)
                    {
                        BearingB = goB.GetComponent<VehicleBearing>() ?? goB.GetComponentInParent<VehicleBearing>();
                        if (BearingB != null)
                        {
                            ParentB = BearingB.RotatingHead;
                            CoreB = BearingB.GetComponentInParent<VehicleCore>() ?? goB.GetComponent<VehiclePiece>()?.ParentVehicle;
                            RbB = CoreB != null ? CoreB.Rb : BearingB.GetComponentInParent<Rigidbody>();
                        }
                    }
                    else
                    {
                        CoreB = goB.GetComponent<VehicleCore>() ?? goB.GetComponentInParent<VehicleCore>() ?? goB.GetComponent<VehiclePiece>()?.ParentVehicle;
                        ParentB = CoreB != null ? CoreB.transform : goB.transform;
                        RbB = CoreB != null ? CoreB.Rb : goB.GetComponentInParent<Rigidbody>();
                    }
                }
            }

            IgnorePieceCollisions(ParentA, CoreA);
            IgnorePieceCollisions(ParentB, CoreB);
            EnsureVisuals();

            // If we've found targets or exhausted 30 attempts (~7 seconds)
            if ((_targetZdoA == ZDOID.None || ParentA != null) && (_targetZdoB == ZDOID.None || ParentB != null))
            {
                _restoreDone = true;
                CancelInvoke(nameof(TryRestore));
            }
            else if (_restoreAttempts > 30)
            {
                _restoreDone = true;
                CancelInvoke(nameof(TryRestore));
            }
        }

        private string GetZdoKey(string key) => $"Valhicle_Rope_{key}";
    }

    public class VehicleRopeAnchorProxy : MonoBehaviour, Interactable, Hoverable
    {
        public VehicleRope Rope;

        public bool Interact(Humanoid user, bool hold, bool alt) => Rope != null && Rope.Interact(user, hold, alt);
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public string GetHoverText() => Rope != null ? Rope.GetHoverText() : "";
        public string GetHoverName() => Rope != null ? Rope.GetHoverName() : "Rope Tie-Down";
        public float GetHoverOffset() => 0f;
    }
}
