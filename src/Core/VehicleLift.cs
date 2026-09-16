using UnityEngine;

namespace Valhicle.Core
{
    /// <summary>
    /// A single 2x2 wooden floor. Up/Down arrows raise and lower it for building;
    /// [E] freezes or drops it as a physics vehicle. No pillar, no extra chassis.
    /// </summary>
    public class VehicleLift : MonoBehaviour, Interactable, Hoverable, IPlaced
    {
        public const string HeightZdoKey = "Valhicle_PlatHeight";
        public const string GroundYZdoKey = "Valhicle_GroundY";
        public const string BuildModeZdoKey = "Valhicle_BuildMode";

        public float MinHeight = 0.05f;
        public float MaxHeight = 4.0f;
        public float RaiseSpeed = 2.2f;

        public VehicleCore Core { get; private set; }
        public bool IsBuildMode => Core != null && Core.IsOnLift;

        private ZNetView _nview;
        private float _groundY;
        private bool _groundSampled;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            Core = GetComponent<VehicleCore>();
            if (!ZNetView.m_forceDisableInit)
            {
                VehicleUtil.MakePlacedPieceReal(gameObject);
            }
        }

        private void Start()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject))
            {
                return;
            }

            VehicleUtil.MakePlacedPieceReal(gameObject);
            if (Core == null) Core = GetComponent<VehicleCore>();
            RestoreHeight();
            bool build = true;
            if (_nview != null && _nview.IsValid())
            {
                build = _nview.GetZDO().GetBool(BuildModeZdoKey, true);
            }
            if (build) EnterBuildMode();
            else ReleaseToPhysics();
        }

        public void OnPlaced()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;
            VehicleUtil.MakePlacedPieceReal(gameObject);
            SampleGround(force: true);
            EnterBuildMode();
        }

        public void EnterBuildMode()
        {
            if (Core == null) Core = GetComponent<VehicleCore>() ?? gameObject.AddComponent<VehicleCore>();
            Core.DockOnLift(this, transform);
            if (_nview != null && _nview.IsValid() && _nview.IsOwner())
            {
                _nview.GetZDO().Set(BuildModeZdoKey, true);
            }
        }

        public void ReleaseToPhysics()
        {
            if (Core == null) Core = GetComponent<VehicleCore>();
            Core?.ScanChildComponents();
            Core?.ReleaseFromLift();
            if (_nview != null && _nview.IsValid() && _nview.IsOwner())
            {
                _nview.GetZDO().Set(BuildModeZdoKey, false);
            }
        }

        public float GetHeight()
        {
            return transform.position.y - _groundY;
        }

        public void SetHeight(float height)
        {
            SampleGround();
            height = Mathf.Clamp(height, MinHeight, MaxHeight);
            Vector3 p = transform.position;
            p.y = _groundY + height;
            transform.position = p;

            if (_nview != null && _nview.IsValid() && _nview.IsOwner())
            {
                _nview.GetZDO().Set(HeightZdoKey, height);
                _nview.GetZDO().Set(GroundYZdoKey, _groundY);
            }
        }

        public void AdjustHeight(float delta)
        {
            if (!IsBuildMode) EnterBuildMode();
            SetHeight(GetHeight() + delta);
        }

        private void SampleGround(bool force = false)
        {
            if (_groundSampled && !force) return;
            _groundY = transform.position.y;
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain");
            if (Physics.Raycast(transform.position + Vector3.up * 4f, Vector3.down, out var hit, 16f, mask, QueryTriggerInteraction.Ignore))
            {
                if (!VehicleUtil.BelongsToVehicle(hit.collider, Core))
                {
                    _groundY = hit.point.y;
                }
            }
            _groundSampled = true;
        }

        private void RestoreHeight()
        {
            if (_nview == null || !_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            if (zdo.GetFloat(GroundYZdoKey, out float gy))
            {
                _groundY = gy;
                _groundSampled = true;
            }
        }

        private void Update()
        {
            if (VehicleUtil.IsPlacementGhost(gameObject)) return;
            if (Player.m_localPlayer == null) return;
            if (UiBlocksInput()) return;
            if (Vector3.Distance(Player.m_localPlayer.transform.position, transform.position) > 10f) return;
            if (!IsClosestToLocalPlayer()) return;

            float dir = 0f;
            if (Input.GetKey(KeyCode.UpArrow)) dir += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) dir -= 1f;
            if (Mathf.Abs(dir) < 0.01f) return;

            AdjustHeight(dir * RaiseSpeed * Time.deltaTime);
        }

        private bool IsClosestToLocalPlayer()
        {
            var player = Player.m_localPlayer;
            if (player == null) return false;
            var lifts = FindObjectsOfType<VehicleLift>();
            float my = Vector3.SqrMagnitude(transform.position - player.transform.position);
            for (int i = 0; i < lifts.Length; i++)
            {
                if (lifts[i] == null || lifts[i] == this) continue;
                if (VehicleUtil.IsPlacementGhost(lifts[i].gameObject)) continue;
                float d = Vector3.SqrMagnitude(lifts[i].transform.position - player.transform.position);
                if (d < my) return false;
            }
            return true;
        }

        private static bool UiBlocksInput()
        {
            try
            {
                if (Console.IsVisible()) return true;
            }
            catch { /* ignored */ }

            try
            {
                if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            }
            catch { /* ignored */ }

            try
            {
                if (TextInput.IsVisible()) return true;
            }
            catch { /* ignored */ }

            return false;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            var player = user as Player;
            if (player == null) return false;

            if (IsBuildMode)
            {
                ReleaseToPhysics();
                player.Message(MessageHud.MessageType.Center, "Platform dropped — physics on. Drive or [E] to freeze again.");
            }
            else
            {
                EnterBuildMode();
                player.Message(MessageHud.MessageType.Center, "Platform frozen. Up/Down arrows raise and lower it.");
            }
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public string GetHoverText()
        {
            string state = IsBuildMode
                ? "<color=cyan>Frozen (building)</color>"
                : "<color=orange>Physics active</color>";
            return Localization.instance.Localize(
                "<b>Vehicle Platform</b>\n" +
                $"Status: {state}   Height: {GetHeight():0.0}m\n" +
                "[<color=yellow><b>Up / Down arrows</b></color>] Raise / lower\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] Freeze for building / drop to physics"
            );
        }

        public string GetHoverName() => "Vehicle Platform";

        public float GetHoverOffset() => 0.2f;
    }
}
