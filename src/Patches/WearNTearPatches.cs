using HarmonyLib;
using UnityEngine;
using Valhicle.Components;
using Valhicle.Core;

namespace Valhicle.Patches
{
    [HarmonyPatch(typeof(WearNTear))]
    public static class WearNTearPatches
    {
        public static bool IsValhicle(WearNTear wnt)
        {
            if (wnt == null) return false;
            if (wnt.GetComponent<VehiclePiece>() != null
                || wnt.GetComponent<VehicleLift>() != null
                || wnt.GetComponent<VehicleCore>() != null
                || wnt.GetComponent<VehicleBearing>() != null
                || wnt.GetComponentInParent<VehicleCore>() != null
                || wnt.GetComponentInParent<VehicleLift>() != null
                || wnt.GetComponentInParent<VehicleBearing>() != null)
            {
                return true;
            }

            var nv = wnt.GetComponent<ZNetView>();
            if (nv != null)
            {
                var zdo = nv.GetZDO();
                if (zdo != null && (zdo.GetZDOID(VehicleUtil.ParentZdoKey) != ZDOID.None || zdo.GetZDOID(VehicleUtil.BearingZdoKey) != ZDOID.None))
                {
                    return true;
                }
            }
            return false;
        }

        [HarmonyPatch("Awake")]
        [HarmonyPrefix]
        public static bool AwakePrefix(WearNTear __instance)
        {
            if (__instance.GetComponent<ZNetView>() == null)
            {
                __instance.enabled = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        public static void AwakePostfix(WearNTear __instance)
        {
            if (!IsValhicle(__instance)) return;
            __instance.m_noRoofWear = true;
            __instance.m_noSupportWear = true;
            __instance.m_supports = true;
            __instance.m_snow = null;
            __instance.m_snowWorn = null;
            __instance.m_snowBroken = null;
            __instance.m_snowDamageImmune = true;
            VehicleUtil.StripSnowOverlays(__instance.gameObject);
        }

        [HarmonyPatch(nameof(WearNTear.CanHaveSnow), new[] { typeof(bool) })]
        [HarmonyPrefix]
        public static bool CanHaveSnowPrefix(WearNTear __instance, ref bool __result)
        {
            if (!IsValhicle(__instance)) return true;
            __result = false;
            return false;
        }

        [HarmonyPatch(nameof(WearNTear.CanHaveSnow), new[] { typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        public static bool CanHaveSnowShieldedPrefix(WearNTear __instance, ref bool __result)
        {
            if (!IsValhicle(__instance)) return true;
            __result = false;
            return false;
        }

        [HarmonyPatch("UpdateSupport")]
        [HarmonyPrefix]
        public static bool UpdateSupportPrefix(WearNTear __instance, ref float ___m_support)
        {
            if (!IsValhicle(__instance)) return true;
            ___m_support = 1500f;
            return false;
        }

        [HarmonyPatch("HaveSupport")]
        [HarmonyPrefix]
        public static bool HaveSupportPrefix(WearNTear __instance, ref bool __result)
        {
            if (!IsValhicle(__instance)) return true;
            __result = true;
            return false;
        }

        [HarmonyPatch("GetSupport")]
        [HarmonyPrefix]
        public static bool GetSupportPrefix(WearNTear __instance, ref float __result)
        {
            if (!IsValhicle(__instance)) return true;
            __result = 1500f;
            return false;
        }
    }

    [HarmonyPatch(typeof(EffectArea), "Awake")]
    public static class EffectAreaPatches
    {
        [HarmonyPrefix]
        public static bool AwakePrefix(EffectArea __instance)
        {
            // Cloned smelter/chair visuals can carry EffectAreas with no valid setup.
            if (__instance.GetComponentInParent<VehicleEngine>() != null
                || __instance.GetComponentInParent<VehicleSeat>() != null
                || __instance.GetComponentInParent<VehicleLift>() != null
                || __instance.GetComponentInParent<VehicleCore>() != null)
            {
                Object.Destroy(__instance);
                return false;
            }
            return true;
        }
    }
}
