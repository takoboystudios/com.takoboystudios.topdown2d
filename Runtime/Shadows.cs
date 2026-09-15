using TakoBoyStudios.Animation;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Ground shadows that shrink as whatever casts them rises.
    ///
    /// **One asset, one animation per size.** The shared shadow asset holds `shadow-16px`,
    /// `shadow-24px` and so on. Frame 0 of each is the shadow of something on the floor, and each
    /// frame after it is smaller, down to the last, which is the shadow of something at
    /// <see cref="FullHeight"/> or above.
    ///
    /// **The animation is never played.** The frame is picked from the height and set directly, so
    /// a shadow is exactly as big as the height says and never mid-way through a timed sequence.
    ///
    /// **Allocates nothing.** Names are string literals, and a frame is only set when it changes.
    /// </summary>
    public static class Shadows
    {
        /// <summary>
        /// The height, in pixels, at which a shadow is at its smallest. Grim's jump peak (the owner
        /// set it to 32), so the player's own jump runs the whole range and anything flying higher
        /// simply stays small. Change it with the jump.
        /// </summary>
        public const float FullHeight = 32f;

        /// <summary>The animation for a size, or null for Off.</summary>
        public static string AnimationName(ShadowSize size)
        {
            switch (size)
            {
                case ShadowSize.Px4: return "shadow-4px";
                case ShadowSize.Px6: return "shadow-6px";
                case ShadowSize.Px8: return "shadow-8px";
                case ShadowSize.Px12: return "shadow-12px";
                case ShadowSize.Px16: return "shadow-16px";
                case ShadowSize.Px20: return "shadow-20px";
                case ShadowSize.Px24: return "shadow-24px";
                default: return null;
            }
        }

        /// <summary>Which of a size's frames a height shows: 0 on the floor, the last at <see cref="FullHeight"/>.</summary>
        public static int FrameFor(float height, int frameCount)
        {
            if (frameCount <= 1)
                return 0;

            float t = Mathf.Clamp01(height / FullHeight);
            return Mathf.Clamp(Mathf.RoundToInt(t * (frameCount - 1)), 0, frameCount - 1);
        }

        /// <summary>
        /// Shows the frame for a height and returns it, or -1 when there is nothing to show. Pass back
        /// what it returned last time; the sprite is only touched when the answer changed or something
        /// else (an animator starting itself) put a different frame up.
        /// </summary>
        public static int Show(SpriteAnimation shadow, ShadowSize size, float height, int shownFrame)
        {
            if (shadow == null)
                return -1;

            string name = AnimationName(size);
            if (name == null)
                return -1;

            SpriteAnimationData data = shadow.GetAnimationData(name);
            if (data == null)
                return -1;

            int frame = FrameFor(height, data.frameDatas.Count);

            if (frame != shownFrame || shadow.CurrentFrame != frame || shadow.CurrentAnimationName != name)
                shadow.SetFrame(name, frame);

            return frame;
        }
    }
}
