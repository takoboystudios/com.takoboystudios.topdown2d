using System.Collections.Generic;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// One switch that shows every collision shape in the game, in the Game view, while playing.
    ///
    /// Shapes were only ever visible as gizmos, which means the Scene view, which means not while
    /// actually playing the thing. That is backwards for the shapes that matter: a hurtbox is authored
    /// once and checked once, but room collision is derived from every tile in a room and a tile with
    /// no shape on it leaves a hole you cannot see until you walk through a wall.
    ///
    /// Drawn with GL in immediate mode rather than as sprites or UI. Sprites would mean a GameObject
    /// per box, which goes stale the moment a box moves with its entity or a bullet returns to its
    /// pool. UI Toolkit is screen space, so every box would have to be projected each frame and would
    /// fight the pixel-perfect camera. Immediate mode reads the collider's current bounds at the
    /// moment it draws, so it is right by construction and costs nothing while switched off.
    ///
    /// Nothing needs wiring: the driver installs itself on load, in play mode only.
    /// </summary>
    public static class DebugDraw
    {
        /// <summary>One registered shape. The collider is read live, so a moving box follows for free.</summary>
        readonly struct Shape
        {
            public readonly Collider2D collider;
            public readonly Color color;

            public Shape(Collider2D collider, Color color)
            {
                this.collider = collider;
                this.color = color;
            }
        }

        static readonly List<Shape> _shapes = new();
        static bool _enabled;

        /// <summary>Whether shapes are being drawn. Toggled with the key below, or set from code.</summary>
        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public static void Toggle() => _enabled = !_enabled;

        /// <summary>How many shapes are registered. For the debug readout.</summary>
        public static int ShapeCount => _shapes.Count;

        /// <summary>
        /// Starts drawing this collider while the overlay is on. Safe to call for a pooled object:
        /// a destroyed collider is dropped when it is next drawn rather than needing to be unregistered.
        /// </summary>
        public static void Register(Collider2D collider, Color color)
        {
            if (collider == null)
                return;

            for (int i = 0; i < _shapes.Count; i++)
            {
                if (_shapes[i].collider == collider)
                    return;
            }

            _shapes.Add(new Shape(collider, color));
        }

        public static void Unregister(Collider2D collider)
        {
            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                if (_shapes[i].collider == collider)
                    _shapes.RemoveAt(i);
            }
        }

        /// <summary>Drops everything. A room swap and a scene load both invalidate the whole list.</summary>
        public static void Clear() => _shapes.Clear();

        #region Annotations

        /// <summary>
        /// A thing drawn for one frame because something decided it, rather than because a collider
        /// exists.
        ///
        /// Colliders answer "what is solid". These answer "what does it think it is doing", which is
        /// the question that actually costs time when behaviour looks wrong: where an enemy believes
        /// it is going, which tile it picked, what it is about to do there. Guessing that from the
        /// outside is what makes a bug like this hard, and drawing it makes it obvious.
        ///
        /// Queued and cleared every frame, so nothing has to remember to take an annotation down and
        /// a stale one cannot lie.
        /// </summary>
        public static void Box(Vector2 center, Vector2 size, Color color)
        {
            if (_enabled)
                _annotations.Add(new Annotation(center, size, color));
        }

        /// <summary>A line, drawn as a thin box so it survives the camera zoom like everything else.</summary>
        public static void Line(Vector2 from, Vector2 to, Color color, float thickness = 1.5f)
        {
            if (!_enabled)
                return;

            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.001f)
                return;

            _lines.Add(new Line2(from, to, thickness, color));
        }

        /// <summary>A cross, for a point that has no size of its own.</summary>
        public static void Cross(Vector2 at, float size, Color color)
        {
            Box(at, new Vector2(size, 1.5f), color);
            Box(at, new Vector2(1.5f, size), color);
        }

        internal readonly struct Annotation
        {
            public readonly Vector2 center;
            public readonly Vector2 size;
            public readonly Color color;

            public Annotation(Vector2 center, Vector2 size, Color color)
            {
                this.center = center;
                this.size = size;
                this.color = color;
            }
        }

        internal readonly struct Line2
        {
            public readonly Vector2 from;
            public readonly Vector2 to;
            public readonly float thickness;
            public readonly Color color;

            public Line2(Vector2 from, Vector2 to, float thickness, Color color)
            {
                this.from = from;
                this.to = to;
                this.thickness = thickness;
                this.color = color;
            }
        }

        static readonly List<Annotation> _annotations = new();
        static readonly List<Line2> _lines = new();

        internal static void ForEachAnnotation(System.Action<Bounds, Color> draw)
        {
            for (int i = 0; i < _annotations.Count; i++)
            {
                Annotation a = _annotations[i];
                draw(new Bounds(a.center, a.size), a.color);
            }
        }

        internal static void ForEachLine(System.Action<Line2> draw)
        {
            for (int i = 0; i < _lines.Count; i++)
                draw(_lines[i]);
        }

        /// <summary>Drops the frame's annotations. Called by the renderer once it has drawn them.</summary>
        internal static void ClearAnnotations()
        {
            _annotations.Clear();
            _lines.Clear();
        }

        #endregion

        /// <summary>
        /// Hands each live shape's current bounds to the renderer.
        ///
        /// Destroyed colliders are pruned here rather than being unregistered by whatever owned them.
        /// A pooled bullet is disabled and re-enabled constantly and is destroyed without ceremony at
        /// the end, and asking every one of those paths to keep a debug list tidy is how the list ends
        /// up wrong. A disabled collider is skipped but kept, because it is coming back.
        /// </summary>
        internal static void ForEach(System.Action<Bounds, Color> draw)
        {
            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                Collider2D collider = _shapes[i].collider;

                if (collider == null)
                {
                    _shapes.RemoveAt(i);
                    continue;
                }

                if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                    continue;

                draw(collider.bounds, _shapes[i].color);
            }
        }

        /// <summary>
        /// The colour a box type draws in. The same language the gizmos have always used, so a shape
        /// means the same thing whichever view it is seen in.
        /// </summary>
        public static Color ColorFor(string boxType) =>
            boxType switch
            {
                "Ground" => new Color(1f, 0.94f, 0.85f, 1f),
                "Geometry" => new Color(1f, 0.89f, 0.38f, 1f),
                "Trigger" => new Color(0.27f, 0.94f, 1f, 1f),
                "Hitbox" => new Color(1f, 0.89f, 0.38f, 1f),
                "Hurtbox" => new Color(0.77f, 0.07f, 0f, 1f),
                "Collider" => new Color(0.15f, 0.67f, 0.24f, 1f),
                _ => Color.white,
            };

        #region Driver

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            _shapes.Clear();

            GameObject go = new GameObject("[DebugDraw]") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<DebugDrawRenderer>();
            Object.DontDestroyOnLoad(go);
        }

        #endregion
    }

    /// <summary>
    /// Polls the toggle and does the drawing. Created by <see cref="DebugDraw"/>; never added by hand.
    /// </summary>
    public sealed class DebugDrawRenderer : MonoBehaviour
    {
        /// <summary>Fill opacity. Low enough to read the art through a shape, high enough to see it.</summary>
        public static float FillAlpha = 0.5f;

        /// <summary>Outline opacity. Drawn as quads, so the thickness below is in world pixels.</summary>
        public static float EdgeAlpha = 0.95f;

        /// <summary>Outline thickness in world pixels. 1 reads cleanly on 16px tiles.</summary>
        public static float EdgeThickness = 1f;

        static Material _material;

        void Update()
        {
            // Shift and D together, either shift, so it cannot be hit while typing a single letter
            // into a debug field and cannot collide with the single-key room debug bindings.
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift && Input.GetKeyDown(KeyCode.D))
            {
                DebugDraw.Toggle();
                Debug.Log(
                    $"[DebugDraw] Collision shapes {(DebugDraw.Enabled ? "on" : "off")} "
                        + $"({DebugDraw.ShapeCount} registered)."
                );
            }
        }

        /// <summary>
        /// Draws after everything else in the frame, so a shape is never hidden behind the art it
        /// describes. Reads each collider's bounds as it goes, which is what keeps a box that moved
        /// this frame, or came out of a pool this frame, correct without any bookkeeping.
        /// </summary>
        void OnRenderObject()
        {
            if (!DebugDraw.Enabled)
                return;

            // Only the camera actually rendering the game, or the shapes are drawn once per camera.
            if (Camera.current == null || Camera.current.cameraType == CameraType.Preview)
                return;

            EnsureMaterial();
            _material.SetPass(0);

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);

            // Fills and outlines are both quads, so one GL.Begin covers the lot. GL.LINES was the
            // obvious choice and the wrong one: a GL line is one screen pixel wide however far the
            // camera is zoomed, so at this game's scale the outlines all but vanished.
            GL.Begin(GL.QUADS);
            DebugDraw.ForEach(DrawFill);
            DebugDraw.ForEach(DrawEdges);

            // What things think they are doing, on top of what is solid.
            DebugDraw.ForEachAnnotation(DrawEdges);
            DebugDraw.ForEachLine(DrawLine);
            GL.End();

            GL.PopMatrix();

            // Cleared after drawing rather than before, so whatever queued them this frame does not
            // have to know when the renderer runs.
            DebugDraw.ClearAnnotations();
        }

        static void DrawLine(DebugDraw.Line2 line)
        {
            GL.Color(new Color(line.color.r, line.color.g, line.color.b, EdgeAlpha));

            Vector2 along = (line.to - line.from).normalized;
            Vector2 across = new Vector2(-along.y, along.x) * (line.thickness * 0.5f);

            GL.Vertex3(line.from.x - across.x, line.from.y - across.y, 0f);
            GL.Vertex3(line.from.x + across.x, line.from.y + across.y, 0f);
            GL.Vertex3(line.to.x + across.x, line.to.y + across.y, 0f);
            GL.Vertex3(line.to.x - across.x, line.to.y - across.y, 0f);
        }

        static void DrawFill(Bounds b, Color c)
        {
            GL.Color(new Color(c.r, c.g, c.b, FillAlpha));
            GL.Vertex3(b.min.x, b.min.y, 0f);
            GL.Vertex3(b.max.x, b.min.y, 0f);
            GL.Vertex3(b.max.x, b.max.y, 0f);
            GL.Vertex3(b.min.x, b.max.y, 0f);
        }

        static void DrawEdges(Bounds b, Color c)
        {
            GL.Color(new Color(c.r, c.g, c.b, EdgeAlpha));

            float t = EdgeThickness;

            // Clamp to the shape, so a one-pixel sliver of collision is still drawn as itself rather
            // than as an outline bigger than the thing it describes.
            t = Mathf.Min(t, Mathf.Min(b.size.x, b.size.y) * 0.5f);

            Quad(b.min.x, b.max.y - t, b.max.x, b.max.y);
            Quad(b.min.x, b.min.y, b.max.x, b.min.y + t);
            Quad(b.min.x, b.min.y + t, b.min.x + t, b.max.y - t);
            Quad(b.max.x - t, b.min.y + t, b.max.x, b.max.y - t);
        }

        static void Quad(float xMin, float yMin, float xMax, float yMax)
        {
            GL.Vertex3(xMin, yMin, 0f);
            GL.Vertex3(xMax, yMin, 0f);
            GL.Vertex3(xMax, yMax, 0f);
            GL.Vertex3(xMin, yMax, 0f);
        }

        static void EnsureMaterial()
        {
            if (_material != null)
                return;

            // The built-in coloured shader, which is what every immediate-mode debug drawer uses.
            _material = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
    }
}
