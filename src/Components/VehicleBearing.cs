using System.Collections.Generic;
using UnityEngine;
using Valhicle.Core;

namespace Valhicle.Components
{
    public enum BearingMode
    {
        Steering = 0,
        FreeSpinning = 1,
        Motorized = 2
    }

    /// <summary>
    /// Pieces on the hub are parented to RotatingHead and kept there with saved local pose.
    /// The chassis the bearing sits on is never attached.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    public class VehicleBearing : MonoBehaviour, Interactable, Hoverable
    {
        public BearingMode Mode = BearingMode.FreeSpinning;
        public float MaxSteerAngle = 35f;
        public float SteerSpeed = 160f;
        public float MotorSpeed = 180f;
        public bool Reverse;
        public Transform RotatingHead;
        public float CurrentAngle { get; private set; }

        private ZNetView _nview;
        private float _omega;
        private float _reclaimTimer;
        private float _arrowHoldTimer;
        private float _arrowRepeatTimer;
        private Vector3 _lastVehicleVel;
        private readonly List<Load> _loads = new List<Load>();

        private struct Load
        {
            public Transform T;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>() ?? GetComponentInParent<ZNetView>();
            EnsureHead();
        }

        private void Start()
        {
            if (_nview != null && _nview.IsValid())
            {
                var zdo = _nview.GetZDO();
                Mode = (BearingMode)zdo.GetInt(GetZdoKey("Mode"), (int)Mode);
                Reverse = zdo.GetBool(GetZdoKey("Reverse"), false);
                MaxSteerAngle = zdo.GetFloat(GetZdoKey("Angle"), MaxSteerAngle);
                CurrentAngle = zdo.GetFloat(GetZdoKey("CurAngle"), CurrentAngle);
            }
            EnsureHead();
            if (RotatingHead != null)
            {
                RotatingHead.localRotation = Quaternion.Euler(0f, CurrentAngle, 0f);
            }
        }

        private void EnsureHead()
        {
            if (RotatingHead != null) return;
            RotatingHead = transform.Find("RotatingHead");
            if (RotatingHead != null) return;
            var go = new GameObject("RotatingHead");
            RotatingHead = go.transform;
            RotatingHead.SetParent(transform, false);
            RotatingHead.localPosition = Vector3.zero;
            RotatingHead.localRotation = Quaternion.identity;
            RotatingHead.localScale = Vector3.one;
        }

        public void StripHeadBodies()
        {
            if (RotatingHead == null) return;
            foreach (var j in RotatingHead.GetComponentsInChildren<Joint>(true))
            {
                if (j != null) Destroy(j);
            }
            foreach (var rb in RotatingHead.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb != null) Destroy(rb);
            }
        }

        public bool IsMountSupport(Transform t)
        {
            if (t == null) return true;
            if (t == transform || t == RotatingHead) return true;
            if (transform.IsChildOf(t) || t == transform.parent) return true;
            if (t.GetComponent<VehicleCore>() != null || t.GetComponent<VehicleLift>() != null) return true;
            return false;
        }

        public bool Owns(Transform t)
        {
            for (int i = 0; i < _loads.Count; i++)
            {
                if (_loads[i].T == t) return true;
            }
            return t != null && RotatingHead != null && (t.parent == RotatingHead || t.IsChildOf(RotatingHead));
        }

        public void RegisterAttachment(Transform t)
        {
            if (t == null) return;
            EnsureHead();
            if (IsMountSupport(t)) return;
            if (t.GetComponent<VehicleSeat>() != null || t.GetComponent<VehicleEngine>() != null) return;
            if (t.GetComponent<VehicleBearing>() != null) return;

            for (int i = 0; i < _loads.Count; i++)
            {
                if (_loads[i].T == t)
                {
                    return;
                }
            }

            foreach (var j in t.GetComponentsInChildren<Joint>(true))
            {
                if (j != null) Destroy(j);
            }
            foreach (var extraRb in t.GetComponentsInChildren<Rigidbody>(true))
            {
                if (extraRb != null) Destroy(extraRb);
            }

            t.SetParent(RotatingHead, true);
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
                nv.GetZDO().Set(VehicleUtil.BearingZdoKey, VehicleUtil.GetUid(this));
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

        private void LateUpdate()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;
            EnsureHead();
            TickRotation(Time.deltaTime);

            for (int i = _loads.Count - 1; i >= 0; i--)
            {
                var load = _loads[i];
                if (load.T == null)
                {
                    _loads.RemoveAt(i);
                    continue;
                }
                if (load.T.parent != RotatingHead)
                {
                    load.T.SetParent(RotatingHead, false);
                }
                load.T.localPosition = load.LocalPos;
                load.T.localRotation = load.LocalRot;
                load.T.localScale = Vector3.one;
            }

            _reclaimTimer += Time.deltaTime;
            if (_reclaimTimer > 1f)
            {
                _reclaimTimer = 0f;
                ReclaimTagged();
            }
        }

