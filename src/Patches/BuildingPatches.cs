using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Valhicle.Components;
using Valhicle.Core;

namespace Valhicle.Patches
{
    [HarmonyPatch(typeof(Player))]
    public static class BuildingPatches
    {
        public static Collider LastHitCollider { get; private set; }
        public static Vector3 LastHitPoint { get; private set; }
        public static float LastHitTime { get; private set; }

        [HarmonyPatch("PlacePiece")]
        [HarmonyPostfix]
        public static void PlacePiecePostfix(Player __instance, Piece piece, Vector3 pos, Quaternion rot)
        {
            if (piece == null) return;

            Piece placed = FindPlacedInstance(piece, pos);
            if (placed == null) return;

            VehicleUtil.MakePlacedPieceReal(placed.gameObject);

            var lift = placed.GetComponent<VehicleLift>();
            if (lift != null && VehicleUtil.PrefabName(placed.gameObject) == "valhicle_lift")
            {
                lift.OnPlaced();
                return;
            }

            var rope = placed.GetComponent<VehicleRope>();
            if (rope != null)
            {
                if (VehicleRope.ActivePendingRope == null)
                {
                    rope.InitAnchorA(pos, LastHitCollider, LastHitPoint);
                    VehicleRope.ActivePendingRope = rope;
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Rope Anchor A set! Click point B to tie down.");
                    return;
                }
                else
                {
                    var active = VehicleRope.ActivePendingRope;
                    active.InitAnchorB(pos, LastHitCollider, LastHitPoint);
                    VehicleRope.ActivePendingRope = null;
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Rope secured! Span: {active.RestLength:0.00}m ([E] Stiff/Elastic, [L] Length)");

                    // Refund materials consumed for piece B so a rope costs 1 placement
                    var pieceComp = placed.GetComponent<Piece>();
                    if (pieceComp != null && pieceComp.m_resources != null && Player.m_localPlayer != null)
                    {
                        foreach (var req in pieceComp.m_resources)
                        {
                            if (req != null && req.m_resItem != null && req.m_amount > 0)
                            {
                                Player.m_localPlayer.GetInventory()?.AddItem(req.m_resItem.gameObject, req.m_amount);
                            }
                        }
                    }

                    var nv = placed.GetComponent<ZNetView>();
                    if (nv != null && nv.IsValid())
                    {
                        nv.ClaimOwnership();
                        ZNetScene.instance?.Destroy(placed.gameObject);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(placed.gameObject);
                    }
                    return;
                }
            }

            var vp = placed.GetComponent<VehiclePiece>() ?? placed.gameObject.AddComponent<VehiclePiece>();
            VehicleBearing targetBearing = null;
            bool directHubHit = false;

            if (LastHitCollider != null 
                && Time.time - LastHitTime < 0.8f 
                && Vector3.Distance(placed.transform.position, LastHitPoint) < 2.2f)
            {
                var bearingCandidate = LastHitCollider.GetComponentInParent<VehicleBearing>();
                if (bearingCandidate != null)
                {
                    if (bearingCandidate.IsHubTarget(LastHitPoint, LastHitCollider))
                    {
                        targetBearing = bearingCandidate;
                        directHubHit = true;
                    }
                    else if (bearingCandidate.Owns(LastHitCollider.transform))
                    {
                        targetBearing = bearingCandidate;
                    }
                }

                if (targetBearing == null)
                {
                    var hitPiece = LastHitCollider.GetComponentInParent<VehiclePiece>();
                    if (hitPiece != null)
                    {
                        if (hitPiece.AttachedBearing != null && hitPiece.AttachedBearing.Owns(hitPiece.transform))
                        {
                            targetBearing = hitPiece.AttachedBearing;
                        }
                    }
                }
            }
            vp.OnPlaced(targetBearing, directHubHit);
        }

