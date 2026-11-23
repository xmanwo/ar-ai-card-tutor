using System;
using System.Collections.Generic;

public static class HandCropChecks
{
    static int checks;
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; }
    static void Near(double a, double b, string message) => Check(Math.Abs(a-b) < .001, message);

    public static int Main()
    {
        try { return Run(); }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }

    static int Run()
    {
        double u, v;
        double hx, hy, hz;
        Check(HandCropGeometry.HeadRelative(1.1, 1.2, -1.4, 1, 1.5, -2, 0, 0, 0, 1, out hx, out hy, out hz), "Tracked head/palm difference failed.");
        Near(hx, .1, "Physical hand x must not include rig translations.");
        Near(hy, -.3, "Hand height must use the same origin as tracked head height.");
        Near(hz, .6, "A 60cm hand must not become a 12cm hand due to scene camera offsets.");
        Check(HandCropGeometry.HeadRelative(11.1, 5.2, 8.6, 11, 5.5, 8, 0, 0, 0, 1, out hx, out hy, out hz), "Recentered tracked pose failed.");
        Near(hx, .1, "Recentring must not shift crop x."); Near(hy, -.3, "Recentring must not shift crop y."); Near(hz, .6, "Recentring must not resize the fan.");
        double half = Math.Sqrt(.5);
        Check(HandCropGeometry.HeadRelative(.6, 1.2, 0, 0, 1.5, 0, 0, half, 0, half, out hx, out hy, out hz), "Rotated head conversion failed.");
        Near(hx, 0, "Yaw must rotate the palm into optical horizontal coordinates."); Near(hz, .6, "Yaw must preserve physical forward hand distance.");
        Check(!HandCropGeometry.HeadRelative(0, 0, 1, 0, 0, 0, 0, 0, 0, 0, out hx, out hy, out hz), "Missing head rotation must cause full-frame fallback.");
        Check(HandCropGeometry.Project(.1, -.2, 1, 800, 800, 960, 540, 1920, 1080, 960, 540, out u, out v), "Valid optical projection failed.");
        Near(u, 520, "Intrinsics must scale to delivered image width.");
        Near(v, 190, "Projection must preserve top-left camera coordinates.");
        Check(!HandCropGeometry.Project(0, 0, -1, 800, 800, 960, 540, 1920, 1080, 960, 540, out u, out v), "Behind-camera points must not create a crop.");
        Check(!HandCropGeometry.Project(double.NaN, 0, 1, 800, 800, 960, 540, 1920, 1080, 960, 540, out u, out v), "Invalid hand coordinates must fall back.");
        Check(!HandCropGeometry.Project(0, 0, 1, 0, 800, 960, 540, 1920, 1080, 960, 540, out u, out v), "Missing intrinsics must not guess a camera FOV.");
        Check(HandCropGeometry.TryCrop(101, 203, 701, 803, 1728, 1728, out var crop), "Useful crop rejected.");
        Check(crop.X <= 101 && crop.Y <= 203 && crop.X + crop.Width >= 701 && crop.Y + crop.Height >= 803, "Rounding must include all fan edges.");
        Check(crop.X % 32 == 0 && crop.Width % 32 == 0, "Texture reuse alignment changed.");
        Check(HandCropGeometry.TryCrop(-20, 100, 600, 700, 1728, 1728, out crop) && crop.X == 0, "Visible edge cards must remain usable.");
        Check(!HandCropGeometry.TryCrop(-1800, 100, 200, 700, 1728, 1728, out crop), "A mostly offscreen hand needs full-frame recovery.");
        Check(!HandCropGeometry.TryCrop(0, 0, 1728, 1728, 1728, 1728, out crop), "A full-size crop should use the original JPEG.");
        Check(!HandCropGeometry.TryCrop(0, 0, 80, 80, 1728, 1728, out crop), "Tiny crop must not erase the fan.");
        Check(!HandCropGeometry.TryCrop(double.NaN, 10, 500, 600, 1728, 1728, out crop), "Invalid bounds accepted.");
        crop = new HandCropGeometry.Crop(320, 192, 640, 960);
        var box = HandCropGeometry.RestoreBox(new float[] { 0, 0, 320, 480 }, 320, 480, crop, 1728, 1728);
        Near(box[0], 320, "Crop x origin missing."); Near(box[1], 192, "Crop y origin missing.");
        Near(box[2], 960, "Resized response x must restore to native pixels."); Near(box[3], 1152, "Resized response y must restore to native pixels.");
        box = HandCropGeometry.RestoreBox(new float[] { 30, 60, 90, 120 }, 640, 960, crop, 1728, 1728);
        Near(box[0], 350, "Detection jitter must be measured in original coordinates.");
        Near(box[1], 252, "Do not flip JPEG detection coordinates twice.");
        Check(HandCropGeometry.RestoreBox(new float[] { 0, 0, 5, 5 }, 0, 480, crop, 1728, 1728) == null, "Invalid response dimensions accepted.");
        Check(HandCropGeometry.RestoreBox(new float[] { 0, 0, float.NaN, 5 }, 640, 960, crop, 1728, 1728) == null, "NaN boxes must not enter tracking.");
        Check(HandCropGeometry.RestoreBox(new float[] { 4, 4, 2, 2 }, 640, 960, crop, 1728, 1728) == null, "Negative-size boxes accepted.");
        var small = new HandCropGeometry.Crop(2, 1, 3, 2);
        Check(HandCropGeometry.TextureRowOffset(small, 8, 6, 0) == 26, "Top-left crop must address the correct bottom-up texture row.");
        Check(HandCropGeometry.TextureRowOffset(small, 8, 6, 1) == 34, "Crop row order must match the source texture.");
        Check(HandCropGeometry.TextureRowOffset(small, 8, 6, 2) == -1, "Crop must not read beyond its final row.");
        Check(HandCropGeometry.TextureRowOffset(new HandCropGeometry.Crop(7, 1, 3, 2), 8, 6, 0) == -1, "Out-of-bounds native buffer copy accepted.");
        var selector = new HoldingHandSelector();
        Check(selector.Choose(true, true) == -1, "Auto mode must acquire full-frame card evidence first.");
        selector.Observe(-1, 1, 7);
        Check(selector.Selected == 1 && selector.Choose(true, true) == 1, "Auto mode must select the hand with the cards.");
        selector.Observe(1, 8, 7);
        Check(selector.Selected == 1, "The pointer hand must not steal an established crop.");
        Check(selector.Choose(true, false) == -1, "Brief holding-hand loss must fall back, not switch hands.");
        selector.Observe(-1, 7, 0); selector.Observe(-1, 7, 0);
        Check(selector.Selected == 1, "Two misses should tolerate transient occlusion.");
        selector.Observe(-1, 7, 0);
        Check(selector.Selected == -1 && selector.Choose(true, true) == -1, "Sustained loss must reacquire rather than remain locked forever.");
        selector.Observe(-1, 6, 0);
        Check(selector.Selected == 0, "Cards transferred to the other hand must be selectable.");
        selector.Reset(); selector.Choose(true, true); selector.Observe(-1, 0, 0);
        Check(selector.Choose(true, true) == 0 && selector.Choose(true, true) == 1, "Tiny full-frame cards need alternating hand-crop discovery.");
        selector.Observe(1, 0, 4);
        Check(selector.Selected == 1, "Successful crop discovery must lock the holding hand.");
        selector.Reset(); selector.Choose(true, true); selector.Observe(-1, 1, 0);
        Check(selector.Selected == -1 && selector.Choose(true, true) == 0, "The last UNO card needs a verification crop, not alternating hands forever.");
        selector.Observe(0, 1, 0);
        Check(selector.Selected == 0, "A single remaining card must lock after repeated evidence.");
        selector.Reset(); selector.Choose(true, true); selector.Observe(-1, 1, 0);
        selector.Choose(true, true); selector.Observe(0, 0, 0);
        Check(selector.Selected == -1, "An isolated single-card false alarm must not lock a hand.");
        // Actual detections from the user's native Quest frame: four fan corners plus a
        // misleading printed UNO-box symbol at the bottom left. The palm is a replay input.
        var observed = new List<HandCardFocus.Box> {
            new HandCardFocus.Box(1149,762,1218,836), new HandCardFocus.Box(1038,659,1101,730),
            new HandCardFocus.Box(947,671,1010,743), new HandCardFocus.Box(834,705,900,788),
            new HandCardFocus.Box(83,1364,157,1432)
        };
        Check(HandCardFocus.TryFit(observed, 1040, 1060, 90, 1728, 1728, out crop, out int members), "Card evidence near the holding hand must produce a crop.");
        Check(members == 4, "Printed UNO-box graphics must not join the hand's card cluster.");
        Check(crop.Contains(850,740) && crop.Contains(1180,800), "Both fan end corners must remain in the crop.");
        Check(crop.Contains(810,1080), "Padding must reveal an adjacent missed lower/end card.");
        Check(!crop.Contains(120,1400), "Distant desk packaging must remain outside the recognition input.");
        Check((double)crop.Width*crop.Height < 1728*1728*.25, "This native frame needs a materially smaller ROI, not a nearly full-frame green box.");
        Console.WriteLine($"Native-frame focus replay: {crop.X},{crop.Y},{crop.Width},{crop.Height}; {members} evidence corners.");
        var reference = crop;
        Check(HandCardFocus.TryFit(observed, 500, 700, 90, 1728, 1728, out crop, out members), "Card evidence must tolerate an imperfect palm projection.");
        Check(crop.Contains(710,1100) && crop.Contains(1180,800), "Do not clip a missed end card when the palm estimate is offset.");
        Check(!crop.Contains(120,1400), "Tolerating palm offset must not pull in the remote packaging.");
        Check(HandCardFocus.Follow(reference, 1040, 1060, 2, 1120, 1040, 2, 1728, 1728, out var moved), "The fitted crop must follow a moving holding hand.");
        Check(moved.Contains(1180+80,800-20), "Hand translation must preserve the selected card group.");
        Check(HandCardFocus.Follow(reference, 1040, 1060, 2, 1040, 1060, 3, 1728, 1728, out var closer), "Moving the hand closer must not disable cropping.");
        Check(closer.Width > reference.Width && closer.Height > reference.Height, "Closer cards require a larger pixel crop, within the frame budget.");
        Check(!HandCardFocus.Follow(reference, 1040, 1060, 2, 1650, 1040, 2, 1728, 1728, out moved), "A tracking jump must reacquire the cards instead of moving a stale crop.");
        Check(HandCardFocus.Discovery(1000, 1100, 1000, 1728, 1728, out var near), "A nearby hand needs bounded acquisition, not a rejected metre-sized envelope.");
        Check((double)near.Width*near.Height <= 1728*1728*.55, "Near-hand acquisition must never label an almost-full frame as focused.");
        Check(HandCardFocus.Discovery(1000, 1100, 20, 1728, 1728, out var far), "Small distant hands still need a discovery window.");
        Check(near.Width > far.Width, "Discovery should adapt to hand size.");
        var repeats = new List<HandCardFocus.Box> {
            new HandCardFocus.Box(600,650,660,720), new HandCardFocus.Box(700,650,760,720)
        };
        Check(HandCardFocus.TryFit(repeats, 700, 850, 70, 1728, 1728, out crop, out members) && members == 2,
            "Repeated card labels occupy independent boxes and must not disappear from the focus evidence.");
        Check(HandCardFocus.TryFit(new[]{new HandCardFocus.Box(600,650,660,720)}, 670, 800, 60,
            1728, 1728, out crop, out members) && members == 1, "The last UNO card must still be focusable.");
        Check(crop.Width >= 192 && crop.Height >= 192, "Do not send a tiny corner-only fragment as the whole recognition region.");
        Check(!HandCardFocus.TryFit(observed, 20, 20, 40, 1728, 1728, out crop, out members), "A pointer hand far from the cards must not steal the ROI.");
        Check(!HandCardFocus.TryFit(new HandCardFocus.Box[0], 1000, 1100, 70, 1728, 1728, out crop, out members), "No card observations must use discovery/recovery.");
        Check(!HandCardFocus.TryFit(observed, double.NaN, 1100, 70, 1728, 1728, out crop, out members), "Invalid hand projection must fall back safely.");
        var giantFan = new List<HandCardFocus.Box>();
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 6; col++)
                giantFan.Add(new HandCardFocus.Box(40+col*290,40+row*400,160+col*290,180+row*400));
        Check(!HandCardFocus.TryFit(giantFan, 900, 1000, 200, 1728, 1728, out crop, out members, out bool fillsFrame) && fillsFrame,
            "If real observed cards fill the frame, use full frame instead of a discovery crop that loses cards.");
        Console.WriteLine("Hand crop checks passed: " + checks);
        return 0;
    }
}
