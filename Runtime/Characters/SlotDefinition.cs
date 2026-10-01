using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// What every loadout slot ability has in common, whichever slot it goes in. The slots are named
    /// the way the HUD and the design name them (owner, 2026-09-29):
    ///
    /// - **Grip**, the throw slot: <see cref="GripDefinition"/> (the Bomb, the Guitar Riff)
    /// - **Knuckle**, the melee slot: <see cref="KnuckleDefinition"/>. Kept for Sen; Grim has no melee
    ///   since 2026-10-01 (T-507), and the Guitar Bash asset waits unused.
    /// - **Brand**, the special: <see cref="BrandDefinition"/> (the Mega Riff), carried as uses with
    ///   no meter, the way a Gungeon Blank is (T-508).
    ///
    /// Trigger (the gun) comes later on the same base.
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

        [BoxGroup("Uses")]
        [Tooltip(
            "The most it can hold. A rest or a pickup gives uses back up to this and no further. 0 means "
                + "the starting count is the most. The Mega Riff starts with 1 and holds 2."
        )]
        [SerializeField, MinValue(0), ShowIf("@uses >= 0")]
        int maxUses;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public string Animation => animation;

        /// <summary>Uses a player starts a run with. Negative means it never runs out.</summary>
        public int Uses => uses;

        /// <summary>The most uses it can hold: <see cref="maxUses"/>, or the starting count when that is higher.</summary>
        public int MaxUses => Mathf.Max(uses, maxUses);

        /// <summary>
        /// How many clip sets this ability plays. One for a throw or a swing; the Brand has an intro, a
        /// loop and a recovery. The character builds its facing tables from every one of them at setup.
        /// </summary>
        public virtual int ClipSetCount => 1;

        /// <summary>One of this ability's clip sets, without the facing. The first is <see cref="Animation"/>.</summary>
        public virtual string ClipSet(int index) => animation;
    }
}
