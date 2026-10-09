namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// Switches for testing rooms and fights: a player who cannot be hurt, slots that never run out,
    /// enemies that stand still so a formation can be looked at. All off unless the game turns them
    /// on; Hell Wilds sets them from its HellWildsSettings asset, live from the Config window.
    ///
    /// Static on purpose. They answer "how is this process being tested", which has one answer per
    /// process even with two players, so they are the kind of static the co-op rule allows (CLAUDE.md).
    /// </summary>
    public static class TestCheats
    {
        /// <summary>Players ignore every hit. Statuses already on them still tick.</summary>
        public static bool PlayersInvincible;

        /// <summary>Spending a slot use (a bomb, a Brand) takes nothing away.</summary>
        public static bool InfiniteUses;

        /// <summary>Enemies stand where they are, as they do before their fight starts.</summary>
        public static bool EnemiesPaused;
    }
}
