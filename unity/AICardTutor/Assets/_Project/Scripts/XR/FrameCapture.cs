using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Grabs one frame from the Quest passthrough camera and encodes it as JPEG. Manual captures also
/// land in persistentDataPath/Captures; the automatic ones stay in memory, because the scanner
/// asks for one every couple of seconds. The frame is kept so the upload step can reuse the exact
/// same bytes that were encoded.
/// </summary>
public class FrameCapture : MonoBehaviour
{
    [Tooltip("Leave empty to find the ARCameraManager automatically.")]
    public ARCameraManager cameraManager;

    [Tooltip("Optional. Shows 'Frame saved' / 'Camera not ready' in the headset.")]
    public Text statusText;

    [Tooltip("Press A on the right controller to capture. Useful before the UI button exists.")]
    public bool captureOnPrimaryButton = true;

    [Tooltip("Write manual captures to persistentDataPath/Captures. Auto scans never write, " +
             "otherwise a session leaves thousands of files behind.")]
    public bool saveManualCaptures = true;

    /// <summary>JPEG bytes of the most recent capture. Reuse these for upload.</summary>
    public byte[] LastJpeg { get; private set; }

    /// <summary>Monotonic id of the most recent capture. Sent as the X-Frame-Id header.</summary>
    public int LastFrameId { get; private set; }

    /// <summary>Texture of the most recent capture, for displaying the scanned image later.</summary>
    public Texture2D LastTexture { get; private set; }

    /// <summary>Full path of the most recent file on disk. Null for auto captures.</summary>
    public string LastSavedPath { get; private set; }

    /// <summary>True when the most recent capture came from the user rather than the auto scanner.</summary>
    public bool LastCaptureWasManual { get; private set; }

    // The controller button read flickers under OpenXR, which produced several captures per press.
    // Manual presses still need to stay responsive while the auto scanner runs, so the manual gap
    // only has to swallow the repeat, not enforce a real cooldown.
    const float MinSecondsBetweenManualCaptures = 0.25f;

    // The auto scanner asks for a frame on its own timer; this only stops a frame that is already
    // in flight from being grabbed twice.
    const float MinSecondsBetweenAutoCaptures = 0.5f;

    int nextFrameId = 1;
    bool wasPrimaryPressed;
    float lastManualCaptureAt = -999f;
    float lastAutoCaptureAt = -999f;
    Texture2D previousTexture;

    void Awake()
    {
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>();
    }

    void Update()
    {
        if (!captureOnPrimaryButton)
            return;

        if (!TryReadPrimaryButton(out bool pressed))
            return;

        if (pressed && !wasPrimaryPressed)
            Capture();

        wasPrimaryPressed = pressed;
    }

    /// <summary>Capture one frame for the user. Wire this to the Scan button's OnClick.</summary>
    public void Capture()
    {
        if (Time.unscaledTime - lastManualCaptureAt < MinSecondsBetweenManualCaptures)
            return;

        lastManualCaptureAt = Time.unscaledTime;
        CaptureFrame(saveToDisk: saveManualCaptures, manual: true);
    }

    /// <summary>
    /// Capture one frame for the auto scanner: no file on disk and no status text, because this
    /// runs every couple of seconds for the whole session.
    /// </summary>
    public void CaptureQuiet()
    {
        if (Time.unscaledTime - lastAutoCaptureAt < MinSecondsBetweenAutoCaptures)
            return;

        lastAutoCaptureAt = Time.unscaledTime;
        CaptureFrame(saveToDisk: false, manual: false);
    }

    void CaptureFrame(bool saveToDisk, bool manual)
    {
        if (cameraManager == null)
        {
            Fail("Camera not ready: no ARCameraManager found.", manual);
            return;
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
        {
            Fail("Camera not ready: no image available.", manual);
            return;
        }

        using (image)
        {
            // Keep the previous frame alive: the result panel is still showing it while the next
            // automatic scan is in flight, and destroying it here would blank the thumbnail.
            if (previousTexture != null)
                Destroy(previousTexture);

            previousTexture = LastTexture;

            // Full native resolution: the model needs detail, the preview does not.
            LastTexture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);

            var conversion = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(image.width, image.height),
                outputFormat = TextureFormat.RGBA32,
                // Must match QuestCameraPreview, otherwise the saved image is not what the user saw.
                transformation = XRCpuImage.Transformation.MirrorX
            };

            image.Convert(conversion, new NativeSlice<byte>(LastTexture.GetRawTextureData<byte>()));
            LastTexture.Apply(false, false);
        }

        LastJpeg = ImageConversion.EncodeToJPG(LastTexture, 90);
        LastFrameId = nextFrameId++;
        LastCaptureWasManual = manual;

        if (saveToDisk)
        {
            string folder = Path.Combine(Application.persistentDataPath, "Captures");
            Directory.CreateDirectory(folder);
            LastSavedPath = Path.Combine(folder, $"frame_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
            File.WriteAllBytes(LastSavedPath, LastJpeg);
        }
        else
        {
            LastSavedPath = null;
        }

        if (manual)
        {
            SetStatus($"Frame saved (#{LastFrameId})");
            Debug.Log($"[FrameCapture] frame {LastFrameId}: {LastSavedPath ?? "memory only"} " +
                      $"({LastJpeg.Length} bytes)");
        }
    }

    void Fail(string message, bool showStatus)
    {
        // An automatic pass that finds no image is normal and happens every couple of seconds;
        // only a deliberate press is worth a warning and a status line.
        if (!showStatus)
            return;

        SetStatus("Camera not ready");
        Debug.LogWarning($"[FrameCapture] {message}");
    }

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    static bool TryReadPrimaryButton(out bool pressed)
    {
        pressed = false;

        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.isValid)
            device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!device.isValid)
            return false;

        return device.TryGetFeatureValue(CommonUsages.primaryButton, out pressed);
    }

    void OnDestroy()
    {
        if (LastTexture != null)
            Destroy(LastTexture);

        if (previousTexture != null)
            Destroy(previousTexture);
    }
}
