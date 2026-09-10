using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Defines how a projectile should be spawned on a specific attack frame.
    ///
    /// Features:
    /// - Multiple direction modes (aim, fixed, forward, random)
    /// - Spread and burst support
    /// - Spawn point configuration
    /// - Pool integration
    ///
    /// Usage:
    /// Serializable spawn pattern config: count, spread, muzzle selection.
    /// </summary>
    [System.Serializable]
    public class ProjectileSpawnData
    {
        public enum DirectionMode
        {
            AimDirection, // Shoot toward aim/look direction
            Fixed, // Fixed angle in degrees
            Forward, // Entity's facing direction
            Random, // Random direction
            TowardTarget, // Toward a target (if available)
        }

        [TabGroup("Projectile", "Setup")]
        [Required]
        [Tooltip("Projectile prefab to spawn")]
        [AssetsOnly]
        [LabelText("Projectile Prefab")]
        public Projectile projectilePrefab;

        [TabGroup("Projectile", "Setup")]
        [Tooltip("Number of projectiles to spawn this frame")]
        [Range(1, 20)]
        [LabelText("Projectile Count")]
        public int projectileCount = 1;

        [TabGroup("Projectile", "Setup")]
        [Tooltip("Pool size for this projectile type (created on init)")]
        [Range(5, 100)]
        [LabelText("Pool Size")]
        public int poolSize = 20;

        [TabGroup("Projectile", "Direction")]
        [Tooltip("How to determine projectile direction")]
        [LabelText("Direction Mode")]
        [EnumToggleButtons]
        public DirectionMode directionMode = DirectionMode.AimDirection;

        [TabGroup("Projectile", "Direction")]
        [ShowIf("directionMode", DirectionMode.Fixed)]
        [Tooltip("Fixed angle in degrees (0 = right, 90 = up)")]
        [Range(0f, 360f)]
        [LabelText("Fixed Angle (°)")]
        public float fixedAngle = 0f;

        [TabGroup("Projectile", "Direction")]
        [Tooltip("Spread angle in degrees (randomizes direction)")]
        [Range(0f, 180f)]
        [LabelText("Spread Angle (°)")]
        public float spreadAngle = 0f;

        [TabGroup("Projectile", "Direction")]
        [ShowIf("@projectileCount > 1")]
        [Tooltip("Distribute projectiles evenly across spread angle")]
        [LabelText("Even Spread")]
        public bool evenSpread = false;

        [TabGroup("Projectile", "Spawn Point")]
        [Tooltip("Use specific spawn point (null = entity position)")]
        [LabelText("Spawn Transform")]
        [SceneObjectsOnly]
        public Transform spawnPoint;

        [TabGroup("Projectile", "Spawn Point")]
        [Tooltip("Offset from spawn point in local space")]
        [LabelText("Spawn Offset")]
        public Vector2 spawnOffset = Vector2.zero;

        [TabGroup("Projectile", "Spawn Point")]
        [Tooltip("Use directional muzzle points (8-way like GunComponent)")]
        [LabelText("Use 8-Way Muzzles")]
        public bool useDirectionalMuzzles = false;

        [TabGroup("Projectile", "Spawn Point")]
        [ShowIf("useDirectionalMuzzles")]
        [SceneObjectsOnly]
        [LabelText("↑ North")]
        public Transform northMuzzle;

        [TabGroup("Projectile", "Spawn Point")]
        [ShowIf("useDirectionalMuzzles")]
        [SceneObjectsOnly]
        [LabelText("↗ North East")]
        public Transform northEastMuzzle;

        [TabGroup("Projectile", "Spawn Point")]
        [ShowIf("useDirectionalMuzzles")]
        [SceneObjectsOnly]
        [LabelText("→ East")]
        public Transform eastMuzzle;

        [TabGroup("Projectile", "Spawn Point")]
        [ShowIf("useDirectionalMuzzles")]
        [SceneObjectsOnly]
        [LabelText("↘ South East")]
        public Transform southEastMuzzle;

        [TabGroup("Projectile", "Spawn Point")]
        [ShowIf("useDirectionalMuzzles")]
        [SceneObjectsOnly]
        [LabelText("↓ South")]
        public Transform southMuzzle;

        [TabGroup("Projectile", "Spawn Point")]
        [ShowIf("useDirectionalMuzzles")]
        [SceneObjectsOnly]
        [LabelText("↙ South West")]
        public Transform southWestMuzzle;

        [TabGroup("Projectile", "Spawn Point")]
        [ShowIf("useDirectionalMuzzles")]
        [SceneObjectsOnly]
        [LabelText("← West")]
        public Transform westMuzzle;

        [TabGroup("Projectile", "Spawn Point")]
        [ShowIf("useDirectionalMuzzles")]
        [SceneObjectsOnly]
        [LabelText("↖ North West")]
        public Transform northWestMuzzle;

        /// <summary>
        /// Calculates the base direction for projectile spawning
        /// </summary>
        public Vector2 GetBaseDirection(Entity owner, Vector2 aimDirection)
        {
            switch (directionMode)
            {
                case DirectionMode.AimDirection:
                    return aimDirection.normalized;

                case DirectionMode.Fixed:
                    float rad = fixedAngle * Mathf.Deg2Rad;
                    return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

                case DirectionMode.Forward:
                    return owner.LastMoveDirection;

                case DirectionMode.Random:
                    float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    return new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle));

                case DirectionMode.TowardTarget:
                    // Could be extended to support target tracking
                    return aimDirection.normalized;

                default:
                    return Vector2.right;
            }
        }

        /// <summary>
        /// Applies spread to a direction
        /// </summary>
        public Vector2 ApplySpread(Vector2 baseDirection, int projectileIndex)
        {
            if (spreadAngle <= 0f)
                return baseDirection;

            float spreadOffset;

            if (evenSpread && projectileCount > 1)
            {
                // Distribute evenly across spread angle
                float step = spreadAngle / (projectileCount - 1);
                spreadOffset = -spreadAngle / 2f + (step * projectileIndex);
            }
            else
            {
                // Random spread
                spreadOffset = Random.Range(-spreadAngle / 2f, spreadAngle / 2f);
            }

            // Convert direction to angle
            float currentAngle = Mathf.Atan2(baseDirection.y, baseDirection.x) * Mathf.Rad2Deg;

            // Apply spread
            float newAngle = currentAngle + spreadOffset;

            // Convert back to direction
            float rad = newAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        /// <summary>
        /// Gets the appropriate muzzle point for a direction (if using directional muzzles)
        /// </summary>
        public Transform GetMuzzleForDirection(Vector2 direction)
        {
            if (!useDirectionalMuzzles)
                return spawnPoint;

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            angle = (angle + 360f) % 360f; // Normalize to 0-360

            if (angle >= 337.5f || angle < 22.5f)
                return eastMuzzle ?? spawnPoint;
            else if (angle >= 22.5f && angle < 67.5f)
                return northEastMuzzle ?? spawnPoint;
            else if (angle >= 67.5f && angle < 112.5f)
                return northMuzzle ?? spawnPoint;
            else if (angle >= 112.5f && angle < 157.5f)
                return northWestMuzzle ?? spawnPoint;
            else if (angle >= 157.5f && angle < 202.5f)
                return westMuzzle ?? spawnPoint;
            else if (angle >= 202.5f && angle < 247.5f)
                return southWestMuzzle ?? spawnPoint;
            else if (angle >= 247.5f && angle < 292.5f)
                return southMuzzle ?? spawnPoint;
            else // angle >= 292.5f && angle < 337.5f
                return southEastMuzzle ?? spawnPoint;
        }

        /// <summary>
        /// Gets the final spawn position for a projectile
        /// </summary>
        public Vector3 GetSpawnPosition(Entity owner, Vector2 direction)
        {
            Transform muzzle = GetMuzzleForDirection(direction);

            if (muzzle != null)
            {
                // Use muzzle position + offset
                Vector3 worldOffset = muzzle.TransformDirection(spawnOffset);
                return muzzle.position + worldOffset;
            }
            else
            {
                // Use owner position + offset
                Vector3 worldOffset = owner.transform.TransformDirection(spawnOffset);
                return owner.Position + (Vector2)worldOffset;
            }
        }

        /// <summary>
        /// Checks if this projectile spawn data is valid
        /// </summary>
        public bool IsValid()
        {
            return projectilePrefab != null;
        }
    }
}
