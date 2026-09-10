using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    public enum ShakeIntensity
    {
        Light,
        Medium,
        Heavy,
    }

    [CreateAssetMenu(fileName = "CameraShake", menuName = "Camera/Shake Preset")]
    public class CameraShakePreset : ScriptableObject
    {
        [BoxGroup("Shake Settings")]
        [SerializeField]
        ShakeIntensity intensity = ShakeIntensity.Medium;

        [BoxGroup("Shake Settings")]
        [SerializeField, Range(0.1f, 5f)]
        public float force = 1f;

        // [BoxGroup("Shake Settings")]
        // [SerializeField, Range(0.1f, 2f)]
        // float duration = 0.3f;

        // [BoxGroup("Advanced")]
        // [SerializeField]
        // AnimationCurve falloff = AnimationCurve.EaseInOut(0, 1, 1, 0);

        void OnValidate()
        {
            // Auto-set force based on intensity
            force = intensity switch
            {
                ShakeIntensity.Light => 0.3f,
                ShakeIntensity.Medium => 0.7f,
                ShakeIntensity.Heavy => 1.5f,
                _ => force,
            };
        }

        [Button("Preview Shake"), BoxGroup("Debug")]
        void PreviewShake()
        {
#if UNITY_EDITOR
            // Nothing wired means nothing shakes, which is the right answer in a scene with no
            // camera controller rather than a warning the previewer can do nothing about.
            ScreenShake.Request(this);
#endif
        }

        public static CameraShakePreset CreatePreset(ShakeIntensity intensity)
        {
            var preset = CreateInstance<CameraShakePreset>();
            preset.intensity = intensity;
            preset.OnValidate();
            return preset;
        }
    }
}
