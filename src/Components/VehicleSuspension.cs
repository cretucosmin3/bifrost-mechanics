using System.Collections.Generic;
using UnityEngine;
using Valhicle.Core;

namespace Valhicle.Components
{
    public enum SuspensionType
    {
        Standard = 0,  // Flexible, good for bumpy offroad
        HeavyDuty = 1  // Stiff, high load capacity for large cargo/engines
    }

    public enum SuspensionMode
    {
        Both = 0,      // Resists compression (pushes) and extension (pulls)
        PushOnly = 1,  // Resists compression only (bumper / pusher)
        PullOnly = 2   // Resists extension only (puller / tension strap)
    }

    /// <summary>
    /// Dynamic suspension strut that connects two structures or supports mounted assemblies (such as wheels or subframes).
    /// Generates physical spring-damper push and pull forces and drives 3D helical spring compression/extension visuals.
    /// Supports in-game force configuration via [E] (text menu) or arrow keys, and mode toggling via [Shift+E].
    /// </summary>
    [DefaultExecutionOrder(20001)]
    public class VehicleSuspension : MonoBehaviour, Interactable, Hoverable, TextReceiver
    {
        public SuspensionType Type = SuspensionType.Standard;
        public SuspensionMode Mode = SuspensionMode.Both;

        [Header("Spring Physical Properties")]
        public float SpringForce = 2500f; // Spring stiffness (N/m)
        public float RestLength = 0.65f;
        public float MaxCompression = 0.35f;
        public float MaxExtension = 0.25f;
        public float DampingRatio = 0.65f;

        [Header("Transforms & Visuals")]
        public Transform MovingHead;
        public Transform SpringContainer;
        public Transform SpringMeshTransform;
        public Transform PistonRod;
        public Transform BaseDamperTube;
        public Transform TopBracket;
        public Transform BottomBracket;

        public Vector3 OriginalSpringScale = Vector3.one;
        public float BaseHeadLocalY { get; private set; }
        public float CurrentDisplacement { get; private set; } // negative = compressed, positive = extended
        public float CurrentLength => Mathf.Clamp(RestLength + CurrentDisplacement, RestLength - MaxCompression, RestLength + MaxExtension);

        private ZNetView _nview;
        private float _reclaimTimer;
        private float _arrowHoldTimer;
        private float _arrowRepeatTimer;

        // Dynamics state for moving head
        private float _headVelocity;
        private readonly List<Load> _loads = new List<Load>();

        // Optional connection to an external structure/Rigidbody or Bearing
        private VehicleBearing _bearingTarget;
        private Rigidbody _externalTargetRb;
        private Transform _externalTargetTransform;
        private Vector3 _externalTargetLocalAnchor;
        private float _targetSearchTimer;

        private struct Load
        {
            public Transform T;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>() ?? GetComponentInParent<ZNetView>();
            ApplySuspensionSettings();
            EnsureMovingHead();
        }

        private void Start()
        {
            if (_nview != null && _nview.IsValid())
            {
                var zdo = _nview.GetZDO();
                SpringForce = zdo.GetFloat(GetZdoKey("Force"), SpringForce);
                Mode = (SuspensionMode)zdo.GetInt(GetZdoKey("Mode"), (int)Mode);
            }
            EnsureMovingHead();
            BaseHeadLocalY = (TopBracket != null) ? TopBracket.localPosition.y : (RestLength * 0.5f);
            FindExternalTarget();
        }

        public void ApplySuspensionSettings()
        {
            if (Type == SuspensionType.Standard)
            {
                RestLength = 0.65f;
                MaxCompression = 0.30f;
                MaxExtension = 0.20f;
                if (SpringForce < 100f) SpringForce = 2500f;
            }
            else // HeavyDuty
            {
                RestLength = 0.85f;
                MaxCompression = 0.40f;
                MaxExtension = 0.25f;
                if (SpringForce < 100f) SpringForce = 5000f;
            }
        }

