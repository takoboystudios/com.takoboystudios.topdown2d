using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TakoBoyStudios.Animation;
using TakoBoyStudios.Core;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    public enum EntityState
    {
        Idle,
        Dead,
        Custom,
    };

    [ExecuteInEditMode]
    // The motor requirement lives on Character, not here: a terrain-like entity (Breakable) has no
    // motor at all, and RequireComponent makes DestroyImmediate refuse SILENTLY, which is how a
    // "converted" barrel kept its motor and kept falling (T-351). Actors get the requirement back
    // one level down; projectiles and effects get motors from their generators explicitly.
    [RequireComponent(typeof(Rigidbody2D))]
    public partial class Entity : MonoBehaviour, IPoolable
    {
        // ----------------------------
        // Instance configuration
        // ----------------------------
        [BoxGroup("Instance Properties")]
        [SerializeField]
        [Tooltip(
            "Optional shared stat sheet. When set, its vitals overwrite the inline defaults below on "
                + "Init, so the Stats editor is the single source of truth for HP, speed and weight. "
                + "Leave empty to use the inline values."
        )]
        protected EntityStats stats;

        [BoxGroup("Instance Properties")]
        [SerializeField, Min(0f)]
        protected float m_moveSpeed = 9f;

        [BoxGroup("Instance Properties")]
        [SerializeField, Min(1)]
        [Tooltip("Maximum health points")]
        protected int m_maxHp = 1;

        [BoxGroup("Instance Properties")]
        [SerializeField]
        [Tooltip("Lifetime in seconds (-1 = infinite)")]
        protected float m_lifetime = -1f;

        [BoxGroup("Instance Properties")]
        [SerializeField]
        [Tooltip("Entity weight (affects knockback)")]
        protected float m_weight = 100f;

        [BoxGroup("Instance Properties")]
        [SerializeField]
        [Tooltip("Elemental type")]
        protected Element m_element = Element.None;

        // Components
        [BoxGroup("Components")]
        public Transform character; // visual root that will be lifted by motor.Height

        [BoxGroup("Components")]
        [SerializeField]
        protected Size m_shadowSize;

        [BoxGroup("Components")]
        [SerializeField]
        [HideIf("@this.m_shadowSize == TakoBoyStudios.TopDown2D.Size.Off")]
        protected SpriteAnimation m_shadowAnimator;


        [BoxGroup("Components")]
        [SerializeField]
        protected Rigidbody2D m_rb; // kept for safe teleports via property setters

        // Animations
        [BoxGroup("Animations")]
        [SerializeField]
        protected EntityAnimator m_entityAnimator;

        // Runtime state
        protected bool m_initialized;
        protected List<Hitbox2D> m_hitboxes;
        protected List<Hurtbox2D> m_hurtboxes;
        protected Vector2 m_facingDirection;
        protected Vector2 m_lastMoveDirection;
        protected Fsm m_fsm;

        // Meta
        protected float m_hitLagTimer;
        protected int m_hp;
        protected float timeAlive;
        protected bool m_isPooled;

        // Debug
        [BoxGroup("Debug")]
        [SerializeField]
        [Tooltip("Show debug info on screen next to entity")]
        bool m_showDebugOnScreen = false;

        // ----------------------------
        // Properties
        // ----------------------------
        public float ScaleX
        {
            get => transform.localScale.x;
            set => transform.localScale = new Vector3(value, transform.localScale.y, 1f);
        }

        public Vector2 Position
        {
            get => transform.position;
            set
            {
                transform.position = value;
                if (m_rb)
                    m_rb.position = value; // keep RB in sync for kinematic motor
            }
        }

        public Vector2 FacingDirection
        {
            get => m_facingDirection;
            set => m_facingDirection = value;
        }

        // Movement state, exposed as virtuals so a static Entity reports the harmless defaults a
        // motorless body always reported, and a PhysicsEntity overrides them with the real motor
        // (T-355). Keeping the surface on Entity lets shared code (DealDamage, UpdateAnimations, the
        // debug overlay) read movement without knowing whether the body can move.
        public virtual bool IsColliding => false;
        public virtual Vector2 Velocity => Vector2.zero;
        public virtual float Z => character ? character.localPosition.y : 0f;
        public virtual float VerticalVelocity => 0f;
        public virtual bool Grounded => false;
        public virtual bool Flying => false;
        public virtual Vector2 MoveInput => Vector2.zero;
        public virtual Vector2 ImpulseVelocity => Vector2.zero;
        public Vector2 LastMoveDirection => m_lastMoveDirection;
        public float Weight => m_weight;
        public Element Element => m_element;

        public EntityAnimator Animator
        {
            get
            {
                if (!m_entityAnimator)
                    m_entityAnimator = GetComponent<EntityAnimator>();
                return m_entityAnimator;
            }
        }

        // ----------------------------
        // Unity lifecycle
        // ----------------------------
        protected virtual void Reset()
        {
            if (!m_rb)
                m_rb = GetComponent<Rigidbody2D>();
        }

        protected virtual void Awake()
        {
            if (!m_rb)
                m_rb = GetComponent<Rigidbody2D>();
        }

        protected virtual void Start()
        {
            Init();
        }

        public virtual void Init()
        {
            if (m_initialized)
                return;
            m_initialized = true;

            // Data over inline: a stat sheet, if one is assigned, is the source of truth. The
            // serialized fields below are the fallback for an entity that has none.
            if (stats != null)
            {
                m_maxHp = stats.maxHp;
                m_moveSpeed = stats.moveSpeed;
                m_weight = stats.weight;
                m_lifetime = stats.lifetime;
                m_element = stats.element;
            }

            m_lastMoveDirection = new Vector2(-1f, 0f);
            m_hp = m_maxHp;
            m_hitboxes = new List<Hitbox2D>(GetComponentsInChildren<Hitbox2D>(true));
            m_hurtboxes = new List<Hurtbox2D>(GetComponentsInChildren<Hurtbox2D>(true));

            for (int i = 0; i < m_hitboxes.Count; i++)
                m_hitboxes[i].Init(this);

            for (int i = 0; i < m_hurtboxes.Count; i++)
                m_hurtboxes[i].Init(this);

            if (m_entityAnimator)
            {
                m_entityAnimator.Init(this);
                m_entityAnimator.UpdateAnimations();
            }

            AddStates();
            OnShadowSizeChanged();

            OnInitialized();
        }

        /// <summary>
        /// Hook for what a subclass needs at the end of Init. PhysicsEntity uses it to start a
        /// flier flying (T-355); Entity itself does nothing.
        /// </summary>
        protected virtual void OnInitialized() { }

        void Update()
        {
            if (!Application.isPlaying)
                return;

            // Nothing ticks before it has been set up. The state machine is built in Init, and an
            // entity that is active without it throws here every single frame: the player did it for
            // the frames before something called Init, and a pooled effect did it for its whole life,
            // because being acquired activates the object but does not initialise it.
            //
            // Hundreds of swallowed exceptions a second is worse than the bug that causes them, since
            // it makes the console useless for finding the next one.
            if (m_fsm == null)
                return;

            float dt = Time.deltaTime;
            Tick(dt);
        }

        protected virtual bool Tick(float dt)
        {
            if (IsDead)
            {
                m_fsm.Tick(dt);
                return false;
            }

            if (m_lifetime > 0f)
            {
                timeAlive += dt;
                if (timeAlive >= m_lifetime)
                {
                    Die();
                    return false;
                }
            }

            if (IsHitLagged(dt))
                return false;

            for (int i = 0; i < m_hitboxes.Count; i++)
                m_hitboxes[i].Tick(dt);

            for (int i = 0; i < m_hurtboxes.Count; i++)
                m_hurtboxes[i].Tick(dt);

            m_fsm.Tick(dt);

            // A static Entity (a Breakable barrel) has no motor, so this is a no-op; a PhysicsEntity
            // ticks its motor here (T-355).
            TickMovement(dt);

            // Animation decisions use motor state
            UpdateAnimations(dt);
            return true;
        }

        /// <summary>Advances movement each frame. No-op on a static Entity; the motor on a PhysicsEntity.</summary>
        protected virtual void TickMovement(float dt) { }

        void FixedUpdate()
        {
            if (!Application.isPlaying || m_fsm == null)
                return;

            float fdt = Time.fixedDeltaTime;

            for (int i = 0; i < m_hitboxes.Count; i++)
                m_hitboxes[i].PhysicsTick(fdt);

            for (int i = 0; i < m_hurtboxes.Count; i++)
                m_hurtboxes[i].PhysicsTick(fdt);

            PhysicsTick(fdt);
        }

        /// <summary>
        /// Fixed-step movement. No-op on a static Entity; a PhysicsEntity feeds input and impulse to
        /// the motor here (T-355). Kept as the name Enemy already overrides, so the chain is intact.
        /// </summary>
        protected virtual void PhysicsTick(float fdt) { }

        // A shadow only reads once the body is clearly off the floor; below this it just clutters the
        // sprite, so it is hidden. Pixels of fake height.
        const float ShadowLiftThreshold = 1f;

        void LateUpdate()
        {
            // Visual lift based on fake vertical height. Only the visual root rises; the Shadow child
            // hangs off the entity root, not this, so it stays on the floor and the growing gap
            // between sprite and shadow reads as elevation.
            // A static Entity reports Z from its own character offset, so this settles to a no-op;
            // a PhysicsEntity reports motor height and the visual rises with the jump/arc (T-355).
            float height = Z;

            if (character != null)
            {
                Vector3 local = character.localPosition;
                local.y = height;
                character.localPosition = local;
            }

            LiftHitboxes(height);
            UpdateShadowVisibility(height);
        }

        /// <summary>
        /// Show the ground shadow only while the entity is actually lifted (a jump, a hover, flight).
        /// Flush on the floor it is turned off, because an always-on shadow makes it harder to read who
        /// is grounded and who is not. Toggles the renderer rather than the object so the shadow's own
        /// animation keeps ticking and reappears instantly on the next lift.
        /// </summary>
        void UpdateShadowVisibility(float height)
        {
            if (m_shadowAnimator == null || m_shadowSize == Size.Off)
                return;

            SpriteRenderer shadowRenderer = m_shadowAnimator.renderer;
            if (shadowRenderer != null)
                shadowRenderer.enabled = height > ShadowLiftThreshold;
        }

        float[] _hitboxBaseY;

        /// <summary>
        /// Lifts the combat hitboxes with the visual so a hover or an arc keeps them under the body the
        /// player sees rather than stranded on the floor. Navigation collision (the motor's own
        /// collider) and the shadow stay grounded: what a bullet hits follows the sprite, what the body
        /// pushes against does not. A hitbox already parented under the lifted visual moves on its own,
        /// so it is left alone here.
        /// </summary>
        void LiftHitboxes(float height)
        {
            if (m_hitboxes == null || m_hitboxes.Count == 0)
                return;

            if (_hitboxBaseY == null || _hitboxBaseY.Length != m_hitboxes.Count)
            {
                _hitboxBaseY = new float[m_hitboxes.Count];
                for (int i = 0; i < m_hitboxes.Count; i++)
                    _hitboxBaseY[i] = m_hitboxes[i] != null ? m_hitboxes[i].transform.localPosition.y : 0f;
            }

            for (int i = 0; i < m_hitboxes.Count; i++)
            {
                Hitbox2D hitbox = m_hitboxes[i];
                if (hitbox == null)
                    continue;

                Transform t = hitbox.transform;
                if (character != null && t != character && t.IsChildOf(character))
                    continue;

                Vector3 local = t.localPosition;
                local.y = _hitboxBaseY[i] + height;
                t.localPosition = local;
            }
        }

        // ----------------------------
        // FSM
        // ----------------------------
        public virtual void AddStates()
        {
            m_fsm = new Fsm();
            m_fsm.AddState(StateIdle);
            m_fsm.AddState(StateDead);
            m_fsm.ChangeState((int)EntityState.Idle);
        }

        protected virtual void StateIdle(Fsm.StateStep step, float deltaTime)
        {
            switch (step)
            {
                case Fsm.StateStep.Enter:
                    break;
                case Fsm.StateStep.Update:
                    // Movement is handled in FixedUpdate via inputs
                    break;
                case Fsm.StateStep.Exit:
                    break;
            }
        }

        protected virtual void StateDead(Fsm.StateStep step, float deltaTime)
        {
            if (step == Fsm.StateStep.Enter)
            {
                ResetMovement();
                Dispose();
            }
        }

        /// <summary>Zeroes movement input and impulse. No-op on a static Entity; real on PhysicsEntity.</summary>
        protected virtual void ResetMovement() { }

        // ----------------------------
        // Input feeding API (player or AI)
        // ----------------------------
        // Additive impulse for knockback, dashes and scripted pushes. No-op on a static Entity (a
        // barrel ignores knockback); a PhysicsEntity feeds it into the motor (T-355). SetMoveDirection
        // and SetJumpInput moved to PhysicsEntity with the input fields they write.
        public virtual void AddImpulse(Vector2 impulse) { }

        public void SetVelocityInLastDirection(float speed)
        {
            // Convert to impulse to respect motor slide and collision
            AddImpulse(m_lastMoveDirection.normalized * speed);
        }

        public void AddForceInLastDirection(float force)
        {
            AddImpulse(m_lastMoveDirection.normalized * force);
        }

        public void AddForce(Vector2 force)
        {
            AddImpulse(force);
        }

        // ----------------------------
        // Combat state
        // ----------------------------

        /// <summary>
        /// Whether this entity is part of a live encounter. A CamLock turns it on for everything it
        /// owns when the fight starts and off again when it clears.
        ///
        /// It defaults to true, so anything spawned outside a CamLock (the endless lab, a test scene)
        /// behaves exactly as it always has and nothing has to opt in. What it buys is the other
        /// direction: an actor standing in a room before the fight begins can idle, talk, patrol or
        /// sleep, and only start fighting when the encounter does.
        ///
        /// The base does nothing with it beyond storing it. Subclasses override
        /// <see cref="OnCombatStateChanged"/> to react.
        /// </summary>
        public bool InCombat { get; private set; } = true;

        public void SetCombatState(bool inCombat)
        {
            if (InCombat == inCombat)
                return;

            InCombat = inCombat;
            OnCombatStateChanged(inCombat);
        }

        /// <summary>
        /// Called when <see cref="InCombat"/> actually changes. Deliberately empty here: the base
        /// behaviour is to carry on as before.
        /// </summary>
        protected virtual void OnCombatStateChanged(bool inCombat) { }

        public event Action<HitEvent> OnTakeDamage;
        public event Action OnDeath;

        Status.StatusHolder m_statuses;
        bool m_statusesLookedUp;

        /// <summary>
        /// The statuses on this entity, or null when it has never carried one. Cached, because
        /// movement and damage both ask every frame; <see cref="Status.StatusHolder.Of"/> fills the
        /// cache when it adds one, so the lookup happens once in an entity's life.
        /// </summary>
        public Status.StatusHolder Statuses
        {
            get
            {
                if (!m_statusesLookedUp)
                {
                    m_statusesLookedUp = true;
                    m_statuses = GetComponent<Status.StatusHolder>();
                }

                return m_statuses;
            }
        }

        /// <summary>Called by <see cref="Status.StatusHolder.Of"/> when it adds one, so the cache never goes stale.</summary>
        public void RegisterStatusHolder(Status.StatusHolder holder)
        {
            m_statuses = holder;
            m_statusesLookedUp = true;
        }

        public bool IsDead => m_fsm?.CurrentState == (int)EntityState.Dead;

        /// <summary>Current hit points. Read-only, for readouts and death checks.</summary>
        public int Health => m_hp;

        public int MaxHealth => m_maxHp;

        /// <summary>
        /// Who is ultimately responsible for the damage this entity deals: the enemy that fired a
        /// projectile or dropped a bomb, so a kill by a bomb's blast is credited to the bird that
        /// dropped it, not to the bomb or its explosion (T-328). Set by whoever spawns the thing, and
        /// propagated down a bomb-to-explosion chain. Null on an entity that answers for itself; the
        /// central <c>FireProjectile</c>/<c>LobBomb</c> helpers set it, and pool reuse clears it.
        /// </summary>
        public Entity Instigator { get; set; }

        /// <summary>
        /// Walks up the instigator chain to the entity ultimately responsible, or this one if it
        /// answers for itself. Capped so a stray cycle cannot loop forever.
        /// </summary>
        public Entity RootInstigator
        {
            get
            {
                Entity e = this;
                for (int i = 0; i < 8 && e.Instigator != null && e.Instigator != e; i++)
                    e = e.Instigator;
                return e;
            }
        }

        // Entity.cs - Updated TakeHit method
        public virtual void DealDamage(HitEvent hitEvent)
        {
            DamageInfo info = hitEvent.damageInfo;

            // Calculate final damage and knockback
            int damage = info.GetFinalDamage();
            Vector2 knockback = info.GetFinalKnockback();

            // A heavy hit breaks the statuses the game says it breaks, which in Hell Wilds is the
            // freeze. The multiplier is already in the damage above; clearing it here means the shove
            // that follows lands on something that can be moved again. The entity layer does not know
            // which status that is, so it asks.
            if (info.values.heavy)
                CombatRules.BreakOnHeavyHit(Statuses);

            // Apply damage
            m_hp -= damage;
            m_hp = Mathf.Clamp(m_hp, 0, int.MaxValue);

            // Announced before the death branch, so a killing blow is still a hit. Anything that fires
            // on hits would otherwise silently skip the one hit in an encounter that most wants to be
            // reacted to, and a Perk that ignites on hit would never ignite the thing it killed.
            EntityEvents.ReportHitLanded(info, Status.StatusHolder.DamagingStatus);

            if (m_hp <= 0)
            {
                Die();
                return;
            }

            // Apply knockback
            AddImpulse(knockback);

            // Forced movement from a hit is a Displacement, which is what Bleed pays off on and what
            // a displacement-triggered Perk fires from. Walking is not this, and neither is a dash:
            // both go through AddImpulse without a hit behind them, which is why this lives here and
            // not there. A status's own damage carries no knockback, so a burn cannot pop bleed.
            if (knockback.sqrMagnitude > 0f)
            {
                Status.StatusHolder statuses = Statuses;
                if (statuses != null)
                {
                    Status.StatusEvents.ReportDisplaced(this, info.source);
                    statuses.OnDisplaced();
                }
            }

            // Apply hitstun/hitlag
            if (Grounded)
                StartHitLag(info.GetHitLagFrames());

            // Trigger effects
            OnTakeDamage?.Invoke(hitEvent);
        }

        public virtual void Die()
        {
            if (IsDead)
                return;

            m_hp = 0;
            OnDeath?.Invoke();
            m_fsm?.ChangeState((int)EntityState.Dead);
        }

        public virtual void StartHitLag(int frames)
        {
            if (frames <= 0)
                return;

            m_hitLagTimer = frames / 60f;
            if (m_entityAnimator)
                m_entityAnimator.Paused = true;
        }

        bool IsHitLagged(float deltaTime)
        {
            if (m_hitLagTimer <= 0f)
                return false;

            m_hitLagTimer -= deltaTime;
            if (m_hitLagTimer > 0f)
                return true;

            StopHitLag();
            return false;
        }

        public virtual void StopHitLag()
        {
            m_hitLagTimer = 0f;
            if (m_entityAnimator)
                m_entityAnimator.Paused = false;
        }

        // ----------------------------
        // Visuals and animations
        // ----------------------------
        public virtual void PlayAnimation(string anim)
        {
            if (!m_entityAnimator)
            {
                Debug.LogError(
                    $"There is no EntityAnimator component on this entity {gameObject.name}"
                );
                return;
            }

            if (
                !m_entityAnimator.CurrentAnimationName.Equals(anim)
                && m_entityAnimator.HasAnimation(anim)
            )
            {
                m_entityAnimator.Play(anim);
            }
        }

        public bool IsAnimationValid(string anim)
        {
            if (!m_entityAnimator)
            {
                Debug.LogError(
                    $"There is no EntityAnimator component on this entity {gameObject.name}"
                );
                return false;
            }

            return m_entityAnimator.HasAnimation(anim);
        }

        public virtual bool IsCurrentAnimation(string anim)
        {
            if (!m_entityAnimator)
            {
                Debug.LogError(
                    $"There is no EntityAnimator component on this entity {gameObject.name}"
                );
                return false;
            }

            return m_entityAnimator.CurrentAnimationName.Equals(anim);
        }

        protected virtual void UpdateAnimations(float deltaTime)
        {
            if (!Grounded)
            {
                if (
                    IsAnimationValid(AnimConst.JumpAntic)
                    && IsCurrentAnimation(AnimConst.JumpAntic)
                    && m_entityAnimator
                    && !m_entityAnimator.IsDone
                )
                    return;

                if (Mathf.Abs(VerticalVelocity) <= 0.1f && IsAnimationValid(AnimConst.JumpPeak))
                    PlayAnimation(AnimConst.JumpPeak);
                else if (VerticalVelocity > 0.1f)
                    PlayAnimation(AnimConst.JumpLoop);
                else
                    PlayAnimation(AnimConst.Fall);
            }
            else if (MoveInput.sqrMagnitude > 0f)
            {
                PlayAnimation(AnimConst.Move);
            }
            else
            {
                PlayAnimation(AnimConst.Idle);
            }
        }

        protected virtual void OnShadowSizeChanged()
        {
            if (!m_shadowAnimator)
            {
                Transform shadow = transform.Find("Shadow");
                if (shadow == null)
                    return;
                m_shadowAnimator = shadow.GetComponent<SpriteAnimation>();
                if (!m_shadowAnimator)
                    return;
            }

            if (m_shadowSize == Size.Off)
            {
                m_shadowAnimator.gameObject.SetActive(false);
                return;
            }

            m_shadowAnimator.gameObject.SetActive(true);
            m_shadowAnimator.Play($"{m_shadowSize.ToString().ToLower()}");
        }

        public virtual void FlipX(bool flip)
        {
            ScaleX = flip ? -1f : 1f;
        }

        /// <summary>
        /// Puts this body in the air: it stops colliding with the room's walls.
        ///
        /// The flag was here already and nothing read it, so flight meant nothing and an entity that
        /// declared itself flying still walked into rock like everything else. It does the thing now,
        /// which is why there is one idea of flight rather than a flag beside an enemy that quietly
        /// arranged its own.
        ///
        /// The combat boundary is untouched. A flier sealed into an encounter is still held inside
        /// the frame, or clearing a fight would mean chasing a bird across the room.
        /// </summary>
        public virtual float GetMoveSpeed()
        {
            // Chill slows and Frozen stops outright (T-408). Read through the cached holder, since
            // this is asked for every moving entity every frame.
            Status.StatusHolder statuses = Statuses;
            return statuses != null ? m_moveSpeed * statuses.MoveSpeedMultiplier : m_moveSpeed;
        }

        // ----------------------------
        // Utilities
        // ----------------------------
        public static bool IsOffscreen(Renderer renderer, Camera camera)
        {
            if (!renderer || !camera)
                return true;

            Bounds bounds = renderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;

            Vector3[] corners = new Vector3[8];
            corners[0] = new Vector3(min.x, min.y, min.z);
            corners[1] = new Vector3(min.x, min.y, max.z);
            corners[2] = new Vector3(min.x, max.y, min.z);
            corners[3] = new Vector3(min.x, max.y, max.z);
            corners[4] = new Vector3(max.x, min.y, min.z);
            corners[5] = new Vector3(max.x, min.y, max.z);
            corners[6] = new Vector3(max.x, max.y, min.z);
            corners[7] = new Vector3(max.x, max.y, max.z);

            for (int i = 0; i < 8; i++)
            {
                Vector3 screenPoint = camera.WorldToScreenPoint(corners[i]);
                if (
                    screenPoint.z > 0
                    && screenPoint.x >= 0
                    && screenPoint.x <= Screen.width
                    && screenPoint.y >= 0
                    && screenPoint.y <= Screen.height
                )
                {
                    return false;
                }
            }

            return true;
        }

        // ----------------------------
        // Pooling
        // ----------------------------
        public virtual void Dispose()
        {
            if (m_isPooled)
                PoolManager.Instance.Release(gameObject);
            else
                gameObject.SetActive(false);
        }

        public virtual void OnCreated()
        {
            m_isPooled = true;
        }

        public virtual void OnAcquired()
        {
            // A pooled body may never have been initialised: the pool activates it, which starts it
            // ticking, but nothing else calls Init. Idempotent, so an instance on its second life
            // simply skips it.
            Init();

            // Reset for reuse. A pooled entity comes back from its previous life dead: zero HP, in
            // the Dead state, with any knockback or hit lag still on it. Restore it so it spawns
            // alive and can be killed, and counted, again. Without this a re-acquired enemy is a
            // corpse that a wave waits on forever.
            m_hp = m_maxHp;
            m_hitLagTimer = 0f;
            ResetMovement();

            // Statuses belong to the life that earned them. A body still burning from its previous
            // one would burn its replacement, and a freeze left on would spawn an enemy that cannot
            // move (T-408).
            Statuses?.ClearAll(silent: true);

            // A pooled projectile/bomb/effect carries whoever instigated its previous life; clear it so
            // a reused body starts un-attributed and only the current spawner's Instigator sticks.
            Instigator = null;

            // A body from the pool comes back combat-ready. Anything that wants it standing around
            // instead (a CamLock placing an actor before its fight) sets that explicitly after
            // acquiring, so a stale false can never leave an enemy permanently inert.
            InCombat = true;

            // ForceState, not ChangeState. ChangeState only queues the transition and CurrentState
            // keeps its old value until the next Tick, so a body reused from the pool would still
            // answer IsDead for the rest of the frame it was spawned in. Anything that asks during
            // that window, and the wave runner asks every frame, sees a corpse and writes the enemy
            // off before it has drawn breath.
            m_fsm?.ForceState((int)EntityState.Idle);
        }

        public virtual void OnReleased() { }

        public virtual void OnDestroyed() { }

        // ----------------------------
        // Debug
        // ----------------------------
        void OnGUI()
        {
            if (!m_showDebugOnScreen || !Application.isPlaying)
                return;

            Camera cam = Camera.main;
            if (!cam)
                return;

            // Get screen position above entity
            Vector3 worldPos = transform.position + new Vector3(0, 1f, 0);
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

            // Don't draw if behind camera
            if (screenPos.z < 0)
                return;

            // Flip Y coordinate (Unity GUI uses top-left origin)
            screenPos.y = Screen.height - screenPos.y;

            // Prepare debug text
            string debugText = GetDebugInfo();

            // Calculate box size
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 32;
            style.normal.textColor = Color.white;
            style.alignment = TextAnchor.MiddleLeft;

            Vector2 textSize = style.CalcSize(new GUIContent(debugText));
            float padding = 8f;
            Rect boxRect = new Rect(
                screenPos.x - textSize.x / 2 - padding / 2,
                screenPos.y - textSize.y - padding,
                textSize.x + padding,
                textSize.y + padding
            );

            // Draw background box
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Box(boxRect, "");

            // Draw text
            GUI.color = Color.white;
            Rect textRect = new Rect(
                boxRect.x + padding / 2,
                boxRect.y + padding / 2,
                boxRect.width - padding,
                boxRect.height - padding
            );
            GUI.Label(textRect, debugText, style);
        }

        public virtual string GetDebugInfo()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            // Entity name
            sb.AppendLine($"<b>{gameObject.name}</b>");
            sb.AppendLine("─────────────────────");

            // State info
            sb.AppendLine($"State: {GetCurrentStateName()}");
            sb.AppendLine($"HP: {m_hp}/{m_maxHp}");
            sb.AppendLine($"Time Alive: {timeAlive:F2}s");

            // Animation info
            if (m_entityAnimator)
            {
                sb.AppendLine($"Anim: {m_entityAnimator.CurrentAnimationName}");
                sb.AppendLine($"Frame: {m_entityAnimator.CurrentFrame}");
                sb.AppendLine($"Done: {m_entityAnimator.IsDone}");
                sb.AppendLine($"Paused: {m_entityAnimator.Paused}");
            }

            // Position & Movement
            sb.AppendLine($"Pos: {Position.x:F1}, {Position.y:F1}");
            sb.AppendLine($"Vel: {Velocity.x:F1}, {Velocity.y:F1} ({Velocity.magnitude:F1})");
            sb.AppendLine($"Speed: {m_moveSpeed:F1}");
            sb.AppendLine($"Impulse: {ImpulseVelocity.x:F1}, {ImpulseVelocity.y:F1}");

            // Input & Direction
            sb.AppendLine($"MoveInput: {MoveInput.x:F2}, {MoveInput.y:F2}");
            sb.AppendLine($"FacingDir: {m_facingDirection.x:F2}, {m_facingDirection.y:F2}");
            sb.AppendLine($"LastMoveDir: {m_lastMoveDirection.x:F2}, {m_lastMoveDirection.y:F2}");

            // Physics state
            sb.AppendLine($"Grounded: {Grounded}");
            sb.AppendLine($"Flying: {Flying}");
            sb.AppendLine($"Height (Z): {Z:F2}");
            sb.AppendLine($"VertVel: {VerticalVelocity:F2}");

            // Pooling
            if (m_isPooled)
                sb.AppendLine($"<color=cyan>Pooled</color>");

            return sb.ToString();
        }

        protected virtual string GetCurrentStateName()
        {
            if (m_fsm == null)
                return "No FSM";

            return m_fsm.CurrentState switch
            {
                (int)EntityState.Idle => "Idle",
                (int)EntityState.Dead => "Dead",
                _ => $"Custom ({m_fsm.CurrentStateName})",
            };
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!character)
                return;
            Gizmos.DrawLine(character.position, transform.position);
        }
#endif
    }
}
