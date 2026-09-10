using Sirenix.OdinInspector;
using TakoBoyStudios.Core;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    public enum BoxTypes
    {
        Ground,
        Geometry,
        Trigger,
        Hitbox,
        Hurtbox,
        Collider,
    }

    public enum WallSide
    {
        Left = -1,
        Right = 1,
    }

    public enum DebugDrawMode
    {
        Always,
        Selected,
        Never,
    }

    [ExecuteInEditMode]
    public class PhysicsBox : EntityComponent
    {
        [SerializeField]
        DebugDrawMode drawMode;

        [SerializeField]
        [ShowIf("@this.BoxType == BoxTypes.Hurtbox || this.BoxType == BoxTypes.Hitbox")]
        Team team;

        [SerializeField]
        Vector3 size = new Vector3(32, 32, 0);

        [SerializeField]
        Vector3 center;

        Collider2D _collider;
        bool _isColliding;

        public virtual BoxTypes BoxType { get; }

        public DebugDrawMode DrawMode
        {
            get => drawMode;
            set => drawMode = value;
        }
        public Vector3 Center
        {
            get => center;
            set => center = value;
        }
        public Vector3 Size
        {
            get => size;
            set => size = value;
        }

        public Team Team => team;

        public override void Init(Entity owner)
        {
            base.Init(owner);

            if (_collider == null)
                _collider = GetComponent<Collider2D>();

            // A box is either a detection volume or a solid. Hitboxes and hurtboxes are detection:
            // they are found by OverlapBox/Cast queries and must never resolve as physical geometry,
            // or a bullet's hurtbox shoves whatever it passes over and enemies bump off each other's
            // damage boxes. Only genuinely solid box types stay non-trigger. This is enforced at Init
            // rather than trusted from the prefab, because it is the one setting that silently breaks
            // every collision in the game when it is wrong.
            if (_collider != null)
                _collider.isTrigger = IsDetectionBox(BoxType);
        }

        /// <summary>
        /// Registers with the global overlay so Shift+D shows this box in the Game view.
        ///
        /// Registered on enable and never unregistered. A pooled bullet is enabled and disabled
        /// constantly, and the overlay skips a disabled collider and drops a destroyed one on its own,
        /// so there is nothing here for a pool path to remember to do and therefore nothing for it to
        /// get wrong.
        /// </summary>
        void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            if (_collider == null)
                _collider = GetComponent<Collider2D>();

            if (_collider != null)
                DebugDraw.Register(_collider, DebugDraw.ColorFor(BoxType.ToString()));
        }

        /// <summary>
        /// True for box types that only detect overlaps, false for box types that block movement.
        /// </summary>
        static bool IsDetectionBox(BoxTypes type)
        {
            return type == BoxTypes.Hitbox || type == BoxTypes.Hurtbox || type == BoxTypes.Trigger;
        }

        #region Debug

