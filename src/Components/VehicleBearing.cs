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
    public class VehicleBearing : MonoBehaviour, Interactable, Hoverable, TextReceiver
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
        private float _externalTorque;
        private float _reclaimTimer;
        private float _arrowHoldTimer;
        private float _arrowRepeatTimer;
        private Vector3 _lastVehicleVel;
        private readonly List<Load> _loads = new List<Load>();

        public void AddTorque(float torque)
        {
            _externalTorque += torque;
        }

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
                MotorSpeed = zdo.GetFloat(GetZdoKey("MotorSpeed"), MotorSpeed);
                SteerSpeed = zdo.GetFloat(GetZdoKey("SteerSpeed"), SteerSpeed);
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
            if (Owns(t)) return false;

            // Any piece physically behind the mount plate is on the chassis/support side
            Vector3 localPos = transform.InverseTransformPoint(t.position);
            if (localPos.y < -0.05f) return true;

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

            float totalTorque = _externalTorque;
            _externalTorque = 0f;
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

                if (IsMountSupport(p.transform))
                {
                    if (nv.IsOwner()) nv.GetZDO().Set(VehicleUtil.BearingZdoKey, ZDOID.None);
                    continue;
                }

                RegisterAttachment(p.transform);
            }
        }

        public void SetupPhysics() { }

        public bool IsHubTarget(Vector3 worldPoint, Collider hitCol)
        {
            if (hitCol == null) return false;
            var b = hitCol.GetComponentInParent<VehicleBearing>();
            if (b != this) return false;

            // Must be the bearing piece itself, NOT another piece attached to the bearing!
            var piece = hitCol.GetComponentInParent<Piece>();
            if (piece != null && piece.gameObject != gameObject) return false;

            Vector3 toPoint = worldPoint - transform.position;
            float projUp = Vector3.Dot(toPoint, transform.up);
            if (projUp < -0.02f) return false; // Behind mount plate

            Vector3 radial = toPoint - transform.up * projUp;
            return radial.magnitude <= 0.22f;
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

            // R key toggles reverse while hovering
            if (Input.GetKeyDown(KeyCode.R))
            {
                Reverse = !Reverse;
                SaveZdo();
                player.Message(MessageHud.MessageType.Center, Reverse ? "Bearing: Reversed" : "Bearing: Normal / Forward");
                return;
            }

            // Enter key opens exact numerical input dialog
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OpenConfigDialog();
                return;
            }

            float delta = 0f;
            float coarseDelta = 0f;

            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                delta -= 1f;
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                delta += 1f;
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
            }
            else if (Input.GetKey(KeyCode.LeftArrow))
            {
                _arrowHoldTimer += Time.deltaTime;
                if (_arrowHoldTimer > 0.35f)
                {
                    _arrowRepeatTimer += Time.deltaTime;
                    float repeatRate = _arrowHoldTimer > 1.5f ? 0.04f : 0.08f;
                    if (_arrowRepeatTimer >= repeatRate)
                    {
                        _arrowRepeatTimer = 0f;
                        delta -= _arrowHoldTimer > 2.5f ? 4f : 1f;
                    }
                }
            }
            else if (Input.GetKey(KeyCode.RightArrow))
            {
                _arrowHoldTimer += Time.deltaTime;
                if (_arrowHoldTimer > 0.35f)
                {
                    _arrowRepeatTimer += Time.deltaTime;
                    float repeatRate = _arrowHoldTimer > 1.5f ? 0.04f : 0.08f;
                    if (_arrowRepeatTimer >= repeatRate)
                    {
                        _arrowRepeatTimer = 0f;
                        delta += _arrowHoldTimer > 2.5f ? 4f : 1f;
                    }
                }
            }
            else
            {
                _arrowHoldTimer = 0f;
                _arrowRepeatTimer = 0f;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow)) coarseDelta -= 1f;
            if (Input.GetKeyDown(KeyCode.UpArrow)) coarseDelta += 1f;

            if (Mathf.Abs(delta) < 0.001f && Mathf.Abs(coarseDelta) < 0.001f) return;

            if (Mode == BearingMode.Motorized)
            {
                if (Mathf.Abs(delta) > 0.001f)
                {
                    MotorSpeed = Mathf.Clamp(MotorSpeed + delta * 15f, 5f, 3600f);
                }
                if (Mathf.Abs(coarseDelta) > 0.001f)
                {
                    MotorSpeed = Mathf.Clamp(MotorSpeed + coarseDelta * 60f, 5f, 3600f);
                }
                SaveZdo();
                player.Message(MessageHud.MessageType.Center, $"Motor Speed: {MotorSpeed:0}°/s ({MotorSpeed / 6f:0.1} RPM)");
            }
            else if (Mode == BearingMode.Steering)
            {
                if (Mathf.Abs(delta) > 0.001f)
                {
                    MaxSteerAngle = Mathf.Clamp(MaxSteerAngle + delta * 5f, 10f, 85f);
                    SaveZdo();
                    player.Message(MessageHud.MessageType.Center, $"Steer Limit: {MaxSteerAngle:0}°");
                }
                if (Mathf.Abs(coarseDelta) > 0.001f)
                {
                    SteerSpeed = Mathf.Clamp(SteerSpeed + coarseDelta * 20f, 30f, 720f);
                    SaveZdo();
                    player.Message(MessageHud.MessageType.Center, $"Steer Pivot Speed: {SteerSpeed:0}°/s");
                }
            }
            else if (Mode == BearingMode.FreeSpinning)
            {
                if (Mathf.Abs(delta) > 0.001f)
                {
                    _omega += delta * 60f;
                    player.Message(MessageHud.MessageType.Center, "Bearing: Spun push");
                }
            }
        }

        private void OpenConfigDialog()
        {
            if (TextInput.instance == null) return;
            string prompt = Mode == BearingMode.Motorized
                ? "Motor Speed (5 - 3600 °/s or e.g. '30 rpm')"
                : "Steer Angle Limit (10 - 85°)";
            TextInput.instance.RequestText(this, prompt, 12);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            var player = user as Player;
            if (player == null) return false;

            if (hold)
            {
                if (Mode == BearingMode.FreeSpinning)
                {
                    _omega += alt ? -250f : 250f;
                    player.Message(MessageHud.MessageType.Center, "Bearing: Spun push");
                    return true;
                }

                // [Hold E] in Motorized or Steering mode opens exact text input dialog
                OpenConfigDialog();
                return true;
            }

            if (alt)
            {
                // [Shift + E] toggles Reverse
                Reverse = !Reverse;
                SaveZdo();
                player.Message(MessageHud.MessageType.Center, Reverse ? "Bearing: Reversed" : "Bearing: Normal / Forward");
                return true;
            }

            // [E] cycles Mode: Steering -> FreeSpinning -> Motorized -> Steering
            Mode = (BearingMode)(((int)Mode + 1) % 3);
            _omega = 0f;
            SaveZdo();
            player.Message(MessageHud.MessageType.Center, $"Bearing: {Mode}");
            return true;
        }

        public string GetText()
        {
            if (Mode == BearingMode.Motorized)
            {
                return ((int)MotorSpeed).ToString();
            }
            return ((int)MaxSteerAngle).ToString();
        }

        public void SetText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Trim();

            bool isRpm = text.ToLowerInvariant().EndsWith("rpm");
            if (isRpm)
            {
                text = text.Substring(0, text.Length - 3).Trim();
            }

            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                if (isRpm) val *= 6f; // 1 RPM = 6 deg/sec

                if (Mode == BearingMode.Motorized)
                {
                    if (val < 0f)
                    {
                        Reverse = true;
                        val = Mathf.Abs(val);
                    }
                    MotorSpeed = Mathf.Clamp(val, 5f, 3600f);
                    SaveZdo();
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Bearing Motor Speed: {MotorSpeed:0}°/s ({MotorSpeed / 6f:0.1} RPM)");
                }
                else if (Mode == BearingMode.Steering)
                {
                    MaxSteerAngle = Mathf.Clamp(Mathf.Abs(val), 10f, 85f);
                    SaveZdo();
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Bearing Steer Limit: {MaxSteerAngle:0}°");
                }
            }
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
            zdo.Set(GetZdoKey("MotorSpeed"), MotorSpeed);
            zdo.Set(GetZdoKey("SteerSpeed"), SteerSpeed);
            zdo.Set(GetZdoKey("CurAngle"), CurrentAngle);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
        {
            string dirStr = Reverse ? "<color=orange>REV</color>" : "<color=cyan>FWD</color>";

            if (Mode == BearingMode.Motorized)
            {
                return Localization.instance.Localize(
                    "<b>Mechanical Bearing (Motorized)</b>\n" +
                    $"Speed: <color=yellow>{MotorSpeed:0}°/s ({MotorSpeed / 6f:0.1} RPM)</color>   Dir: {dirStr}\n" +
                    "[<color=yellow><b>Left / Right arrows</b></color>] Speed (±15°/s)   [<color=yellow><b>Up / Down</b></color>] ±60°/s\n" +
                    "[<color=yellow><b>$KEY_Use</b></color>] Mode   [<color=yellow><b>Hold $KEY_Use</b></color>] Set Speed   [<color=yellow><b>Shift + $KEY_Use</b></color>] Reverse"
                );
            }
            else if (Mode == BearingMode.Steering)
            {
                return Localization.instance.Localize(
                    "<b>Mechanical Bearing (Steering)</b>\n" +
                    $"Limit: <color=yellow>{MaxSteerAngle:0}°</color>   Speed: <color=yellow>{SteerSpeed:0}°/s</color>   Dir: {dirStr}\n" +
                    "[<color=yellow><b>Left / Right arrows</b></color>] Steer Limit   [<color=yellow><b>Up / Down</b></color>] Steer Speed\n" +
                    "[<color=yellow><b>$KEY_Use</b></color>] Mode   [<color=yellow><b>Hold $KEY_Use</b></color>] Set Angle   [<color=yellow><b>Shift + $KEY_Use</b></color>] Reverse"
                );
            }
            else
            {
                return Localization.instance.Localize(
                    "<b>Mechanical Bearing (Free Spinning)</b>\n" +
                    "Mode: <color=yellow>Free Spinning (Physics)</color>\n" +
                    "[<color=yellow><b>$KEY_Use</b></color>] Mode   [<color=yellow><b>Hold $KEY_Use</b></color>] Spin Push"
                );
            }
        }

        public string GetHoverName() => "Mechanical Bearing";

        public float GetHoverOffset() => 0f;

        private string GetZdoKey(string subKey)
        {
            if (_nview != null && _nview.gameObject != gameObject)
            {
                return $"Valhicle_B_{GetInstanceID()}_{subKey}";
            }
            return $"Valhicle_Bearing_{subKey}";
        }
    }
}
