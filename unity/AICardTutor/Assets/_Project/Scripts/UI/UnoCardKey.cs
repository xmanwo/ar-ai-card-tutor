using System;

/// <summary>Canonical lookup for the 54 card face textures, including recognition aliases.</summary>
public static class UnoCardKey
{
    public static string For(string color, string value)
    {
        CardValue face = CardFaces.ParseValue(value);
        if (face == CardValue.Wild) return "WC";
        if (face == CardValue.WildDrawFour) return "W4";
        CardColor suit = CardFaces.ParseColor(color);
        string prefix;
        switch (suit)
        {
            case CardColor.Red: prefix = "R"; break;
            case CardColor.Blue: prefix = "B"; break;
            case CardColor.Green: prefix = "G"; break;
            case CardColor.Yellow: prefix = "Y"; break;
            default: return null;
        }
        if (CardFaces.IsNumber(face)) return prefix + CardFaces.NumberOf(face);
        switch (face)
        {
            case CardValue.DrawTwo: return prefix + "A2";
            case CardValue.Reverse: return prefix + "R";
            case CardValue.Skip: return prefix + "S";
            default: return null;
        }
    }
}

public static class HandCardLayout
{
    public const int PageSize = 10;
    public static int PageCount(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);

    public static float PositionX(int index, int count, float rowWidth, float cardWidth)
    {
        if (count <= 1) return 0f;
        float step = Math.Min(cardWidth * 0.78f, Math.Max(0f, rowWidth - cardWidth) / (count - 1));
        return (index - (count - 1) * 0.5f) * step;
    }
}
