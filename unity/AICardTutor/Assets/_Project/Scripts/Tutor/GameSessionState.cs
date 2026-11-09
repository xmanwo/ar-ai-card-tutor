using System;
using System.Collections.Generic;
using UnityEngine;

public enum ScanTarget { Hand, Discard }

// Runtime snapshot. It does not reference a mutable CardEntry or a camera texture.
public sealed class ConfirmedCard
{
    public readonly string id;
    public readonly string color;
    public readonly string value;
    public readonly CardSource source;
    public readonly float confidence;

    public ConfirmedCard(CardEntry card)
    {
        id = Guid.NewGuid().ToString("N");
        color = card.color;
        value = card.value;
        source = card.source;
        confidence = card.confidence;
    }

    public string DisplayName =>
        string.IsNullOrEmpty(color) ? value : $"{color} {value}";
}

public class GameSessionState : MonoBehaviour
{
    public IReadOnlyList<ConfirmedCard> Hand { get; private set; } =
        Array.AsReadOnly(new ConfirmedCard[0]);

    public ConfirmedCard Discard { get; private set; }
    public string ActiveColor { get; private set; }
    public bool HasHand { get; private set; }

    public bool ReadyForTutor =>
        HasHand && Discard != null && !string.IsNullOrEmpty(ActiveColor);

    public static bool IsWild(CardEntry card)
    {
        return card != null &&
            (string.Equals(card.color, "Wild", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(card.value, "Wild", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(card.DisplayName, "Draw 4", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsValidColor(string color)
    {
        return color == "Red" || color == "Yellow" || color == "Green" || color == "Blue";
    }

    public bool TrySave(ScanTarget target, IReadOnlyList<CardEntry> cards,
        string wildColor, out string error)
    {
        error = null;

        if (cards == null || cards.Count == 0)
        {
            error = "No cards to save.";
            return false;
        }

        foreach (var card in cards)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.value) ||
                string.Equals(card.value, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                error = "Resolve unknown cards before confirming.";
                return false;
            }

            if (!IsWild(card) && !IsValidColor(card.color))
            {
                error = "Resolve unknown card colours before confirming.";
                return false;
            }
        }

        if (target == ScanTarget.Hand)
        {
            var snapshot = new ConfirmedCard[cards.Count];
            for (int i = 0; i < cards.Count; i++)
                snapshot[i] = new ConfirmedCard(cards[i]);

            Hand = Array.AsReadOnly(snapshot);
            HasHand = true;
            return true;
        }

        if (cards.Count != 1)
        {
            error = "Scan exactly one discard card.";
            return false;
        }

        var top = cards[0];
        string activeColor = IsWild(top) ? wildColor : top.color;
        if (!IsValidColor(activeColor))
        {
            error = "Choose a valid active colour before confirming.";
            return false;
        }

        Discard = new ConfirmedCard(top);
        ActiveColor = activeColor;
        return true;
    }
}