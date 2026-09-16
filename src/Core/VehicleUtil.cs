using UnityEngine;
using Valhicle.Components;

namespace Valhicle.Core
{
    internal static class VehicleUtil
    {
        public const string ParentZdoKey = "Valhicle_Parent";
        public const string BearingZdoKey = "Valhicle_Bearing";
        public const string LocalPosZdoKey = "Valhicle_LocalPos";
        public const string LocalRotZdoKey = "Valhicle_LocalRot";
        public const string DockedZdoKey = "Valhicle_Docked";
        public const string HasStarterZdoKey = "Valhicle_HasStarter";

        public static int PieceLayer
        {
            get
            {
                int layer = LayerMask.NameToLayer("piece");
                return layer >= 0 ? layer : 0;
            }
        }

        public static int GhostLayer => LayerMask.NameToLayer("ghost");

        private static PhysicsMaterial _smoothPhysicMaterial;
        public static PhysicsMaterial SmoothPhysicMaterial
        {
            get
            {
                if (_smoothPhysicMaterial == null)
                {
                    _smoothPhysicMaterial = new PhysicsMaterial("Valhicle_Smooth")
                    {
                        dynamicFriction = 0.02f,
                        staticFriction = 0.02f,
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                        bounciness = 0f,
                        bounceCombine = PhysicsMaterialCombine.Minimum
                    };
                }
                return _smoothPhysicMaterial;
            }
        }

        public static bool IsPlacementGhost(GameObject go)
        {
            if (go == null) return true;
            if (ZNetView.m_forceDisableInit) return true;
            int ghost = GhostLayer;
            if (ghost >= 0 && go.layer == ghost) return true;
            var nv = go.GetComponent<ZNetView>();
            if (nv != null)
            {
                try
                {
                    if (TraverseGhost(nv)) return true;
                }
                catch
                {
                    // ignored
                }
            }
            return false;
        }

        private static bool TraverseGhost(ZNetView nv)
        {
            return HarmonyLib.Traverse.Create(nv).Field<bool>("m_ghost").Value;
        }

        public static bool IsSnowObject(Transform t)
        {
            if (t == null) return false;
            string n = t.name;
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("snow", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("wet", System.StringComparison.OrdinalIgnoreCase) >= 0 && n.IndexOf("snow", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void StripSnowOverlays(GameObject go)
        {
            if (go == null) return;
            var transforms = go.GetComponentsInChildren<Transform>(true);
            for (int i = transforms.Length - 1; i >= 0; i--)
            {
                var t = transforms[i];
                if (t == null || t.gameObject == go) continue;
                if (IsSnowObject(t))
                {
                    Object.DestroyImmediate(t.gameObject);
                }
            }
        }

        public static string PrefabName(GameObject go)
        {
            if (go == null) return string.Empty;
            string n = go.name;
            int clone = n.IndexOf("(Clone)");
            if (clone >= 0) n = n.Substring(0, clone);
            return n.Trim();
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null || layer < 0) return;
            go.layer = layer;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
            }
        }

        public static void MakePlacedPieceReal(GameObject go)
        {
            if (go == null) return;
            SetLayerRecursive(go, PieceLayer);

            var nv = go.GetComponent<ZNetView>();
            if (nv != null)
            {
                try
                {
                    HarmonyLib.Traverse.Create(nv).Field<bool>("m_ghost").Value = false;
                }
                catch { /* ignored */ }
            }

            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                if (IsSnowObject(renderers[i].transform)) continue;
                renderers[i].enabled = true;
            }
        }

        public static void AddSnapPoint(Transform parent, Vector3 localPos)
        {
            if (parent == null) return;
            var go = new GameObject("_snappoint");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            try
            {
                go.tag = "snappoint";
            }
            catch
            {
                // Tag is registered in Valheim; ignore if a tool host lacks it.
            }
        }

