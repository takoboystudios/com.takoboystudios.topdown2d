using System;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Declares a damage box belonging to one of the enemy's attacks, and tells the prefab
    /// scaffolder to build it.
    ///
    /// This is NOT contact damage. An attack hurtbox is inert until the enemy explicitly strikes
    /// with it on the frames where its animation connects, so standing next to the enemy is safe
    /// and only the swing hurts. Use [ContactDamage] for creatures that hurt to touch.
    ///
    /// It hits each target once per swing. The enemy calls ResetStrike when a new swing begins,
    /// which is what stops a three frame active window landing three hits.
    ///
    /// FieldName is the serialized Hurtbox2D field on the enemy class to assign the built box to,
    /// so the wiring is visible in code rather than something to remember in the Inspector:
    ///
    ///     [AttackHurtbox("swingHurtbox", Width = 26f, Height = 20f)]
    ///     public class GobGrunt : Enemy
    ///     {
    ///         [SerializeField] Hurtbox2D swingHurtbox;
    ///
    /// Sizes are in pixels, since the project runs at 1 unit per pixel.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class AttackHurtboxAttribute : Attribute
    {
        /// <summary>Serialized Hurtbox2D field on the enemy that this box gets assigned to.</summary>
        public string FieldName { get; }

        /// <summary>Width of the damage box.</summary>
        public float Width = 24f;

        /// <summary>Height of the damage box.</summary>
        public float Height = 20f;

        /// <summary>
        /// Height above the enemy's feet where the box sits. Sprites pivot at BottomCenter, so 0 is
        /// on the ground and around 10 puts it on the body.
        /// </summary>
        public float OffsetY = 10f;

        /// <summary>
        /// Base name of the animation this box belongs to. The scaffolder looks for
        /// "{Animation}-{direction}" in the animation asset and builds one box per direction that
        /// actually exists, so the box set can never claim more directions than the art has.
        /// </summary>
        public string Animation = "attack";

        /// <summary>Damage dealt per hit.</summary>
        public int Damage = 1;

        /// <summary>Knockback applied to whatever it hits.</summary>
        public float Knockback = 140f;

        public AttackHurtboxAttribute(string fieldName)
        {
            FieldName = fieldName;
        }
    }
}
