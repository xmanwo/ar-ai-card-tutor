using System.Collections.Generic;
using UnityEngine;

public enum ScanState
{
    /// <summary>Nothing scanned yet, or the last round was cleared.</summary>
    Ready,

    /// <summary>A request is in flight. No list should be shown as final.</summary>
    Scanning,

    /// <summary>A result arrived. The user may edit the list; it must not be overwritten.</summary>
    Reviewing,

    /// <summary>The user confirmed. The list is frozen.</summary>
    Confirmed,

    /// <summary>The request failed. Show the reason and offer a retry.</summary>
    Error
}

/// <summary>
/// Everything the result panel needs to draw. The implementation must not touch the network,
/// the camera, or the scan state itself — it only renders what it is given.
/// </summary>
public interface IScanView
{
    /// <param name="state">Which of the three pages to show.</param>
    /// <param name="frame">The image this result belongs to. May be null before the first scan.</param>
    /// <param name="cards">The list for <paramref name="frame"/>. May be empty, never null.</param>
    /// <param name="message">Error text or a short status line. May be empty.</param>
    void Render(ScanState state, Texture2D frame, IReadOnlyList<CardEntry> cards, string message);
}
