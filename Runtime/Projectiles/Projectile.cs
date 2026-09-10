using System.Collections.Generic;
using TakoBoyStudios.Animation;
using TakoBoyStudios.Core;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    // A projectile moves, so it is a PhysicsEntity, which carries the motor requirement (T-355).
    public class Projectile : PhysicsEntity
    {
        [Header("Homing")]
        [SerializeField]
        [Tooltip(
            "Curves toward nearby enemies while flying. This is the aim-assist magnetism the "
                + "8-directional aim leans on: it forgives the gun's spread by pulling shots back "
                + "onto a target in range. Leave off for straight shots, like most enemy fire."
        )]
        bool homing;

        [SerializeField, Min(0f)]
        [Tooltip("How close a target must be, in pixels, before it starts to pull the shot.")]
        float homingRange = 80f;

        [SerializeField, Min(0f)]
        [Tooltip(
            "How fast it can turn toward a target, in degrees per second. Low is a gentle drift, "
                + "high snaps onto the target. Keep it under a homing-missile to leave dodging in."
        )]
        float homingTurnRate = 360f;

        [SerializeField, Range(0f, 180f)]
        [Tooltip(
            "Only pulls toward targets within this many degrees of where it is already heading, so "
                + "a shot never boomerangs back at something behind it."
        )]
        float homingConeAngle = 120f;

        [Header("Range")]
        [SerializeField, Min(0f)]
        [Tooltip(
            "How far the shot travels before it gives out, in pixels. 0 means it flies until it "
                + "leaves the screen, which is what enemy fire wants. For the player it is the dial "
                + "that stops a fight being won from across the room: the encounter frame is 256 wide "
                + "and 144 tall, so a range under about 128 means you have to close on something to "
                + "kill it. Needs playing, not reasoning about."
        )]
        float maxRange;

        public Transform Target { get; private set; }
        Vector2 _direction;

        /// <summary>Distance flown this life, against maxRange. Reset per shot, since bullets are pooled.</summary>
        float _travelled;
        Hurtbox2D _hurtbox;
        float _spawnGracePeriod;

        ContactFilter2D _homingFilter;
        readonly List<Collider2D> _homingHits = new List<Collider2D>(16);

        // The gameplay camera, cached and shared across every live bullet. The offscreen despawn check
        // runs each frame per projectile, and Camera.main is a tagged-object lookup every call, so with
        // a screen full of shots it added up. Re-fetched automatically if the camera is torn down (a
        // destroyed camera reads as null here).
        static Camera _mainCamera;
        static Camera MainCamera => _mainCamera != null ? _mainCamera : (_mainCamera = Camera.main);

        protected override void Awake()
        {
            base.Awake();

            // A bullet is stopped by the combat frame through its hurtbox's wall check, which kills it
            // and plays the hit animation. Letting the motor contain it as well would leave live
            // bullets sliding along the invisible edge instead.
            if (motor != null)
                motor.SetContainedByCombatBoundary(false);

            _hurtbox = GetComponentInChildren<Hurtbox2D>();
            // _hurtbox.OnHitEntity += OnHitEntity;
            // _hurtbox.OnHitGeometry += OnHitGeometry;

            // Homing looks for hitboxes, which are triggers on the Hitbox layer. The team check in
            // the search is what keeps a shot pulling only toward the other side.
            _homingFilter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = true,
                layerMask = LayerMask.GetMask("Hitbox"),
            };
        }

        public override void OnCreated()
        {
            base.OnCreated();
            Init();
        }

        public override void OnAcquired()
        {
            base.OnAcquired();
            // Reset state
            _direction = Vector2.zero;
            m_moveInput = Vector2.zero;
            Target = null;
            _spawnGracePeriod = 0f;
            _travelled = 0f;

            // A hurtbox remembers what it has already hit and how many targets it has spent, so it
            // cannot hit the same body twice or exceed maxTargets. That memory is per-life: without
            // clearing it on reuse, a pooled bullet that hit one enemy comes back from the pool with
            // its hit count already spent, so it passes through everything and never dies. Clear it
            // here, when the bullet begins a new life.
            if (_hurtbox != null)
                _hurtbox.ResetTracking();
        }

        public override void OnReleased()
        {
            base.OnReleased();
            // Reset state
            _direction = Vector2.zero;
            m_moveInput = Vector2.zero;
            Target = null;
        }

        public void Shoot(Vector3 direction, Transform target = null)
        {
            _direction = direction.normalized;
            m_moveInput = _direction; // feeds into motor during PhysicsTick
            Target = target;
            _spawnGracePeriod = 0.1f; // 100ms grace period to avoid hitting owner
            _travelled = 0f;
            m_fsm.ChangeState((int)EntityState.Idle);
        }

        // -----------------------------
        // FSM States
        // -----------------------------
        protected override void StateIdle(Fsm.StateStep step, float deltaTime)
        {
            switch (step)
            {
                case Fsm.StateStep.Enter:
                    PlayAnimation(AnimConst.Idle);
                    break;

                case Fsm.StateStep.Update:
                    // Tick down grace period
                    if (_spawnGracePeriod > 0f)
                    {
                        _spawnGracePeriod -= deltaTime;
                    }

                    // Bend toward a nearby enemy before committing this frame's heading.
                    if (homing)
                        ApplyHoming(deltaTime);

                    // Always push in shoot direction
                    SetMoveDirection(_direction);

                    // Spent shots give out where they are rather than sailing on to the screen edge.
                    // Measured along the flight rather than as distance from the muzzle, so a homed
                    // shot that curves spends its range on the path it actually flew instead of being
                    // rewarded for taking a longer one.
                    if (maxRange > 0f)
                    {
                        _travelled += Velocity.magnitude * deltaTime;
                        if (_travelled >= maxRange)
                        {
                            m_fsm.ChangeState((int)EntityState.Dead);
                            break;
                        }
                    }

                    // Check despawn conditions
                    if (m_entityAnimator != null)
                    {
                        var activeAnimator = m_entityAnimator.GetActiveAnimator();
                        if (
                            activeAnimator != null
                            && IsOffscreen(activeAnimator.renderer, MainCamera)
                        )
                        {
                            m_fsm.ChangeState((int)EntityState.Dead);
                        }
                    }
                    break;
                case Fsm.StateStep.FixedUpdate:
                    // From the first frame, not after a grace period. The old 100ms blackout was
                    // meant to keep a shot from hitting its own shooter, but the team check already
                    // does that (a shot and its owner share a team, so IsOpposingTarget skips it),
                    // and the blackout let a target within ~20px of the muzzle be flown straight
                    // through: a bullet fired point-blank at a barrel, crate or stacked enemy passed
                    // clean through it (T-358). Checking from spawn is what makes contact land.
                    if (_hurtbox != null)
                    {
                        if (
                            _hurtbox.UpdateCollisions(
                                Position,
                                Velocity,
                                deltaTime,
                                out HitEvent hitEvent
                            )
                        )
                        {
                            m_fsm.ChangeState((int)EntityState.Dead);
                        }
                    }
                    break;
            }
        }

        protected override void StateDead(Fsm.StateStep step, float deltaTime)
        {
            switch (step)
            {
                case Fsm.StateStep.Enter:
                    SetMoveDirection(Vector2.zero);
                    PlayAnimation(AnimConst.Hit);
                    break;

                case Fsm.StateStep.Update:
                    if (m_entityAnimator && m_entityAnimator.IsDone)
                        Dispose();
                    break;
            }
        }

        // -----------------------------
        // Homing
        // -----------------------------

        /// <summary>
        /// Rotates the flight direction toward the nearest damageable enemy in range, by at most the
        /// turn rate this frame. The cap is the whole point: it curves the shot rather than teleports
        /// its aim, so a fast enough enemy can still slip a pull.
        /// </summary>
        void ApplyHoming(float deltaTime)
        {
            if (!TryFindHomingTarget(out Vector2 targetPosition))
                return;

            Vector2 toTarget = targetPosition - (Vector2)Position;
            if (toTarget.sqrMagnitude < 0.0001f)
                return;
            toTarget.Normalize();

            float maxRadians = homingTurnRate * Mathf.Deg2Rad * deltaTime;
            Vector2 steered = Vector3.RotateTowards(_direction, toTarget, maxRadians, 0f);

            // Only _direction needs updating; the caller pushes it into the motor via
            // SetMoveDirection on the very next line.
            _direction = steered.normalized;
        }

        /// <summary>
        /// Finds the nearest hitbox in range that this projectile could actually hurt and that sits
        /// within the homing cone. Skips its own team and anything invincible, so a shot never wastes
        /// its pull on a buried Whelp or on a friendly.
        /// </summary>
        bool TryFindHomingTarget(out Vector2 targetPosition)
        {
            targetPosition = default;

            _homingHits.Clear();
            int count = Physics2D.OverlapCircle(Position, homingRange, _homingFilter, _homingHits);
            if (count == 0)
                return false;

            Team ownTeam = _hurtbox != null ? _hurtbox.Team : Team.Player;
            float bestSqrDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < _homingHits.Count; i++)
            {
                Hitbox2D hitbox = _homingHits[i].GetComponent<Hitbox2D>();
                if (hitbox == null || hitbox.Team == ownTeam || hitbox.Invincible)
                    continue;

                Vector2 position = hitbox.transform.position;
                Vector2 toTarget = position - (Vector2)Position;

                if (Vector2.Angle(_direction, toTarget) > homingConeAngle)
                    continue;

                float sqrDistance = toTarget.sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    targetPosition = position;
                    found = true;
                }
            }

            return found;
        }

        protected override void UpdateAnimations(float deltaTime) { }
    }
}
