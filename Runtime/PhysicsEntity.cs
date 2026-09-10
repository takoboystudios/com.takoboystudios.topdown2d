using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// An <see cref="Entity"/> that moves (T-355). Everything that walks, flies, is thrown or is
    /// knocked back is a PhysicsEntity: it owns the <see cref="TopDownMotor2D"/> and the movement
    /// path (planar input plus impulse fed to the motor, the fake-Z jump/arc/hover, flight, and the
    /// hitbox lift that follows the visual). A plain Entity has none of that, which is what lets a
    /// barrel or an explosion be a first-class entity without pretending to be an actor with its
    /// movement switched off.
    ///
    /// The class tree Tom drew, 2026-09-01:
    ///   Entity (exists, has health, can be hit)
    ///     PhysicsEntity (also moves)
    ///       Projectile -> Bomb
    ///       Character -> Player, Enemy
    ///     Effect     (static)
    ///     Breakable  (static)
    ///
    /// The motor requirement lives here, on the one class every moving entity shares, so the static
    /// Entities can drop the motor without RequireComponent making the removal fail silently, which
    /// is the trap that kept a "converted" barrel moving (T-351). Entity exposes the movement state
    /// (Velocity, Z, Grounded, MoveInput, AddImpulse, ...) as virtuals with motorless defaults, and
    /// this class overrides each with the real motor, so shared code on Entity reads movement
    /// without knowing whether the body can move.
    /// </summary>
    [RequireComponent(typeof(TopDownMotor2D))]
    public abstract class PhysicsEntity : Entity
    {
        [BoxGroup("Instance Properties")]
        [SerializeField, Min(0f)]
        protected float m_maxImpulseSpeed = 80f;

        [BoxGroup("Instance Properties")]
        [SerializeField]
        protected bool m_startFlying;

        [BoxGroup("Components")]
        [SerializeField]
        protected TopDownMotor2D motor;

        // Inputs collected externally (player or AI).
        protected Vector2 m_moveInput; // normalized or analog
        protected bool m_jumpPressed; // edge this frame
        protected bool m_jumpHeld; // held state

        // Additive velocity from knockback, dashes and scripted pushes.
        protected Vector2 m_impulseVelocity;

        protected bool m_flying;

        // What this body collided with before it took off, so landing can restore it.
        LayerMask _groundedCollisionMask;

        // ----------------------------
        // Movement state (overrides Entity's motorless defaults)
        // ----------------------------
        public override bool IsColliding => motor && motor.IsColliding();
        public override Vector2 Velocity => motor ? motor.Velocity : Vector2.zero;
        public override float Z => motor ? motor.Height : base.Z;
        public override float VerticalVelocity => motor ? motor.VerticalVelocity : 0f;
        public override bool Grounded => motor && motor.IsGrounded;
        public override bool Flying => m_flying;
        public override Vector2 MoveInput => m_moveInput;
        public override Vector2 ImpulseVelocity => m_impulseVelocity;

        // ----------------------------
        // Lifecycle
        // ----------------------------
        protected override void Reset()
        {
            base.Reset();
            if (!motor)
                motor = GetComponent<TopDownMotor2D>();
        }

        protected override void Awake()
        {
            base.Awake();
            if (!motor)
                motor = GetComponent<TopDownMotor2D>();
        }

        protected override void OnInitialized()
        {
            if (m_startFlying)
                StartFlying();
        }

        protected override void TickMovement(float dt)
        {
            if (motor != null)
                motor.Tick(dt);
        }

        protected override void PhysicsTick(float fdt)
        {
            // Compose desired planar velocity from input and impulse.
            Vector2 desired = m_moveInput * m_moveSpeed + m_impulseVelocity;
            bool jumpNow = m_jumpPressed;
            bool jumpHeld = m_jumpHeld;

            // Feed the motor (shared for player and AI).
            motor.ApplyInput(desired, jumpNow, jumpHeld);
            motor.PhysicsTick(fdt);
            m_fsm.PhysicsTick(fdt);

            // Clear the edge-pressed flag after consuming it.
            m_jumpPressed = false;

            // Exponential decay for snappy knockback feel. Heavier entities shed impulse faster.
            if (m_impulseVelocity.sqrMagnitude > 0f)
            {
                // Base decay: 0.94^60 is about 5% remaining after one second at default weight.
                const float baseDecay = 0.94f;
                const float baseWeight = 100f;

                float weightScale = m_weight / baseWeight;
                float effectiveDecay = Mathf.Pow(baseDecay, weightScale);

                float decayFactor = Mathf.Pow(effectiveDecay, fdt * 60f);
                m_impulseVelocity *= decayFactor;

                // Cut to zero when very small, to stop float drift.
                if (m_impulseVelocity.sqrMagnitude < 0.01f)
                    m_impulseVelocity = Vector2.zero;
            }

            // Update last move direction for facing and attacks.
            if (m_moveInput.sqrMagnitude > 0.0001f)
                m_lastMoveDirection = m_moveInput;
        }

        protected override void ResetMovement()
        {
            m_moveInput = Vector2.zero;
            m_impulseVelocity = Vector2.zero;
        }

        // ----------------------------
        // Input feeding API (player or AI)
        // ----------------------------
        public void SetMoveDirection(Vector2 input)
        {
            m_moveInput = input;
            if (input.sqrMagnitude > 0.0001f)
                m_facingDirection = input.normalized;
        }

        public void SetJumpInput(bool pressed, bool held)
        {
            // pressed is an edge. The motor buffers, but the edge is still passed once.
            m_jumpPressed |= pressed;
            m_jumpHeld = held;
        }

        public override void AddImpulse(Vector2 impulse)
        {
            m_impulseVelocity += impulse;
            if (m_impulseVelocity.magnitude > m_maxImpulseSpeed)
                m_impulseVelocity = m_impulseVelocity.normalized * m_maxImpulseSpeed;
        }

        // ----------------------------
        // Flight
        // ----------------------------
        /// <summary>
        /// Puts this body in the air: it stops colliding with the room's walls. The combat boundary
        /// is untouched, so a flier sealed into an encounter is still held inside the frame.
        /// </summary>
        protected virtual void StartFlying()
        {
            if (m_flying)
                return;

            m_flying = true;

            if (motor != null)
            {
                _groundedCollisionMask = motor.CollisionMask;
                motor.SetCollisionMask(0);
            }
        }

        /// <summary>Back on the floor, colliding with whatever it collided with before it took off.</summary>
        protected virtual void StopFlying()
        {
            if (!m_flying)
                return;

            m_flying = false;

            if (motor != null)
                motor.SetCollisionMask(_groundedCollisionMask);
        }
    }
}