        public void EnsureMovingHead()
        {
            if (MovingHead != null) return;
            var t = transform.Find("MovingHead");
            if (t != null)
            {
                MovingHead = t;
                return;
            }
            var go = new GameObject("MovingHead");
            MovingHead = go.transform;
            MovingHead.SetParent(transform, false);
            MovingHead.localPosition = Vector3.zero;
            MovingHead.localRotation = Quaternion.identity;
            MovingHead.localScale = Vector3.one;
        }

        public bool IsMountSupport(Transform t)
        {
            if (t == null) return true;
            if (t == transform || t == MovingHead) return true;
            if (transform.IsChildOf(t) || t == transform.parent) return true;
            if (t.GetComponent<VehicleCore>() != null || t.GetComponent<VehicleLift>() != null) return true;
            if (Owns(t)) return false;

            Vector3 localPos = transform.InverseTransformPoint(t.position);
            if (localPos.y < -RestLength * 0.35f) return true;

            return false;
        }

        public bool IsHeadTarget(Vector3 worldPoint, Collider hitCol)
        {
            if (hitCol == null) return false;
            var s = hitCol.GetComponentInParent<VehicleSuspension>();
            if (s != this) return false;

            var piece = hitCol.GetComponentInParent<Piece>();
            if (piece != null && piece.gameObject != gameObject) return false;

            Vector3 toPoint = worldPoint - transform.position;
            float projUp = Vector3.Dot(toPoint, transform.up);
            if (projUp < RestLength * 0.15f) return false; // Lower half or base bracket

            Vector3 radial = toPoint - transform.up * projUp;
            return radial.magnitude <= 0.22f;
        }

        public bool Owns(Transform t)
        {
            for (int i = 0; i < _loads.Count; i++)
            {
                if (_loads[i].T == t) return true;
            }
            return t != null && MovingHead != null && (t.parent == MovingHead || t.IsChildOf(MovingHead));
        }

        public void RegisterAttachment(Transform t)
        {
            if (t == null) return;
            EnsureMovingHead();
            if (IsMountSupport(t)) return;
            if (t.GetComponent<VehicleSuspension>() != null) return;

            for (int i = 0; i < _loads.Count; i++)
            {
                if (_loads[i].T == t) return;
            }

            foreach (var j in t.GetComponentsInChildren<Joint>(true))
            {
                if (j != null) Destroy(j);
            }
            foreach (var extraRb in t.GetComponentsInChildren<Rigidbody>(true))
            {
                if (extraRb != null) Destroy(extraRb);
            }

            t.SetParent(MovingHead, true);
            t.localScale = Vector3.one;

            _loads.Add(new Load
            {
                T = t,
                LocalPos = t.localPosition,
                LocalRot = t.localRotation
            });

            var nv = t.GetComponent<ZNetView>();
            if (nv != null && nv.IsValid() && nv.IsOwner())
            {
                nv.GetZDO().Set(VehicleUtil.SuspensionZdoKey, VehicleUtil.GetUid(this));
                nv.GetZDO().Set(VehicleUtil.LocalPosZdoKey, t.localPosition);
                nv.GetZDO().Set(VehicleUtil.LocalRotZdoKey, t.localRotation);
            }
        }

        public void UpdateLoadPose(Transform t)
        {
            if (t == null) return;
            for (int i = 0; i < _loads.Count; i++)
            {
                if (_loads[i].T == t)
                {
                    _loads[i] = new Load
                    {
                        T = t,
                        LocalPos = t.localPosition,
                        LocalRot = t.localRotation
                    };
                    var nv = t.GetComponent<ZNetView>();
                    if (nv != null && nv.IsValid() && nv.IsOwner())
                    {
                        nv.GetZDO().Set(VehicleUtil.LocalPosZdoKey, t.localPosition);
                        nv.GetZDO().Set(VehicleUtil.LocalRotZdoKey, t.localRotation);
                    }
                    return;
                }
            }
        }

