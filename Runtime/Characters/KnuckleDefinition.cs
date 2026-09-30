using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A Knuckle ability: what is on the melee button, and what the HUD's Knuckle slot will draw.
    /// Grim's is the Guitar Bash (T-454).
    ///
    /// A swing is an **arc**: everything whose hitbox is within <see cref="reach"/> of the body and
    /// inside <see cref="arcDegrees"/> of the aim is hit once, on the frames the art draws the swing
    /// (<see cref="activeStartFrame"/> to <see cref="activeEndFrame"/>). The frames are read off the
    /// animation, never timed beside it, the same way the Grip's release frame is. What it hits is
    /// shoved along the aim, not out from the body: everything one swing connects with moves the same
    /// way, which is how the reference's melee pushes and what "you point, it goes there" asks for.
    ///
    /// A shot built to be knocked back (<see cref="Projectile.CanBeDeflected"/>) that is inside the arc
    /// is sent back along the aim, pulled onto an enemy close to that line if there is one (T-454).
    ///
    /// No invincibility anywhere: the parry window is the active frames and nothing more (T-454).
    /// </summary>
    [CreateAssetMenu(menuName = "TopDown2D/Knuckle", fileName = "Knuckle")]
    public class KnuckleDefinition : SlotDefinition
    {
        [BoxGroup("Timing")]
        [Tooltip(
            "First frame of the animation that hits: where the art draws the swing coming through. "
                + "Grim's guitar smash draws it on frame 2, after a 240 ms wind-up."
        )]
        [SerializeField, MinValue(0)]
        int activeStartFrame = 2;

        [BoxGroup("Timing")]
        [Tooltip("Last frame that hits. Grim's smash draws the swing on frames 2 and 3, about a tenth of a second.")]
        [SerializeField, MinValue(0)]
        int activeEndFrame = 3;

        [BoxGroup("Timing")]
        [Tooltip(
            "Pressing again once the swing has started queues the next one, which starts the frame this "
                + "one ends, so a mash is never lost and never cuts a swing short. Off drops those presses."
        )]
        [SerializeField]
        bool queueNextSwing = true;

        [BoxGroup("Arc")]
        [Tooltip(
            "How far the swing reaches, in pixels from the body's position to the edge of a target's "
                + "hitbox. 24 is one tile past Grim's body, which makes this a tool rather than a second gun (T-454)."
        )]
        [SerializeField, MinValue(0f)]
        float reach = 24f;

        [BoxGroup("Arc")]
        [Tooltip(
            "How wide the swing is, in degrees, centred on the aim. 135 covers the aim and the direction "
                + "either side of it, three of the eight (T-454), which is what closes the gaps between "
                + "the gun's eight lines at point blank (T-199)."
        )]
        [SerializeField, Range(0f, 360f)]
        float arcDegrees = 135f;

        [BoxGroup("Hit")]
        [Tooltip(
            "What a connecting swing is worth. Damage in hits of 10. The shove fields push along the aim: "
                + "a sharp short shove is about 24 pixels over 0.13 seconds (T-480)."
        )]
        [SerializeField, HideLabel]
        DamageValues damage = new DamageValues
        {
            damage = 30,
            knockback = 0f,
            shoveDistance = 24f,
            shoveTime = 0.13f,
            shakeIntensity = 0.1f,
        };

        [BoxGroup("Hit")]
        [Tooltip("Played where the swing connects, on each thing it hits or knocks back. Optional: the swing is drawn in the body's own art.")]
        [SerializeField]
        Effect hitEffect;

        [BoxGroup("Hit")]
        [Tooltip("How many hit effects can be playing at once, across every player.")]
        [SerializeField, MinValue(1), ShowIf("@hitEffect != null")]
        int hitEffectPoolSize = 8;

        [BoxGroup("Knock back shots")]
        [Tooltip("Whether the swing knocks back enemy shots that are built to be knocked back. Only those are; a bomb, for one, is not.")]
        [SerializeField]
        bool knocksBackShots = true;

        [BoxGroup("Knock back shots")]
        [Tooltip(
            "A knocked-back shot leaves along the aim, snapped to eight, unless an enemy stands within this "
                + "many degrees of that line: then it goes at that enemy. The little pull T-454 asks for. 0 is none."
        )]
        [SerializeField, Range(0f, 45f), ShowIf("knocksBackShots")]
        float magnetDegrees = 15f;

        [BoxGroup("Knock back shots")]
        [Tooltip("How far away, in pixels, an enemy can be and still pull a knocked-back shot onto it. 160 is most of a screen.")]
        [SerializeField, MinValue(0f), ShowIf("knocksBackShots")]
        float magnetRange = 160f;

        public int ActiveStartFrame => activeStartFrame;
        public int ActiveEndFrame => activeEndFrame;
        public bool QueueNextSwing => queueNextSwing;
        public float Reach => reach;
        public float ArcDegrees => arcDegrees;
        public DamageValues Damage => damage;
        public Effect HitEffect => hitEffect;
        public int HitEffectPoolSize => hitEffectPoolSize;
        public bool KnocksBackShots => knocksBackShots;
        public float MagnetDegrees => magnetDegrees;
        public float MagnetRange => magnetRange;
    }
}
