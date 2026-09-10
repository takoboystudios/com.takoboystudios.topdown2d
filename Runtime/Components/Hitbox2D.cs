using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Hitbox component for entities that can receive damage.
    ///
    /// Features:
    /// - Invincibility state management
    /// - Timed invincibility frames (I-frames)
    /// - Damage reception with bypass checks
    /// - Event hooks for custom hit responses
    ///
    /// Attach this to any entity that should be able to take damage.
    /// </summary>
    public class Hitbox2D : PhysicsBox
    {
        public override BoxTypes BoxType => BoxTypes.Hitbox;

        [BoxGroup("Invincibility")]
        [Tooltip("Is this hitbox currently invincible?")]
        [SerializeField]
        bool m_invincible = false;

        [BoxGroup("Invincibility")]
        [Tooltip("Duration of invincibility frames after taking damage (seconds)")]
        [SerializeField]
        [Range(0f, 2f)]
        float m_iFrameDuration = 0.5f;

        [BoxGroup("Invincibility")]
        [Tooltip("Enable automatic I-frames after taking damage")]
        [SerializeField]
        bool m_useIFrames = true;

        [BoxGroup("Debug")]
        [ShowInInspector]
        [ReadOnly]
        [Tooltip("Time remaining on current invincibility")]
        float m_invincibilityTimer = 0f;

        // Events
        public event Action<HitEvent> OnHit;
        public event Action<HitEvent> OnHitBlocked; // Called when hit while invincible

        [BoxGroup("Invincibility")]
        [Tooltip(
            "Phased means shots and melee pass straight through, as if nothing is here: a burrowed "
                + "enemy underground. This is different from Invincible, which is present and stops a "
                + "shot dead (it just takes no damage), the way an armoured front pings bullets off."
        )]
        [SerializeField]
        bool m_phased = false;

        [BoxGroup("Damage")]
        [Tooltip(
            "Shrugs off passive contact damage: a body simply touching this box deals it nothing, "
                + "only active hits (shots, swings, blasts) land. A barrel is broken by what strikes "
                + "it, not by an enemy bumping into it. Off for creatures, which take contact damage "
                + "normally."
        )]
        [SerializeField]
        bool m_ignoresContactDamage = false;

        // Properties
        public bool Invincible => m_invincible || m_invincibilityTimer > 0f;
        public float InvincibilityTimeRemaining => m_invincibilityTimer;

        /// <summary>When true, incoming attacks ignore this hitbox entirely rather than stopping on it.</summary>
        public bool Phased => m_phased;

        /// <summary>
        /// When true, a hurtbox flagged <see cref="DamageBoxBehavior.ContactDamage"/> deals this
        /// hitbox no damage: it is only hurt by active strikes. Scenery like a barrel sets this.
        /// </summary>
        public bool IgnoresContactDamage => m_ignoresContactDamage;

        public void SetPhased(bool value) => m_phased = value;

        /// <summary>
        /// Called when this hitbox is hit by a hurtbox.
        /// Applies damage to owner if not invincible.
        /// </summary>
        public void Hit(HitEvent hitEvent)
        {
            // Invincibility is absolute now. The flag that let a hit ignore it was off on every damage
            // asset in the project and had never been used, so it went with the profile.
            if (Invincible)
            {
                OnHitBlocked?.Invoke(hitEvent);
                return;
            }

            // Fire hit event
            OnHit?.Invoke(hitEvent);

            // Deal damage to owner
            if (Owner != null)
            {
                Owner.DealDamage(hitEvent);

                // Start I-frames if enabled
                if (m_useIFrames && m_iFrameDuration > 0f)
                {
                    StartInvincibility(m_iFrameDuration);
                }
            }
        }

        /// <summary>
        /// Sets permanent invincibility state (on/off).
        /// </summary>
        public void SetInvincible(bool value)
        {
            m_invincible = value;

            if (value)
            {
                // Clear timed invincibility when enabling permanent
                m_invincibilityTimer = 0f;
            }
        }

        /// <summary>
        /// Starts temporary invincibility for the specified duration.
        /// </summary>
        public void StartInvincibility(float duration)
        {
            m_invincibilityTimer = Mathf.Max(m_invincibilityTimer, duration);
        }

        /// <summary>
        /// Clears all invincibility (permanent and timed).
        /// </summary>
        public void ClearInvincibility()
        {
            m_invincible = false;
            m_invincibilityTimer = 0f;
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);

            if (m_invincibilityTimer > 0f)
            {
                m_invincibilityTimer -= dt;
                if (m_invincibilityTimer < 0f)
                    m_invincibilityTimer = 0f;
            }
        }

        /// <summary>
        /// Draws the hitbox blue while it is invincible, and its normal colour while it can be hit.
        /// This is the fastest way to see a vulnerability window that is not opening: watch the box
        /// in the Scene view during play, or turn on Gizmos in the Game view. A hitbox that stays
        /// blue is rejecting every hit, which reads as "my shots do nothing" even though the shots
        /// are landing.
        /// </summary>
        protected override string GetEditorColor()
        {
            return Invincible ? "#3f8cff" : base.GetEditorColor();
        }
    }
}
