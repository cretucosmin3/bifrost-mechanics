using HarmonyLib;
using UnityEngine;
using Valhicle.Core;

namespace Valhicle.Patches
{
    [HarmonyPatch(typeof(Player))]
    public static class MechanicToolPatches
    {
        public const string ToolName = "valhicle_wrench";

        public static bool IsHoldingMechanicTool(Player player)
        {
            if (player == null) return false;
            var rightItem = Traverse.Create(player).Field<ItemDrop.ItemData>("m_rightItem").Value;
            return rightItem != null && rightItem.m_dropPrefab != null && rightItem.m_dropPrefab.name == ToolName;
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        public static void UpdatePostfix(Player __instance)
        {
            if (!IsHoldingMechanicTool(__instance)) return;
            var hoverObj = __instance.GetHoverObject();
            if (hoverObj == null) return;

            var lift = hoverObj.GetComponentInParent<VehicleLift>();
            var vehicle = hoverObj.GetComponentInParent<VehicleCore>();
            if (lift == null && vehicle == null) return;

            if (Input.GetMouseButtonDown(0))
            {
                HandleWrenchClick(__instance, lift, vehicle);
            }
        }

        private static void HandleWrenchClick(Player player, VehicleLift lift, VehicleCore vehicle)
        {
            var ghost = Traverse.Create(player).Field<GameObject>("m_placementGhost").Value;
            if (ghost != null && ghost.activeSelf) return;

            if (lift == null && vehicle != null)
            {
                lift = vehicle.GetComponent<VehicleLift>() ?? vehicle.CurrentLift;
            }

            if (lift != null)
            {
                if (lift.IsBuildMode)
                {
                    lift.ReleaseToPhysics();
                    player.Message(MessageHud.MessageType.Center, "Platform dropped — physics on");
                }
                else
                {
                    lift.EnterBuildMode();
                    player.Message(MessageHud.MessageType.Center, "Platform frozen — Up/Down arrows to raise");
                }
                return;
            }

            if (vehicle != null && vehicle.IsOnLift)
            {
                vehicle.ReleaseFromLift();
                player.Message(MessageHud.MessageType.Center, "Vehicle released");
            }
        }
    }
}
