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

    public ScanState State { get; private set; } = ScanState.Ready;
    public List<CardEntry> Cards { get; } = new List<CardEntry>();
    public string LastError { get; private set; }
    public bool IsBusy { get; private set; }
    public float LastLatencyMs { get; private set; }
    public string LastNote { get; private set; }

    IScanView view;
    ICardScanSource source;
    Texture2D lastFrame;
    int lastSeenFrameId;

    void Awake()
    {
        if (capture == null)
            capture = FindFirstObjectByType<FrameCapture>();

        view = viewBehaviour as IScanView;
        if (viewBehaviour != null && view == null)
            Debug.LogError("[Scan] viewBehaviour does not implement IScanView.");

        if (scanSource != null)
            source = scanSource as ICardScanSource;

        if (source == null)
            source = GetComponent<ICardScanSource>();

        if (scanSource != null && source == null)
            Debug.LogError("[Scan] scanSource does not implement ICardScanSource.");
    }

    void Start()
    {
        Debug.Log($"[Scan] ready. capture={(capture != null ? capture.name : "NULL")}, " +
                  $"source={(source != null ? source.DisplayName : "NONE")}, " +
                  $"view={(view != null ? view.GetType().Name : "NONE")}");

        Render();
    }

    void Update()
    {
        // A confirmed list is final. Only Rescan may start a new round.
        if (State == ScanState.Confirmed)
            return;

        if (!scanEnabled || IsBusy || capture == null || source == null)
            return;

        if (capture.LastFrameId == lastSeenFrameId || capture.LastJpeg == null)
            return;

        lastSeenFrameId = capture.LastFrameId;
        lastFrame = capture.LastTexture;

        StartCoroutine(RunScan(new ScanFrame
        {
            frameId = lastSeenFrameId,
            texture = lastFrame,
            jpeg = capture.LastJpeg
        }));
    }

    IEnumerator RunScan(ScanFrame frame)
    {
        IsBusy = true;
        LastError = null;
        State = ScanState.Scanning;
        Render();

        CardScanResult result = default;
        bool succeeded = false;
        string failure = null;

        yield return source.Scan(
            frame,
            value => { result = value; succeeded = true; },
            error => { failure = error; });

        if (succeeded)
        {
            LastLatencyMs = result.latencyMs;
            LastNote = result.note;

            Cards.Clear();
            if (result.cards != null)
                Cards.AddRange(result.cards);

            State = ScanState.Reviewing;
            LogResult(frame.frameId, result, Cards);
        }
        else
        {
            LastError = failure ?? "Unknown error";
            State = ScanState.Error;
            Debug.LogWarning($"[Scan] frame {frame.frameId} FAILED: {LastError}");
        }

        Render();
        IsBusy = false;
    }

    /// <summary>Wire this to the Scan button. Takes one frame and runs it through the source.</summary>
    public void RequestScan()
    {
        if (IsBusy)
            return;

        if (capture == null || source == null)
        {
            LastError = capture == null ? "No camera" : "No scan source";
            State = ScanState.Error;
            Render();
            return;
        }

        capture.Capture();
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
