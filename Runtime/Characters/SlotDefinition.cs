using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// What every loadout slot ability has in common, whichever slot it goes in. The slots are named
    /// the way the HUD and the design name them (owner, 2026-09-29):
    ///
    /// - **Grip**, the throw slot: <see cref="GripDefinition"/> (the Bomb, the Guitar Riff)
    /// - **Knuckle**, the melee slot: <see cref="KnuckleDefinition"/> (the Guitar Bash)
    ///
    /// Trigger (the gun) and Brand (the special) come later on the same base.
    ///
    /// What lives here is only what the player code treats the same way for every slot: who it is,
    /// its icon, the body clip it plays, and how many uses it has. What it *does* is the subclass's.
    /// The player side is shared the same way: one <see cref="LoadoutSlot{T}"/> for equipping and
    /// spending, and one clip runner for playing the animation and reading its frames.
    ///
    /// **Per-player state never lives here.** One asset is shared by every player who equips it, so
    /// how many uses are left is kept in the player's <see cref="LoadoutSlot{T}"/>.
    /// </summary>
    public abstract class SlotDefinition : ScriptableObject
    {
        [BoxGroup("Identity")]
        [Tooltip("Stable id, lower case with hyphens: 'grim-bomb', 'grim-guitar-bash'. Saves and the equip screen will key on this.")]
        [SerializeField]
        string id;

        [BoxGroup("Identity")]
        [Tooltip("The name the player sees. 'Bomb', 'Guitar Riff', 'Guitar Bash'.")]
        [SerializeField]
        string displayName;

        [BoxGroup("Identity")]
        [Tooltip("Drawn in the HUD slot while this is equipped. 16 by 16, like every slot icon.")]
        [SerializeField, PreviewField(32)]
        Sprite icon;

        [BoxGroup("Animation")]
        [Tooltip(
            "The body's clip set, without the facing: 'throw-bomb', 'guitar-strum', 'guitar-smash'. The "
                + "character adds the facing ('-e', '-n', ...) and falls back to the nearest facing it "
                + "has art for."
        )]
        [SerializeField]
        string animation;

        [BoxGroup("Uses")]
        [Tooltip(
            "How many times it can be used in a run. Each use spends one, and at zero the button does "
                + "nothing until something refills it. -1 never runs out: a melee. The Bomb starts with 3."
        )]
        [SerializeField, MinValue(-1)]
        int uses = -1;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public string Animation => animation;

        /// <summary>Uses a player starts a run with. Negative means it never runs out.</summary>
        public int Uses => uses;
    }
}
