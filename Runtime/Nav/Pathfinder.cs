using System.Collections.Generic;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A* over a <see cref="NavGrid"/>.
    ///
    /// One instance per user, reused. Every buffer is sized once to the grid and cleared by a
    /// generation stamp rather than refilled, so a search allocates nothing and costs nothing to
    /// start: enemies repath often, and a pathfinder that allocated per query would be a garbage
    /// collection every few seconds during a fight.
    ///
    /// Eight directions, with diagonals refused when both neighbouring orthogonals are blocked. That
    /// stops a body cutting the corner of a wall it is physically inside, which on a half-tile grid
    /// happens at every doorway.
    ///
    /// The result is straightened before it is returned. A* on a grid produces a staircase, and an
    /// enemy walking a staircase across open floor looks like it is following instructions rather
    /// than walking somewhere.
    /// </summary>
    public sealed class Pathfinder
    {
        /// <summary>Integer costs, so ties break identically every run and a path is reproducible.</summary>
        const int Orthogonal = 10;
        const int Diagonal = 14;

        /// <summary>How far out to look for standable ground when an end of the path is inside a wall.</summary>
        const int NearestRings = 6;

        /// <summary>
        /// A ceiling on work per search. A room is a few thousand cells, so a search that has looked
        /// at this many has not found a way and is not going to; giving up beats a frame spike.
        /// </summary>
        const int MaxExpansions = 4000;

        NavGrid _grid;
        int[] _gScore;
        int[] _fScore;
        int[] _cameFrom;
        int[] _stamp;
        bool[] _closed;
        int _generation;

        readonly List<int> _open = new();

        static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] StepY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>How many cells the last search looked at. For tuning and the tests.</summary>
        public int LastExpansions { get; private set; }

        /// <summary>
        /// Fills <paramref name="path"/> with world positions from <paramref name="from"/> to
        /// <paramref name="to"/>, or returns false when there is no way through.
        ///
        /// The path excludes the starting point and ends at the destination cell, so an agent can
        /// walk it by heading for entry zero and dropping it on arrival.
        /// </summary>
        public bool TryFindPath(NavGrid grid, Vector2 from, Vector2 to, List<Vector2> path)
        {
            path.Clear();
            LastExpansions = 0;

            if (grid == null || grid.Count == 0)
                return false;

            Prepare(grid);

            if (!grid.TryFindNearest(from, NearestRings, out int startX, out int startY))
                return false;

            if (!grid.TryFindNearest(to, NearestRings, out int goalX, out int goalY))
                return false;

            int start = startY * grid.Width + startX;
            int goal = goalY * grid.Width + goalX;

            if (start == goal)
            {
                path.Add(grid.CellToWorld(goalX, goalY));
                return true;
            }

            _generation++;
            _open.Clear();

            Touch(start);
            _gScore[start] = 0;
            _fScore[start] = Heuristic(startX, startY, goalX, goalY);
            _open.Add(start);

            while (_open.Count > 0)
            {
                int current = TakeLowest();
                if (current == goal)
                {
                    Reconstruct(grid, start, goal, path);

                    // Straighten from where the agent can actually stand, not from where it is. A
                    // body pressed against a wall sits on a cell the inflated grid calls blocked, so
                    // anchoring on its raw position makes every line of sight fail and the path stays
                    // a staircase for exactly the enemies most in need of a sensible route.
                    Vector2 anchor = grid.IsWalkableAt(from) ? from : grid.CellToWorld(startX, startY);
                    Straighten(grid, anchor, path);
                    return true;
                }

                _closed[current] = true;

                if (++LastExpansions > MaxExpansions)
                    return false;

                int cx = current % grid.Width;
                int cy = current / grid.Width;

                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + StepX[i];
                    int ny = cy + StepY[i];

                    if (!grid.IsWalkable(nx, ny))
                        continue;

                    bool diagonal = i >= 4;
                    if (diagonal && !grid.IsWalkable(cx + StepX[i], cy) && !grid.IsWalkable(cx, cy + StepY[i]))
                        continue;

                    int neighbour = ny * grid.Width + nx;
                    if (_stamp[neighbour] == _generation && _closed[neighbour])
                        continue;

                    int tentative = _gScore[current] + (diagonal ? Diagonal : Orthogonal);

                    bool seen = _stamp[neighbour] == _generation;
                    if (seen && tentative >= _gScore[neighbour])
                        continue;

                    Touch(neighbour);
                    _cameFrom[neighbour] = current;
                    _gScore[neighbour] = tentative;
                    _fScore[neighbour] = tentative + Heuristic(nx, ny, goalX, goalY);

                    if (!seen)
                        _open.Add(neighbour);
                }
            }

            return false;
        }

        void Prepare(NavGrid grid)
        {
            if (_grid == grid && _gScore != null && _gScore.Length == grid.Count)
                return;

            _grid = grid;
            _gScore = new int[grid.Count];
            _fScore = new int[grid.Count];
            _cameFrom = new int[grid.Count];
            _stamp = new int[grid.Count];
            _closed = new bool[grid.Count];
            _generation = 0;
        }

        /// <summary>
        /// Brings a cell into this search. The stamp is what makes clearing free: a cell from an
        /// earlier search is simply one whose stamp is stale, so nothing has to be reset between
        /// queries.
        /// </summary>
        void Touch(int cell)
        {
            if (_stamp[cell] == _generation)
                return;

            _stamp[cell] = _generation;
            _gScore[cell] = int.MaxValue;
            _fScore[cell] = int.MaxValue;
            _cameFrom[cell] = -1;
            _closed[cell] = false;
        }

        /// <summary>
        /// Cheapest open cell, by linear scan.
        ///
        /// A heap is the textbook answer and would be faster on a big graph. The open set here peaks
        /// in the low hundreds on a room-sized grid, where a scan over a flat list beats a heap's
        /// bookkeeping and is a great deal easier to be sure is correct.
        /// </summary>
        int TakeLowest()
        {
            int bestIndex = 0;
            int best = _fScore[_open[0]];

            for (int i = 1; i < _open.Count; i++)
            {
                int score = _fScore[_open[i]];
                if (score >= best)
                    continue;

                best = score;
                bestIndex = i;
            }

            int cell = _open[bestIndex];
            _open[bestIndex] = _open[_open.Count - 1];
            _open.RemoveAt(_open.Count - 1);
            return cell;
        }

        /// <summary>Octile distance: exact for eight-way movement, so the search never wanders.</summary>
        static int Heuristic(int x, int y, int goalX, int goalY)
        {
            int dx = Mathf.Abs(x - goalX);
            int dy = Mathf.Abs(y - goalY);
            return Orthogonal * (dx + dy) + (Diagonal - 2 * Orthogonal) * Mathf.Min(dx, dy);
        }

        void Reconstruct(NavGrid grid, int start, int goal, List<Vector2> path)
        {
            int cell = goal;
            while (cell != start && cell >= 0)
            {
                path.Add(grid.CellToWorld(cell % grid.Width, cell / grid.Width));
                cell = _cameFrom[cell];
            }

            path.Reverse();
        }

        /// <summary>
        /// Drops every waypoint that can be walked past in a straight line.
        ///
        /// Done from the agent's actual position rather than from the first cell centre, because the
        /// first thing a staircase does otherwise is send it sideways to the middle of the cell it is
        /// already standing in.
        /// </summary>
        static void Straighten(NavGrid grid, Vector2 from, List<Vector2> path)
        {
            if (path.Count < 2)
                return;

            int write = 0;
            Vector2 anchor = from;

            for (int i = 0; i < path.Count; i++)
            {
                bool last = i == path.Count - 1;

                // Keep a point only when the one after it cannot be reached directly from the anchor.
                if (!last && grid.HasLineOfSight(anchor, path[i + 1]))
                    continue;

                anchor = path[i];
                path[write++] = path[i];
            }

            path.RemoveRange(write, path.Count - write);
        }
    }
}