        public static void AddSnapGrid(Transform parent, float halfExtent, float y)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    AddSnapPoint(parent, new Vector3(x * halfExtent, y, z * halfExtent));
                }
            }
        }

        public static void AddSnapBox(Transform parent, float halfX, float halfY, float halfZ)
        {
            AddSnapGrid(parent, halfX, halfY);
            AddSnapGrid(parent, halfX, -halfY);
            for (int z = -1; z <= 1; z++)
            {
                AddSnapPoint(parent, new Vector3(-halfX, 0f, z * halfZ));
                AddSnapPoint(parent, new Vector3(halfX, 0f, z * halfZ));
            }
            for (int x = -1; x <= 1; x++)
            {
                AddSnapPoint(parent, new Vector3(x * halfX, 0f, -halfZ));
                AddSnapPoint(parent, new Vector3(x * halfX, 0f, halfZ));
            }
        }

        /// <summary>
        /// Parent without shearing. Non-uniform parent scale + a rotated child is what
        /// stretched bearings on walls. Prefer a uniformly scaled ancestor (the chassis).
        /// </summary>
        public static void ParentKeepWorld(Transform t, Transform parent, bool forceParent = false)
        {
            if (t == null || parent == null) return;
            Vector3 pos = t.position;
            Quaternion rot = t.rotation;
            Transform target = parent;
            if (!forceParent && !IsUniformScale(parent.lossyScale))
            {
                var core = parent.GetComponentInParent<VehicleCore>();
                if (core != null) target = core.transform;
            }
            t.SetParent(target, true);
            t.position = pos;
            t.rotation = rot;
            t.localScale = Vector3.one;
        }

        public static bool IsUniformScale(Vector3 s)
        {
            return Mathf.Abs(s.x - s.y) < 0.02f && Mathf.Abs(s.y - s.z) < 0.02f;
        }

        public static void ForceWorldScaleOne(Transform t)
        {
            if (t == null) return;
            t.localScale = Vector3.one;
        }

        public static void ApplySharedMaterial(GameObject target, Material material)
        {
            if (target == null || material == null) return;
            var renderers = target.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = material;
            }
        }

        public static Material GetSharedMaterial(GameObject source)
        {
            if (source == null) return null;
            var mr = source.GetComponentInChildren<MeshRenderer>(true);
            return mr != null ? mr.sharedMaterial : null;
        }

        public static VehicleCore FindVehicleNear(Vector3 pos, float radius, VehicleCore exclude = null)
        {
            var cols = Physics.OverlapSphere(pos, radius);
            VehicleCore best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                var core = cols[i].GetComponentInParent<VehicleCore>();
                if (core == null || core == exclude) continue;
                float d = Vector3.SqrMagnitude(core.transform.position - pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = core;
                }
            }
            return best;
        }

        public static VehicleBearing FindBearingNear(Vector3 pos, float radius, VehicleBearing exclude = null)
        {
            var cols = Physics.OverlapSphere(pos, radius);
            VehicleBearing best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                var bearing = cols[i].GetComponentInParent<VehicleBearing>();
                if (bearing == null || bearing == exclude) continue;
                float d = Vector3.SqrMagnitude(bearing.transform.position - pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = bearing;
                }
            }
            return best;
        }

        public static ZDOID GetUid(Component c)
        {
            if (c == null) return ZDOID.None;
            var nv = c.GetComponent<ZNetView>() ?? c.GetComponentInParent<ZNetView>();
            if (nv == null || !nv.IsValid()) return ZDOID.None;
            return nv.GetZDO().m_uid;
        }

        public static GameObject FindByUid(ZDOID id)
        {
            if (id == ZDOID.None || ZNetScene.instance == null) return null;
            return ZNetScene.instance.FindInstance(id);
        }

        public static bool BelongsToVehicle(Collider col, VehicleCore vehicle)
        {
            if (col == null || vehicle == null) return false;
            return col.transform == vehicle.transform || col.transform.IsChildOf(vehicle.transform);
        }

        public static void IgnoreCollisions(Component a, Component b, bool ignore)
        {
            if (a == null || b == null) return;
            var ca = a.GetComponentsInChildren<Collider>(true);
            var cb = b.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < ca.Length; i++)
            {
                if (ca[i] == null) continue;
                for (int j = 0; j < cb.Length; j++)
                {
                    if (cb[j] == null) continue;
                    Physics.IgnoreCollision(ca[i], cb[j], ignore);
                }
            }
        }
    }
}
