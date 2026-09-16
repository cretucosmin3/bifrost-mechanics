using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Valhicle.Components;
using Valhicle.Core;

namespace Valhicle.Prefabs
{
    public static class PrefabRegistry
    {
        private static bool _isRegistered = false;
        private static readonly List<GameObject> _registeredPrefabs = new List<GameObject>();
        private static GameObject _prefabRoot;
        private static GameObject _wrenchPrefab;
        private static PieceTable _vehiclePieceTable;
        private static Piece _templatePiece;
        private static Material _woodMaterial;

        public static void Reset()
        {
            _isRegistered = false;
        }

        public static void RegisterAllPrefabs()
        {
            if (_isRegistered) return;
            if (ZNetScene.instance == null || ObjectDB.instance == null) return;

            Plugin.Log.LogInfo("Registering Valhicle modular pieces...");

            try
            {
                if (_prefabRoot == null)
                {
                    _prefabRoot = new GameObject("Valhicle_PrefabRoot");
                    _prefabRoot.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_prefabRoot);
                }

                _registeredPrefabs.Clear();

                var cartPrefab = FindPrefab("Cart", "cart", "piece_cart", "Vagon");
                var woodFloorPrefab = FindPrefab("wood_floor", "piece_woodfloor2x2", "piece_woodfloor1x1", "piece_woodfloor", "piece_woodwall")
                                   ?? cartPrefab;
                var polePrefab = FindPrefab("wood_pole", "wood_pole2", "wood_beam", "piece_woodpole", "piece_woodbeam")
                              ?? woodFloorPrefab;
                var chairPrefab = FindPrefab("piece_chair", "piece_chair02", "piece_chair01", "piece_bench01", "chair")
                               ?? woodFloorPrefab;
                var smelterPrefab = FindPrefab("smelter", "piece_smelter")
                                 ?? cartPrefab;
                var hammerPrefab = ObjectDB.instance.GetItemPrefab("Hammer")
                                ?? FindPrefab("Hammer", "hammer");
                var springItem = ObjectDB.instance.GetItemPrefab("MechanicalSpring")
                              ?? FindPrefab("MechanicalSpring", "item_mechanicalspring");
                var wheelSource = FindWheelSource(cartPrefab);

                if (woodFloorPrefab != null)
                {
                    _templatePiece = woodFloorPrefab.GetComponent<Piece>();
                    _woodMaterial = VehicleUtil.GetSharedMaterial(woodFloorPrefab);
                }

                Plugin.Log.LogInfo($"Found base prefabs: Cart={cartPrefab?.name}, WheelSource={wheelSource?.name}, Spring={springItem?.name}, Chair={chairPrefab?.name}, Smelter={smelterPrefab?.name}");

                CreateLiftPrefab(woodFloorPrefab, polePrefab);
                CreateChassisPrefab(woodFloorPrefab);

                CreateWheelPrefab(wheelSource, WheelSize.Small, "valhicle_wheel_small", "Cart Wheel (Small)", 0.40f,
                    new[] { Req("Wood", 4), Req("BronzeNails", 2) });
                CreateWheelPrefab(wheelSource, WheelSize.Medium, "valhicle_wheel_medium", "Cart Wheel (Medium)", 0.65f,
                    new[] { Req("Wood", 8), Req("BronzeNails", 4) });
                CreateWheelPrefab(wheelSource, WheelSize.Large, "valhicle_wheel_large", "Cart Wheel (Large)", 1.00f,
                    new[] { Req("FineWood", 12), Req("IronNails", 6) });

                CreateSuspensionPrefab(springItem, SuspensionType.Standard, "valhicle_suspension_std", "Vehicle Suspension (Standard)",
                    new[] { Req("Wood", 4), Req("Iron", 2) });
                CreateSuspensionPrefab(springItem, SuspensionType.HeavyDuty, "valhicle_suspension_hd", "Vehicle Suspension (Heavy-Duty)",
                    new[] { Req("Iron", 4), Req("Bronze", 2) });

                CreateButtonBearingPrefab("valhicle_bearing", "Mechanical Bearing (Swivel)",
                    new[] { Req("Bronze", 2), Req("FineWood", 2) });

                CreateSeatPrefab(chairPrefab, wheelSource, "valhicle_seat", "Driver Seat & Steering Wheel",
                    new[] { Req("FineWood", 6), Req("DeerHide", 2), Req("Bronze", 2) });

                CreateEnginePrefab(smelterPrefab, EngineTier.Small, "valhicle_engine_small", "Small Steam Engine", 0.45f,
                    new[] { Req("SurtlingCore", 1), Req("Iron", 10), Req("Stone", 5), Req("Wood", 5) });
                CreateEnginePrefab(smelterPrefab, EngineTier.Heavy, "valhicle_engine_heavy", "Heavy Steam Engine", 0.70f,
                    new[] { Req("SurtlingCore", 2), Req("Iron", 20), Req("Stone", 10) });

                CreateWrenchTool(hammerPrefab);
                RegisterToHammerTable();

                _isRegistered = true;
                Plugin.Log.LogInfo($"Successfully registered {_registeredPrefabs.Count} Valhicle pieces!");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to register Valhicle prefabs: {ex}");
            }
        }

