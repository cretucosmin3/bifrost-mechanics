using UnityEngine;

namespace Valhicle.Components
{
    public enum SuspensionType
    {
        Standard = 0,  // Flexible, good for bumpy offroad
        HeavyDuty = 1  // Stiff, high load capacity for large cargo/engines
    }

    /// <summary>
    /// Suspension module providing structural damping and a visual dynamic coiled spring linkage.
    /// Animates compression and expansion of the actual 3D helical metal spring.
    /// </summary>
    public class VehicleSuspension : MonoBehaviour
    {
        public SuspensionType Type = SuspensionType.Standard;
        public float RestLength = 0.6f;
        public float MaxCompression = 0.3f;
        public float MaxExtension = 0.2f;

        [Header("Spring Visuals")]
        public Transform SpringMeshTransform;
        public Vector3 OriginalSpringScale = Vector3.one;

        [Header("Linkage Visuals")]
        public Transform UpperPistonTransform;
        public Transform LowerPistonTransform;

        public float CurrentLength { get; private set; }

        private void Awake()
        {
            RestLength = (Type == SuspensionType.Standard) ? 0.6f : 0.75f;
            CurrentLength = RestLength;
            ApplySuspensionSettings();

            if (SpringMeshTransform != null)
            {
                OriginalSpringScale = SpringMeshTransform.localScale;
            }
        }

        public void ApplySuspensionSettings()
        {
            if (Type == SuspensionType.Standard)
            {
                RestLength = 0.6f;
                MaxCompression = 0.3f;
                MaxExtension = 0.2f;
            }
            else // HeavyDuty
            {
                RestLength = 0.75f;
                MaxCompression = 0.4f;
                MaxExtension = 0.25f;
            }
        }

        /// <summary>
        /// Updates the visual dynamic coiled spring and piston linkage based on wheel compression.
        /// Stretches or compresses the spring along its Y axis in real-time.
        /// </summary>
        public void UpdateVisuals(float compressionRatio)
        {
            float targetLength = RestLength - (compressionRatio * MaxCompression);
            CurrentLength = Mathf.Clamp(targetLength, RestLength - MaxCompression, RestLength + MaxExtension);

            float scaleFactor = Mathf.Clamp(CurrentLength / RestLength, 0.35f, 1.45f);

            if (SpringMeshTransform != null)
            {
                SpringMeshTransform.localScale = new Vector3(OriginalSpringScale.x, OriginalSpringScale.y * scaleFactor, OriginalSpringScale.z);
            }

            if (UpperPistonTransform != null)
            {
                UpperPistonTransform.localScale = new Vector3(1f, scaleFactor, 1f);
            }
        }
    }
}
