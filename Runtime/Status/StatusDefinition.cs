using Sirenix.OdinInspector;
using UnityEngine;

namespace TakoBoyStudios.TopDown2D.Status
{
    /// <summary>What a second application does to a status already running.</summary>
    public enum StatusRefresh
    {
        /// <summary>Put the clock back to full. The usual behaviour.</summary>
        Restart = 0,

        /// <summary>Add to whatever is left, so repeated application piles up time.</summary>
        Extend = 1,

        /// <summary>Leave the clock alone. Stacks may still rise.</summary>
        Keep = 2,
    }

    /// <summary>
    /// One status a game defines: what it is called, how long it lasts, how it stacks, and what it
    /// does while it is on something.
    ///
    /// **The entity package deliberately names no statuses.** Burn, Chill and Frozen are Hell Wilds'
    /// design, not a property of every top-down game, so they are assets in the game rather than
    /// values in an enum here. A different game authors its own and this machinery does not change.
    ///
    /// Most statuses are pure data: a duration, a stack cap, some damage over time, a multiplier or
    /// two. Anything with real behaviour subclasses this and overrides the hooks: Hell Wilds' Chill
    /// overrides <see cref="OnReachedCap"/> to freeze its victim, and its Frozen overrides
    /// <see cref="CanApply"/> and <see cref="OnEnded"/> to run an anti-stunlock window.
    ///
    /// Every number here is scaled by the potency of whoever applied the status, so one keyword can be
    /// weak from an enemy and strong from a built-up player without being two different things.
    /// </summary>
    [CreateAssetMenu(menuName = "HellWilds/Combat/Status", fileName = "Status")]
    public class StatusDefinition : ScriptableObject
    {
        [BoxGroup("Identity")]
        [Tooltip("Stable id, lowercase. Saved in records and used by conditions, so renaming one orphans them.")]
        [SerializeField] string id;

        [BoxGroup("Identity")]
        [Tooltip("Strings key for the name a player reads.")]
        [SerializeField] string displayNameId;

        [BoxGroup("Time")]
        [Tooltip("Seconds it lasts, before potency. Zero lasts until something removes it.")]
        [SerializeField, Min(0f)] float duration = 3f;

        [BoxGroup("Time")]
        [Tooltip("What a second application does to the clock.")]
        [SerializeField] StatusRefresh refresh = StatusRefresh.Restart;

        [BoxGroup("Stacks")]
        [Tooltip("Most stacks that count. One means it does not stack.")]
        [SerializeField, Min(1)] int maxStacks = 1;

        [BoxGroup("Effect")]
        [Tooltip("Damage per stack per second while it is on. Zero for a status that does no damage of its own.")]
        [SerializeField, Min(0f)] float damagePerStackPerSecond;

        [BoxGroup("Effect")]
        [Tooltip("Damage per stack each time the victim is displaced. The payoff for a status that waits to be triggered.")]
        [SerializeField, Min(0f)] float damagePerStackOnDisplace;

        [BoxGroup("Effect")]
        [Tooltip("Move speed lost per stack, as a fraction. 0.2 is twenty percent slower. 1 is a full stop.")]
        [SerializeField, Range(0f, 1f)] float moveSpeedLossPerStack;

        [BoxGroup("Effect")]
        [Tooltip("Damage the victim deals, lost per stack, as a fraction.")]
        [SerializeField, Range(0f, 1f)] float damageDealtLossPerStack;

        [BoxGroup("Effect")]
        [Tooltip("Extra damage the victim takes, per stack, as a fraction. 0.15 is fifteen percent more.")]
        [SerializeField, Range(0f, 4f)] float damageTakenGainPerStack;

        [BoxGroup("Notes")]
        [TextArea(2, 4)]
        [Tooltip("For whoever tunes it. Never shown to a player.")]
        [SerializeField] string designNote;

        [BoxGroup("Look")]
        [Tooltip(
            "A one-shot particle popped on the victim over and over while this is on, like a particle "
                + "system: an Effect prefab whose animation plays out and releases itself (a flame that "
                + "burns down to smoke, a snowflake that fades out). Empty for none."
        )]
        [SerializeField] GameObject vfx;

        [BoxGroup("Look")]
        [Tooltip("Centre of the area particles pop in, in pixels from the victim's position, before their height is added. Around (0, 6) centres it on a 16px body.")]
        [SerializeField] Vector2 vfxOffset = new Vector2(0f, 6f);

        [BoxGroup("Look")]
        [Tooltip("Width and height of that area, in pixels. Each particle lands on a random whole pixel inside it. Around 12 by 14 covers a 16px body.")]
        [SerializeField] Vector2 vfxArea = new Vector2(12f, 14f);

        [BoxGroup("Look")]
        [Tooltip("Seconds between particles. Lower is busier. Around 0.2; keep it longer than a particle's own animation if they should not overlap.")]
        [SerializeField, Min(0.02f)] float vfxInterval = 0.2f;

