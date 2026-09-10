using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A set of damage boxes, one per facing direction, placed by hand.
    ///
    /// Why per direction: a melee swing does not land in the same place relative to the body
    /// depending on which way it faces. Measuring the GobGrunt attack art, the weapon sits low and
    /// below the body swinging south, out to the side swinging east, and high above the head
    /// swinging north. No single offset along the facing vector expresses that, because the
    /// difference is in height as much as in reach.
    ///
    /// Why NOT per frame: across the frames where the swing connects, the drawn weapon barely
    /// moves. One box per direction, live for the hit window, matches the art. If an enemy ever
    /// needs the box to travel mid swing, that enemy can move it from its own class, which stays
    /// more readable than a general per frame system nobody else needs.
    ///
    /// Setup: this component sits on a parent, with one child per direction named exactly
    /// n, ne, e, se, s, sw, w, nw. Each child carries a Hurtbox2D and a collider, positioned by
    /// eye against the attack animation. Author only the directions the enemy can actually face;
    /// anything missing falls back to "s", the same way animations do.
    /// </summary>
    public class DirectionalHurtbox : MonoBehaviour
    {
        static readonly string[] Names = { "e", "ne", "n", "nw", "w", "sw", "s", "se" };

        [Tooltip("Boxes found on the children, one per direction. Populated automatically.")]
        [ShowInInspector, ReadOnly]
        readonly Dictionary<string, Hurtbox2D> _boxes = new Dictionary<string, Hurtbox2D>();

        bool _initialized;

        /// <summary>
        /// Caches the child boxes. The Hurtbox2D components themselves are initialised by
        /// Entity.Init, which collects every hurtbox in the hierarchy, so this only builds the
        /// direction lookup.
        /// </summary>
        public void Init()
        {
            _boxes.Clear();

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                Hurtbox2D box = child.GetComponent<Hurtbox2D>();

                if (box == null)
                    continue;

                _boxes[child.name.ToLowerInvariant()] = box;
            }

            if (_boxes.Count == 0)
                Debug.LogWarning(
                    $"{name} has no direction children. Expected children named n, ne, e, se, s, sw, w, nw."
                );

            _initialized = true;
        }

        /// <summary>
        /// The box for a facing direction, or the south box if that direction was never authored,
        /// or null if there is nothing at all.
        /// </summary>
        public Hurtbox2D Get(Vector2 direction)
        {
            if (!_initialized)
                Init();

            string key = DirectionKey(direction);

            if (_boxes.TryGetValue(key, out Hurtbox2D box))
                return box;

            return _boxes.TryGetValue("s", out Hurtbox2D fallback) ? fallback : null;
        }

        /// <summary>Clears hit tracking on every box, so a new swing can connect again.</summary>
        public void ResetTracking()
        {
            foreach (Hurtbox2D box in _boxes.Values)
                box.ResetTracking();
        }

        /// <summary>
        /// Same eight way split the animation system uses, so the box and the sprite always agree
        /// about which direction the enemy is facing.
        /// </summary>
        static string DirectionKey(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return "s";

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            angle = (angle + 360f + 22.5f) % 360f;

            return Names[Mathf.FloorToInt(angle / 45f)];
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            // Draw every authored box so the whole swing set can be judged at once.
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.35f);

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                Hurtbox2D box = child.GetComponent<Hurtbox2D>();

                if (box != null)
                    Gizmos.DrawCube(child.position + box.Center, box.Size);
            }
        }
#endif
    }
}
