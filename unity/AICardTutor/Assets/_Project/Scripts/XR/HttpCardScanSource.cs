using System;
using System.Collections;
using System.Collections.Generic;
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

/// <summary>How the headset reaches the PC service.</summary>
public enum ServerTransport
{
    /// <summary>Plain LAN: the headset dials the PC's IPv4 directly.</summary>
    Lan = 0,

    /// <summary>USB cable: adb reverse exposes the PC port on the headset's own loopback.</summary>
    Usb = 1,

    /// <summary>Both stay configured. USB is tried first, the LAN catches the fall.</summary>
    Auto = 2,
}

/// <summary>
/// Recognition on the PC: posts the JPEG to POST /detect and parses the JSON reply.
///
/// Both transports are configured at all times; the mode only decides who goes first. Over USB
/// the headset talks to 127.0.0.1 and the adb daemon forwards that port to the PC, so the request
/// path is byte-for-byte the same as on a LAN. That is why this class needs no second protocol,
/// only a place to decide the base URL.
///
/// Two independent safety nets, because a demo should not die with one cable:
///   1. Resolution picks whichever candidate answers /health, and caches it.
///   2. If the cached transport dies mid-scan, the *same frame* is retried on the other one.
/// Nothing reaches the caller as an error until both have been tried.
///
/// <see cref="SetTransport"/> reorders them at runtime (wired to B in ScanInputRouter), so the
/// choice does not have to be baked into the build.
/// </summary>
public class HttpCardScanSource : MonoBehaviour, ICardScanSource
{
    [Header("Transport")]
    [Tooltip("Lan = dial the PC's IPv4. Usb = adb reverse, dial 127.0.0.1. " +
             "Auto = USB first, LAN as the fallback. Switchable at runtime with B.")]
    public ServerTransport transport = ServerTransport.Auto;

    [Tooltip("Base URL of the detection service over LAN, without a trailing slash. " +
             "Use the PC's IPv4 address, never 127.0.0.1 from the headset.")]
    public string serverUrl = "http://192.168.1.100:8000";

    [Tooltip("Base URL as seen by the headset while adb reverse is running. Usually leave this.")]
    public string usbUrl = "http://127.0.0.1:8000";

    [Tooltip("Seconds allowed for one /health probe. Only paid when a candidate is unreachable.")]
    public int probeTimeoutSeconds = 2;

    [Tooltip("Log every probe and every transport switch.")]
    public bool logProbes = true;

    [Tooltip("Seconds allowed for one /detect request.")]
    public int timeoutSeconds = 8;

    /// <summary>The address that answered /health, or null until one does.</summary>
    string resolvedBaseUrl;

