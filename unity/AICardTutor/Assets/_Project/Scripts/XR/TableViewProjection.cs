using UnityEngine;

/// <summary>RGB intrinsics in upright image pixels; optical rays use the same convention as HandRoiProvider.</summary>
public struct RgbViewProjection
{
    public float Fx,Fy,Cx,Cy;
    public int Width,Height;
    public Quaternion OpticalFromHead;
    float LensNorm=>OpticalFromHead.x*OpticalFromHead.x+OpticalFromHead.y*OpticalFromHead.y+
        OpticalFromHead.z*OpticalFromHead.z+OpticalFromHead.w*OpticalFromHead.w;
    public bool Valid=>Width>0 && Height>0 && Fx>0 && Fy>0 && HandCropGeometry.Finite(Fx) &&
        HandCropGeometry.Finite(Fy) && HandCropGeometry.Finite(Cx) && HandCropGeometry.Finite(Cy) &&
        HandCropGeometry.Finite(OpticalFromHead.x) && HandCropGeometry.Finite(OpticalFromHead.y) &&
        HandCropGeometry.Finite(OpticalFromHead.z) && HandCropGeometry.Finite(OpticalFromHead.w) &&
        HandCropGeometry.Finite(LensNorm) && LensNorm>.5f && LensNorm<1.5f;
}

public static class TableViewProjection
{
    /// <summary>Rotation compensation only. The caller bounds translation; this does not estimate table depth.</summary>
    public static bool Reproject(HandCropGeometry.Crop original,Pose reference,Pose current,
        RgbViewProjection source,RgbViewProjection target,out HandCropGeometry.Crop projected)
    {
        projected=default;
        if(!source.Valid || !target.Valid)return false;
        if(Quaternion.Angle(reference.rotation,current.rotation)<.001f && Quaternion.Angle(source.OpticalFromHead,target.OpticalFromHead)<.001f &&
            source.Width==target.Width && source.Height==target.Height && Mathf.Abs(source.Fx-target.Fx)<.001f &&
            Mathf.Abs(source.Fy-target.Fy)<.001f && Mathf.Abs(source.Cx-target.Cx)<.001f && Mathf.Abs(source.Cy-target.Cy)<.001f)
        {projected=original;return original.Width>0 && original.Height>0;}
        var rotation=Quaternion.Inverse(current.rotation*target.OpticalFromHead)*(reference.rotation*source.OpticalFromHead);
        float left=float.PositiveInfinity,top=float.PositiveInfinity,right=float.NegativeInfinity,bottom=float.NegativeInfinity;
        foreach(float x in new[]{(float)original.X,original.X+original.Width})
            foreach(float y in new[]{(float)original.Y,original.Y+original.Height})
            {
                var ray=rotation*new Vector3((x-source.Cx)/source.Fx,(y-source.Cy)/source.Fy,1);
                if(!HandCropGeometry.Finite(ray.x) || !HandCropGeometry.Finite(ray.y) || !HandCropGeometry.Finite(ray.z) || ray.z<.15f)return false;
                float u=target.Fx*ray.x/ray.z+target.Cx,v=target.Fy*ray.y/ray.z+target.Cy;
                left=Mathf.Min(left,u);right=Mathf.Max(right,u);top=Mathf.Min(top,v);bottom=Mathf.Max(bottom,v);
            }
        return HandCropGeometry.TryCrop(left,top,right,bottom,target.Width,target.Height,out projected);
    }
}
