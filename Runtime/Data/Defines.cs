using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    public class AnimConst
    {
        public const string Idle = "idle";
        public const string Move = "run";
        public const string JumpAntic = "jump-antic";
        public const string JumpLoop = "jump-loop";
        public const string JumpPeak = "jump-peak";
        public const string Fall = "fall";
        public const string Hit = "hit";
        public const string Roll = "roll";
        public const string Stunned = "stunned";
        public const string Frozen = "frozen";
        public const string Poisoned = "poisoned";
        public const string Spawn = "spawn";
        public const string Attack = "attack";
        public const string TeleportIn = "teleport-in";
        public const string TeleportOut = "teleport-out";
        public const string Shoot = "shoot";
        public const string Death = "death";
        public const string Ghost = "ghost";
        public const string Revive = "revive";
        public const string Resurrection = "resurrection";

        public const string North = "n";
        public const string South = "s";
        public const string East = "e";
        public const string West = "w";

        public const string NorthEast = "ne";
        public const string SouthEast = "se";
        public const string SouthWest = "sw";
        public const string NorthWest = "nw";
    }

    /// <summary>
    /// How wide a ground shadow is, in pixels at ground level. Each value is its own width, so the
    /// number in the Inspector is the number on screen, and each names an animation in the shared
    /// shadow asset (see <see cref="Shadows"/>). Values are explicit because they are serialized.
    /// </summary>
    public enum ShadowSize
    {
        Off = 0,
        [InspectorName("4px")] Px4 = 4,
        [InspectorName("6px")] Px6 = 6,
        [InspectorName("8px")] Px8 = 8,
        [InspectorName("12px")] Px12 = 12,
        [InspectorName("16px")] Px16 = 16,
        [InspectorName("20px")] Px20 = 20,
        [InspectorName("24px")] Px24 = 24,
    }

    public enum Element
    {
        None,
        Steel,
        Electric,
        Fire,
        Frost,
        Nature,
        Chaos,
    }

    public enum Direction
    {
        Unknown,

        North,
        South,
        East,
        West,

        North_East,
        North_West,
        South_East,
        South_West,
    }
}
