using System.Collections.Generic;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    public static class ExtensionMethods
    {
        public static readonly Dictionary<Vector2, Direction> Vector2ToDirection = new Dictionary<
            Vector2,
            Direction
        >
        {
            { Vector2.zero, Direction.Unknown },
            { Vector2.up, Direction.North },
            { Vector2.down, Direction.South },
            { Vector2.right, Direction.East },
            { Vector2.left, Direction.West },
            { new Vector2(1, 1), Direction.North_East },
            { new Vector2(1, -1), Direction.North_West },
            { new Vector2(-1, 1), Direction.South_East },
            { new Vector2(-1, -1), Direction.South_West },
        };

        public static readonly Dictionary<Direction, string> DirectionToAnimId = new Dictionary<
            Direction,
            string
        >
        {
            { Direction.Unknown, "unknown" },
            { Direction.North, AnimConst.North },
            { Direction.South, AnimConst.South },
            { Direction.East, AnimConst.East },
            { Direction.West, AnimConst.West },
            { Direction.North_East, AnimConst.NorthEast },
            { Direction.North_West, AnimConst.NorthWest },
            { Direction.South_East, AnimConst.SouthEast },
            { Direction.South_West, AnimConst.SouthWest },
        };

        public static Direction ToDirection(this Vector2 v2) =>
            Vector2ToDirection.GetValueOrDefault(v2, Direction.South);

        public static string ToAnimId(this Direction v2) => DirectionToAnimId.GetValueOrDefault(v2);
    }
}
