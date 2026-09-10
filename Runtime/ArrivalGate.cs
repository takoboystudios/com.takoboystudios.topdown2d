namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A single global switch that keeps a room still until the player has actually moved in it.
    ///
    /// The rule, from the owner: **nothing is in combat before the player moves.** Arriving somewhere
    /// and being rushed before you have taken a step is bad design. You should get to land, look at
    /// what is in front of you, and choose to start it. So a room opens with everything in it asleep,
    /// however plainly visible it is, and the first step is what wakes the world.
    ///
    /// This is deliberately about *moving*, not about time. A grace period measured in seconds
    /// punishes a player who is reading the room and rewards one who is not, and it makes the same
    /// room feel different depending on how quickly they happen to be playing. Distance travelled is
    /// the honest trigger, and it is the same one the camera release uses.
    ///
    /// Sibling to <see cref="CombatGate"/>, which is a narrower thing: that one lets enemies think and
    /// move while holding their damage off. This one stops them thinking at all.
    ///
    /// Defaults to true so anything outside the room system, the lab included, is unaffected.
    /// </summary>
    public static class ArrivalGate
    {
        /// <summary>
        /// False from the moment a room places the player until they first move. One way: it never
        /// closes again while the room is running, so backing up to your entry point does not send
        /// the room to sleep.
        /// </summary>
        public static bool PlayerHasMoved = true;

        /// <summary>How far the player has to travel from where they were placed before a room stirs.</summary>
        public const float MoveThreshold = 2f;
    }
}
