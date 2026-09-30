using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Darkens the whole gameplay frame, leaving whatever is drawn above it (<see cref="Entity.DrawAbove"/>)
    /// in the light: the Brand's dim, with Grim alone in the foreground (T-200).
    ///
    /// **Stepped, never lerped** (PIXEL_RULES rule 2). A smooth fade blends the scene toward black and
    /// spreads colours the palette does not have. This steps through a few fixed levels, the way an NES
    /// fade walks through darker palettes, and the camera's palette clamp snaps each level onto real
    /// palette colours. It runs on unscaled time, since the world is frozen while it plays.
    ///
    /// **Only the world is dimmed, and a game can tell.** The dim's shader writes its own opacity into
    /// the frame's alpha, and anything opaque drawn in front of it writes 1 back. A full-screen colour
    /// pass can read that to remap the dimmed pixels alone (<see cref="Lookup"/>): Hell Wilds' palette
    /// clamp does, because blending its palette toward black lands on greys and black (the owner,
    /// 2026-09-29: "only grey and black show"), and remapping through a lookup built for the dim keeps
    /// Grim, drawn in front, exactly as he is.
    ///
    /// One dim for the whole screen, so a static owner of one object, built at setup
    /// (<see cref="Ensure"/>, from a player's Init) and only shown and hidden after that.
    /// </summary>
    public sealed class ScreenDim : MonoBehaviour
    {
        /// <summary>The sorting order the dim draws at on the Default layer: above every y-sorted thing in a room.</summary>
        public const int SortingOrder = 30000;

        /// <summary>The order for what stands in front of the dim. Anything above <see cref="SortingOrder"/> works.</summary>
        public const int AboveOrder = SortingOrder + 10;

        static ScreenDim _instance;

        SpriteRenderer _renderer;
        float[] _levels = { 0.3f, 0.5f, 0.65f };
        float _stepTime = 0.06f;
        int _level = -1;
        int _target = -1;
        float _stepTimer;
        Texture3D _lookup;

        /// <summary>
        /// The colour lookup a game's full-screen colour pass should use for the dimmed pixels while the
        /// dim is showing, or null for none. Given with <see cref="Show"/>; the dim only carries it.
        /// </summary>
        public static Texture3D Lookup => _instance != null ? _instance._lookup : null;

        /// <summary>
        /// Builds the dim if it does not exist yet. Setup only: a player's Init calls it, so the object,
        /// its one-pixel sprite and its renderer are made before any fight rather than during one.
        /// </summary>
        public static void Ensure()
        {
            if (_instance != null)
                return;

            GameObject go = new GameObject("[ScreenDim]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ScreenDim>();

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            _instance._renderer = go.AddComponent<SpriteRenderer>();

            // The dim's own shader, which marks what it covers in the frame's alpha. Loaded from the
            // package's Resources so a build always includes it; the plain sprite shader is the fallback,
            // which still dims but marks nothing.
            Material material = Resources.Load<Material>("TopDown2DScreenDim");
            if (material != null)
            {
                _instance._renderer.sharedMaterial = material;
            }
            else
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    _instance._renderer.sharedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            _instance._renderer.sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            _instance._renderer.sortingOrder = SortingOrder;
            _instance._renderer.color = new Color(0f, 0f, 0f, 0f);
            _instance._renderer.enabled = false;
        }

        /// <summary>
        /// Steps the screen down to its darkest level. <paramref name="levels"/> are the dim's opacities
        /// from lightest to darkest, and <paramref name="stepTime"/> is how long each is held on the way,
        /// in seconds. The same levels walk back up on <see cref="Hide"/>. <paramref name="lookup"/> is
        /// what a game's colour pass should remap the dimmed pixels through while it shows (see
        /// <see cref="Lookup"/>); null for none.
        /// </summary>
        public static void Show(float[] levels, float stepTime, Texture3D lookup = null)
        {
            Ensure();
            _instance._lookup = lookup;
            if (levels != null && levels.Length > 0)
                _instance._levels = levels;
            _instance._stepTime = Mathf.Max(0.01f, stepTime);
            _instance._target = _instance._levels.Length - 1;
            _instance._stepTimer = 0f;
            _instance.enabled = true;
        }

        /// <summary>Steps the screen back up to full light, then hides the dim.</summary>
        public static void Hide()
        {
            if (_instance == null)
                return;
            _instance._target = -1;
            _instance._stepTimer = 0f;
        }

        /// <summary>True while any level of dim is showing.</summary>
        public static bool Showing => _instance != null && _instance._level >= 0;

        void LateUpdate()
        {
            // Unscaled, because the world it dims is frozen.
            if (_level != _target)
            {
                _stepTimer -= Time.unscaledDeltaTime;
                if (_stepTimer <= 0f)
                {
                    _stepTimer = _stepTime;
                    _level += _target > _level ? 1 : -1;
                    Apply();
                }
            }

            if (_level < 0)
                return;

            // Cover exactly the frame the players can see, wherever the camera is.
            Rect view = ScreenView.Current;
            if (view.width <= 0f)
                return;

            transform.position = new Vector3(view.center.x, view.center.y, 0f);
            transform.localScale = new Vector3(view.width + 2f, view.height + 2f, 1f);
        }

        void Apply()
        {
            if (_level < 0)
            {
                _renderer.enabled = false;
                return;
            }

            _renderer.enabled = true;
            _renderer.color = new Color(0f, 0f, 0f, _levels[Mathf.Min(_level, _levels.Length - 1)]);
        }
    }
}
