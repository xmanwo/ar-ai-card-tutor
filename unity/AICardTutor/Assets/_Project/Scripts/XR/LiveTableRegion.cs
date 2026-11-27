using UnityEngine;

/// <summary>Rotation-compensated RGB table window. Translation remains bounded because no table depth is measured.</summary>
public sealed class LiveTableRegion
{
    public HandCropGeometry.Crop Region { get; private set; }
    public bool IsSet { get; private set; }
    public string Issue { get; private set; }="Table not located. Stop Live, scan Discard and Confirm.";
    public float TranslationMeters { get; private set; }
    public float RotationDegrees { get; private set; }
    int width,height;
    Pose head;
    HandCropGeometry.Crop referenceRegion;
    RgbViewProjection referenceProjection;
    public bool Set(ScanPreviewFrame frame,float[] box)
    {
        IsSet=false;
        if(frame==null || frame.Target!=ScanTarget.Discard)
        {Issue="No table photo. Scan Discard and Confirm.";return false;}
        if(!frame.HeadTracked){Issue="Table photo had no head tracking. Wear headset and rescan Discard.";return false;}
        if(!frame.RgbProjection.Valid){Issue="RGB calibration unavailable. Wait for passthrough, then rescan Discard.";return false;}
        if(box==null || box.Length<4 || !new LiveCardTracker.Box(box[0],box[1],box[2],box[3]).Valid)
        {Issue="Table card location uncertain. Scan Discard alone and Confirm.";return false;}
        float padX=Mathf.Max(128,(box[2]-box[0])*.6f),padY=Mathf.Max(128,(box[3]-box[1])*.6f);
        if(!HandCropGeometry.TryCrop(box[0]-padX,box[1]-padY,box[2]+padX,box[3]+padY,
            frame.Width,frame.Height,out var region) || (float)region.Width*region.Height/(frame.Width*frame.Height)>.35f)
        {Issue="Table region too large or clipped. Rescan with the pile fully visible.";return false;}
        Region=referenceRegion=region;referenceProjection=frame.RgbProjection;
        width=frame.Width;height=frame.Height;head=frame.HeadPose;IsSet=true;Issue="";return true;
    }
    public bool TryGet(FrameCapture capture,out HandCropGeometry.Crop region)
    {
        region=Region;
        if(!IsSet)return false;
        if(capture==null || capture.LastTexture==null){Issue="Waiting for a camera image.";return false;}
        if(!HandRoiProvider.TryGetTrackedHead(out var current)){Issue="Head tracking unavailable.";return false;}
        TranslationMeters=Vector3.Distance(current.position,head.position);RotationDegrees=Quaternion.Angle(current.rotation,head.rotation);
        if(!Project(current,capture.LastRgbProjection,capture.LastTexture.width,capture.LastTexture.height,out region,out var issue))
        {Issue=issue;return false;}
        Issue="";Region=region;return true;
    }
    public bool CapturedPoseMatches(FrameCapture capture)=>IsSet && capture!=null && capture.LastHeadTracked &&
        capture.LastTexture!=null && Project(capture.LastHeadPose,capture.LastRgbProjection,capture.LastTexture.width,capture.LastTexture.height,out _,out _);
    public bool TryGetForFrame(ScanPreviewFrame frame,out HandCropGeometry.Crop region)
    {
        region=default;
        return IsSet && frame!=null && frame.HeadTracked &&
            Project(frame.HeadPose,frame.RgbProjection,frame.Width,frame.Height,out region,out _);
    }
    public bool MatchesFrame(ScanPreviewFrame frame)=>TryGetForFrame(frame,out _);
    bool Project(Pose current,RgbViewProjection projection,int imageWidth,int imageHeight,out HandCropGeometry.Crop region,out string issue)
    {
        region=default;issue="";
        if(imageWidth!=width || imageHeight!=height){issue="Camera format changed. Rescan Discard.";return false;}
        if(Vector3.Distance(current.position,head.position)>.05f){issue="Head position moved. Stop Live and rescan Discard.";return false;}
        if(!TableViewProjection.Reproject(referenceRegion,head,current,referenceProjection,projection,out region))
        {issue="Table outside camera view or RGB calibration unavailable.";return false;}
        return true;
    }
    public static bool Disjoint(HandCropGeometry.Crop a,HandCropGeometry.Crop b)=>
        a.X+a.Width<=b.X || b.X+b.Width<=a.X || a.Y+a.Height<=b.Y || b.Y+b.Height<=a.Y;
}