        public void UnregisterAttachment(Transform t)
        {
            if (t == null) return;
            for (int i = _loads.Count - 1; i >= 0; i--)
            {
                if (_loads[i].T == t)
                {
                    _loads.RemoveAt(i);
                    break;
                }
            }
        }

        private void FixedUpdate()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;

            float dt = Time.fixedDeltaTime;
            if (dt <= 0.0001f) return;

            // Check or refresh external target connection periodically
            _targetSearchTimer += dt;
            if (_targetSearchTimer >= 1.0f)
            {
                _targetSearchTimer = 0f;
                FindExternalTarget();
            }

            var core = GetComponentInParent<VehicleCore>();
            Rigidbody baseRb = core != null ? core.Rb : GetComponentInParent<Rigidbody>();

            if (_bearingTarget != null && _externalTargetTransform != null)
            {
                SimulateBearingPhysics(baseRb, dt);
            }
            else if (_externalTargetTransform != null && _externalTargetRb != null && _externalTargetRb != baseRb)
            {
                SimulateExternalTargetPhysics(baseRb, dt);
            }
            else
            {
                SimulateMountedHeadPhysics(baseRb, core, dt);
            }
        }

        private void SimulateBearingPhysics(Rigidbody baseRb, float dt)
        {
            Vector3 basePoint = transform.position - transform.up * (RestLength * 0.42f);
            Vector3 targetPoint = _externalTargetTransform.TransformPoint(_externalTargetLocalAnchor);

            Vector3 delta = targetPoint - basePoint;
            float currentDist = delta.magnitude;
            if (currentDist < 0.05f) return;

            Vector3 dir = delta / currentDist;
            float disp = currentDist - RestLength;

            float nominalDisp = 0f;
            if (Mode == SuspensionMode.PushOnly) nominalDisp = MaxExtension;
            else if (Mode == SuspensionMode.PullOnly) nominalDisp = -MaxCompression;

            float dispError = disp - nominalDisp;

            Vector3 vA = baseRb != null ? baseRb.GetPointVelocity(basePoint) : Vector3.zero;
            Vector3 vB = Vector3.zero;
            var core = _bearingTarget.GetComponentInParent<VehicleCore>();
            if (core != null && core.Rb != null) vB = core.Rb.GetPointVelocity(targetPoint);
            float relVel = Vector3.Dot(vB - vA, dir);

            float critDamp = 2f * Mathf.Sqrt(SpringForce * 150f) * DampingRatio;
            float forceMagnitude = -SpringForce * dispError - critDamp * relVel;

            if (Mode == SuspensionMode.PushOnly && forceMagnitude < 0f) forceMagnitude = 0f;
            if (Mode == SuspensionMode.PullOnly && forceMagnitude > 0f) forceMagnitude = 0f;

            forceMagnitude = Mathf.Clamp(forceMagnitude, -35000f, 35000f);
            Vector3 forceOnTarget = dir * forceMagnitude;

            Transform head = _bearingTarget.RotatingHead != null ? _bearingTarget.RotatingHead : _bearingTarget.transform;
            Vector3 r = targetPoint - head.position;
            Vector3 axis = _bearingTarget.transform.up;
            float torque = Vector3.Dot(Vector3.Cross(r, forceOnTarget), axis);
            _bearingTarget.AddTorque(torque);

            if (baseRb != null && !baseRb.isKinematic)
            {
                baseRb.AddForceAtPosition(-forceOnTarget, basePoint, ForceMode.Force);
            }

            CurrentDisplacement = Mathf.Clamp(disp, -MaxCompression, MaxExtension);
        }

