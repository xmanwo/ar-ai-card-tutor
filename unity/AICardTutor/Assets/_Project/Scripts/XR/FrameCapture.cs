using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Grabs one frame from the Quest passthrough camera, encodes it as JPEG and saves it to
/// persistentDataPath/Captures. Keeps the last frame in memory so the upload step can reuse
/// the exact same bytes that were written to disk.
/// </summary>
public class FrameCapture : MonoBehaviour
{
    [Tooltip("Leave empty to find the ARCameraManager automatically.")]
    public ARCameraManager cameraManager;

    [Tooltip("Optional. Shows 'Frame saved' / 'Camera not ready' in the headset.")]
    public Text statusText;

    [Tooltip("Press A on the right controller to capture. Useful before the UI button exists.")]
    public bool captureOnPrimaryButton = true;

    /// <summary>JPEG bytes of the most recent capture. Reuse these for upload.</summary>
    public byte[] LastJpeg { get; private set; }

    /// <summary>Monotonic id of the most recent capture. Sent as the X-Frame-Id header.</summary>
    public int LastFrameId { get; private set; }

    /// <summary>Texture of the most recent capture, for displaying the scanned image later.</summary>
    public Texture2D LastTexture { get; private set; }

    /// <summary>Full path of the most recent file on disk.</summary>
    public string LastSavedPath { get; private set; }

    // The controller button read flickers under OpenXR, which produced several captures per press.
    // A minimum gap between captures is enough to stop that; the UI button will not need it.
    const float MinSecondsBetweenCaptures = 1f;

    int nextFrameId = 1;
    bool wasPrimaryPressed;
    float lastCaptureAt = -999f;

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

    /// <summary>Capture one frame. Wire this to the Scan button's OnClick.</summary>
    public void Capture()
    {
        if (Time.unscaledTime - lastCaptureAt < MinSecondsBetweenCaptures)
            return;

        if (cameraManager == null)
        {
            Fail("Camera not ready: no ARCameraManager found.");
            return;
        }

        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
        {
            Fail("Camera not ready: no image available.");
            return;
        }

        using (image)
        {
            if (LastTexture != null)
                Destroy(LastTexture);

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
        lastCaptureAt = Time.unscaledTime;

        string folder = Path.Combine(Application.persistentDataPath, "Captures");
        Directory.CreateDirectory(folder);
        LastSavedPath = Path.Combine(folder, $"frame_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg");
        File.WriteAllBytes(LastSavedPath, LastJpeg);

        SetStatus($"Frame saved (#{LastFrameId})");
        Debug.Log($"[FrameCapture] frame {LastFrameId}: {LastSavedPath} ({LastJpeg.Length} bytes)");
    }

    void Fail(string message)
    {
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
    }
}