        [BoxGroup("Look")]
        [Tooltip(
            "On to step the victim's main colour toward Swap Target and back while this is on, the way "
                + "Grim's purple goes to red while he burns. Only that one colour changes. Each body's main "
                + "colour is set on its prefab. With two such statuses on, the loops take turns."
        )]
        [SerializeField] bool swapsMainColour;

        [BoxGroup("Look")]
        [ShowIf("swapsMainColour")]
        [Tooltip("The colour the main colour steps toward. Burn is red (227, 49, 49); Chill is the frozen art's cyan (2, 218, 216).")]
        [SerializeField] Color swapTarget = new Color(0.8901961f, 0.19215687f, 0.19215687f, 1f);

        [BoxGroup("Look")]
        [Tooltip("An animation the victim holds while this is on, standing still and doing nothing else. 'frozen' for Frozen. Empty for none.")]
        [SerializeField] string holdAnimation;

        public string Id => id;
        public string DisplayNameId => displayNameId;
        public StatusRefresh Refresh => refresh;
        public int MaxStacks => Mathf.Max(1, maxStacks);
        public float DamagePerStackPerSecond => damagePerStackPerSecond;
        public float DamagePerStackOnDisplace => damagePerStackOnDisplace;
        public string DesignNote => designNote;

        /// <summary>The effect shown on a victim, or null.</summary>
        public GameObject Vfx => vfx;

        /// <summary>Centre of the area particles pop in, before the victim's height.</summary>
        public Vector2 VfxOffset => vfxOffset;

        /// <summary>Size of the area particles pop in.</summary>
        public Vector2 VfxArea => vfxArea;

        /// <summary>True when this recolours the victim's main colour while it is on.</summary>
        public bool SwapsMainColour => swapsMainColour;

        /// <summary>The colour the main colour steps toward.</summary>
        public Color SwapTarget => swapTarget;

        /// <summary>The clip the victim holds while this is on, or empty.</summary>
        public string HoldAnimation => holdAnimation;

        /// <summary>Seconds between particles.</summary>
        public float VfxInterval => Mathf.Max(0.02f, vfxInterval);

        [System.NonSerialized] string _vfxPoolName;

        /// <summary>The pool the effect comes from. Cached, because reading a name off an asset makes a string every time.</summary>
        public string VfxPoolName => vfx == null ? null : (_vfxPoolName ??= vfx.name);

        /// <summary>How long it runs for at a given potency.</summary>
        public virtual float DurationFor(float potency) => duration * Mathf.Max(0.01f, potency);

        /// <summary>
        /// What the victim's move speed is multiplied by. Never below zero, and a full stop is a
        /// legitimate answer for a status that holds something still.
        /// </summary>
        public virtual float MoveSpeedMultiplier(in StatusInstance instance) =>
            Mathf.Clamp01(1f - moveSpeedLossPerStack * instance.stacks * instance.potency);

        /// <summary>What damage the victim deals is multiplied by.</summary>
        public virtual float DamageDealtMultiplier(in StatusInstance instance) =>
            Mathf.Clamp01(1f - damageDealtLossPerStack * instance.stacks * instance.potency);

        /// <summary>
        /// What damage the victim takes is multiplied by. Given the hit, so a status can care what
        /// kind of hit it is: Hell Wilds' Frozen doubles a heavy one and is broken by it.
        /// </summary>
        public virtual float DamageTakenMultiplier(in StatusInstance instance, in DamageValues values) =>
            1f + damageTakenGainPerStack * instance.stacks * instance.potency;

        /// <summary>
        /// Whether this may be applied right now. The hook an anti-stunlock window hangs off, so a
        /// hold cannot be reapplied the instant it ends.
        /// </summary>
        public virtual bool CanApply(StatusHolder holder) => true;

        /// <summary>Just applied or reapplied.</summary>
        public virtual void OnApplied(StatusHolder holder, in StatusInstance instance) { }

        /// <summary>
        /// Stacks just reached <see cref="MaxStacks"/>. Where a status that becomes another status
        /// does it, which is how Chill turns into a freeze.
        /// </summary>
        public virtual void OnReachedCap(StatusHolder holder, in StatusInstance instance) { }

        /// <summary>Ran out, or was removed. Where a cooldown window is started.</summary>
        public virtual void OnEnded(StatusHolder holder) { }

        public override string ToString() => string.IsNullOrEmpty(id) ? name : id;
    }

    /// <summary>One status as it currently sits on one entity.</summary>
    public struct StatusInstance
    {
        public StatusDefinition definition;
        public int stacks;
        public float remaining;

        /// <summary>Who applied it, so a kill by it is credited to them and not to the status.</summary>
        public Entity applier;

        /// <summary>The applier's potency when it landed. Scales every number the definition uses.</summary>
        public float potency;

        /// <summary>Fractional damage carried between frames, so damage over time is honest on an int health pool.</summary>
        public float damageCarry;

        /// <summary>Seconds until the next particle pops. Zero when it lands, so one shows at once.</summary>
        public float vfxTimer;

        public bool IsActive => definition != null && stacks > 0;
    }
}
