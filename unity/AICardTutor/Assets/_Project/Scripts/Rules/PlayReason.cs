/// <summary>
/// Why a card is or is not playable. The rule engine returns this code; the text layer turns it
/// into a sentence. Codes are stable so tests, logs and the tutor can rely on them.
///
/// Keep the set small. If a new case needs different wording, add a code; if it only needs a
/// different variable, reuse the existing code.
/// </summary>
public enum PlayReason
{
    /// <summary>The card matches the active colour.</summary>
    SameColor,

    /// <summary>The card matches the top card's value or symbol.</summary>
    SameValue,

    /// <summary>A Wild. Playable on anything.</summary>
    WildCard,

    /// <summary>A Wild Draw Four, allowed because the hand holds no card of the active colour.</summary>
    WildDrawFour,

    /// <summary>A Wild Draw Four refused because the hand still holds the active colour.</summary>
    WildDrawFourBlocked,

    /// <summary>Neither colour nor value matches the top card.</summary>
    NoMatch,

    /// <summary>The card's colour or value could not be parsed.</summary>
    UnknownCard,

    /// <summary>No discard card has been confirmed yet.</summary>
    NoTopCard,

    /// <summary>The hand is empty, so there is nothing to check against.</summary>
    NotReady
}
