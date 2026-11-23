using System;

public enum HoldingHandMode { Auto, Left, Right, Off }

/// <summary>Pixel coordinates always use the upright JPEG's top-left origin.</summary>
public static class HandCropGeometry
{
    public struct Crop
    {
        public int X, Y, Width, Height;
        public Crop(int x, int y, int width, int height)
        { X = x; Y = y; Width = width; Height = height; }
        public bool Contains(float x, float y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    }

    public static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);

    /// <summary>Relative physical pose from two samples in the SAME XR tracking space.</summary>
    public static bool HeadRelative(double px, double py, double pz, double hx, double hy, double hz,
        double qx, double qy, double qz, double qw, out double x, out double y, out double z)
    {
        x = y = z = 0;
        if (!Finite(px) || !Finite(py) || !Finite(pz) || !Finite(hx) || !Finite(hy) || !Finite(hz)) return false;
        double norm = qx*qx + qy*qy + qz*qz + qw*qw;
        if (!Finite(norm) || norm < .5 || norm > 1.5) return false;
        double scale = 1 / Math.Sqrt(norm);
        // Rotate palm-head by the inverse head quaternion. No scene/camera-offset transforms.
        qx *= -scale; qy *= -scale; qz *= -scale; qw *= scale;
        px -= hx; py -= hy; pz -= hz;
        double tx = 2*(qy*pz-qz*py), ty = 2*(qz*px-qx*pz), tz = 2*(qx*py-qy*px);
        x = px + qw*tx + qy*tz-qz*ty;
        y = py + qw*ty + qz*tx-qx*tz;
        z = pz + qw*tz + qx*ty-qy*tx;
        return Finite(x) && Finite(y) && Finite(z);
    }

    // x right, y down, z forward in the RGB optical frame, not the rendered eye camera.
    public static bool Project(double x, double y, double z, double fx, double fy,
        double cx, double cy, int calibrationWidth, int calibrationHeight,
        int imageWidth, int imageHeight, out double u, out double v)
    {
        u = v = 0;
        if (!Finite(x) || !Finite(y) || !Finite(z) || z < .08 ||
            !Finite(fx) || !Finite(fy) || fx <= 0 || fy <= 0 || !Finite(cx) || !Finite(cy) ||
            calibrationWidth <= 0 || calibrationHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return false;
        u = (fx * x / z + cx) * imageWidth / calibrationWidth;
        v = (fy * y / z + cy) * imageHeight / calibrationHeight;
        return Finite(u) && Finite(v);
    }

    public static bool TryCrop(double left, double top, double right, double bottom,
        int width, int height, out Crop crop)
    {
        crop = new Crop(0, 0, width, height);
        if (width < 128 || height < 128 || !Finite(left) || !Finite(top) ||
            !Finite(right) || !Finite(bottom) || right <= left || bottom <= top) return false;
        double l = Math.Max(0, left), t = Math.Max(0, top);
        double r = Math.Min(width, right), b = Math.Min(height, bottom);
        if (r - l < 128 || b - t < 128 ||
            (r-l)*(b-t) < (right-left)*(bottom-top)*.40) return false;
        // Quantise outwards to reduce crop texture reallocations without cutting off corners.
        int x = Math.Max(0, (int)Math.Floor(l / 32) * 32);
        int y = Math.Max(0, (int)Math.Floor(t / 32) * 32);
        int x2 = Math.Min(width, (int)Math.Ceiling(r / 32) * 32);
        int y2 = Math.Min(height, (int)Math.Ceiling(b / 32) * 32);
        if ((double)(x2-x)*(y2-y) >= (double)width*height*.90) return false;
        crop = new Crop(x, y, x2-x, y2-y);
        return true;
    }

    public static float[] RestoreBox(float[] box, int responseWidth, int responseHeight,
        Crop crop, int fullWidth, int fullHeight)
    {
        if (box == null || box.Length < 4 || responseWidth <= 0 || responseHeight <= 0 ||
            crop.Width <= 0 || crop.Height <= 0 || fullWidth <= 0 || fullHeight <= 0) return null;
        for (int i = 0; i < 4; i++) if (!Finite(box[i])) return null;
        float x1 = Clamp(crop.X + box[0] * crop.Width / responseWidth, 0, fullWidth);
        float y1 = Clamp(crop.Y + box[1] * crop.Height / responseHeight, 0, fullHeight);
        float x2 = Clamp(crop.X + box[2] * crop.Width / responseWidth, 0, fullWidth);
        float y2 = Clamp(crop.Y + box[3] * crop.Height / responseHeight, 0, fullHeight);
        return x2 > x1 && y2 > y1 ? new[] { x1, y1, x2, y2 } : null;
    }

    public static int TextureRowOffset(Crop crop, int fullWidth, int fullHeight, int row)
    {
        if (crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 ||
            crop.X + crop.Width > fullWidth || crop.Y + crop.Height > fullHeight ||
            row < 0 || row >= crop.Height) return -1;
        return (fullHeight - crop.Y - crop.Height + row) * fullWidth + crop.X;
    }

    static float Clamp(float n, float min, float max) => Math.Max(min, Math.Min(max, n));
}

/// <summary>Locks to the hand with card evidence, rather than following the UI-pointing hand.</summary>
public sealed class HoldingHandSelector
{
    public int Selected { get; private set; } = -1;
    bool searchFullFrame = true;
    int probe, misses, tentative = -1, tentativeHits;

    public void Reset() { Selected = -1; searchFullFrame = true; probe = misses = tentativeHits = 0; tentative = -1; }

    public int Choose(bool left, bool right)
    {
        if (Selected >= 0) return (Selected == 0 ? left : right) ? Selected : -1;
        if (searchFullFrame) { searchFullFrame = false; return -1; }
        if (tentative >= 0 && (tentative == 0 ? left : right)) return tentative;
        int candidate = probe++ % 2;
        if (candidate == 0 && left || candidate == 1 && right) return candidate;
        return left ? 0 : right ? 1 : -1;
    }

    public void Observe(int usedHand, int leftCards, int rightCards)
    {
        if (Selected >= 0)
        {
            int visible = Selected == 0 ? leftCards : rightCards;
            misses = visible > 0 ? 0 : misses + 1;
            if (misses >= 3) Reset();
            return;
        }
        int candidate = usedHand >= 0 && (usedHand == 0 ? leftCards : rightCards) > 0
            ? usedHand : usedHand < 0 && Math.Max(leftCards, rightCards) > 0
                ? leftCards >= rightCards ? 0 : 1 : -1;
        if (candidate >= 0)
        {
            tentativeHits = tentative == candidate ? tentativeHits + 1 : 1;
            tentative = candidate;
            int count = candidate == 0 ? leftCards : rightCards;
            if (count >= 2 || tentativeHits >= 2) Selected = candidate;
        }
        else { tentative = -1; tentativeHits = 0; }
        misses = 0;
    }
}