        private static Piece FindPlacedInstance(Piece prefab, Vector3 pos)
        {
            var found = new List<Piece>();
            Piece.GetAllPiecesInRadius(pos, 2.5f, found);
            string want = VehicleUtil.PrefabName(prefab.gameObject);
            Piece best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < found.Count; i++)
            {
                var p = found[i];
                if (p == null || p == prefab) continue;
                if (VehicleUtil.PrefabName(p.gameObject) != want) continue;
                float d = Vector3.SqrMagnitude(p.transform.position - pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }

            if (best == null)
            {
                Piece.GetAllPiecesInRadius(pos, 6.0f, found);
                for (int i = 0; i < found.Count; i++)
                {
                    var p = found[i];
                    if (p == null || p == prefab) continue;
                    if (VehicleUtil.PrefabName(p.gameObject) != want) continue;
                    float d = Vector3.SqrMagnitude(p.transform.position - pos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = p;
                    }
                }
            }

            return best;
        }

        public static GameObject CurrentPlacementGhost { get; private set; }

        [HarmonyPatch("UpdatePlacementGhost")]
        [HarmonyPostfix]
        public static void UpdatePlacementGhostPostfix(Player __instance, GameObject ___m_placementGhost)
        {
            CurrentPlacementGhost = ___m_placementGhost;
            if (___m_placementGhost == null) return;

            if (VehiclePlacement.IsVehicleBuildPiece(___m_placementGhost))
            {
                VehiclePlacement.TryUpdateGhost(__instance, ___m_placementGhost);
                return;
            }

            // For vanilla build pieces targeted on vehicles/bearings:
            if (LastHitCollider != null && Time.time - LastHitTime < 1.0f && IsBuildableVehicleSurface(LastHitCollider))
            {
                var tr = Traverse.Create(__instance);
                int status = tr.Field<int>("m_placementStatus").Value;
                if (status != 0)
                {
                    tr.Field("m_placementStatus").SetValue(0);
                    var setValid = AccessTools.Method(typeof(Player), "SetPlacementGhostValid");
                    setValid?.Invoke(__instance, new object[] { true });
                }
            }
        }

        /// <summary>
        /// Vanilla PieceRayTest skips any collider with a Rigidbody, so the vehicle platform
        /// (which must have one) is invisible to building. Prefer hits on our pieces.
        /// </summary>
        [HarmonyPatch("PieceRayTest")]
        [HarmonyPostfix]
        public static void PieceRayTestPostfix(
            Player __instance,
            GameObject ___m_placementGhost,
            ref bool __result,
            ref Vector3 point,
            ref Vector3 normal,
            ref Piece piece,
            ref Heightmap heightmap,
            ref Collider waterSurface)
        {
            if (GameCamera.instance == null) return;
            if (!VehiclePlacement.TryRaycast(__instance, ___m_placementGhost, out RaycastHit hit)) return;
            if (hit.collider == null) return;
            if (!IsBuildableVehicleSurface(hit.collider)) return;

            Vector3 cam = GameCamera.instance.transform.position;
            float existing = __result ? Vector3.Distance(cam, point) : float.MaxValue;
            if (hit.distance <= existing + 0.05f)
            {
                __result = true;
                point = hit.point;
                normal = hit.normal;
                piece = hit.collider.GetComponentInParent<Piece>();
                heightmap = hit.collider.GetComponent<Heightmap>();
                waterSurface = null;

                LastHitCollider = hit.collider;
                LastHitPoint = hit.point;
                LastHitTime = Time.time;
            }
        }

        private static bool IsBuildableVehicleSurface(Collider col)
        {
            if (col == null) return false;
            return col.GetComponentInParent<VehicleLift>() != null
                || col.GetComponentInParent<VehicleCore>() != null
                || col.GetComponentInParent<VehicleBearing>() != null
                || col.GetComponentInParent<VehicleRope>() != null
                || col.GetComponentInParent<VehiclePiece>() != null;
        }
    }
}
