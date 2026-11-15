using System.Collections.Generic;

/// <summary>
/// Language used for user-facing text. English is the product default; the others are optional
/// and may be incomplete.
/// </summary>
public enum Language
{
    En,
    Zh,
    Sv
}

/// <summary>
/// One placeholder inside a text template. <see cref="Name"/> is the word inside the braces, so
/// a template "same colour as {top}" takes a TextVar named "top".
/// </summary>
public readonly struct TextVar
{
    public readonly string Name;
    public readonly string Value;

    public TextVar(string name, string value)
    {
        Name = name;
        Value = value ?? string.Empty;
    }
}

/// <summary>
/// The only place that turns a localisation key into displayable text.
///
/// Lookup order is current language, then English, then the key itself. A raw key such as
/// "why.sameColor" appearing on screen is deliberate: it makes a missing translation obvious
/// instead of silently showing an empty label.
///
/// Templates use braces, e.g. "{card} can be played: same colour as the top card ({colour})".
/// </summary>
public static class Loc
{
    /// <summary>Language for every user-facing string. English is the default.</summary>
    public static Language Current = Language.En;

    /// <summary>
    /// Resolves a key with optional placeholders. Values are substituted verbatim, so a missing
    /// variable leaves the braces in place rather than throwing.
    /// </summary>
    public static string T(string key, params TextVar[] vars)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        string text = Lookup(key);

        if (text == null)
            return key;

        if (vars == null || vars.Length == 0)
            return text;

        for (int i = 0; i < vars.Length; i++)
        {
            TextVar v = vars[i];

            if (string.IsNullOrEmpty(v.Name))
                continue;

            text = text.Replace("{" + v.Name + "}", v.Value);
        }

        return text;
    }

    static string Lookup(string key)
    {
        Dictionary<string, string> pack = PackFor(Current);

        if (pack != null && pack.TryGetValue(key, out string value))
            return value;

        // A partially translated pack still has to read like something, so English fills the gaps.
        if (Current != Language.En && EnText.Pack.TryGetValue(key, out string english))
            return english;

        return null;
    }

    static Dictionary<string, string> PackFor(Language language)
    {
        switch (language)
        {
            case Language.Zh:
                return ZhText.Pack;

            case Language.Sv:
                // Not written yet. English covers it until a Swedish pack exists.
                return null;

            default:
                return EnText.Pack;
        }
    }
}
