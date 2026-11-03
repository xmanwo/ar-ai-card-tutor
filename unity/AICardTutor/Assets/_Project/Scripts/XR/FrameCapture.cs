using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Grabs one frame from the Quest passthrough camera and encodes it as JPEG.
///
/// <see cref="CaptureForScan"/> is the path the scan loop uses: no file on disk and a slightly
/// lower quality, because it runs several times a second. <see cref="Capture"/> is the manual
/// path, which also writes the frame to persistentDataPath/Captures so the pictures behind a bad
/// recognition can be looked at later.
///
/// The A button is deliberately not read here any more: it belongs to ScanInputRouter, so that a
/// press and the hand HUD button both go through the same Rescan() entry point.
/// </summary>
public class FrameCapture : MonoBehaviour
{
    [Tooltip("Leave empty to find the ARCameraManager automatically.")]
    public ARCameraManager cameraManager;

    [Tooltip("Optional. Shows 'Frame saved' / 'Camera not ready' in the headset.")]
    public Text statusText;

    [Tooltip("JPEG quality for scan frames. 80 keeps the upload small and the encode short.")]
    [Range(40, 95)] public int scanJpegQuality = 80;

    [Tooltip("JPEG quality for manual captures written to disk.")]
    [Range(40, 100)] public int savedJpegQuality = 90;

    /// <summary>JPEG bytes of the most recent capture. Reuse these for upload.</summary>
    public byte[] LastJpeg { get; private set; }

    /// <summary>Monotonic id of the most recent capture. Sent as the X-Frame-Id header.</summary>
    public int LastFrameId { get; private set; }

    /// <summary>Texture of the most recent capture, for the result panel's thumbnail.</summary>
    public Texture2D LastTexture { get; private set; }

    /// <summary>Full path of the most recent manual capture. Null for scan frames.</summary>
    public string LastSavedPath { get; private set; }

    int nextFrameId = 1;

    void Awake()
    {
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>();
    }

    /// <summary>
    /// Grab a frame for the scan loop. Stays in memory: a round takes up to 25 of these and the
    /// headset should not end up with a photo album of them.
    /// </summary>
    public bool CaptureForScan()
    {
        return CaptureFrame(saveToDisk: false, quality: scanJpegQuality);
    }

    /// <summary>Grab a frame and keep it on disk. Used for collecting pictures behind bad results.</summary>
    public bool Capture()
    {
        bool ok = CaptureFrame(saveToDisk: true, quality: savedJpegQuality);

        if (ok)
        {
            SetStatus($"Frame saved (#{LastFrameId})");
            Debug.Log($"[FrameCapture] frame {LastFrameId}: {LastSavedPath} ({LastJpeg.Length} bytes)");
        }

        return ok;
    }

    bool CaptureFrame(bool saveToDisk, int quality)
    {
        if (cameraManager == null)
        {
            SetStatus("Camera not ready");
            return false;
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
            return false;

        using (image)
        {
            if (LastTexture != null)
                Destroy(LastTexture);

            // Full native resolution: the model needs the detail, so the JPEG is not downscaled.
            LastTexture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);

            var conversion = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(image.width, image.height),
                outputFormat = TextureFormat.RGBA32,
                // Must match what the player sees, otherwise the saved picture is mirrored.
                transformation = XRCpuImage.Transformation.MirrorX
            };

            image.Convert(conversion, new NativeSlice<byte>(LastTexture.GetRawTextureData<byte>()));
            LastTexture.Apply(false, false);
        }

        LastJpeg = ImageConversion.EncodeToJPG(LastTexture, quality);
        LastFrameId = nextFrameId++;

        if (!saveToDisk)
        {
            LastSavedPath = null;
            return true;
        }

        string folder = Path.Combine(Application.persistentDataPath, "Captures");
        Directory.CreateDirectory(folder);
        LastSavedPath = Path.Combine(folder, $"frame_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
        File.WriteAllBytes(LastSavedPath, LastJpeg);
        return true;
    }

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    void OnDestroy()
    {
        if (LastTexture != null)
            Destroy(LastTexture);
    }
}
