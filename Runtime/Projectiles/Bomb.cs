using Sirenix.OdinInspector;
using TakoBoyStudios.Core;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A lobbed explosive. Arcs to a spot, sits there burning a fuse, then goes off.
    ///
    /// Why this is not a Projectile: a projectile damages what it touches on the way, which is why
    /// every one has an `idle` for flight and a `hit` for impact. A bomb is the opposite. It is
    /// harmless in flight, dangerous only where it lands, and dangerous over an area rather than
    /// along a line. It also travels on an arc to a **point** rather than in a direction, so it
    /// passes over anything between the thrower and the target.
    ///
    /// The fuse is the fairness. A bomb that detonated on impact would be unreadable; the second
    /// it spends sitting on the ground is the player's window to leave, and the short `explode`
    /// animation at the end of it is the last warning before the blast lands.
    ///
    /// Bombs carry no damage box at all. Everything dangerous is spawned on detonation:
    ///
    /// - an **explosion** Effect, which owns the blast and its damage
    /// - optionally **shrapnel**, projectiles thrown outward in a ring
    ///
    /// That split is what makes variants cheap. A plain bomb sets an explosion. A cluster bomb adds
    /// shrapnel. A poison bomb swaps the explosion for one with a different profile. None of them
    /// need a new class.
    ///
    /// Shared, because more than one enemy throws bombs. ScaleskinBomber lobs them at the player,
    /// BoomBird drops them as it flies over.
    /// </summary>
    public class Bomb : Projectile
    {
        #region Inspector

        // World scale is 1 unit per pixel. The arena is 320x180.

        [BoxGroup("Arc")]
        [Tooltip("How high the throw peaks, in pixels. 0 drops it straight down with no arc, which is what a bird flying overhead wants.")]
        [SerializeField, MinValue(0f)]
        float arcHeight = 60f;

        [BoxGroup("Arc")]
        [Tooltip("Seconds in the air. Fixed rather than derived from distance, so the tell is always the same length regardless of range.")]
        [SerializeField, MinValue(0.05f)]
        float flightTime = 0.7f;

        [BoxGroup("Fuse")]
        [Tooltip("Seconds between landing and detonating. The player's window to move. Below about 0.4 it stops being readable.")]
        [SerializeField, MinValue(0f)]
        float fuseTime = 0.7f;

        [BoxGroup("Fuse")]
        [Tooltip(
            "Animation played once the fuse burns out, immediately before the blast spawns. The "
                + "artist drew the bomb igniting as the first frames of the explosion; those frames "
                + "live here so the explosion itself can be a generic fireball."
        )]
        [SerializeField]
        string explodeAnimation = "explode";

        [BoxGroup("Detonation")]
        [Tooltip("Explosion spawned on detonation. This is what actually hurts: the bomb has no damage box of its own.")]
        [SerializeField, Required]
        Effect explosionPrefab;

        [BoxGroup("Detonation")]
        [Tooltip("Pool size for explosions. At least as many bombs as can be in flight at once.")]
        [SerializeField, MinValue(1)]
        int explosionPoolSize = 8;

        [BoxGroup("Shrapnel")]
        [Tooltip("Optional projectiles flung outward on detonation. Leave empty for a plain bomb.")]
        [SerializeField]
        Projectile shrapnelPrefab;

        [BoxGroup("Shrapnel")]
        [Tooltip("How many pieces fly out, spread evenly around a full circle.")]
        [SerializeField, MinValue(0)]
        int shrapnelCount = 8;

        [BoxGroup("Shrapnel")]
        [Tooltip("Rotation of the shrapnel ring in degrees, so two bombs do not throw identical patterns.")]
        [SerializeField]
        float shrapnelAngle;

        [BoxGroup("Shrapnel")]
        [Tooltip("Pool size for shrapnel. Needs to cover several bombs detonating together.")]
        [SerializeField, MinValue(1)]
        int shrapnelPoolSize = 32;

        #endregion

        #region Runtime State

        float _fuse;

        /// <summary>
        /// Whether the throw has actually lifted it. A bomb begins its life standing on the floor, so
        /// without this it counts as landed on the frame it is created and a short fuse detonates it at
        /// the thrower's feet before it has gone anywhere. Harmless at 0.7s because the arc is well
        /// clear by then; fatal at 0.
        /// </summary>
        bool _leftGround;

        bool _landed;
        bool _igniting;
        bool _detonated;

        #endregion

        #region Lifecycle

        public override void Init()
        {
            base.Init();

            CreatePool(explosionPrefab != null ? explosionPrefab.gameObject : null, explosionPoolSize);
            CreatePool(shrapnelPrefab != null ? shrapnelPrefab.gameObject : null, shrapnelPoolSize);
        }

        public override void OnAcquired()
        {
            base.OnAcquired();

            _fuse = fuseTime;
            _leftGround = false;
            _landed = false;
            _igniting = false;
            _detonated = false;
        }

        static void CreatePool(GameObject prefab, int size)
        {
            if (prefab == null || PoolManager.Instance == null)
                return;

            // Fixed size and pre-warmed, so a detonation never allocates.
            PoolManager.Instance.CreatePool(
                prefab.name,
                prefab,
                new PoolConfig(size, size, grow: 0, autoGrow: false)
            );
        }

        #endregion

        #region Throwing

        /// <summary>
        /// Lobs the bomb at a world position. Use this instead of Shoot: a bomb travels to a spot
        /// on an arc, not off in a direction, which is what lets it clear cover.
        ///
        /// The horizontal speed is derived from the distance and the flight time so it lands where
        /// it was aimed, however far that is.
        /// </summary>
        public void Lob(Vector2 target)
        {
            Vector2 delta = target - (Vector2)Position;
            float distance = delta.magnitude;

            if (distance > 0.001f)
            {
                Vector2 direction = delta / distance;
                Shoot(direction);

                // Shoot sets the projectile moving at its own speed; override so the horizontal
                // travel matches the airtime and it actually arrives on target.
                m_moveSpeed = distance / flightTime;
                SetMoveDirection(direction);
            }
            else
            {
                SetMoveDirection(Vector2.zero);
            }

            if (motor != null && arcHeight > 0f)
            {
                motor.LaunchArc(arcHeight, flightTime);
                return;
            }

            // Released rather than thrown. With no arc there is nothing to leave the ground, so the
            // "it has to go up first" latch in the fuse would never open and the bomb would sit
            // inert forever, harmless and permanent. The arc height tooltip offers 0 as the way a
            // bird drops one, so it has to actually work: a released bomb is already where it lands,
            // and its fuse starts now.
            _leftGround = true;
        }

        #endregion

        #region Fuse

        protected override void StateIdle(Fsm.StateStep step, float deltaTime)
        {
            base.StateIdle(step, deltaTime);

            if (step != Fsm.StateStep.Update || _detonated)
                return;

            // Landing is a physical event, so the bomb settles exactly when it visibly touches
            // down however long the arc took. But it has to have gone up first: a bomb is created
            // standing on the floor, so without the latch below it lands on the frame it is thrown.
            if (!_landed)
            {
                if (!Grounded)
                {
                    _leftGround = true;
                    return;
                }

                if (!_leftGround)
                    return;

                _landed = true;
            }

            // Once down it stays put. The base Projectile tick re-applies its travel direction every
            // frame, so a single stop on the landing frame is not enough: without pinning it here the
            // bomb keeps sliding along its throw line through the whole fuse.
            SetMoveDirection(Vector2.zero);

            // Igniting: the bomb plays its own last two frames, then the blast takes over.
            if (_igniting)
            {
                if (m_entityAnimator == null || m_entityAnimator.IsDone)
                    Detonate();

                return;
            }

            _fuse -= deltaTime;

            if (_fuse <= 0f)
                Ignite();
        }

        /// <summary>
        /// Starts the ignition animation. Detonation waits for it to finish, so the blast always
        /// lands on the frame the bomb visibly goes up rather than a fixed time later.
        /// </summary>
        void Ignite()
        {
            _igniting = true;

            if (m_entityAnimator != null && !string.IsNullOrEmpty(explodeAnimation))
            {
                if (m_entityAnimator.HasAnimation(explodeAnimation))
                    m_entityAnimator.Play(explodeAnimation);
                else
                    Detonate();
            }
            else
            {
                Detonate();
            }
        }

        void Detonate()
        {
            if (_detonated)
                return;

            _detonated = true;

            SpawnExplosion();
            SpawnShrapnel();

            Dispose();
        }

        void SpawnExplosion()
        {
            if (explosionPrefab == null || PoolManager.Instance == null)
                return;

            GameObject blast = PoolManager.Instance.Acquire(explosionPrefab.name, Position);
            if (blast == null)
            {
                Debug.LogWarning($"{name}: explosion pool '{explosionPrefab.name}' is empty.");
                return;
            }

            // The blast owns the damage, so carry the bomb's attribution onto it: a kill by this
            // explosion is credited to whoever dropped the bomb, not to the explosion (T-328).
            Entity blastEntity = blast.GetComponent<Entity>();
            if (blastEntity != null)
                blastEntity.Instigator = RootInstigator;
        }

        void SpawnShrapnel()
        {
            if (shrapnelPrefab == null || shrapnelCount <= 0 || PoolManager.Instance == null)
                return;

            float step = 360f / shrapnelCount;

            for (int i = 0; i < shrapnelCount; i++)
            {
                float radians = (shrapnelAngle + step * i) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));

                GameObject piece = PoolManager.Instance.Acquire(shrapnelPrefab.name, Position);

                if (piece == null)
                {
                    Debug.LogWarning($"{name}: shrapnel pool '{shrapnelPrefab.name}' is empty.");
                    return;
                }

                Projectile projectile = piece.GetComponent<Projectile>();
                if (projectile != null)
                {
                    projectile.Instigator = RootInstigator; // shrapnel keeps the bomb's attribution (T-328)
                    projectile.Shoot(direction);
                }
            }
        }

        #endregion
    }
}
