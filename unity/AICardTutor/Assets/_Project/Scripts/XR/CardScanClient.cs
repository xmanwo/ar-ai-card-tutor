using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Owns the scan state machine and the result list. Recognition itself is delegated to an
/// <see cref="ICardScanSource"/>, so the server path and the on-device path are interchangeable
/// without touching this class.
/// </summary>
public class CardScanClient : MonoBehaviour
{
    [Tooltip("Leave empty to find the FrameCapture component automatically.")]
    public FrameCapture capture;

    [Tooltip("Where recognition happens: HttpCardScanSource for the PC service, or the " +
             "on-device source. Leave empty to use a source on this same GameObject.")]
    public MonoBehaviour scanSource;

    [Tooltip("Optional result panel. Any component implementing IScanView.")]
    public MonoBehaviour viewBehaviour;

    public bool scanEnabled = true;

    [Header("Automatic scanning")]
    [Tooltip("Keep scanning on its own, so a card held in view is recognised without pressing A. " +
             "Pressing A still forces an immediate scan.")]
    public bool autoScan = true;

    [Tooltip("Seconds between automatic scans while the result is still changing.")]
    public float autoScanInterval = 2f;

    [Tooltip("Seconds between automatic scans once two results in a row came back identical. " +
             "Keeps the headset from uploading the same picture forever.")]
    public float autoScanIdleInterval = 4f;

    public ScanState State { get; private set; } = ScanState.Ready;
    public List<CardEntry> Cards { get; } = new List<CardEntry>();
    public string LastError { get; private set; }
    public bool IsBusy { get; private set; }
    public float LastLatencyMs { get; private set; }
    public string LastNote { get; private set; }

    /// <summary>True when the panel should present itself as watching rather than waiting for a tap.</summary>
    public bool AutoScanning => autoScan;

    IScanView view;
    ICardScanSource source;
    Texture2D lastFrame;
    int lastSeenFrameId;
    float nextAutoScanAt;
    string lastResultSignature;
    int sameResultStreak;

    void Awake()
    {
        if (capture == null)
            capture = FindFirstObjectByType<FrameCapture>();

        view = viewBehaviour as IScanView;

        // Dragging a GameObject onto a MonoBehaviour field picks the first MonoBehaviour on it,
        // which is usually the GraphicRaycaster rather than the view. Look one level deeper.
        if (view == null && viewBehaviour != null)
            view = viewBehaviour.GetComponent<IScanView>();

        if (viewBehaviour != null && view == null)
            Debug.LogError("[Scan] viewBehaviour has no component implementing IScanView.");

        if (scanSource != null)
            source = scanSource as ICardScanSource;

        if (source == null)
            source = GetComponent<ICardScanSource>();

        if (scanSource != null && source == null)
            Debug.LogError("[Scan] scanSource does not implement ICardScanSource.");
    }

    void Start()
    {
        string autoText = autoScan
            ? $"{autoScanInterval}s, idle {autoScanIdleInterval}s"
            : "off";

        Debug.Log($"[Scan] ready. capture={(capture != null ? capture.name : "NULL")}, " +
                  $"source={(source != null ? source.DisplayName : "NONE")}, " +
                  $"view={(view != null ? view.GetType().Name : "NONE")}, auto={autoText}");

        Render();
    }

    void Update()
    {
        // A confirmed list is final. Only Rescan may start a new round.
        if (State == ScanState.Confirmed)
            return;

        if (!scanEnabled || IsBusy || capture == null || source == null)
            return;

        // The auto scanner asks FrameCapture for a frame; the frame itself is picked up below,
        // exactly like a manual press, so there is only one path into RunScan.
        if (autoScan && Time.unscaledTime >= nextAutoScanAt)
        {
            ScheduleNextAutoScan();
            capture.CaptureQuiet();
            return;
        }

        if (capture.LastFrameId == lastSeenFrameId || capture.LastJpeg == null)
            return;

        lastSeenFrameId = capture.LastFrameId;
        lastFrame = capture.LastTexture;

        // A manual press gets the "Scanning..." page; an automatic refresh must not flash the
        // panel every couple of seconds, so it updates the list in place.
        StartCoroutine(RunScan(new ScanFrame
        {
            frameId = lastSeenFrameId,
            texture = lastFrame,
            jpeg = capture.LastJpeg
        }, quiet: !capture.LastCaptureWasManual));
    }

