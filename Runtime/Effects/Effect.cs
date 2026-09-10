using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A play-once world effect: explosions, dust puffs, status particles.
    ///
    /// Plays one animation and returns itself to the pool when it finishes. It never destroys
    /// itself. `DestroyAfterAnimation` in the animation package looks like the right fit but is
    /// not: its Destroy mode allocates and collects during gameplay, and its Disable mode leaves
    /// the object outside the pool's bookkeeping, so the pool cannot hand it out again.
    ///
    /// Effects can hurt. An explosion is an effect with a damage box, so the box is optional here
    /// rather than living on a separate projectile that happens to sit still.
    ///
    /// Everything is pooled and pre-warmed at load. Nothing here allocates mid-fight.
    /// </summary>
    public class Effect : Entity
    {
        #region Inspector

        [BoxGroup("Effect")]
        [Tooltip("Animation played on spawn. The effect releases itself when this finishes.")]
        [SerializeField]
        string animationName = "idle";

        [BoxGroup("Damage")]
        [Tooltip(
            "Optional damage box. An explosion has one, a dust puff does not. Leave empty for "
                + "anything purely cosmetic."
        )]
        [SerializeField]
        Hurtbox2D damageBox;

        [BoxGroup("Damage")]
        [Tooltip("First animation frame that damages. Lets the blast land when the art is at its widest, not on frame 0.")]
        [SerializeField, MinValue(0)]
        int damageStartFrame;

        [BoxGroup("Damage")]
        [Tooltip("Last animation frame that damages.")]
        [SerializeField, MinValue(0)]
        int damageEndFrame = 2;

        #endregion

        #region Setup

#if UNITY_EDITOR
        /// <summary>
        /// Builds the damage box for this effect and wires it up.
        ///
        /// Effects are created cosmetic, because most of them are dust and sparkles and a hurtbox
        /// nobody assigned is worse than none at all: it looks configured and silently does
        /// nothing. This is the one click that turns a cosmetic effect into a damaging one.
        /// </summary>
        [BoxGroup("Damage")]
        [Button(ButtonSizes.Medium)]
        [HideIf("@this.damageBox != null")]
        void AddDamageBox()
        {
            Transform existing = transform.Find("DamageBox");
            GameObject box = existing != null ? existing.gameObject : new GameObject("DamageBox");

            if (existing == null)
            {
                box.transform.SetParent(transform, false);
                UnityEditor.Undo.RegisterCreatedObjectUndo(box, "Add Damage Box");
            }

            box.layer = gameObject.layer;

            BoxCollider2D collider = box.GetComponent<BoxCollider2D>();
            if (collider == null)
                collider = box.AddComponent<BoxCollider2D>();

            collider.size = new Vector2(40f, 32f);
            collider.isTrigger = true;

            Hurtbox2D hurtbox = box.GetComponent<Hurtbox2D>();
            if (hurtbox == null)
                hurtbox = box.AddComponent<Hurtbox2D>();

            UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(hurtbox);

            UnityEditor.SerializedProperty size = so.FindProperty("size");
            if (size != null)
                size.vector3Value = new Vector3(40f, 32f, 0f);

            // Targets Player and Enemy, matching every other damage box in the project. A hurtbox
            // with no target layers can never hit anything.
            UnityEditor.SerializedProperty targets = so.FindProperty("targetLayers");
            if (targets != null)
                targets.intValue = (1 << 6) | (1 << 7);

            so.ApplyModifiedProperties();

            damageBox = hurtbox;
            UnityEditor.EditorUtility.SetDirty(this);

            Debug.Log(
                $"[Effect] {name} now has a damage box. Its damage is still zero, or it "
                    + "will deal no damage.",
                this
            );
        }
#endif

        #endregion

        #region Properties

        /// <summary>The animation this effect plays. Used by validation tooling.</summary>
        public string AnimationName => animationName;

        /// <summary>The damage box, or null for a cosmetic effect.</summary>
        public Hurtbox2D DamageBox => damageBox;

        #endregion

        #region Lifecycle

        public override void Init()
        {
            base.Init();
            Play();
        }

        public override void OnAcquired()
        {
            base.OnAcquired();
            Play();
        }

        void Play()
        {
            if (damageBox != null)
                damageBox.ResetTracking();

            if (m_entityAnimator != null && !string.IsNullOrEmpty(animationName))
                m_entityAnimator.Play(animationName);
        }

        #endregion

        #region Tick

        protected override bool Tick(float deltaTime)
        {
            if (!base.Tick(deltaTime))
                return false;

            if (damageBox != null)
            {
                int frame = m_entityAnimator != null ? m_entityAnimator.CurrentFrame : 0;

                // Hits each target once for the whole blast, because tracking is only reset when
                // the effect is next acquired.
                if (frame >= damageStartFrame && frame <= damageEndFrame)
                    damageBox.CheckArea(damageBox.transform.position);
            }

            if (m_entityAnimator == null || m_entityAnimator.IsDone)
                Dispose();

            return true;
        }

        /// <summary>
        /// Effects drive their own single animation. The base would otherwise try to pick between
        /// idle, move and fall based on whether it is grounded, which means nothing here.
        /// </summary>
        protected override void UpdateAnimations(float deltaTime) { }

        #endregion
    }
}
