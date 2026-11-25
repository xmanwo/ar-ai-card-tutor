using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.ARFoundation;

/// <summary>Tracked palms guide selection; observed card corners determine the crop size.</summary>
[DisallowMultipleComponent]
public class HandRoiProvider : MonoBehaviour
{
    public HoldingHandMode mode = HoldingHandMode.Auto;
    public string LastNote { get; private set; } = "Full frame";
    public int LastHand { get; private set; } = -1;
    public bool LastCropped { get; private set; }
    public string LastDiagnostics { get; private set; } = "";

    readonly QuestRgbCalibration calibration = new QuestRgbCalibration();
    readonly HoldingHandSelector selector = new HoldingHandSelector();
    readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
    readonly HandCropGeometry.Crop[] candidates = new HandCropGeometry.Crop[2];
    readonly bool[] available = new bool[2];
    struct Anchor { public Vector2 Pixel; public float Span, Scale; }
    sealed class Focus
    {
        public bool Valid, FullFrame;
        public HandCropGeometry.Crop Crop;
        public Anchor Reference;
        public int Members;
    }
    readonly Anchor[] anchors = new Anchor[2];
    readonly Focus[] focus = { new Focus(), new Focus() };
    readonly string[] candidateReason = new string[2];
    XRHandSubsystem hands;
    int emptyCrops, recoveryFrames;
    HoldingHandMode lastMode;
    int frameWidth, frameHeight;

    public string ModeLabel => mode == HoldingHandMode.Auto ? "Hand: Auto" :
        mode == HoldingHandMode.Off ? "Crop: Off" : "Hand: " + mode;

    public void CycleMode()
    {
        mode = (HoldingHandMode)(((int)mode + 1) % 4);
        ResetRound();
    }

    public void ResetRound()
    {
        selector.Reset();
        focus[0].Valid = focus[1].Valid = false;
        focus[0].FullFrame = focus[1].FullFrame = false;
        focus[0].Members = focus[1].Members = 0;
        emptyCrops = recoveryFrames = 0;
        LastHand = -1;
        LastCropped = false;
        lastMode = mode;
    }

    public bool CanFocusHand(ARCameraManager camera)
    {
        if (mode == HoldingHandMode.Off || camera == null || !camera.TryGetIntrinsics(out var intrinsics) ||
            !calibration.EnsureLoaded() || !TryGetTrackedHead(out Pose head))
            return false;

        var headDevice = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (headDevice.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out Vector3 velocity) &&
            velocity.sqrMagnitude > 2.25f)
            return false;

        FindHands();
        if (hands == null || !hands.running)
            return false;

        int width = intrinsics.resolution.x;
        int height = intrinsics.resolution.y;
        if (width <= 0 || height <= 0)
            return false;

        return (mode != HoldingHandMode.Right && CanFocus(hands.leftHand, 0)) ||
               (mode != HoldingHandMode.Left && CanFocus(hands.rightHand, 1));

