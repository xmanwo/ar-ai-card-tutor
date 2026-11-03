using System.Collections.Generic;
using UnityEngine;

public enum ScanState
{
    /// <summary>Watching for the picture to change. No request is in flight and none is sent.</summary>
    Seeking,

    /// <summary>A round is running: one request every few hundred milliseconds, building tracks.</summary>
    Scanning,

    /// <summary>The result froze. The user may edit the list; it must not be overwritten.</summary>
    Reviewing,

    /// <summary>The user confirmed. The list is frozen for good.</summary>
    Confirmed,

    /// <summary>The round finished and recognised nothing. Not an error: try another angle.</summary>
    Failed,

    /// <summary>The request itself failed: no server, bad URL, timeout. Retry in place.</summary>
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
