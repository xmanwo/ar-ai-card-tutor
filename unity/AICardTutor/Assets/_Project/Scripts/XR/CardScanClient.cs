using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Owns the scan state machine: when to look, how many frames to average over, and when a result
/// is allowed to become visible. Recognition itself is delegated to an <see cref="ICardScanSource"/>,
/// so the server path and an on-device path stay interchangeable.
///
/// A round looks like this:
///   Seeking   - only the frame difference runs. Nothing is uploaded.
///   Scanning  - one request every <see cref="requestInterval"/>; every reply is folded into a
///               track table instead of being shown.
///   Reviewing - the tracks satisfied the freeze rules and became the visible list. Nothing is
///               requested any more, so a manual edit can never be overwritten.
///   Failed    - the round ended with zero detections.
///   Error     - a request itself failed: no server, bad URL, timeout.
///
/// The panel is only re-rendered on state changes, which is what keeps a round from flickering:
/// intermediate detections never reach it. The rules are in the handover doc, section 2.
/// </summary>
public class CardScanClient : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Leave empty to find the FrameCapture component automatically.")]
    public FrameCapture capture;

    [Tooltip("Where recognition happens: HttpCardScanSource for the PC service, or the " +
             "on-device source. Leave empty to use a source on this same GameObject.")]
    public MonoBehaviour scanSource;

    [Tooltip("Optional result panel. Any component implementing IScanView.")]
    public MonoBehaviour viewBehaviour;

    public bool scanEnabled = true;

    [Header("Round timing")]
    [Tooltip("Seconds between requests while scanning. 300 ms is the default from the doc.")]
    public float requestInterval = 0.3f;

    [Tooltip("A round always runs at least this long, so the first, worst frame never decides.")]
    public float minScanSeconds = 1.5f;

    [Tooltip("A round never runs longer than this.")]
    public float maxScanSeconds = 8f;

    [Tooltip("Hard cap on requests per round, in case the network is slower than the timer.")]
    public int maxRequestsPerRound = 25;

    [Tooltip("Seconds before another rescan is accepted. Swallows the OpenXR button bounce.")]
    public float rescanCooldownSeconds = 1f;

    [Header("Freeze rules")]
    [Tooltip("How many recent requests must agree on the track count.")]
    public int stableWindow = 4;

    [Tooltip("A track must have been seen at least this many times.")]
    public int minTrackHits = 3;

    [Tooltip("Track association radius, as a fraction of the image diagonal. Centres only: the " +
             "same card produces boxes 20x apart in size, so IoU would shatter the track.")]
    public float trackMatchFraction = 0.08f;

    [Tooltip("Requests a track may be missing before it is dropped.")]
    public int trackExpiryMisses = 3;

    [Header("Wake on frame change")]
    [Tooltip("Leave Seeking when the picture changes. Turn off to start a round immediately.")]
    public bool wakeOnFrameChange = true;

    [Tooltip("Fraction of pixels that must change to wake the scanner. 1.5% by default.")]
    public float changedPixelFraction = 0.015f;

    [Tooltip("Seconds after a wake before another wake is allowed.")]
    public float wakeCooldownSeconds = 0.5f;

    [Tooltip("How often the frame difference is sampled. 10 Hz is plenty.")]
    public float sampleIntervalSeconds = 0.1f;

    [Header("Uncertain flags")]
    [Tooltip("Below this best confidence the card is shown as uncertain.")]
    public float uncertainConfidence = 0.45f;

    [Tooltip("Below this share of the votes the card is shown as uncertain.")]
    public float uncertainVoteShare = 0.6f;

    public ScanState State { get; private set; } = ScanState.Seeking;
    public List<CardEntry> Cards { get; } = new List<CardEntry>();

    /// <summary>Why the last request failed. Only meaningful in <see cref="ScanState.Error"/>.</summary>
    public string LastError { get; private set; }

    /// <summary>A short remark shown under the result, e.g. "kept the previous result".</summary>
    public string Notice { get; private set; }

    public bool IsBusy { get; private set; }
    public float LastLatencyMs { get; private set; }
    public string LastNote { get; private set; }

    /// <summary>Seconds the current round has been running. The panel ticks this while scanning.</summary>
    public float ScanElapsedSeconds =>
        State == ScanState.Scanning ? Time.unscaledTime - scanStartedAt : 0f;

    IScanView view;
    ICardScanSource source;
    FrameDiffWatcher watcher;
    Texture2D lastFrame;

    readonly List<Track> tracks = new List<Track>();
    readonly List<int> activeCountHistory = new List<int>();

    float scanStartedAt;
    float lastRequestAt = -999f;
    float lastWakeAt = -999f;
    float lastRescanAt = -999f;
    int requestCount;
    int lastNewTrackRequest = -999;
    int nextTrackId = 1;

    /// <summary>One physical card, followed across the frames of a round.</summary>
    class Track
    {
        public int id;
        public int hits;
        public int lastSeenRequest;
        public float cx;
        public float cy;
        public string bestLabel = "Unknown";
        public float bestConf = -1f;
        public float[] bestBbox;
        public readonly Dictionary<string, int> votes = new Dictionary<string, int>();
    }

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
        var cameraManager = capture != null ? capture.cameraManager : null;

        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>();

        watcher = new FrameDiffWatcher(cameraManager, sampleIntervalSeconds);

        Debug.Log($"[Scan] ready. capture={(capture != null ? capture.name : "NULL")}, " +
                  $"source={(source != null ? source.DisplayName : "NONE")}, " +
                  $"view={(view != null ? view.GetType().Name : "NONE")}, " +
                  $"wake={(wakeOnFrameChange ? "frame diff" : "off")}");

        Render();
    }

    void OnDestroy()
    {
        if (watcher != null)
            watcher.Dispose();
    }

    void Update()
    {
        if (!scanEnabled || capture == null || source == null)
            return;

        switch (State)
        {
            case ScanState.Seeking:
            case ScanState.Failed:
                // A failed round is not a dead end: moving the cards is a valid way out of it.
                TickWake();
                break;

            case ScanState.Scanning:
                TickScanning();
                break;

            // Reviewing and Confirmed are frozen. Only Rescan may leave them, otherwise a result
            // the user is reading (or editing) would be replaced under their hands.
        }
    }

    // ------------------------------------------------------------------ L0 wake

    void TickWake()
    {
        if (!wakeOnFrameChange)
        {
            // Without the frame difference a round restarts as soon as it ends. Keep the same
            // cooldown, otherwise a failing camera would spin rounds back to back.
            if (Time.unscaledTime - lastWakeAt < wakeCooldownSeconds)
                return;

            lastWakeAt = Time.unscaledTime;
            BeginRound();
            return;
        }

        if (watcher == null || !watcher.IsAvailable)
            return;

        if (Time.unscaledTime - lastWakeAt < wakeCooldownSeconds)
            return;

        if (!watcher.TrySample(out float changedFraction))
            return;

        if (changedFraction < changedPixelFraction)
            return;

        Debug.Log($"[Scan] woke: {changedFraction:P1} of the picture changed");
        lastWakeAt = Time.unscaledTime;
        BeginRound();
    }

    // ------------------------------------------------------------------ L1 loop

    void TickScanning()
    {
        if (IsBusy)
            return;

        if (Time.unscaledTime - lastRequestAt < requestInterval)
            return;

        StartCoroutine(ScanOneFrame());
    }

    IEnumerator ScanOneFrame()
    {
        IsBusy = true;
        lastRequestAt = Time.unscaledTime;

        if (!capture.CaptureForScan())
        {
            // The camera has not produced an image yet. Not a request, just wait for the next tick.
            IsBusy = false;
            yield break;
        }

        var frame = new ScanFrame
        {
            frameId = capture.LastFrameId,
            texture = capture.LastTexture,
            jpeg = capture.LastJpeg
        };

        requestCount++;

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
            // A source that throws must not leave the loop stuck for the rest of the session.
            IsBusy = false;
        }

        if (!succeeded)
        {
            LastError = failure ?? "Unknown error";
            SetState(ScanState.Error);
            Render();
            yield break;
        }

        LastLatencyMs = result.latencyMs;
        LastNote = result.note;

        int detected = result.cards != null ? result.cards.Count : 0;
        ApplyDetections(result.cards, capture.LastTexture);

        if (TryFreeze(detected))
            LogRound();
    }

    // ------------------------------------------------------------------ tracks

    void ApplyDetections(List<CardEntry> cards, Texture2D frame)
    {
        float diagonal = frame != null
            ? Mathf.Sqrt(frame.width * frame.width + frame.height * frame.height)
            : 1000f;

        float matchRadius = diagonal * trackMatchFraction;

        if (cards != null)
        {
            foreach (var card in cards)
            {
                if (card.bbox == null || card.bbox.Length < 4)
                    continue;

                float cx = (card.bbox[0] + card.bbox[2]) * 0.5f;
                float cy = (card.bbox[1] + card.bbox[3]) * 0.5f;

                Track track = FindNearest(cx, cy, matchRadius);

                if (track == null)
                {
                    track = new Track { id = nextTrackId++ };
                    tracks.Add(track);
                    lastNewTrackRequest = requestCount;
                }

                track.hits++;
                track.lastSeenRequest = requestCount;
                track.cx = cx;
                track.cy = cy;

                string label = card.DisplayName;
                track.votes.TryGetValue(label, out int votes);
                track.votes[label] = votes + 1;

                // Keep the best frame, not the latest: the last frame of a round is usually the
                // one with a hand across it.
                if (card.confidence > track.bestConf)
                {
                    track.bestConf = card.confidence;
                    track.bestLabel = label;
                    track.bestBbox = card.bbox;
                }
            }
        }

        for (int i = tracks.Count - 1; i >= 0; i--)
        {
            if (requestCount - tracks[i].lastSeenRequest >= trackExpiryMisses)
                tracks.RemoveAt(i);
        }

        activeCountHistory.Add(tracks.Count);

        if (activeCountHistory.Count > stableWindow)
            activeCountHistory.RemoveAt(0);
    }

    Track FindNearest(float cx, float cy, float maxDistance)
    {
        Track best = null;
        float bestDistance = maxDistance;

        foreach (var track in tracks)
        {
            float dx = track.cx - cx;
            float dy = track.cy - cy;
            float distance = Mathf.Sqrt(dx * dx + dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = track;
            }
        }

        return best;
    }

    // ------------------------------------------------------------------ freeze

    bool TryFreeze(int lastDetected)
    {
        float elapsed = Time.unscaledTime - scanStartedAt;
        bool timedOut = elapsed >= maxScanSeconds || requestCount >= maxRequestsPerRound;

        if (timedOut)
        {
            Debug.Log($"[Scan] round timed out after {requestCount} requests / {elapsed:F1}s");
            Freeze(unstable: true);
            return true;
        }

        if (lastDetected < 1)
            return false;                                 // F0: never freeze on an empty frame

        if (elapsed < minScanSeconds)
            return false;                                 // F4

        if (activeCountHistory.Count < stableWindow)
            return false;                                 // F1 needs a full window

        if (!AllSame(activeCountHistory))
            return false;                                 // F1

        if (requestCount - lastNewTrackRequest < stableWindow)
            return false;                                 // F2: something new just appeared

        if (!AllTracksMature())
            return false;                                 // F3

        Freeze(unstable: false);
        return true;
    }

    static bool AllSame(List<int> values)
    {
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] != values[0])
                return false;
        }

        return true;
    }

    bool AllTracksMature()
    {
        foreach (var track in tracks)
        {
            if (track.hits < minTrackHits)
                return false;
        }

        return true;
    }

    void Freeze(bool unstable)
    {
        // The frame difference was not sampled during the round, so its baseline is now several
        // seconds old. Without this reset a failed round would immediately wake itself again.
        if (watcher != null)
            watcher.Reset();

        var frozen = BuildResultCards();

        if (frozen.Count == 0)
        {
            // A round that finds nothing must not wipe a good result off the screen.
            if (Cards.Count > 0)
            {
                Notice = "Nothing recognised this time - keeping the previous result.";
                SetState(ScanState.Reviewing);
            }
            else
            {
                Notice = null;
                SetState(ScanState.Failed);
            }

            Render();
            return;
        }

        Cards.Clear();
        Cards.AddRange(frozen);
        lastFrame = capture != null ? capture.LastTexture : null;
        Notice = unstable ? "Stopped early - the picture never settled." : null;

        SetState(ScanState.Reviewing);
        Render();
    }

    List<CardEntry> BuildResultCards()
    {
        var result = new List<CardEntry>(tracks.Count);

        foreach (var track in tracks)
        {
            string label = track.bestLabel;
            int bestVotes = 0;
            int totalVotes = 0;

            foreach (var vote in track.votes)
            {
                totalVotes += vote.Value;

                if (vote.Value > bestVotes)
                {
                    bestVotes = vote.Value;
                    label = vote.Key;
                }
            }

            float share = totalVotes > 0 ? (float)bestVotes / totalVotes : 0f;

            var entry = CardEntry.FromLabel(label, track.bestConf, track.bestBbox);
            entry.uncertain = track.bestConf < uncertainConfidence || share < uncertainVoteShare;
            result.Add(entry);
        }

        // Left to right, so the list reads the same way every time.
        result.Sort((a, b) => CenterX(a).CompareTo(CenterX(b)));
        return result;
    }

    static float CenterX(CardEntry card)
    {
        if (card.bbox == null || card.bbox.Length < 4)
            return float.MaxValue;

        return (card.bbox[0] + card.bbox[2]) * 0.5f;
    }

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Starts a new round. The single entry point for the A button, the hand HUD button and the
    /// panel's Scan/Rescan buttons - three callers, one cooldown, one track reset.
    /// </summary>
    public void Rescan()
    {
        // A press during a round is ignored, not queued: restarting mid-scan would throw away
        // three seconds of tracks and the player would see the counter jump back to zero.
        if (State == ScanState.Scanning)
            return;

        if (Time.unscaledTime - lastRescanAt < rescanCooldownSeconds)
            return;

        lastRescanAt = Time.unscaledTime;
        Debug.Log("[Scan] rescan requested");

        // Cards are kept on purpose: if this round finds nothing, the old list stays on screen.
        BeginRound();
    }

    /// <summary>Kept for the panel's Scan button; same thing as <see cref="Rescan"/>.</summary>
    public void RequestScan()
    {
        Rescan();
    }

    /// <summary>Freezes the current list for good.</summary>
    public void Confirm()
    {
        if (Cards.Count == 0)
            return;

        SetState(ScanState.Confirmed);
        Debug.Log($"[Scan] confirmed {Cards.Count} cards");
        Render();
    }

    void BeginRound()
    {
        tracks.Clear();
        activeCountHistory.Clear();
        requestCount = 0;
        lastNewTrackRequest = -999;
        lastRequestAt = -999f;
        scanStartedAt = Time.unscaledTime;
        IsBusy = false;
        LastError = null;
        Notice = null;

        if (watcher != null)
            watcher.Reset();

        SetState(ScanState.Scanning);
        Render();
    }

    void SetState(ScanState next)
    {
        if (State == next)
            return;

        Debug.Log($"[Scan] {State} -> {next}");
        State = next;
    }

    void Render()
    {
        if (view == null)
            return;

        string message = State == ScanState.Error
            ? LastError ?? string.Empty
            : Notice ?? string.Empty;

        view.Render(State, lastFrame, Cards, message);
    }

    // ------------------------------------------------------------------ logging

    void LogRound()
    {
        string timing = $"{requestCount} requests, {Time.unscaledTime - scanStartedAt:F1}s";
        string via = source != null ? source.DisplayName : "?";

        if (Cards.Count == 0)
        {
            Debug.Log($"[Scan] round via {via} ({timing}) | no cards");
            return;
        }

        var sb = new StringBuilder();

        for (int i = 0; i < Cards.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");

            var card = Cards[i];
            sb.Append(card.uncertain ? $"{card.DisplayName}?" : card.DisplayName);
            sb.Append($" ({card.confidence:F2})");
        }

        Debug.Log($"[Scan] round via {via} ({timing}) | {Cards.Count} cards: {sb}");
    }
}
