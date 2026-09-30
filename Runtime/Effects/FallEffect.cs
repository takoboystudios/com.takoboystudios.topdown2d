using Sirenix.OdinInspector;
using TakoBoyStudios.Core;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A thing that falls out of the sky onto a spot and goes off there: the note bombs that rain
    /// during the Brand (T-200). Cosmetic: it carries no damage, and what it leaves is an optional
    /// impact effect, which the Brand gives a harmless blast.
    ///
    /// **It falls by height, not by y** (CLAUDE.md, sorting rule two). The ground position slides in
    /// from one side to the spot while the hover height drops from above the top of the screen to zero,
    /// so the sprite draws a diagonal out of the sky and lands exactly on its spot. Faking the fall by
    /// moving y would put it somewhere else in depth for its whole flight.
    ///
    /// It moves itself in its tick rather than through physics, because the world it falls into may
    /// be frozen, and physics does not run while it is (<see cref="WorldFreeze"/>).
    /// </summary>
    public class FallEffect : Effect
    {
        [BoxGroup("Fall")]
        [Tooltip("How fast it comes down, in pixels per second along its diagonal. 360 crosses a screen's height in about half a second.")]
        [SerializeField, MinValue(1f)]
        float fallSpeed = 360f;

        [BoxGroup("Fall")]
        [Tooltip("Played where it lands. Optional; the Brand's is a harmless blast.")]
        [SerializeField]
        Effect impactEffect;

        [BoxGroup("Fall")]
        [Tooltip("How many impacts can be playing at once, across everything that falls.")]
        [SerializeField, MinValue(1), ShowIf("@impactEffect != null")]
        int impactPoolSize = 32;

        Vector2 _from;
        Vector2 _to;
        float _startHeight;
        float _duration;
        float _age;
        bool _falling;

        public Effect ImpactEffect => impactEffect;
        public int ImpactPoolSize => impactPoolSize;

        public override void Init()
        {
            base.Init();

            // A fallback for a FallEffect nobody pooled the impact for. The Brand pools it at setup
            // (Player.PoolBrand), so this finds the pool already there and does nothing.
            if (impactEffect != null && PoolManager.Instance != null && !PoolManager.Instance.HasPool(impactEffect.name))
            {
                PoolManager.Instance.CreatePool(
                    impactEffect.name,
                    impactEffect.gameObject,
                    new PoolConfig(impactPoolSize, impactPoolSize, grow: 0, autoGrow: false)
                );
            }
        }

        public override void OnAcquired()
        {
            base.OnAcquired();
            _falling = false;
            _age = 0f;
            SetHeight(0f);
        }

        /// <summary>
        /// Drops it onto <paramref name="target"/> from <paramref name="height"/> pixels up, its ground
        /// position starting <paramref name="groundOffset"/> away and sliding in as it falls. A ground
        /// offset of (height, 0) comes down from the upper right at 45 degrees.
        /// </summary>
        public void Drop(Vector2 target, float height, Vector2 groundOffset)
        {
            _to = target;
            _from = target + groundOffset;
            _startHeight = Mathf.Max(0f, height);
            float distance = Mathf.Sqrt(groundOffset.sqrMagnitude + _startHeight * _startHeight);
            _duration = Mathf.Max(0.02f, distance / fallSpeed);
            _age = 0f;
            _falling = true;

            Position = _from;
            SetHeight(_startHeight);
        }

        protected override bool Tick(float deltaTime)
        {
            if (!base.Tick(deltaTime))
                return false;

            if (!_falling || !gameObject.activeInHierarchy)
                return true;

            _age += deltaTime;
            float t = Mathf.Clamp01(_age / _duration);

            Position = Vector2.LerpUnclamped(_from, _to, t);
            SetHeight(_startHeight * (1f - t));

            if (t >= 1f)
                Land();

            return true;
        }

        void Land()
        {
            _falling = false;

            if (impactEffect != null && PoolManager.Instance != null)
            {
                GameObject spawned = PoolManager.Instance.Acquire(impactEffect.name, _to, Quaternion.identity);
                Entity impact = spawned != null ? spawned.GetComponent<Entity>() : null;
                if (impact != null && RunsWhileFrozen)
                {
                    // Landing during the freeze: the blast plays through it and in front of the dim, the
                    // same as the bomb that made it.
                    impact.SetRunsWhileFrozen(true);
                    impact.DrawAbove(ScreenDim.AboveOrder);
                }
            }

            Dispose();
        }

        void SetHeight(float height)
        {
            if (character == null)
                return;
            Vector3 local = character.localPosition;
            local.y = height;
            character.localPosition = local;
        }
    }
}
