namespace REPOBot.Core
{
    /// <summary>
    /// Runtime-switchable behaviour profiles. Off disables all automation and
    /// hands control back to the human immediately.
    /// </summary>
    public enum BotMode
    {
        /// <summary>No automation; the bot only observes (HUD/timer still work).</summary>
        Off = 0,

        /// <summary>
        /// Professional, careful play. Prioritises survival: keeps wide berths
        /// from monsters, collects valuables when the path is safe, extracts when
        /// the haul is worthwhile. Optimises for completing the level intact.
        /// </summary>
        SafeCollect = 1,

        /// <summary>
        /// Minimum-time play. Takes the most direct routes, accepts more risk,
        /// grabs only what is on the way (or required), and rushes extraction.
        /// This is the mode that chases best-time records.
        /// </summary>
        Speedrun = 2
    }

    /// <summary>The controller's current high-level intent.</summary>
    public enum RunPhase
    {
        Idle,       // waiting for a run / level to be active
        Collect,    // moving to and grabbing a valuable
        Haul,       // carrying a valuable toward extraction
        Extract,    // at the extraction point, completing it
        Flee,       // a monster is too close; survival overrides everything
        Done        // level objectives complete
    }
}
