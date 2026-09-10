using System;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D.Status
{
    /// <summary>
    /// The combat facts the Perk layer listens for. Static, the same as
    /// <see cref="HellWilds.Scoring.ScoreEvents"/>, so the reporters need no reference to whoever is
    /// listening and a scene with no Perk build reports into silence.
    ///
    /// These are what an <c>EffectResolver</c> is driven from. Each carries who was responsible, so
    /// an effect belonging to one player never fires on another player's hit, and a kill by a Burn
    /// somebody else applied is credited to them.
    /// </summary>
    public static class StatusEvents
    {
        /// <summary>A status was applied or refreshed. Target, which status, how many stacks now, who applied it.</summary>
        public static event Action<Entity, StatusDefinition, int, Entity> Applied;

        /// <summary>A status ran out or was broken. Target, which status.</summary>
        public static event Action<Entity, StatusDefinition> Expired;

        /// <summary>
        /// A status did damage: Burn ticking, Bleed popping. Target, which status, how much, who
        /// applied it. Kill credit follows the applier, not the status.
        /// </summary>
        public static event Action<Entity, StatusDefinition, int, Entity> Damaged;

        /// <summary>
        /// Something was forcibly moved by a hit: knockback, a pull, an explosion's push. Walking is
        /// not this, and neither is being slowed. What Bleed pays off on. Target, who displaced it.
        /// </summary>
        public static event Action<Entity, Entity> Displaced;

        public static void ReportApplied(Entity target, StatusDefinition definition, int stacks, Entity applier) =>
            Applied?.Invoke(target, definition, stacks, applier);

        public static void ReportExpired(Entity target, StatusDefinition definition) =>
            Expired?.Invoke(target, definition);

        public static void ReportDamaged(Entity target, StatusDefinition definition, int damage, Entity applier) =>
            Damaged?.Invoke(target, definition, damage, applier);

        public static void ReportDisplaced(Entity target, Entity by) =>
            Displaced?.Invoke(target, by);

        // Static events outlive a play session when domain reload is off, so a dead listener from the
        // last session would hear this one's hits.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearSubscribers()
        {
            Applied = null;
            Expired = null;
            Damaged = null;
            Displaced = null;
        }
    }
}
