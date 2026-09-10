using System.Collections.Generic;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// One agent's route, kept between frames.
    ///
    /// A path is a plan, and a plan is worth keeping: searching every frame would be both wasteful and
    /// jittery, because two searches a frame apart can pick equally good routes round opposite sides
    /// of a pillar and the agent would stand there twitching between them. It repaths when the plan is
    /// stale, when the destination has genuinely moved, or when it has run out, and walks the plan in
    /// between.
    ///
    /// The first repath is offset by a random slice of the interval so a room full of enemies does not
    /// search on the same frame. That is the difference between a steady cost and a stutter every time
    /// a wave lands.
    /// </summary>
    public sealed class PathFollower
    {
        /// <summary>How close counts as having reached a waypoint. Under a cell, or it skips corners.</summary>
        const float ArriveRadius = 5f;

        /// <summary>How far the destination has to move before the plan is worth redoing.</summary>
        const float RepathDistance = 24f;

        /// <summary>Seconds a plan is trusted for, even if nothing has moved.</summary>
        const float RepathInterval = 0.45f;

        readonly List<Vector2> _path = new();
        readonly Pathfinder _finder = new();

        int _index;
        Vector2 _plannedFor;
        float _timer = -1f;

        /// <summary>The route as it stands, for the debug overlay.</summary>
        public IReadOnlyList<Vector2> Path => _path;

        public int Waypoint => _index;

        public void Clear()
        {
            _path.Clear();
            _index = 0;
            _timer = -1f;
        }

        /// <summary>
        /// The direction to walk this frame to get to <paramref name="goal"/>, or false when there is
        /// no route and the caller should fall back to its own steering.
        /// </summary>
        public bool TryHeading(
            NavGrid grid,
            Vector2 from,
            Vector2 goal,
            float deltaTime,
            out Vector2 heading
        )
        {
            heading = Vector2.zero;

            if (grid == null)
                return false;

            // Staggered, so a crowd spawned on one frame does not then search on one frame forever.
            if (_timer < 0f)
                _timer = Random.Range(0f, RepathInterval);

            _timer -= deltaTime;

            bool stale = _timer <= 0f;
            bool moved = Vector2.Distance(goal, _plannedFor) > RepathDistance;
            bool spent = _index >= _path.Count;

            if (stale || moved || spent)
            {
                _timer = RepathInterval;
                _plannedFor = goal;
                _index = 0;

                if (!_finder.TryFindPath(grid, from, goal, _path))
                {
                    _path.Clear();
                    return false;
                }
            }

            // Drop waypoints already reached. A loop rather than one step, because a fast body can
            // cross more than one in a frame and would otherwise walk back to pick them up.
            while (_index < _path.Count && Vector2.Distance(from, _path[_index]) <= ArriveRadius)
                _index++;

            if (_index >= _path.Count)
            {
                // Arrived at the end of the plan. Head at the goal directly for the last few pixels.
                Vector2 remaining = goal - from;
                if (remaining.sqrMagnitude < 0.0001f)
                    return false;

                heading = remaining.normalized;
                return true;
            }

            Vector2 toWaypoint = _path[_index] - from;
            if (toWaypoint.sqrMagnitude < 0.0001f)
                return false;

            heading = toWaypoint.normalized;
            return true;
        }
    }

    /// <summary>
    /// The grid for the room currently loaded, if there is one.
    ///
    /// A static because navigation is a property of the place rather than of any object in it, and
    /// every enemy needs it without wanting a reference to the room controller. Null outside a room,
    /// which is the case that keeps the lab and the tests working: an enemy with no grid steers the
    /// way it always did.
    /// </summary>
    public static class Nav
    {
        public static NavGrid Grid { get; private set; }

        /// <summary>
        /// Every tile in the room a flier can sit on, as world positions of tile centres.
        ///
        /// Worked out once when the room is built rather than sampled at random and tested, which is
        /// what every earlier version of this did. A room has a few hundred of these and they never
        /// move, so asking "where can I perch" should be a lookup rather than a search that can fail.
        /// </summary>
        public static IReadOnlyList<Vector2> Perches => _perches;

        static readonly List<Vector2> _perches = new();
        static readonly HashSet<Vector2Int> _cells = new();
        static int _tile = 16;

        public static void Set(NavGrid grid) => Grid = grid;

        public static void SetPerches(List<Vector2> perches, int tileSize)
        {
            _perches.Clear();
            _cells.Clear();
            _tile = Mathf.Max(1, tileSize);

            if (perches == null)
                return;

            _perches.AddRange(perches);
            for (int i = 0; i < perches.Count; i++)
                _cells.Add(Cell(perches[i]));
        }

        /// <summary>
        /// Whether the tile containing this point is one of them.
        ///
        /// The one place that answers this. Anything that perches asks here rather than working it
        /// out again from the physics world: a second opinion is a second rule, and the two drifted
        /// apart exactly once, which put a bird on a tile the debug overlay was not drawing. The
        /// overlay and the behaviour have to be the same fact or looking at the overlay proves
        /// nothing.
        ///
        /// False outside a room, so the lab and the tests fall back to the caller's own steering.
        /// </summary>
        public static bool IsPerch(Vector2 point) => _cells.Contains(Cell(point));

        /// <summary>Grid coordinates, so a query anywhere in a tile matches its stored centre.</summary>
        static Vector2Int Cell(Vector2 point) =>
            new(Mathf.FloorToInt(point.x / _tile), Mathf.FloorToInt(point.y / _tile));

        public static void Clear()
        {
            Grid = null;
            _perches.Clear();
            _cells.Clear();
        }
    }
}
