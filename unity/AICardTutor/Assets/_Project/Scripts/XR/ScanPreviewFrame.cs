using System;
using Unity.Collections;
using UnityEngine;

/// <summary>A bounded reusable photo pair, separate from the camera's mutable capture buffers.</summary>
public sealed class ScanPreviewFrame : IDisposable
{
    public Texture2D Original { get; private set; }
    Texture2D focused;
    public Texture2D Recognition => Cropped ? focused : Original;
    public int FrameId { get; private set; }
    public float CapturedAt { get; private set; }
    public ScanTarget Target { get; private set; }
    public HandCropGeometry.Crop Crop { get; private set; }
    public bool Cropped { get; private set; }
    public HandCropGeometry.Crop UploadCrop { get; private set; }
    public bool UploadCropped { get; private set; }
    public string Note { get; private set; }
    public Pose HeadPose { get; private set; }
    public bool HeadTracked { get; private set; }
    public RgbViewProjection RgbProjection { get; private set; }
    public int Width => Original != null ? Original.width : 0;
    public int Height => Original != null ? Original.height : 0;

    public void Capture(FrameCapture source, ScanTarget target, float capturedAt,HandCropGeometry.Crop? presentation=null)
    {
        FrameId=source.LastFrameId; CapturedAt=capturedAt; Target=target;
        Crop=source.LastScanCrop; Cropped=source.LastScanCropped; Note=source.LastCropNote;
        UploadCrop=Crop;UploadCropped=Cropped;
        HeadPose=source.LastHeadPose;HeadTracked=source.LastHeadTracked;
        RgbProjection=source.LastRgbProjection;
        Original=Copy(source.LastTexture,Original);
        if(presentation.HasValue && Original!=null)
        {
            var view=presentation.Value;
            if(view.X>=0 && view.Y>=0 && view.Width>0 && view.Height>0 && view.X+view.Width<=Width && view.Y+view.Height<=Height)
            {Crop=view;Cropped=true;focused=CopyCrop(Original,focused,view);Note="Live · Following hand · Stable view";return;}
        }
        if (Cropped) focused=Copy(source.LastRecognitionTexture,focused);
    }

    public void CopyFrom(ScanPreviewFrame source)
    {
        FrameId=source.FrameId; CapturedAt=source.CapturedAt; Target=source.Target;
        Crop=source.Crop; Cropped=source.Cropped; Note=source.Note;
        UploadCrop=source.UploadCrop;UploadCropped=source.UploadCropped;
        HeadPose=source.HeadPose;HeadTracked=source.HeadTracked;
        RgbProjection=source.RgbProjection;
        Original=Copy(source.Original,Original);
        if (Cropped) focused=Copy(source.Recognition,focused);
    }

    static Texture2D Copy(Texture2D source,Texture2D destination)
    {
        if (source==null) return destination;
        if (destination==null || destination.width!=source.width || destination.height!=source.height)
        {
            Release(destination);
            destination=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);
        }
        NativeArray<byte>.Copy(source.GetRawTextureData<byte>(),destination.GetRawTextureData<byte>());
        destination.Apply(false,false);
        return destination;
    }
    static Texture2D CopyCrop(Texture2D source,Texture2D destination,HandCropGeometry.Crop crop)
    {
        if(destination==null || destination.width!=crop.Width || destination.height!=crop.Height)
        {Release(destination);destination=new Texture2D(crop.Width,crop.Height,TextureFormat.RGBA32,false);}
        var pixels=source.GetRawTextureData<Color32>();var output=destination.GetRawTextureData<Color32>();
        for(int row=0;row<crop.Height;row++)
            NativeArray<Color32>.Copy(pixels,HandCropGeometry.TextureRowOffset(crop,source.width,source.height,row),output,row*crop.Width,crop.Width);
        destination.Apply(false,false);return destination;
    }

    public void Dispose()
    {
        Release(Original); Release(focused);
        Original=null; focused=null;
    }

    static void Release(Texture2D texture)
    {
        if (texture==null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
        else UnityEngine.Object.DestroyImmediate(texture);
    }
}
