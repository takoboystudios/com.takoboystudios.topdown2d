// Hurtbox2D.cs - The box that deals damage, and the values it deals
using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// How high an attack reaches: a class on the attack, compared with the heights of the attacker and
    /// the target (HellWilds #352, after the reference's five classes). Every body's height is its
    /// Entity.Z, how far it is above the floor it stands on.
    ///
    /// The numbers are the saved values, so they never change: Ground and AllHeights were the only two,
    /// and every prefab that already uses them means exactly what it did.
    /// </summary>
    public enum AttackHeight
    {
        /// <summary>Misses a target at or above the clearance height: a jump goes over it.</summary>
        Ground = 0,

        /// <summary>No height check: explosions, the player's shots.</summary>
        AllHeights = 1,

        /// <summary>
        /// Lands on a target within reach of the attacker's own height: the attack's reach plus the
        /// target's half height. A shot at chest height hits a jumping player too.
        /// </summary>
        Normal = 2,

        /// <summary>Only lands on a target at or above the clearance height: a floating body that hurts only someone who jumps into it.</summary>
        Air = 3,
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

        [BoxGroup("Area")]
        [Tooltip(
            "For an area hit (an explosion, a shockwave): hit everything whose hitbox is within this "
                + "many pixels of the box's centre, instead of everything inside the box. 0 uses the box. "
                + "A ring drawn on the floor wants a circle, because a square box reaches further on the "
                + "diagonals than the art shows. Grim's guitar shockwave is 24: one tile past his body."
        )]
        [SerializeField, MinValue(0f)]
        float areaRadius;

        /// <summary>The circle an area hit uses, in pixels, or 0 when it uses the box.</summary>
        public float AreaRadius => areaRadius;

        static readonly Color AreaColor = new Color(1f, 0.45f, 0.1f, 1f);

        [BoxGroup("Height")]
        [Tooltip(
            "Ground: passes under a target that has jumped above the clearance height (a bullet keeps "
                + "going, contact fails to land). AllHeights: hits regardless of the target's height, so "
                + "explosions and the player's shots threaten anything. Normal: lands on a target within "
                + "reach of the attacker's own height, so a shot at chest height still catches a jump "
                + "(the reference's floating bubble). Air: lands only on a target at or above the "
                + "clearance height, for a floating body that hurts only someone who jumps into it. Most "
                + "enemy attacks are Ground; player shots and blasts are AllHeights."
        )]
        [SerializeField]
        AttackHeight m_attackHeight = AttackHeight.Ground;

        [BoxGroup("Height")]
        [Tooltip(
            "For a Ground attack, the target height (in pixels) at and above which this attack passes "
                + "underneath; for Air, the height a target must reach to be hit. Below it a Ground "
                + "attack still lands, so takeoff and landing carry risk while the top of the jump is "
                + "clear. Around 4."
        )]
        [SerializeField, MinValue(0f)]
        float m_groundClearance = 4f;

        [BoxGroup("Height")]
        [Tooltip(
            "For a Normal attack, how far above and below its own height it reaches, in pixels. It "
                + "lands when the target's height is within this plus the target's half height. 8, so a "
                + "Normal attack reaches anything within 16 of its own height: Grim's whole 14 px jump."
        )]
        [SerializeField, MinValue(0f), ShowIf("@m_attackHeight == TakoBoyStudios.TopDown2D.AttackHeight.Normal")]
        float m_heightReach = 8f;

        [BoxGroup("Walls")]
        [Tooltip(
            "On to test walls with a box of its own instead of the damage box. A shot is drawn at the "
                + "hand but travels over the floor under it, and a wall only stops what is on the floor. "
                + "Without this, a player flush under a wall who fires sideways starts the shot inside "
                + "the wall and it dies on its first frame. Off uses the damage box for both."
        )]
        [SerializeField]
        bool separateWallBox;

        [BoxGroup("Walls")]
        [Tooltip(
            "Centre of the wall box, in pixels from the shot. Negative y puts it under the drawn shot, "
                + "down at the shooter's feet: -6 for a shot fired from Grim's hand."
        )]
        [SerializeField, ShowIf("separateWallBox")]
        Vector2 wallBoxCenter = new Vector2(0f, -6f);

        [BoxGroup("Walls")]
        [Tooltip("Size of the wall box, in pixels. Small and flat: the shot's footprint, not its art. Around 4 by 2.")]
        [SerializeField, ShowIf("separateWallBox")]
        Vector2 wallBoxSize = new Vector2(4f, 2f);

        static readonly Color WallBoxColor = new Color(1f, 0.89f, 0.38f, 1f);

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

        int _authoredDamage = -1;

        /// <summary>
        /// Overrides how much this box takes off, for the rest of its life. For a shot that changes
        /// hands: Mina re-profiles a deflected mud ball to a weaker record rather than keeping the
        /// one it was fired with, and ours does the opposite and makes it worth more.
        ///
        /// Pooled bodies keep it, so whatever sets it resets it on acquire.
        /// </summary>
        public void SetDamage(int amount)
        {
            if (_authoredDamage < 0)
                _authoredDamage = damage.damage;
            damage.damage = amount;
        }

        /// <summary>Back to the damage the prefab was authored with.</summary>
        public void ResetDamage()
        {
            if (_authoredDamage >= 0)
                damage.damage = _authoredDamage;
        }
        public AttackHeight AttackHeight => m_attackHeight;
        public float GroundClearance => m_groundClearance;
        public float HeightReach => m_heightReach;

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

            // A box under the owner's lifted visual already moves with its height; any other box is
            // lifted by the owner's height when it is checked (DrawnLift).
            _underLiftedVisual =
                owner != null
                && owner.character != null
                && transform != owner.character
                && transform.IsChildOf(owner.character);
        }

        bool _underLiftedVisual;

        /// <summary>
        /// How far up this box's owner is drawn, which is where the box is checked (#352). What a
        /// hovering shot or a flier's body hits follows its sprite, the same way what a bullet hits on
        /// a target follows the target's sprite (Entity.LiftHitboxes): both sides of a hit are where
        /// the player sees them, and the heights are then compared by class. Walls are still tested on
        /// the floor, because a wall only stops what is on the floor.
        /// </summary>
        float DrawnLift => Owner != null && !_underLiftedVisual ? Owner.Z : 0f;

