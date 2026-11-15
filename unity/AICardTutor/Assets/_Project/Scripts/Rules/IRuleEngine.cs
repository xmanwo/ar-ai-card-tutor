using System.Collections.Generic;

/// <summary>
/// Answers two questions: "can this card be played?" and "which cards in the hand can be played?".
///
/// Implementations must stay plain C#: no MonoBehaviour, no scene lookup, no network. That is what
/// lets the EditMode tests run the whole rule set without opening a scene or wearing a headset.
///
/// P0 reads <see cref="ConfirmedCard"/> directly because it is already the immutable snapshot the
/// session holds. If the rules ever need to run before confirmation, introduce a narrow card
/// interface at that point rather than now.
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Checks one card against the current top card and the colour in force.
    /// </summary>
    /// <param name="card">The card being considered.</param>
    /// <param name="hand">The whole confirmed hand. Wild Draw Four legality depends on it.</param>
    /// <param name="top">The confirmed discard card, or null when none has been confirmed.</param>
    /// <param name="activeColor">
    /// Colour currently in force. Already resolved for a Wild top by
    /// <see cref="GameSessionState.ActiveColor"/>, so implementations must not fall back to
    /// guessing.
    /// </param>
    PlayCheck Check(ConfirmedCard card, IReadOnlyList<ConfirmedCard> hand,
                    ConfirmedCard top, string activeColor);

    /// <summary>
    /// Appends every playable card to <paramref name="into"/>, in hand order. The list is not
    /// cleared first, so callers can reuse a buffer across evaluations.
    /// </summary>
    void CollectLegal(IReadOnlyList<ConfirmedCard> hand, ConfirmedCard top,
                      string activeColor, List<ConfirmedCard> into);
}
