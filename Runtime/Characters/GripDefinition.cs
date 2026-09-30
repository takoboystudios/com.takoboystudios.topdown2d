using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A Grip ability: what is on the throw button, and what the HUD's Grip slot draws.
    ///
    /// **The slot is the player's, the ability is what fills it.** Grim's Grip slot used to be the
    /// bomb throw, wired straight into <see cref="Player"/>. It is now whichever Grip ability is
    /// equipped, so a character can carry the Bomb into one run and the Guitar Riff into the next
    /// without either knowing about the other (owner, 2026-09-29). The design board calls these
    /// Secondaries, equipped in the Tavern before a run; that equip screen does not exist yet, so the
    /// first option in the player's Grip slot is what they start with and the debug overlay cycles it.
    ///
    /// Data, not code. Everything one does is a combination of the parts below, and a part left empty
    /// is simply skipped:
    ///
    /// - the **animation** (on <see cref="SlotDefinition"/>) and the frame it lands on
    /// - a **bomb** lobbed along the aim, exactly the throw that already existed
    /// - a **shockwave** spawned at the body: an Effect whose damage box hits and shoves what is close
    /// - **notes**, cosmetic drift effects thrown out in a ring on the same frame
    ///
    /// The Bomb uses only a bomb; the Guitar Riff uses all of them. A new Grip ability that needs
    /// something none of these can express earns a new part here, not a subclass.
    /// </summary>
    [CreateAssetMenu(menuName = "TopDown2D/Grip", fileName = "Grip")]
    public class GripDefinition : SlotDefinition
    {
        [BoxGroup("Animation")]
        [Tooltip(
            "Which frame of the animation everything happens on: the bomb leaves the hand, the "
                + "shockwave goes out. Read off the art rather than timed beside it, so what you see and "
                + "what the game does cannot drift apart. 2 is the arm coming through on the throw and "
                + "the strum landing on the guitar."
        )]
        [SerializeField, MinValue(0)]
        int releaseFrame = 2;

        [BoxGroup("Bomb")]
        [Tooltip("Lobbed along the aim on the release frame. Leave empty for a Grip ability that throws nothing.")]
        [SerializeField]
        Bomb bombPrefab;

        [BoxGroup("Bomb")]
        [Tooltip(
            "How far along the aim it lands, in pixels. A bomb goes to a spot rather than off in a "
                + "direction, so this is the whole of its range. Grim's was tuned to 50."
        )]
        [SerializeField, MinValue(0f), ShowIf("@bombPrefab != null")]
        float throwDistance = 50f;

        [BoxGroup("Bomb")]
        [Tooltip("How many of these bombs can be in the air at once, across every player. Fixed and pre-warmed, so a throw never allocates.")]
        [SerializeField, MinValue(1), ShowIf("@bombPrefab != null")]
        int bombPoolSize = 8;

        [BoxGroup("Shockwave")]
        [Tooltip(
            "Spawned at the feet on the release frame. An Effect: its art is the ring on the floor and "
                + "its damage box is the hit and the shove, so the radius the player sees and the radius "
                + "that hits are tuned in the same prefab. Leave empty for a Grip ability with no shockwave."
        )]
        [SerializeField]
        Effect shockwavePrefab;

        [BoxGroup("Shockwave")]
        [Tooltip("How many shockwaves can be playing at once, across every player. A few is plenty: each lasts about a second.")]
        [SerializeField, MinValue(1), ShowIf("@shockwavePrefab != null")]
        int shockwavePoolSize = 4;

        [BoxGroup("Notes")]
        [Tooltip("Cosmetic notes thrown out around the body on the release frame. Leave empty for none.")]
        [SerializeField]
        DriftEffect notePrefab;

        [BoxGroup("Notes")]
        [Tooltip(
            "How many notes go out per use, spread evenly around a circle with a little jitter so the "
                + "ring does not look stamped. 6 to 8 reads as a burst without crowding the shockwave."
        )]
        [SerializeField, MinValue(0), ShowIf("@notePrefab != null")]
        int noteCount = 6;

        [BoxGroup("Notes")]
        [Tooltip("Degrees of random wobble on each note's heading, either way. 0 is a perfect ring.")]
        [SerializeField, Range(0f, 45f), ShowIf("@notePrefab != null")]
        float noteJitter = 15f;

        [BoxGroup("Notes")]
        [Tooltip("Pool size for the notes. At least note count times the uses that can overlap: 24 covers four strums in the air.")]
        [SerializeField, MinValue(1), ShowIf("@notePrefab != null")]
        int notePoolSize = 24;

        public int ReleaseFrame => releaseFrame;

        public Bomb BombPrefab => bombPrefab;
        public float ThrowDistance => throwDistance;
        public int BombPoolSize => bombPoolSize;

        public Effect ShockwavePrefab => shockwavePrefab;
        public int ShockwavePoolSize => shockwavePoolSize;

        public DriftEffect NotePrefab => notePrefab;
        public int NoteCount => noteCount;
        public float NoteJitter => noteJitter;
        public int NotePoolSize => notePoolSize;
    }
}
