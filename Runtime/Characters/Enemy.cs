using Sirenix.OdinInspector;
using TakoBoyStudios.Core;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Base class for all enemies.
    ///
    /// Each enemy is its own class with its own state enum, so reading the file tells you what the
    /// enemy does. This base provides the shared maths those classes call: movement, animation,
    /// projectiles, contact damage. It never decides anything itself.
    ///
    /// The distinction that matters: there is no "movement type" field dispatched in Update. An
    /// enemy calls Chase() on the line where chasing makes sense, so the behaviour stays visible in
    /// the enemy's own file and only the arithmetic is shared.
    ///
    /// To make an enemy: derive, add a State enum, implement UpdateEnemy.
    /// </summary>
    public abstract class Enemy : Character
    {
        #region Inspector

        [BoxGroup("References")]
        [Tooltip("The player. Found automatically by tag on Init if left empty.")]
        [SerializeField]
        Entity player;

        [BoxGroup("Contact Damage")]
        [Tooltip(
            "Hurtbox that damages the player on touch. Leave empty for enemies that are harmless "
                + "to walk into. Set it deliberately: a rusher has one, a gunner usually does not. "
                + "Declare [ContactDamage] on the class and the scaffolder fills this in. For a weapon swing use [AttackHurtbox] instead."
        )]
        [SerializeField]
        Hurtbox2D contactHurtbox;

        #endregion

        #region Runtime State

        bool _contactDamageEnabled = true;

        // The damage tell. Recolours the body for a moment on every hit without touching its state
        // machine or its animation, so a charger keeps charging while it visibly takes the shot.
        readonly HitFlash _hitFlash = new();

        #endregion

        #region Properties

        public Entity Player => player;
        public bool HasPlayer => player != null;

        /// <summary>
        /// The EnemyRegistry id this body was spawned as (T-325). Stamped by every spawn path
        /// right after the pool hands the body over, so a pooled body can never carry a stale id.
        /// Scoring reads it to price the kill. Empty on an enemy placed straight into a scene,
        /// which is a lab and editor situation, never a room one.
        /// </summary>
        public string RegistryId { get; set; }
        public float HealthPercent => m_hp > 0 ? (float)m_hp / m_maxHp : 0f;

        /// <summary>Distance to the player, or float.MaxValue if there is no player.</summary>
        public float DistanceToPlayer =>
            player != null ? Vector2.Distance(Position, player.Position) : float.MaxValue;

        /// <summary>Normalised direction to the player, or zero if there is no player.</summary>
        public Vector2 DirectionToPlayer =>
            player != null
                ? ((Vector2)player.Position - (Vector2)Position).normalized
                : Vector2.zero;

        static int _geometryMask = -1;
        static int GeometryMask => _geometryMask >= 0 ? _geometryMask : (_geometryMask = LayerMask.GetMask("Geometry"));

        /// <summary>
        /// True when the player is within range and no wall sits between here and there. This is the
        /// eye test a turret or a charger uses: it can act only on what it can actually see.
        /// </summary>
        protected bool HasLineOfSightToPlayer(float maxDistance)
        {
            if (player == null)
                return false;

            Vector2 origin = Position;
            Vector2 toPlayer = (Vector2)player.Position - origin;
            float distance = toPlayer.magnitude;
            if (distance > maxDistance || distance < 0.0001f)
                return distance <= maxDistance;

            return Physics2D.Raycast(origin, toPlayer / distance, distance, GeometryMask).collider == null;
        }

        /// <summary>
        /// Deflects a desired heading around a wall in front of it, with two whiskers. Cheap and
        /// local, not a path: it turns a body that would walk into a wall so it slides around the
        /// near edge instead of pressing into it. Enough for a pursuer in a convex room; a true
        /// route around complex geometry would want a nav graph.
        /// </summary>
        protected Vector2 SteerAround(Vector2 desired, float lookAhead)
        {
            if (desired.sqrMagnitude < 0.0001f)
                return desired;

            desired.Normalize();
            if (Physics2D.Raycast(Position, desired, lookAhead, GeometryMask).collider == null)
                return desired;

            Vector2 left = new Vector2(-desired.y, desired.x);
            Vector2 leftHeading = (desired + left).normalized;
            Vector2 rightHeading = (desired - left).normalized;

            bool leftClear = Physics2D.Raycast(Position, leftHeading, lookAhead, GeometryMask).collider == null;
            bool rightClear = Physics2D.Raycast(Position, rightHeading, lookAhead, GeometryMask).collider == null;

            if (leftClear && !rightClear)
                return leftHeading;
            if (rightClear && !leftClear)
                return rightHeading;

            // Both or neither clear: peel off along the wall rather than grind into it.
            return leftClear ? left : -left;
        }

        /// <summary>
        /// The movement input this frame. Entity keeps it protected, but enemies routinely need to
        /// know whether they are currently moving in order to pick between walk and idle.
        /// </summary>
        public Vector2 MoveDirection => m_moveInput;

        /// <summary>True if the enemy is moving under its own power this frame.</summary>
        public bool IsMoving => m_moveInput.sqrMagnitude > 0.0001f;

        /// <summary>Current frame of the playing animation. Use to time actions to the art.</summary>
        public int AnimationFrame => m_entityAnimator != null ? m_entityAnimator.CurrentFrame : 0;

        /// <summary>True once the current animation has finished playing.</summary>
        public bool AnimationDone => m_entityAnimator == null || m_entityAnimator.IsDone;

        #endregion

        #region Initialization

        public override void Init()
        {
            base.Init();

            _hitFlash.Bind(character);
            OnTakeDamage -= OnHitForFlash;
            OnTakeDamage += OnHitForFlash;

            if (player == null)
            {
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                    player = playerObj.GetComponent<Entity>();

                if (player == null)
                    Debug.LogWarning($"{name} could not find a player. Tag one 'Player'.");
            }
        }

        public override void OnAcquired()
        {
            base.OnAcquired();

            // A fresh life from the pool is vulnerable and hurts to touch again. Death, and a
            // burrower's cycle, had turned those off. An enemy that gates them per state (SandWhelp)
            // sets them again in its own OnAcquired, which runs after this.
            _contactDamageEnabled = true;
            if (m_hitboxes != null)
            {
                SetVulnerable(true);
                SetTangible(true);
            }

            // A body from the pool may be mid-flash from its last life.
            _hitFlash.Bind(character);
        }

        void OnHitForFlash(HitEvent hitEvent) => _hitFlash.Play();

        /// <summary>
        /// Dropping out of combat stops the body dead so a cleared encounter does not leave a survivor
        /// coasting on its last heading. Entering combat is left to the enemy's own first tick.
        /// </summary>
        protected override void OnCombatStateChanged(bool inCombat)
        {
            if (!inCombat)
                StopMoving();
        }

        #endregion

        #region Tick

        protected override bool Tick(float deltaTime)
        {
            // Outside base.Tick because the flash has to keep running through hit lag and death: a
            // killing blow should still flash, and hit lag is exactly when the player is looking.
            _hitFlash.Tick(deltaTime);

            if (!base.Tick(deltaTime))
                return false;

            if (IsDead)
                return false;

            // Out of combat the enemy's own state machine does not run at all, so an enemy standing in
            // a room before its CamLock fires never chases, never shoots, and never advances whatever
            // internal timer it uses. It simply waits. This is the whole of the V1 gating: it is a
            // clean stop rather than a half-active enemy, and it costs the hand-authored enemy classes
            // nothing.
            if (!InCombat)
            {
                UpdateOutOfCombat(deltaTime);
                return true;
            }

            UpdateEnemy(deltaTime);
            return true;
        }

        /// <summary>
        /// True while a spawn presentation is walking this enemy into an encounter from off screen.
        /// Out of combat the enemy normally plants itself; this says "something else is steering me,
        /// leave my movement alone". Set and cleared by the wave runner, never by the enemy.
        /// </summary>
        public bool EnteringEncounter { get; set; }

        /// <summary>
        /// True when this enemy was put into the room rather than conjured into it, so it must not
        /// play whatever arrival animation it owns.
        ///
        /// Several enemies open on a spawn, emerge or summon clip. That is right for something that
        /// materialises out of nothing and wrong for something the player has already watched arrive.
        ///
        /// A wave does all three. It places its first enemies in the room before the fight, where the
        /// player walks up and looks at them; it walks later ones in from off screen; and it also
        /// telegraphs a point with a spawn marker and puts an enemy there out of nothing. Only the
        /// first two are placements. The third is a conjuring and keeps its animation, which is the
        /// whole reason this is a flag rather than an assumption about waves.
        ///
        /// Set by whatever placed the enemy. Defaults to false, so anything that does not think about
        /// it keeps its arrival.
        /// </summary>
        public bool PlacedInRoom { get; set; }

        /// <summary>
        /// What the enemy does while its encounter has not started, or after it has ended. Standing
        /// still by default. Override for an actor that should idle, patrol, talk or sleep out of
        /// combat; keep it free of anything that damages the player.
        /// </summary>
        protected virtual void UpdateOutOfCombat(float deltaTime)
        {
            if (!EnteringEncounter)
            {
                StopMoving();

                // Waiting, not frozen. Most enemies own only directional clips, so the base animator's
                // non-directional idle matches nothing and the sprite sits on whatever single frame it
                // happened to stop on. This keeps a standing enemy breathing before its fight starts.
                PlayDirectional(AnimConst.Idle);
                return;
            }

            // Being walked in from off screen. The heading is set by the spawn presentation; all this
            // does is face the body the right way and put a walk clip on it. Both names are tried
            // because the roster is split between them, and PlaySimpleAnimation no-ops on a clip the
            // enemy does not own, so an enemy with neither simply keeps its idle.
            if (!IsMoving)
                return;

            FacingDirection = MoveDirection.normalized;
            PlayDirectional("walk");
            PlayDirectional(AnimConst.Move);
        }

        protected override void PhysicsTick(float fixedDeltaTime)
        {
            base.PhysicsTick(fixedDeltaTime);

            // Contact damage is for creatures that hurt to touch, and is centred on the body. An
            // attack that reaches out in front is not this: see Strike().
            //
            // The hurtbox should have HitMultipleTimes set so it does not latch onto the player
            // after one touch; the player's own i-frames govern how often it can land.
            if (
                !IsDead
                && InCombat
                && _contactDamageEnabled
                && CombatGate.AttacksAllowed
                && contactHurtbox != null
            )
                contactHurtbox.CheckArea(contactHurtbox.transform.position);
        }

        /// <summary>
        /// Where the enemy's own logic lives. Called once per frame while alive and not hit lagged.
        /// Drive state, movement and animation from here.
        /// </summary>
        protected abstract void UpdateEnemy(float deltaTime);

        /// <summary>
        /// Turns contact damage on and off, for enemies that only hurt during a charge or lunge.
        /// Does nothing if the enemy has no contact hurtbox.
        /// </summary>
        protected void SetContactDamage(bool enabled)
        {
            _contactDamageEnabled = enabled;
        }

        #endregion

        #region Attack Helpers

        /// <summary>
        /// Swings the box for a direction, damaging whatever overlaps it this frame.
        ///
        /// Call every frame the attack animation is connecting. The box hits each target once per
        /// swing, so calling it across a three frame window lands one hit, not three. Call
        /// ResetStrike when a new swing starts, or the second swing hits nothing.
        ///
        /// The box's position comes from where it was placed by hand for that direction, not from
        /// any offset computed here. A swing lands somewhere different depending on which way it
        /// faces, and only the art knows where.
        /// </summary>
        protected void Strike(DirectionalHurtbox boxes, Vector2 direction)
        {
            if (boxes == null)
                return;

            Hurtbox2D box = boxes.Get(direction);

            if (box != null)
                box.CheckArea(box.transform.position);
        }

        /// <summary>
        /// Snaps a direction to the nearest cardinal.
        ///
        /// For an enemy whose attack art is three sheets (n, e, s, with west mirroring east), the
        /// animator has nothing to play for a diagonal and silently falls back to the south swing.
        /// Snapping first means the sprite and the damage box always agree on which way the enemy
        /// is swinging. Enemies drawn with full eight way attack art should not call this.
        /// </summary>
        protected static Vector2 SnapToCardinal(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return Vector2.down;

            return Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
                ? new Vector2(Mathf.Sign(direction.x), 0f)
                : new Vector2(0f, Mathf.Sign(direction.y));
        }

        /// <summary>
        /// Snaps a direction to left or right.
        ///
        /// For an enemy drawn side on only, like a slime with a single walk-e sheet. Facing north
        /// would find no animation at all: the animator tries the exact direction, then the mirror
        /// (which does not exist for n or s), then falls back to "-s", which a side on enemy also
        /// does not have. Snapping first keeps it on the one sheet it owns, mirrored for west.
        /// </summary>
        protected static Vector2 SnapToHorizontal(Vector2 direction)
        {
            return direction.x < 0f ? Vector2.left : Vector2.right;
        }

        /// <summary>
        /// Clears what an attack's boxes have already hit, so the next swing can connect again.
        /// Call when an attack begins, not when it ends, so an interrupted attack still resets.
        /// </summary>
        protected void ResetStrike(DirectionalHurtbox boxes)
        {
            if (boxes != null)
                boxes.ResetTracking();
        }

        #endregion

        #region Vulnerability

        /// <summary>
        /// Opens or closes the enemy's window of being damageable.
        ///
        /// This is one of the two levers that make contact damage enemies interesting without
        /// giving them attacks. A burrower is untouchable underground and vulnerable while it is
        /// up; an armoured charger is only open from behind; a shelled creature opens between
        /// swipes. Without it every enemy is just a health bar that walks.
        /// </summary>
        protected void SetVulnerable(bool vulnerable)
        {
            // Can be called from OnAcquired, which the pool runs before Init has gathered the hitboxes.
            // The enemy re-applies its state once Init has run, so skipping here is safe.
            if (m_hitboxes == null)
                return;

            for (int i = 0; i < m_hitboxes.Count; i++)
                m_hitboxes[i].SetInvincible(!vulnerable);
        }

        /// <summary>
        /// Makes the enemy's hitboxes solid or phased. A phased enemy is not there as far as incoming
        /// shots and melee are concerned: they pass straight through rather than dying on it. Use it
        /// for a burrower underground, so bullets do not visibly stop on nothing. Distinct from
        /// SetVulnerable, which keeps the body present and blocking while it shrugs off damage.
        /// </summary>
        protected void SetTangible(bool tangible)
        {
            if (m_hitboxes == null)
                return;

            for (int i = 0; i < m_hitboxes.Count; i++)
                m_hitboxes[i].SetPhased(!tangible);
        }

        #endregion

        #region Death Effects

        /// <summary>
        /// Spawns something from the pool at this enemy's position. For death effects: splitting
        /// into smaller enemies, bursting into projectiles, releasing a swarm.
        ///
        /// The other lever worth leaning on. It costs almost nothing to build and it changes where
        /// the player wants to kill things, which is positional pressure without any new AI.
        ///
        /// Call from an overridden Die(), before base.Die().
        /// </summary>
        protected GameObject SpawnFromPool(GameObject prefab, Vector2 offset = default)
        {
            if (prefab == null || PoolManager.Instance == null)
                return null;

            GameObject spawned = PoolManager.Instance.Acquire(
                prefab.name,
                (Vector2)Position + offset
            );

            if (spawned == null)
                Debug.LogWarning($"{name} could not acquire '{prefab.name}'. Is the pool created?");

            return spawned;
        }

        /// <summary>
        /// Creates a pool for anything this enemy spawns. Call from Init.
        /// Fixed size and pre-warmed, for the same reason as CreateProjectilePool.
        /// </summary>
        protected void CreatePool(GameObject prefab, int size)
        {
            if (prefab == null || PoolManager.Instance == null)
                return;

            PoolManager.Instance.CreatePool(
                prefab.name,
                prefab,
                new PoolConfig(size, size, grow: 0, autoGrow: false)
            );
        }

        #endregion

        #region Movement Helpers

        /// <summary>
        /// Moves in a direction at an absolute speed in world units per second.
        /// Everything else here funnels through this.
        /// </summary>
        protected void Move(Vector2 direction, float speed)
        {
            if (direction.sqrMagnitude < 0.0001f || m_moveSpeed <= 0.0001f)
            {
                StopMoving();
                return;
            }

            // Entity applies m_moveInput * m_moveSpeed, so scale to hit the requested speed.
            SetMoveDirection(direction.normalized * (speed / m_moveSpeed));
        }

        protected void StopMoving()
        {
            SetMoveDirection(Vector2.zero);
        }

        /// <summary>
        /// Moves at the player, around the room rather than into it.
        ///
        /// It used to head straight there and let the motor stop it, which reads as intent until a
        /// wall is in the way and then reads as an animal walking into glass. With a nav grid it
        /// walks the route; without one it steers with the whiskers as before, which is what keeps
        /// the lab and any room with no grid behaving exactly as they did.
        /// </summary>
        protected void Chase(float speed)
        {
            Move(ChaseHeading(), speed);
        }

        /// <summary>The direction to walk to reach the player. Public-ish so a state machine can face it.</summary>
        protected Vector2 ChaseHeading()
        {
            Vector2 direct = DirectionToPlayer;
            if (player == null)
                return direct;

            _route ??= new PathFollower();

            if (Nav.Grid != null
                && _route.TryHeading(Nav.Grid, Position, player.Position, Time.deltaTime, out Vector2 routed))
            {
                return routed;
            }

            return SteerAround(direct, ChaseLookAhead);
        }

        /// <summary>How far ahead the fallback whiskers look, in pixels. About a body and a half.</summary>
        const float ChaseLookAhead = 24f;

        PathFollower _route;

        /// <summary>
        /// Moves straight at a world position, stopping once within stopDistance.
        /// Distances are in pixels: the project runs at 1 unit per pixel, arena 320x180.
        /// </summary>
        protected void MoveToward(Vector2 position, float speed, float stopDistance = 2f)
        {
            Vector2 delta = position - (Vector2)Position;

            if (delta.magnitude <= stopDistance)
            {
                StopMoving();
                return;
            }

            Move(delta, speed);
        }

        /// <summary>Moves directly away from the player.</summary>
        protected void Flee(float speed)
        {
            Move(-DirectionToPlayer, speed);
        }

        /// <summary>
        /// Holds a band of distance from the player: closes if further than max, backs off if
        /// closer than min, stands still in between. The standard ranged-enemy footwork.
        /// </summary>
        protected void KeepDistance(float min, float max, float speed)
        {
            float distance = DistanceToPlayer;

            if (distance > max)
                Chase(speed);
            else if (distance < min)
                Flee(speed);
            else
                StopMoving();
        }

        /// <summary>
        /// Circles the player at a radius, correcting inward or outward as it goes.
        /// Positive degreesPerSecond goes anticlockwise.
        /// </summary>
        protected void Orbit(float radius, float degreesPerSecond, float speed)
        {
            if (player == null)
            {
                StopMoving();
                return;
            }

            Vector2 toPlayer = (Vector2)player.Position - (Vector2)Position;
            float distance = toPlayer.magnitude;

            if (distance < 0.0001f)
            {
                StopMoving();
                return;
            }

            Vector2 inward = toPlayer / distance;
            Vector2 tangent = new Vector2(-inward.y, inward.x) * Mathf.Sign(degreesPerSecond);

            // Blend the tangent with a correction toward the target radius. The further off the
            // radius, the more of the movement goes into fixing it.
            float radiusError = Mathf.Clamp((distance - radius) / Mathf.Max(radius, 0.0001f), -1f, 1f);
            Vector2 direction = (tangent + inward * radiusError).normalized;

            Move(direction, speed);
        }

        #endregion

        #region Animation Helpers

        /// <summary>
        /// Plays an animation with the direction suffix for the way the enemy is facing, for
        /// example PlayDirectional("walk") becomes "walk-ne". Falls back through the mirrored
        /// direction and then "-s" if the exact one does not exist.
        /// </summary>
        protected void PlayDirectional(string baseName)
        {
            if (m_entityAnimator == null)
                return;

            m_entityAnimator.PlaySimpleAnimation(baseName, FacingDirection);
        }

        /// <summary>
        /// Plays an animation with the direction suffix for an explicit direction, for when the
        /// enemy should face its target rather than its movement.
        /// </summary>
        protected void PlayDirectional(string baseName, Vector2 direction)
        {
            if (m_entityAnimator == null)
                return;

            m_entityAnimator.PlaySimpleAnimation(baseName, direction);
        }

        #endregion

        #region Projectile Helpers

        /// <summary>
        /// Creates a pool for a projectile. Call from Init for everything the enemy can spawn.
        /// Size it to the peak number alive at once across the whole room.
        ///
        /// Fixed size and pre-warmed: the pool will not grow during play, so nothing allocates
        /// mid-fight. If it runs dry, FireProjectile logs it rather than quietly allocating, which
        /// makes an undersized pool a thing you find in playtesting instead of a frame spike you
        /// find in a profiler.
        /// </summary>
        protected void CreateProjectilePool(Projectile prefab, int size)
        {
            if (prefab == null || PoolManager.Instance == null)
                return;

            PoolManager.Instance.CreatePool(
                prefab.name,
                prefab.gameObject,
                new PoolConfig(size, size, grow: 0, autoGrow: false)
            );
        }

        /// <summary>
        /// Fires one projectile from an offset, in a direction.
        /// </summary>
        protected Projectile FireProjectile(Projectile prefab, Vector2 direction, Vector2 offset = default)
        {
            // Held off during the lab's entry grace. FireSpread and FireRadial route through here, so
            // this one gate covers every straight shot.
            if (!CombatGate.AttacksAllowed)
                return null;

            if (prefab == null)
            {
                Debug.LogWarning($"{name} tried to fire a null projectile");
                return null;
            }

            if (PoolManager.Instance == null)
            {
                Debug.LogWarning($"{name} cannot fire, there is no PoolManager");
                return null;
            }

            Vector3 spawnPosition = (Vector2)Position + offset;
            GameObject obj = PoolManager.Instance.Acquire(prefab.name, spawnPosition);

            if (obj == null)
            {
                Debug.LogWarning($"{name} could not acquire '{prefab.name}'. Is the pool created?");
                return null;
            }

            Projectile projectile = obj.GetComponent<Projectile>();
            if (projectile == null)
            {
                Debug.LogWarning($"'{prefab.name}' has no Projectile component");
                return null;
            }

            // Credited to this enemy, so a kill by its shot is attributed to it and not to the bullet
            // (T-328). Set after Acquire, whose OnAcquired cleared the pooled body's last instigator.
            projectile.Instigator = this;

            // Projectile has its own spawn grace period so it cannot hit whoever fired it.
            projectile.Shoot(direction.normalized);
            return projectile;
        }

        /// <summary>
        /// Lobs a bomb at a world position.
        ///
        /// Separate from FireProjectile because a bomb travels to a spot on an arc rather than off
        /// in a direction, which is what lets it clear cover. Aim at where the player is standing,
        /// not at the player.
        /// </summary>
        protected Bomb LobBomb(Bomb prefab, Vector2 target)
        {
            // Held off during the lab's entry grace, same as straight shots.
            if (!CombatGate.AttacksAllowed)
                return null;

            if (prefab == null)
            {
                Debug.LogWarning($"{name} tried to lob a null bomb");
                return null;
            }

            if (PoolManager.Instance == null)
                return null;

            GameObject obj = PoolManager.Instance.Acquire(prefab.name, Position);

            if (obj == null)
            {
                Debug.LogWarning($"{name} could not acquire '{prefab.name}'. Is the pool created?");
                return null;
            }

            Bomb bomb = obj.GetComponent<Bomb>();

            if (bomb == null)
            {
                Debug.LogWarning($"'{prefab.name}' has no Bomb component");
                return null;
            }

            // The bomb, and by propagation its explosion, is credited to this enemy (T-328).
            bomb.Instigator = this;

            bomb.Lob(target);
            return bomb;
        }

        /// <summary>
        /// Creates a pool for a bomb. Same fixed-size, pre-warmed rules as projectiles.
        /// </summary>
        protected void CreateBombPool(Bomb prefab, int size)
        {
            if (prefab == null || PoolManager.Instance == null)
                return;

            PoolManager.Instance.CreatePool(
                prefab.name,
                prefab.gameObject,
                new PoolConfig(size, size, grow: 0, autoGrow: false)
            );
        }

        /// <summary>
        /// Fires a fan of projectiles centred on a direction. A spread of 0 fires them all along
        /// the same line, which is only useful with a count of 1.
        /// </summary>
        protected void FireSpread(
            Projectile prefab,
            Vector2 direction,
            int count,
            float totalSpreadDegrees,
            Vector2 offset = default
        )
        {
            if (count <= 0)
                return;

            if (count == 1)
            {
                FireProjectile(prefab, direction, offset);
                return;
            }

            float step = totalSpreadDegrees / (count - 1);
            float start = -totalSpreadDegrees * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float angle = start + step * i;
                Vector2 rotated = Rotate(direction, angle);
                FireProjectile(prefab, rotated, offset);
            }
        }

        /// <summary>
        /// Fires projectiles evenly around a full circle. Radial burst patterns.
        /// </summary>
        protected void FireRadial(Projectile prefab, int count, float startAngleDegrees = 0f, Vector2 offset = default)
        {
            if (count <= 0)
                return;

            float step = 360f / count;

            for (int i = 0; i < count; i++)
            {
                Vector2 direction = Rotate(Vector2.right, startAngleDegrees + step * i);
                FireProjectile(prefab, direction, offset);
            }
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        #endregion

        #region Death

        /// <summary>
        /// Reports the kill to scoring on lethal damage (T-325). Here and not in Die(), because
        /// Die() is also the removal path: a CamLock sealing out an awake enemy and room teardown
        /// both call it, and removals must not score. Only damage that actually emptied the health
        /// bar counts as a kill.
        /// </summary>
        public override void DealDamage(HitEvent hitEvent)
        {
            bool wasAlive = Health > 0 && !IsDead;
            base.DealDamage(hitEvent);

            if (wasAlive && Health <= 0)
                EntityEvents.ReportEnemyKilled(this, RegistryId, Position, hitEvent.damageInfo.source);
        }

        public override void Die()
        {
            if (IsDead)
                return;

            _contactDamageEnabled = false;
            StopMoving();
            base.Die();
        }

        #endregion

#if UNITY_EDITOR

        #region Editor Setup

        /// <summary>
        /// Builds a contact damage HurtBox on this enemy and wires it to contactHurtbox.
        ///
        /// The scaffolder only builds a contact box for enemies that declare [ContactDamage], and
        /// only when the prefab is first created. This is the escape hatch for the other case: an
        /// enemy whose prefab already exists and needs a contact box added by hand during setup,
        /// like a burrower that gains one late. It produces the same box the scaffolder would: a
        /// trigger on the body, HitMultipleTimes so the player's own i-frames govern the cadence,
        /// Enemy team, Player and Enemy targets, and its damage filled in from the class's own
        /// [ContactDamage] attribute so the box is not silently harmless.
        ///
        /// It reads the class's [ContactDamage] attribute for size, damage and knockback if it
        /// declares one, and falls back to the attribute's own defaults if it does not. Open the
        /// prefab before clicking, so the box is saved into the prefab rather than a scene instance.
        /// </summary>
        [BoxGroup("Contact Damage")]
        [PropertyTooltip(
            "Adds a HurtBox child and wires it, for an enemy that was built without one. Open the "
                + "prefab first so it saves into the prefab."
        )]
        [Button("Create Contact Hurtbox", ButtonSizes.Medium)]
        [ShowIf("@this.contactHurtbox == null")]
        void CreateContactHurtbox()
        {
            if (contactHurtbox != null)
                return;

            ContactDamageAttribute contact =
                (ContactDamageAttribute)
                    System.Attribute.GetCustomAttribute(GetType(), typeof(ContactDamageAttribute))
                ?? new ContactDamageAttribute();

            GameObject hurtBoxObject = new GameObject("HurtBox");
            UnityEditor.Undo.RegisterCreatedObjectUndo(hurtBoxObject, "Create Contact Hurtbox");
            hurtBoxObject.transform.SetParent(transform, false);
            hurtBoxObject.transform.localPosition = new Vector3(0f, contact.OffsetY, 0f);

            // A hurtbox lives on the Hurtbox layer, not the body's layer, so movement masks never
            // catch it. Falls back to the body layer only if the project is missing the layer.
            int hurtboxLayer = LayerMask.NameToLayer("Hurtbox");
            hurtBoxObject.layer = hurtboxLayer >= 0 ? hurtboxLayer : gameObject.layer;

            BoxCollider2D collider = hurtBoxObject.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(contact.Width, contact.Height);
            collider.isTrigger = true;

            Hurtbox2D hurtbox = hurtBoxObject.AddComponent<Hurtbox2D>();

            UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(hurtbox);
            so.FindProperty("size").vector3Value = new Vector3(contact.Width, contact.Height, 0f);

            // Without HitMultipleTimes the box latches onto the player after one touch and never
            // hits again. The player's own i-frames are what should govern the cadence.
            UnityEditor.SerializedProperty behavior = so.FindProperty("behavior");
            behavior.intValue |= (int)DamageBoxBehavior.HitMultipleTimes;

            so.FindProperty("team").intValue = (int)Team.Enemy;

            // A hurtbox with no target layers silently hits nothing. It targets the Hitbox layer;
            // the Enemy team set above is what keeps it from hitting other enemies.
            so.FindProperty("targetLayers").intValue = LayerMask.GetMask("Hitbox");

            // Straight onto the box, from the class's own [ContactDamage] attribute. There is no
            // asset to create and therefore none to forget to link.
            so.FindProperty("damage.damage").intValue = contact.Damage;
            so.FindProperty("damage.knockback").floatValue = contact.Knockback;
            so.FindProperty("damage.shakeIntensity").floatValue = 0.1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            UnityEditor.Undo.RecordObject(this, "Create Contact Hurtbox");
            contactHurtbox = hurtbox;
            UnityEditor.EditorUtility.SetDirty(this);

            Debug.Log($"[Enemy] Added a contact HurtBox to {name}.", this);
        }


        #endregion

#endif

        #region Debug

        public override string GetDebugInfo()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(base.GetDebugInfo());

            sb.AppendLine("──────────────────────");
            sb.AppendLine("<b>ENEMY</b>");
            sb.AppendLine($"HP: {m_hp}/{m_maxHp} ({HealthPercent:P0})");

            if (player != null)
                sb.AppendLine($"Player Dist: {DistanceToPlayer:F1}");
            else
                sb.AppendLine("<color=yellow>No player reference</color>");

            string enemyInfo = GetEnemyDebugInfo();
            if (!string.IsNullOrEmpty(enemyInfo))
                sb.Append(enemyInfo);

            return sb.ToString();
        }

        /// <summary>
        /// Override to add this enemy's own state to the on screen debug display.
        /// </summary>
        protected virtual string GetEnemyDebugInfo() => string.Empty;

        #endregion
    }
}
