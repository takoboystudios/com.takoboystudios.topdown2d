namespace TakoBoyStudios.TopDown2D
{
    /// <summary>
    /// A single global switch the lab uses to hold enemy damage off for a beat after the player is
    /// placed into a room. When it is closed, enemies still think and move, but they cannot deal
    /// damage: ranged shots and lobbed bombs do not fire, contact damage is suppressed, and the few
    /// bespoke attacks that commit hard (charges, the Boom Slime touch detonation) will not begin.
    ///
    /// It lives here, not in the room layer, so any enemy can consult it without taking a dependency on
    /// the lab. Outside the lab it simply stays open, so normal play is unaffected. It is a plain static
    /// because there is only ever one player being protected at a time.
    /// </summary>
    public static class CombatGate
    {
        /// <summary>True in normal play. The lab sets this false during the entry grace, then true.</summary>
        public static bool AttacksAllowed = true;
    }
}
