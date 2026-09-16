using System;
using UnityEngine;
using Valhicle.Core;

namespace Valhicle.Components
{
    /// <summary>
    /// Driver seat with an integrated steering wheel.
    /// Implements IDoodadController to intercept player W/A/S/D inputs natively
    /// and keep the player securely seated playing the sitting animation.
    /// </summary>
    public class VehicleSeat : MonoBehaviour, Interactable, Hoverable, IDoodadController
    {
        [Header("Seat Configuration")]
        public Transform SeatMountPoint;
        public Transform SteeringWheelTransform;
        public Vector3 DetachOffset = new Vector3(0f, 0.5f, -1.0f);
        public float MaxSteeringWheelTurnAngle = 90f;

        // Current Driver
        public Player CurrentDriver { get; private set; }
        public bool IsOccupied => CurrentDriver != null;

        // Input state
        public float ThrottleInput { get; private set; } = 0f;
        public float SteerInput { get; private set; } = 0f;
        public bool HandbrakeInput { get; private set; } = false;

        private float _lastMountTime = 0f;
        private ZNetView _nview;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>() ?? GetComponentInParent<ZNetView>();

            if (SeatMountPoint == null)
            {
                var mp = transform.Find("MountPoint");
                SeatMountPoint = mp != null ? mp : transform;
            }

            if (SteeringWheelTransform == null || SteeringWheelTransform == transform)
            {
                SteeringWheelTransform = FindNamed(transform, "SteeringWheelPivot");
            }
        }

        private static Transform FindNamed(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var f = FindNamed(root.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        private void Update()
        {
            if (CurrentDriver == null)
            {
                ThrottleInput = 0f;
                SteerInput = 0f;
                HandbrakeInput = false;

                if (SteeringWheelTransform != null && SteeringWheelTransform != transform)
                {
                    SteeringWheelTransform.localRotation = Quaternion.identity;
                }
                return;
            }

            // Verify driver is still alive and attached
            if (CurrentDriver.IsDead() || !CurrentDriver.IsAttached())
            {
                DismountDriver();
                return;
            }

            // Check dismount input after a brief delay so the initial mount keypress is ignored
            if (Time.time - _lastMountTime > 0.6f && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape)))
            {
                DismountDriver();
                return;
            }

            if (SteeringWheelTransform != null && SteeringWheelTransform != transform
                && SteeringWheelTransform.GetComponent<VehicleSeat>() == null)
            {
                float targetAngle = -SteerInput * MaxSteeringWheelTurnAngle;
                SteeringWheelTransform.localRotation = Quaternion.Euler(0f, targetAngle, 0f);
            }
        }

        public void DismountDriver()
        {
            if (CurrentDriver != null)
            {
                var driver = CurrentDriver;
                CurrentDriver = null;
                ThrottleInput = 0f;
                SteerInput = 0f;
                HandbrakeInput = false;

                if (driver.GetDoodadController() == (IDoodadController)this)
                {
                    driver.StopDoodadControl();
                }

                if (driver.IsAttached())
                {
                    driver.AttachStop();
                }
            }
        }

        #region IDoodadController

        public void ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block)
        {
            // moveDir.z: Forward (+1) / Backward (-1)
            // moveDir.x: Left (-1) / Right (+1)
            ThrottleInput = moveDir.z;
            SteerInput = moveDir.x;
            HandbrakeInput = block || Input.GetKey(KeyCode.Space);
        }

        public void OnUseStop(Player player)
        {
            DismountDriver();
        }

        public Component GetControlledComponent() => this;

        public Vector3 GetPosition() => transform.position;

        public bool IsValid() => this != null && gameObject != null;

        #endregion

        #region Interactable & Hoverable

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;

            var player = user as Player;
            if (player == null) return false;

            if (CurrentDriver != null)
            {
                if (CurrentDriver == player)
                {
                    DismountDriver();
                    return true;
                }
                player.Message(MessageHud.MessageType.Center, "Seat is already occupied!");
                return false;
            }

            if (Time.time - _lastMountTime < 0.5f) return false;

            CurrentDriver = player;
            _lastMountTime = Time.time;

            var core = GetComponentInParent<VehicleCore>();
            core?.ClaimOwnership();
            core?.ScanChildComponents();

            player.StartDoodadControl(this);
            player.AttachStart(SeatMountPoint, gameObject, hideWeapons: false, isBed: false, onShip: true, "attach_chair", DetachOffset);
            player.Message(MessageHud.MessageType.Center, "Mounted Driver Seat! [W/S] Drive | [A/D] Steer | [Space] Brake | [E] Exit");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
        {
            if (CurrentDriver != null)
            {
                return Localization.instance.Localize("Driver Seat (Occupied)");
            }
            return Localization.instance.Localize("<b>Driver Seat & Steering Wheel</b>\n[<color=yellow><b>$KEY_Use</b></color>] Drive Cart");
        }

        public string GetHoverName() => "Driver Seat";

        public float GetHoverOffset() => 0f;

        #endregion

        private void OnDestroy()
        {
            DismountDriver();
        }
    }
}
