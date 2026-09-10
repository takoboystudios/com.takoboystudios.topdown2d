using TakoBoyStudios.Animation;
using TakoBoyStudios.Core;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Character is an intermediate layer between Entity and specific character types (Player, Enemy).
    /// Contains shared functionality for animated combat characters.
    /// </summary>
    public abstract class Character : PhysicsEntity
    {
        #region Components

        protected Vector2 _lastAimDirection;

        #endregion

        #region Initialization

        // Init() inherited from Entity - no need to override since base class handles m_entityAnimator

        #endregion

        #region States

        protected override void StateDead(Fsm.StateStep step, float deltaTime)
        {
            switch (step)
            {
                case Fsm.StateStep.Enter:
                    m_moveInput = Vector2.zero;
                    m_impulseVelocity = Vector2.zero;
                    Vector2 faceDirection = GetAnimationFacingDirection();
                    Direction direction = faceDirection.ToDirection();
                    PlayAnimationWithDirection(AnimConst.Death, direction);
                    break;
                case Fsm.StateStep.Update:
                    if (m_entityAnimator.IsDone)
                        Dispose();
                    break;
            }
        }

        #endregion

        #region Animation System

        /// <summary>
        /// Updates animations based on current state, movement, and aim direction.
        /// Override this in child classes for specific animation behavior.
        /// </summary>
        protected override void UpdateAnimations(float deltaTime)
        {
            if (!m_entityAnimator)
            {
                base.UpdateAnimations(deltaTime);
                return;
            }

            // Handle aerial animations
            if (!Grounded)
            {
                UpdateAerialAnimations();
                return;
            }

            // Handle grounded animations
            UpdateGroundedAnimations();
        }

        /// <summary>
        /// Handles animations when the character is in the air.
        /// Can be overridden for specific aerial animation behaviors.
        /// </summary>
        protected virtual void UpdateAerialAnimations()
        {
            // Let jump anticipation finish
            if (
                IsAnimationValid(AnimConst.JumpAntic)
                && IsCurrentAnimation(AnimConst.JumpAntic)
                && m_entityAnimator
                && !m_entityAnimator.IsDone
            )
            {
                return;
            }

            Vector2 faceDirection = GetAnimationFacingDirection();
            Direction direction = faceDirection.ToDirection();

            // Jump peak
            if (Mathf.Abs(VerticalVelocity) <= 0.1f && IsAnimationValid(AnimConst.JumpPeak))
            {
                PlayAnimationWithDirection(AnimConst.JumpPeak, direction);
            }
            // Rising
            else if (VerticalVelocity > 0.1f)
            {
                PlayAnimationWithDirection(AnimConst.JumpLoop, direction);
            }
            // Falling
            else
            {
                PlayAnimationWithDirection(AnimConst.Fall, direction);
            }
        }

        /// <summary>
        /// Handles animations when the character is on the ground.
        /// Override this for specific grounded animation behaviors.
        /// </summary>
        protected virtual void UpdateGroundedAnimations()
        {
            // Default ground animation logic
            if (m_moveInput.sqrMagnitude > 0f)
            {
                PlayAnimation(AnimConst.Move);
            }
            else
            {
                PlayAnimation(AnimConst.Idle);
            }
        }

        /// <summary>
        /// Determines the direction the character should face for animations.
        /// Override this to customize facing behavior (e.g., aim direction vs movement direction).
        /// </summary>
        protected virtual Vector2 GetAnimationFacingDirection()
        {
            if (m_moveInput.sqrMagnitude > 0.0001f)
                return m_moveInput;

            return FacingDirection;
        }

        /// <summary>
        /// Plays an animation with directional suffix (e.g., "Idle-N", "Move-SE").
        /// </summary>
        protected void PlayAnimationWithDirection(string baseAnim, Direction dir)
        {
            if (!m_entityAnimator)
                return;

            string animId = $"{baseAnim}-{dir.ToAnimId()}";
            if (!m_entityAnimator.HasAnimation(animId))
                return;

            if (!m_entityAnimator.CurrentAnimationName.Equals(animId))
                m_entityAnimator.Play(animId);
        }

        #endregion

        #region Combat System

        /// <summary>
        /// Called when the character should perform an attack.
        /// Override this to implement specific attack behaviors.
        /// </summary>
        /// <param name="direction">Direction to attack in</param>
        public virtual void Attack(Vector2 direction)
        {
            _lastAimDirection = direction.normalized;
        }

        /// <summary>
        /// Gets the last direction this character aimed/attacked in.
        /// </summary>
        public Vector2 LastAimDirection => _lastAimDirection;

        #endregion

        #region State Management

        // AddStates() can be overridden by child classes (Player, Enemy) to add custom states
        // No need to override here since Character doesn't add any states

        #endregion

        #region Utility Methods

        /// <summary>
        /// Checks if the EntityAnimator component is valid and initialized.
        /// </summary>
        protected bool HasEntityAnimator => m_entityAnimator != null;

        #endregion
    }
}
