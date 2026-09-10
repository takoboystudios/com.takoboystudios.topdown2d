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

        public const string North = "n";
        public const string South = "s";
        public const string East = "e";
        public const string West = "w";

        public const string NorthEast = "ne";
        public const string SouthEast = "se";
        public const string SouthWest = "sw";
        public const string NorthWest = "nw";
    }

    public enum Size
    {
        Off,
        Small,
        Medium,
        Large,
        XLarge,
        XXLarge,
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
