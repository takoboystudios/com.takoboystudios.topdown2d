// Hurtbox2D.cs - The box that deals damage, and the values it deals
using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// How high an attack reaches. Ground attacks pass harmlessly under a target that has jumped clear
    /// of the floor; AllHeights attacks reach a target whether it is grounded or airborne, which is how
    /// flying enemies and explosions stay dangerous to a jumping player. This is the whole combat side
    /// of the height system: it is a semantic mode, not real 3D.
    /// </summary>
    public enum AttackHeight
    {
        Ground,
        AllHeights,
    }

    /// <summary>
    /// Hurtbox component that deals damage when it collides with Hitbox2D components.
    ///
    /// Features:
    /// - damage values carried on the box itself
    /// - BoxCast and OverlapBox collision detection
    /// - Multi-hit tracking and limiting
    /// - Wall collision detection
    /// - Configurable targeting and behavior
    ///
    /// Workflow:
    /// 1. Attach to the GameObject that should deal damage
    /// 2. Fill in Damage: how much, what element, how hard it shoves
    /// 3. Configure target layers (what can be hit)
    /// 4. Set behavior flags (pass-through, multi-hit, etc.)
    /// 5. Drive it: a Projectile calls UpdateCollisions (overlap first, then a swept anti-tunnel
    ///    pass), contact damage overlaps with CheckArea
    /// </summary>
    public class Hurtbox2D : PhysicsBox
    {
        public override BoxTypes BoxType => BoxTypes.Hurtbox;

        [BoxGroup("Damage")]
        [Tooltip("What a hit from this box is worth. Filled in here rather than in a separate asset.")]
        [SerializeField, HideLabel]
        DamageValues damage = DamageValues.Default;

        [BoxGroup("Targeting")]
        [Tooltip("Layers this hurtbox can damage (usually Player/Enemy)")]
        [SerializeField]
        LayerMask targetLayers;

        [BoxGroup("Targeting")]
        [Tooltip("Layers that block this hurtbox (usually Geometry/Walls)")]
        [SerializeField]
        LayerMask wallLayers;

        /// <summary>
        /// The authored walls plus the temporary combat frame, if a CamLock has one raised. Outside an
        /// encounter the extra term is zero, so this is exactly the serialized mask. It is what keeps
        /// shots inside the fight without every projectile prefab needing to know the layer exists.
        /// </summary>
        int EffectiveWallLayers => wallLayers | CombatRules.ContainmentMask();

        [BoxGroup("Behavior")]
        [Tooltip("Special behaviors for this hurtbox")]
        [SerializeField]
        DamageBoxBehavior behavior = DamageBoxBehavior.None;

        [BoxGroup("Behavior")]
        [Tooltip("Maximum number of targets this hurtbox can hit. Below 1 it hits nothing, so it is clamped.")]
        [SerializeField, MinValue(1), ShowIf("@!HasBehavior(DamageBoxBehavior.HitMultipleTimes)")]
        int maxTargets = 1;

        [BoxGroup("Height")]
        [Tooltip(
            "Ground: passes under a target that has jumped above the clearance height (a bullet keeps "
                + "going, contact fails to land). AllHeights: hits regardless of the target's height, so "
                + "flyers and explosions still threaten an airborne player. Most enemy attacks are "
                + "Ground; player shots and flying-enemy contact should be AllHeights."
        )]
        [SerializeField]
        AttackHeight m_attackHeight = AttackHeight.Ground;

        [BoxGroup("Height")]
        [Tooltip(
            "For a Ground attack, the target height (in pixels) at and above which this attack passes "
                + "underneath. Below it the target is still hit, so takeoff and landing carry risk while "
                + "the apex is clear. Around 4."
        )]
        [SerializeField, MinValue(0f)]
        float m_groundClearance = 4f;

        // Runtime tracking
        readonly HashSet<Hitbox2D> _alreadyHit = new HashSet<Hitbox2D>();
        readonly List<RaycastHit2D> _collisionResults = new List<RaycastHit2D>(16);
        readonly List<Collider2D> _areaResults = new List<Collider2D>(16);
        ContactFilter2D _targetFilter;
        int _totalHits;

        // Properties
        public int TotalHits => _totalHits;
        public IReadOnlyCollection<Hitbox2D> AlreadyHit => _alreadyHit;
        public DamageValues Damage => damage;
        public AttackHeight AttackHeight => m_attackHeight;
        public float GroundClearance => m_groundClearance;

        // Events
        public event Action<HitEvent> OnHitEntity;
        public event Action OnHitGeometry;
        public event Action OnMaxTargetsReached;

        protected override void Awake()
        {
            base.Awake();
            _targetFilter = new ContactFilter2D
            {
                useTriggers = true,
                useLayerMask = true,
                layerMask = targetLayers,
                useNormalAngle = false,
            };
        }

        public override void Init(Entity owner)
        {
            base.Init(owner);
        }

        protected override void Reset()
        {
            base.Reset();
            // Hurtboxes damage the Hitbox layer, where every hitbox lives regardless of team. The
            // Team field, not the layer, decides friend from foe.
            targetLayers = LayerMask.GetMask("Hitbox");
            wallLayers = LayerMask.GetMask("Geometry", "Interact");
        }

        public bool UpdateCollisions(
            Vector2 startPos,
            Vector2 velocity,
            float deltaTime,
            out HitEvent hitEvent
        )
        {
            hitEvent = default;

            Vector2 origin = startPos + (Vector2)Center;
            float distance = velocity.magnitude * deltaTime;
            Vector2 direction = distance > 0.0001f ? velocity.normalized : Vector2.right;

            // Walls end the projectile. Overlap catches a wall the box is already sitting inside; the
            // sweep catches a wall it would cross before the next frame. The overlap is not
            // redundant: a projectile passes through triggers and so can end a frame parked inside a
            // collider, and a BoxCast that starts inside a collider reports nothing while
            // Physics2D.queriesStartInColliders is off, which the motor sets globally.
            if (!HasBehavior(DamageBoxBehavior.IgnoreWalls))
            {
                int walls = EffectiveWallLayers;
                bool insideWall = Physics2D.OverlapBox(origin, Size, 0f, walls) != null;
                bool crossingWall =
                    !insideWall
                    && distance > 0.0001f
                    && Physics2D
                        .BoxCast(origin, Size, 0f, direction, distance, walls)
                        .collider != null;

                if (insideWall || crossingWall)
                {
                    // The projectile only needs to know it struck geometry; there is no victim to
                    // build a full hit event for.
                    OnHitGeometry?.Invoke();
                    return true;
                }
            }

            // Targets. A projectile stops at any enemy it touches: it damages a vulnerable one and
            // dies against an invincible (armoured) one, the way it dies against a wall, rather than
            // sailing through. Overlap first, so a shot sitting inside a hitbox still registers (the
            // case a swept cast misses while queriesStartInColliders is off), then a swept fallback
            // for a fast shot that crossed a target between frames.
            bool struck = StrikeOverlap(origin, ref hitEvent);
            if (!struck && distance > 0.0001f)
                struck = StrikeSweep(origin, direction, distance, ref hitEvent);

            // A piercing shot keeps going; anything else stops at the enemy, damaged or armoured.
            return struck && !HasBehavior(DamageBoxBehavior.PassThroughEnemies);
        }

        /// <summary>
        /// Damages every vulnerable enemy the box is overlapping, and reports whether it touched any
        /// enemy at all, an armoured (invincible) one included, so the projectile knows to stop.
        /// </summary>
        bool StrikeOverlap(Vector2 origin, ref HitEvent hitEvent)
        {
            _areaResults.Clear();
            int count = Physics2D.OverlapBox(origin, Size, 0f, _targetFilter, _areaResults);

            bool struck = false;
            for (int i = 0; i < count; i++)
            {
                Hitbox2D hitbox = _areaResults[i].GetComponent<Hitbox2D>();
                if (!IsOpposingTarget(hitbox))
                    continue;

                struck = true;
                if (!CanDamage(hitbox))
                    continue;

                Vector2 toTarget = ((Vector2)hitbox.transform.position - origin).normalized;
                hitEvent = BuildHitEventForArea(origin, toTarget, hitbox);
                hitbox.Hit(hitEvent);
                _totalHits++;
                if (!HasBehavior(DamageBoxBehavior.HitMultipleTimes))
                    _alreadyHit.Add(hitbox);
                OnHitEntity?.Invoke(hitEvent);
            }

            return struck;
        }

        /// <summary>Anti-tunnel sweep of StrikeOverlap, for a projectile fast enough to cross a target in one step.</summary>
        bool StrikeSweep(Vector2 origin, Vector2 direction, float distance, ref HitEvent hitEvent)
        {
            _collisionResults.Clear();
            int count = Physics2D.BoxCast(origin, Size, 0f, direction, _targetFilter, _collisionResults, distance);
            _collisionResults.Sort(ByDistance);

            bool struck = false;
            for (int i = 0; i < count; i++)
            {
                Hitbox2D hitbox = _collisionResults[i].collider.GetComponent<Hitbox2D>();
                if (!IsOpposingTarget(hitbox))
                    continue;

                struck = true;
                if (CanDamage(hitbox))
                {
                    hitEvent = BuildHitEvent(_collisionResults[i], hitbox);
                    hitbox.Hit(hitEvent);
                    _totalHits++;
                    if (!HasBehavior(DamageBoxBehavior.HitMultipleTimes))
                        _alreadyHit.Add(hitbox);
                    OnHitEntity?.Invoke(hitEvent);
                }

                if (!HasBehavior(DamageBoxBehavior.PassThroughEnemies))
                    break;
            }

            return struck;
        }

        /// <summary>
        /// A hitbox this box would stop at: an enemy of the other team, or anyone if HitAllies. A
        /// phased hitbox (a burrowed enemy underground) is ignored entirely, so shots and melee pass
        /// through it instead of dying on an invisible body. An invincible-but-present hitbox is still
        /// a target: it stops the shot, it just takes no damage.
        /// </summary>
        bool IsOpposingTarget(Hitbox2D hitbox) =>
            hitbox != null
            && !hitbox.Phased
            && !PassesOverTarget(hitbox)
            && (HasBehavior(DamageBoxBehavior.HitAllies) || hitbox.Team != Team);

        /// <summary>
        /// True when this is a Ground attack and the target has jumped to or past the clearance height,
        /// so the attack should slip underneath: a projectile keeps going (not a strike), contact does
        /// not land. The target's height is its owner's fake-Z (Entity.Z = motor height). AllHeights
        /// attacks never pass under.
        /// </summary>
        bool PassesOverTarget(Hitbox2D hitbox) =>
            m_attackHeight == AttackHeight.Ground
            && hitbox != null
            && hitbox.Owner != null
            && hitbox.Owner.Z >= m_groundClearance;

        static readonly System.Comparison<RaycastHit2D> ByDistance =
            (a, b) => a.distance.CompareTo(b.distance);

        public int CheckArea(Vector2 centerPos)
        {
            _areaResults.Clear();
            int count = Physics2D.OverlapBox(
                centerPos + (Vector2)Center,
                Size,
                0f,
                _targetFilter,
                _areaResults
            );

            if (count == 0)
                return 0;

            int hits = 0;
            foreach (var collider in _areaResults)
            {
                Hitbox2D hitbox = collider.GetComponent<Hitbox2D>();
                if (hitbox == null || !CanDamage(hitbox))
                    continue;

                Vector2 direction = ((Vector2)hitbox.transform.position - centerPos).normalized;
                HitEvent hitEvent = BuildHitEventForArea(centerPos, direction, hitbox);

                hitbox.Hit(hitEvent);
                _totalHits++;
                hits++;

                if (!HasBehavior(DamageBoxBehavior.HitMultipleTimes))
                    _alreadyHit.Add(hitbox);

                OnHitEntity?.Invoke(hitEvent);
            }

            if (!HasBehavior(DamageBoxBehavior.HitMultipleTimes) && _totalHits >= maxTargets)
                OnMaxTargetsReached?.Invoke();

            return hits;
        }

        bool CanDamage(Hitbox2D hitbox)
        {
            if (hitbox.Invincible)
                return false;
            // Passive contact damage (an enemy's body touching) does not hurt a target that shrugs
            // off contact, such as a barrel: it is broken by shots and blasts, not by being bumped.
            // Both sides are generic combat properties, so nothing here knows what a barrel is.
            if (HasBehavior(DamageBoxBehavior.ContactDamage) && hitbox.IgnoresContactDamage)
                return false;
            if (PassesOverTarget(hitbox)) // a Ground attack cannot land on a target that jumped clear
                return false;
            if (!HasBehavior(DamageBoxBehavior.HitMultipleTimes) && _alreadyHit.Contains(hitbox))
                return false;
            if (
                !HasBehavior(DamageBoxBehavior.HitMultipleTimes)
                && !HasBehavior(DamageBoxBehavior.PassThroughEnemies)
                && _totalHits >= maxTargets
            )
                return false;
            if (!HasBehavior(DamageBoxBehavior.HitAllies) && hitbox.Team == Team)
                return false;
            return true;
        }

        HitEvent BuildHitEvent(RaycastHit2D hit, Hitbox2D hitbox)
        {
            if (damage.damage <= 0)
                return default;

            Vector2 knockbackDir =
                hit.normal != Vector2.zero
                    ? -hit.normal
                    : (hit.point - (Vector2)transform.position).normalized;

            DamageInfo info = new DamageInfo(damage, Owner, hitbox?.Owner, knockbackDir)
            {
                hitPoint = hit.point,
                hitNormal = hit.normal,
            };

            return new HitEvent(info, hit.collider.gameObject);
        }

        HitEvent BuildHitEventForArea(Vector2 centerPos, Vector2 direction, Hitbox2D hitbox)
        {
            if (damage.damage <= 0)
                return default;

            DamageInfo info = new DamageInfo(damage, Owner, hitbox.Owner, direction)
            {
                hitPoint = centerPos,
                hitNormal = -direction,
            };

            return new HitEvent(info, hitbox.gameObject);
        }

        public void ResetTracking()
        {
            _alreadyHit.Clear();
            _totalHits = 0;
        }

        bool HasBehavior(DamageBoxBehavior flag) => (behavior & flag) != 0;
    }
}