        private void TickRotation(float dt)
        {
            var core = GetComponentInParent<VehicleCore>();
            bool occupied = core != null && core.GetSteerSeat() != null;
            bool freeze = core != null && core.IsOnLift && !occupied;

            switch (Mode)
            {
                case BearingMode.Steering:
                    if (freeze) break;
                    float steer = core != null ? core.GetSteerInput() : 0f;
                    if (Reverse) steer = -steer;
                    CurrentAngle = Mathf.MoveTowards(CurrentAngle, steer * MaxSteerAngle, SteerSpeed * dt);
                    break;
                case BearingMode.Motorized:
                    if (!freeze) CurrentAngle += MotorSpeed * (Reverse ? -1f : 1f) * dt;
                    break;
                case BearingMode.FreeSpinning:
                    if (!freeze)
                    {
                        UpdateFreeSpinPhysics(core, dt);
                    }
                    break;
            }

            if (CurrentAngle > 360f || CurrentAngle < -360f) CurrentAngle %= 360f;
            RotatingHead.localRotation = Quaternion.Euler(0f, CurrentAngle, 0f);
        }

        private void UpdateFreeSpinPhysics(VehicleCore core, float dt)
        {
            if (dt <= 0.0001f) return;
            EnsureHead();

            Vector3 axis = transform.up;
            Vector3 center = RotatingHead.position;

            Vector3 gEff = Physics.gravity;
            if (core != null && core.Rb != null)
            {
                Vector3 v = core.Rb.linearVelocity;
                if (_lastVehicleVel != Vector3.zero)
                {
                    Vector3 accel = (v - _lastVehicleVel) / dt;
                    accel = Vector3.ClampMagnitude(accel, 35f);
                    gEff -= accel;
                }
                _lastVehicleVel = v;
            }

            float totalTorque = 0f;
            float totalInertia = 0.8f;

            for (int i = 0; i < _loads.Count; i++)
            {
                var t = _loads[i].T;
                if (t == null) continue;

                var col = t.GetComponentInChildren<Collider>();
                Vector3 pieceCenter = col != null ? col.bounds.center : t.position;
                Vector3 r = pieceCenter - center;

                Vector3 rPerp = r - Vector3.Project(r, axis);
                float distSq = rPerp.sqrMagnitude;

                float mass = 15f;
                if (t.GetComponent<VehicleWheel>() != null) mass = 25f;
                else if (t.GetComponent<VehicleEngine>() != null) mass = 110f;
                else
                {
                    var wnt = t.GetComponent<WearNTear>();
                    if (wnt != null && wnt.m_health > 0f) mass = Mathf.Clamp(wnt.m_health * 0.1f, 8f, 60f);
                }

                totalInertia += mass * distSq;

                Vector3 axisCrossR = Vector3.Cross(axis, r);
                totalTorque += mass * Vector3.Dot(axisCrossR, gEff);

                var wheel = t.GetComponent<VehicleWheel>();
                if (wheel != null && wheel.IsGrounded && core != null && core.Rb != null)
                {
                    Vector3 groundPoint = wheel.GroundHitPoint;
                    Vector3 rGround = groundPoint - center;
                    Vector3 driveDir = wheel.GetDriveDirection(core);
                    Vector3 wheelRight = Vector3.Cross(Vector3.up, driveDir).normalized;
                    Vector3 ptVel = core.Rb.GetPointVelocity(groundPoint);
                    float lateralVel = Vector3.Dot(ptVel, wheelRight);
                    Vector3 lateralForce = wheelRight * Mathf.Clamp(-lateralVel * (wheel.GripFactor * 2000f), -5000f, 5000f);
                    totalTorque += Vector3.Dot(Vector3.Cross(rGround, lateralForce), axis);
                }
            }

            float alphaRad = totalTorque / Mathf.Max(0.1f, totalInertia);
            float alphaDeg = alphaRad * Mathf.Rad2Deg;

            _omega += alphaDeg * dt;
            _omega *= Mathf.Exp(-dt * 1.8f);

            if (Mathf.Abs(_omega) < 0.2f && Mathf.Abs(totalTorque) < 0.5f)
            {
                _omega = 0f;
            }

            _omega = Mathf.Clamp(_omega, -1500f, 1500f);
            CurrentAngle += _omega * dt;
        }