#if UNITY_EDITOR

        // EDITOR TOOLS
        public override void OnEditorUpdate()
        {
            if (_collider == null)
                return;

            if (BoxType == BoxTypes.Geometry && _collider is PolygonCollider2D polygonCollider2D)
            {
                // float side = wallSide == WallSide.Right ? 1 : -1;
                //
                // polygonCollider2D.pathCount = 1;
                // polygonCollider2D.SetPath(0, new List<Vector2>
                // {
                //     new Vector2(18 * side, 32),
                //     new Vector2(18 * side, 16),
                //     new Vector2(0, 8),
                //     new Vector2(0, 32)
                // });
            }
            else if (_collider is BoxCollider2D boxCollider)
            {
                boxCollider.offset = center;
                boxCollider.size = size;
            }

            // int layer = LayerMask.NameToLayer(BoxType.ToString());
            // if (layer != -1)
            // {
            //     gameObject.layer = layer;
            // }
        }

        void OnDrawGizmos()
        {
            if (drawMode == DebugDrawMode.Always)
            {
                DrawGizmoBox(transform, _collider, GetEditorColor());
            }
        }

        void OnDrawGizmosSelected()
        {
            if (drawMode == DebugDrawMode.Selected)
            {
                DrawGizmoBox(transform, _collider, GetEditorColor());
            }
        }

        public static void DrawGizmoBox(Transform transform, Collider2D collider, string color)
        {
            if (collider == null)
                return;

            Gizmos.matrix = Matrix4x4.TRS(
                transform.position,
                transform.rotation,
                transform.localScale
            );
            Gizmos.color = color.ToColor(0.5f);

            if (collider is BoxCollider2D boxCollider)
            {
                Vector3 center = boxCollider.offset;
                Vector3 size = boxCollider.size;

                // Draw the box for BoxCollider2D
                Gizmos.DrawCube(center, size);

                Gizmos.color = color.ToColor();

                float sizeX = size.x * 0.5f;
                float sizeY = size.y * 0.5f;

                Gizmos.DrawCube(
                    new Vector3(center.x, center.y + sizeY - 0.5f),
                    new Vector3(size.x, 1)
                );
                Gizmos.DrawCube(
                    new Vector3(center.x, center.y - sizeY + 0.5f),
                    new Vector3(size.x, 1)
                );

                Gizmos.DrawCube(
                    new Vector3(center.x + sizeX - 0.5f, center.y),
                    new Vector3(1, size.y - 2)
                );
                Gizmos.DrawCube(
                    new Vector3(center.x - sizeX + 0.5f, center.y),
                    new Vector3(1, size.y - 2)
                );
            }
            else if (collider is PolygonCollider2D polygonCollider)
            {
                Gizmos.color = color.ToColor();

                for (int i = 0; i < polygonCollider.pathCount; i++)
                {
                    Vector2[] points = polygonCollider.GetPath(i);

                    for (int j = 0; j < points.Length; j++)
                    {
                        Vector2 start =
                            (Vector2)transform.position + polygonCollider.offset + points[j];
                        Vector2 end =
                            (Vector2)transform.position
                            + polygonCollider.offset
                            + points[(j + 1) % points.Length];
                        DrawThickLine(start, end, 1);
                    }
                }
            }
        }

        public static void DrawThickLine(Vector3 start, Vector3 end, float thickness)
        {
            Camera c = Camera.current;
            if (c == null)
                return;

            // Only draw on normal cameras
            if (c.clearFlags == CameraClearFlags.Depth || c.clearFlags == CameraClearFlags.Nothing)
            {
                return;
            }

            // Only draw the line when it is the closest thing to the camera
            // (Remove the Z-test code and other objects will not occlude the line.)
            var prevZTest = UnityEditor.Handles.zTest;
            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            Vector3 direction = (end - start).normalized;
            Vector3 midpoint = (start + end) / 2;

            // Calculate perpendicular vector for thickness
            Vector3 perpendicular =
                Vector3.Cross(direction, Vector3.forward).normalized * thickness / 2;

            // Define the four corners of the rectangle
            Vector3 corner1 =
                midpoint + perpendicular + direction * Vector2.Distance(start, end) / 2;
            Vector3 corner2 =
                midpoint - perpendicular + direction * Vector2.Distance(start, end) / 2;
            Vector3 corner3 =
                midpoint - perpendicular - direction * Vector2.Distance(start, end) / 2;
            Vector3 corner4 =
                midpoint + perpendicular - direction * Vector2.Distance(start, end) / 2;

            // Set color for the outline and fill
            UnityEditor.Handles.color = Gizmos.color;

            // Draw the rectangle with the specified color and outline
            UnityEditor.Handles.DrawSolidRectangleWithOutline(
                new Vector3[] { corner1, corner2, corner3, corner4 },
                Gizmos.color * 0.5f,
                Gizmos.color
            );

            UnityEditor.Handles.zTest = prevZTest;
        }
#endif

        protected virtual string GetEditorColor()
        {
            return BoxType switch
            {
                BoxTypes.Ground => "#fff0da",
                BoxTypes.Geometry => "#ffe362",
                BoxTypes.Trigger => "#44efff",
                BoxTypes.Hitbox => "#ffe362",
                BoxTypes.Hurtbox => "#c51200",
                BoxTypes.Collider => "#27aa3d",
                _ => "#ffffff",
            };
        }
        #endregion
    }
}
