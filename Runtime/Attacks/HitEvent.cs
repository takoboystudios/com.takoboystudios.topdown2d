using System;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    [Flags]
    public enum DamageBoxBehavior
    {
        None = 0,
        IgnoreWalls = 1 << 0, // Pass through geometry
        HitAllies = 1 << 1, // Damage same team
        HitMultipleTimes = 1 << 2, // Same target can be hit repeatedly
        PassThroughEnemies = 1 << 3, // Continue after hitting enemy
        ContactDamage = 1 << 4, // Passive body-contact damage, not an active strike: shrugged off by contact-immune targets
    }

    [System.Serializable]
    public struct HitEvent
    {
        public DamageInfo damageInfo;
        public GameObject victim;

        public HitEvent(DamageInfo info, GameObject victim)
        {
            this.damageInfo = info;
            this.victim = victim;
        }
    }
}
