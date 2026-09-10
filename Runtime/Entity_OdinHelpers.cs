using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    // =================================================================================================================
    // EDITOR ONLY ENTITY DRAWERS
    // =================================================================================================================
    public partial class Entity
    {
#if UNITY_EDITOR
        [SerializeField]
        [BoxGroup("Animations")]
        [HorizontalGroup("Animations/h1"), HideLabel]
        [ValueDropdown("GetAnimationDropdown")]
        [OnValueChanged("OnSelectedAnimationChanged")]
        private string m_selectedAnimation;

        private bool m_playingEditorAnimation;
        private DateTime lastUpdateTime;
        private float simulatedDeltaTime;

        // =================================================================================================================
        // UPDATES FOR THE EDITOR
        // =================================================================================================================

        private void EditorUpdate()
        {
            simulatedDeltaTime = (float)(DateTime.Now - lastUpdateTime).TotalSeconds;
            lastUpdateTime = DateTime.Now;
            EditorUpdateAnimation(simulatedDeltaTime);
        }

        public virtual void EditorUpdateAnimation(float deltaTime)
        {
            if (
                m_playingEditorAnimation
                && !string.IsNullOrEmpty(m_selectedAnimation)
                && IsAnimatorReady()
            )
            {
                if (m_entityAnimator.NeedsToInitialize)
                    m_entityAnimator.UpdateAnimations();

                if (m_entityAnimator.IsDone && !m_entityAnimator.Play(m_selectedAnimation))
                {
                    var animAsset = m_entityAnimator.GetAnimationAsset();
                    if (animAsset != null && animAsset.animations.Count > 0)
                        m_selectedAnimation = animAsset.animations[0].name;
                    return;
                }

                m_entityAnimator.EditorUpdate(deltaTime);
            }
        }

        // =================================================================================================================
        // INSPECTOR EDITOR BUTTONS
        // =================================================================================================================

        [GUIColor("GetButtonColor")]
        [BoxGroup("Animations")]
        [Button("", Icon = SdfIconType.CaretRightFill)]
        [HorizontalGroup("Animations/h1", Width = 20)]
        private void PlayButton()
        {
            m_playingEditorAnimation = !m_playingEditorAnimation;
        }

        // =================================================================================================================
        // ON PROPERTY CHANGED CALLBACKS
        // =================================================================================================================

        private void OnSelectedAnimationChanged()
        {
            if (m_entityAnimator == null)
            {
                m_entityAnimator = GetComponent<EntityAnimator>();
            }

            if (!IsAnimatorReady())
            {
                return;
            }

            m_entityAnimator.UpdateAnimations();
            m_entityAnimator.Play(m_selectedAnimation);
        }

        // =================================================================================================================
        // VALUE GETTERS
        // =================================================================================================================

        private Color GetButtonColor()
        {
            return m_playingEditorAnimation ? Color.green : Color.white;
        }

        private IEnumerable<ValueDropdownItem<string>> GetAnimationDropdown()
        {
            if (m_entityAnimator == null)
            {
                yield return new ValueDropdownItem<string>();
                yield break;
            }

            var animAsset = m_entityAnimator.GetAnimationAsset();
            if (animAsset == null || animAsset.animations == null)
            {
                yield return new ValueDropdownItem<string>();
                yield break;
            }

            yield return new ValueDropdownItem<string>("[none]", string.Empty);

            for (int i = 0; i < animAsset.animations.Count; i++)
            {
                yield return new ValueDropdownItem<string>(
                    animAsset.animations[i].name,
                    animAsset.animations[i].name
                );
            }
        }

        // =================================================================================================================
        // HELPERS
        // =================================================================================================================

        public bool IsAnimatorReady()
        {
            if (m_entityAnimator == null)
                return false;

            var animAsset = m_entityAnimator.GetAnimationAsset();
            return animAsset != null && animAsset.animations != null;
        }

        // =================================================================================================================
        // UNITY CALLBACKS
        // =================================================================================================================

        private void OnEnable()
        {
            UnityEditor.EditorApplication.update += EditorUpdate;
        }

        private void OnDisable()
        {
            m_playingEditorAnimation = false;
            UnityEditor.EditorApplication.update -= EditorUpdate;
        }

#endif
    }
}
