using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class Detection
{
    public int id;
    public string raw_label;
    public string label;
    public float confidence;
    public float[] bbox_xyxy;
}

[Serializable]
public class DetectResponse
{
    public int frame_id;
    public int image_width;
    public int image_height;
    public float inference_ms;
    public Detection[] detections;
}

/// <summary>
/// Uploads the most recent captured frame to the detection service and logs the parsed result.
/// One request at a time; a new upload only starts after a new frame has been captured.
/// </summary>
public class CardScanClient : MonoBehaviour
{
    [Tooltip("Leave empty to find the FrameCapture component automatically.")]
    public FrameCapture capture;

    [Tooltip("Base URL of the detection service, without a trailing slash. " +
             "Use the PC's IPv4 address, never 127.0.0.1 from the headset.")]
    public string serverUrl = "http://192.168.1.100:8000";

    public bool uploadEnabled = true;

    [Tooltip("Optional result panel. Any component implementing IScanView. Leave empty to log only.")]
    public MonoBehaviour viewBehaviour;

    public ScanState State { get; private set; } = ScanState.Ready;
    public List<CardEntry> Cards { get; } = new List<CardEntry>();

    public DetectResponse LastResponse { get; private set; }
    public string LastError { get; private set; }
    public bool IsBusy { get; private set; }
    public float LastRoundTripMs { get; private set; }

    IScanView view;
    Texture2D lastFrame;
    int lastSeenFrameId;

    void Awake()
    {
        if (capture == null)
            capture = FindFirstObjectByType<FrameCapture>();

        view = viewBehaviour as IScanView;
        if (viewBehaviour != null && view == null)
            Debug.LogError("[Scan] viewBehaviour does not implement IScanView.");
    }

    void Start()
    {
        Debug.Log($"[Scan] client started. capture={(capture != null ? capture.name : "NULL")}, " +
                  $"url={serverUrl}, uploadEnabled={uploadEnabled}");

        Render();
    }

    void Update()
    {
        // A confirmed list is final. Only Rescan may start a new round.
        if (State == ScanState.Confirmed)
            return;

        if (!uploadEnabled || IsBusy || capture == null)
            return;

        if (capture.LastFrameId == lastSeenFrameId || capture.LastJpeg == null)
            return;

        lastSeenFrameId = capture.LastFrameId;
        StartCoroutine(Upload(lastSeenFrameId, capture.LastJpeg));
    }

    IEnumerator Upload(int frameId, byte[] jpeg)
    {
        IsBusy = true;
        LastError = null;
        lastFrame = capture != null ? capture.LastTexture : null;

        State = ScanState.Scanning;
        Render();

        string url = serverUrl.TrimEnd('/') + "/detect";

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("file", jpeg, $"frame_{frameId}.jpg", "image/jpeg")
        };

        using (var request = UnityWebRequest.Post(url, form))
        {
            request.SetRequestHeader("X-Frame-Id", frameId.ToString());
            request.timeout = 8;

            float started = Time.realtimeSinceStartup;
            yield return request.SendWebRequest();
            LastRoundTripMs = (Time.realtimeSinceStartup - started) * 1000f;

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastError = $"{(int)request.responseCode} {request.error}";
                Debug.LogWarning($"[Scan] frame {frameId} FAILED after {LastRoundTripMs:F0}ms: {LastError}");
                State = ScanState.Error;
                Render();
                IsBusy = false;
                yield break;
            }

            string body = request.downloadHandler.text;
            DetectResponse parsed = null;

            try
            {
                parsed = JsonUtility.FromJson<DetectResponse>(body);
            }
            catch (Exception e)
            {
                LastError = "JSON parse failed: " + e.Message;
                Debug.LogWarning($"[Scan] frame {frameId} bad JSON ({e.Message}): {body}");
                State = ScanState.Error;
                Render();
                IsBusy = false;
                yield break;
            }

            if (parsed == null)
            {
                LastError = "Empty response";
                Debug.LogWarning($"[Scan] frame {frameId} returned an empty body");
                State = ScanState.Error;
                Render();
                IsBusy = false;
                yield break;
            }

            LastResponse = parsed;
            LogResult(frameId, parsed);

            BuildCards(parsed);
            State = ScanState.Reviewing;
            Render();
        }

        IsBusy = false;
    }

    void BuildCards(DetectResponse response)
    {
        Cards.Clear();

        if (response.detections == null)
            return;

        foreach (var detection in response.detections)
            Cards.Add(CardEntry.FromModel(detection));
    }

    /// <summary>Wire this to the Scan button. Takes one frame and uploads it.</summary>
    public void RequestScan()
    {
        if (IsBusy)
            return;

        if (capture == null)
        {
            LastError = "No camera";
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

    void LogResult(int frameId, DetectResponse r)
    {
        if (r.frame_id != frameId)
            Debug.LogWarning($"[Scan] frame id mismatch: sent {frameId}, got {r.frame_id}");

        int count = r.detections != null ? r.detections.Length : 0;
        string timing = $"{LastRoundTripMs:F0}ms round trip, {r.inference_ms:F0}ms inference";

        if (count == 0)
        {
            Debug.Log($"[Scan] frame {frameId} OK ({timing}) | No cards detected");
            return;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                sb.Append(", ");

            var d = r.detections[i];
            sb.Append($"{d.label} ({d.confidence:F2})");
        }

        Debug.Log($"[Scan] frame {frameId} OK ({timing}) | {count} cards: {sb}");
    }
}
