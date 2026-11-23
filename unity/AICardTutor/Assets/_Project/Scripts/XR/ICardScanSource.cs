using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One captured frame, handed to a scan source.</summary>
public struct ScanFrame
{
    public int frameId;

    /// <summary>Choose the hand-specific recognition profile without changing discard scanning.</summary>
    public ScanTarget target;

    /// <summary>The complete original frame as a texture, for display.</summary>
    public Texture2D texture;

    /// <summary>Upload JPEG, which may contain only crop while texture retains the full photo.</summary>
    public byte[] jpeg;

    /// <summary>Uploaded pixels' location within the full preview, top-left origin.</summary>
    public HandCropGeometry.Crop crop;
    public int fullWidth, fullHeight;
    public bool cropped;
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
