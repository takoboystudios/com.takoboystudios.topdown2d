using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// One hit, in flight: what it is worth, who threw it, who is being hit, and from which direction.
    ///
    /// The values used to come from a `DamageProfile` asset linked to the hurtbox. They now sit on the
    /// hurtbox itself, for the reasons on <see cref="DamageValues"/>, and this struct carries a copy
    /// rather than a reference so a hit already travelling cannot be changed underneath.
    ///
    /// The maths that has gone with it was not simplification for its own sake: crit chance, knockback
    /// growth, air knockback, the two hit-feel multipliers, multi-hit and the armour flag were all
    /// identical across every damage asset in the project and had never been tuned. Every one of them
    /// was a term that always evaluated to the same thing. What is left is what the game actually does:
    /// element weakness doubles a hit, knockback shoves in the hit's direction, and hit lag and hit
    /// stun follow from the damage and the shove.
    /// </summary>
    public struct DamageInfo
    {
        public DamageValues values;

        public Entity source;
        public Entity target;
        public Vector2 knockbackDirection;
        public Vector2 hitPoint;
        public Vector2 hitNormal;

        int? _cachedFinalDamage;

        public DamageInfo(DamageValues values, Entity source, Entity target, Vector2 knockbackDir)
        {
            this.values = values;
            this.source = source;
            this.target = target;
            this.knockbackDirection = knockbackDir.normalized;
            this.hitPoint = Vector2.zero;
            this.hitNormal = Vector2.zero;

            _cachedFinalDamage = null;
        }

        /// <summary>A hit needs somebody at both ends of it to mean anything.</summary>
        public bool IsValid => source != null && target != null;

        /// <summary>
        /// Damage after the element chart. Cached, because knockback and hit stun both ask for it and
        /// the answer must not change between them.
        /// </summary>
        public int GetFinalDamage()
        {
            if (!IsValid)
                return 0;

            if (_cachedFinalDamage.HasValue)
                return _cachedFinalDamage.Value;

            float damage = values.damage;

            if (CombatRules.IsWeakTo(values.damageType, target.Element))
                damage *= 2f;

            // Statuses, in the one place every hit passes through (T-408). What each one does to a
            // hit is the definition's business, not this struct's: it asks for a multiplier and does
            // not know which statuses exist. Both holders are null on anything that has never carried
            // a status, which is most things.
            Status.StatusHolder attacker = source.Statuses;
            if (attacker != null)
                damage *= attacker.DamageDealtMultiplier;

            // What the attacker's run has made of them (T-347). Statuses are what is happening to
            // them now; this is what they have accumulated, and the two are different questions with
            // different owners. Unset for everything that has no build, which is every enemy.
            damage = RunStats.OutgoingDamageFor(source, damage);

            Status.StatusHolder victim = target.Statuses;
            if (victim != null)
                damage *= victim.DamageTakenMultiplier(values);

            // Rounded up, so a multiplier can never turn a hit that would have hurt into one that
            // does nothing. A hit that connects always costs the victim something.
            int final = Mathf.Max(values.damage > 0 ? 1 : 0, Mathf.RoundToInt(damage));
            _cachedFinalDamage = final;
            return final;
        }

        /// <summary>The shove, in the direction the hit was travelling.</summary>
        public Vector2 GetFinalKnockback() =>
            IsValid ? knockbackDirection * values.knockback : Vector2.zero;

        /// <summary>
        /// The freeze on both parties at the moment of impact, in frames. Scales with damage so a
        /// bigger hit lands heavier.
        /// </summary>
        public int GetHitLagFrames() => IsValid ? (int)(GetFinalDamage() * 0.333f + 1f) : 0;

        /// <summary>How long the victim is locked after the freeze. Follows the shove, not the damage.</summary>
        public int GetHitStunFrames() => IsValid ? (int)(values.knockback * 0.4f) : 0;

        public float GetShakeIntensity() => IsValid ? values.shakeIntensity : 0f;
    }
}
