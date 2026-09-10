using System;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Screen shake, asked for rather than performed.
    ///
    /// A gun firing knows it wants a shake; it must not know what a camera is (T-409). The entity
    /// layer asks here and the game wires <see cref="Handler"/> to whatever it actually owns, which
    /// in Hell Wilds is <c>CameraController.Shake</c>. Nothing is wired means nothing shakes, which
    /// is the right answer for a headless test or a scene with no camera controller.
    /// </summary>
    public static class ScreenShake
    {
        /// <summary>What the game does about a shake request. Left null, requests are dropped.</summary>
        public static Action<CameraShakePreset> Handler;

        public static void Request(CameraShakePreset preset)
        {
            if (preset != null)
                Handler?.Invoke(preset);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Clear() => Handler = null;
    }

    /// <summary>
    /// The rules the entity layer needs but does not own, supplied by whichever game is using it
    /// (T-409).
    ///
    /// Each one has a default that means "no opinion", so the entity layer works on its own and a
    /// game only fills in what it actually has. That matters for the tests, which build bare
    /// entities with no game running underneath them.
    /// </summary>
    public static class CombatRules
    {
        /// <summary>
        /// Whether an attack's element beats a target's, which doubles the hit.
        ///
        /// The <see cref="Element"/> list stays in the entity layer because it is serialized onto
        /// prefabs and stat assets, and changing its type would break every one of them. The chart is
        /// the part that is genuinely per-game, so that is what moves out. Unset means no element
        /// ever beats another.
        /// </summary>
        public static Func<Element, Element, bool> WeaknessRule;

        /// <summary>
        /// Extra layers a contained body must collide with, on top of its own mask.
        ///
        /// Hell Wilds uses this for the temporary walls an encounter raises around the visible frame.
        /// Unset means nothing extra, which is what a game with no such concept wants.
        /// </summary>
        public static Func<int> ExtraContainmentMask;

        /// <summary>
        /// Statuses a heavy hit removes. Hell Wilds breaks a freeze with one; another game may break
        /// nothing. Unset means a heavy hit removes nothing, which is the right default.
        /// </summary>
        public static System.Action<Status.StatusHolder> HeavyHitBreaks;

        public static void BreakOnHeavyHit(Status.StatusHolder holder)
        {
            if (holder != null)
                HeavyHitBreaks?.Invoke(holder);
        }

        public static bool IsWeakTo(Element attack, Element target) =>
            WeaknessRule != null && WeaknessRule(attack, target);

        public static int ContainmentMask() => ExtraContainmentMask != null ? ExtraContainmentMask() : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Clear()
        {
            WeaknessRule = null;
            ExtraContainmentMask = null;
            HeavyHitBreaks = null;
        }
    }
}