        bool CanFocus(XRHand hand, int index)
        {
            return BuildAnchor(hand, index, head, intrinsics, width, height, out var anchor) &&
                (focus[index].FullFrame || HandCardFocus.Discovery(anchor.Pixel.x, anchor.Pixel.y,
                    anchor.Span, width, height, out _));
        }
    }

    public HandCropGeometry.Crop GetCrop(ARCameraManager camera, int width, int height, ScanTarget target)
    {
        var full = new HandCropGeometry.Crop(0, 0, width, height);
        LastCropped = false;
        LastHand = -1;
        available[0] = available[1] = false;
        LastDiagnostics = "";
        if (mode != lastMode) ResetRound();
        if (frameWidth != width || frameHeight != height)
        {
            focus[0].Valid = focus[1].Valid = false;
            focus[0].FullFrame = focus[1].FullFrame = false;
            frameWidth = width; frameHeight = height;
        }
        if (target != ScanTarget.Hand) { LastNote = "Full frame · Discard"; return full; }
        if (mode == HoldingHandMode.Off) { LastNote = "Full frame · Crop off"; return full; }
        // Current tracking poses lead the captured image during a fast head turn. Avoid making
        // a tight crop from mismatched instants; the padded envelope handles normal movement.
        var headDevice = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (headDevice.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out Vector3 velocity) &&
            velocity.sqrMagnitude > 2.25f)
        { LastNote = "Full frame · Head moving"; return full; }
        if (camera == null || !camera.TryGetIntrinsics(out var intrinsics) || !calibration.EnsureLoaded())
        { LastNote = "Full frame · Camera calibration unavailable"; return full; }
        if (intrinsics.resolution.x <= 0 || intrinsics.resolution.y <= 0 ||
            Mathf.Abs((float)intrinsics.resolution.x / intrinsics.resolution.y - (float)width / height) > .03f)
        { LastNote = "Full frame · Camera format changed"; return full; }
        FindHands();
        if (hands == null || !hands.running) { LastNote = "Full frame · Hand tracking unavailable"; return full; }
        if (!hands.leftHand.isTracked && !hands.rightHand.isTracked)
        {
            bool controllerTracked = ControllerTracked(XRNode.LeftHand) || ControllerTracked(XRNode.RightHand);
            LastNote = controllerTracked
                ? "Full frame · Hands not tracked\nPut down controllers, then Rescan"
                : "Full frame · Hands not tracked\nShow your holding hand, then Rescan";
            LastDiagnostics = $"leftTracked=False rightTracked=False controllerTracked={controllerTracked}";
            return full;
        }
        if (!TryGetTrackedHead(out Pose trackedHead))
        { LastNote = "Full frame · Head tracking pose unavailable"; return full; }
        available[0] = BuildAnchor(hands.leftHand, 0, trackedHead, intrinsics, width, height, out anchors[0]);
        available[1] = BuildAnchor(hands.rightHand, 1, trackedHead, intrinsics, width, height, out anchors[1]);
        LastDiagnostics = $"leftTracked={hands.leftHand.isTracked} leftROI={available[0]}({candidateReason[0]}) " +
            $"rightTracked={hands.rightHand.isTracked} rightROI={available[1]}({candidateReason[1]}) " +
            $"intrinsics={intrinsics.resolution.x}x{intrinsics.resolution.y} image={width}x{height} " +
            $"headTracking={trackedHead.position:F2}";
        if (!available[0] && !available[1])
        {
            LastNote = "Full frame · Hand area unavailable\nMove holding hand into view, then Rescan";
            return full;
        }
        int hand = mode == HoldingHandMode.Auto ? selector.Choose(available[0], available[1]) :
            mode == HoldingHandMode.Left ? 0 : 1;
        if (recoveryFrames > 0)
        { recoveryFrames--; LastNote = "Full frame · Searching for cards"; return full; }
        if (hand < 0)
        { LastNote = "Full frame · Finding holding hand"; return full; }
        if (!available[hand])
        { LastNote = "Full frame · " + (hand == 0 ? "Left" : "Right") + " hand not tracked"; return full; }
        var current = anchors[hand];
        var evidence = focus[hand];
        if (evidence.FullFrame)
        { LastNote = "Full frame · Cards already fill image"; return full; }
        bool refined = evidence.Valid && HandCardFocus.Follow(evidence.Crop,
            evidence.Reference.Pixel.x, evidence.Reference.Pixel.y, evidence.Reference.Scale,
            current.Pixel.x, current.Pixel.y, current.Scale, width, height, out candidates[hand]);
        if (evidence.Valid && !refined)
        {
            evidence.Valid = false;
            LastNote = "Full frame · Reacquiring card group";
            return full;
        }
        if (!refined && !HandCardFocus.Discovery(current.Pixel.x, current.Pixel.y, current.Span,
            width, height, out candidates[hand]))
        { LastNote = "Full frame · Locating cards near hand"; return full; }
        LastHand = hand;
        LastCropped = true;
        int percent = Mathf.RoundToInt(100f*candidates[hand].Width*candidates[hand].Height/width/height);
        LastNote = (hand == 0 ? "Left" : "Right") + (refined ? " hand · Cards focused" : " hand · Locating cards") +
            $"\nRegion: {percent}% of photo";
        return candidates[hand];
    }

    public void RecordResult(IReadOnlyList<CardEntry> cards)
    {
        int[] counts = new int[2];
        var boxes = new List<HandCardFocus.Box>();
        if (cards != null)
            foreach (var card in cards)
            {
                if (card?.bbox == null || card.bbox.Length < 4 || card.confidence < .25f) continue;
                boxes.Add(new HandCardFocus.Box(card.bbox[0], card.bbox[1], card.bbox[2], card.bbox[3]));
            }
        for (int hand = 0; hand < 2; hand++)
        {
            if (!available[hand]) continue;
            Anchor anchor = anchors[hand];
            bool hasFit = HandCardFocus.TryFit(boxes, anchor.Pixel.x, anchor.Pixel.y, anchor.Span,
                frameWidth, frameHeight, out var fitted, out counts[hand], out bool fillsFrame);
            focus[hand].FullFrame = fillsFrame;
            if (fillsFrame) { focus[hand].Valid = false; focus[hand].Members = counts[hand]; }
            if (!hasFit) continue;
            // A momentary partial detection must not shrink away the missing end cards.
            if (focus[hand].Valid && counts[hand] + 1 < focus[hand].Members) continue;
            focus[hand].Crop = fitted;
            focus[hand].Reference = anchor;
            focus[hand].Members = counts[hand];
            focus[hand].Valid = true;
        }
        if (mode == HoldingHandMode.Auto) selector.Observe(LastHand, counts[0], counts[1]);
        emptyCrops = LastCropped && (cards == null || cards.Count == 0) ? emptyCrops + 1 : 0;
        if (emptyCrops >= 2)
        {
            if (LastHand >= 0) focus[LastHand].Valid = false;
            recoveryFrames = 2; emptyCrops = 0;
        }
    }

    bool BuildAnchor(XRHand hand, int index, Pose trackedHead,
        UnityEngine.XR.ARSubsystems.XRCameraIntrinsics intrinsics, int width, int height,
        out Anchor anchor)
    {
        anchor = default;
        candidateReason[index] = "ok";
        if (!hand.isTracked)
        { candidateReason[index] = "not tracked"; return false; }
        if (!hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm))
        { candidateReason[index] = "palm pose missing"; return false; }
        if (!ToHead(palm.position, trackedHead, out Vector3 palmInHead))
        { candidateReason[index] = "invalid tracked pose"; return false; }
        if (palmInHead.z < .10f || palmInHead.z > 1.8f)
        { candidateReason[index] = $"palm depth {palmInHead.z:F2}m"; return false; }
        if (!Project(palmInHead, intrinsics, width, height, out Vector2 pixel))
        { candidateReason[index] = "palm outside RGB projection"; return false; }
        float span = 0;
        if (hand.GetJoint(XRHandJointID.IndexMetacarpal).TryGetPose(out var indexPose) &&
            hand.GetJoint(XRHandJointID.LittleMetacarpal).TryGetPose(out var littlePose) &&
            ToHead(indexPose.position, trackedHead, out var a) && ToHead(littlePose.position, trackedHead, out var b) &&
            Project(a, intrinsics, width, height, out var pa) && Project(b, intrinsics, width, height, out var pb))
            span = Vector2.Distance(pa, pb);
        if (span < 8 && Project(palmInHead + new Vector3(.065f, 0, 0), intrinsics, width, height, out var edge))
            span = Vector2.Distance(pixel, edge);
        span = Mathf.Clamp(span, 12, Mathf.Min(width, height)*.12f);
        anchor = new Anchor { Pixel = pixel, Span = span, Scale = 1/palmInHead.z };
        candidateReason[index] = $"palmHead={palmInHead:F2} pixel={pixel:F0} span={span:F0} evidence={focus[index].Members}";
        return true;
    }

    bool Project(Vector3 inHead, UnityEngine.XR.ARSubsystems.XRCameraIntrinsics intrinsics,
        int width, int height, out Vector2 pixel)
    {
        double sumX = 0, sumY = 0; int count = 0;
        foreach (Pose lens in calibration.OpticalFromHead)
        {
            Vector3 p = Quaternion.Inverse(lens.rotation) * (inHead - lens.position);
            if (!HandCropGeometry.Project(p.x, p.y, p.z, intrinsics.focalLength.x, intrinsics.focalLength.y,
                intrinsics.principalPoint.x, intrinsics.principalPoint.y, intrinsics.resolution.x,
                intrinsics.resolution.y, width, height, out double x, out double y)) continue;
            sumX += x; sumY += y; count++;
        }
        pixel = count > 0 ? new Vector2((float)(sumX/count), (float)(sumY/count)) : default;
        return count > 0;
    }

    static bool ToHead(Vector3 point, Pose head, out Vector3 result)
    {
        bool valid = HandCropGeometry.HeadRelative(point.x, point.y, point.z,
            head.position.x, head.position.y, head.position.z,
            head.rotation.x, head.rotation.y, head.rotation.z, head.rotation.w, out double x, out double y, out double z);
        result = new Vector3((float)x, (float)y, (float)z);
        return valid;
    }

    static bool TryGetTrackedHead(out Pose pose)
    {
        pose = default;
        var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked) return false;
        bool position = device.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 p) ||
            device.TryGetFeatureValue(CommonUsages.devicePosition, out p);
        bool rotation = device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion q) ||
            device.TryGetFeatureValue(CommonUsages.deviceRotation, out q);
        if (!position || !rotation) return false;
        pose = new Pose(p, q);
        return true;
    }

    static bool ControllerTracked(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        return (device.characteristics & InputDeviceCharacteristics.Controller) != 0 &&
            device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
    }

    void FindHands()
    {
        if (hands != null && hands.running) return;
        subsystems.Clear();
        SubsystemManager.GetSubsystems(subsystems);
        hands = null;
        foreach (var subsystem in subsystems) if (subsystem.running) { hands = subsystem; break; }
    }
}
