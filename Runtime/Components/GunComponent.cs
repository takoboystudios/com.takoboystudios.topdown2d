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
        [Tooltip("Off: one muzzle point. On: a point per compass direction, picked by the shot's heading.")]
        public bool usesDirectionalMuzzle;

        [FoldoutGroup("Muzzle")]
        [InfoBox("Muzzle points are edited in the Scene view: select the gun and drag the lettered dots. Whole pixels, with Undo.")]
        [Tooltip("The single muzzle, in pixels from the gun's transform.")]
        public Vector2 muzzleOffset;

        // The eight directional points, in pixels from the gun's transform, compass order from north
        // clockwise: N NE E SE S SW W NW. Hidden: the Scene handles are the editor for these.
        [HideInInspector]
        public Vector2[] muzzleOffsets = new Vector2[8];

        // Which of the eight are authored. An unauthored one falls back (see GetMuzzleForDirection).
        [HideInInspector]
        public bool[] muzzleAuthored = new bool[8];

        // The old way: one child transform per point. Kept, hidden, so an existing prefab migrates
        // itself the first time it is touched (MigrateMuzzleTransforms), then these are cleared.
        [HideInInspector] public Transform muzzle;
        [HideInInspector] public Transform northMuzzle;
        [HideInInspector] public Transform northEastMuzzle;
        [HideInInspector] public Transform eastMuzzle;
        [HideInInspector] public Transform southEastMuzzle;
        [HideInInspector] public Transform southMuzzle;
        [HideInInspector] public Transform southWestMuzzle;
        [HideInInspector] public Transform westMuzzle;
        [HideInInspector] public Transform northWestMuzzle;

        float _fireTimer;

        /// <summary>
        /// Who fires this gun, found once at setup. Stamped on every bullet as its Instigator, so a hit
        /// is credited to the shooter rather than to the bullet: Perks, Relics, kill credit and scoring
        /// all follow the Instigator back to a player, and a bullet with none belongs to nobody.
        /// </summary>
        Entity _shooter;

        protected override void Awake()
        {
            base.Awake();
            _shooter = GetComponentInParent<Entity>();
            // A prefab still carrying child-transform muzzles keeps its points; the children go.
            MigrateMuzzleTransforms(destroyChildren: true);
        }

        public override void Init(Entity e)
        {
            base.Init(e);

            if (e != null)
                _shooter = e;

            // A gun with no bullet is a set of muzzle points (the Corvidden's throw hand): nothing
            // to pool, and the owner fires its own projectile from MuzzleLocal.
            if (PoolManager.Instance && bulletPrefab != null)
                PoolManager.Instance.CreatePool(bulletPrefab.gameObject, bulletPoolSize);
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);

            if (_fireTimer > 0)
                _fireTimer -= Time.deltaTime;
        }

        /// <summary>
        /// Fires one shot from the muzzle for that direction, or nothing while the fire rate is
        /// cooling. Returns the shot so a caller can adjust it (a per-thrower speed, say), or null.
        /// </summary>
        public Projectile Shoot(Vector2 shootDirection)
        {
            if (_fireTimer > 0)
                return null;

            _fireTimer = fireRate;

            // Apply spread to shoot direction
            Vector2 finalDirection = ApplySpread(shootDirection);

            Vector3 muzzleWorld = MuzzleWorldPosition(shootDirection);

            GameObject bulletObject = PoolManager.Instance.Acquire(bulletPrefab.name, muzzleWorld);
            if (bulletObject == null)
                return null;
            Projectile bullet = bulletObject.GetComponent<Projectile>();
            bullet.transform.position = muzzleWorld;

            // After Acquire, because a pooled body clears its Instigator when it is handed out.
            bullet.Instigator = _shooter;
            bullet.Shoot(finalDirection);

            // Camera shake on shoot
            if (shootShake != null)
            {
                ScreenShake.Request(shootShake);
            }

            return bullet;
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

        public static readonly string[] MuzzleNames = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>Compass index from north clockwise for a heading: N 0, NE 1, E 2 ... NW 7.</summary>
        public static int CompassIndex(Vector2 direction)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            return Mathf.RoundToInt(((90f - angle) + 720f) % 360f / 45f) % 8;
        }

        /// <summary>
        /// The local muzzle point for a heading. An unauthored diagonal falls back to the cardinal
        /// beside it, an unauthored cardinal to the single muzzle, so a gun with four points fires
        /// from somewhere sensible.
        /// </summary>
        public Vector2 MuzzleLocal(Vector2 shootDirection)
        {
            if (!usesDirectionalMuzzle)
                return muzzleOffset;

            EnsureArrays();
            int index = CompassIndex(shootDirection);
            if (muzzleAuthored[index])
                return muzzleOffsets[index];

            if (index % 2 == 1)
            {
                bool vertical = Mathf.Abs(shootDirection.y) >= Mathf.Abs(shootDirection.x);
                int cardinal = vertical ? (shootDirection.y >= 0f ? 0 : 4) : (shootDirection.x >= 0f ? 2 : 6);
                if (muzzleAuthored[cardinal])
                    return muzzleOffsets[cardinal];
            }
            return muzzleOffset;
        }

        public Vector3 MuzzleWorldPosition(Vector2 shootDirection) =>
            transform.TransformPoint(MuzzleLocal(shootDirection));

        void EnsureArrays()
        {
            if (muzzleOffsets == null || muzzleOffsets.Length != 8)
                muzzleOffsets = new Vector2[8];
            if (muzzleAuthored == null || muzzleAuthored.Length != 8)
                muzzleAuthored = new bool[8];
        }

        /// <summary>
        /// Copies the old child-transform muzzles into offsets and drops the transforms. Safe to
        /// call any time; does nothing once there are no transforms left. Runs on Awake so a
        /// prefab authored the old way keeps its points, and from the editor so the prefab itself
        /// is updated and the children removed.
        /// </summary>
        public bool MigrateMuzzleTransforms(bool destroyChildren)
        {
            EnsureArrays();
            bool migrated = false;
            if (muzzle != null)
            {
                muzzleOffset = transform.InverseTransformPoint(muzzle.position);
                if (destroyChildren) DestroyPoint(muzzle);
                muzzle = null;
                migrated = true;
            }
            Transform[] old = { northMuzzle, northEastMuzzle, eastMuzzle, southEastMuzzle, southMuzzle, southWestMuzzle, westMuzzle, northWestMuzzle };
            for (int i = 0; i < 8; i++)
            {
                if (old[i] == null)
                    continue;
                muzzleOffsets[i] = transform.InverseTransformPoint(old[i].position);
                muzzleAuthored[i] = true;
                if (destroyChildren) DestroyPoint(old[i]);
                migrated = true;
            }
            northMuzzle = northEastMuzzle = eastMuzzle = southEastMuzzle = southMuzzle = southWestMuzzle = westMuzzle = northWestMuzzle = null;
            return migrated;
        }

        void DestroyPoint(Transform point)
        {
            if (point == null || point == transform)
                return;
            if (Application.isPlaying)
                Destroy(point.gameObject);
            else
                DestroyImmediate(point.gameObject);
        }

