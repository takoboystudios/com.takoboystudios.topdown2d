using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A single piece of tossed debris: a wood splinter off a barrel, a shard of glass, a chunk of
    /// rock. It is a real physics body, so it throws out along the ground on a random heading, arcs up
    /// and falls under gravity, bumps off walls on the way, and settles where it lands. Once down it
    /// blinks out the old arcade way, a few quick flickers, then returns to the pool.
    ///
    /// It does not animate. On spawn it picks one shard shape at random and holds it static; a burst
    /// reads as a mix because each piece drew a different shard and threw itself a different way (Tom,
    /// 2026-09-02). Debris is spawned in a handful, never authored as one burst: a barrel that breaks
    /// tosses four or five of these. It carries no damage and hits nothing but walls; it is pure juice.
    /// Being a <see cref="PhysicsEntity"/> is what buys the height, the landing and the wall collisions
    /// for free.
    /// </summary>
    public class DebrisPiece : PhysicsEntity
    {
        [BoxGroup("Toss")]
        [Tooltip("Slowest and fastest ground speed the piece is thrown at, in pixels per second. Each picks a random speed in this range so a burst scatters unevenly and no two pieces land together.")]
        [SerializeField, MinValue(0f)]
        float minSpeed = 60f;

        [BoxGroup("Toss")]
        [Tooltip("See above. Around 150 throws a chunk a couple of tiles before it lands.")]
        [SerializeField, MinValue(0f)]
        float maxSpeed = 150f;

        [BoxGroup("Toss")]
        [Tooltip("Lowest and highest peak the arc reaches, in pixels of fake height. The sprite lifts by this as it flies.")]
        [SerializeField, MinValue(0f)]
        float minPeakHeight = 12f;

        [BoxGroup("Toss")]
        [Tooltip("See above. Around 32 gives a lively pop; lower keeps the toss flat and close.")]
        [SerializeField, MinValue(0f)]
        float maxPeakHeight = 32f;

        [BoxGroup("Toss")]
        [Tooltip("Shortest and longest time in the air before it lands, in seconds. The horizontal throw and the arc both last this long, so a longer flight also throws the piece further.")]
        [SerializeField, MinValue(0.05f)]
        float minFlightTime = 0.45f;

        [BoxGroup("Toss")]
        [Tooltip("See above.")]
        [SerializeField, MinValue(0.05f)]
        float maxFlightTime = 0.7f;

        [BoxGroup("Blink out")]
        [Tooltip("How long the piece flickers on the ground before it vanishes, in seconds. The classic arcade blink-out.")]
        [SerializeField, MinValue(0f)]
        float blinkDuration = 0.35f;

        [BoxGroup("Blink out")]
        [Tooltip("How long each on or off flicker lasts, in seconds. Smaller flickers faster. Around 0.05 reads as an old-school blink.")]
        [SerializeField, MinValue(0.01f)]
        float blinkInterval = 0.05f;

        [BoxGroup("Blink out")]
        [Tooltip("Longest a piece may live if it never cleanly lands (wedged in a corner, thrown at a wall). A safety net so nothing lingers forever, in seconds.")]
        [SerializeField, MinValue(0.1f)]
        float maxAirTime = 1.2f;

        [BoxGroup("Shard")]
        [Tooltip("The shard variants this piece can be, as single-frame animation names (idle-0, idle-1, ...). Each piece plays one at random and holds it, so a burst reads as a mix of shard shapes. Static frames are animations too.")]
        [SerializeField]
        string[] shardAnimations = { "idle-0", "idle-1" };

        SpriteRenderer[] _renderers;
        bool _landed;
        bool _leftGround;
        float _blinkTimer;
        float _airTimer;

        public override void Init()
        {
            base.Init();
            CacheRenderers();
            Toss();
        }

        public override void OnAcquired()
        {
            base.OnAcquired();
            Toss();
        }

        void CacheRenderers()
        {
            Transform root = character != null ? character : transform;
            _renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        }

        /// <summary>Throws the piece off on a fresh random heading and arc, and restarts its life.</summary>
        void Toss()
        {
            _landed = false;
            _leftGround = false;
            _blinkTimer = 0f;
            _airTimer = 0f;

            SetVisible(true);
            PickShard();

            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            m_moveSpeed = Random.Range(minSpeed, maxSpeed);
            SetMoveDirection(direction);

            if (motor != null)
                motor.LaunchArc(Random.Range(minPeakHeight, maxPeakHeight), Random.Range(minFlightTime, maxFlightTime));
        }

        /// <summary>Plays one shard variant at random, holds it, and randomly mirrors it. Each variant
        /// is a single-frame animation, so the burst reads as a mix of shard shapes without anything
        /// moving; the random flip doubles the apparent variety off just two shards.</summary>
        void PickShard()
        {
            if (m_entityAnimator != null && shardAnimations != null && shardAnimations.Length > 0)
                m_entityAnimator.Play(shardAnimations[Random.Range(0, shardAnimations.Length)]);

            if (_renderers == null)
                return;

            bool flipX = Random.value < 0.5f;
            bool flipY = Random.value < 0.5f;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                    continue;
                _renderers[i].flipX = flipX;
                _renderers[i].flipY = flipY;
            }
        }

        protected override bool Tick(float deltaTime)
        {
            // Base ticks the motor, so the arc and the wall-aware horizontal throw both happen here.
            if (!base.Tick(deltaTime))
                return false;

            if (!_landed)
            {
                _airTimer += deltaTime;

                // It has to leave the floor before a landing counts: it is spawned standing on it.
                if (!Grounded)
                    _leftGround = true;

                if ((_leftGround && Grounded) || _airTimer >= maxAirTime)
                {
                    _landed = true;
                    SetMoveDirection(Vector2.zero); // stop sliding along the throw line
                }

                return true;
            }

            // Down: blink out, then gone.
            _blinkTimer += deltaTime;
            bool on = (int)(_blinkTimer / blinkInterval) % 2 == 0;
            SetVisible(on);

            if (_blinkTimer >= blinkDuration)
            {
                SetVisible(true); // leave the shared renderers enabled for the next piece out of the pool
                Dispose();
                return false;
            }

            return true;
        }

        void SetVisible(bool visible)
        {
            if (_renderers == null)
                return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                    _renderers[i].enabled = visible;
            }
        }

        /// <summary>Debris drives its own single tumble; the base's grounded/moving picker means nothing here.</summary>
        protected override void UpdateAnimations(float deltaTime) { }
    }
}
