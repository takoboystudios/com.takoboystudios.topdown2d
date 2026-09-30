using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Stops the world while one thing keeps moving: the Brand's freeze (T-200), where everything on
    /// screen, the other player included, holds still while Grim plays and the notes rain.
    ///
    /// **It stops time itself, not just the entities.** The owner's suggestion was to stop the delta
    /// time the entities are fed and keep feeding the player. Doing it with <c>Time.timeScale</c>
    /// does exactly that, and also stops everything that is not an entity and reads the clock on its
    /// own: a wave waiting to spawn, the chain gauge draining, the camera, every sprite animation,
    /// every UniTask delay. Stopping only the entities would have left each of those running through
    /// the freeze, and a wave that spawned mid-special would be standing in the middle of it.
    ///
    /// What keeps moving opts out with <see cref="Entity.SetRunsWhileFrozen"/>: it is fed unscaled
    /// time and its sprite animations play unscaled. Physics stops outright (FixedUpdate does not run
    /// at a time scale of zero), so anything that has to move during a freeze moves itself in its tick,
    /// the way <see cref="FallEffect"/> and <see cref="DriftEffect"/> do.
    ///
    /// One freeze for the whole game, so a static: it is what the process is doing, not what a player
    /// knows. Nested, so two freezes overlapping end when the last one does.
    /// </summary>
    public static class WorldFreeze
    {
        static int _depth;
        static float _scaleBefore = 1f;

        /// <summary>True while the world is stopped.</summary>
        public static bool IsFrozen => _depth > 0;

        /// <summary>Stops the world. Pair every call with one <see cref="End"/>.</summary>
        public static void Begin()
        {
            if (_depth == 0)
            {
                _scaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            _depth++;
        }

        /// <summary>Lets the world go again once every <see cref="Begin"/> has been ended.</summary>
        public static void End()
        {
            if (_depth == 0)
                return;

            _depth--;
            if (_depth == 0)
                Time.timeScale = _scaleBefore;
        }

        /// <summary>
        /// Puts time back no matter how many freezes are open. For a scene ending in the middle of one,
        /// so the next scene never starts stopped.
        /// </summary>
        public static void Clear()
        {
            if (_depth > 0)
                Time.timeScale = _scaleBefore;
            _depth = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            _depth = 0;
            _scaleBefore = 1f;
        }
    }
}
