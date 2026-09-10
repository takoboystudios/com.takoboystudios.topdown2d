using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    public enum EntityType
    {
        None,
        Object,
        Player,
        Enemy,
    }

    [CreateAssetMenu(fileName = "Stats", menuName = "Owner/Stats", order = 0)]
    public class Stats : ScriptableObject
    {
        [BoxGroup("Attributes")]
        [OnInspectorInit("UpdateIdIfNotValid")]
        [ReadOnly]
        public int id;

        [BoxGroup("Attributes")]
        [TextArea]
        public string description;

        [BoxGroup("Attributes")]
        public EntityType entityType;

        [BoxGroup("Attributes")]
        public Element element;

        [BoxGroup("Stats")]
        public int maxHealth = 10;

        [BoxGroup("Stats")]
        public int moveSpeed = 100;

        [BoxGroup("Stats")]
        public int weight = 10;

        [BoxGroup("Stats")]
        public float jumpHeight = 2;

        [BoxGroup("Stats")]
        public float timeToJumpApex = 0.32f;

        private void UpdateIdIfNotValid()
        {
            id = Guid.NewGuid().GetHashCode();
        }
    }
}
