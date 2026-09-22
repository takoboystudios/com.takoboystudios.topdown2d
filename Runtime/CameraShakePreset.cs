using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// How hard something shakes the screen, in whole pixels.
    ///
    /// Pixels, because the game is 256 x 144 and the camera sits on the pixel grid: an offset of
    /// less than a pixel rounds away to nothing. A preset asking for "0.3" used to ask for a third
    /// of a pixel, which the screen could not show, so most of the game's shakes did nothing at all
    /// (T-447).
    ///
    /// 1 is a nudge, 2 a solid hit, 4 an explosion. Above about 6 the frame is hard to read.
    /// </summary>
    [CreateAssetMenu(fileName = "CameraShake", menuName = "Camera/Shake Preset")]
    public class CameraShakePreset : ScriptableObject
    {
        [BoxGroup("Shake")]
        [Tooltip("How far the picture jumps, in whole pixels. 1 nudge, 2 hit, 4 explosion. Under 1 is nothing at all.")]
        [SerializeField, Range(0f, 8f)]
        float pixels = 2f;

        /// <summary>The shake, in whole pixels.</summary>
        public float Pixels => pixels;

        [Button("Preview Shake"), BoxGroup("Debug")]
        void PreviewShake()
        {
            // Nothing wired means nothing shakes, which is the right answer in a scene with no
            // camera controller rather than a warning the previewer can do nothing about.
            ScreenShake.Request(this);
        }

        /// <summary>A preset made in code, for a caller with no asset to point at.</summary>
        public static CameraShakePreset CreatePreset(float pixels)
        {
            CameraShakePreset preset = CreateInstance<CameraShakePreset>();
            preset.pixels = pixels;
            return preset;
        }
    }
}
