namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// How floors stack when they are drawn (HellWilds #355, after the reference's rendering decode).
    ///
    /// Floors are numbered with gaps: 1 is the ground, 3 is one storey up, 2 is the band that joins
    /// them (stairs, the walls between). A floor belongs to a draw tier, and every tier draws whole
    /// over the one below: ground and stairs-down are tier 1, stairs-up and the storey are tier 2, and
    /// so on. Within a tier, things sort by their ground y as they always have; a tier is a big fixed
    /// step added on top, so an upper storey's art covers whoever is walking underneath it.
    ///
    /// Tier 1 adds nothing, so a room that is all floor 1, which is every room so far, sorts exactly as
    /// it did before floors existed.
    /// </summary>
    public static class FloorTiers
    {
        /// <summary>
        /// The sort-order step between tiers. Sorting is ground y times ten, so this allows about 500
        /// world units of y above and below the origin before two tiers could overlap; rooms stay
        /// well inside that. Unity's sorting order is 16 bit, which leaves room for tiers 0 to 3.
        /// </summary>
        public const int TierSpan = 10000;

        /// <summary>The floor every body is on until something says otherwise.</summary>
        public const int Ground = 1;

        /// <summary>
        /// Which draw tier a floor is in: 0 and 1 are tier 1, 2 and 3 tier 2, 4 and 5 tier 3, -3 to -1
        /// tier 0. Integer division truncating toward zero, exactly the reference's rule.
        /// </summary>
        public static int TierOf(int floor) => (floor + 2) / 2;

        /// <summary>What a body on this floor adds to its ground-y sort order.</summary>
        public static int OrderOffset(int floor) => (TierOf(floor) - 1) * TierSpan;
    }
}
