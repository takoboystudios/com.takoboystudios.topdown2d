using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A Brand ability: the special, and the fourth loadout slot. Grim's is the Mega Riff (T-200).
    ///
    /// The owner's shape, 2026-09-29: the screen darkens with Grim alone in the foreground; he plays
    /// the intro once, then the loop for <see cref="loopDuration"/> seconds, with notes coming off him
    /// on the beat and note bombs raining onto the screen from the upper right. **Everything else is
    /// frozen, the other player included.** Then the screen comes back, he plays the recovery, and
    /// everything on screen takes <see cref="damage"/> (100: most things die, bosses do not).
    ///
    /// Three clip sets rather than one: <see cref="SlotDefinition.Animation"/> is the intro, and the
    /// loop and the recovery are named here. The character resolves each for its facings the same way
    /// it does every other slot's clips, so a Brand drawn in only one facing plays that one.
    ///
    /// The rain and the notes are show, not damage. The damage is the one sweep at the end, so nothing
    /// depends on where a bomb happened to land, and nothing can be missed because it stood between two.
    /// </summary>
    [CreateAssetMenu(menuName = "TopDown2D/Brand", fileName = "Brand")]
    public class BrandDefinition : SlotDefinition
    {
        [BoxGroup("Animation")]
        [Tooltip("The clip set that loops while the world is frozen, without the facing: 'guitar-ultimate-loop'. Played again each time it ends.")]
        [SerializeField]
        string loopAnimation;

        [BoxGroup("Animation")]
        [Tooltip("The clip set played once as the world comes back, without the facing: 'guitar-ultimate-recovery'. The hit lands as it starts.")]
        [SerializeField]
        string recoveryAnimation;

        [BoxGroup("Timing")]
        [Tooltip("Seconds the loop plays with the world frozen, after the intro. The show's length. About 2.")]
        [SerializeField, MinValue(0f)]
        float loopDuration = 2f;

        [BoxGroup("Screen")]
        [Tooltip(
            "How dark the screen gets, as the dim's opacity at each step from lightest to darkest. Stepped, "
                + "not faded (PIXEL_RULES): each step is held, and the camera's palette clamp puts every "
                + "level on real palette colours. Three steps up to 0.65 reads as night falling."
        )]
        [SerializeField]
        float[] dimLevels = { 0.3f, 0.5f, 0.65f };

        [BoxGroup("Screen")]
        [Tooltip("Seconds each dim step is held, going down and coming back. 0.06 is about four frames.")]
        [SerializeField, MinValue(0.01f)]
        float dimStepTime = 0.06f;

        [BoxGroup("Notes")]
        [Tooltip("Notes that come off the body on the beat while it loops. The Guitar Riff's notes.")]
        [SerializeField]
        DriftEffect notePrefab;

        [BoxGroup("Notes")]
        [Tooltip("Seconds between beats. 0.18 lands twice per pass of Grim's 360 ms strum loop.")]
        [SerializeField, MinValue(0.02f), ShowIf("@notePrefab != null")]
        float beatInterval = 0.18f;

        [BoxGroup("Notes")]
        [Tooltip("Notes per beat, each on its own random heading.")]
        [SerializeField, MinValue(0), ShowIf("@notePrefab != null")]
        int notesPerBeat = 3;

        [BoxGroup("Notes")]
        [Tooltip("Pool for the notes, across every player. Shared with anything else using the same prefab.")]
        [SerializeField, MinValue(1), ShowIf("@notePrefab != null")]
        int notePoolSize = 24;

        [BoxGroup("Rain")]
        [Tooltip("What rains while it loops: a note bomb that falls from the upper right onto a random spot on screen and goes off. Cosmetic.")]
        [SerializeField]
        FallEffect rainPrefab;

        [BoxGroup("Rain")]
        [Tooltip("Seconds between drops. 0.06 is about 33 over a two second loop.")]
        [SerializeField, MinValue(0.01f), ShowIf("@rainPrefab != null")]
        float rainInterval = 0.06f;

        [BoxGroup("Rain")]
        [Tooltip("Pixels kept clear of each edge of the screen when picking where a drop lands. The bottom also clears the HUD band.")]
        [SerializeField, MinValue(0f), ShowIf("@rainPrefab != null")]
        float rainMargin = 16f;

        [BoxGroup("Rain")]
        [Tooltip("Pool for the drops, across every player. At least the drops in the air at once.")]
        [SerializeField, MinValue(1), ShowIf("@rainPrefab != null")]
        int rainPoolSize = 32;

        [BoxGroup("Hit")]
        [Tooltip(
            "What everything on screen takes as the world comes back. 100 kills nearly anything that is not "
                + "a boss. Breaks barrels and crates too."
        )]
        [SerializeField, HideLabel]
        DamageValues damage = new DamageValues { damage = 100, breaksEnvironment = true, shakeIntensity = 0.3f };

        [BoxGroup("Hit")]
        [Tooltip("Pops every enemy shot on screen as the world comes back, the published Mega Riff's panic button.")]
        [SerializeField]
        bool clearsEnemyShots = true;

        public string LoopAnimation => loopAnimation;
        public string RecoveryAnimation => recoveryAnimation;
        public float LoopDuration => loopDuration;
        public float[] DimLevels => dimLevels;
        public float DimStepTime => dimStepTime;
        public DriftEffect NotePrefab => notePrefab;
        public float BeatInterval => beatInterval;
        public int NotesPerBeat => notesPerBeat;
        public int NotePoolSize => notePoolSize;
        public FallEffect RainPrefab => rainPrefab;
        public float RainInterval => rainInterval;
        public float RainMargin => rainMargin;
        public int RainPoolSize => rainPoolSize;
        public DamageValues Damage => damage;
        public bool ClearsEnemyShots => clearsEnemyShots;

        public override int ClipSetCount => 3;

        public override string ClipSet(int index) =>
            index == 0 ? Animation : index == 1 ? loopAnimation : recoveryAnimation;
    }
}