#if UNITY_EDITOR
        /// <summary>Four cardinal points 8px out, for a gun authored from scratch. Then drag them.</summary>
        [FoldoutGroup("Muzzle")]
        [Button("Create Cardinal Muzzles")]
        void CreateCardinalMuzzles()
        {
            UnityEditor.Undo.RecordObject(this, "Create Cardinal Muzzles");
            EnsureArrays();
            usesDirectionalMuzzle = true;
            Vector2[] defaults = { new Vector2(0f, 8f), Vector2.zero, new Vector2(8f, 0f), Vector2.zero, new Vector2(0f, -8f), Vector2.zero, new Vector2(-8f, 0f), Vector2.zero };
            for (int i = 0; i < 8; i += 2)
            {
                if (muzzleAuthored[i])
                    continue;
                muzzleOffsets[i] = defaults[i];
                muzzleAuthored[i] = true;
            }
            UnityEditor.EditorUtility.SetDirty(this);
        }

        static readonly Color[] MuzzleColours =
        {
            new Color(0.3f, 1f, 0.4f), new Color(0.6f, 1f, 0.4f), new Color(1f, 0.35f, 0.3f), new Color(1f, 0.6f, 0.3f),
            new Color(0.4f, 0.6f, 1f), new Color(0.6f, 0.5f, 1f), new Color(1f, 0.9f, 0.3f), new Color(0.8f, 0.9f, 0.4f),
        };

        public static Color MuzzleColour(int index) => MuzzleColours[index % MuzzleColours.Length];

        /// <summary>Every muzzle as a coloured, lettered dot, so a gun reads at a glance in the Scene view.</summary>
        void OnDrawGizmos()
        {
            if (!usesDirectionalMuzzle)
            {
                Vector3 at = transform.TransformPoint(muzzleOffset);
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(at, 1.5f);
                UnityEditor.Handles.Label(at + new Vector3(2f, 2f, 0f), "muzzle");
                return;
            }

            EnsureArrays();
            for (int i = 0; i < 8; i++)
            {
                if (!muzzleAuthored[i])
                    continue;
                Vector3 at = transform.TransformPoint(muzzleOffsets[i]);
                Gizmos.color = MuzzleColour(i);
                Gizmos.DrawSphere(at, 1.2f);
                Gizmos.DrawLine(transform.position, at);
                UnityEditor.Handles.Label(at + new Vector3(2f, 2f, 0f), MuzzleNames[i]);
            }
        }
#endif
    }
}
