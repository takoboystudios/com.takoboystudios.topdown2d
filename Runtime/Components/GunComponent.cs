using Sirenix.OdinInspector;
using TakoBoyStudios.Core;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    public class GunComponent : EntityComponent
    {
        [BoxGroup("Projectile")]
        public Projectile bulletPrefab;

        [BoxGroup("Projectile")]
        public int bulletPoolSize = 10;

        [BoxGroup("Firing")]
        public float fireRate = 0.2f;

        [BoxGroup("Accuracy")]
        [Tooltip(
            "Random cone applied to each shot, in degrees. Keep it near zero on anything the player "
                + "fires: aim is locked to eight directions on purpose, and a random cone on top of "
                + "that hands back the imprecision the snap exists to remove. Forgiveness is meant to "
                + "come from generous hitboxes and the bullet's own homing, not from scatter."
        )]
        [SerializeField, Range(0f, 45f)]
        float spreadAngle;

        [BoxGroup("Accuracy")]
        [SerializeField]
        [Tooltip(
            "If true, spread is random within cone. If false, bullets spread in fixed pattern."
        )]
        bool randomSpread = true;

        [BoxGroup("Feel")]
        [SerializeField]
        CameraShakePreset shootShake;

        [FoldoutGroup("Muzzle")]
        public bool usesDirectionalMuzzle;

        [HideIf("usesDirectionalMuzzle")]
        [FoldoutGroup("Muzzle")]
        public Transform muzzle;

        [ShowIf("usesDirectionalMuzzle")]
        [FoldoutGroup("Muzzle")]
        public Transform northMuzzle;

        [ShowIf("usesDirectionalMuzzle")]
        [FoldoutGroup("Muzzle")]
        public Transform northEastMuzzle;

        [ShowIf("usesDirectionalMuzzle")]
        [FoldoutGroup("Muzzle")]
        public Transform eastMuzzle;

        [ShowIf("usesDirectionalMuzzle")]
        [FoldoutGroup("Muzzle")]
        public Transform southEastMuzzle;

        [ShowIf("usesDirectionalMuzzle")]
        [FoldoutGroup("Muzzle")]
        public Transform southMuzzle;

        [ShowIf("usesDirectionalMuzzle")]
        [FoldoutGroup("Muzzle")]
        public Transform southWestMuzzle;

        [ShowIf("usesDirectionalMuzzle")]
        [FoldoutGroup("Muzzle")]
        public Transform westMuzzle;

        [ShowIf("usesDirectionalMuzzle")]
        [FoldoutGroup("Muzzle")]
        public Transform northWestMuzzle;

        float _fireTimer;

        public override void Init(Entity e)
        {
            base.Init(e);

            if (PoolManager.Instance)
                PoolManager.Instance.CreatePool(bulletPrefab.gameObject, bulletPoolSize);
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);

            if (_fireTimer > 0)
                _fireTimer -= Time.deltaTime;
        }

        public void Shoot(Vector2 shootDirection)
        {
            if (_fireTimer > 0)
                return;

            _fireTimer = fireRate;

            // Apply spread to shoot direction
            Vector2 finalDirection = ApplySpread(shootDirection);

            Transform selectedMuzzle = GetMuzzleForDirection(shootDirection);

            GameObject bulletObject = PoolManager.Instance.Acquire(
                bulletPrefab.name,
                selectedMuzzle.position
            );
            Projectile bullet = bulletObject.GetComponent<Projectile>();
            bullet.transform.position = selectedMuzzle.position;
            bullet.Shoot(finalDirection);

            // Camera shake on shoot
            if (shootShake != null)
            {
                ScreenShake.Request(shootShake);
            }
        }

        Vector2 ApplySpread(Vector2 direction)
        {
            if (spreadAngle <= 0f)
                return direction;

            float spreadInDegrees;

            if (randomSpread)
            {
                // Random spread within cone
                spreadInDegrees = Random.Range(-spreadAngle, spreadAngle);
            }
            else
            {
                // Fixed spread (could be extended for burst patterns)
                spreadInDegrees = Random.Range(-spreadAngle, spreadAngle);
            }

            // Convert direction to angle
            float currentAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            // Apply spread
            float newAngle = currentAngle + spreadInDegrees;

            // Convert back to direction
            float rad = newAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        Transform GetMuzzleForDirection(Vector2 shootDirection)
        {
            if (!usesDirectionalMuzzle)
                return muzzle;

            float angle = Mathf.Atan2(shootDirection.y, shootDirection.x) * Mathf.Rad2Deg;
            angle = (angle + 360f) % 360f; // Normalize angle to 0-360

            if (angle >= 337.5f || angle < 22.5f)
                return eastMuzzle;
            else if (angle >= 22.5f && angle < 67.5f)
                return northEastMuzzle;
            else if (angle >= 67.5f && angle < 112.5f)
                return northMuzzle;
            else if (angle >= 112.5f && angle < 157.5f)
                return northWestMuzzle;
            else if (angle >= 157.5f && angle < 202.5f)
                return westMuzzle;
            else if (angle >= 202.5f && angle < 247.5f)
                return southWestMuzzle;
            else if (angle >= 247.5f && angle < 292.5f)
                return southMuzzle;
            else // angle >= 292.5f && angle < 337.5f
                return southEastMuzzle;
        }
    }
}
