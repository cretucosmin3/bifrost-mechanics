using UnityEngine;
using Valhicle.Core;

namespace Valhicle.Components
{
    public enum WheelSize
    {
        Small = 0,
        Medium = 1,
        Large = 2
    }

    public class VehicleWheel : MonoBehaviour, Interactable, Hoverable
    {
        public WheelSize Size = WheelSize.Medium;
        public float Radius = 0.65f;
        public float SuspensionDistance = 0.35f;
        public float SpringStrength = 18000f;
        public float SpringDamper = 2500f;
        public float GripFactor = 1.0f;
        public bool IsMotorized = true;
        public bool IsInverted = false;
        public bool ReverseOnSteer = false;
        public Transform WheelMeshTransform;
        public Vector3 AxleAxis = Vector3.right;

        public bool IsGrounded { get; private set; }
        public float Compression { get; private set; }
        public Vector3 GroundHitPoint { get; private set; }
        public Vector3 ForcePoint => IsGrounded ? GroundHitPoint : transform.position;

        private float _currentSpinAngle;
        private Quaternion _meshBaseRot = Quaternion.identity;
        private bool _meshBaseCached;
        private Transform _arrow;
        private ZNetView _nview;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static int GroundMask = 0;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>() ?? GetComponentInParent<ZNetView>();
            ApplyWheelSizeProperties();
            if (GroundMask == 0)
            {
                GroundMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle");
            }
        }

        private void Start()
        {
            if (_nview != null && _nview.IsValid())
            {
                IsInverted = _nview.GetZDO().GetBool(GetZdoKey("Inverted"), IsInverted);
                ReverseOnSteer = _nview.GetZDO().GetBool(GetZdoKey("SteerRev"), ReverseOnSteer);
            }
        }

        public void ApplyWheelSizeProperties()
        {
            switch (Size)
            {
                case WheelSize.Small:
                    Radius = 0.40f;
                    SuspensionDistance = 0.22f;
                    SpringStrength = 16000f;
                    SpringDamper = 2800f;
                    break;
                case WheelSize.Medium:
                    Radius = 0.65f;
                    SuspensionDistance = 0.28f;
                    SpringStrength = 18000f;
                    SpringDamper = 3200f;
                    break;
                case WheelSize.Large:
                    Radius = 1.00f;
                    SuspensionDistance = 0.35f;
                    SpringStrength = 20000f;
                    SpringDamper = 3600f;
                    break;
            }
        }