    IEnumerator RunScan(ScanFrame frame, bool quiet)
    {
        IsBusy = true;
        LastError = null;

        if (!quiet)
        {
            State = ScanState.Scanning;
            Render();
        }

        CardScanResult result = default;
        bool succeeded = false;
        string failure = null;

        try
        {
            yield return source.Scan(
                frame,
                value => { result = value; succeeded = true; },
                error => { failure = error; });
        }
        finally
        {
            // A source that throws must not leave the scan loop stuck for the rest of the session.
            IsBusy = false;
        }

        if (succeeded)
        {
            LastLatencyMs = result.latencyMs;
            LastNote = result.note;

            Cards.Clear();
            if (result.cards != null)
                Cards.AddRange(result.cards);

            State = ScanState.Reviewing;
            TrackResultStability();
            LogResult(frame.frameId, result, Cards);
        }
        else if (quiet)
        {
            // One dropped auto scan must not wipe a result the user is reading.
            Debug.LogWarning($"[Scan] auto frame {frame.frameId} failed: {failure ?? "Unknown error"}");
            ScheduleNextAutoScan();
        }
        else
        {
            LastError = failure ?? "Unknown error";
            State = ScanState.Error;
            Debug.LogWarning($"[Scan] frame {frame.frameId} FAILED: {LastError}");
        }

        Render();
    }

    void ScheduleNextAutoScan()
    {
        float interval = sameResultStreak >= 2 ? autoScanIdleInterval : autoScanInterval;
        nextAutoScanAt = Time.unscaledTime + interval;
    }

    /// <summary>
    /// Slows the auto scanner down once the same answer comes back twice, so holding one card
    /// still does not keep the CPU and the network busy.
    /// </summary>
    void TrackResultStability()
    {
        string signature = ResultSignature();

        if (signature == lastResultSignature)
            sameResultStreak++;
        else
        {
            lastResultSignature = signature;
            sameResultStreak = 0;
        }

        ScheduleNextAutoScan();
    }

    string ResultSignature()
    {
        if (Cards.Count == 0)
            return string.Empty;

        var names = new List<string>(Cards.Count);
        foreach (var card in Cards)
            names.Add(card.DisplayName);

        names.Sort(string.CompareOrdinal);
        return string.Join("|", names);
    }

    /// <summary>Wire this to the Scan button. Takes one frame and runs it through the source.</summary>
    public void RequestScan()
    {
        if (capture == null || source == null)
        {
            LastError = capture == null ? "No camera" : "No scan source";
            State = ScanState.Error;
            Render();
            return;
        }

        // Capture even while a scan is in flight: the frame waits for the running request and is
        // picked up right after it, so a press never feels dropped.
        capture.Capture();

        // A press means "look again now": drop the backoff so the next automatic pass is not
        // delayed by a result that was already stable.
        sameResultStreak = 0;
        lastResultSignature = null;
        nextAutoScanAt = Time.unscaledTime + autoScanInterval;
    }

    /// <summary>Freezes the current list. Later responses must not overwrite it.</summary>
    public void Confirm()
    {
        if (Cards.Count == 0)
            return;

        State = ScanState.Confirmed;
        Debug.Log($"[Scan] confirmed {Cards.Count} cards");
        Render();
    }

    /// <summary>Starts a new round and discards the previous result.</summary>
    public void Rescan()
    {
        Cards.Clear();
        LastError = null;
        State = ScanState.Ready;
        Render();
        RequestScan();
    }

    void Render()
    {
        if (view == null)
            return;

        view.Render(State, lastFrame, Cards, LastError ?? string.Empty);
    }

    void LogResult(int frameId, CardScanResult result, List<CardEntry> cards)
    {
        string timing = $"{result.latencyMs:F0}ms round trip";
        if (!string.IsNullOrEmpty(result.note))
            timing += $", {result.note}";

        string via = source != null ? source.DisplayName : "?";

        if (cards.Count == 0)
        {
            Debug.Log($"[Scan] frame {frameId} OK via {via} ({timing}) | No cards detected");
            return;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < cards.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");

            var card = cards[i];
            sb.Append(card.confidence >= 0f
                ? $"{card.DisplayName} ({card.confidence:F2})"
                : card.DisplayName);
        }

        Debug.Log($"[Scan] frame {frameId} OK via {via} ({timing}) | {cards.Count} cards: {sb}");
    }
}
