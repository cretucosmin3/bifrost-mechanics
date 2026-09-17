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
            if (GameCamera.instance == null || __instance == null) return;
            if (hover == null) return;

            var vehicle = hover.GetComponentInParent<VehicleCore>();
            var lift = hover.GetComponentInParent<VehicleLift>();
            if (vehicle == null && lift == null) return;

            Transform vehicleRoot = vehicle != null ? vehicle.transform : lift.transform;

            float maxDist = 5f;
            try
            {
                maxDist = Traverse.Create(__instance).Field<float>("m_maxInteractDistance").Value;
                if (maxDist < 1f) maxDist = 5f;
            }
            catch
            {
                maxDist = 5f;
            }

            int mask = LayerMask.GetMask("Default", "static_solid", "piece", "piece_nonsolid", "item", "character", "vehicle");
            if (!Physics.Raycast(GameCamera.instance.transform.position, GameCamera.instance.transform.forward, out var hit, 16f, mask, QueryTriggerInteraction.Collide))
            {
                return;
            }

            var col = hit.collider;
            if (col == null) return;

            // Ensure the raycast hit actually belongs to this vehicle hierarchy
            if (col.transform != vehicleRoot && !col.transform.IsChildOf(vehicleRoot))
            {
                return;
            }

            // Check interact distance
            Hoverable targetHoverable = col.GetComponentInParent<Hoverable>();
            float hoverOffset = targetHoverable != null ? targetHoverable.GetHoverOffset() : 0f;
            if (Vector3.Distance(__instance.GetEyePoint(), hit.point) > maxDist + hoverOffset + 1.5f)
            {
                return;
            }

            // Walk upwards from the hit collider looking for the most specific Hoverable / Interactable
            Transform current = col.transform;
            GameObject specificTarget = null;

            while (current != null && current != vehicleRoot)
            {
                // Prefer specific vehicle components or game interactables (Bed, Container, Chair, Door, CraftingStation, etc.)
                var h = current.GetComponent<Hoverable>();
                var inter = current.GetComponent<Interactable>();
                if (h != null || inter != null)
                {
                    specificTarget = current.gameObject;
                    break;
                }

                // If this is a Piece with interactive children
                var piece = current.GetComponent<Piece>();
                if (piece != null)
                {
                    var childHoverable = piece.GetComponentInChildren<Hoverable>(true);
                    var childInteractable = piece.GetComponentInChildren<Interactable>(true);
                    if (childHoverable != null)
                    {
                        specificTarget = (childHoverable as MonoBehaviour)?.gameObject ?? piece.gameObject;
                        break;
                    }
                    if (childInteractable != null)
                    {
                        specificTarget = (childInteractable as MonoBehaviour)?.gameObject ?? piece.gameObject;
                        break;
                    }
                }

                current = current.parent;
            }

            if (specificTarget != null)
            {
                hover = specificTarget;
            }
            else
            {
                // If the player aimed at an attached structural piece (e.g. wood wall or roof)
                // that has no interaction, do not display the vehicle lift menu!
                var vp = col.GetComponentInParent<VehiclePiece>();
                if (vp != null && vp.transform != vehicleRoot)
                {
                    hover = null;
                }
            }
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