#if UNITY_EDITOR
        /// <summary>The wall box, drawn in the Scene view whenever this hurtbox is selected, so it can be authored by eye.</summary>
        protected override void DrawExtraGizmos()
        {
            Gizmos.matrix = Matrix4x4.identity;

            if (areaRadius > 0f)
            {
                Gizmos.color = AreaColor;
                Gizmos.DrawWireSphere((Vector2)transform.position + (Vector2)Center, areaRadius);
            }

            if (!separateWallBox)
                return;

            Gizmos.color = WallBoxColor;
            Gizmos.DrawWireCube((Vector2)transform.position + wallBoxCenter, wallBoxSize);
        }
#endif

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

            Vector2 floor = startPos + (Vector2)Center;
            Vector2 origin = floor + new Vector2(0f, DrawnLift);
            float distance = velocity.magnitude * deltaTime;
            Vector2 direction = distance > 0.0001f ? velocity.normalized : Vector2.right;

            // Walls end the projectile. Overlap catches a wall the box is already sitting inside; the
            // sweep catches a wall it would cross before the next frame. The overlap is not
            // redundant: a projectile passes through triggers and so can end a frame parked inside a
            // collider, and a BoxCast that starts inside a collider reports nothing while
            // Physics2D.queriesStartInColliders is off, which the motor sets globally.
            if (!HasBehavior(DamageBoxBehavior.IgnoreWalls))
            {
                // The wall test is the shot's footprint on the floor when it has one, never the art.
                Vector2 wallOrigin = separateWallBox ? startPos + wallBoxCenter : floor;
                Vector2 wallSize = separateWallBox ? wallBoxSize : (Vector2)Size;

                if (separateWallBox)
                    DebugDraw.Box(wallOrigin, wallSize, WallBoxColor);

                int walls = EffectiveWallLayers;
                bool insideWall = Physics2D.OverlapBox(wallOrigin, wallSize, 0f, walls) != null;
                bool crossingWall =
                    !insideWall
                    && distance > 0.0001f
                    && Physics2D
                        .BoxCast(wallOrigin, wallSize, 0f, direction, distance, walls)
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
        bool IsOpposingTarget(Hitbox2D hitbox)
        {
            if (hitbox == null || hitbox.Phased)
                return false;
            if (!HasBehavior(DamageBoxBehavior.HitAllies) && hitbox.Team == Team)
                return false;

            if (MissesByHeight(hitbox))
            {
                // What the height rule decided, where it decided it: a cross on a target this attack
                // went over or under, coloured by the class that let it pass.
                DebugDraw.Cross(hitbox.transform.position, 6f, HeightMissColor);
                return false;
            }

            return true;
        }

        static readonly Color HeightMissColor = new Color(0.55f, 0.85f, 1f, 1f);

        /// <summary>
        /// True when the heights say this attack does not reach the target, so a projectile keeps
        /// going (not a strike) and contact does not land. The heights are the owners' Entity.Z
        /// (#352); the rule itself is <see cref="HeightReaches"/>.
        /// </summary>
        bool MissesByHeight(Hitbox2D hitbox)
        {
            if (m_attackHeight == AttackHeight.AllHeights || hitbox == null)
                return false;

            float attackerZ = Owner != null ? Owner.Z : 0f;
            float targetZ = hitbox.Owner != null ? hitbox.Owner.Z : 0f;
            return !HeightReaches(m_attackHeight, attackerZ, targetZ, m_groundClearance, m_heightReach, hitbox.HalfHeight);
        }

        /// <summary>
        /// The whole height rule, on its own so it can be tested without a scene. Heights in pixels
        /// above the floor.
        /// - Ground: lands while the target is below the clearance height.
        /// - Air: lands once the target is at or above it.
        /// - Normal: lands when the target is within reach plus its half height of the attacker.
        /// - AllHeights: always.
        /// </summary>
        public static bool HeightReaches(
            AttackHeight height,
            float attackerZ,
            float targetZ,
            float clearance,
            float reach,
            float targetHalfHeight
        )
        {
            switch (height)
            {
                case AttackHeight.Ground:
                    return targetZ < clearance;
                case AttackHeight.Air:
                    return targetZ >= clearance;
                case AttackHeight.Normal:
                    return Mathf.Abs(targetZ - attackerZ) <= reach + targetHalfHeight;
                default:
                    return true;
            }
        }

        static readonly System.Comparison<RaycastHit2D> ByDistance =
            (a, b) => a.distance.CompareTo(b.distance);

        public int CheckArea(Vector2 centerPos)
        {
            _areaResults.Clear();
            Vector2 areaCenter = centerPos + (Vector2)Center + new Vector2(0f, DrawnLift);

            // A circle when one is set, which is what a ring on the floor means. The overlay draws
            // the same circle the query uses, so what Shift+D shows is what hits.
            int count;
            if (areaRadius > 0f)
            {
                count = Physics2D.OverlapCircle(areaCenter, areaRadius, _targetFilter, _areaResults);
                DebugDraw.Circle(areaCenter, areaRadius, AreaColor);
            }
            else
            {
                count = Physics2D.OverlapBox(areaCenter, Size, 0f, _targetFilter, _areaResults);
            }

            if (count == 0)
                return 0;

            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                Hitbox2D hitbox = _areaResults[i].GetComponent<Hitbox2D>();
                if (hitbox == null || !CanDamage(hitbox))
                    continue;

                // Straight out from the centre of the blast: each target's hitbox against the centre
                // the area was cast from. A target exactly on the centre has no direction of its own
                // and used to get no push at all; it now goes south, toward the camera, where the
                // player can see it land.
                Vector2 away = (Vector2)hitbox.transform.position - areaCenter;
                Vector2 direction = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.down;
                HitEvent hitEvent = BuildHitEventForArea(centerPos, direction, hitbox);

                // What it decided: a line from the centre along each push.
                DebugDraw.Line(areaCenter, areaCenter + direction * (areaRadius > 0f ? areaRadius : 16f), AreaColor, 1f);

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
            if (MissesByHeight(hitbox)) // the heights say it does not reach (#352)
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