    /// <summary>Why the last resolution attempt failed, for the panel's error line.</summary>
    string lastResolveError;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(resolvedBaseUrl))
                return "Server " + resolvedBaseUrl;

            return $"Server ({transport}, not resolved)";
        }
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(serverUrl) || !string.IsNullOrWhiteSpace(usbUrl);

    /// <summary>Current mode. Auto means both are live and USB goes first.</summary>
    public ServerTransport Transport => transport;

    /// <summary>The address in use, or null before a probe has succeeded.</summary>
    public string ResolvedUrl => resolvedBaseUrl;

    /// <summary>
    /// Reorders the transports at runtime. Pass Auto to hand control back to the automatic order.
    /// The next scan re-resolves; callers usually want to Rescan right after.
    /// </summary>
    public void SetTransport(ServerTransport mode)
    {
        if (transport == mode)
            return;

        transport = mode;
        resolvedBaseUrl = null;
        lastResolveError = null;

        Debug.Log($"[Scan] transport -> {mode} (next scan re-resolves)");
    }

    // ------------------------------------------------------------------ scan

    public IEnumerator Scan(ScanFrame frame, Action<CardScanResult> onSuccess, Action<string> onFailure)
    {
        if (frame.jpeg == null || frame.jpeg.Length == 0)
        {
            onFailure?.Invoke("No image data");
            yield break;
        }

        // At most two passes: one per configured transport. A cable that dies between frames is
        // retried on the other transport inside this same call, so the scan loop above never
        // learns that a switch happened - it just gets a result.
        var dead = new List<string>(2);
        string lastFailure = null;

        for (int pass = 0; pass < 2; pass++)
        {
            if (string.IsNullOrEmpty(resolvedBaseUrl))
                yield return ResolveBaseUrl(dead);

            if (string.IsNullOrEmpty(resolvedBaseUrl))
            {
                onFailure?.Invoke(lastResolveError ?? lastFailure ?? "No reachable server");
                yield break;
            }

            string attempted = resolvedBaseUrl;

            CardScanResult result = default;
            bool succeeded = false;
            bool transportFailed = false;
            string failure = null;

            // The fallback gets half the budget. A half-dead transport (a PC that went to sleep,
            // a stale IP that still routes) would otherwise burn the full timeout twice before
            // the frame gives up, and a 16 s stall looks like a hang.
            int budget = pass == 0 ? timeoutSeconds : Mathf.Max(2, timeoutSeconds / 2);

            yield return SendDetect(
                attempted,
                frame,
                budget,
                value => { result = value; succeeded = true; },
                (message, deadTransport) => { failure = message; transportFailed = deadTransport; });

            if (succeeded)
            {
                onSuccess?.Invoke(result);
                yield break;
            }

            lastFailure = failure;

            if (!transportFailed)
            {
                // A live server answered and rejected the request. The other address would give
                // the same answer, so this is a real error and not a routing problem.
                onFailure?.Invoke(failure);
                yield break;
            }

            if (logProbes)
                Debug.LogWarning($"[Scan] {attempted} dropped mid-scan ({failure}); trying the other transport");

            dead.Add(attempted);
            resolvedBaseUrl = null;
        }

        onFailure?.Invoke(lastFailure ?? "No reachable server");
    }

    /// <summary>
    /// One POST /detect. Reports the failure together with whether it was the transport that
    /// died - that flag is what decides if retrying on the other address is worth it.
    /// </summary>
    IEnumerator SendDetect(string baseUrl, ScanFrame frame, int timeout,
                           Action<CardScanResult> onSuccess, Action<string, bool> onFailure)
    {
        string url = baseUrl.TrimEnd('/') + "/detect";

        UnityWebRequest request;

        try
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", frame.jpeg, $"frame_{frame.frameId}.jpg", "image/jpeg")
            };

            request = UnityWebRequest.Post(url, form);
        }
        catch (Exception e)
        {
            onFailure?.Invoke($"Could not build request: {e.Message}", false);
            yield break;
        }

        using (request)
        {
            request.SetRequestHeader("X-Frame-Id", frame.frameId.ToString());
            request.timeout = timeout;

            float started = Time.realtimeSinceStartup;
            yield return request.SendWebRequest();
            float elapsedMs = (Time.realtimeSinceStartup - started) * 1000f;

            if (request.result != UnityWebRequest.Result.Success)
            {
                // responseCode 0 means no HTTP response at all: the transport is gone. Anything
                // else came from a live server and must not trigger a retry on the other address.
                onFailure?.Invoke($"{(int)request.responseCode} {request.error}", request.responseCode == 0);
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
                Debug.LogWarning($"[Scan] bad JSON: {body}");
                onFailure?.Invoke("JSON parse failed: " + e.Message, false);
                yield break;
            }

            if (parsed == null)
            {
                onFailure?.Invoke("Empty response", false);
                yield break;
            }

            if (parsed.frame_id != frame.frameId)
                Debug.LogWarning($"[Scan] frame id mismatch: sent {frame.frameId}, got {parsed.frame_id}");

            var cards = new List<CardEntry>();

            if (parsed.detections != null)
            {
                foreach (var detection in parsed.detections)
                    cards.Add(CardEntry.FromModel(detection));
            }

            // The panel shows this line, so the live transport is visible without a logcat.
            string via = IsLoopback(baseUrl) ? "usb" : "lan";

            onSuccess?.Invoke(new CardScanResult
            {
                frameId = frame.frameId,
                cards = cards,
                latencyMs = elapsedMs,
                note = $"{parsed.inference_ms:F0}ms inference · {via}"
            });
        }
    }

    // ------------------------------------------------------------------ address resolution

    /// <summary>
    /// Picks a base URL by asking each candidate for /health. The body check matters: a stray
    /// process on port 8000 that answers 200 with something else must not be mistaken for the
    /// detector, and the headset may well have one.
    ///
    /// <paramref name="exclude"/> holds addresses that already failed during this scan, so a
    /// retry does not walk back into the transport that just died.
    /// </summary>
    IEnumerator ResolveBaseUrl(List<string> exclude)
    {
        resolvedBaseUrl = null;
        lastResolveError = null;

        var candidates = Candidates();
        var failures = new List<string>(candidates.Count);
        float startedAt = Time.realtimeSinceStartup;

        foreach (string candidate in candidates)
        {
            string baseUrl = candidate.TrimEnd('/');

            if (exclude != null && exclude.Contains(baseUrl))
            {
                failures.Add($"{baseUrl}: dropped earlier in this scan");
                continue;
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add($"{baseUrl}: not an http(s) URL");
                continue;
            }

            string healthUrl = baseUrl + "/health";
            string failure = null;
            bool ready = false;

            var request = UnityWebRequest.Get(healthUrl);
            request.timeout = probeTimeoutSeconds;

            try
            {
                yield return request.SendWebRequest();

                string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

                if (request.result != UnityWebRequest.Result.Success)
                    failure = $"{(int)request.responseCode} {request.error}";
                else if (request.responseCode != 200)
                    failure = $"HTTP {request.responseCode}";
                else if (string.IsNullOrEmpty(body) || !body.Contains("\"ready\""))
                    failure = "answered, but not the detection service";
                else
                    ready = true;
            }
            finally
            {
                // The probe can be torn down with the coroutine, so the request is released here
                // rather than after the yield.
                request.Dispose();
            }

            if (ready)
            {
                resolvedBaseUrl = baseUrl;

                if (logProbes)
                {
                    Debug.Log($"[Scan] transport -> {baseUrl} " +
                              $"({transport}, {(Time.realtimeSinceStartup - startedAt) * 1000f:F0} ms)");
                }

                yield break;
            }

            failures.Add($"{baseUrl}: {failure}");

            if (logProbes)
                Debug.LogWarning($"[Scan] probe missed {baseUrl}: {failure}");
        }

        lastResolveError = failures.Count > 0
            ? "No reachable server. " + string.Join(" | ", failures)
            : "No server URL configured.";

        Debug.LogError("[Scan] " + lastResolveError);
    }

    /// <summary>
    /// Every configured address, in the order this mode wants them tried. Auto keeps both, so a
    /// dead transport always has somewhere to fall.
    /// </summary>
    List<string> Candidates()
    {
        var list = new List<string>(2);

        if (transport != ServerTransport.Lan)
            AddUnique(list, usbUrl);

        if (transport != ServerTransport.Usb)
            AddUnique(list, serverUrl);

        return list;
    }

    static void AddUnique(List<string> list, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        string trimmed = url.Trim();

        foreach (string existing in list)
        {
            if (string.Equals(existing.TrimEnd('/'), trimmed.TrimEnd('/'),
                              StringComparison.OrdinalIgnoreCase))
                return;
        }

        list.Add(trimmed);
    }

    static bool IsLoopback(string baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return false;

        return baseUrl.Contains("127.0.0.1") || baseUrl.Contains("localhost") ||
               baseUrl.Contains("[::1]");
    }
}
