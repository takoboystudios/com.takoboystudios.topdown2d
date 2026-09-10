using System.Collections.Generic;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    // EXTRACTION NOTES (T-356). This motor is meant to become a game-agnostic takoboystudios
    // top-down-2D package, like the animation and core packages. What blocks that today, all
    // marked "PACKAGE COUPLING" at their sites below:
    //   1. `using HellWilds.Rooms` for CombatLayers.ActiveMask in EffectiveCollisionMask. Replace
    //      with an injected "extra solid mask" provider (a Func<int> the game sets), so containment
    //      is game-supplied rather than baked in.
    //   2. The hardcoded layer names "Breakable" (BreakableMask), "Geometry" and "Enemy" (Reset).
    //      A package cannot ship a project's layer names; take them as serialized masks.
    //   3. The collisionMask==0 special case in EffectiveCollisionMask conflates "flyer/projectile"
    //      with "empty mask"; separate the intent when the layer names become configurable.
    //   4. queriesStartInColliders is set as a global in Awake; a package should let the host set it
    //      once rather than a component mutating global physics.
    // None of that changes behaviour; it is decoupling for the move, deferred until the extraction.
    [RequireComponent(typeof(Rigidbody2D))]
    public class TopDownMotor2D : EntityComponent
    {
        #region Inspector: References
        [SerializeField]
        Rigidbody2D body;

        [SerializeField]
        CircleCollider2D collider2d;
        #endregion

        #region Inspector: Layers & Query
        [Tooltip(
            "Layers that hard-block movement. Walls only, normally the Geometry layer. This must NOT "
                + "include other bodies or damage boxes, or the body stops dead against them. Bodies "
                + "separate softly instead, see separationMask."
        )]
        [SerializeField]
        LayerMask collisionMask = ~0;

        [Tooltip(
            "Layers this body gently pushes apart from, so a crowd spreads out instead of stacking "
                + "on one point. Normally the Enemy layer for an enemy. Leave empty for the player, "
                + "which passes through enemies and only takes contact damage. Unlike collisionMask "
                + "this never hard-stops movement, it just nudges overlaps apart over a few frames."
        )]
        [SerializeField]
        LayerMask separationMask;

        [Tooltip(
            "Fraction of an overlap resolved per frame during soft separation. 1 pushes fully out in "
                + "one frame (jittery when two bodies each do it), lower spreads the correction over "
                + "several frames and reads as soft. 0.5 is a good start."
        )]
        [SerializeField, Range(0.05f, 1f)]
        float separationStrength = 0.5f;

        [Tooltip(
            "Cap on how far soft separation can move the body in a single frame, in pixels. Stops a "
                + "deep overlap from flinging a body across the arena."
        )]
        [SerializeField, Min(0f)]
        float maxSeparationPerFrame = 6f;

        [SerializeField, Min(0.0001f)]
        float skin = 2f;

        [SerializeField, Range(1, 8)]
        int sweepIterations = 4;

        [SerializeField, Range(1, 4)]
        int depenetrationIterations = 2;
        #endregion

        #region Inspector: Jump & Vertical (fake Z)
        [SerializeField, Min(0f)]
        float gravity = 1800f;

        [SerializeField, Min(0f)]
        float jumpSpeed = 500f;

        [SerializeField, Min(0f)]
        float jumpCutGravityScale = 2.2f;

        [SerializeField, Min(0f)]
        float coyoteTime = 0.08f;

        [SerializeField, Min(0f)]
        float jumpBuffer = 0.12f;

        [SerializeField]
        bool variableJump = true;
        #endregion

        #region Inspector: Feel
        [SerializeField]
        float contactOffset = 1f;
        #endregion

        #region Combat boundary
        [Tooltip(
            "Whether this body is contained by the temporary CombatBoundary walls a CamLock raises "
                + "around the camera. True for anything with a physical body: the player, enemies. "
                + "False for projectiles, which are stopped by their hurtbox's wall check instead so "
                + "they die against the edge rather than piling up on it."
        )]
        [SerializeField]
        bool containedByCombatBoundary = true;

        // Layers this body is temporarily excused from, even while they are globally solid. Only the
        // CombatBoundary uses it, and only for an enemy walking in from outside the frame: it must be
        // able to cross inward, and the moment it is inside the excuse is revoked and it is contained
        // like everything else.
        int _excusedMask;

        /// <summary>
        /// Lets this body pass through layers that are globally solid right now. One-way admission for
        /// an entering enemy; call <see cref="ClearExcusedCollisionLayers"/> once it is in.
        /// </summary>
        public void ExcuseCollisionLayers(int mask) => _excusedMask |= mask;

        /// <summary>
        /// Whether the combat frame walls apply to this body at all. Projectiles turn it off at Awake:
        /// a bullet should die on the edge, not slide along it.
        /// </summary>
        public void SetContainedByCombatBoundary(bool contained) => containedByCombatBoundary = contained;

        public void ClearExcusedCollisionLayers() => _excusedMask = 0;

        public bool IsExcusedFrom(int mask) => (_excusedMask & mask) != 0;

        /// <summary>
        /// Replaces what hard-blocks this body's movement.
        ///
        /// For a body that is genuinely in the air rather than merely drawn above the floor. Setting
        /// nothing here lets it pass over walls, and it is deliberately not the excused mask: that one
        /// only lifts the combat boundary for an enemy still walking into frame, and is cleared the
        /// moment it arrives. Flight is a property of the enemy, not a temporary dispensation, so it
        /// is set by the enemy in code rather than left as a serialized value on a prefab that reads
        /// as an oversight.
        ///
        /// The combat boundary still applies. A bird sealed into an encounter is held in the frame
        /// like anything else, which is what stops it flying out of a fight it has been caught in.
        /// </summary>
        public void SetCollisionMask(LayerMask mask) => collisionMask = mask;

        /// <summary>What currently hard-blocks this body, so a caller can put it back.</summary>
        public LayerMask CollisionMask => collisionMask;

        /// <summary>
        /// What this body actually sweeps against this frame: its authored walls, plus whatever the
        /// combat frame has made solid, minus anything it is currently excused from. Outside a CamLock
        /// the extra term is zero and this is exactly the serialized mask.
        /// </summary>
        // Breakables (barrels, crates) live on their own layer, off Geometry, so that projectile
        // wall checks ignore them and a bullet can reach the thing it should damage (T-351). To
        // everything that walks they are still walls, added here the same way the combat boundary
        // is, so no prefab has to know the layer exists. A grounded mover of zero mask is a
        // projectile flying straight or a flyer: both pass over a barrel by design.
        static int _breakableMask = -1;
        static int BreakableMask =>
            _breakableMask != -1 ? _breakableMask : _breakableMask = LayerMask.GetMask("Breakable");

        // PACKAGE COUPLING (T-356): CombatLayers.ActiveMask ties this to HellWilds.Rooms; the
        // BreakableMask and the collisionMask==0 case tie it to this game's layers. See the
        // extraction notes at the top of the file.
        /// <summary>
        /// Whether the shared co-op view is a wall for this body. Set by <see cref="Player"/> on
        /// itself, so no prefab has to know the layer exists and nothing but a player is ever held in
        /// by it.
        /// </summary>
        public bool ContainedByPlayerFrame { get; set; }

        int EffectiveCollisionMask =>
            (containedByCombatBoundary
                ? collisionMask | (CombatRules.ContainmentMask() & ~_excusedMask)
                : (int)collisionMask)
            | (ContainedByPlayerFrame ? CombatRules.FrameMask() : 0)
            | (collisionMask == 0 ? 0 : BreakableMask);
        #endregion

        #region Runtime (read-only)
        public Vector2 Velocity => _velocity;
        public float Height => _height;
        public float VerticalVelocity => _verticalVelocity;
        public bool IsGrounded => _isGrounded;
        public IReadOnlyList<RaycastHit2D> Collisions => _collisionsThisFrame;
        #endregion

        #region Input API
        public void ApplyInput(Vector2 desiredVelocity, bool jumpPressed, bool jumpHeld)
        {
            _desiredVelocity = desiredVelocity;
            _queuedJumpPressed |= jumpPressed;
            _jumpHeldCurrent = jumpHeld;
            if (jumpPressed)
                _lastJumpPressedTime = Time.time;
        }
        #endregion

        #region Internals
        // planar
        Vector2 _desiredVelocity;
        Vector2 _velocity;

        // fake vertical
        float _height;
        float _verticalVelocity;
        float _arcGravity;
        bool _usingArcGravity;
        bool _jumpHeldCurrent;
        bool _isGrounded;
        float _lastOnGroundTime = -999f;
        float _lastJumpPressedTime = -999f;
        bool _queuedJumpPressed;

        ContactFilter2D _filter;
        ContactFilter2D _separationFilter;
        readonly RaycastHit2D[] _castHits = new RaycastHit2D[8];
        readonly Collider2D[] _overlapHits = new Collider2D[8];
        readonly Collider2D[] _separationHits = new Collider2D[16];

        // collision tracking
        readonly List<RaycastHit2D> _collisionsThisFrame = new List<RaycastHit2D>(8);

        // tuning
        const float Eps = 1e-6f;
        const float MinPen = 0.0005f; // ignore tiny overlaps to prevent jitter
        #endregion

        #region Unity
        protected override void Reset()
        {
            base.Reset();
            body = GetComponent<Rigidbody2D>();
            collider2d = GetComponent<CircleCollider2D>();

            // Sane defaults for a hand-added motor: block on walls, ease apart from other enemies.
            // The scaffolder and the migration tool set these explicitly per body type (the player
            // clears separationMask so it passes through enemies, projectiles clear both so they fly
            // straight), so this only governs a motor dropped on by hand.
            collisionMask = LayerMask.GetMask("Geometry");
            separationMask = LayerMask.GetMask("Enemy");
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (!body)
                body = GetComponent<Rigidbody2D>();
            if (!collider2d)
                collider2d = GetComponent<CircleCollider2D>();
        }

        protected override void Awake()
        {
            if (!body)
                body = GetComponent<Rigidbody2D>();
            if (!collider2d)
                collider2d = GetComponent<CircleCollider2D>();

            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            Physics2D.queriesStartInColliders = false;

            _filter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true,
                layerMask = collisionMask,
                useNormalAngle = false,
            };

            _separationFilter = new ContactFilter2D
            {
                useTriggers = false,
                useLayerMask = true,
                layerMask = separationMask,
                useNormalAngle = false,
            };
        }

        public override void PhysicsTick(float fdt)
        {
            // Re-read the solid mask every tick. A CamLock can raise or drop the combat frame at any
            // moment, and an entering enemy loses its excuse the frame it crosses inside.
            _filter.layerMask = EffectiveCollisionMask;

            // Clear collision tracking from last frame
            _collisionsThisFrame.Clear();

            // Ensure we never start a frame inside geometry
            PreDepenetrateOnce();

            UpdateGroundedFromHeight();
            _velocity = _desiredVelocity;
            UpdateArielVelocity(fdt);

            Vector2 delta = _velocity * fdt;
            if (delta.sqrMagnitude > 0f)
                SweepAndSlide(ref delta);

            // Any remainder (should be zero) is applied here
            body.position += delta;

            // Final safety push if we somehow ended with real overlap with solid geometry.
            Depenetrate();

            // Soft push apart from other bodies. Separate from wall depenetration on purpose: walls
            // are resolved fully so nothing tunnels through them, bodies are resolved partially so a
            // crowd eases apart instead of two enemies shoving each other back and forth every frame.
            SoftSeparate();

            _queuedJumpPressed = false;
        }
        #endregion

        #region Helpers
        public bool IsColliding() => _collisionsThisFrame.Count > 0;

        /// <summary>
        /// Launches on a ballistic arc that peaks at the given height and lands after the given
        /// time. Used for anything thrown rather than jumped: a lobbed bomb, a hopping crab.
        ///
        /// The motor owns vertical motion, so an arc has to be set here. Entity drives the visual
        /// height straight off Height every frame, which means faking a parabola on the sprite
        /// transform would be overwritten immediately.
        ///
        /// Gravity is derived from the arc rather than taken from the inspector, so the flight time
        /// is exactly what was asked for. It resets to the serialized value on landing.
        /// </summary>
        public void LaunchArc(float peakHeight, float flightTime)
        {
            if (flightTime <= 0.0001f)
                return;

            float h = Mathf.Max(peakHeight, 0.001f);

            // Standard projectile arc: peak at h, back to zero at flightTime.
            _arcGravity = 8f * h / (flightTime * flightTime);
            _verticalVelocity = 4f * h / flightTime;

            _height = 0.0001f;
            _isGrounded = false;
            _usingArcGravity = true;
        }

        bool _hovering;
        float _hoverHeight;

        /// <summary>
        /// Holds the body at a fixed visual height with no gravity: a flyer hovering above the floor
        /// rather than a thing arcing through the air. The ground XY still moves normally; only the
        /// fake-Z is pinned. Entity lifts the sprite (and the combat hitbox) to this height, and the
        /// shadow stays on the floor, so the gap reads as hover.
        /// </summary>
        public void SetHover(float height)
        {
            _hovering = true;
            _hoverHeight = Mathf.Max(0f, height);
            _height = _hoverHeight;
            _verticalVelocity = 0f;
            _isGrounded = false;
            _usingArcGravity = false;
        }

        public void ClearHover() => _hovering = false;

        /// <summary>
        /// Forces the body back to the ground plane immediately: height zero, no vertical velocity, no
        /// arc or hover in progress. Used when a room reset needs to land the player cleanly regardless
        /// of a jump that was in the air at the time.
        /// </summary>
        public void ResetVertical()
        {
            _height = 0f;
            _verticalVelocity = 0f;
            _isGrounded = true;
            _usingArcGravity = false;
            _hovering = false;
        }
        #endregion

        #region Movement: Vertical (Jump)
        void UpdateGroundedFromHeight()
        {
            if (_hovering)
            {
                _height = _hoverHeight;
                _isGrounded = false;
                return;
            }

            bool wasGrounded = _isGrounded;
            _isGrounded = _height <= 0.0001f && _verticalVelocity <= 0f;

            if (_isGrounded)
            {
                if (!wasGrounded)
                    _lastOnGroundTime = Time.time;
                _height = 0f;
                _verticalVelocity = Mathf.Max(0f, _verticalVelocity);
            }
        }

        void UpdateArielVelocity(float dt)
        {
            // A hovering flyer has no gravity and cannot jump; its height is pinned in SetHover.
            if (_hovering)
                return;

            bool bufferedJump =
                _queuedJumpPressed || (Time.time - _lastJumpPressedTime) <= jumpBuffer;
            bool canCoyote = (Time.time - _lastOnGroundTime) <= coyoteTime;

            if (bufferedJump && (_isGrounded || canCoyote))
            {
                _verticalVelocity = jumpSpeed;
                _height = 0.0001f;
                _isGrounded = false;
                _queuedJumpPressed = false;
                _lastJumpPressedTime = -999f;
            }

            // An arc sets its own gravity so its flight time is exact. Jump feel tweaks like the
            // variable-height cut would distort that, so they are skipped while arcing.
            float g = _usingArcGravity ? _arcGravity : gravity;

            if (!_usingArcGravity && variableJump && _verticalVelocity > 0f && !_jumpHeldCurrent)
                g *= jumpCutGravityScale;
            _verticalVelocity -= g * dt;

            _height += _verticalVelocity * dt;

            if (_height <= 0f)
            {
                _height = 0f;
                if (_verticalVelocity < 0f)
                    _verticalVelocity = 0f;
                _isGrounded = true;
                _lastOnGroundTime = Time.time;
                _usingArcGravity = false;
            }
        }
        #endregion

        #region Collision: Sweep & Slide
        void SweepAndSlide(ref Vector2 delta)
        {
            Vector2 remaining = delta;
            Vector2 pos = body.position;

            for (int iter = 0; iter < sweepIterations; iter++)
            {
                float dist = remaining.magnitude;
                if (dist <= Eps)
                    break;

                Vector2 dir = remaining / Mathf.Max(dist, Eps);

                // Cast from the *current* pos each iteration
                body.position = pos;

                int hitCount = body.Cast(dir, _filter, _castHits, dist + skin);
                if (hitCount == 0)
                {
                    // No hits: move fully
                    pos += remaining;
                    remaining = Vector2.zero;
                    break;
                }

                // earliest hit
                RaycastHit2D hit = _castHits[0];
                for (int i = 1; i < hitCount; i++)
                    if (_castHits[i].fraction < hit.fraction)
                        hit = _castHits[i];

                // Track collision
                _collisionsThisFrame.Add(hit);

                // Move up to just before contact (respect frame budget)
                float travel = Mathf.Clamp(hit.distance - skin, 0f, dist);
                pos += dir * travel;

                // Kill velocity into the wall
                Vector2 n = hit.normal;
                float vn = Vector2.Dot(_velocity, n);
                if (vn < 0f)
                    _velocity -= n * vn;

                // Slide tangentially by removing the normal component from the *remaining* displacement
                remaining -= n * Vector2.Dot(remaining, n);
            }

            body.position = pos;
            delta = Vector2.zero;
        }
        #endregion

        #region Overlap Resolution
        void PreDepenetrateOnce()
        {
            // if we don't have a collider don't worry about this
            if (collider2d == null)
                return;

            // One decisive push before we start sweeping, if needed.
            int count = Physics2D.OverlapCollider(collider2d, _filter, _overlapHits);
            if (count == 0)
                return;

            float maxPen = 0f;
            ColliderDistance2D best = default;

            for (int j = 0; j < count; j++)
            {
                Collider2D other = _overlapHits[j];
                if (!other)
                    continue;

                var d = collider2d.Distance(other);
                if (d.isOverlapped)
                {
                    float pen = -d.distance; // negative when overlapped
                    if (pen > maxPen)
                    {
                        maxPen = pen;
                        best = d;
                    }
                }
            }

            if (maxPen > MinPen)
            {
                Vector2 push = -best.normal * (maxPen + Mathf.Min(contactOffset, skin * 0.5f));
                body.position += push;
            }
        }

        void Depenetrate()
        {
            // if we don't have a collider, don't worry about this
            if (collider2d == null)
                return;

            for (int i = 0; i < depenetrationIterations; i++)
            {
                int count = Physics2D.OverlapCollider(collider2d, _filter, _overlapHits);
                if (count == 0)
                    break;

                float maxPen = 0f;
                ColliderDistance2D best = default;

                for (int j = 0; j < count; j++)
                {
                    Collider2D other = _overlapHits[j];
                    if (!other)
                        continue;

                    var d = collider2d.Distance(other);
                    if (d.isOverlapped)
                    {
                        float pen = -d.distance;
                        if (pen > maxPen)
                        {
                            maxPen = pen;
                            best = d;
                        }
                    }
                }

                if (maxPen > MinPen)
                {
                    Vector2 push = -best.normal * (maxPen + Mathf.Min(contactOffset, skin * 0.5f));
                    body.position += push;
                }
                else
                    break;
            }
        }

        /// <summary>
        /// Gently pushes this body out of everything on separationMask, summed across all overlaps
        /// so a body wedged between several others slides toward the open space rather than fighting
        /// each neighbour in turn.
        ///
        /// The push is scaled by separationStrength and capped per frame. Two bodies each apply their
        /// half, so with the default 0.5 a symmetric overlap resolves in roughly one frame without
        /// either one snapping. It queries non-triggers only, so damage boxes (triggers) are ignored:
        /// only actual bodies separate.
        /// </summary>
        void SoftSeparate()
        {
            if (collider2d == null || separationMask.value == 0)
                return;

            _separationFilter.layerMask = separationMask;

            int count = Physics2D.OverlapCollider(collider2d, _separationFilter, _separationHits);
            if (count == 0)
                return;

            Vector2 push = Vector2.zero;

            for (int j = 0; j < count; j++)
            {
                Collider2D other = _separationHits[j];
                if (!other || other == collider2d)
                    continue;

                ColliderDistance2D d = collider2d.Distance(other);
                if (!d.isOverlapped)
                    continue;

                float pen = -d.distance;
                if (pen > MinPen)
                    push += -d.normal * pen;
            }

            if (push.sqrMagnitude <= Eps)
                return;

            push *= separationStrength;

            if (push.magnitude > maxSeparationPerFrame)
                push = push.normalized * maxSeparationPerFrame;

            body.position += push;
        }
        #endregion

        #region Gizmos
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(
                transform.position,
                transform.position + new Vector3(_velocity.x, _velocity.y, 0f)
            );
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, transform.position + Vector3.forward * _height);
        }
        #endregion
    }
}
