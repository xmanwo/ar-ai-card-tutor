using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One captured frame, handed to a scan source.</summary>
public struct ScanFrame
{
    public int frameId;

    /// <summary>The frame as a texture, for display.</summary>
    public Texture2D texture;

    /// <summary>The same frame encoded as JPEG. Sources that send it over the network use this.</summary>
    public byte[] jpeg;
}

/// <summary>What a scan source hands back.</summary>
public struct CardScanResult
{
    public int frameId;
    public List<CardEntry> cards;
    public float latencyMs;

    /// <summary>Short note for the log, e.g. "35ms inference". May be empty.</summary>
    public string note;
}

/// <summary>
/// Where card recognition happens. The server source posts the frame to the PC service; the
/// on-device source reads the corner indices locally. They are interchangeable, so a failed
/// network at demo time can be swapped for the local path without touching the scan flow.
/// </summary>
public interface ICardScanSource
{
    /// <summary>Shown in logs so it is obvious which path is live.</summary>
    string DisplayName { get; }

    /// <summary>False when the source cannot run, e.g. no server URL or no model loaded.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Runs one scan. Yields while the work is in flight, then calls exactly one of the
    /// callbacks.
    /// </summary>
    IEnumerator Scan(ScanFrame frame, Action<CardScanResult> onSuccess, Action<string> onFailure);
}
