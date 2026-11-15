using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Owns the teaching answer for the current table position: reads <see cref="GameSessionState"/>,
/// asks the rule engine what is playable, and hands the result to whoever draws it.
///
/// It does not poll. Call <see cref="Evaluate"/> after the player confirms a hand, confirms the
/// discard card, or rescans the table. That keeps the cost at one evaluation per user action
/// instead of one per frame.
///
/// The service never renders text. It publishes <see cref="TutorAdvice"/>, whose sentences are
/// localisation keys, so the panel can redraw in another language without asking for a new answer.
/// </summary>
public class TutorService : MonoBehaviour
{
    [Tooltip("Leave empty to find the GameSessionState automatically.")]
    public GameSessionState session;

    [Tooltip("Optional. When empty the official rules are used.")]
    public RuleSet ruleSet;

    [Tooltip("Log one line per evaluation. Useful while wiring the scene, noisy in a build.")]
    public bool logEvaluations = true;

    /// <summary>Latest answer. Never null after Start.</summary>
    public TutorAdvice Current { get; private set; }

    /// <summary>Raised whenever a new answer replaces the previous one.</summary>
    public event Action<TutorAdvice> Changed;

    IRuleEngine rules;

    readonly List<ConfirmedCard> legalBuffer = new List<ConfirmedCard>(8);
    readonly List<TextRef> legalLines = new List<TextRef>(8);
    readonly List<TextRef> blockedLines = new List<TextRef>(TutorAdvice.MaxBlockedLines);
    readonly StringBuilder listBuilder = new StringBuilder(96);

    void Awake()
    {
        if (session == null)
            session = FindFirstObjectByType<GameSessionState>();

        rules = new StandardUnoRules(ruleSet);
    }

    void Start()
    {
        // Publish an initial answer so a freshly opened scene shows "not ready" instead of an
        // empty panel with no explanation.
        Evaluate();
    }

    /// <summary>
    /// Recomputes the advice for the current position. Safe to call at any time; doing so when
    /// nothing has been confirmed simply yields a "not ready" message.
    /// </summary>
    public void Evaluate()
    {
        if (session == null)
        {
            Publish(TutorAdvice.Message("why.notReady"));
            return;
        }

        if (!session.HasHand)
        {
            Publish(TutorAdvice.Message("why.notReady"));
            return;
        }

        if (session.Discard == null)
        {
            Publish(TutorAdvice.Message("why.noTopCard"));
            return;
        }

        legalBuffer.Clear();
        rules.CollectLegal(session.Hand, session.Discard, session.ActiveColor, legalBuffer);

        legalLines.Clear();
        for (int i = 0; i < legalBuffer.Count; i++)
        {
            PlayCheck check = rules.Check(legalBuffer[i], session.Hand,
                                          session.Discard, session.ActiveColor);
            legalLines.Add(PlayReasonText.Describe(check, legalBuffer[i], session.Discard));
        }

        // Only the first few failures are worth showing. Listing every dead card buries the point.
        blockedLines.Clear();
        for (int i = 0; i < session.Hand.Count && blockedLines.Count < TutorAdvice.MaxBlockedLines; i++)
        {
            ConfirmedCard card = session.Hand[i];
            PlayCheck check = rules.Check(card, session.Hand, session.Discard, session.ActiveColor);

            if (!check.Legal)
                blockedLines.Add(PlayReasonText.Describe(check, card, session.Discard));
        }

        Publish(new TutorAdvice(
            true,
            legalBuffer.ToArray(),
            legalLines.ToArray(),
            blockedLines.ToArray(),
            BuildSummary()));
    }

    /// <summary>Builds the headline from the cards already collected in <see cref="legalBuffer"/>.</summary>
    TextRef BuildSummary()
    {
        if (legalBuffer.Count == 0)
            return new TextRef("tutor.cannotPlay");

        listBuilder.Length = 0;

        for (int i = 0; i < legalBuffer.Count; i++)
        {
            if (i > 0)
                listBuilder.Append(", ");

            listBuilder.Append(legalBuffer[i].DisplayName);
        }

        return new TextRef(
            "tutor.canPlay",
            new TextVar("n", legalBuffer.Count.ToString()),
            new TextVar("list", listBuilder.ToString()));
    }

    void Publish(TutorAdvice advice)
    {
        Current = advice;

        if (logEvaluations)
        {
            if (advice.Ready)
            {
                Debug.Log($"[Tutor] {session.Hand.Count} in hand, {advice.LegalCount} playable " +
                          $"(blocked shown: {advice.BlockedLines.Count})");
            }
            else
            {
                Debug.Log($"[Tutor] not ready: {advice.Summary.Key}");
            }
        }

        Changed?.Invoke(advice);
    }
}
