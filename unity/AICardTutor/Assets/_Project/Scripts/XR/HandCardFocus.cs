using System;
using System.Collections.Generic;

/// <summary>Fits a pixel crop to card-corner evidence near a tracked palm, not a fixed metre box.</summary>
public static class HandCardFocus
{
    public struct Box
    {
        public double Left, Top, Right, Bottom;
        public Box(double left, double top, double right, double bottom)
        { Left = left; Top = top; Right = right; Bottom = bottom; }
        public double X => (Left + Right) * .5;
        public double Y => (Top + Bottom) * .5;
        public double Size => Math.Max(Right - Left, Bottom - Top);
        public bool Valid => HandCropGeometry.Finite(Left) && HandCropGeometry.Finite(Top) &&
            HandCropGeometry.Finite(Right) && HandCropGeometry.Finite(Bottom) && Right > Left && Bottom > Top;
    }

    public static bool TryFit(IReadOnlyList<Box> observations, double palmX, double palmY,
        double handSpan, int width, int height, out HandCropGeometry.Crop crop, out int members)
        => TryFit(observations, palmX, palmY, handSpan, width, height, out crop, out members, out _);

    public static bool TryFit(IReadOnlyList<Box> observations, double palmX, double palmY,
        double handSpan, int width, int height, out HandCropGeometry.Crop crop, out int members, out bool fillsFrame)
    {
        crop = default; members = 0; fillsFrame = false;
        if (!HandCropGeometry.Finite(palmX) || !HandCropGeometry.Finite(palmY) || width < 128 || height < 128 || observations == null) return false;
        if (!HandCropGeometry.Finite(handSpan) || handSpan < 1) handSpan = width*.03;
        var boxes = new List<Box>();
        foreach (var box in observations)
            if (box.Valid && box.Size >= 4 && box.Left < width && box.Top < height && box.Right > 0 && box.Bottom > 0)
            { boxes.Add(box); if (boxes.Count == 128) break; }
        if (boxes.Count == 0) return false;
        double diagonal = Math.Sqrt((double)width*width + (double)height*height);
        var seen = new bool[boxes.Count];
        double bestScore = double.PositiveInfinity;
        double rejectedScore = double.PositiveInfinity; int rejectedMembers = 0;
        for (int seed = 0; seed < boxes.Count; seed++)
        {
            if (seen[seed]) continue;
            var component = new List<int> { seed }; seen[seed] = true;
            for (int index = 0; index < component.Count; index++)
                for (int other = 0; other < boxes.Count; other++)
                {
                    if (seen[other]) continue;
                    Box a = boxes[component[index]], b = boxes[other];
                    double limit = Math.Min(diagonal*.18, Math.Max(a.Size, b.Size)*3.5);
                    double dx = a.X-b.X, dy = a.Y-b.Y;
                    if (dx*dx + dy*dy <= limit*limit) { seen[other] = true; component.Add(other); }
                }
            double left = double.PositiveInfinity, top = left, right = double.NegativeInfinity, bottom = right;
            var sizes = new List<double>();
            foreach (int index in component)
            {
                Box box = boxes[index];
                left = Math.Min(left, box.Left); top = Math.Min(top, box.Top);
                right = Math.Max(right, box.Right); bottom = Math.Max(bottom, box.Bottom);
                sizes.Add(box.Size);
            }
            sizes.Sort(); double size = sizes[sizes.Count/2];
            double dxPalm = Math.Max(left-palmX, Math.Max(0, palmX-right));
            double dyPalm = Math.Max(top-palmY, Math.Max(0, palmY-bottom));
            double distance = Math.Sqrt(dxPalm*dxPalm + dyPalm*dyPalm);
            double reach = Math.Min(diagonal*.34, Math.Max(diagonal*.12, Math.Max(size*6, handSpan*5)));
            if (component.Count == 1) reach = Math.Min(reach, Math.Max(size*4, handSpan*3));
            if (distance > reach) continue;
            double score = distance / Math.Max(1, reach) - .08*Math.Min(10, component.Count);
            if (score >= bestScore) continue;
            // Two corner widths reveal adjacent missed end cards. The fan's front card and a
            // lower left corner may extend well below the detected top-corner row. Include the grip
            // only when nearby: an imperfect palm projection must not drag the box across the scene.
            double padding = Math.Max(size*2, (right-left)*.22);
            double l = left-padding, r = right+padding, t = top-size*.85, btm = bottom+size*4;
            if (distance <= size*4)
            {
                l = Math.Min(l, palmX-size*.5); r = Math.Max(r, palmX+size*.5);
                btm = Math.Max(btm, palmY+size);
            }
            if (!Useful(l, t, r, btm, width, height, out var fitted) &&
                !Useful(left-size*.6, top-size*.5, right+size*.6, bottom+size*1.1, width, height, out fitted))
            {
                if (score < rejectedScore) { rejectedScore = score; rejectedMembers = component.Count; }
                continue;
            }
            crop = fitted; members = component.Count; bestScore = score;
        }
        if (rejectedScore < bestScore)
        {
            crop = default; members = rejectedMembers; fillsFrame = true;
            return false;
        }
        return members > 0;
    }

    public static bool Discovery(double palmX, double palmY, double handSpan, int width, int height,
        out HandCropGeometry.Crop crop)
    {
        double w = Math.Min(width*.56, Math.Max(width*.22, handSpan*4.5));
        double h = Math.Min(height*.52, Math.Max(height*.22, handSpan*4.5));
        // Only an acquisition window. Actual detected corners replace its size on the next reply.
        return Useful(palmX-w*.5, palmY-h*.78, palmX+w*.5, palmY+h*.22, width, height, out crop);
    }

    public static bool Follow(HandCropGeometry.Crop reference, double oldX, double oldY, double oldScale,
        double newX, double newY, double newScale, int width, int height, out HandCropGeometry.Crop crop)
    {
        crop = default;
        if (oldScale <= 0 || newScale <= 0 || !HandCropGeometry.Finite(oldScale) || !HandCropGeometry.Finite(newScale)) return false;
        double ratio = newScale/oldScale;
        if (ratio < .55 || ratio > 1.8 || Math.Abs(newX-oldX) > width*.28 || Math.Abs(newY-oldY) > height*.28) return false;
        return Useful(newX+(reference.X-oldX)*ratio, newY+(reference.Y-oldY)*ratio,
            newX+(reference.X+reference.Width-oldX)*ratio, newY+(reference.Y+reference.Height-oldY)*ratio,
            width, height, out crop);
    }

    static bool Useful(double left, double top, double right, double bottom, int width, int height,
        out HandCropGeometry.Crop crop)
    {
        // Preserve a readable input even for one visible corner; never claim near-full-frame zoom.
        double cx = (left+right)*.5, cy = (top+bottom)*.5;
        double w = Math.Max(192, right-left), h = Math.Max(192, bottom-top);
        bool valid = HandCropGeometry.TryCrop(cx-w*.5, cy-h*.5, cx+w*.5, cy+h*.5, width, height, out crop);
        return valid && (double)crop.Width*crop.Height <= (double)width*height*.55;
    }
}
