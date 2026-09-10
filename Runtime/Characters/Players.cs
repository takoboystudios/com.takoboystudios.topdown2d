using System;
using System.Collections.Generic;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Who is playing, right now, in this session. Player one is index zero, player two is index one.
    ///
    /// **This is the thing everything else was working around.** The HUD bound itself to whichever
    /// player spawned first, the camera followed whichever spawned last, and the room kept a single
    /// `Player` field. Each of those carried a comment promising a registry keyed by index; this is
    /// it, and the promise is what the comments were waiting on.
    ///
    /// **A static roster is not the same as static per-player state.** What lives here is "who is in
    /// this game", which has exactly one answer per running game, the same way the content catalogue
    /// and the element chart do. What each of those players knows (their account, their build, their
    /// unlocks) stays on their own objects; putting any of that here would be the exact mistake this
    /// codebase already made once.
    ///
    /// Index is arrival order and is stable for the life of a session: player one leaving does not
    /// promote player two, because their HUD block, their input device and their save slot are all
    /// keyed by it. A freed index is reused by the next player to join, which is what makes a
    /// drop-in rejoin land back in the same seat.
    /// </summary>
    public static class Players
    {
        // Sized for the roster in scope. A list rather than an array so a third is not a rewrite,
        // but nothing here assumes more than two either.
        static readonly List<Player> _players = new List<Player>(2);

        /// <summary>A player joined, with the index they took.</summary>
        public static event Action<Player, int> Joined;

        /// <summary>A player left, with the index they were at. That index is now free.</summary>
        public static event Action<Player, int> Left;

        /// <summary>How many seats are occupied. Prunes anything that has been destroyed first.</summary>
        public static int Count
        {
            get
            {
                Prune();

                int count = 0;
                for (int i = 0; i < _players.Count; i++)
                {
                    if (_players[i] != null)
                        count++;
                }

                return count;
            }
        }

        /// <summary>The highest occupied index plus one. What to loop to when walking every seat.</summary>
        public static int SeatCount
        {
            get
            {
                Prune();
                return _players.Count;
            }
        }

        /// <summary>The player in a seat, or null when that seat is empty or does not exist.</summary>
        public static Player At(int index)
        {
            Prune();
            return index >= 0 && index < _players.Count ? _players[index] : null;
        }

        /// <summary>Player one, or null before anyone has spawned. For the things that genuinely only concern the first player.</summary>
        public static Player First => At(0);

        /// <summary>Which seat a player is in, or -1 if they are not registered.</summary>
        public static int IndexOf(Player player)
        {
            if (player == null)
                return -1;

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] == player)
                    return i;
            }

            return -1;
        }

        /// <summary>True when this is one of the players, rather than an enemy or a prop.</summary>
        public static bool IsPlayer(Entity entity) => entity is Player player && IndexOf(player) >= 0;

        /// <summary>
        /// The point a camera should sit on to hold everyone: the midpoint of the living players.
        ///
        /// The midpoint and not player one, because a camera anchored to one player pushes the other
        /// off the edge of a fixed 256 by 144 frame, and this game's frame does not zoom. Dead
        /// players are left out so a wipe does not drag the view toward a corpse, and if everyone is
        /// down the last known midpoint is the honest answer rather than the origin.
        /// </summary>
        public static Vector2 Center(Vector2 fallback = default)
        {
            Prune();

            Vector2 sum = Vector2.zero;
            int counted = 0;

            for (int i = 0; i < _players.Count; i++)
            {
                Player player = _players[i];
                if (player == null || player.IsDead)
                    continue;

                sum += (Vector2)player.transform.position;
                counted++;
            }

            return counted > 0 ? sum / counted : fallback;
        }

        /// <summary>
        /// How far apart the living players are, on each axis. What a camera asks before deciding
        /// whether everyone still fits in the frame.
        /// </summary>
        public static Vector2 Spread()
        {
            Prune();

            bool any = false;
            Vector2 min = Vector2.zero;
            Vector2 max = Vector2.zero;

            for (int i = 0; i < _players.Count; i++)
            {
                Player player = _players[i];
                if (player == null || player.IsDead)
                    continue;

                Vector2 at = player.transform.position;
                if (!any)
                {
                    min = at;
                    max = at;
                    any = true;
                    continue;
                }

                min = Vector2.Min(min, at);
                max = Vector2.Max(max, at);
            }

            return any ? max - min : Vector2.zero;
        }

        /// <summary>
        /// Take a seat. The lowest free index, so a player who dropped out and rejoined lands back in
        /// the seat their HUD block and input device are already keyed to.
        ///
        /// Called by <see cref="Player.Init"/>. Registering twice is a no-op rather than a second
        /// seat, because Init can run again on a pooled or re-enabled body.
        /// </summary>
        public static int Join(Player player)
        {
            if (player == null)
                return -1;

            int existing = IndexOf(player);
            if (existing >= 0)
                return existing;

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] == null)
                {
                    _players[i] = player;
                    Joined?.Invoke(player, i);
                    return i;
                }
            }

            _players.Add(player);
            int index = _players.Count - 1;
            Joined?.Invoke(player, index);
            return index;
        }

        /// <summary>Give up a seat. The index is freed but the ones after it do not shuffle down.</summary>
        public static void Leave(Player player)
        {
            int index = IndexOf(player);
            if (index < 0)
                return;

            _players[index] = null;
            Left?.Invoke(player, index);
            TrimTail();
        }

        /// <summary>
        /// Forget destroyed bodies at the end of the roster, without announcing them. A body removed
        /// by a scene change never said goodbye.
        ///
        /// A destroyed player in the middle of the roster is deliberately left as an empty seat
        /// rather than closed up, so player two keeps index one while player one is being respawned.
        /// Unity's null comparison already reports a destroyed object as null, so every read below
        /// treats that seat as empty without anything having to be rewritten.
        /// </summary>
        static void Prune() => TrimTail();

        static void TrimTail()
        {
            for (int i = _players.Count - 1; i >= 0 && _players[i] == null; i--)
                _players.RemoveAt(i);
        }

        /// <summary>Empty the roster. For a test, and for the start of a session.</summary>
        public static void Clear() => _players.Clear();

        // The roster is per session, and with domain reload off these statics outlive one. Without
        // this, the second play session would start with player one already seated by a destroyed
        // body from the first.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            _players.Clear();
            Joined = null;
            Left = null;
        }
    }
}