        private void SimulateExternalTargetPhysics(Rigidbody baseRb, float dt)
        {
            Vector3 basePoint = transform.position - transform.up * (RestLength * 0.42f);
            Vector3 targetPoint = _externalTargetTransform.TransformPoint(_externalTargetLocalAnchor);

            Vector3 delta = targetPoint - basePoint;
            float currentDist = delta.magnitude;
            if (currentDist < 0.05f) return;

            Vector3 dir = delta / currentDist;
            float disp = currentDist - RestLength;

            float nominalDisp = 0f;
            if (Mode == SuspensionMode.PushOnly) nominalDisp = MaxExtension;
            else if (Mode == SuspensionMode.PullOnly) nominalDisp = -MaxCompression;

            float dispError = disp - nominalDisp;

            Vector3 vA = baseRb != null ? baseRb.GetPointVelocity(basePoint) : Vector3.zero;
            Vector3 vB = _externalTargetRb.GetPointVelocity(targetPoint);
            float relVel = Vector3.Dot(vB - vA, dir);

            float critDamp = 2f * Mathf.Sqrt(SpringForce * 200f) * DampingRatio;
            float forceMagnitude = -SpringForce * dispError - critDamp * relVel;

            if (Mode == SuspensionMode.PushOnly && forceMagnitude < 0f) forceMagnitude = 0f;
            if (Mode == SuspensionMode.PullOnly && forceMagnitude > 0f) forceMagnitude = 0f;

            forceMagnitude = Mathf.Clamp(forceMagnitude, -35000f, 35000f);
            Vector3 forceOnTarget = dir * forceMagnitude;

            _externalTargetRb.AddForceAtPosition(forceOnTarget, targetPoint, ForceMode.Force);
            if (baseRb != null && !baseRb.isKinematic)
            {
                baseRb.AddForceAtPosition(-forceOnTarget, basePoint, ForceMode.Force);
            }

            CurrentDisplacement = Mathf.Clamp(disp, -MaxCompression, MaxExtension);
        }

