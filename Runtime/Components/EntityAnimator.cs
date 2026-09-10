using Sirenix.OdinInspector;
using TakoBoyStudios.Animation;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    public class EntityAnimator : EntityComponent
    {
        public enum AnimatorMode
        {
            SimpleFullBody, // Single animator (enemies, simple characters)
            SplitBody, // Top + Bottom split (complex characters)
            FullBodyWithSplit, // All three animators (player, bosses)
        }

        [BoxGroup("Configuration")]
        [SerializeField]
        [OnValueChanged("OnModeChanged")]
        AnimatorMode animatorMode = AnimatorMode.SimpleFullBody;

        [BoxGroup("Animators")]
        [SerializeField]
        [ShowIf(
            "@animatorMode == AnimatorMode.SimpleFullBody || animatorMode == AnimatorMode.FullBodyWithSplit"
        )]
        [Required]
        SpriteAnimation fullBodyAnimator;

        [BoxGroup("Animators")]
        [SerializeField]
        [ShowIf(
            "@animatorMode == AnimatorMode.SplitBody || animatorMode == AnimatorMode.FullBodyWithSplit"
        )]
        [Required]
        SpriteAnimation topAnimator;

        [BoxGroup("Animators")]
        [SerializeField]
        [ShowIf(
            "@animatorMode == AnimatorMode.SplitBody || animatorMode == AnimatorMode.FullBodyWithSplit"
        )]
        [Required]
        SpriteAnimation bottomAnimator;

        [BoxGroup("Fallback Settings")]
        [SerializeField]
        [Tooltip(
            "If true, will attempt to play animations on fullBodyAnimator when split animators fail"
        )]
        bool useFallbackAnimations = true;

        public SpriteAnimation FullBodyAnimator => fullBodyAnimator;
        public SpriteAnimation TopAnimator => topAnimator;
        public SpriteAnimation BottomAnimator => bottomAnimator;
        public AnimatorMode Mode => animatorMode;

        string _lastDirection = "s";
        AnimationPlaybackMode _currentPlaybackMode = AnimationPlaybackMode.Split;

        // Cache for the full-body animator's last request. PlayFullBody is called every frame by a
        // moving enemy; without this it built "base-dir" strings and ran HasAnimation lookups each
        // time only to find the clip had not changed, which is per-frame garbage that scales with the
        // enemy count. The name string is now built only when the request actually changes.
        string _fbReqBase;
        string _fbReqDir;
        string _fbResolved;
        bool _fbFlipX;

        enum AnimationPlaybackMode
        {
            Split, // Currently using top + bottom
            FullBody, // Currently using full body
        }

        protected override void Start()
        {
            base.Start();

            if (topAnimator)
                topAnimator.UpdateAnimations();
            if (bottomAnimator)
                bottomAnimator.UpdateAnimations();
            if (fullBodyAnimator)
                fullBodyAnimator.UpdateAnimations();

            // Initialize based on mode
            InitializeAnimatorVisibility();
        }

        void InitializeAnimatorVisibility()
        {
            switch (animatorMode)
            {
                case AnimatorMode.SimpleFullBody:
                    if (fullBodyAnimator)
                        fullBodyAnimator.gameObject.SetActive(true);
                    if (topAnimator)
                        topAnimator.gameObject.SetActive(false);
                    if (bottomAnimator)
                        bottomAnimator.gameObject.SetActive(false);
                    _currentPlaybackMode = AnimationPlaybackMode.FullBody;
                    break;

                case AnimatorMode.SplitBody:
                    if (fullBodyAnimator)
                        fullBodyAnimator.gameObject.SetActive(false);
                    if (topAnimator)
                        topAnimator.gameObject.SetActive(true);
                    if (bottomAnimator)
                        bottomAnimator.gameObject.SetActive(true);
                    _currentPlaybackMode = AnimationPlaybackMode.Split;
                    break;

                case AnimatorMode.FullBodyWithSplit:
                    // Start with split mode, can switch to full body when needed
                    if (topAnimator)
                        topAnimator.gameObject.SetActive(true);
                    if (bottomAnimator)
                        bottomAnimator.gameObject.SetActive(true);
                    if (fullBodyAnimator)
                        fullBodyAnimator.gameObject.SetActive(false);
                    _currentPlaybackMode = AnimationPlaybackMode.Split;
                    break;
            }
        }

#if UNITY_EDITOR
        void OnModeChanged()
        {
            if (!Application.isPlaying)
                InitializeAnimatorVisibility();
        }
#endif

        // ----------------------------
        // Animation Control
        // ----------------------------

        /// <summary>
        /// Resolves and plays a directional clip on the full-body animator, building the "base-dir"
        /// name only when the request changes. The unchanged per-frame case (a moving enemy asking for
        /// the same move/idle it already plays) allocates nothing and only re-checks that the resolved
        /// clip is still the one playing, re-playing it if some one-shot interrupted it.
        ///
        /// Returns true when a clip for the request exists (played or already playing), false when the
        /// full-body animator has none, so a caller can fall back to the split animators.
        /// </summary>
        bool PlayFullBody(string animBase, string directionStr)
        {
            if (!fullBodyAnimator)
                return false;

            if (_fbResolved == null || _fbReqBase != animBase || _fbReqDir != directionStr)
            {
                string adjusted = GetAdjustedDirection(fullBodyAnimator, animBase, directionStr, out bool flip);
                string full = string.Concat(animBase, "-", adjusted);
                _fbReqBase = animBase;
                _fbReqDir = directionStr;
                _fbFlipX = flip;
                _fbResolved = fullBodyAnimator.HasAnimation(full) ? full : null;
            }

            if (_fbResolved == null)
                return false;

            if (fullBodyAnimator.CurrentAnimationName != _fbResolved)
                fullBodyAnimator.Play(_fbResolved);
            fullBodyAnimator.renderer.flipX = _fbFlipX;
            return true;
        }

        /// <summary>
        /// Play a full-body animation (roll, jump, death, etc.)
        /// Falls back to split animations if full body animator not available.
        /// </summary>
        public void PlayFullBodyAnimation(string animName, Vector2 direction = default)
        {
            // Try to use full body animator if available
            if (fullBodyAnimator && CanSwitchToFullBody())
            {
                SwitchToFullBody();

                string directionStr =
                    direction != Vector2.zero ? GetDirectionString(direction) : _lastDirection;

                if (PlayFullBody(animName, directionStr))
                    return;
            }

            // Fallback: try to play on split animators
            if (useFallbackAnimations && topAnimator && bottomAnimator && CanUseSplitAnimators())
            {
                SwitchToSplit();
                PlaySplitAnimation(animName, direction);
            }
            else if (useFallbackAnimations)
            {
                Debug.LogWarning(
                    $"EntityAnimator: Cannot play animation '{animName}' - no suitable animator available"
                );
            }
        }

        /// <summary>
        /// Play animation on split animators (fallback for full body animations)
        /// </summary>
        void PlaySplitAnimation(string animName, Vector2 direction = default)
        {
            if (!topAnimator || !bottomAnimator)
                return;

            string directionStr =
                direction != Vector2.zero ? GetDirectionString(direction) : _lastDirection;

            // Try to play on both top and bottom
            string topAdjustedDir = GetAdjustedDirection(
                topAnimator,
                animName,
                directionStr,
                out bool topFlipX
            );
            string topAnim = $"{animName}-{topAdjustedDir}";

            if (topAnimator.HasAnimation(topAnim))
            {
                if (topAnimator.CurrentAnimationName != topAnim)
                    topAnimator.Play(topAnim);
                topAnimator.renderer.flipX = topFlipX;
            }

            string bottomAdjustedDir = GetAdjustedDirection(
                bottomAnimator,
                animName,
                directionStr,
                out bool bottomFlipX
            );
            string bottomAnim = $"{animName}-{bottomAdjustedDir}";

            if (bottomAnimator.HasAnimation(bottomAnim))
            {
                if (bottomAnimator.CurrentAnimationName != bottomAnim)
                    bottomAnimator.Play(bottomAnim);
                bottomAnimator.renderer.flipX = bottomFlipX;
            }
        }

        /// <summary>
        /// Update split animations (idle/run/shoot).
        /// Falls back to simple fullbody animations if split not available.
        /// </summary>
        public void UpdateMoveAimAndShoot(Vector2 moveDir, Vector2 shootDir)
        {
            // Try split animation system first
            if (topAnimator && bottomAnimator && CanUseSplitAnimators())
            {
                UpdateSplitAnimations(moveDir, shootDir);
                return;
            }

            // Fallback to simple full body animations
            if (useFallbackAnimations && fullBodyAnimator)
            {
                SwitchToFullBody();
                UpdateSimpleFullBodyAnimation(moveDir, shootDir);
            }
        }

        /// <summary>
        /// Full implementation of split animation logic
        /// </summary>
        void UpdateSplitAnimations(Vector2 moveDir, Vector2 shootDir)
        {
            if (!topAnimator || !bottomAnimator || _owner == null)
                return;

            SwitchToSplit();

            bool isShooting = shootDir != Vector2.zero;
            bool isMoving = moveDir != Vector2.zero;

            // --- Top (shooting / idle upper body) ---
            string shootSuffix = isShooting ? GetDirectionString(shootDir) : _lastDirection;

            if (string.IsNullOrEmpty(shootSuffix))
                shootSuffix = "s";

            string topAnimBase = isShooting ? "shoot" : "idle";
            string topAdjustedDir = GetAdjustedDirection(
                topAnimator,
                topAnimBase,
                shootSuffix,
                out bool topFlipX
            );
            string topAnim = $"{topAnimBase}-{topAdjustedDir}";

            if (topAnimator.HasAnimation(topAnim))
            {
                if (topAnimator.CurrentAnimationName != topAnim)
                    topAnimator.Play(topAnim);
                topAnimator.renderer.flipX = topFlipX;
            }

            // --- Bottom (running / idle lower body) ---
            bool reverse = false;
            string moveSuffix;

            if (isMoving && isShooting)
            {
                int diff = GetAngleDifference(moveDir, shootDir);
                reverse = diff >= 135 && diff <= 225;
                moveSuffix = GetDirectionString(reverse ? shootDir : moveDir);
            }
            else
            {
                moveSuffix = isMoving ? GetDirectionString(moveDir) : _lastDirection;
            }

            if (string.IsNullOrEmpty(moveSuffix))
                moveSuffix = "s";

            string bottomAnimBase = isMoving ? "run" : "idle";
            string bottomAdjustedDir = GetAdjustedDirection(
                bottomAnimator,
                bottomAnimBase,
                moveSuffix,
                out bool bottomFlipX
            );
            string bottomAnim = $"{bottomAnimBase}-{bottomAdjustedDir}";

            if (bottomAnimator.HasAnimation(bottomAnim))
            {
                if (bottomAnimator.CurrentAnimationName != bottomAnim)
                    bottomAnimator.Play(bottomAnim, reversed: reverse);
                bottomAnimator.renderer.flipX = bottomFlipX;
            }

            // --- Persist last direction ---
            if (isMoving || isShooting)
                _lastDirection = GetDirectionString(isShooting ? shootDir : moveDir);
        }

        /// <summary>
        /// Simple full body animation for enemies that don't use split animations
        /// </summary>
        void UpdateSimpleFullBodyAnimation(Vector2 moveDir, Vector2 shootDir)
        {
            if (!fullBodyAnimator)
                return;

            bool isMoving = moveDir != Vector2.zero;
            bool isShooting = shootDir != Vector2.zero;

            // Determine which direction to use
            Vector2 animDirection = Vector2.zero;
            if (isShooting)
                animDirection = shootDir;
            else if (isMoving)
                animDirection = moveDir;

            string directionStr =
                animDirection != Vector2.zero ? GetDirectionString(animDirection) : _lastDirection;

            // Determine animation
            string animBase = isMoving ? "move" : "idle";
            PlayFullBody(animBase, directionStr);

            // Persist direction
            if (animDirection != Vector2.zero)
                _lastDirection = directionStr;
        }

        /// <summary>
        /// Plays a simple animation on whatever animator is available
        /// </summary>
        public void PlaySimpleAnimation(string animName, Vector2 direction = default)
        {
            string directionStr =
                direction != Vector2.zero ? GetDirectionString(direction) : _lastDirection;

            if (
                fullBodyAnimator
                && (
                    _currentPlaybackMode == AnimationPlaybackMode.FullBody
                    || animatorMode == AnimatorMode.SimpleFullBody
                )
            )
            {
                PlayFullBody(animName, directionStr);
            }
            else if (topAnimator && bottomAnimator)
            {
                PlaySplitAnimation(animName, direction);
            }
        }

        // ----------------------------
        // Mode Switching
        // ----------------------------

        bool CanSwitchToFullBody()
        {
            return animatorMode == AnimatorMode.FullBodyWithSplit
                || animatorMode == AnimatorMode.SimpleFullBody;
        }

        bool CanUseSplitAnimators()
        {
            return animatorMode == AnimatorMode.SplitBody
                || animatorMode == AnimatorMode.FullBodyWithSplit;
        }

        void SwitchToFullBody()
        {
            if (_currentPlaybackMode == AnimationPlaybackMode.FullBody)
                return;

            if (!CanSwitchToFullBody())
                return;

            _currentPlaybackMode = AnimationPlaybackMode.FullBody;

            if (topAnimator)
                topAnimator.gameObject.SetActive(false);
            if (bottomAnimator)
                bottomAnimator.gameObject.SetActive(false);
            if (fullBodyAnimator)
                fullBodyAnimator.gameObject.SetActive(true);
        }

        void SwitchToSplit()
        {
            if (_currentPlaybackMode == AnimationPlaybackMode.Split)
                return;

            if (!CanUseSplitAnimators())
                return;

            _currentPlaybackMode = AnimationPlaybackMode.Split;

            if (topAnimator)
                topAnimator.gameObject.SetActive(true);
            if (bottomAnimator)
                bottomAnimator.gameObject.SetActive(true);
            if (fullBodyAnimator)
                fullBodyAnimator.gameObject.SetActive(false);
        }

        // ----------------------------
        // Helpers
        // ----------------------------

        /// <summary>
        /// Adjusts direction for animations that don't have full 8-way sprites.
        /// Flips west-facing directions to use east equivalents if needed.
        /// Falls back to south direction if animation not found.
        /// </summary>
        string GetAdjustedDirection(
            SpriteAnimation animator,
            string animBase,
            string direction,
            out bool flipX
        )
        {
            flipX = false;

            if (!animator)
                return direction;

            string fullAnimName = $"{animBase}-{direction}";

            // If animation exists, use it as-is
            if (animator.HasAnimation(fullAnimName))
                return direction;

            // Try to mirror west-facing directions to east equivalents
            string mirroredDirection = GetMirroredDirection(direction);
            if (mirroredDirection != direction)
            {
                string mirroredAnimName = $"{animBase}-{mirroredDirection}";
                if (animator.HasAnimation(mirroredAnimName))
                {
                    flipX = true;
                    return mirroredDirection;
                }
            }

            // Fallback to south direction if not already trying south
            if (direction != "s")
            {
                string southAnimName = $"{animBase}-s";
                if (animator.HasAnimation(southAnimName))
                {
                    return "s";
                }
            }

            // Final fallback to original direction (will still fail, but at least we tried)
            return direction;
        }

        /// <summary>
        /// Returns the horizontally mirrored direction (w->e, nw->ne, sw->se)
        /// </summary>
        static string GetMirroredDirection(string direction)
        {
            switch (direction)
            {
                case "w":
                    return "e";
                case "nw":
                    return "ne";
                case "sw":
                    return "se";
                case "e":
                    return "w";
                case "ne":
                    return "nw";
                case "se":
                    return "sw";
                default:
                    return direction; // n, s don't mirror
            }
        }

        static readonly string[] _directionStrings = { "e", "ne", "n", "nw", "w", "sw", "s", "se" };

        static string GetDirectionString(Vector2 direction)
        {
            if (direction == Vector2.zero)
                return string.Empty;

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            angle = (angle + 360f + 22.5f) % 360f;

            int index = Mathf.FloorToInt(angle / 45f);
            return _directionStrings[index];
        }

        static int GetAngleDifference(Vector2 a, Vector2 b)
        {
            float angleA = Mathf.Atan2(a.y, a.x) * Mathf.Rad2Deg;
            float angleB = Mathf.Atan2(b.y, b.x) * Mathf.Rad2Deg;
            return Mathf.Abs(Mathf.RoundToInt(Mathf.DeltaAngle(angleA, angleB)));
        }

        // ----------------------------
        // Public Utilities
        // ----------------------------

        /// <summary>
        /// Check if the animator has a specific animation available.
        /// Checks both with direction suffix (e.g., "idle-s") and without (e.g., "idle").
        /// </summary>
        public bool HasAnimation(string animName, string direction = null)
        {
            // First, check if the animation exists without direction suffix (for projectiles, etc.)
            if (fullBodyAnimator && fullBodyAnimator.HasAnimation(animName))
                return true;

            if (topAnimator && topAnimator.HasAnimation(animName))
                return true;

            if (bottomAnimator && bottomAnimator.HasAnimation(animName))
                return true;

            // Then check with direction suffix (for directional animations)
            string dir = direction ?? _lastDirection;
            string fullAnimName = $"{animName}-{dir}";

            if (fullBodyAnimator && fullBodyAnimator.HasAnimation(fullAnimName))
                return true;

            if (topAnimator && topAnimator.HasAnimation(fullAnimName))
                return true;

            if (bottomAnimator && bottomAnimator.HasAnimation(fullAnimName))
                return true;

            return false;
        }

        /// <summary>
        /// Get current animation length from active animator
        /// </summary>
        public float GetCurrentAnimationLength()
        {
            if (_currentPlaybackMode == AnimationPlaybackMode.FullBody && fullBodyAnimator)
                return fullBodyAnimator.GetCurrentAnimationLength();

            if (_currentPlaybackMode == AnimationPlaybackMode.Split && topAnimator)
                return topAnimator.GetCurrentAnimationLength();

            return 0f;
        }

        /// <summary>
        /// Gets the currently active SpriteAnimation (for backwards compatibility and direct access)
        /// </summary>
        public SpriteAnimation GetActiveAnimator()
        {
            if (_currentPlaybackMode == AnimationPlaybackMode.FullBody && fullBodyAnimator)
                return fullBodyAnimator;

            // For split mode, return top animator as primary
            if (_currentPlaybackMode == AnimationPlaybackMode.Split && topAnimator)
                return topAnimator;

            return fullBodyAnimator; // Fallback
        }

        /// <summary>
        /// Gets or sets the paused state for all animators
        /// </summary>
        public bool Paused
        {
            get
            {
                SpriteAnimation active = GetActiveAnimator();
                return active ? active.paused : false;
            }
            set
            {
                if (fullBodyAnimator)
                    fullBodyAnimator.paused = value;
                if (topAnimator)
                    topAnimator.paused = value;
                if (bottomAnimator)
                    bottomAnimator.paused = value;
            }
        }

        /// <summary>
        /// Returns true if the current animation is done playing
        /// </summary>
        public bool IsDone
        {
            get
            {
                SpriteAnimation active = GetActiveAnimator();
                return active ? active.IsDone : true;
            }
        }

        /// <summary>
        /// Gets the current frame of the active animator
        /// </summary>
        public int CurrentFrame
        {
            get
            {
                SpriteAnimation active = GetActiveAnimator();
                return active ? active.CurrentFrame : 0;
            }
        }

        /// <summary>
        /// Gets the current animation name from the active animator
        /// </summary>
        public string CurrentAnimationName
        {
            get
            {
                SpriteAnimation active = GetActiveAnimator();
                return active ? active.CurrentAnimationName : string.Empty;
            }
        }

        /// <summary>
        /// Update animation assets (used by editor)
        /// </summary>
        public void UpdateAnimations()
        {
            if (fullBodyAnimator)
                fullBodyAnimator.UpdateAnimations();
            if (topAnimator)
                topAnimator.UpdateAnimations();
            if (bottomAnimator)
                bottomAnimator.UpdateAnimations();
        }

        /// <summary>
        /// Check if any animator needs to be initialized
        /// </summary>
        public bool NeedsToInitialize
        {
            get
            {
                if (fullBodyAnimator && fullBodyAnimator.NeedsToInitialize)
                    return true;
                if (topAnimator && topAnimator.NeedsToInitialize)
                    return true;
                if (bottomAnimator && bottomAnimator.NeedsToInitialize)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// Get the animation asset from the active animator (for editor use)
        /// </summary>
        public SpriteAnimationAsset GetAnimationAsset()
        {
            SpriteAnimation active = GetActiveAnimator();
            return active ? active.animationAsset : null;
        }

        /// <summary>
        /// Set animation asset on all animators (for editor use)
        /// </summary>
        public void SetAnimationAsset(SpriteAnimationAsset asset)
        {
            if (fullBodyAnimator)
                fullBodyAnimator.animationAsset = asset;
            if (topAnimator)
                topAnimator.animationAsset = asset;
            if (bottomAnimator)
                bottomAnimator.animationAsset = asset;
        }

        /// <summary>
        /// Play animation (simplified API that auto-detects mode)
        /// </summary>
        public bool Play(string animName)
        {
            SpriteAnimation active = GetActiveAnimator();

            // An exact clip name always wins. This is what makes whole names like "attack-charge-s",
            // "pop-into-ground" or "spawn-intro" work: they are complete clip names, not
            // base-plus-direction. The old code split on the first hyphen and read the second token
            // as a facing, so anything with more than one hyphen (or a non-direction second token)
            // silently failed to play and the animator stuck on the last name that happened to
            // parse, which was "fire-s".
            if (active && active.HasAnimation(animName))
            {
                // A literal clip is authored in its own orientation, so it is never mirrored. Clear
                // any flip left over from a previously mirrored directional animation.
                if (active.renderer != null)
                    active.renderer.flipX = false;
                return active.Play(animName);
            }

            // No exact clip. Treat a trailing direction token as a facing so mirrored directions
            // still resolve, e.g. "run-w" has no clip of its own and plays "run-e" flipped.
            int lastDash = animName.LastIndexOf('-');
            if (lastDash > 0 && lastDash < animName.Length - 1)
            {
                Vector2 dir = DirectionStringToVector(animName.Substring(lastDash + 1));
                if (dir != Vector2.zero)
                {
                    PlaySimpleAnimation(animName.Substring(0, lastDash), dir);
                    return true;
                }
            }

            // Last resort: hand the literal name to the active animator.
            if (active)
                return active.Play(animName);

            return false;
        }

        /// <summary>
        /// Editor update for animation preview. Currently a no-op.
        ///
        /// This used to call SpriteAnimation.EditorUpdateAnimation(deltaTime) to advance frames
        /// while previewing from the Entity inspector. That method is now private in
        /// com.takoboystudios.animation, and the package drives its own preview by subscribing
        /// EditorUpdate to EditorApplication.update in OnEnable.
        ///
        /// Consequence: Entity level preview still selects an animation but no longer plays it.
        /// To restore, the package needs a public frame advance (its private OnUpdate(float) is
        /// the method that does the work). See TESTING.md T-112.
        /// </summary>
        public void EditorUpdate(float deltaTime) { }

        /// <summary>
        /// Helper to convert direction string to Vector2
        /// </summary>
        static Vector2 DirectionStringToVector(string dir)
        {
            switch (dir.ToLower())
            {
                case "n":
                    return Vector2.up;
                case "ne":
                    return new Vector2(1, 1).normalized;
                case "e":
                    return Vector2.right;
                case "se":
                    return new Vector2(1, -1).normalized;
                case "s":
                    return Vector2.down;
                case "sw":
                    return new Vector2(-1, -1).normalized;
                case "w":
                    return Vector2.left;
                case "nw":
                    return new Vector2(-1, 1).normalized;
                default:
                    return Vector2.zero;
            }
        }
    }
}
