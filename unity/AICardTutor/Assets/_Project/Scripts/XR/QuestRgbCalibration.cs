using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads Camera2 metadata only; it never opens a second camera session. The AR Foundation
/// provider does not expose its selected RGB camera id, so crop bounds cover both calibrated
/// RGB lenses. Coordinate convention follows Meta's Camera2/PassthroughCameraUtils sample:
/// https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples/tree/b4addcbbde63f249c91271af509cf3c9525bf75f
/// </summary>
public sealed class QuestRgbCalibration
{
    public readonly List<Pose> OpticalFromHead = new List<Pose>();
    float retryAt;

    public bool EnsureLoaded()
    {
        if (OpticalFromHead.Count > 0) return true;
        if (Time.unscaledTime < retryAt) return false;
        retryAt = Time.unscaledTime + 5;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var manager = activity.Call<AndroidJavaObject>("getSystemService", "camera");
            foreach (string id in manager.Call<string[]>("getCameraIdList"))
            {
                using var characteristics = manager.Call<AndroidJavaObject>("getCameraCharacteristics", id);
                using var keys = characteristics.Call<AndroidJavaObject>("getKeys");
                bool passthrough = false, hasPosition = false;
                for (int i = 0; i < keys.Call<int>("size"); i++)
                {
                    using var key = keys.Call<AndroidJavaObject>("get", i);
                    string name = key.Call<string>("getName");
                    if (name != "com.meta.extra_metadata.camera_source" && name != "com.meta.extra_metadata.position") continue;
                    // Meta vendor tags may be byte arrays (older OS) or boxed integers.
                    using var value = characteristics.Call<AndroidJavaObject>("get", key);
                    if (value == null) continue;
                    int number = ReadVendorNumber(value);
                    if (name.EndsWith("camera_source")) passthrough = number == 0;
                    else hasPosition = number == 0 || number == 1;
                }
                if (!passthrough || !hasPosition) continue;
                using var translationKey = characteristics.GetStatic<AndroidJavaObject>("LENS_POSE_TRANSLATION");
                using var rotationKey = characteristics.GetStatic<AndroidJavaObject>("LENS_POSE_ROTATION");
                float[] t = characteristics.Call<float[]>("get", translationKey);
                float[] q = characteristics.Call<float[]>("get", rotationKey);
                if (t == null || t.Length < 3 || q == null || q.Length < 4) continue;
                var rotation = new Quaternion(-q[0], -q[1], q[2], q[3]);
                float norm = rotation.x*rotation.x + rotation.y*rotation.y + rotation.z*rotation.z + rotation.w*rotation.w;
                if (!HandCropGeometry.Finite(norm) || norm < .5f || norm > 1.5f) continue;
                var position = new Vector3(t[0], t[1], -t[2]);
                if (!HandCropGeometry.Finite(position.sqrMagnitude) || position.magnitude > .3f) continue;
                OpticalFromHead.Add(new Pose(position,
                    Quaternion.Inverse(rotation.normalized) * Quaternion.Euler(180, 0, 0)));
            }
            if (OpticalFromHead.Count > 0)
                Debug.Log($"[HandROI] Loaded {OpticalFromHead.Count} RGB lens poses from Camera2 metadata.");
        }
        catch (Exception error)
        {
            OpticalFromHead.Clear();
            Debug.LogWarning("[HandROI] RGB calibration unavailable; using full frame. " + error.Message);
        }
#endif
        return OpticalFromHead.Count > 0;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    static int ReadVendorNumber(AndroidJavaObject value)
    {
        using var type = value.Call<AndroidJavaObject>("getClass");
        string name = type.Call<string>("getName");
        using var array = new AndroidJavaClass("java.lang.reflect.Array");
        if (name == "[B" || name == "[I")
        {
            if (array.CallStatic<int>("getLength", value) < 1) return -1;
            return name == "[B" ? array.CallStatic<sbyte>("getByte", value, 0) : array.CallStatic<int>("getInt", value, 0);
        }
        return name == "java.lang.Integer" || name == "java.lang.Byte" || name == "java.lang.Long"
            ? value.Call<int>("intValue") : -1;
    }
#endif
}
