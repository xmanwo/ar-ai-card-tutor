using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(RawImage))]
public class QuestCameraPreview : MonoBehaviour
{
    public ARCameraManager cameraManager;

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
        cameraManager.enabled = true;

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

    void Update()
    {
        if (!ready || Time.unscaledTime < nextRefresh)
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

    void OnDestroy()
    {
        if (texture != null)
            Destroy(texture);
    }
}