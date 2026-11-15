/// <summary>
/// A localisation key plus its placeholders. The rules and the tutor pass these around instead of
/// finished sentences, which keeps "what is true" separate from "how it is worded".
///
/// Colour and value words inside <see cref="Vars"/> are already resolved when the TextRef is
/// built. After changing <see cref="Loc.Current"/>, rebuild the TextRef rather than only calling
/// <see cref="Resolve"/> again, otherwise those words stay in the old language.
/// </summary>
public readonly struct TextRef
{
    public readonly string Key;
    public readonly TextVar[] Vars;

    public TextRef(string key, params TextVar[] vars)
    {
        Key = key;
        Vars = vars ?? new TextVar[0];
    }

    public bool IsEmpty
    {
        get { return string.IsNullOrEmpty(Key); }
    }

    /// <summary>Renders the template in the current language.</summary>
    public string Resolve()
    {
        return IsEmpty ? string.Empty : Loc.T(Key, Vars);
    }

    public override string ToString()
    {
        return Resolve();
    }
}