        private static GameObject FindPrefab(string preferredName, params string[] fallbacks)
        {
            if (ZNetScene.instance == null) return null;

            var go = ZNetScene.instance.GetPrefab(preferredName);
            if (go != null) return go;

            foreach (var fb in fallbacks)
            {
                go = ZNetScene.instance.GetPrefab(fb);
                if (go != null) return go;
            }

            if (ZNetScene.instance.m_prefabs != null)
            {
                foreach (var p in ZNetScene.instance.m_prefabs)
                {
                    if (p != null && string.Equals(p.name, preferredName, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
                foreach (var p in ZNetScene.instance.m_prefabs)
                {
                    if (p != null && p.name.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return p;
                }
            }

            return null;
        }

        private static GameObject FindWheelSource(GameObject cartPrefab)
        {
            if (cartPrefab == null) return null;

            var vagon = cartPrefab.GetComponent<Vagon>();
            if (vagon != null && vagon.m_wheels != null && vagon.m_wheels.Length > 0 && vagon.m_wheels[0] != null)
            {
                return vagon.m_wheels[0].gameObject;
            }

            foreach (var child in cartPrefab.GetComponentsInChildren<Transform>(true))
            {
                if (child != cartPrefab.transform && child.name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0 && child.GetComponent<MeshFilter>() != null)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private static void SetGhostInit(bool value)
        {
            Traverse.Create(typeof(ZNetView)).Field<bool>("m_ghostInit").Value = value;
        }

        private static bool GetGhostInit()
        {
            return Traverse.Create(typeof(ZNetView)).Field<bool>("m_ghostInit").Value;
        }

        private static void CleanTemplateZNetView(GameObject obj)
        {
            if (obj == null) return;
            var nview = obj.GetComponent<ZNetView>();
            if (nview == null) return;

            Traverse.Create(nview).Field<bool>("m_ghost").Value = false;
            var zdo = nview.GetZDO();
            if (zdo != null)
            {
                var instances = Traverse.Create(ZNetScene.instance).Field<Dictionary<ZDO, ZNetView>>("m_instances").Value;
                instances?.Remove(zdo);
                ZDOMan.instance?.DestroyZDO(zdo);
                nview.ResetZDO();
            }
        }

        private static MeshFilter FindMainMeshFilter(GameObject source)
        {
            if (source == null) return null;
            MeshFilter best = null;
            float bestVol = -1f;
            var filters = source.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                if (VehicleUtil.IsSnowObject(mf.transform)) continue;
                if (mf.transform.parent != null && VehicleUtil.IsSnowObject(mf.transform.parent)) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr != null && !mr.enabled) continue;
                Vector3 e = mf.sharedMesh.bounds.extents;
                float vol = e.x * e.y * e.z;
                if (vol > bestVol)
                {
                    bestVol = vol;
                    best = mf;
                }
            }
            return best;
        }

        private static GameObject CreateVisualMeshChild(GameObject source, Transform parent, Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            var go = new GameObject("VisualMesh");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;

            var mfSource = FindMainMeshFilter(source);
            MeshRenderer mrSource = mfSource != null ? mfSource.GetComponent<MeshRenderer>() : null;
            if (mfSource == null && source != null)
            {
                // Last resort: first active non-snow renderer.
                foreach (var mf in source.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (mf == null || mf.sharedMesh == null || VehicleUtil.IsSnowObject(mf.transform)) continue;
                    mfSource = mf;
                    mrSource = mf.GetComponent<MeshRenderer>();
                    break;
                }
            }

            if (mfSource != null && mrSource != null && mfSource.sharedMesh != null)
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mfSource.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mrSource.sharedMaterials;
                return go;
            }

            var fallback = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fallback.name = "FallbackMesh";
            fallback.transform.SetParent(go.transform, false);
            fallback.transform.localPosition = Vector3.zero;
            fallback.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            fallback.transform.localScale = Vector3.one;
            var col = fallback.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            if (_woodMaterial != null) VehicleUtil.ApplySharedMaterial(fallback, _woodMaterial);
            return go;
        }

        private static GameObject CreateSolidDeck(Transform parent, Vector3 size)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "SolidDeck";
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = Vector3.zero;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = size;
            var col = cube.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            if (_woodMaterial != null) VehicleUtil.ApplySharedMaterial(cube, _woodMaterial);
            return cube;
        }

        private static GameObject AttachClonedVisual(GameObject source, Transform parent)
        {
            if (source == null) return null;
            bool prevGhost = GetGhostInit();
            GameObject vis;
            try
            {
                SetGhostInit(true);
                vis = UnityEngine.Object.Instantiate(source, parent);
            }
            finally
            {
                SetGhostInit(prevGhost);
            }

            vis.name = "ClonedVisual";
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;
            vis.transform.localScale = Vector3.one;
            vis.SetActive(true);
            StripClonedJunk(vis);
            foreach (var c in vis.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(c);
            }
            foreach (var p in vis.GetComponentsInChildren<Piece>(true))
            {
                UnityEngine.Object.DestroyImmediate(p);
            }
            foreach (var z in vis.GetComponentsInChildren<ZNetView>(true))
            {
                UnityEngine.Object.DestroyImmediate(z);
            }
            foreach (var w in vis.GetComponentsInChildren<WearNTear>(true))
            {
                UnityEngine.Object.DestroyImmediate(w);
            }
            VehicleUtil.StripSnowOverlays(vis);
            foreach (var mr in vis.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr == null) continue;
                if (VehicleUtil.IsSnowObject(mr.transform)) continue;
                mr.enabled = true;
            }
            return vis;
        }

        private static void StripPhysics(GameObject clone)
        {
            foreach (var joint in clone.GetComponentsInChildren<Joint>(true))
            {
                UnityEngine.Object.DestroyImmediate(joint);
            }
            foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.DestroyImmediate(rb);
            }
            foreach (var sync in clone.GetComponentsInChildren<ZSyncTransform>(true))
            {
                UnityEngine.Object.DestroyImmediate(sync);
            }
        }

