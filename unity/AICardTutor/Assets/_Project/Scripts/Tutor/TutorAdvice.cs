using System;
using System.Collections.Generic;

/// <summary>
/// One evaluation of a confirmed table position: which cards can be played, why, and why the rest
/// cannot.
///
/// Everything here is text-free. Sentences are <see cref="TextRef"/> values resolved at draw time,
/// so the advice survives a language switch without re-running the rules.
///
/// Instances are immutable snapshots: the service hands a new one to its listener each time, so a
/// view can hold on to it without worrying about the next scan mutating the list underneath.
/// </summary>
public sealed class TutorAdvice
{
    /// <summary>At most this many "cannot play" lines, so the panel never turns into a wall.</summary>
    public const int MaxBlockedLines = 2;

    static readonly ConfirmedCard[] NoCards = new ConfirmedCard[0];
    static readonly TextRef[] NoLines = new TextRef[0];

    /// <summary>False when the table position is not usable yet; <see cref="Summary"/> says why.</summary>
    public readonly bool Ready;

    /// <summary>Cards that can be played, in hand order.</summary>
    public readonly IReadOnlyList<ConfirmedCard> Legal;
    public readonly IReadOnlyList<PlayCheck> Verdicts;
    public readonly bool UnoAlert;

    /// <summary>One sentence per legal card, same order as <see cref="Legal"/>.</summary>
    public readonly IReadOnlyList<TextRef> LegalLines;

    /// <summary>Sentences for unplayable cards, capped at <see cref="MaxBlockedLines"/>.</summary>
    public readonly IReadOnlyList<TextRef> BlockedLines;

    /// <summary>Headline above the list.</summary>
    public readonly TextRef Summary;

    public TutorAdvice(bool ready, IReadOnlyList<ConfirmedCard> legal,
                       IReadOnlyList<TextRef> legalLines, IReadOnlyList<TextRef> blockedLines,
                       TextRef summary, IReadOnlyList<PlayCheck> verdicts = null, bool unoAlert = false)
    {
        Ready = ready;
        Legal = legal ?? NoCards;
        LegalLines = legalLines ?? NoLines;
        BlockedLines = blockedLines ?? NoLines;
        Summary = summary;
        Verdicts = verdicts ?? Array.Empty<PlayCheck>();
        UnoAlert = unoAlert;
    }

    /// <summary>
    /// An advice that carries a single message and no card lists, for "not ready" situations.
    /// </summary>
    public static TutorAdvice Message(string summaryKey)
    {
        return new TutorAdvice(false, NoCards, NoLines, NoLines, new TextRef(summaryKey));
    }

    public int LegalCount
    {
        get { return Legal.Count; }
    }

    public override string ToString()
    {
        string head = Summary.Resolve();

        if (!Ready)
            return head;

        return $"{head} | legal {Legal.Count}, shown {LegalLines.Count}, " +
               $"blocked shown {BlockedLines.Count}";
    }
}
