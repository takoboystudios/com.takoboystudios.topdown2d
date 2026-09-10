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
        /// A hit connected and its damage was applied. Carries the whole hit, so a listener knows who
        /// threw it, who took it, what element it was and whether it was heavy, plus the status whose
        /// damage this was when a status caused it and null when the hit came from an attack.
        ///
        /// **That second argument is what stops a Perk feeding itself.** A status ticking goes through
        /// the same damage path as a sword, so without it a Perk that ignites on hit would be ignited
        /// by its own burn, once per tick, forever. Naming the status lets a listener recognise the
        /// hit as the mechanic's own output and decline to answer it.
        ///
        /// Reported for every hit between any two entities, including the one that kills, and
        /// including hits an enemy lands on the player. Filtering to the hits it cares about is the
        /// listener's job: a Perk build asks whether the source is its own player, and most listeners
        /// want nothing to do with most hits. Doing that filtering here would mean this package
        /// knowing what a player is to the game that owns it.
        /// </summary>
        public static event Action<DamageInfo, Status.StatusDefinition> HitLanded;

        /// <summary>
        /// An enemy died to damage. Carries the body, the id it was spawned as, where it fell and
        /// whoever landed the killing hit, so a listener can score it, count it, or react to what it
        /// was and who did it.
        ///
        /// The killer is the immediate source of the lethal hit, which for a bullet is the bullet:
        /// walk <see cref="Entity.RootInstigator"/> to reach the player behind it. It is whoever
        /// applied the status when a status finished something off, so kill credit follows the person
        /// who set the fire rather than the fire. Null is possible and means nothing was credited.
        ///
        /// Reported from lethal damage rather than from death itself, because death is also the
        /// removal path (a room tearing down, an encounter sealing something out) and a removal is
        /// not a kill.
        /// </summary>
        public static event Action<Entity, string, Vector2, Entity> EnemyKilled;

        public static void ReportPlayerSpawned(Entity player) => PlayerSpawned?.Invoke(player);

        public static void ReportPlayerDamaged(Entity player) => PlayerDamaged?.Invoke(player);

        public static void ReportHitLanded(DamageInfo info, Status.StatusDefinition fromStatus) =>
            HitLanded?.Invoke(info, fromStatus);

        public static void ReportEnemyKilled(Entity enemy, string registryId, Vector2 position, Entity killer) =>
            EnemyKilled?.Invoke(enemy, registryId, position, killer);

        // Static events outlive a play session when domain reload is off, so a listener from the last
        // session would hear this one's kills.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearSubscribers()
        {
            PlayerSpawned = null;
            PlayerDamaged = null;
            HitLanded = null;
            EnemyKilled = null;
        }
    }
}
