using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Valhicle.Prefabs;

namespace Valhicle.Patches
{
    [HarmonyPatch(typeof(ZNetScene))]
    public static class ZNetScenePatches
    {
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        public static void AwakePostfix()
        {
            PrefabRegistry.RegisterAllPrefabs();
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        public static void OnDestroyPostfix()
        {
            PrefabRegistry.Reset();
        }

        /// <summary>
        /// Prevents console typing / autocomplete from throwing NullReferenceException on dead prefab references.
        /// </summary>
        [HarmonyPatch("GetPrefabNames")]
        [HarmonyPrefix]
        public static bool GetPrefabNamesPrefix(ZNetScene __instance, ref List<string> __result)
        {
            var list = new List<string>();
            var dict = Traverse.Create(__instance).Field<Dictionary<int, GameObject>>("m_namedPrefabs").Value;
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    if (kvp.Value != null)
                    {
                        try
                        {
                            string n = kvp.Value.name;
                            if (!string.IsNullOrEmpty(n))
                            {
                                list.Add(n);
                            }
                        }
                        catch
                        {
                            // Ignore destroyed Unity pseudo-null objects
                        }
                    }
                }
            }
            __result = list;
            return false;
        }

        /// <summary>
        /// Purges any destroyed or invalid ZNetViews from m_instances to prevent NRE during sector unloading.
        /// </summary>
        [HarmonyPatch("RemoveObjects")]
        [HarmonyPrefix]
        public static bool RemoveObjectsPrefix(Dictionary<ZDO, ZNetView> ___m_instances)
        {
            if (___m_instances == null) return true;

            var toRemove = new List<ZDO>();
            foreach (var kvp in ___m_instances)
            {
                if (kvp.Value == null || kvp.Value.GetZDO() == null)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove.Count > 0)
            {
                foreach (var zdo in toRemove)
                {
                    ___m_instances.Remove(zdo);
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ObjectDB))]
    public static class ObjectDBPatches
    {
        [HarmonyPatch("CopyOtherDB")]
        [HarmonyPostfix]
        public static void CopyOtherDBPostfix()
        {
            PrefabRegistry.RegisterAllPrefabs();
            PrefabRegistry.RegisterToHammerTable();
        }

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        public static void AwakePostfix()
        {
            PrefabRegistry.RegisterAllPrefabs();
            PrefabRegistry.RegisterToHammerTable();
        }
    }

    [HarmonyPatch(typeof(PieceTable))]
    public static class PieceTablePatches
    {
        /// <summary>
        /// Purges null or destroyed GameObjects from the piece table before UpdateAvailable checks piece.GetComponent&lt;Piece&gt;().
        /// Prevents game-breaking NullReferenceException crashes during build menu updates.
        /// </summary>
        [HarmonyPatch("UpdateAvailable")]
        [HarmonyPrefix]
        public static void UpdateAvailablePrefix(PieceTable __instance)
        {
            if (__instance != null && __instance.m_pieces != null)
            {
                __instance.m_pieces.RemoveAll(p => p == null || p.GetComponent<Piece>() == null);
            }
        }
    }

    [HarmonyPatch(typeof(BuildUi))]
    public static class BuildUiPatches
    {
        [HarmonyPatch("IsFavoritePiece")]
        [HarmonyPrefix]
        public static bool IsFavoritePiecePrefix(Piece piece, ref bool __result)
        {
            if (piece == null || piece.gameObject == null)
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch("UpdatePieceButtons")]
        [HarmonyPrefix]
        public static void UpdatePieceButtonsPrefix(BuildUi __instance)
        {
            if (__instance == null) return;
            var tempPieces = Traverse.Create(__instance).Field<List<Piece>>("m_tempPieces").Value;
            tempPieces?.RemoveAll(p => p == null || p.gameObject == null);
        }
    }

    [HarmonyPatch(typeof(FavoritePieceList))]
    public static class FavoritePieceListPatches
    {
        [HarmonyPatch("IsFavorite", new Type[] { typeof(Piece) })]
        [HarmonyPrefix]
        public static bool IsFavoritePrefix(Piece piece, ref bool __result)
        {
            if (piece == null || piece.gameObject == null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BuildUiPieceButton))]
    public static class BuildUiPieceButtonPatches
    {
        [HarmonyPatch("Setup")]
        [HarmonyPrefix]
        public static bool SetupPrefix(Piece pieceInfo)
        {
            if (pieceInfo == null || pieceInfo.gameObject == null)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ZNetView))]
    public static class ZNetViewPatches
    {
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        public static void AwakePostfix(ZNetView __instance)
        {
            if (__instance == null) return;
            var zdo = __instance.GetZDO();
            if (zdo == null) return;

            var parentId = zdo.GetZDOID(Valhicle.Core.VehicleUtil.ParentZdoKey);
            var bearingId = zdo.GetZDOID(Valhicle.Core.VehicleUtil.BearingZdoKey);
            var suspensionId = zdo.GetZDOID(Valhicle.Core.VehicleUtil.SuspensionZdoKey);
            if (parentId != ZDOID.None || bearingId != ZDOID.None || suspensionId != ZDOID.None)
            {
                if (__instance.GetComponent<Valhicle.Core.VehiclePiece>() == null)
                {
                    __instance.gameObject.AddComponent<Valhicle.Core.VehiclePiece>();
                }
                var wnt = __instance.GetComponent<WearNTear>();
                if (wnt != null)
                {
                    wnt.m_noRoofWear = true;
                    wnt.m_noSupportWear = true;
                    wnt.m_supports = true;
                    wnt.m_snowDamageImmune = true;
                }
            }
        }
    }
}