        public Vector3 CalculateForces(VehicleCore vehicle, Rigidbody rb, float driveTorque, float brakeForce, float fixedDeltaTime)
        {
            IsGrounded = false;
            Compression = 0f;
            if (rb == null) return Vector3.zero;

            Vector3 origin = transform.position;
            Vector3 driveDir = GetDriveDirection(vehicle);
            Vector3 wheelRight = Vector3.Cross(Vector3.up, driveDir).normalized;

            float rayLength = Radius + SuspensionDistance;
            float sag = 0.04f;

            // Multi-ray sensing: central ground ray + forward & backward climbing rays to detect rocks/obstacles
            var rayOrigins = new[]
            {
                origin,
                origin + driveDir * (Radius * 0.35f),
                origin - driveDir * (Radius * 0.30f)
            };
            var rayDirs = new[]
            {
                Vector3.down,
                (Vector3.down + driveDir * 0.50f).normalized,
                (Vector3.down - driveDir * 0.40f).normalized
            };
            var rayLengths = new[]
            {
                rayLength,
                Radius * 1.20f + SuspensionDistance,
                Radius * 1.10f + SuspensionDistance
            };

            float maxPenetration = -float.MaxValue;
            RaycastHit best = default;
            float bestDistanceToOrigin = float.MaxValue;
            bool found = false;

            for (int r = 0; r < rayOrigins.Length; r++)
            {
                int count = Physics.RaycastNonAlloc(rayOrigins[r], rayDirs[r], Hits, rayLengths[r], GroundMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var hit = Hits[i];
                    if (hit.collider == null) continue;
                    if (hit.normal.y < 0.08f) continue; // Allow steep rock faces up to ~85°
                    if (vehicle != null && VehicleUtil.BelongsToVehicle(hit.collider, vehicle)) continue;

                    float distToOrigin = Vector3.Distance(origin, hit.point);
                    if (distToOrigin > rayLength + 0.15f) continue;

                    float penetration = (Radius + sag) - distToOrigin;
                    // Prefer obstacle contact that intrudes deepest into tire profile
                    if (penetration > maxPenetration)
                    {
                        maxPenetration = penetration;
                        bestDistanceToOrigin = distToOrigin;
                        best = hit;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                return Vector3.zero;
            }

            IsGrounded = true;
            GroundHitPoint = best.point;

            float x = Mathf.Max(0f, maxPenetration);
            Compression = Mathf.Clamp01(x / Mathf.Max(0.05f, SuspensionDistance));

            Vector3 pointVelocity = rb.GetPointVelocity(origin);
            float suspensionVelocity = Vector3.Dot(pointVelocity, Vector3.up);
            float springForce = x * SpringStrength - suspensionVelocity * SpringDamper;
            springForce = Mathf.Clamp(springForce, 0f, rb.mass * Physics.gravity.magnitude * 2.2f);

            // Push normal blends contact normal and world up to push away from rock slopes
            Vector3 pushDir = Vector3.Lerp(Vector3.up, best.normal, 0.45f).normalized;
            Vector3 netForce = pushDir * springForce;

            // Lateral grip resists sliding sideways
            float lateralVelocity = Vector3.Dot(pointVelocity, wheelRight);
            netForce += wheelRight * Mathf.Clamp(-lateralVelocity * (GripFactor * 3200f), -12000f, 12000f);

            // Surface-tangent drive direction drives the wheel UP the face of rocks and slopes
            Vector3 climbDir = Vector3.ProjectOnPlane(driveDir, best.normal).normalized;
            if (climbDir.sqrMagnitude < 0.01f) climbDir = driveDir;

            float longitudinalVelocity = Vector3.Dot(pointVelocity, climbDir);
            float motor = GetMotorInput(vehicle);

            if (brakeForce > 0.01f)
            {
                float brake = -Mathf.Sign(longitudinalVelocity) * Mathf.Min(Mathf.Abs(longitudinalVelocity) * 2000f, brakeForce);
                netForce += climbDir * brake;
            }
            else if (IsMotorized && Mathf.Abs(driveTorque) > 0.01f)
            {
                netForce += climbDir * (driveTorque / Mathf.Max(0.2f, Radius)) * motor;
            }

            // Ledge & rock climbing assist: when tire is powered into an obstacle, tire friction pulls the wheel up
            if (IsMotorized && Mathf.Abs(motor) > 0.05f)
            {
                Vector3 travelDir = driveDir * Mathf.Sign(motor);
                float obstacleDot = Vector3.Dot(travelDir, -best.normal);
                if (obstacleDot > 0.10f)
                {
                    float climbLift = Mathf.Clamp01(obstacleDot) * Mathf.Abs(motor) * (rb.mass * 9.81f * 0.70f);
                    netForce += Vector3.up * climbLift;
                }
            }

            netForce -= climbDir * (longitudinalVelocity * 20f);
            return netForce;
        }

        private void LateUpdate()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;
            EnsureMesh();
            var core = GetComponentInParent<VehicleCore>();
            Vector3 driveDir = GetDriveDirection(core);
            if (ReverseOnSteer && core != null)
            {
                float st = core.GetSteerInput();
                if (Mathf.Abs(st) > 0.05f) driveDir *= Mathf.Sign(st);
            }
            Vector3 forwardRoll = Vector3.Cross(transform.TransformDirection(AxleAxis), Vector3.up);
            if (forwardRoll.sqrMagnitude < 0.0001f) forwardRoll = transform.forward;
            forwardRoll = Vector3.ProjectOnPlane(forwardRoll, Vector3.up).normalized;

            float linear = 0f;
            if (core != null && core.Rb != null)
            {
                Vector3 vel = core.Rb.GetPointVelocity(transform.position);
                linear = Vector3.Dot(vel, forwardRoll);
                var seat = core.GetSteerSeat();
                float motor = GetMotorInput(core);
                if (Mathf.Abs(linear) < 0.15f && IsMotorized && Mathf.Abs(motor) > 0.05f)
                {
                    linear = Vector3.Dot(driveDir, forwardRoll) * motor * 6f;
                }
            }
            UpdateSpin(linear, Time.deltaTime);
            UpdateDirectionArrow(driveDir);
        }

        private void EnsureMesh()
        {
            if (WheelMeshTransform == null)
            {
                var spin = transform.Find("WheelSpin");
                WheelMeshTransform = spin != null ? spin : GetComponentInChildren<MeshFilter>(true)?.transform;
            }
            if (WheelMeshTransform != null && !_meshBaseCached)
            {
                _meshBaseRot = WheelMeshTransform.localRotation;
                _meshBaseCached = true;
            }
        }

        /// <summary>
        /// Push direction in the ground plane, from the wheel axle (how the tire actually rolls).
        /// Invert flips thrust. Do not use chassis.forward — wood floors often face sideways.
        /// </summary>
        public Vector3 GetDriveDirection(VehicleCore vehicle)
        {
            Vector3 axle = transform.TransformDirection(AxleAxis);
            Vector3 roll = Vector3.Cross(axle, Vector3.up);
            if (roll.sqrMagnitude < 0.0001f)
            {
                roll = transform.forward;
            }
            roll = Vector3.ProjectOnPlane(roll, Vector3.up);
            if (roll.sqrMagnitude < 0.0001f) roll = Vector3.forward;
            roll.Normalize();
            if (IsInverted) roll = -roll;
            return roll;
        }

        public float GetMotorInput(VehicleCore vehicle)
        {
            if (vehicle == null) return 0f;
            var seat = vehicle.GetSteerSeat();
            if (ReverseOnSteer) return vehicle.GetSteerInput();
            return seat != null ? seat.ThrottleInput : 0f;
        }

        private void UpdateDirectionArrow(Vector3 driveDir)
        {
            var player = Player.m_localPlayer;
            bool show = false;
            if (player != null)
            {
                var hover = player.GetHoverObject();
                show = hover != null && hover.GetComponentInParent<VehicleWheel>() == this;
            }
            if (!show)
            {
                if (_arrow != null) _arrow.gameObject.SetActive(false);
                return;
            }

            if (_arrow == null) _arrow = CreateArrow();
            _arrow.gameObject.SetActive(true);
            _arrow.position = transform.position + Vector3.up * (Radius + 0.25f);
            if (driveDir.sqrMagnitude > 0.001f)
            {
                _arrow.rotation = Quaternion.LookRotation(driveDir, Vector3.up);
            }
        }

        private Transform CreateArrow()
        {
            var root = new GameObject("DriveArrow").transform;
            root.SetParent(null, true);
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var mat = sh != null ? new Material(sh) : new Material(GetComponentInChildren<MeshRenderer>()?.sharedMaterial);
            if (mat != null) mat.color = new Color(1f, 0.85f, 0.1f, 1f);

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "Shaft";
            shaft.transform.SetParent(root, false);
            shaft.transform.localPosition = new Vector3(0f, 0f, 0.28f);
            shaft.transform.localScale = new Vector3(0.07f, 0.07f, 0.55f);
            Object.Destroy(shaft.GetComponent<Collider>());
            shaft.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(root, false);
            head.transform.localPosition = new Vector3(0f, 0f, 0.58f);
            head.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            head.transform.localScale = new Vector3(0.22f, 0.06f, 0.22f);
            Object.Destroy(head.GetComponent<Collider>());
            head.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return root;
        }

        private void OnDestroy()
        {
            if (_arrow != null) Object.Destroy(_arrow.gameObject);
        }

        private void UpdateSpin(float longitudinalVelocity, float dt)
        {
            if (Mathf.Abs(Radius) < 0.01f) return;
            float angularSpeed = (longitudinalVelocity / Radius) * Mathf.Rad2Deg;
            _currentSpinAngle += angularSpeed * dt;
            if (WheelMeshTransform == null) return;
            WheelMeshTransform.localRotation = _meshBaseRot * Quaternion.AngleAxis(_currentSpinAngle, AxleAxis);
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            var player = user as Player;
            if (alt)
            {
                ReverseOnSteer = !ReverseOnSteer;
                if (_nview != null && _nview.IsValid())
                {
                    _nview.GetZDO().Set(GetZdoKey("SteerRev"), ReverseOnSteer);
                }
                player?.Message(MessageHud.MessageType.Center,
                    ReverseOnSteer ? "Wheel: Reverse on steer (A/D)" : "Wheel: Drive with throttle (W/S)");
                return true;
            }

            IsInverted = !IsInverted;
            if (_nview != null && _nview.IsValid())
            {
                _nview.GetZDO().Set(GetZdoKey("Inverted"), IsInverted);
            }
            player?.Message(MessageHud.MessageType.Center, IsInverted ? "Wheel: Reverse (pushes backward)" : "Wheel: Forward");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
        {
            string dir = IsInverted ? "Inverted" : "Forward";
            string steer = ReverseOnSteer ? "ON" : "off";
            return Localization.instance.Localize(
                $"<b>Cart Wheel ({Size})</b>  {Radius:0.00}m\n" +
                $"Drive: <color=yellow>{dir}</color>   Reverse on steer: <color=yellow>{steer}</color>\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] Reverse   " +
                "[<color=yellow><b>Shift + $KEY_Use</b></color>] Reverse on A/D");
        }

        public string GetHoverName() => $"Cart Wheel ({Size})";

        public float GetHoverOffset() => 0f;

        private string GetZdoKey(string subKey) => $"Valhicle_Wheel_{subKey}";
    }
}