        private void SimulateMountedHeadPhysics(Rigidbody baseRb, VehicleCore core, float dt)
        {
            float totalMass = 25f;
            float wheelCompression = 0f;
            int groundedWheelCount = 0;

            for (int i = 0; i < _loads.Count; i++)
            {
                var t = _loads[i].T;
                if (t == null) continue;

                var wheel = t.GetComponent<VehicleWheel>();
                if (wheel != null)
                {
                    totalMass += 25f;
                    if (wheel.IsGrounded)
                    {
                        groundedWheelCount++;
                        wheelCompression = Mathf.Max(wheelCompression, wheel.Compression);
                    }
                }
                else if (t.GetComponent<VehicleEngine>() != null)
                {
                    totalMass += 120f;
                }
                else
                {
                    var wnt = t.GetComponent<WearNTear>();
                    if (wnt != null && wnt.m_health > 0f) totalMass += Mathf.Clamp(wnt.m_health * 0.08f, 5f, 40f);
                }
            }

            float nominalTargetDisp = 0f;
            if (Mode == SuspensionMode.PushOnly) nominalTargetDisp = MaxExtension;
            else if (Mode == SuspensionMode.PullOnly) nominalTargetDisp = -MaxCompression;

            float allowedDisp = nominalTargetDisp;
            Vector3 basePoint = transform.position - transform.up * (RestLength * 0.42f);

            if (groundedWheelCount > 0)
            {
                float wheelContactDisp = -wheelCompression * MaxCompression;
                if (wheelContactDisp < allowedDisp) allowedDisp = wheelContactDisp;
            }

            float castRadius = 0.18f;
            float castDist = RestLength + MaxExtension + 0.15f;
            int layerMask = ~LayerMask.GetMask("Ignore Raycast");
            bool hitObstacle = false;
            RaycastHit obstacleHit = default;

            if (Physics.SphereCast(basePoint, castRadius, transform.up, out RaycastHit hit, castDist, layerMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && hit.transform != transform && !hit.transform.IsChildOf(transform))
                {
                    var hitCore = hit.collider.GetComponentInParent<VehicleCore>();
                    if (hitCore == null || hitCore != core)
                    {
                        hitObstacle = true;
                        obstacleHit = hit;
                        float contactDisp = (hit.distance - RestLength * 0.42f) - RestLength * 0.5f;
                        if (contactDisp < allowedDisp)
                        {
                            allowedDisp = Mathf.Clamp(contactDisp, -MaxCompression, MaxExtension);
                        }
                    }
                }
            }

            float targetDisp = Mathf.Clamp(allowedDisp, -MaxCompression, MaxExtension);

            float dispError = targetDisp - CurrentDisplacement;
            float omega = Mathf.Sqrt(SpringForce / Mathf.Max(1f, totalMass));
            float dampCoeff = 2f * omega * DampingRatio;

            float accel = (SpringForce / totalMass) * dispError - dampCoeff * _headVelocity;
            _headVelocity += accel * dt;
            _headVelocity = Mathf.Clamp(_headVelocity, -15f, 15f);

            CurrentDisplacement += _headVelocity * dt;
            CurrentDisplacement = Mathf.Clamp(CurrentDisplacement, -MaxCompression, MaxExtension);

            float pushingDeflection = nominalTargetDisp - CurrentDisplacement;
            if (Mathf.Abs(pushingDeflection) > 0.005f || groundedWheelCount > 0 || hitObstacle)
            {
                float strutForce = SpringForce * pushingDeflection - (dampCoeff * totalMass) * _headVelocity;
                if (groundedWheelCount > 0)
                {
                    strutForce += wheelCompression * SpringForce * 1.5f;
                }

                if (Mode == SuspensionMode.PushOnly && strutForce < 0f) strutForce = 0f;
                if (Mode == SuspensionMode.PullOnly && strutForce > 0f) strutForce = 0f;

                strutForce = Mathf.Clamp(strutForce, -35000f, 35000f);
                Vector3 pushVec = transform.up * strutForce;

                if (baseRb != null && !baseRb.isKinematic)
                {
                    baseRb.AddForceAtPosition(pushVec, basePoint, ForceMode.Force);
                }

                if (hitObstacle && obstacleHit.collider != null)
                {
                    var otherRb = obstacleHit.collider.GetComponentInParent<Rigidbody>();
                    if (otherRb != null && otherRb != baseRb && !otherRb.isKinematic)
                    {
                        otherRb.AddForceAtPosition(pushVec, obstacleHit.point, ForceMode.Force);
                    }
                }
            }
        }

        private void FindExternalTarget()
        {
            _bearingTarget = null;
            _externalTargetTransform = null;
            _externalTargetRb = null;

            for (int i = 0; i < _loads.Count; i++)
            {
                if (_loads[i].T == null) continue;
                var b = _loads[i].T.GetComponentInParent<VehicleBearing>();
                if (b != null && b.transform != transform)
                {
                    _bearingTarget = b;
                    _externalTargetTransform = _loads[i].T;
                    _externalTargetLocalAnchor = _loads[i].LocalPos;
                    return;
                }
            }

            Vector3 topMountPos = transform.position + transform.up * (RestLength * 0.45f);
            var colliders = Physics.OverlapSphere(topMountPos, 0.75f);

            var myCore = GetComponentInParent<VehicleCore>();
            Transform bestT = null;
            float bestDist = float.MaxValue;
            VehicleBearing bestBearing = null;
            Rigidbody bestRb = null;

            for (int i = 0; i < colliders.Length; i++)
            {
                var c = colliders[i];
                if (c == null || c.isTrigger) continue;
                if (c.transform == transform || c.transform.IsChildOf(transform)) continue;

                var core = c.GetComponentInParent<VehicleCore>();
                var bearing = c.GetComponentInParent<VehicleBearing>();
                var rb = c.GetComponentInParent<Rigidbody>();

                if (core != null && core == myCore && bearing == null)
                {
                    continue;
                }

                float d = Vector3.Distance(topMountPos, c.ClosestPoint(topMountPos));
                if (d < bestDist)
                {
                    bestDist = d;
                    bestBearing = bearing;
                    bestRb = rb;
                    bestT = (bearing != null && bearing.RotatingHead != null) ? bearing.RotatingHead : c.transform;
                }
            }

            if (bestT != null)
            {
                _bearingTarget = bestBearing;
                _externalTargetTransform = bestT;
                _externalTargetRb = bestRb;
                _externalTargetLocalAnchor = bestT.InverseTransformPoint(topMountPos);
            }
        }

