using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Valhicle.Components;
using Valhicle.Core;

namespace Valhicle.Patches
{
    /// <summary>
    /// Example patch demonstrating how to hook into Valheim's Player class using Harmony.
    /// </summary>
    [HarmonyPatch(typeof(Player))]
    public static class PlayerPatches
    {
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        private static void AwakePostfix(Player __instance)
        {
            Plugin.Log.LogInfo($"Player '{__instance.GetPlayerName()}' initialized!");
        }

        [HarmonyPatch("OnSpawned")]
        [HarmonyPostfix]
        private static void OnSpawnedPostfix(Player __instance)
        {
            if (__instance == null) return;

            try
            {
                Prefabs.PrefabRegistry.RegisterAllPrefabs();
                Prefabs.PrefabRegistry.RegisterToHammerTable();

                var known = Traverse.Create(__instance).Field<HashSet<string>>("m_knownRecipes").Value;
                if (known != null && !known.Contains("Recipe_ValhicleWrench"))
                {
                    known.Add("Recipe_ValhicleWrench");
                }

                // Heal any existing Mechanic's Wrench in player inventory
                var wrenchPrefab = ObjectDB.instance?.GetItemPrefab("valhicle_wrench");
                if (wrenchPrefab != null)
                {
                    foreach (var item in __instance.GetInventory().GetAllItems())
                    {
                        if (item != null && item.m_shared != null && item.m_shared.m_name == "Mechanic's Wrench")
                        {
                            item.m_dropPrefab = wrenchPrefab;
                        }
                    }
                }

                Plugin.Log.LogInfo("Valhicle: Player spawned, registered Mechanic's Wrench recipe!");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error in OnSpawnedPostfix: {ex}");
            }
        }

        [HarmonyPatch("FindHoverObject")]
        [HarmonyPostfix]
        private static void FindHoverObjectPostfix(Player __instance, ref GameObject hover)
        {
            if (hover == null || GameCamera.instance == null) return;
            if (hover.GetComponent<VehicleLift>() == null && hover.GetComponent<VehicleCore>() == null)
            {
                return;
            }

            int mask = LayerMask.GetMask("Default", "static_solid", "piece", "piece_nonsolid", "item", "character", "vehicle");
            if (!Physics.Raycast(GameCamera.instance.transform.position, GameCamera.instance.transform.forward, out var hit, 12f, mask, QueryTriggerInteraction.Collide))
            {
                return;
            }

            var col = hit.collider;
            if (col == null) return;

            Component better = col.GetComponentInParent<VehicleSeat>();
            if (better == null) better = col.GetComponentInParent<VehicleEngine>();
            if (better == null) better = col.GetComponentInParent<VehicleWheel>();
            if (better == null) better = col.GetComponentInParent<VehicleBearing>();
            if (better == null) return;
            if (better.GetComponentInParent<VehicleCore>() != hover.GetComponentInParent<VehicleCore>()
                && better.GetComponentInParent<VehicleLift>() != hover.GetComponent<VehicleLift>())
            {
                // still allow if it's on this vehicle
                if (better.GetComponentInParent<VehicleCore>() == null) return;
            }
            hover = better.gameObject;
        }

        [HarmonyPatch("UpdateKnownRecipesList")]
        [HarmonyPrefix]
        private static void UpdateKnownRecipesListPrefix(Player __instance)
        {
            if (__instance == null) return;
            var inv = __instance.GetInventory();
            if (inv == null) return;

            var tables = new List<PieceTable>();
            inv.GetAllPieceTables(tables);
            foreach (var table in tables)
            {
                if (table != null && table.m_pieces != null)
                {
                    table.m_pieces.RemoveAll(p => p == null || p.GetComponent<Piece>() == null);
                }
            }
        }
    }
}
