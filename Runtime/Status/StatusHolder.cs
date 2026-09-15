using System.Collections.Generic;
using TakoBoyStudios.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace TakoBoyStudios.TopDown2D.Status
{
    /// <summary>
    /// Every status currently on one entity: which definitions, how many stacks, how long is left,
    /// and who applied each.
    ///
    /// **The machinery is here and the statuses are not.** This knows how to run a status, expire it,
    /// stack it and aggregate what it does; it does not know that any particular one exists. A game
    /// authors <see cref="StatusDefinition"/> assets and this runs whatever it is handed, which is
    /// what lets the entity package be shared without carrying one game's keywords into another.
    ///
    /// **This is the source of truth.** Presentation reads it and never keeps its own idea of what is
    /// burning, which is the lesson from the perch overlay: two definitions of one fact means the
    /// overlay confidently shows you that nothing is wrong.
    ///
    /// Added on demand by <see cref="Of"/> rather than authored onto every prefab, so anything in the
    /// game can be afflicted without someone having remembered to prepare it for that.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatusHolder : MonoBehaviour
    {
        readonly List<StatusInstance> _active = new List<StatusInstance>(4);

        /// <summary>
        /// Definitions that cannot be reapplied yet, and for how long. A definition sets one on itself
        /// from <see cref="StatusDefinition.OnEnded"/> to build an anti-stunlock window, and asks
        /// about it from <see cref="StatusDefinition.CanApply"/>.
        /// </summary>
        readonly Dictionary<StatusDefinition, float> _cooldowns = new Dictionary<StatusDefinition, float>();

        Entity _owner;
        SortingGroup _ownerSorting;

        public Entity Owner => _owner;

        /// <summary>The holder on an entity, creating it the first time one is needed.</summary>
        public static StatusHolder Of(Entity entity)
        {
            if (entity == null)
                return null;

            StatusHolder holder = entity.Statuses;
            if (holder == null)
                holder = entity.gameObject.AddComponent<StatusHolder>();

            holder._owner = entity;
            entity.RegisterStatusHolder(holder);
            return holder;
        }

        void Awake()
        {
            if (_owner == null)
                _owner = GetComponent<Entity>();

            _ownerSorting = GetComponent<SortingGroup>();
        }

        // Recycling is cleared by Entity.OnAcquired, which is the precise moment a pooled body starts
        // a new life. Deliberately not OnDisable: a room teardown deactivates entities that are coming
        // back, and an enemy should not lose the burn on it because the camera moved.

        // ---------------------------------------------------------------- asking

        public bool Has(StatusDefinition definition) => IndexOf(definition) >= 0;

        public int Stacks(StatusDefinition definition)
        {
            int i = IndexOf(definition);
            return i >= 0 ? _active[i].stacks : 0;
        }

        public float Remaining(StatusDefinition definition)
        {
            int i = IndexOf(definition);
            return i >= 0 ? _active[i].remaining : 0f;
        }

        /// <summary>Who applied a status, for kill credit. Null when it is not on.</summary>
        public Entity ApplierOf(StatusDefinition definition)
        {
            int i = IndexOf(definition);
            return i >= 0 ? _active[i].applier : null;
        }

        /// <summary>Every status currently running, for a HUD, a debug readout or a condition.</summary>
        public IReadOnlyList<StatusInstance> Active => _active;

        /// <summary>
        /// How many distinct statuses are on this entity. Distinct, never stacks, so five stacks of
        /// one thing is one affliction.
        /// </summary>
        public int AfflictionCount => _active.Count;

        public bool IsAfflicted => _active.Count > 0;

        /// <summary>What this entity's move speed is multiplied by, from every status at once.</summary>
        public float MoveSpeedMultiplier
        {
            get
            {
                float total = 1f;
                for (int i = 0; i < _active.Count; i++)
                    total *= _active[i].definition.MoveSpeedMultiplier(_active[i]);
                return Mathf.Max(0f, total);
            }
        }

        /// <summary>What damage this entity deals is multiplied by.</summary>
        public float DamageDealtMultiplier
        {
            get
            {
                float total = 1f;
                for (int i = 0; i < _active.Count; i++)
                    total *= _active[i].definition.DamageDealtMultiplier(_active[i]);
                return Mathf.Max(0f, total);
            }
        }

        /// <summary>
        /// What damage this entity takes is multiplied by, for this particular hit. The hit is passed
        /// through so a status can care what kind it is.
        /// </summary>
        public float DamageTakenMultiplier(in DamageValues values)
        {
            float total = 1f;
            for (int i = 0; i < _active.Count; i++)
                total *= _active[i].definition.DamageTakenMultiplier(_active[i], values);
            return Mathf.Max(0f, total);
        }

        // ---------------------------------------------------------------- cooldowns

        /// <summary>Stop a definition being applied again for a while. How an anti-stunlock window is built.</summary>
        public void SetCooldown(StatusDefinition definition, float seconds)
        {
            if (definition != null && seconds > 0f)
                _cooldowns[definition] = seconds;
        }

        public bool IsOnCooldown(StatusDefinition definition) =>
            definition != null && _cooldowns.TryGetValue(definition, out float left) && left > 0f;

        public float CooldownRemaining(StatusDefinition definition) =>
            definition != null && _cooldowns.TryGetValue(definition, out float left) ? left : 0f;

        // ---------------------------------------------------------------- applying

        /// <summary>
        /// Put a status on, or stack and refresh one already there.
        ///
        /// <paramref name="potency"/> is the applier's potency, which scales every number the
        /// definition uses. Passed in rather than read here, because the same keyword applied by an
        /// enemy and by a built-up player is one status with two strengths.
        ///
        /// Returns false when nothing happened: no definition, a dead target, or the definition
        /// refused through <see cref="StatusDefinition.CanApply"/>.
        /// </summary>
        public bool Apply(StatusDefinition definition, int stacks, Entity applier, float potency = 1f)
        {
            if (definition == null || stacks <= 0 || _owner == null || _owner.IsDead)
                return false;

            if (!definition.CanApply(this))
                return false;

            potency = Mathf.Max(0f, potency);
            int index = IndexOf(definition);

            if (index < 0)
            {
                _active.Add(new StatusInstance
                {
                    definition = definition,
                    stacks = Mathf.Min(stacks, definition.MaxStacks),
                    remaining = definition.DurationFor(potency),
                    applier = applier,
                    potency = potency,
                });
                index = _active.Count - 1;
            }
            else
            {
                StatusInstance instance = _active[index];
                instance.stacks = Mathf.Min(instance.stacks + stacks, definition.MaxStacks);
                instance.applier = applier;
                instance.potency = potency;
                instance.remaining = definition.Refresh switch
                {
                    StatusRefresh.Restart => definition.DurationFor(potency),
                    StatusRefresh.Extend => instance.remaining + definition.DurationFor(potency),
                    _ => instance.remaining,
                };
                _active[index] = instance;
            }

            StatusInstance applied = _active[index];
            StatusEvents.ReportApplied(_owner, definition, applied.stacks, applier);
            definition.OnApplied(this, applied);

            // Reaching the cap is the hook a status that becomes another status hangs off. Read the
            // index again first: OnApplied may already have changed what is on this entity.
            int after = IndexOf(definition);
            if (after >= 0 && _active[after].stacks >= definition.MaxStacks)
                definition.OnReachedCap(this, _active[after]);

            return true;
        }

        /// <summary>Take a status off before it ran out. Reports expiry, so listeners stay in step.</summary>
        public void Clear(StatusDefinition definition)
        {
            int index = IndexOf(definition);
            if (index < 0)
                return;

            ReleaseVfx(_active[index]);
            _active.RemoveAt(index);
            if (_owner != null)
                StatusEvents.ReportExpired(_owner, definition);
            definition.OnEnded(this);
        }

        public void ClearAll(bool silent = false)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                StatusDefinition definition = _active[i].definition;
                ReleaseVfx(_active[i]);
                _active.RemoveAt(i);

                if (silent)
                    continue;

                if (_owner != null)
                    StatusEvents.ReportExpired(_owner, definition);
                definition.OnEnded(this);
            }

            _active.Clear();
            _cooldowns.Clear();
        }

        // ---------------------------------------------------------------- paying off

        /// <summary>
        /// Called when the owner is displaced, so any status that pays off on movement can do it.
        ///
        /// Displacement is forced movement from a hit. Walking is not, and neither is being slowed,
        /// which is why this is raised from the damage path and not from every impulse.
        /// </summary>
        public void OnDisplaced()
        {
            if (_owner == null || _owner.IsDead)
                return;

            for (int i = 0; i < _active.Count; i++)
            {
                StatusInstance instance = _active[i];
                float per = instance.definition.DamagePerStackOnDisplace;
                if (per <= 0f)
                    continue;

                int damage = Mathf.Max(1, Mathf.RoundToInt(per * instance.stacks * instance.potency));
                DealStatusDamage(instance.definition, damage, instance.applier);
            }
        }

        /// <summary>
        /// Damage from a status, credited to whoever applied it rather than to the status.
        ///
        /// Goes through the owner's own damage path so death and scoring behave exactly as they do for
        /// a hit, and carries no knockback, so a burn is not a displacement and cannot set off the
        /// statuses that wait for one.
        /// </summary>
        /// <summary>
        /// The status whose damage is being applied right this instant, or null when the hit in flight
        /// is an ordinary attack.
        ///
        /// Ambient context for one synchronous call, read by <see cref="Entity.DealDamage"/> when it
        /// announces the hit. Status damage goes down the same path as a sword swing, and by the time
        /// it arrives there is nothing left in the hit itself to say where it came from: no element,
        /// no knockback, an applier who is just whoever set the status. Something has to carry the
        /// provenance across that call, and the alternative is a second damage path that death,
        /// scoring and hit reporting would all have to be taught about twice.
        ///
        /// Set and restored around the one call that needs it, and never observed from anywhere but
        /// inside that call.
        /// </summary>
        public static StatusDefinition DamagingStatus { get; private set; }

        void DealStatusDamage(StatusDefinition definition, int damage, Entity applier)
        {
            if (damage <= 0 || _owner == null || _owner.IsDead)
                return;

            DamageValues values = new DamageValues
            {
                damage = damage,
                damageType = Element.None,
                knockback = 0f,
                shakeIntensity = 0f,
            };

            DamageInfo info = new DamageInfo(values, applier != null ? applier : _owner, _owner, Vector2.zero);
            StatusEvents.ReportDamaged(_owner, definition, damage, applier);

            // Restored rather than nulled: one status's damage can kill something whose death applies
            // another status, and a plain null on the way out would tell the outer hit it was an
            // attack. Nested is rare and wrong-looking, which is exactly when a guard has to hold.
            StatusDefinition previous = DamagingStatus;
            DamagingStatus = definition;
            try
            {
                _owner.DealDamage(new HitEvent(info, _owner.gameObject));
            }
            finally
            {
                DamagingStatus = previous;
            }
        }

        // ---------------------------------------------------------------- ticking

        void Update()
        {
            if (!Application.isPlaying || _owner == null)
                return;

            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advance every clock. Public so tests can drive it without a running scene, which is the
        /// only way to test a three-second status in less than three seconds.
        /// </summary>
        public void Tick(float dt)
        {
            if (dt <= 0f)
                return;

            TickCooldowns(dt);

            if (_owner.IsDead)
                return;

            // Damage first, then expiry, so a status always gets the tick it was still alive for.
            for (int i = 0; i < _active.Count; i++)
            {
                StatusInstance instance = _active[i];
                float per = instance.definition.DamagePerStackPerSecond;
                if (per <= 0f)
                    continue;

                instance.damageCarry += per * instance.stacks * instance.potency * dt;
                if (instance.damageCarry >= 1f)
                {
                    int whole = Mathf.FloorToInt(instance.damageCarry);
                    instance.damageCarry -= whole;
                    _active[i] = instance;
                    DealStatusDamage(instance.definition, whole, instance.applier);

                    // The damage may have killed the owner or changed what is on it.
                    if (_owner.IsDead)
                        return;
                    if (i >= _active.Count || _active[i].definition != instance.definition)
                        continue;
                }
                else
                {
                    _active[i] = instance;
                }
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                StatusInstance instance = _active[i];

                // A zero duration means it stays until something removes it.
                if (instance.definition.DurationFor(instance.potency) <= 0f)
                    continue;

                instance.remaining -= dt;
                if (instance.remaining > 0f)
                {
                    _active[i] = instance;
                    continue;
                }

                ReleaseVfx(instance);
                _active.RemoveAt(i);
                if (_owner != null)
                    StatusEvents.ReportExpired(_owner, instance.definition);
                instance.definition.OnEnded(this);
            }
        }

        // ---------------------------------------------------------------- looks

        /// <summary>
        /// Makes the pool a status's effect comes from. **Setup only**: a game calls this for each of
        /// its statuses while a room is built, so the first burn in a fight takes an effect rather
        /// than making one. A status whose pool was never warmed simply shows nothing.
        /// </summary>
        public static void PrewarmVfx(StatusDefinition definition, int count = 8)
        {
            PoolManager pools = PoolManager.Instance;
            if (pools == null || definition == null || definition.Vfx == null)
                return;

            string pool = definition.VfxPoolName;
            if (!pools.HasPool(pool))
                pools.CreatePool(pool, definition.Vfx, new PoolConfig(count, count * 4, grow: 4, autoGrow: true));
        }

        /// <summary>
        /// Keeps every status's effect on the victim: taken from the pool when one is missing, placed
        /// on the body at its height and drawn just in front of it, and handed back when the victim
        /// dies. Presentation reads the list and never decides what is on the body.
        /// </summary>
        void LateUpdate()
        {
            if (!Application.isPlaying || _owner == null || _active.Count == 0)
                return;

            bool dead = _owner.IsDead;
            Vector2 at = _owner.Position;
            float height = _owner.Z;
            int order = _ownerSorting != null ? _ownerSorting.sortingOrder + 1 : 0;

            for (int i = 0; i < _active.Count; i++)
            {
                StatusInstance instance = _active[i];
                if (instance.definition == null || instance.definition.Vfx == null)
                    continue;

                if (dead)
                {
                    if (instance.vfx != null)
                    {
                        ReleaseVfx(instance);
                        instance.vfx = null;
                        instance.vfxSorting = null;
                        _active[i] = instance;
                    }

                    continue;
                }

                if (instance.vfx == null && !TryAcquireVfx(ref instance))
                    continue;

                Vector2 offset = instance.definition.VfxOffset;
                instance.vfx.transform.position = new Vector3(
                    Mathf.Round(at.x + offset.x),
                    Mathf.Round(at.y + height + offset.y),
                    0f
                );

                if (instance.vfxSorting != null)
                    instance.vfxSorting.sortingOrder = order;

                _active[i] = instance;
            }
        }

        // A disabled body is either going back to its pool or sitting out a room change; either way its
        // effects go back now and are taken again if it returns still afflicted.
        void OnDisable()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                StatusInstance instance = _active[i];
                if (instance.vfx == null)
                    continue;

                ReleaseVfx(instance);
                instance.vfx = null;
                instance.vfxSorting = null;
                _active[i] = instance;
            }
        }

        bool TryAcquireVfx(ref StatusInstance instance)
        {
            PoolManager pools = PoolManager.Instance;
            string pool = instance.definition.VfxPoolName;
            if (pools == null || !pools.HasPool(pool))
                return false;

            GameObject go = pools.Acquire(pool, _owner.Position);
            if (go == null)
                return false;

            instance.vfx = go;

            // Once per effect taken, when a status starts showing. Never per frame.
            instance.vfxSorting = go.GetComponent<SortingGroup>();
            return true;
        }

        static void ReleaseVfx(in StatusInstance instance)
        {
            if (instance.vfx == null || PoolManager.Instance == null)
                return;

            PoolManager.Instance.Release(instance.vfx);
        }

        void TickCooldowns(float dt)
        {
            if (_cooldowns.Count == 0)
                return;

            _cooldownKeys.Clear();
            foreach (StatusDefinition key in _cooldowns.Keys)
                _cooldownKeys.Add(key);

            for (int i = 0; i < _cooldownKeys.Count; i++)
            {
                float left = _cooldowns[_cooldownKeys[i]] - dt;
                if (left <= 0f)
                    _cooldowns.Remove(_cooldownKeys[i]);
                else
                    _cooldowns[_cooldownKeys[i]] = left;
            }
        }

        readonly List<StatusDefinition> _cooldownKeys = new List<StatusDefinition>(2);

        int IndexOf(StatusDefinition definition)
        {
            if (definition == null)
                return -1;

            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].definition == definition)
                    return i;
            }

            return -1;
        }

        /// <summary>One line for the debug overlay.</summary>
        public string Describe()
        {
            if (_active.Count == 0)
                return "clean";

            using var text = Cysharp.Text.ZString.CreateStringBuilder();
            for (int i = 0; i < _active.Count; i++)
            {
                if (i > 0)
                    text.Append(' ');

                text.Append(_active[i].definition.Id);
                if (_active[i].stacks > 1)
                {
                    text.Append('x');
                    text.Append(_active[i].stacks);
                }
                text.Append('(');
                text.Append(_active[i].remaining.ToString("0.0"));
                text.Append(')');
            }

            return text.ToString();
        }
    }
}
