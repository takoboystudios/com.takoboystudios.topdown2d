using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A play-once effect that also travels: it drifts out along a heading from where it was spawned,
    /// slowing to a stop as its animation plays. The music notes that burst off Grim's strum.
    ///
    /// Why not <see cref="DebrisPiece"/>: debris is a physics body that is tossed, lands, bounces off
    /// walls and blinks out, which is right for a splinter and wrong for a note. A note is light, it
    /// has its own animation (it pops, flashes and fades), and it is gone before it could ever land.
    /// So this stays an <see cref="Effect"/>: pooled, one animation, released when it finishes, and it
    /// simply moves while it plays.
    ///
    /// **It floats up the body, through hover, never through y.** A note leaves the body, not the
    /// floor, so its sprite is lifted by <see cref="startHeight"/> on the visual child while its
    /// position, which is what it sorts by, stays on the ground under it. Faking the lift by nudging y
    /// would sort it behind Grim on the frame it appears (CLAUDE.md, sorting rule two).
    ///
    /// Cosmetic only. It carries no damage box and ignores walls: it lives about a third of a second
    /// and travels a tile or so.
    /// </summary>
    public class DriftEffect : Effect
    {
        [BoxGroup("Drift")]
        [Tooltip("Slowest launch speed, in pixels per second. Each note picks a speed between this and the one below, so a burst spreads unevenly.")]
        [SerializeField, MinValue(0f)]
        float minSpeed = 120f;

        [BoxGroup("Drift")]
        [Tooltip("Fastest launch speed, in pixels per second. With a 0.3 second drift, 170 carries a note about 25 pixels, the edge of the guitar's shockwave.")]
        [SerializeField, MinValue(0f)]
        float maxSpeed = 170f;

        [BoxGroup("Drift")]
        [Tooltip("Seconds to slow from the launch speed to a stop. The distance travelled is about half of speed times this. Around the length of the animation, 0.3.")]
        [SerializeField, MinValue(0.01f)]
        float driftTime = 0.3f;

        [BoxGroup("Drift")]
        [Tooltip("Height the sprite is drawn above its position when it appears, in pixels. Spawned at Grim's position, which is already 8 above his feet, so 8 starts the notes about level with his shoulders, out of him rather than the floor.")]
        [SerializeField, MinValue(0f)]
        float startHeight = 8f;

        [BoxGroup("Drift")]
        [Tooltip("Extra height gained over the drift, in pixels, so a note floats up a little as it goes. 0 keeps it level.")]
        [SerializeField]
        float rise = 4f;

        Vector2 _direction;
        float _speed;
        float _age;
        bool _launched;

        /// <summary>
        /// Sends the note off along <paramref name="direction"/> at a random speed in the Inspector's
        /// range. Call right after acquiring it; until then it sits where it was spawned.
        /// </summary>
        public void Launch(Vector2 direction)
        {
            _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
            _speed = Random.Range(minSpeed, maxSpeed);
            _age = 0f;
            _launched = true;
            SetHeight(startHeight);
        }

        public override void OnAcquired()
        {
            base.OnAcquired();
            _launched = false;
            _age = 0f;
            SetHeight(startHeight);
        }

        protected override bool Tick(float deltaTime)
        {
            if (!base.Tick(deltaTime))
                return false;

            // The base releases the effect when its animation ends; nothing left to move.
            if (!_launched || !gameObject.activeInHierarchy)
                return true;

            _age += deltaTime;
            float t = Mathf.Clamp01(_age / driftTime);

            // Linear slow-down: full speed at launch, a stop at driftTime. Distance is half of
            // speed times driftTime, which is what the tooltips promise.
            float speed = _speed * (1f - t);
            if (speed > 0f)
                Position += _direction * (speed * deltaTime);

            SetHeight(startHeight + rise * t);
            return true;
        }

        void SetHeight(float height)
        {
            if (character == null)
                return;
            Vector3 local = character.localPosition;
            local.y = height;
            character.localPosition = local;
        }
    }
}
