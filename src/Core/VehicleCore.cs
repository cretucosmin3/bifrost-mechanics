using System.Collections.Generic;
using UnityEngine;
using Valhicle.Components;

namespace Valhicle.Core
{
    /// <summary>
    /// Physics brain for one cart. Lives on the chassis root (scale must stay 1,1,1).
    /// </summary>
    public class VehicleCore : MonoBehaviour
    {
        public float CenterOfMassOffset = -0.25f;
        public float MaxSpeed = 16f;
        public float AntiRollForce = 3500f;

        public readonly List<VehicleWheel> Wheels = new List<VehicleWheel>();
        public readonly List<VehicleEngine> Engines = new List<VehicleEngine>();
        public readonly List<VehicleSuspension> Suspensions = new List<VehicleSuspension>();
        public readonly List<VehicleBearing> Bearings = new List<VehicleBearing>();
        public readonly List<VehicleSeat> Seats = new List<VehicleSeat>();
        public readonly List<VehiclePiece> Pieces = new List<VehiclePiece>();

        public Rigidbody Rb { get; private set; }
        public bool IsOnLift { get; private set; }
        public VehicleLift CurrentLift { get; private set; }
        public ZNetView NetView { get; private set; }

        private float _zdoSyncTimer;
        private float _settleTimer;

        private void Awake()
        {
            NetView = GetComponent<ZNetView>();
            Rb = GetComponent<Rigidbody>();

            if (VehicleUtil.IsPlacementGhost(gameObject) || ZNetView.m_forceDisableInit)
            {
                if (Rb != null)
                {
                    Rb.isKinematic = true;
                    Rb.detectCollisions = false;
                    Rb.useGravity = false;
                }
                enabled = false;
                return;
            }

            if (Rb == null) Rb = gameObject.AddComponent<Rigidbody>();
            Rb.mass = 400f;
            Rb.linearDamping = 0.12f;
            Rb.angularDamping = 2.2f;
            Rb.interpolation = RigidbodyInterpolation.Interpolate;
            Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Rb.centerOfMass = new Vector3(0f, CenterOfMassOffset, 0f);
            Rb.useGravity = true;

            EnsureSyncTransform();
            ScanChildComponents();
        }

        private void EnsureSyncTransform()
        {
            var sync = GetComponent<ZSyncTransform>();
            if (sync == null) sync = gameObject.AddComponent<ZSyncTransform>();
            sync.m_syncPosition = true;
            sync.m_syncRotation = true;
            sync.m_syncScale = false;
            sync.m_syncBodyVelocity = true;
        }

        public void ScanChildComponents()
        {
            Wheels.Clear();
            Wheels.AddRange(GetComponentsInChildren<VehicleWheel>(true));

            Engines.Clear();
            Engines.AddRange(GetComponentsInChildren<VehicleEngine>(true));

            Suspensions.Clear();
            Suspensions.AddRange(GetComponentsInChildren<VehicleSuspension>(true));

            Bearings.Clear();
            Bearings.AddRange(GetComponentsInChildren<VehicleBearing>(true));

            Seats.Clear();
            Seats.AddRange(GetComponentsInChildren<VehicleSeat>(true));

            Pieces.Clear();
            Pieces.AddRange(GetComponentsInChildren<VehiclePiece>(true));

            RecalculateMass();
        }

        public VehicleSeat GetSteerSeat()
        {
            var seats = GetComponentsInChildren<VehicleSeat>(true);
            for (int i = 0; i < seats.Length; i++)
            {
                if (seats[i] != null && seats[i].IsOccupied) return seats[i];
            }
            for (int i = 0; i < Seats.Count; i++)
            {
                if (Seats[i] != null && Seats[i].IsOccupied) return Seats[i];
            }
            return null;
        }

        public float GetSteerInput()
        {
            var seat = GetSteerSeat();
            return seat != null ? seat.SteerInput : 0f;
        }

        public void RecalculateMass()
        {
            if (Rb == null) return;
            float mass = 280f;
            mass += Wheels.Count * 25f;
            mass += Engines.Count * 110f;
            mass += Mathf.Max(0, Pieces.Count - 1) * 12f;
            Rb.mass = Mathf.Clamp(mass, 200f, 5000f);
            Rb.centerOfMass = new Vector3(0f, CenterOfMassOffset, 0f);
        }

        public void ClaimOwnership()
        {
            if (NetView != null && NetView.IsValid())
            {
                NetView.ClaimOwnership();
            }
        }

        public void DockOnLift(VehicleLift lift, Transform mountPoint)
        {
            IsOnLift = true;
            CurrentLift = lift;

            ClaimOwnership();
            if (Rb != null)
            {
                Rb.linearVelocity = Vector3.zero;
                Rb.angularVelocity = Vector3.zero;
                Rb.isKinematic = true;
                Rb.useGravity = false;
            }
            ApplySyncBodyFlags(kinematic: true);

            if (mountPoint != null)
            {
                transform.position = mountPoint.position;
                transform.rotation = mountPoint.rotation;
            }

            VehicleUtil.ForceWorldScaleOne(transform);
        }

        public void ReleaseFromLift()
        {
            IsOnLift = false;
            CurrentLift = null;

            ClaimOwnership();
            StripStrayBodies();
            if (Rb == null) Rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
            Rb.detectCollisions = true;
            Rb.isKinematic = false;
            Rb.useGravity = true;
            Rb.linearVelocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            ApplySyncBodyFlags(kinematic: false);
            _settleTimer = 0.45f;
            Physics.SyncTransforms();
            Rb.WakeUp();
        }

