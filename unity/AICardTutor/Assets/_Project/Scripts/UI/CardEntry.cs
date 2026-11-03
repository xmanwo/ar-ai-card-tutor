using System;

public enum CardSource
{
    /// <summary>Detected by the model.</summary>
    Model,

    /// <summary>Added or corrected by the user. Has no model confidence.</summary>
    User
}

/// <summary>
/// One card in the scanned list. A user edit changes the colour and value but keeps the
/// original model confidence, so the two are never conflated.
/// </summary>
[Serializable]
public class CardEntry
{
    public string color;          // Red / Yellow / Green / Blue / Wild
    public string value;          // 0-9, Skip, Reverse, Draw 2, Wild, Draw 4
    public CardSource source = CardSource.Model;

    /// <summary>Model confidence in 0..1. -1 means the user supplied this card.</summary>
    public float confidence = -1f;

    /// <summary>Pixel box in the captured image, top-left origin. Optional.</summary>
    public float[] bbox;

    /// <summary>
    /// The scan froze on a card the model was not sure about: either the best confidence was low
    /// or the votes were split between labels. Shown in warning colour so the player can judge.
    /// </summary>
    public bool uncertain;

    public string DisplayName =>
        string.IsNullOrEmpty(color) ? value : $"{color} {value}";

    public static CardEntry FromModel(Detection detection)
    {
        var entry = new CardEntry
        {
            source = CardSource.Model,
            confidence = detection.confidence,
            bbox = detection.bbox_xyxy
        };

        SplitLabel(detection.label, out entry.color, out entry.value);
        return entry;
    }

    /// <summary>
    /// Builds an entry from an already-resolved label. The scan client decides the label by vote
    /// across a round, so there is no single Detection to hand over.
    /// </summary>
    public static CardEntry FromLabel(string label, float confidence, float[] bbox)
    {
        var entry = new CardEntry
        {
            source = CardSource.Model,
            confidence = confidence,
            bbox = bbox
        };

        SplitLabel(label, out entry.color, out entry.value);
        return entry;
    }

    public static CardEntry FromUser(string color, string value)
    {
        return new CardEntry
        {
            color = color,
            value = value,
            source = CardSource.User,
            confidence = -1f
        };
    }

    /// <summary>Splits "Green 3" into colour and value. Unknown shapes fall back to Wild.</summary>
    static void SplitLabel(string label, out string color, out string value)
    {
        if (string.IsNullOrEmpty(label))
        {
            color = "Wild";
            value = "Unknown";
            return;
        }

        int space = label.IndexOf(' ');
        if (space < 0)
        {
            color = "Wild";
            value = label;
            return;
        }

        color = label.Substring(0, space);
        value = label.Substring(space + 1);
    }
}