        private static void StripClonedJunk(GameObject clone)
        {
            if (clone == null) return;
            VehicleUtil.StripSnowOverlays(clone);
            StripPhysics(clone);

            var behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null) continue;
                if (b.gameObject == clone && (b is Piece || b is ZNetView || b is WearNTear || b is VehiclePiece || b is VehicleCore || b is VehicleLift || b is VehicleSeat || b is VehicleEngine || b is VehicleWheel || b is VehicleBearing || b is VehicleSuspension))
                {
                    continue;
                }
                string tn = b.GetType().Name;
                if (tn == "EffectArea" || tn == "CraftingStation" || tn == "Smelter" || tn == "Fireplace"
                    || tn == "CircleProjector" || tn == "WearNTear" || tn == "Piece" || tn == "ZNetView"
                    || tn == "Container" || tn == "Chair" || tn == "Vagon" || tn == "ItemStand"
                    || tn == "WispSpawner" || tn == "Aoe" || tn == "Bed" || tn == "TeleportWorld")
                {
                    UnityEngine.Object.DestroyImmediate(b);
                }
            }

            foreach (var wnt in clone.GetComponentsInChildren<WearNTear>(true))
            {
                if (wnt != null && wnt.gameObject != clone)
                {
                    UnityEngine.Object.DestroyImmediate(wnt);
                }
            }
        }

        private static void ApplyPieceMeta(Piece piece, string label, string desc, Piece.Requirement[] requirements, bool groundOnly)
        {
            piece.m_name = label;
            piece.m_description = desc;
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_resources = requirements.Where(r => r != null).ToArray();
            piece.m_craftingStation = null;
            piece.m_enabled = true;
            piece.m_canBeRemoved = true;
            piece.m_canRotate = true;
            piece.m_groundOnly = groundOnly;
            piece.m_groundPiece = groundOnly;
            piece.m_clipGround = groundOnly;
            piece.m_clipEverything = false;
            piece.m_noInWater = false;
            piece.m_notOnWood = false;
            piece.m_notOnTiltingSurface = false;
            piece.m_notOnFloor = false;
            piece.m_noClipping = false;
            piece.m_allowedInDungeons = true;
            piece.m_allowRotatedOverlap = true;
            piece.m_extraPlacementDistance = groundOnly ? 0 : 6;
            piece.m_comfort = 0;
            if (_templatePiece != null)
            {
                if (piece.m_icon == null) piece.m_icon = _templatePiece.m_icon;
                if (_templatePiece.m_placeEffect != null) piece.m_placeEffect = _templatePiece.m_placeEffect;
            }
        }

        private static void AddWear(GameObject obj, float health)
        {
            VehicleUtil.StripSnowOverlays(obj);
            var wnt = obj.GetComponent<WearNTear>() ?? obj.AddComponent<WearNTear>();
            wnt.m_health = health;
            wnt.m_noRoofWear = true;
            wnt.m_noSupportWear = true;
            wnt.m_supports = true;
            wnt.m_snowDamageImmune = true;
            wnt.m_snow = null;
            wnt.m_snowWorn = null;
            wnt.m_snowBroken = null;
            wnt.m_wet = null;
            wnt.m_materialType = WearNTear.MaterialType.Wood;
        }

        private static void FinishPieceRoot(GameObject obj, bool groundOnly, string label, string desc, Piece.Requirement[] reqs, float health)
        {
            obj.transform.localScale = Vector3.one;
            VehicleUtil.SetLayerRecursive(obj, VehicleUtil.PieceLayer);

            var piece = obj.GetComponent<Piece>() ?? obj.AddComponent<Piece>();
            ApplyPieceMeta(piece, label, desc, reqs, groundOnly);
            AddWear(obj, health);

            var nview = obj.GetComponent<ZNetView>() ?? obj.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_syncInitialScale = false;
            CleanTemplateZNetView(obj);
        }

        #region Prefab Creation

        private static void CreateLiftPrefab(GameObject floorPrefab, GameObject polePrefab)
        {
            var obj = CreateBasePieceObject(floorPrefab, "valhicle_lift", "Vehicle Platform",
                "A wooden floor you build on. Up/Down arrows raise and lower it. [E] freeze or drop physics.",
                new[] { Req("Wood", 10), Req("SurtlingCore", 1) });

            foreach (var tm in obj.GetComponentsInChildren<TerrainModifier>(true))
            {
                UnityEngine.Object.DestroyImmediate(tm);
            }

            if (obj.GetComponent<Collider>() == null)
            {
                var box = obj.AddComponent<BoxCollider>();
                box.size = new Vector3(2.0f, 0.18f, 2.0f);
            }

            var rb = obj.GetComponent<Rigidbody>() ?? obj.AddComponent<Rigidbody>();
            rb.mass = 400f;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var sync = obj.GetComponent<ZSyncTransform>() ?? obj.AddComponent<ZSyncTransform>();
            sync.m_syncPosition = true;
            sync.m_syncRotation = true;
            sync.m_syncScale = false;
            sync.m_syncBodyVelocity = true;

            if (obj.GetComponent<VehicleCore>() == null) obj.AddComponent<VehicleCore>();
            if (obj.GetComponent<VehicleLift>() == null) obj.AddComponent<VehicleLift>();
            if (obj.GetComponent<VehiclePiece>() == null) obj.AddComponent<VehiclePiece>();

            VehicleUtil.AddSnapBox(obj.transform, 1.0f, 0.1f, 1.0f);
            FinishPieceRoot(obj, groundOnly: true, "Vehicle Platform",
                "A wooden floor you build on. Up/Down arrows raise and lower it. [E] freeze or drop physics.",
                new[] { Req("Wood", 10), Req("SurtlingCore", 1) }, 400f);
            var liftPiece = obj.GetComponent<Piece>();
            if (liftPiece != null)
            {
                liftPiece.m_groundPiece = false;
                liftPiece.m_groundOnly = true;
                liftPiece.m_clipGround = false;
                liftPiece.m_noClipping = false;
                liftPiece.m_allowRotatedOverlap = true;
            }
            VehicleUtil.MakePlacedPieceReal(obj);
            RegisterPrefab(obj, isPiece: true);
        }

        private static void CreateChassisPrefab(GameObject floorPrefab)
        {
            var obj = CreateEmptyPieceObject("valhicle_chassis", "Vehicle Chassis Platform",
                "2m x 2m wooden chassis. Structural foundation of a motorized cart.",
                new[] { Req("Wood", 4), Req("BronzeNails", 2) });

            CreateSolidDeck(obj.transform, new Vector3(2.0f, 0.15f, 2.0f));
            var clonedChassis = AttachClonedVisual(floorPrefab, obj.transform);
            if (clonedChassis != null) clonedChassis.name = "ChassisDeck";

            var box = obj.AddComponent<BoxCollider>();
            box.size = new Vector3(2.0f, 0.18f, 2.0f);
            box.center = new Vector3(0f, 0f, 0f);
            box.sharedMaterial = VehicleUtil.SmoothPhysicMaterial;

            var rb = obj.AddComponent<Rigidbody>();
            rb.mass = 400f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.useGravity = true;

            var sync = obj.AddComponent<ZSyncTransform>();
            sync.m_syncPosition = true;
            sync.m_syncRotation = true;
            sync.m_syncScale = false;
            sync.m_syncBodyVelocity = true;

            obj.AddComponent<VehicleCore>();
            obj.AddComponent<VehiclePiece>();

            VehicleUtil.AddSnapBox(obj.transform, 1.0f, 0.1f, 1.0f);

            FinishPieceRoot(obj, groundOnly: false, "Vehicle Chassis Platform",
                "2m x 2m wooden chassis. Structural foundation of a motorized cart.",
                new[] { Req("Wood", 4), Req("BronzeNails", 2) }, 350f);
            RegisterPrefab(obj, isPiece: true);
        }

        private static void CreateWheelPrefab(GameObject wheelSource, WheelSize size, string name, string label, float radius, Piece.Requirement[] reqs)
        {
            var obj = CreateEmptyPieceObject(name, label,
                $"Modular cart wheel. Press [E] to toggle spin direction. Radius: {radius:F2}m", reqs);

            // Solid hub is small so it never props the chassis up off the ground.
            // Trigger sphere matches the visual radius so [E] and snap still hit the wheel.
            float hub = Mathf.Clamp(radius * 0.35f, 0.12f, 0.28f);
            var box = obj.AddComponent<BoxCollider>();
            box.size = new Vector3(hub, hub, hub);
            box.sharedMaterial = VehicleUtil.SmoothPhysicMaterial;
            var hover = obj.AddComponent<SphereCollider>();
            hover.radius = radius;
            hover.isTrigger = true;

            var spin = new GameObject("WheelSpin");
            spin.transform.SetParent(obj.transform, false);
            spin.transform.localPosition = Vector3.zero;
            spin.transform.localRotation = Quaternion.identity;
            spin.transform.localScale = Vector3.one;

            var visual = CreateVisualMeshChild(wheelSource, spin.transform, Vector3.zero, Quaternion.identity, Vector3.one);
            visual.name = "WheelMeshVisual";
            OrientAndScaleWheel(visual, radius);

            var wheel = obj.AddComponent<VehicleWheel>();
            wheel.Size = size;
            wheel.Radius = radius;
            wheel.WheelMeshTransform = spin.transform;
            wheel.AxleAxis = Vector3.right;
            wheel.IsMotorized = true;
            wheel.ApplyWheelSizeProperties();

            obj.AddComponent<VehiclePiece>();
            VehicleUtil.AddSnapPoint(obj.transform, Vector3.zero);
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(-hub, 0f, 0f));
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(hub, 0f, 0f));

            FinishPieceRoot(obj, false, label, $"Modular cart wheel. [E] toggle spin. Radius {radius:F2}m", reqs, 180f);
            RegisterPrefab(obj, isPiece: true);
        }

        private static void OrientAndScaleWheel(GameObject visual, float radius)
        {
            var mf = visual.GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null)
            {
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                visual.transform.localScale = Vector3.one * (radius * 2f);
                return;
            }

            var b = mf.sharedMesh.bounds;
            Vector3 e = b.extents;
            Quaternion rot = Quaternion.identity;
            float meshRadius;
            if (e.y < e.x * 0.45f && e.y < e.z * 0.45f)
            {
                rot = Quaternion.Euler(0f, 0f, 90f);
                meshRadius = Mathf.Max(e.x, e.z);
            }
            else if (e.x <= e.y && e.x <= e.z)
            {
                rot = Quaternion.identity;
                meshRadius = Mathf.Max(e.y, e.z);
            }
            else if (e.z <= e.y && e.z <= e.x)
            {
                rot = Quaternion.Euler(0f, 90f, 0f);
                meshRadius = Mathf.Max(e.y, e.x);
            }
            else
            {
                rot = Quaternion.Euler(0f, 0f, 90f);
                meshRadius = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
            }

            float s = meshRadius > 0.01f ? radius / meshRadius : 1f;
            visual.transform.localRotation = rot;
            visual.transform.localScale = Vector3.one * s;
            // Pivot the mesh so the hub sits on the piece origin / snap point.
            visual.transform.localPosition = rot * Vector3.Scale(-b.center, visual.transform.localScale);
        }

        private static void CreateSuspensionPrefab(GameObject springSource, SuspensionType type, string name, string label, Piece.Requirement[] reqs)
        {
            var obj = CreateEmptyPieceObject(name, label,
                $"Coiled metal suspension strut ({type}). Snaps between chassis and wheel.", reqs);

            float height = type == SuspensionType.Standard ? 0.70f : 0.90f;
            var col = obj.AddComponent<BoxCollider>();
            col.size = new Vector3(0.22f, height, 0.22f);

            var topBracket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            topBracket.name = "TopBracket";
            topBracket.transform.SetParent(obj.transform, false);
            topBracket.transform.localPosition = new Vector3(0f, height * 0.42f, 0f);
            topBracket.transform.localScale = new Vector3(0.18f, 0.04f, 0.18f);
            var colTop = topBracket.GetComponent<Collider>();
            if (colTop != null) UnityEngine.Object.DestroyImmediate(colTop);
            if (_woodMaterial != null) VehicleUtil.ApplySharedMaterial(topBracket, _woodMaterial);

            var botBracket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            botBracket.name = "BottomBracket";
            botBracket.transform.SetParent(obj.transform, false);
            botBracket.transform.localPosition = new Vector3(0f, -height * 0.42f, 0f);
            botBracket.transform.localScale = new Vector3(0.18f, 0.04f, 0.18f);
            var colBot = botBracket.GetComponent<Collider>();
            if (colBot != null) UnityEngine.Object.DestroyImmediate(colBot);
            if (_woodMaterial != null) VehicleUtil.ApplySharedMaterial(botBracket, _woodMaterial);

            float springScale = type == SuspensionType.Standard ? 0.55f : 0.75f;
            var springVisual = CreateVisualMeshChild(springSource, obj.transform, Vector3.zero, Quaternion.identity, new Vector3(springScale, height * 0.7f, springScale));
            springVisual.name = "SpringMeshVisual";

            var susp = obj.AddComponent<VehicleSuspension>();
            susp.Type = type;
            susp.SpringMeshTransform = springVisual.transform;
            susp.OriginalSpringScale = springVisual.transform.localScale;
            susp.ApplySuspensionSettings();

            obj.AddComponent<VehiclePiece>();
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(0f, height * 0.5f, 0f));
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(0f, -height * 0.5f, 0f));

            FinishPieceRoot(obj, false, label, $"Coiled metal suspension strut ({type}).", reqs, 160f);
            RegisterPrefab(obj, isPiece: true);
        }

        private static void CreateButtonBearingPrefab(string name, string label, Piece.Requirement[] reqs)
        {
            var obj = CreateEmptyPieceObject(name, label,
                "Rotatable button swivel. Sits flush on top, bottom, or the side of a chassis. Snap wheels onto the button. [E] cycles Steering / FreeSpin / Motorized.", reqs);

            var col = obj.AddComponent<BoxCollider>();
            col.size = new Vector3(0.44f, 0.12f, 0.44f);

            // Static mount plate (does not spin). Everything else lives on RotatingHead.
            var mount = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mount.name = "MountPlate";
            mount.transform.SetParent(obj.transform, false);
            mount.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            mount.transform.localScale = new Vector3(0.48f, 0.02f, 0.48f);
            var colMount = mount.GetComponent<Collider>();
            if (colMount != null) UnityEngine.Object.DestroyImmediate(colMount);
            if (_woodMaterial != null) VehicleUtil.ApplySharedMaterial(mount, _woodMaterial);

            var head = new GameObject("RotatingHead");
            head.transform.SetParent(obj.transform, false);
            head.transform.localPosition = Vector3.zero;
            head.transform.localScale = Vector3.one;

            var baseRim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseRim.name = "BearingHousing";
            baseRim.transform.SetParent(head.transform, false);
            baseRim.transform.localPosition = Vector3.zero;
            baseRim.transform.localScale = new Vector3(0.44f, 0.05f, 0.44f);
            var colRim = baseRim.GetComponent<Collider>();
            if (colRim != null) UnityEngine.Object.DestroyImmediate(colRim);
            if (_woodMaterial != null) VehicleUtil.ApplySharedMaterial(baseRim, _woodMaterial);

            var centerButton = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            centerButton.name = "CenterButton";
            centerButton.transform.SetParent(head.transform, false);
            centerButton.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            centerButton.transform.localScale = new Vector3(0.36f, 0.06f, 0.36f);
            var colBtn = centerButton.GetComponent<Collider>();
            if (colBtn != null) UnityEngine.Object.DestroyImmediate(colBtn);
            if (_woodMaterial != null) VehicleUtil.ApplySharedMaterial(centerButton, _woodMaterial);

            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "SpinArm";
            arm.transform.SetParent(head.transform, false);
            arm.transform.localPosition = new Vector3(0f, 0.08f, 0.14f);
            arm.transform.localScale = new Vector3(0.06f, 0.04f, 0.28f);
            var colArm = arm.GetComponent<Collider>();
            if (colArm != null) UnityEngine.Object.DestroyImmediate(colArm);

            // Snap points must be DIRECT children of the piece root (Piece.GetSnapPoints).
            // +Y is the button face (wheels snap here); -Y is the mount face.
            VehicleUtil.AddSnapPoint(obj.transform, Vector3.zero);
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(0f, 0.08f, 0f));
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(0f, -0.06f, 0f));
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(0.22f, 0f, 0f));
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(-0.22f, 0f, 0f));
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(0f, 0f, 0.22f));
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(0f, 0f, -0.22f));

            var bearing = obj.AddComponent<VehicleBearing>();
            bearing.RotatingHead = head.transform;
            obj.AddComponent<VehiclePiece>();

            FinishPieceRoot(obj, false, label,
                "Rotatable button swivel. [E] cycles Steering / FreeSpin / Motorized.", reqs, 140f);
            RegisterPrefab(obj, isPiece: true);
        }

        private static void CreateSeatPrefab(GameObject chairPrefab, GameObject wheelSource, string name, string label, Piece.Requirement[] reqs)
        {
            var obj = CreateBasePieceObject(chairPrefab, name, label,
                "Driver station with steering wheel. [E] Mount and drive with W/A/S/D.", reqs);

            obj.transform.localScale = Vector3.one;
            var seat = obj.AddComponent<VehicleSeat>();

            var chair = chairPrefab != null ? chairPrefab.GetComponent<Chair>() : null;
            var mount = new GameObject("MountPoint");
            mount.transform.SetParent(obj.transform, false);
            if (chair != null && chair.m_attachPoint != null)
            {
                mount.transform.localPosition = chair.m_attachPoint.localPosition;
                mount.transform.localRotation = chair.m_attachPoint.localRotation;
            }
            else
            {
                mount.transform.localPosition = new Vector3(0f, 0.45f, 0.05f);
            }
            seat.SeatMountPoint = mount.transform;

            // Wooden column (Unity cylinder is 2m along Y). Tilt toward the driver.
            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            column.name = "SteeringColumn";
            column.transform.SetParent(obj.transform, false);
            column.transform.localPosition = new Vector3(0f, 0.42f, 0.38f);
            column.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
            column.transform.localScale = new Vector3(0.07f, 0.28f, 0.07f);
            var col = column.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            if (_woodMaterial != null) VehicleUtil.ApplySharedMaterial(column, _woodMaterial);

            var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = "ColumnHub";
            hub.transform.SetParent(column.transform, false);
            hub.transform.localPosition = new Vector3(0f, 1.02f, 0f);
            hub.transform.localScale = new Vector3(1.6f, 0.08f, 1.6f);
            var hubCol = hub.GetComponent<Collider>();
            if (hubCol != null) UnityEngine.Object.DestroyImmediate(hubCol);
            if (_woodMaterial != null) VehicleUtil.ApplySharedMaterial(hub, _woodMaterial);

            // Pivot sits at the top of the stick; local Y is the steering axis.
            var steering = new GameObject("SteeringWheelPivot");
            steering.transform.SetParent(column.transform, false);
            steering.transform.localPosition = new Vector3(0f, 1.15f, 0f);
            steering.transform.localRotation = Quaternion.identity;
            seat.SteeringWheelTransform = steering.transform;

            var wheelVis = CreateVisualMeshChild(wheelSource, steering.transform, Vector3.zero, Quaternion.identity, Vector3.one);
            wheelVis.name = "SteeringWheelMesh";
            OrientAndScaleWheel(wheelVis, 0.42f);
            // Axle along the column (pivot local Y).
            wheelVis.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * wheelVis.transform.localRotation;

            obj.AddComponent<VehiclePiece>();
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(0f, 0f, 0f));
            FinishPieceRoot(obj, false, label, "Driver station. [E] Mount and drive with W/A/S/D.", reqs, 200f);
            RegisterPrefab(obj, isPiece: true);
        }

        private static void CreateEnginePrefab(GameObject baseObj, EngineTier tier, string name, string label, float visualScale, Piece.Requirement[] reqs)
        {
            var obj = CreateEmptyPieceObject(name, label,
                $"Coal-burning steam engine ({tier}). Feeds torque to motorized wheels. [E] Add Coal.", reqs);

            var visualRoot = new GameObject("EngineVisual");
            visualRoot.transform.SetParent(obj.transform, false);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localScale = Vector3.one * visualScale;

            if (baseObj != null)
            {
                bool prevGhost = GetGhostInit();
                GameObject vis;
                try
                {
                    SetGhostInit(true);
                    vis = UnityEngine.Object.Instantiate(baseObj, visualRoot.transform);
                }
                finally
                {
                    SetGhostInit(prevGhost);
                }
                vis.name = "EngineMesh";
                vis.transform.localPosition = Vector3.zero;
                vis.transform.localRotation = Quaternion.identity;
                vis.transform.localScale = Vector3.one;
                StripClonedJunk(vis);
                foreach (var c in vis.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(c);
                }
                foreach (var p in vis.GetComponentsInChildren<Piece>(true))
                {
                    UnityEngine.Object.DestroyImmediate(p);
                }
                foreach (var z in vis.GetComponentsInChildren<ZNetView>(true))
                {
                    UnityEngine.Object.DestroyImmediate(z);
                }
                foreach (var w in vis.GetComponentsInChildren<WearNTear>(true))
                {
                    UnityEngine.Object.DestroyImmediate(w);
                }
            }
            else
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(visualRoot.transform, false);
                cube.transform.localScale = new Vector3(1.2f, 1.0f, 1.4f);
                var c = cube.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }

            float size = visualScale * 2.2f;
            var box = obj.AddComponent<BoxCollider>();
            box.size = new Vector3(size, size * 0.9f, size * 1.1f);

            var engine = obj.AddComponent<VehicleEngine>();
            engine.Tier = tier;
            engine.ApplyTierSettings();
            engine.SmokeEffect = obj.GetComponentInChildren<ParticleSystem>();

            var chest = obj.AddComponent<Container>();
            chest.m_name = "Steam Engine";
            chest.m_width = 4;
            chest.m_height = 1;
            chest.m_privacy = Container.PrivacySetting.Public;

            var smokePrefab = FindPrefab("vfx_FireAddSmoke", "vfx_smelter_smoke", "fx_smelter_smoke", "vfx_Smoke");
            if (engine.SmokeEffect == null && smokePrefab != null)
            {
                var smoke = UnityEngine.Object.Instantiate(smokePrefab, obj.transform);
                smoke.name = "EngineSmoke";
                smoke.transform.localPosition = new Vector3(0f, size * 0.6f, 0f);
                engine.SmokeEffect = smoke.GetComponentInChildren<ParticleSystem>();
            }

            obj.AddComponent<VehiclePiece>();
            VehicleUtil.AddSnapPoint(obj.transform, new Vector3(0f, -size * 0.45f, 0f));
            FinishPieceRoot(obj, false, label, $"Coal-burning steam engine ({tier}). [E] Add Coal.", reqs, 280f);
            RegisterPrefab(obj, isPiece: true);
        }

        private static GameObject CreateEmptyPieceObject(string name, string label, string desc, Piece.Requirement[] requirements)
        {
            bool prevGhost = GetGhostInit();
            GameObject clone;
            try
            {
                SetGhostInit(true);
                clone = new GameObject(name);
                clone.transform.SetParent(_prefabRoot.transform, false);
                clone.transform.localScale = Vector3.one;
            }
            finally
            {
                SetGhostInit(prevGhost);
            }

            clone.SetActive(true);
            var piece = clone.AddComponent<Piece>();
            ApplyPieceMeta(piece, label, desc, requirements, false);

            var nview = clone.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_syncInitialScale = false;
            CleanTemplateZNetView(clone);
            return clone;
        }

        private static GameObject CreateBasePieceObject(GameObject source, string name, string label, string desc, Piece.Requirement[] requirements)
        {
            GameObject clone;
            bool prevGhost = GetGhostInit();
            try
            {
                SetGhostInit(true);
                if (source != null)
                {
                    clone = UnityEngine.Object.Instantiate(source, _prefabRoot.transform);
                }
                else
                {
                    clone = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    clone.transform.SetParent(_prefabRoot.transform, false);
                }
            }
            finally
            {
                SetGhostInit(prevGhost);
            }

            clone.name = name;
            clone.SetActive(true);
            clone.transform.localScale = Vector3.one;

            var vagon = clone.GetComponent<Vagon>();
            if (vagon != null) UnityEngine.Object.DestroyImmediate(vagon);
            var container = clone.GetComponent<Container>();
            if (container != null) UnityEngine.Object.DestroyImmediate(container);
            var chair = clone.GetComponent<Chair>();
            if (chair != null) UnityEngine.Object.DestroyImmediate(chair);
            var smelter = clone.GetComponent<Smelter>();
            if (smelter != null) UnityEngine.Object.DestroyImmediate(smelter);
            var stand = clone.GetComponent<ItemStand>();
            if (stand != null) UnityEngine.Object.DestroyImmediate(stand);

            StripClonedJunk(clone);

            var piece = clone.GetComponent<Piece>() ?? clone.AddComponent<Piece>();
            ApplyPieceMeta(piece, label, desc, requirements, false);

            var nview = clone.GetComponent<ZNetView>() ?? clone.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_syncInitialScale = false;
            CleanTemplateZNetView(clone);
            return clone;
        }

        private static Piece.Requirement Req(string itemName, int amount)
        {
            if (ObjectDB.instance == null) return null;

            ItemDrop item = null;
            var prefab = ObjectDB.instance.GetItemPrefab(itemName);
            if (prefab != null)
            {
                item = prefab.GetComponent<ItemDrop>();
            }

            if (item == null)
            {
                for (int i = 0; i < ObjectDB.instance.m_items.Count; i++)
                {
                    var cur = ObjectDB.instance.m_items[i];
                    if (cur != null && cur.name == itemName)
                    {
                        item = cur.GetComponent<ItemDrop>();
                        break;
                    }
                }
            }

            if (item == null) return null;

            return new Piece.Requirement
            {
                m_resItem = item,
                m_amount = amount,
                m_amountPerLevel = 0,
                m_recover = true
            };
        }

        private static void RegisterPrefab(GameObject prefab, bool isPiece = true)
        {
            if (prefab == null) return;
            if (string.IsNullOrEmpty(prefab.name)) return;

            prefab.SetActive(true);
            prefab.transform.localScale = Vector3.one;
            VehicleUtil.SetLayerRecursive(prefab, VehicleUtil.PieceLayer);

            if (ZNetScene.instance != null)
            {
                if (!ZNetScene.instance.m_prefabs.Contains(prefab))
                {
                    ZNetScene.instance.m_prefabs.Add(prefab);
                }

                var dict = Traverse.Create(ZNetScene.instance).Field<Dictionary<int, GameObject>>("m_namedPrefabs").Value;
                if (dict != null)
                {
                    int hash = prefab.name.GetStableHashCode();
                    dict[hash] = prefab;
                }
            }

            if (isPiece && prefab.GetComponent<Piece>() != null)
            {
                if (!_registeredPrefabs.Contains(prefab))
                {
                    _registeredPrefabs.Add(prefab);
                }
            }
        }

        private static void CreateWrenchTool(GameObject hammerPrefab)
        {
            if (hammerPrefab == null || ObjectDB.instance == null)
            {
                Plugin.Log.LogWarning("Cannot create Mechanic's Wrench: hammerPrefab or ObjectDB is null!");
                return;
            }

            bool prevGhost = GetGhostInit();
            try
            {
                SetGhostInit(true);
                _wrenchPrefab = UnityEngine.Object.Instantiate(hammerPrefab, _prefabRoot.transform);
            }
            finally
            {
                SetGhostInit(prevGhost);
            }

            _wrenchPrefab.name = "valhicle_wrench";
            _wrenchPrefab.SetActive(true);
            _wrenchPrefab.transform.localScale = Vector3.one;

            var p = _wrenchPrefab.GetComponent<Piece>();
            if (p != null) UnityEngine.Object.DestroyImmediate(p);

            CleanTemplateZNetView(_wrenchPrefab);

            var itemDrop = _wrenchPrefab.GetComponent<ItemDrop>();
            if (itemDrop != null)
            {
                if (itemDrop.m_itemData.m_shared != null)
                {
                    itemDrop.m_itemData.m_shared = (ItemDrop.ItemData.SharedData)Traverse.Create(itemDrop.m_itemData.m_shared).Method("MemberwiseClone").GetValue();
                }

                itemDrop.m_itemData.m_shared.m_name = "Mechanic's Wrench";
                itemDrop.m_itemData.m_shared.m_description = "Engineering tool for motorized carts.\n• [Right Click]: Vehicle Workshop\n• [Left Click on Cart]: Dock or release on a Lift";
                itemDrop.m_itemData.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Tool;

                var hammerTable = itemDrop.m_itemData.m_shared.m_buildPieces;
                if (hammerTable != null)
                {
                    _vehiclePieceTable = ScriptableObject.Instantiate(hammerTable);
                    _vehiclePieceTable.name = "_ValhiclePieceTable";
                    _vehiclePieceTable.m_pieces = new List<GameObject>(_registeredPrefabs);
                    itemDrop.m_itemData.m_shared.m_buildPieces = _vehiclePieceTable;
                }

                itemDrop.m_itemData.m_dropPrefab = _wrenchPrefab;
            }

            RegisterPrefab(_wrenchPrefab, isPiece: false);

            if (!ObjectDB.instance.m_items.Contains(_wrenchPrefab))
            {
                ObjectDB.instance.m_items.Add(_wrenchPrefab);
            }

            Traverse.Create(ObjectDB.instance).Method("UpdateRegisters").GetValue();
            CreateWrenchRecipe(itemDrop);
        }

        private static void CreateWrenchRecipe(ItemDrop toolItem)
        {
            if (toolItem == null || ObjectDB.instance == null) return;
            if (ObjectDB.instance.m_recipes.Any(r => r != null && r.name == "Recipe_ValhicleWrench")) return;

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_ValhicleWrench";
            recipe.m_item = toolItem;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_minStationLevel = 0;
            recipe.m_craftingStation = null;
            recipe.m_resources = new[]
            {
                Req("Wood", 4),
                Req("Stone", 2)
            }.Where(r => r != null).ToArray();

            ObjectDB.instance.m_recipes.Add(recipe);
            Plugin.Log.LogInfo("Registered Mechanic's Wrench recipe (4 Wood, 2 Stone)!");
        }

        public static void RegisterToHammerTable()
        {
            if (ObjectDB.instance == null) return;

            _registeredPrefabs.RemoveAll(p => p == null || p.GetComponent<Piece>() == null);

            if (_vehiclePieceTable != null)
            {
                _vehiclePieceTable.m_pieces.RemoveAll(p => p == null || p.GetComponent<Piece>() == null);
                foreach (var prefab in _registeredPrefabs)
                {
                    if (!_vehiclePieceTable.m_pieces.Contains(prefab))
                    {
                        _vehiclePieceTable.m_pieces.Add(prefab);
                    }
                }
            }

            var hammerObj = ObjectDB.instance.GetItemPrefab("Hammer");
            if (hammerObj == null) return;

            var itemDrop = hammerObj.GetComponent<ItemDrop>();
            if (itemDrop == null || itemDrop.m_itemData?.m_shared?.m_buildPieces == null) return;

            var table = itemDrop.m_itemData.m_shared.m_buildPieces;
            table.m_pieces.RemoveAll(p => p == null || p.GetComponent<Piece>() == null);
            foreach (var prefab in _registeredPrefabs)
            {
                if (!table.m_pieces.Contains(prefab))
                {
                    table.m_pieces.Add(prefab);
                }
            }
        }

        #endregion
    }
}
