using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// L0 of the scan rules: answers "did the picture change?" by comparing a 160x120 grey thumbnail
/// with the previous one. It never tries to decide what is in the picture.
///
/// Card-outline detection was the obvious alternative and it does not work here: three cards side
/// by side merge into one blob, the white border is lit unevenly, and the shape filter then throws
/// the whole blob away. Frame difference only has to answer a question it can actually answer.
///
/// Cost: the reference implementation suggests a GPU blit plus AsyncGPUReadback, because a CPU
/// image acquire is not free. AR Foundation only exposes the CPU image on this platform, so this
/// samples at a low rate instead (10 Hz by default). One 160x120 conversion is a few hundred
/// microseconds; at 10 Hz it is well under a percent of the frame budget.
/// </summary>
public class FrameDiffWatcher : IDisposable
{
    public const int Width = 160;
    public const int Height = 120;

    /// <summary>Luma difference, 0-255, above which a pixel counts as changed. Ignores sensor noise.</summary>
    const int LumaThreshold = 12;

    readonly ARCameraManager cameraManager;
    readonly float minIntervalSeconds;

    readonly byte[] previousLuma = new byte[Width * Height];
    readonly byte[] currentLuma = new byte[Width * Height];

    NativeArray<byte> pixels;
    bool hasPrevious;
    float nextSampleAt;

    public FrameDiffWatcher(ARCameraManager cameraManager, float minIntervalSeconds)
    {
        this.cameraManager = cameraManager;
        this.minIntervalSeconds = Mathf.Max(0.02f, minIntervalSeconds);
    }

    public bool IsAvailable => cameraManager != null;

    /// <summary>Throw away the baseline, so the next sample only reports changes from now on.</summary>
    public void Reset()
    {
        hasPrevious = false;
    }

    /// <summary>
    /// Samples the camera if the interval has elapsed.
    /// Returns false when the sample was skipped or no image was available; the caller simply
    /// tries again next frame.
    /// </summary>
    public bool TrySample(out float changedFraction)
    {
        changedFraction = 0f;

        if (cameraManager == null)
            return false;

        if (Time.unscaledTime < nextSampleAt)
            return false;

        nextSampleAt = Time.unscaledTime + minIntervalSeconds;

        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
            return false;

        using (image)
        {
            if (!pixels.IsCreated)
                pixels = new NativeArray<byte>(Width * Height * 4, Allocator.Persistent);

            var conversion = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(Width, Height),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.None
            };

            image.Convert(conversion, pixels);
        }

        for (int i = 0; i < Width * Height; i++)
        {
            int p = i * 4;
            currentLuma[i] = (byte)((pixels[p] * 299 + pixels[p + 1] * 587 + pixels[p + 2] * 114) / 1000);
        }

        if (!hasPrevious)
        {
            Buffer.BlockCopy(currentLuma, 0, previousLuma, 0, currentLuma.Length);
            hasPrevious = true;
            return false;
        }

        int changed = 0;

        for (int i = 0; i < currentLuma.Length; i++)
        {
            int delta = currentLuma[i] - previousLuma[i];

            if (delta > LumaThreshold || delta < -LumaThreshold)
                changed++;
        }

        Buffer.BlockCopy(currentLuma, 0, previousLuma, 0, currentLuma.Length);

        changedFraction = (float)changed / currentLuma.Length;
        return true;
    }

    public void Dispose()
    {
        if (pixels.IsCreated)
            pixels.Dispose();
    }
}
