using System;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// The facts the entity layer reports about itself: a player arrived, a player was hurt, an
    /// enemy was killed.
    ///
    /// These exist so the entity model can say what happened without knowing who cares (T-409).
    /// Scoring, a run recorder, an achievement, a Perk effect and a tutorial all want the same three
    /// facts, and none of them belongs in a reusable entity package. `Enemy` used to call the game's
    /// scoring service directly, which is exactly the dependency that would stop this code being
    /// shared between games.
    ///
    /// The game bridges these onto whatever it actually uses. In Hell Wilds that is
    /// <c>ScoreEventBridge</c>, which forwards them into the scoring service, so nothing that
    /// already listens to scoring had to change.
    ///
    /// Static for the same reason the scoring events are: a reporter needs no reference to a
    /// listener, and a scene with nothing listening reports into silence.
    /// </summary>
    public static class EntityEvents
    {
        /// <summary>A player finished setting up and is live in the scene.</summary>
        public static event Action<Entity> PlayerSpawned;

        /// <summary>A player genuinely lost health. Hits rejected by invincibility never raise this.</summary>
        public static event Action<Entity> PlayerDamaged;

        /// <summary>
        /// An enemy died to damage. Carries the body and the id it was spawned as, so a listener can
        /// score it, count it or react to what it was.
        ///
        /// Reported from lethal damage rather than from death itself, because death is also the
        /// removal path (a room tearing down, an encounter sealing something out) and a removal is
        /// not a kill.
        /// </summary>
        public static event Action<Entity, string, Vector2> EnemyKilled;

        public static void ReportPlayerSpawned(Entity player) => PlayerSpawned?.Invoke(player);

        public static void ReportPlayerDamaged(Entity player) => PlayerDamaged?.Invoke(player);

        public static void ReportEnemyKilled(Entity enemy, string registryId, Vector2 position) =>
            EnemyKilled?.Invoke(enemy, registryId, position);

        // Static events outlive a play session when domain reload is off, so a listener from the last
        // session would hear this one's kills.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearSubscribers()
        {
            PlayerSpawned = null;
            PlayerDamaged = null;
            EnemyKilled = null;
        }
    }
}
