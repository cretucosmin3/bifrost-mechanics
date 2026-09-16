using UnityEngine;

namespace Valhicle.Components
{
    public enum EngineTier
    {
        Small = 0,
        Heavy = 1
    }

    /// <summary>
    /// Coal-burning steam engine. [E] opens a 4-slot coal inventory.
    /// Shift+[E] cycles power. Torque is fed to motorized wheels.
    /// </summary>
    public class VehicleEngine : MonoBehaviour, Interactable, Hoverable
    {
        public EngineTier Tier = EngineTier.Small;
        public float HorsepowerTorque = 4500f;
        public float SecondsPerCoal = 60f;
        public ParticleSystem SmokeEffect;
        public GameObject FireGlowObject;

        public int PowerPercent { get; private set; } = 100;
        public bool IsRunning { get; private set; }

        private float _fuelBurnTimer;
        private ZNetView _nview;
        private Container _container;
        static readonly int[] PowerSteps = { 25, 50, 75, 100 };

        private void Awake()
        {
            _nview = GetComponent<ZNetView>() ?? GetComponentInParent<ZNetView>();
            _container = GetComponent<Container>();
            ApplyTierSettings();
        }

        private void Start()
        {
            _container = GetComponent<Container>();
            if (_nview != null && _nview.IsValid())
            {
                PowerPercent = _nview.GetZDO().GetInt(GetZdoKey("Power"), 100);
                PowerPercent = Mathf.Clamp(PowerPercent, 25, 100);
            }
            UpdateVisuals();
        }

        public void ApplyTierSettings()
        {
            if (Tier == EngineTier.Small)
            {
                HorsepowerTorque = 5500f;
                SecondsPerCoal = 50f;
            }
            else
            {
                HorsepowerTorque = 11000f;
                SecondsPerCoal = 30f;
            }
        }

        public float UpdateEngine(float throttle, float deltaTime)
        {
            int coal = CountCoal();
            if (coal <= 0)
            {
                IsRunning = false;
                UpdateVisuals();
                return 0f;
            }

            float absDemand = Mathf.Abs(throttle);
            if (absDemand < 0.05f)
            {
                IsRunning = false;
                UpdateVisuals();
                return 0f;
            }

            IsRunning = true;
            float power = PowerPercent / 100f;
            _fuelBurnTimer += deltaTime * absDemand * Mathf.Lerp(0.35f, 1f, power);
            if (_fuelBurnTimer >= SecondsPerCoal)
            {
                _fuelBurnTimer = 0f;
                ConsumeOneCoal();
            }

            UpdateVisuals();
            return HorsepowerTorque * power;
        }

        private Inventory GetInv()
        {
            if (_container == null) _container = GetComponent<Container>();
            return _container != null ? _container.GetInventory() : null;
        }

        private int CountCoal()
        {
            var inv = GetInv();
            if (inv == null) return 0;
            int n = 0;
            foreach (var it in inv.GetAllItems())
            {
                if (it != null && IsCoal(it)) n += it.m_stack;
            }
            return n;
        }

        private void ConsumeOneCoal()
        {
            var inv = GetInv();
            if (inv == null) return;
            foreach (var it in inv.GetAllItems())
            {
                if (it != null && IsCoal(it) && it.m_stack > 0)
                {
                    inv.RemoveItem(it, 1);
                    return;
                }
            }
        }

        private void UpdateVisuals()
        {
            if (SmokeEffect != null)
            {
                if (IsRunning && !SmokeEffect.isPlaying) SmokeEffect.Play();
                else if (!IsRunning && SmokeEffect.isPlaying) SmokeEffect.Stop();
            }
            if (FireGlowObject != null)
            {
                FireGlowObject.SetActive(CountCoal() > 0);
            }
        }

        private void CyclePower()
        {
            int idx = 0;
            for (int i = 0; i < PowerSteps.Length; i++)
            {
                if (PowerSteps[i] == PowerPercent) { idx = i; break; }
            }
            PowerPercent = PowerSteps[(idx + 1) % PowerSteps.Length];
            if (_nview != null && _nview.IsValid())
            {
                _nview.GetZDO().Set(GetZdoKey("Power"), PowerPercent);
            }
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            var player = user as Player;
            if (player == null) return false;

            if (alt)
            {
                CyclePower();
                player.Message(MessageHud.MessageType.Center, $"Engine power: {PowerPercent}%");
                return true;
            }

            if (_container == null) _container = GetComponent<Container>();
            if (_container != null)
            {
                return _container.Interact(user, false, false);
            }

            player.Message(MessageHud.MessageType.Center, "Engine has no coal box.");
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (item != null && item.m_shared != null && IsCoal(item) && _container != null)
            {
                return _container.Interact(user, false, false);
            }
            return false;
        }

        private static bool IsCoal(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null) return false;
            string n = item.m_shared.m_name;
            if (n == "$item_coal" || n == "Coal") return true;
            var prefab = item.m_dropPrefab;
            return prefab != null && prefab.name.IndexOf("coal", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public string GetHoverText()
        {
            string engineName = Tier == EngineTier.Small ? "Small Steam Engine" : "Heavy Steam Engine";
            int coal = CountCoal();
            string coalColor = coal > 0 ? "orange" : "red";
            return Localization.instance.Localize(
                $"<b>{engineName}</b>\n" +
                $"Power: <color=yellow>{PowerPercent}%</color>   Coal: <color={coalColor}>{coal}</color>\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] Open coal (4 slots)\n" +
                "[<color=yellow><b>Shift + $KEY_Use</b></color>] Cycle power"
            );
        }

        public string GetHoverName() => Tier == EngineTier.Small ? "Small Steam Engine" : "Heavy Steam Engine";

        public float GetHoverOffset() => 0f;

        private string GetZdoKey(string subKey) => $"Valhicle_Engine_{subKey}";
    }
}
