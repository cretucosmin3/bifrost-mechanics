using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Valhicle
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.kozmo.valhicle";
        public const string ModName = "Valhicle";
        public const string ModVersion = "1.5.1";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        // Example configuration entry
        public static ConfigEntry<bool> ModEnabled { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Bind configuration settings (saved to BepInEx/config/com.kozmo.valhicle.cfg)
            ModEnabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enables or disables the Valhicle mod functionality."
            );

            if (!ModEnabled.Value)
            {
                Log.LogInfo($"{ModName} is disabled in configuration.");
                return;
            }

            // Apply Harmony patches
            _harmony = new Harmony(ModGUID);
            _harmony.PatchAll();

            Log.LogInfo($"{ModName} v{ModVersion} loaded successfully!");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Log?.LogInfo($"{ModName} unloaded.");
        }
    }
}
