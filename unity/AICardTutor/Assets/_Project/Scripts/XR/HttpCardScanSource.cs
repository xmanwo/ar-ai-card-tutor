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

/// <summary>
/// Recognition on the PC: posts the JPEG to POST /detect and parses the JSON reply.
/// This is the path used when the headset and the PC share a working network.
/// </summary>
public class HttpCardScanSource : MonoBehaviour, ICardScanSource
{
    [Tooltip("Base URL of the detection service, without a trailing slash. " +
             "Use the PC's IPv4 address, never 127.0.0.1 from the headset.")]
    public string serverUrl = "http://192.168.1.100:8000";

    public int timeoutSeconds = 8;

    public string DisplayName => "Server";

    public bool IsAvailable => !string.IsNullOrEmpty(serverUrl);

    public IEnumerator Scan(ScanFrame frame, Action<CardScanResult> onSuccess, Action<string> onFailure)
    {
        if (frame.jpeg == null || frame.jpeg.Length == 0)
        {
            onFailure?.Invoke("No image data");
            yield break;
        }

        string url = serverUrl.TrimEnd('/') + "/detect";

        var form = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("file", frame.jpeg, $"frame_{frame.frameId}.jpg", "image/jpeg")
        };

        using (var request = UnityWebRequest.Post(url, form))
        {
            request.SetRequestHeader("X-Frame-Id", frame.frameId.ToString());
            request.timeout = timeoutSeconds;

            float started = Time.realtimeSinceStartup;
            yield return request.SendWebRequest();
            float elapsedMs = (Time.realtimeSinceStartup - started) * 1000f;

            if (request.result != UnityWebRequest.Result.Success)
            {
                onFailure?.Invoke($"{(int)request.responseCode} {request.error}");
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
                onFailure?.Invoke("JSON parse failed: " + e.Message);
                yield break;
            }

            if (parsed == null)
            {
                onFailure?.Invoke("Empty response");
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

            onSuccess?.Invoke(new CardScanResult
            {
                frameId = frame.frameId,
                cards = cards,
                latencyMs = elapsedMs,
                note = $"{parsed.inference_ms:F0}ms inference"
            });
        }
    }
}
