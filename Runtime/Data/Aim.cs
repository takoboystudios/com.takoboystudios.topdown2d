using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Aiming, snapped to eight directions.
    ///
    /// This is a design pillar, not a limitation to work around. Aim locks to the nearest of eight
    /// angles however precisely it is pushed, which moves the skill from pointing accurately to
    /// standing in the right place. Movement stays fully free; only the shot direction snaps.
    ///
    /// The interesting part is the hysteresis. Naively snapping to the nearest octant every frame
    /// makes a stick held near a boundary flicker between two directions several times a second,
    /// which reads as the gun stuttering rather than the player being imprecise. So the current
    /// direction gets to keep the aim until the input has moved a clear margin past the boundary. The
    /// cost is that a deliberate turn lands a few degrees late, which nobody can perceive; the gain
    /// is that a held direction is rock solid, which everybody can.
    /// </summary>
    public static class Aim
    {
        /// <summary>The eight legal aim directions, starting east and going anticlockwise.</summary>
        public static readonly Vector2[] Directions =
        {
            new Vector2(1f, 0f),
            new Vector2(1f, 1f).normalized,
            new Vector2(0f, 1f),
            new Vector2(-1f, 1f).normalized,
            new Vector2(-1f, 0f),
            new Vector2(-1f, -1f).normalized,
            new Vector2(0f, -1f),
            new Vector2(1f, -1f).normalized,
        };

        /// <summary>Half the angle between two neighbouring directions: the plain snapping boundary.</summary>
        public const float HalfSector = 22.5f;

        /// <summary>
        /// How far past the boundary the input must travel before the aim gives up the direction it is
        /// already holding. Roughly a third of a sector: enough to kill boundary flicker, small enough
        /// that a deliberate turn still feels immediate.
        /// </summary>
        public const float DefaultStickiness = 7f;

        /// <summary>Below this the input is treated as no aim at all, so stick drift cannot fire.</summary>
        public const float DeadZone = 0.35f;

        /// <summary>True when an input is a real aim rather than noise or a resting thumb.</summary>
        public static bool IsAiming(Vector2 input) => input.sqrMagnitude >= DeadZone * DeadZone;

        /// <summary>Snaps to the nearest of the eight directions. Zero in, zero out.</summary>
        public static Vector2 Snap(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f)
                return Vector2.zero;

            float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
            int sector = Mathf.RoundToInt(angle / 45f);
            return Directions[((sector % 8) + 8) % 8];
        }

        /// <summary>
        /// Snaps with hysteresis: keeps <paramref name="current"/> until the input is clearly past the
        /// boundary between it and its neighbour. Pass the previous frame's result back in as
        /// <paramref name="current"/>, or zero when the player was not aiming.
        /// </summary>
        public static Vector2 Snap(Vector2 input, Vector2 current, float stickiness = DefaultStickiness)
        {
            if (input.sqrMagnitude < 0.0001f)
                return Vector2.zero;

            // Nothing to hold on to, so this is the first frame of an aim.
            if (current.sqrMagnitude < 0.0001f)
                return Snap(input);

            // Still comfortably inside the sector being held? Then keep holding it.
            if (Vector2.Angle(input, current) <= HalfSector + stickiness)
                return current;

            return Snap(input);
        }
    }
}
