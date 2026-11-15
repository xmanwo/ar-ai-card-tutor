/// <summary>
/// The result of checking one card. Immutable, so it can be cached, logged and asserted on in
/// tests without holding a reference to the live hand.
///
/// This struct carries no text. Turning a reason into a sentence is the text layer's job, and it
/// needs a localisation key rather than a finished string.
/// </summary>
public readonly struct PlayCheck
{
    /// <summary>True when the card may be played right now.</summary>
    public readonly bool Legal;

    /// <summary>Why. Meaningful for both outcomes.</summary>
    public readonly PlayReason Reason;

    /// <summary>The colour in force at the moment of the check, after Wild resolution.</summary>
    public readonly CardColor ActiveColor;

    /// <summary>Display name of the top card, for the reason text. Empty when there is no top.</summary>
    public readonly string TopName;

    PlayCheck(bool legal, PlayReason reason, CardColor activeColor, string topName)
    {
        Legal = legal;
        Reason = reason;
        ActiveColor = activeColor;
        TopName = topName;
    }

    public static PlayCheck Playable(PlayReason reason, CardColor activeColor, string topName)
    {
        return new PlayCheck(true, reason, activeColor, topName);
    }

    public static PlayCheck Blocked(PlayReason reason, CardColor activeColor, string topName)
    {
        return new PlayCheck(false, reason, activeColor, topName);
    }

    public override string ToString()
    {
        string verdict = Legal ? "playable" : "blocked";
        string top = string.IsNullOrEmpty(TopName) ? "none" : TopName;
        return $"{verdict}: {Reason} (active {ActiveColor}, top {top})";
    }
}
