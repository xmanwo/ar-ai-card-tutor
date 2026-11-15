using System;

/// <summary>
/// Colour as the rules see it. Wild means "the card has no colour of its own"; it is not a
/// playable colour, which is why it sits apart from the four suits.
/// </summary>
public enum CardColor
{
    Red,
    Green,
    Blue,
    Yellow,
    Wild,
    Unknown
}

/// <summary>
/// Face value. The ten number members are kept first and contiguous so
/// <see cref="CardFaces"/> can map a digit with one cast.
/// </summary>
public enum CardValue
{
    N0,
    N1,
    N2,
    N3,
    N4,
    N5,
    N6,
    N7,
    N8,
    N9,
    Skip,
    Reverse,
    DrawTwo,
    Wild,
    WildDrawFour,
    Unknown
}

/// <summary>
/// Converts the strings that arrive from the recognition service into the enums the rules use.
/// Everything else in the project must go through here rather than compare strings itself.
///
/// The service sends labels such as "Green 3", "Blue Draw Two", "Wild" or "Wild Draw 4"
/// (see detect_v2.py PRETTY table, then .title() in server.py). Two shapes need care:
///
/// - A bare "Wild" is split by CardEntry.SplitLabel into colour "" and value "Wild", so an
///   empty colour means Wild, not a broken read.
/// - "Draw Two" is spelled with a word while "Draw 4" uses a digit. Both forms, and the "+2"/"+4"
///   shorthand, are accepted here.
/// </summary>
public static class CardFaces
{
    /// <summary>Colour for a card. An empty string maps to <see cref="CardColor.Wild"/>.</summary>
    public static CardColor ParseColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return CardColor.Wild;

        string c = color.Trim();

        if (Same(c, "red")) return CardColor.Red;
        if (Same(c, "green")) return CardColor.Green;
        if (Same(c, "blue")) return CardColor.Blue;
        if (Same(c, "yellow")) return CardColor.Yellow;
        if (Same(c, "wild")) return CardColor.Wild;

        return CardColor.Unknown;
    }

    /// <summary>Face value for a card. Unrecognised text becomes <see cref="CardValue.Unknown"/>.</summary>
    public static CardValue ParseValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CardValue.Unknown;

        string v = value.Trim();

        // A single digit covers all ten number cards without a lookup table.
        if (v.Length == 1 && v[0] >= '0' && v[0] <= '9')
            return (CardValue)((int)CardValue.N0 + (v[0] - '0'));

        if (Same(v, "skip")) return CardValue.Skip;
        if (Same(v, "reverse")) return CardValue.Reverse;
        if (Same(v, "wild")) return CardValue.Wild;

        if (Same(v, "draw two") || Same(v, "draw 2") || Same(v, "drawtwo") || Same(v, "+2"))
            return CardValue.DrawTwo;

        if (Same(v, "draw 4") || Same(v, "draw four") || Same(v, "drawfour") || Same(v, "+4") ||
            Same(v, "wild draw 4") || Same(v, "wild draw four"))
            return CardValue.WildDrawFour;

        return CardValue.Unknown;
    }

    /// <summary>True for both Wild cards. They ignore colour when played.</summary>
    public static bool IsWild(CardValue value)
    {
        return value == CardValue.Wild || value == CardValue.WildDrawFour;
    }

    public static bool IsNumber(CardValue value)
    {
        return value >= CardValue.N0 && value <= CardValue.N9;
    }

    /// <summary>The digit of a number card, 0-9. Only valid when <see cref="IsNumber"/> is true.</summary>
    public static int NumberOf(CardValue value)
    {
        return (int)value - (int)CardValue.N0;
    }

    /// <summary>
    /// Localisation key for a colour, e.g. "colour.red". Numbers have no key: they render as
    /// themselves in every language.
    /// </summary>
    public static string ColorKey(CardColor color)
    {
        switch (color)
        {
            case CardColor.Red: return "colour.red";
            case CardColor.Green: return "colour.green";
            case CardColor.Blue: return "colour.blue";
            case CardColor.Yellow: return "colour.yellow";
            case CardColor.Wild: return "colour.wild";
            default: return "colour.unknown";
        }
    }

    /// <summary>Localisation key for a face value, e.g. "value.drawTwo" or "value.7".</summary>
    public static string ValueKey(CardValue value)
    {
        if (IsNumber(value))
            return "value." + NumberOf(value);

        switch (value)
        {
            case CardValue.Skip: return "value.skip";
            case CardValue.Reverse: return "value.reverse";
            case CardValue.DrawTwo: return "value.drawTwo";
            case CardValue.Wild: return "value.wild";
            case CardValue.WildDrawFour: return "value.wildDrawFour";
            default: return "value.unknown";
        }
    }

    static bool Same(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
