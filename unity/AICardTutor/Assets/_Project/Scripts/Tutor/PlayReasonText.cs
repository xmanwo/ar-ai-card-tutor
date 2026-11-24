/// <summary>
/// Turns a <see cref="PlayCheck"/> into a <see cref="TextRef"/>. It never returns a finished
/// sentence: the caller decides when to render it and in which language.
///
/// This is the counterpart of the HTML prototype's judge() + whyText() pair. The rule decides the
/// reason code; this class only chooses the wording key and fills in the card names.
/// </summary>
public static class PlayReasonText
{
    /// <param name="check">Result of the rule engine.</param>
    /// <param name="card">The card being explained. May be null for a hand-level message.</param>
    /// <param name="top">The discard card, used to name the top card in the sentence.</param>
    public static TextRef Describe(PlayCheck check, ConfirmedCard card, ConfirmedCard top)
    {
        string cardName = NameOf(card);
        string topName = NameOf(top);
        bool topIsWild = CardFaces.IsWild(ValueOf(top));

        switch (check.Reason)
        {
            case PlayReason.SameColor:
                return new TextRef(topIsWild ? "why.activeColourMatch" : "why.sameColor",
                    new TextVar("card", cardName),
                    new TextVar("top", topName),
                    new TextVar("colour", Loc.T(CardFaces.ColorKey(check.ActiveColor))));

            case PlayReason.SameValue:
                return new TextRef("why.sameValue",
                    new TextVar("card", cardName),
                    new TextVar("top", topName),
                    new TextVar("value", Loc.T(CardFaces.ValueKey(ValueOf(top)))));

            case PlayReason.WildCard:
                return new TextRef("why.wild",
                    new TextVar("card", cardName));

            case PlayReason.WildDrawFour:
                return new TextRef("why.wildDrawFour",
                    new TextVar("card", cardName));

            case PlayReason.WildDrawFourBlocked:
                return new TextRef("why.wildDrawFourBlocked",
                    new TextVar("card", cardName),
                    new TextVar("colour", Loc.T(CardFaces.ColorKey(check.ActiveColor))));

            case PlayReason.NoMatch:
                if (topIsWild)
                    return new TextRef("why.activeColourMismatch",
                        new TextVar("card", cardName),
                        new TextVar("colour", Loc.T(CardFaces.ColorKey(check.ActiveColor))));

                return new TextRef("why.noMatch",
                    new TextVar("card", cardName),
                    new TextVar("top", topName));

            case PlayReason.UnknownCard:
                return new TextRef("why.unknownCard",
                    new TextVar("card", cardName));

            case PlayReason.NoTopCard:
                return new TextRef("why.noTopCard");

            default:
                return new TextRef("why.notReady");
        }
    }

    static CardValue ValueOf(ConfirmedCard card)
    {
        return card != null ? CardFaces.ParseValue(card.value) : CardValue.Unknown;
    }

    static string NameOf(ConfirmedCard card)
    {
        return card != null ? card.DisplayName : "?";
    }
}
