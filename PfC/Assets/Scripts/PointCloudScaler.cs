using UnityEngine;

namespace Cwipc
{
    /// <summary>
    /// Simple utility to scale the point cloud renderer
    /// Attach this to the same GameObject as your NetworkPointCloudReader
    /// </summary>
    public class PointCloudScaler : MonoBehaviour
    {
        [Tooltip("Scale multiplier for the point cloud (default 1.0)")]
        public float scaleFactor = 2.0f;

        void Start()
        {
            // Apply the scale
            transform.localScale = Vector3.one * scaleFactor;
            Debug.Log($"PointCloudScaler: Scaled point cloud to {scaleFactor}x");
        }

        // Allow runtime adjustment in inspector
        void OnValidate()
        {
            if (Application.isPlaying)
            {
                transform.localScale = Vector3.one * scaleFactor;
            }
        }
    }
}