        private void LateUpdate()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;
            EnsureMovingHead();

            UpdateVisualLinkage();

            // Maintain attached loads under MovingHead
            for (int i = _loads.Count - 1; i >= 0; i--)
            {
                var load = _loads[i];
                if (load.T == null)
                {
                    _loads.RemoveAt(i);
                    continue;
                }
                if (load.T.parent != MovingHead)
                {
                    load.T.SetParent(MovingHead, false);
                }
                load.T.localPosition = load.LocalPos;
                load.T.localRotation = load.LocalRot;
                load.T.localScale = Vector3.one;
            }

            _reclaimTimer += Time.deltaTime;
            if (_reclaimTimer > 1.2f)
            {
                _reclaimTimer = 0f;
                ReclaimTagged();
            }
        }

        private void UpdateVisualLinkage()
        {
            float halfH = RestLength * 0.42f;
            float yBot = -halfH;
            float yTop = halfH + CurrentDisplacement;

            // Move MovingHead
            if (MovingHead != null)
            {
                MovingHead.localPosition = new Vector3(0f, CurrentDisplacement, 0f);
            }

            // Center and scale the spring container
            if (SpringContainer != null)
            {
                float currentSpan = Mathf.Max(0.08f, yTop - yBot);
                float restSpan = Mathf.Max(0.08f, halfH * 2f);
                float scaleFactor = currentSpan / restSpan;

                SpringContainer.localPosition = new Vector3(0f, (yTop + yBot) * 0.5f, 0f);
                SpringContainer.localScale = new Vector3(1f, scaleFactor, 1f);
            }

            // Piston rod: child of MovingHead, stays at standard local offset in MovingHead
            if (PistonRod != null)
            {
                PistonRod.localPosition = new Vector3(0f, RestLength * 0.15f, 0f);
            }
        }

        /// <summary>
        /// External visual update hook (e.g. called from VehicleCore or wheel compression).
        /// </summary>
        public void UpdateVisuals(float compressionRatio)
        {
        }

        private void ReclaimTagged()
        {
            ZDOID myId = VehicleUtil.GetUid(this);
            if (myId == ZDOID.None) return;
            var cols = Physics.OverlapSphere(transform.position, 4.5f);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                var p = cols[i].GetComponentInParent<VehiclePiece>();
                if (p == null || Owns(p.transform)) continue;
                var nv = p.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid()) continue;
                if (nv.GetZDO().GetZDOID(VehicleUtil.SuspensionZdoKey) != myId) continue;

                if (IsMountSupport(p.transform))
                {
                    if (nv.IsOwner()) nv.GetZDO().Set(VehicleUtil.SuspensionZdoKey, ZDOID.None);
                    continue;
                }

                RegisterAttachment(p.transform);
            }
        }

        private void Update()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;
            var player = Player.m_localPlayer;
            if (player == null || player.InPlaceMode()) return;
            var hover = player.GetHoverObject();
            if (hover == null || hover.GetComponentInParent<VehicleSuspension>() != this)
            {
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
                return;
            }
            if (VehicleUtil.UiBlocksInput()) return;

            float delta = 0f;
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                delta -= 250f;
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                delta += 250f;
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
            }
            else if (Input.GetKey(KeyCode.LeftArrow))
            {
                _arrowHoldTimer += Time.deltaTime;
                if (_arrowHoldTimer > 0.4f)
                {
                    _arrowRepeatTimer += Time.deltaTime;
                    if (_arrowRepeatTimer >= 0.1f)
                    {
                        _arrowRepeatTimer = 0f;
                        delta -= 250f;
                    }
                }
            }
            else if (Input.GetKey(KeyCode.RightArrow))
            {
                _arrowHoldTimer += Time.deltaTime;
                if (_arrowHoldTimer > 0.4f)
                {
                    _arrowRepeatTimer += Time.deltaTime;
                    if (_arrowRepeatTimer >= 0.1f)
                    {
                        _arrowRepeatTimer = 0f;
                        delta += 250f;
                    }
                }
            }
            else
            {
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
            }

            if (Mathf.Abs(delta) > 0.1f)
            {
                SpringForce = Mathf.Clamp(SpringForce + delta, 100f, 25000f);
                SaveZdo();
                player.Message(MessageHud.MessageType.Center, $"Suspension Force: {SpringForce:0} N");
            }
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            var player = user as Player;
            if (player == null) return false;

            if (alt)
            {
                // [Shift + E] cycles mode: Both -> PushOnly -> PullOnly -> Both
                Mode = (SuspensionMode)(((int)Mode + 1) % 3);
                SaveZdo();
                player.Message(MessageHud.MessageType.Center, $"Suspension Mode: {ModeName()}");
                return true;
            }

            // [E] opens the text input menu to enter suspension force
            if (TextInput.instance != null)
            {
                TextInput.instance.RequestText(this, $"Suspension Force (100 - 25000 N)", 8);
                return true;
            }

            // Fallback if TextInput is unavailable: cycle common force presets
            float[] presets = Type == SuspensionType.Standard
                ? new[] { 1500f, 2500f, 4000f, 6000f, 10000f }
                : new[] { 3000f, 5000f, 8000f, 12000f, 18000f };

            int nextIdx = 0;
            for (int i = 0; i < presets.Length; i++)
            {
                if (SpringForce < presets[i] - 10f)
                {
                    nextIdx = i;
                    break;
                }
            }
            SpringForce = presets[nextIdx];
            SaveZdo();
            player.Message(MessageHud.MessageType.Center, $"Suspension Force: {SpringForce:0} N");
            return true;
        }

        public string GetText() => ((int)SpringForce).ToString();

        public void SetText(string text)
        {
            if (float.TryParse(text, out float val))
            {
                SpringForce = Mathf.Clamp(val, 100f, 25000f);
                SaveZdo();
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Suspension Force: {SpringForce:0} N");
            }
        }

        public void SyncZdo() => SaveZdo();

        private void SaveZdo()
        {
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            zdo.Set(GetZdoKey("Force"), SpringForce);
            zdo.Set(GetZdoKey("Mode"), (int)Mode);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
        {
            return Localization.instance.Localize(
                $"<b>Vehicle Suspension ({Type})</b>\n" +
                $"Force: <color=yellow>{SpringForce:0} N</color>  Mode: <color=yellow>{ModeName()}</color>  Len: <color=yellow>{CurrentLength:0.00}m</color>\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] Set Force   " +
                "[<color=yellow><b>Shift + $KEY_Use</b></color>] Mode   " +
                "[<color=yellow><b>Left / Right arrows</b></color>] Adjust Force"
            );
        }

        public string GetHoverName() => $"Vehicle Suspension ({Type})";

        public float GetHoverOffset() => 0f;

        private string ModeName()
        {
            switch (Mode)
            {
                case SuspensionMode.PushOnly: return "Push Only";
                case SuspensionMode.PullOnly: return "Pull Only";
                default: return "Push & Pull (Both)";
            }
        }

        private string GetZdoKey(string subKey) => $"Valhicle_Susp_{subKey}";
    }
}
