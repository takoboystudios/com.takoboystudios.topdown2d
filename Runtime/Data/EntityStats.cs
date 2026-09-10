using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// The shared vital stats of an entity, as data. Assign one to an Entity's Stats slot and it
    /// loads these on Init, overriding the inline defaults on the prefab.
    ///
    /// Pulling the numbers into an asset means the whole roster's tuning lives in one place, the
    /// Stats editor (HellWilds > Stats Editor), instead of one prefab at a time. Two entities can
    /// share a sheet, and balance passes never need to open a prefab.
    ///
    /// Vitals only, on purpose: how tough and how fast an entity is. How hard it hits is a
    /// the hurtbox that deals it, so there is one home for every damage number.
    /// </summary>
    [CreateAssetMenu(menuName = "HellWilds/Entity Stats", fileName = "EntityStats")]
    public class EntityStats : ScriptableObject
    {
        [Tooltip("Maximum health points.")]
        [Min(1)]
        public int maxHp = 1;

        [Tooltip("Move speed. The project runs at 1 unit per pixel, so this is pixels per second.")]
        [Min(0f)]
        public float moveSpeed = 9f;

        [Tooltip("Weight. Heavier entities are shoved less far by the same knockback.")]
        [Min(0f)]
        public float weight = 100f;

        [Tooltip("Lifetime in seconds before it despawns on its own, or -1 to live until killed.")]
        public float lifetime = -1f;

        [Tooltip("Elemental type, used by the damage weakness table.")]
        public Element element = Element.None;
    }
}
