using System.Collections.Generic;

/// <summary>
/// The P0 rule set: colour match, value match, Wild, and the Wild Draw Four restriction.
///
/// Turn flow is deliberately absent. Stacking, skipping, calling UNO and the draw-one lock belong
/// to a table state machine that does not exist yet; mixing them in here would make this class
/// impossible to test in isolation.
///
/// Everything goes through <see cref="CardFaces"/> so string spellings from the recognition
/// service never leak into a rule.
/// </summary>
public sealed class StandardUnoRules : IRuleEngine
{
    readonly RuleSet ruleSet;

    /// <param name="ruleSet">May be null. A null set falls back to the official rules.</param>
    public StandardUnoRules(RuleSet ruleSet)
    {
        this.ruleSet = ruleSet;
    }

    public PlayCheck Check(ConfirmedCard card, IReadOnlyList<ConfirmedCard> hand,
                           ConfirmedCard top, string activeColor)
    {
        if (top == null)
            return PlayCheck.Blocked(PlayReason.NoTopCard, CardColor.Unknown, string.Empty);

        // A null entry can come from a hand assembled outside TrySave. Report it as unreadable
        // instead of throwing, so one bad element cannot take down a whole evaluation.
        if (card == null)
            return PlayCheck.Blocked(PlayReason.UnknownCard, CardColor.Unknown, top.DisplayName);

        if (hand == null || hand.Count == 0)
            return PlayCheck.Blocked(PlayReason.NotReady, CardColor.Unknown, top.DisplayName);

        CardColor active = ResolveActiveColor(activeColor, top);

        CardColor colour = CardFaces.ParseColor(card.color);
        CardValue value = CardFaces.ParseValue(card.value);

        if (colour == CardColor.Unknown || value == CardValue.Unknown)
            return PlayCheck.Blocked(PlayReason.UnknownCard, active, top.DisplayName);

        // Wilds ignore both colour and value, so they are decided before anything else.
        if (value == CardValue.Wild)
            return PlayCheck.Playable(PlayReason.WildCard, active, top.DisplayName);

        if (value == CardValue.WildDrawFour)
        {
            bool needsFreeHand = ruleSet == null || ruleSet.wildDrawFourNeedsNoActiveColour;

            if (needsFreeHand && HoldsColour(hand, active))
                return PlayCheck.Blocked(PlayReason.WildDrawFourBlocked, active, top.DisplayName);

            return PlayCheck.Playable(PlayReason.WildDrawFour, active, top.DisplayName);
        }

        if (active != CardColor.Wild && active != CardColor.Unknown && colour == active)
            return PlayCheck.Playable(PlayReason.SameColor, active, top.DisplayName);

        CardValue topValue = CardFaces.ParseValue(top.value);

        // A Wild on top has no value worth matching, so only colour applies there.
        if (topValue != CardValue.Unknown && !CardFaces.IsWild(topValue) && value == topValue)
            return PlayCheck.Playable(PlayReason.SameValue, active, top.DisplayName);

        return PlayCheck.Blocked(PlayReason.NoMatch, active, top.DisplayName);
    }

    public void CollectLegal(IReadOnlyList<ConfirmedCard> hand, ConfirmedCard top,
                             string activeColor, List<ConfirmedCard> into)
    {
        if (hand == null || into == null)
            return;

        for (int i = 0; i < hand.Count; i++)
        {
            if (Check(hand[i], hand, top, activeColor).Legal)
                into.Add(hand[i]);
        }
    }

    /// <summary>
    /// The session already resolves a Wild top to the colour the player chose, so its value is
    /// trusted first. Falling back to the top card's own colour only covers a session that has
    /// not been saved through GameSessionState.TrySave.
    ///
    /// An empty string is checked before parsing, because ParseColor("") means "bare Wild card"
    /// rather than "no colour set" - the two must not be confused here.
    /// </summary>
    static CardColor ResolveActiveColor(string activeColor, ConfirmedCard top)
    {
        if (!string.IsNullOrWhiteSpace(activeColor))
        {
            CardColor parsed = CardFaces.ParseColor(activeColor);

            if (parsed != CardColor.Unknown)
                return parsed;
        }

        return CardFaces.ParseColor(top.color);
    }

    /// <summary>True when the hand still holds a card of <paramref name="colour"/>.</summary>
    static bool HoldsColour(IReadOnlyList<ConfirmedCard> hand, CardColor colour)
    {
        if (colour == CardColor.Wild || colour == CardColor.Unknown)
            return false;

        for (int i = 0; i < hand.Count; i++)
        {
            if (CardFaces.ParseColor(hand[i].color) == colour)
                return true;
        }

        return false;
    }
}
