using UnityEditor;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    [ExecuteInEditMode]
    public class EntityComponent : MonoBehaviour
    {
        protected Entity _owner;
        public Entity Owner => _owner;

        protected virtual void Awake() { }

        protected virtual void Start() { }

        public virtual void Init(Entity owner) => _owner = owner;

        protected virtual void OnSetup() { }

        public virtual void Tick(float dt) { }

        public virtual void PhysicsTick(float fdt) { }

        public virtual void OnEditorUpdate() { }

        protected virtual void Reset()
        {
            if (Application.isPlaying)
                return;

            if (_owner == null)
                Init(GetComponentInParent<Entity>(true));
        }

        protected virtual void OnValidate()
        {
            if (Application.isPlaying)
                return;

            if (_owner == null)
                Init(GetComponentInParent<Entity>(true));
        }

        protected virtual void OnEnable()
        {
#if UNITY_EDITOR
            EditorApplication.update += OnEditorUpdate;
#endif
        }

        protected virtual void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= OnEditorUpdate;
#endif
        }
    }
}