        private void ApplySyncBodyFlags(bool kinematic)
        {
            var sync = GetComponent<ZSyncTransform>();
            if (sync == null) return;
            var tr = HarmonyLib.Traverse.Create(sync);
            tr.Field("m_isKinematicBody").SetValue(kinematic);
            tr.Field("m_useGravity").SetValue(!kinematic);
            if (NetView != null && NetView.IsValid())
            {
                NetView.ClaimOwnership();
            }
        }

        /// <summary>
        /// Extra rigidbodies/joints on child hubs were holding the cart in the air.
        /// </summary>
        public void StripStrayBodies()
        {
            var joints = GetComponentsInChildren<Joint>(true);
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null) Destroy(joints[i]);
            }

            var bodies = GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null || bodies[i] == Rb) continue;
                if (bodies[i].GetComponentInParent<VehicleBearing>() != null) continue;
                Destroy(bodies[i]);
            }
        }

        private void FixedUpdate()
        {
            if (Rb == null) return;
            if (NetView != null && NetView.IsValid() && !NetView.IsOwner()) return;
            if (IsOnLift)
            {
                Rb.isKinematic = true;
                Rb.useGravity = false;
                return;
            }

            Rb.isKinematic = false;
            Rb.useGravity = true;

            if (_settleTimer > 0f)
            {
                _settleTimer -= Time.fixedDeltaTime;
                Rb.linearVelocity = Vector3.Project(Rb.linearVelocity, Vector3.down);
                Rb.angularVelocity *= 0.5f;
                return;
            }

            VehicleSeat activeSeat = GetSteerSeat();
            float throttle = activeSeat != null ? activeSeat.ThrottleInput : 0f;
            float steer = GetSteerInput();
            bool handbrake = activeSeat != null && activeSeat.HandbrakeInput;

            bool steerDrive = false;
            for (int i = 0; i < Wheels.Count; i++)
            {
                if (Wheels[i] != null && Wheels[i].ReverseOnSteer) steerDrive = true;
            }
            float engineDemand = Mathf.Max(Mathf.Abs(throttle), steerDrive ? Mathf.Abs(steer) : 0f);

            float totalDriveTorque = 0f;
            for (int i = 0; i < Engines.Count; i++)
            {
                if (Engines[i] != null)
                {
                    totalDriveTorque += Engines[i].UpdateEngine(engineDemand, Time.fixedDeltaTime);
                }
            }

            int motorized = 0;
            for (int i = 0; i < Wheels.Count; i++)
            {
                if (Wheels[i] != null && Wheels[i].IsMotorized) motorized++;
            }

            float torquePerWheel = motorized > 0 ? totalDriveTorque / motorized : 0f;
            float brakeForce = handbrake ? 7000f : (Mathf.Abs(throttle) < 0.05f ? 180f : 0f);

            float totalCompression = 0f;
            for (int i = 0; i < Wheels.Count; i++)
            {
                var wheel = Wheels[i];
                if (wheel == null) continue;
                Vector3 force = wheel.CalculateForces(this, Rb, torquePerWheel, brakeForce, Time.fixedDeltaTime);
                if (force.sqrMagnitude > 0.0001f)
                {
                    Rb.AddForceAtPosition(force, wheel.ForcePoint, ForceMode.Force);
                }
                totalCompression += wheel.Compression;
            }

            float avgCompression = Wheels.Count > 0 ? totalCompression / Wheels.Count : 0f;
            for (int i = 0; i < Suspensions.Count; i++)
            {
                if (Suspensions[i] != null)
                {
                    Suspensions[i].UpdateVisuals(avgCompression);
                }
            }

            ApplyAntiRoll();

            if (Rb.linearVelocity.magnitude > MaxSpeed)
            {
                Rb.linearVelocity = Vector3.ClampMagnitude(Rb.linearVelocity, MaxSpeed);
            }

            _zdoSyncTimer += Time.fixedDeltaTime;
            if (_zdoSyncTimer >= 0.5f)
            {
                _zdoSyncTimer = 0f;
                SyncAttachedZdos();
            }
        }

        private void ApplyAntiRoll()
        {
            if (Wheels.Count < 2) return;
            Vector3 right = transform.right;
            float tilt = Vector3.Dot(transform.up, Vector3.up);
            if (tilt > 0.98f) return;
            Vector3 correction = Vector3.Cross(transform.up, Vector3.up);
            Rb.AddTorque(correction * AntiRollForce * (1f - Mathf.Clamp01(tilt)), ForceMode.Force);
        }

        private void SyncAttachedZdos()
        {
            if (NetView != null && NetView.IsValid() && NetView.IsOwner())
            {
                NetView.GetZDO().SetPosition(transform.position);
                NetView.GetZDO().SetRotation(transform.rotation);
            }

            for (int i = 0; i < Bearings.Count; i++)
            {
                if (Bearings[i] != null) Bearings[i].SyncZdo();
            }

            for (int i = 0; i < Suspensions.Count; i++)
            {
                if (Suspensions[i] != null) Suspensions[i].SyncZdo();
            }

            for (int i = 0; i < Pieces.Count; i++)
            {
                if (Pieces[i] != null) Pieces[i].SyncWorldZdo();
            }
        }

        public void RegisterPiece(VehiclePiece piece)
        {
            if (piece == null) return;
            if (!Pieces.Contains(piece))
            {
                Pieces.Add(piece);
            }
            ScanChildComponents();
        }

        public void UnregisterPiece(VehiclePiece piece)
        {
            if (piece == null) return;
            if (Pieces.Remove(piece))
            {
                ScanChildComponents();
            }
        }
    }
}
