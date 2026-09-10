using System;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Declares that an enemy damages what it touches, and how big that box is.
    ///
    /// Contact damage is deliberately opt in, and it is NOT how attacks work. This is for creatures
    /// that hurt to touch, like a slime: the box is centred on the body and is on whenever the enemy
    /// is alive. An enemy that swings a weapon uses [AttackHurtbox] instead, and is safe to stand
    /// next to between swings. Most enemies want neither, or only the latter.
    ///
    /// Putting the declaration at the top of the class means you can see which an enemy is without
    /// opening the prefab.
    ///
    /// The prefab scaffolder reads this and builds the HurtBox for you.
    ///
    /// Sizes are in pixels, since the project runs at 1 unit per pixel.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class ContactDamageAttribute : Attribute
    {
        /// <summary>Width of the damage box.</summary>
        public float Width = 20f;

        /// <summary>Height of the damage box.</summary>
        public float Height = 20f;

        /// <summary>
        /// Height above the entity's feet where the box sits. Sprites pivot at BottomCenter, so 0
        /// is on the ground. Around 10 puts it on the body.
        /// </summary>
        public float OffsetY = 10f;

        /// <summary>Damage dealt per hit.</summary>
        public int Damage = 1;

        /// <summary>Knockback applied to whatever it hits.</summary>
        public float Knockback = 120f;
    }
}