        private void ReclaimTagged()
        {
            ZDOID myId = VehicleUtil.GetUid(this);
            if (myId == ZDOID.None) return;
            var cols = Physics.OverlapSphere(transform.position, 6f);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                var p = cols[i].GetComponentInParent<VehiclePiece>();
                if (p == null || Owns(p.transform)) continue;
                var nv = p.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid()) continue;
                if (nv.GetZDO().GetZDOID(VehicleUtil.BearingZdoKey) != myId) continue;
                RegisterAttachment(p.transform);
            }
        }

        public void SetupPhysics() { }

        public bool IsHubTarget(Vector3 worldPoint, Collider hitCol)
        {
            if (hitCol == null) return Vector3.Distance(worldPoint, transform.position) < 0.5f;
            var b = hitCol.GetComponentInParent<VehicleBearing>();
            if (b != this) return false;
            return Vector3.Distance(worldPoint, transform.position) < 0.5f;
        }

        private void Update()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;
            var player = Player.m_localPlayer;
            if (player == null || player.InPlaceMode()) return;
            var hover = player.GetHoverObject();
            if (hover == null || hover.GetComponentInParent<VehicleBearing>() != this)
            {
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
                return;
            }
            if (VehicleUtil.UiBlocksInput()) return;

            float delta = 0f;
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                delta -= 5f;
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                delta += 5f;
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
                        delta -= 5f;
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
                        delta += 5f;
                    }
                }
            }
            else
            {
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
            }

            if (Mathf.Abs(delta) < 0.01f) return;

            MaxSteerAngle = Mathf.Clamp(MaxSteerAngle + delta, 15f, 75f);
            SaveZdo();
            player.Message(MessageHud.MessageType.Center, $"Steer limit: {MaxSteerAngle:0}°");
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold)
            {
                if (Mode == BearingMode.FreeSpinning)
                {
                    _omega += alt ? -200f : 200f;
                    return true;
                }
                return false;
            }
            var player = user as Player;
            if (player == null) return false;
            if (alt)
            {
                Reverse = !Reverse;
                SaveZdo();
                player.Message(MessageHud.MessageType.Center, Reverse ? "Bearing: Reversed" : "Bearing: Normal");
                return true;
            }
            Mode = (BearingMode)(((int)Mode + 1) % 3);
            _omega = 0f;
            SaveZdo();
            player.Message(MessageHud.MessageType.Center, $"Bearing: {Mode}");
            return true;
        }

        public void SyncZdo()
        {
            SaveZdo();
        }

        private void SaveZdo()
        {
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            zdo.Set(GetZdoKey("Mode"), (int)Mode);
            zdo.Set(GetZdoKey("Reverse"), Reverse);
            zdo.Set(GetZdoKey("Angle"), MaxSteerAngle);
            zdo.Set(GetZdoKey("CurAngle"), CurrentAngle);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
        {
            string spinHint = Mode == BearingMode.FreeSpinning ? "[<color=yellow><b>Hold $KEY_Use</b></color>] Spin push   " : "";
            return Localization.instance.Localize(
                "<b>Mechanical Bearing</b>\n" +
                $"Mode: <color=yellow>{Mode}</color>  {(Reverse ? "REV" : "")}  {MaxSteerAngle:0}°\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] Cycle   " +
                "[<color=yellow><b>Shift + $KEY_Use</b></color>] Reverse   " +
                "[<color=yellow><b>Left / Right arrows</b></color>] Angle   " +
                spinHint
            );
        }

        public string GetHoverName() => "Mechanical Bearing";

        public float GetHoverOffset() => 0f;

        private string GetZdoKey(string subKey) => $"Valhicle_Bearing_{subKey}";
    }
}
