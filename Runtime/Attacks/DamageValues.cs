using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// What a hit is worth. Lives on the hurtbox that deals it rather than in an asset beside it.
    ///
    /// This replaced `DamageProfile`, a ScriptableObject that had to be created and then linked. The
    /// link was the problem: a hurtbox with no profile is still a hurtbox, still overlapping things,
    /// and deals nothing at all with no error. Both explosions shipped that way and had never hurt
    /// anyone; the grenade did the same on its first run. An extra step that fails silently is worse
    /// than no step.
    ///
    /// The asset earned its keep by letting the editor show damage across the roster in one table.
    /// The wiki does that job now, and does it where players can read it, so the tool is not worth the
    /// step it costs.
    ///
    /// **Four fields, because only four were ever used.** The profile had fourteen, and across all
    /// twenty assets in the project ten of them held the same value every time: knockback growth and
    /// air knockback at zero, both hit-feel multipliers at one, crit chance zero, multi-hit off,
    /// armour and invincibility flags off. They were not tuned and never had been. Anything genuinely
    /// needed later can come back as a field here, on the thing that does the hitting.
    /// </summary>
    [System.Serializable]
    public struct DamageValues
    {
        [Tooltip("Health taken off. Grim has 3, so 1 is a third of him and 4 kills most enemies outright.")]
        [MinValue(0)]
        public int damage;

        [Tooltip("Element, for the weakness chart. A hit the target is weak to does double.")]
        public Element damageType;

        [Tooltip("How hard it shoves, in pixels of impulse. A bullet is about 60, an explosion 120 or more.")]
        [MinValue(0f)]
        public float knockback;

        [Tooltip("Screen shake on landing. 0.1 for an ordinary hit, 0.2 for something heavy.")]
        [Range(0f, 1f)]
        public float shakeIntensity;

        [Tooltip(
            "Shatters breakable environment (barrels, crates) outright, whatever this hit's raw damage. "
                + "An explosion clears the environment rather than chipping it, so its 1 damage can still "
                + "level a 3-HP barrel while only taking a third of the player. Leave off for shots and "
                + "melee, so a barrel keeps taking its normal number of hits."
        )]
        public bool breaksEnvironment;

        [Tooltip(
            "A heavy hit: strong knockback, and whatever the game's statuses make of it. Statuses "
                + "decide what a heavy hit does to them, so the meaning is per game rather than fixed here."
        )]
        public bool heavy;

        /// <summary>Sensible values for a hit that has not been configured, so a new box does something.</summary>
        public static DamageValues Default =>
            new DamageValues { damage = 1, damageType = Element.None, knockback = 60f, shakeIntensity = 0.1f };
    }
}
