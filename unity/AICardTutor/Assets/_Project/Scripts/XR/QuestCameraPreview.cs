using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Templates.MR;

[RequireComponent(typeof(RawImage))]
public class QuestCameraPreview : MonoBehaviour
{
    public ARCameraManager cameraManager;

    [Tooltip("Draw the camera picture. Turned off by ScanPanel, which needs the quad out of the way.")]
    public bool showPreview = true;

    const string CameraPermission = "horizonos.permission.HEADSET_CAMERA";

    RawImage preview;
    Texture2D texture;
    AspectRatioFitter aspect;
    bool ready;
    float nextRefresh;

    IEnumerator Start()
    {
        preview = GetComponent<RawImage>();
        preview.raycastTarget = false;

        if (cameraManager == null)
        {
            Debug.LogError("QuestCameraPreview: Assign the ARCameraManager component from Main Camera.");
            enabled = false;
            yield break;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(CameraPermission))
        {
            bool finished = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => finished = true;
            callbacks.PermissionDenied += _ => finished = true;
            callbacks.PermissionDeniedAndDontAskAgain += _ => finished = true;

            Permission.RequestUserPermission(CameraPermission, callbacks);

            while (!finished)
                yield return null;

            if (!Permission.HasUserAuthorizedPermission(CameraPermission))
            {
                Debug.LogError("QuestCameraPreview: Camera permission was denied.");
                yield break;
            }
        }

        // Restart the camera after permission has been granted.
        cameraManager.enabled = false;
        yield return null;
        EnablePassthroughView();

        aspect = GetComponent<AspectRatioFitter>();
        if (aspect == null)
            aspect = gameObject.AddComponent<AspectRatioFitter>();

        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        ready = true;
#else
        Debug.Log("Camera preview requires a Quest headset. Use Build And Run to test it.");
        enabled = false;
        yield break;
#endif
    }

    void EnablePassthroughView()
    {
        // Passthrough must not depend on completing the template's disabled onboarding flow.
        var headCamera = cameraManager.GetComponent<Camera>();
        if (headCamera != null)
        {
            headCamera.clearFlags = CameraClearFlags.SolidColor;
            headCamera.backgroundColor = Color.clear;
        }

        var templateBackground = FindFirstObjectByType<FadeMaterial>();
        if (templateBackground != null && templateBackground.Environment != null)
        {
            var backgroundRenderer = templateBackground.Environment.GetComponent<Renderer>();
            if (backgroundRenderer != null)
                backgroundRenderer.enabled = false;
        }

        cameraManager.enabled = true;
        Debug.Log("[Camera] Passthrough enabled; opaque template background hidden.");
    }

    void Update()
    {
        if (!ready || !showPreview || Time.unscaledTime < nextRefresh)
            return;

        nextRefresh = Time.unscaledTime + 0.2f;

        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
            return;

        using (image)
        {
            int width = Mathf.Min(640, image.width);
            int height = Mathf.Max(1, Mathf.RoundToInt(image.height * (float)width / image.width));

            if (texture == null || texture.width != width || texture.height != height)
            {
                if (texture != null)
                    Destroy(texture);

                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                preview.texture = texture;
                aspect.aspectRatio = (float)width / height;
            }

            var conversion = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(width, height),
                outputFormat = TextureFormat.RGBA32,
                // Flip the native image vertically for an upright, unmirrored Quest preview.
                transformation = XRCpuImage.Transformation.MirrorX
            };

            image.Convert(conversion, new NativeSlice<byte>(texture.GetRawTextureData<byte>()));
            texture.Apply(false, false);
        }
    }

    /// <summary>
    /// Hide the picture without disabling this component: this is the only place that asks for
    /// horizonos.permission.HEADSET_CAMERA, and the head camera stops working without it.
    /// </summary>
    public void SetPreviewVisible(bool visible)
    {
        showPreview = visible;

        // Start() assigns the field from inside a coroutine, so it may still be null here.
        if (preview == null)
            preview = GetComponent<RawImage>();

        if (preview != null)
            preview.enabled = visible;
    }

    void OnDestroy()
    {
        if (texture != null)
            Destroy(texture);
    }
}