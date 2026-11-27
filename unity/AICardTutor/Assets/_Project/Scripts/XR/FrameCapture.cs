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

    /// <summary>Upload JPEG: cropped for focused hand scans, full-size for other captures.</summary>
    public byte[] LastJpeg { get; private set; }

    /// <summary>Monotonic id of the most recent capture. Sent as the X-Frame-Id header.</summary>
    public int LastFrameId { get; private set; }

    /// <summary>Texture of the most recent capture, for the result panel's thumbnail.</summary>
    public Texture2D LastTexture { get; private set; }
    /// <summary>Exact texture used to encode LastJpeg, before JPEG compression.</summary>
    public Texture2D LastRecognitionTexture { get; private set; }

    /// <summary>Full path of the most recent manual capture. Null for scan frames.</summary>
    public string LastSavedPath { get; private set; }

    public HandRoiProvider handRoi;
    /// <summary>Actual uploaded area, in full upright image pixels (top-left origin).</summary>
    public HandCropGeometry.Crop LastScanCrop { get; private set; }
    public bool LastScanCropped { get; private set; }
    public string LastCropNote { get; private set; } = "Full frame";
    public Pose LastHeadPose { get; private set; }
    public bool LastHeadTracked { get; private set; }
    public RgbViewProjection LastRgbProjection { get; private set; }
    Texture2D cropTexture;

    int nextFrameId = 1;
    string lastLoggedCrop;
    float nextCropLog;

    void Awake()
    {
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<ARCameraManager>();
        if (handRoi == null) handRoi = GetComponent<HandRoiProvider>();
        if (handRoi == null) handRoi = gameObject.AddComponent<HandRoiProvider>();
    }

    /// <summary>
    /// Grab a frame for the scan loop. Stays in memory: a round takes up to 25 of these and the
    /// headset should not end up with a photo album of them.
    /// </summary>
    public bool CaptureForScan(ScanTarget target = ScanTarget.Hand,HandCropGeometry.Crop? region = null,bool fullScene=false)
    {
        return CaptureFrame(saveToDisk: false, quality: scanJpegQuality, target: target,region:region,fullScene:fullScene);
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

    bool CaptureFrame(bool saveToDisk, int quality, ScanTarget target = ScanTarget.Hand,HandCropGeometry.Crop? region=null,bool fullScene=false)
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
            LastHeadTracked=HandRoiProvider.TryGetTrackedHead(out var headPose);LastHeadPose=headPose;
            LastRgbProjection=handRoi!=null && handRoi.TryGetRgbProjection(cameraManager,image.width,image.height,out var rgb) ? rgb : default;
            if(region.HasValue && (region.Value.X<0 || region.Value.Y<0 || region.Value.Width<128 || region.Value.Height<128 ||
                region.Value.X+region.Value.Width>image.width || region.Value.Y+region.Value.Height>image.height))return false;
            // Full native resolution: the model needs the detail, so the JPEG is not downscaled.
            if (LastTexture == null || LastTexture.width != image.width || LastTexture.height != image.height)
            {
                if (LastTexture != null) Destroy(LastTexture);
                LastTexture = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);
            }

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
            LastScanCrop = !saveToDisk && handRoi != null
                ? handRoi.GetCrop(cameraManager, image.width, image.height, target)
                : new HandCropGeometry.Crop(0, 0, image.width, image.height);
            LastScanCropped = !saveToDisk && handRoi != null && handRoi.LastCropped;
            LastCropNote = !saveToDisk && handRoi != null ? handRoi.LastNote : "Full frame · Photo";
            if(region.HasValue)
            {LastScanCrop=region.Value;LastScanCropped=true;LastCropNote="Table verification · Dedicated region";}
            if(region.HasValue && target==ScanTarget.Hand)LastCropNote="Live · Following cards · Recognition input";
            if(fullScene){LastScanCrop=new HandCropGeometry.Crop(0,0,image.width,image.height);LastScanCropped=false;LastCropNote="Live · Whole-scene recovery";}
        }

        Texture2D upload = LastTexture;
        if (LastScanCropped)
        {
            var crop = LastScanCrop;
            if (cropTexture == null || cropTexture.width != crop.Width || cropTexture.height != crop.Height)
            {
                if (cropTexture != null) Destroy(cropTexture);
                cropTexture = new Texture2D(crop.Width, crop.Height, TextureFormat.RGBA32, false);
            }
            var pixels = LastTexture.GetRawTextureData<Color32>();
            var destination = cropTexture.GetRawTextureData<Color32>();
            // Unity texture rows start at the bottom; JPEG/detection coordinates start at the top.
            for (int row = 0; row < crop.Height; row++)
                NativeArray<Color32>.Copy(pixels, HandCropGeometry.TextureRowOffset(crop, LastTexture.width, LastTexture.height, row),
                    destination, row * crop.Width, crop.Width);
            cropTexture.Apply(false, false);
            upload = cropTexture;
        }
        LastRecognitionTexture = upload;
        LastJpeg = ImageConversion.EncodeToJPG(upload, quality);
        LastFrameId = nextFrameId++;
        if (!saveToDisk && target == ScanTarget.Hand &&
            (lastLoggedCrop != LastCropNote || Time.unscaledTime >= nextCropLog))
        {
            lastLoggedCrop = LastCropNote;
            nextCropLog = Time.unscaledTime + 2;
            Debug.Log($"[HandROI] frame={LastFrameId} {LastCropNote.Replace('\n', ' ')} " +
                $"crop={LastScanCrop.X},{LastScanCrop.Y},{LastScanCrop.Width},{LastScanCrop.Height} " +
                (handRoi != null ? handRoi.LastDiagnostics : ""));
        }

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
        if (cropTexture != null) Destroy(cropTexture);
    }
}
