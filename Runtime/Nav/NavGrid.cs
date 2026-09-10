using System.Collections.Generic;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Where an enemy can stand, as a grid, derived from the same rectangles the room's colliders come
    /// from.
    ///
    /// Enemies have had no idea what the room is shaped like. They steer straight at the player and
    /// let the motor stop them, which reads as intent right up until a wall is in the way and then
    /// reads as an animal walking into glass. That works in an open arena and stops working the moment
    /// a room has a pillar in it, which every room now does.
    ///
    /// Cells are finer than tiles on purpose. A tile-sized grid makes a two-tile gap look like a
    /// one-cell corridor and an enemy squeeze through gaps it physically cannot, or refuse gaps it
    /// can; half-tile cells cost four times the memory of nothing much and describe the room the way
    /// the colliders actually do.
    ///
    /// Blockers are inflated by the agent's radius rather than the agent being modelled, which is the
    /// standard trade: one grid serves every enemy of roughly one size, and the path it returns can be
    /// walked by a body rather than by a point.
    /// </summary>
    public sealed class NavGrid
    {
        /// <summary>Half a tile. Fine enough to describe a doorway, coarse enough to search quickly.</summary>
        public const float DefaultCellSize = 8f;

        /// <summary>
        /// How far a path keeps off the walls, in pixels.
        ///
        /// Deliberately less than half a body. The motor already stops an enemy walking into rock, so
        /// this only has to keep a route from hugging a corner; setting it to a true body radius
        /// blocked two thirds of a real room and left most enemies standing on cells the grid called
        /// solid, which is the state in which none of this helps them.
        /// </summary>
        public const float DefaultAgentRadius = 3f;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public float CellSize { get; private set; }

        /// <summary>World position of the bottom-left corner of cell (0, 0).</summary>
        public Vector2 Origin { get; private set; }

        bool[] _walkable;

        public int Count => Width * Height;

        /// <summary>How many cells can be stood in. For the tests and the debug readout.</summary>
        public int WalkableCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _walkable.Length; i++)
                {
                    if (_walkable[i])
                        n++;
                }
                return n;
            }
        }

        /// <summary>
        /// Builds a grid over <paramref name="bounds"/>, blocked wherever one of <paramref name="blockers"/>
        /// reaches, grown by the agent's radius so a path never scrapes a wall.
        /// </summary>
        public static NavGrid Build(
            Rect bounds,
            IReadOnlyList<Rect> blockers,
            float cellSize = DefaultCellSize,
            float agentRadius = DefaultAgentRadius
        )
        {
            NavGrid grid = new NavGrid
            {
                CellSize = Mathf.Max(1f, cellSize),
                Origin = new Vector2(bounds.xMin, bounds.yMin),
            };

            grid.Width = Mathf.Max(1, Mathf.CeilToInt(bounds.width / grid.CellSize));
            grid.Height = Mathf.Max(1, Mathf.CeilToInt(bounds.height / grid.CellSize));
            grid._walkable = new bool[grid.Width * grid.Height];

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                    grid._walkable[y * grid.Width + x] = true;
            }

            if (blockers == null)
                return grid;

            for (int i = 0; i < blockers.Count; i++)
            {
                Rect grown = Grow(blockers[i], agentRadius);
                grid.BlockRect(grown);
            }

            return grid;
        }

        static Rect Grow(Rect rect, float by) =>
            new Rect(rect.xMin - by, rect.yMin - by, rect.width + by * 2f, rect.height + by * 2f);

        void BlockRect(Rect rect)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt((rect.xMin - Origin.x) / CellSize));
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt((rect.xMax - Origin.x) / CellSize));
            int minY = Mathf.Max(0, Mathf.FloorToInt((rect.yMin - Origin.y) / CellSize));
            int maxY = Mathf.Min(Height - 1, Mathf.CeilToInt((rect.yMax - Origin.y) / CellSize));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // The cell's centre is what an agent would stand on, so that is what is tested.
                    // Blocking any cell the rectangle merely clips would close a corridor that is
                    // genuinely walkable, and at half-tile cells that is most corridors.
                    if (rect.Contains(CellToWorld(x, y)))
                        _walkable[y * Width + x] = false;
                }
            }
        }

        /// <summary>Whether an agent could stand at this world position.</summary>
        public bool IsWalkableAt(Vector2 world) =>
            TryWorldToCell(world, out int x, out int y) && IsWalkable(x, y);

        public bool IsWalkable(int x, int y) =>
            x >= 0 && y >= 0 && x < Width && y < Height && _walkable[y * Width + x];

        /// <summary>The middle of a cell, which is where a path puts an agent.</summary>
        public Vector2 CellToWorld(int x, int y) =>
            new Vector2(
                Origin.x + (x + 0.5f) * CellSize,
                Origin.y + (y + 0.5f) * CellSize
            );

        public bool TryWorldToCell(Vector2 world, out int x, out int y)
        {
            x = Mathf.FloorToInt((world.x - Origin.x) / CellSize);
            y = Mathf.FloorToInt((world.y - Origin.y) / CellSize);
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        /// <summary>
        /// The nearest cell that can be stood in, searched outward in rings.
        ///
        /// Needed at both ends of every path. A body pushed a pixel into a wall by knockback, and a
        /// player standing against one, are both normal, and neither should make an enemy give up:
        /// the honest answer is the nearest place it could stand, not "no path".
        /// </summary>
        public bool TryFindNearest(Vector2 world, int maxRings, out int cellX, out int cellY)
        {
            TryWorldToCell(world, out int startX, out int startY);
            cellX = Mathf.Clamp(startX, 0, Width - 1);
            cellY = Mathf.Clamp(startY, 0, Height - 1);

            if (IsWalkable(cellX, cellY))
                return true;

            int originX = cellX;
            int originY = cellY;

            for (int ring = 1; ring <= maxRings; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        // Only the edge of the ring; the inside was covered by a smaller one.
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dy) != ring)
                            continue;

                        int x = originX + dx;
                        int y = originY + dy;
                        if (!IsWalkable(x, y))
                            continue;

                        cellX = x;
                        cellY = y;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a straight line between two points stays on walkable ground.
        ///
        /// Used to straighten a path afterwards. A* on a grid returns a staircase, and an enemy
        /// walking a staircase across an open room looks like it is following instructions rather
        /// than going somewhere; dropping every waypoint that can be seen past turns it back into a
        /// straight line wherever the room allows one.
        /// </summary>
        public bool HasLineOfSight(Vector2 from, Vector2 to)
        {
            float distance = Vector2.Distance(from, to);
            int steps = Mathf.CeilToInt(distance / (CellSize * 0.5f));
            if (steps <= 0)
                return true;

            for (int i = 0; i <= steps; i++)
            {
                Vector2 point = Vector2.Lerp(from, to, (float)i / steps);
                if (!TryWorldToCell(point, out int x, out int y) || !IsWalkable(x, y))
                    return false;
            }

            return true;
        }
    }
}
