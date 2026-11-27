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
    readonly LiveReleaseTracker release=new LiveReleaseTracker();
    public float LastTableReleaseAt=>release.ReleasedAt;
    public string TableHandsIssue { get; private set; }="Waiting for tracked hands.";
    public void ResetTableInteraction()=>release.Reset();
    public bool TryGetHoldingPalm(ARCameraManager camera,int width,int height,int preferred,out Vector2 pixel)
    {
        pixel=default;FindHands();
        if(camera==null || !camera.TryGetIntrinsics(out var intrinsics) || !calibration.EnsureLoaded() ||
            !TryGetTrackedHead(out var head) || hands==null || !hands.running)return false;
        int index=preferred>=0 && preferred<=1 ? preferred : LastHand;
        if(index<0 || index>1)return false;
        var hand=index==0 ? hands.leftHand : hands.rightHand;
        return hand.isTracked && hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var pose) &&
            ToHead(pose.position,head,out var local) && Project(local,intrinsics,width,height,out pixel);
    }
    public bool TryGetRgbProjection(ARCameraManager camera,int width,int height,out RgbViewProjection projection)
    {
        projection=default;
        if(camera==null || !camera.TryGetIntrinsics(out var intrinsics) || !calibration.EnsureLoaded() ||
            intrinsics.resolution.x<=0 || intrinsics.resolution.y<=0 ||
            Mathf.Abs((float)intrinsics.resolution.x/intrinsics.resolution.y-(float)width/height)>.03f)return false;
        Quaternion optical=calibration.OpticalFromHead[0].rotation;
        for(int i=1;i<calibration.OpticalFromHead.Count;i++)optical=Quaternion.Slerp(optical,calibration.OpticalFromHead[i].rotation,1f/(i+1));
        projection=new RgbViewProjection{Fx=intrinsics.focalLength.x*width/intrinsics.resolution.x,
            Fy=intrinsics.focalLength.y*height/intrinsics.resolution.y,Cx=intrinsics.principalPoint.x*width/intrinsics.resolution.x,
            Cy=intrinsics.principalPoint.y*height/intrinsics.resolution.y,Width=width,Height=height,OpticalFromHead=optical};
        return projection.Valid;
    }
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
    readonly Focus[] trustedFocus = { new Focus(), new Focus() };
    bool liveContinuity,hasFollowedCrop,forceFullAcquisition;
    HandCropGeometry.Crop followedCrop;
    int followedHand=-1;
    public bool HasLiveFocus=>hasFollowedCrop || followedHand>=0 && trustedFocus[followedHand].FullFrame;
    readonly string[] candidateReason = new string[2];
    XRHandSubsystem hands;
    int emptyCrops, recoveryFrames;
    HoldingHandMode lastMode;
    int frameWidth, frameHeight;

    public string ModeLabel => mode == HoldingHandMode.Auto ? "Hand: Auto" :
        mode == HoldingHandMode.Off ? "Crop: Off" : "Hand: " + mode;

    public void CycleMode()
    {
        bool live=liveContinuity;
        mode = (HoldingHandMode)(((int)mode + 1) % 4);
        ResetRound();
        liveContinuity=live;
    }

    public void ResetRound()
    {
        liveContinuity=false;hasFollowedCrop=false;forceFullAcquisition=false;followedHand=-1;
        Clear(trustedFocus[0]);Clear(trustedFocus[1]);
        selector.Reset();
        focus[0].Valid = focus[1].Valid = false;
        focus[0].FullFrame = focus[1].FullFrame = false;
        focus[0].Members = focus[1].Members = 0;
        emptyCrops = recoveryFrames = 0;
        LastHand = -1;
        LastCropped = false;
        lastMode = mode;
    }

    public void BeginLiveRound(bool fullReset)
    {
        if(fullReset)ResetRound();
        else for(int i=0;i<2;i++)Copy(trustedFocus[i],focus[i]);
        liveContinuity=true;forceFullAcquisition=fullReset;emptyCrops=recoveryFrames=0;
    }
    // Only a stable, sufficiently confident completed Scan Hand may replace the trusted crop.
    public void AcceptLiveRound(IReadOnlyList<CardEntry> cards,int width,int height,bool reliable)
    {
        if(!liveContinuity || !reliable || width<128 || height<128)return;
        forceFullAcquisition=false;
        frameWidth=width;frameHeight=height;
        RecordResult(cards,retainEmptyFocus:true,preserveExtent:true);
        for(int i=0;i<2;i++)if(focus[i].Valid || focus[i].FullFrame)Copy(focus[i],trustedFocus[i]);
        int hand=LastHand>=0 ? LastHand : selector.Selected;
        if(hand>=0 && trustedFocus[hand].FullFrame){followedHand=hand;hasFollowedCrop=false;return;}
        if(hand>=0 && trustedFocus[hand].Valid)
        {
            var saved=trustedFocus[hand];var crop=saved.Crop;
            if(available[hand] && HandCardFocus.Follow(saved.Crop,saved.Reference.Pixel.x,saved.Reference.Pixel.y,saved.Reference.Scale,
                anchors[hand].Pixel.x,anchors[hand].Pixel.y,anchors[hand].Scale,width,height,out var moved))crop=moved;
            else if(hasFollowedCrop && followedHand==hand)crop=followedCrop;
            Remember(crop,hand);return;
        }
        // RGB detections can preserve a useful region even when Quest temporarily lacks a palm pose.
        // They affect the next input crop only; no card filtering or alternate scan model is added.
        float left=float.MaxValue,top=float.MaxValue,right=float.MinValue,bottom=float.MinValue,size=0;
        if(cards!=null)foreach(var card in cards)
        {
            var box=card?.bbox;
            if(box==null || box.Length<4 || !HandCropGeometry.Finite(box[0]) || !HandCropGeometry.Finite(box[1]) ||
                !HandCropGeometry.Finite(box[2]) || !HandCropGeometry.Finite(box[3]) || box[2]<=box[0] || box[3]<=box[1])continue;
            left=Mathf.Min(left,box[0]);top=Mathf.Min(top,box[1]);right=Mathf.Max(right,box[2]);bottom=Mathf.Max(bottom,box[3]);
            size=Mathf.Max(size,Mathf.Max(box[2]-box[0],box[3]-box[1]));
        }
        if(right>left)
        {
            float pad=Mathf.Max(96,Mathf.Min(Mathf.Min(width,height)*.12f,size*1.5f));
            if(HandCropGeometry.TryCrop(left-pad,top-pad,right+pad,bottom+pad,width,height,out var crop))
            {
                if(hasFollowedCrop)
                {
                    float cx=crop.X+crop.Width*.5f,cy=crop.Y+crop.Height*.5f;
                    float w=Mathf.Max(crop.Width,followedCrop.Width),h=Mathf.Max(crop.Height,followedCrop.Height);
                    if(HandCropGeometry.TryCrop(cx-w*.5f,cy-h*.5f,cx+w*.5f,cy+h*.5f,width,height,out var padded))crop=padded;
                }
                Remember(crop,hand);
            }
        }
        emptyCrops=recoveryFrames=0;
    }
    static void Clear(Focus value){value.Valid=value.FullFrame=false;value.Members=0;}
    static void Copy(Focus from,Focus to)
    {to.Valid=from.Valid;to.FullFrame=from.FullFrame;to.Crop=from.Crop;to.Reference=from.Reference;to.Members=from.Members;}
    void Remember(HandCropGeometry.Crop crop,int hand)
    {
        if(crop.Width<128 || crop.Height<128 || crop.X<0 || crop.Y<0 || crop.X+crop.Width>frameWidth || crop.Y+crop.Height>frameHeight)return;
        followedCrop=crop;followedHand=hand;hasFollowedCrop=true;
    }
    HandCropGeometry.Crop HoldOrFull(HandCropGeometry.Crop full,string reason)
    {
        if(liveContinuity && hasFollowedCrop)
        {
            LastHand=followedHand;LastCropped=true;
            LastNote="Live hand · Last reliable region\n"+reason;return followedCrop;
        }
        LastNote="Full frame · "+reason;return full;
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
            hasFollowedCrop=false;Clear(trustedFocus[0]);Clear(trustedFocus[1]);
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
        { return HoldOrFull(full,"Head moving"); }
        if (camera == null || !camera.TryGetIntrinsics(out var intrinsics) || !calibration.EnsureLoaded())
        { return HoldOrFull(full,"Camera calibration unavailable"); }
        if (intrinsics.resolution.x <= 0 || intrinsics.resolution.y <= 0 ||
            Mathf.Abs((float)intrinsics.resolution.x / intrinsics.resolution.y - (float)width / height) > .03f)
        { return HoldOrFull(full,"Camera format changed"); }
        FindHands();
        if (hands == null || !hands.running) return HoldOrFull(full,"Hand tracking unavailable");
        if (!hands.leftHand.isTracked && !hands.rightHand.isTracked)
        {
            bool controllerTracked = ControllerTracked(XRNode.LeftHand) || ControllerTracked(XRNode.RightHand);
            LastDiagnostics = $"leftTracked=False rightTracked=False controllerTracked={controllerTracked}";
            return HoldOrFull(full,controllerTracked ? "Hands not tracked · Put down controllers" : "Hands not tracked · Show your holding hand");
        }
        if (!TryGetTrackedHead(out Pose trackedHead))
        { return HoldOrFull(full,"Head tracking pose unavailable"); }
        available[0] = BuildAnchor(hands.leftHand, 0, trackedHead, intrinsics, width, height, out anchors[0]);
        available[1] = BuildAnchor(hands.rightHand, 1, trackedHead, intrinsics, width, height, out anchors[1]);
        LastDiagnostics = $"leftTracked={hands.leftHand.isTracked} leftROI={available[0]}({candidateReason[0]}) " +
            $"rightTracked={hands.rightHand.isTracked} rightROI={available[1]}({candidateReason[1]}) " +
            $"intrinsics={intrinsics.resolution.x}x{intrinsics.resolution.y} image={width}x{height} " +
            $"headTracking={trackedHead.position:F2}";
        if (!available[0] && !available[1])
        {
            return HoldOrFull(full,"Hand area temporarily unavailable");
        }
        if(forceFullAcquisition)
        {forceFullAcquisition=false;LastNote="Full frame · Live reacquisition";return full;}
        int hand = mode == HoldingHandMode.Auto ? selector.Choose(available[0], available[1]) :
            mode == HoldingHandMode.Left ? 0 : 1;
        if (recoveryFrames > 0)
        { recoveryFrames--; return HoldOrFull(full,"Searching for cards"); }
        if (hand < 0)
        { return HoldOrFull(full,"Finding holding hand"); }
        if (!available[hand])
        { return HoldOrFull(full,(hand == 0 ? "Left" : "Right")+" hand not tracked"); }
        var current = anchors[hand];
        var evidence = liveContinuity && HasLiveFocus ? trustedFocus[hand] : focus[hand];
        if (evidence.FullFrame)
        { LastNote = "Full frame · Cards already fill image"; return full; }
        if(liveContinuity && HasLiveFocus && !evidence.Valid)return HoldOrFull(full,"Following card region");
        bool refined = evidence.Valid && HandCardFocus.Follow(evidence.Crop,
            evidence.Reference.Pixel.x, evidence.Reference.Pixel.y, evidence.Reference.Scale,
            current.Pixel.x, current.Pixel.y, current.Scale, width, height, out candidates[hand]);
        if (evidence.Valid && !refined)
        {
            if(!liveContinuity)evidence.Valid=false;
            return HoldOrFull(full,"Reacquiring card group");
        }
        if (!refined && !HandCardFocus.Discovery(current.Pixel.x, current.Pixel.y, current.Span,
            width, height, out candidates[hand]))
        { return HoldOrFull(full,"Locating cards near hand"); }
        LastHand = hand;
        LastCropped = true;
        if(liveContinuity && HasLiveFocus)Remember(candidates[hand],hand);
        int percent = Mathf.RoundToInt(100f*candidates[hand].Width*candidates[hand].Height/width/height);
        LastNote = (hand == 0 ? "Left" : "Right") + (refined ? " hand · Cards focused" : " hand · Locating cards") +
            $"\nRegion: {percent}% of photo";
        return candidates[hand];
    }

    public void RecordResult(IReadOnlyList<CardEntry> cards,bool retainEmptyFocus=false,bool preserveExtent=false)
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
            if (focus[hand].Valid && (counts[hand] + 1 < focus[hand].Members || (preserveExtent || liveContinuity && HasLiveFocus) && counts[hand]<focus[hand].Members)) continue;
            focus[hand].Crop = fitted;
            focus[hand].Reference = anchor;
            focus[hand].Members = counts[hand];
            focus[hand].Valid = true;
        }
        if (mode == HoldingHandMode.Auto && !(liveContinuity && HasLiveFocus && counts[0]==0 && counts[1]==0))selector.Observe(LastHand, counts[0], counts[1]);
        emptyCrops = LastCropped && (cards == null || cards.Count == 0) ? emptyCrops + 1 : 0;
        if(retainEmptyFocus || liveContinuity && HasLiveFocus)emptyCrops=recoveryFrames=0;
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

    public static bool TryGetTrackedHead(out Pose pose)
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

    /// <summary>Fail closed if either hand/joints/calibration cannot prove clearance of the table.</summary>
    public bool HandsClearOfTable(ARCameraManager camera,HandCropGeometry.Crop region,int width,int height,int holdingHand)
    {
        FindHands();
        if(camera==null || !camera.TryGetIntrinsics(out var intrinsics) || !calibration.EnsureLoaded() ||
            !TryGetTrackedHead(out var head) || hands==null || !hands.running ||
            !hands.leftHand.isTracked || !hands.rightHand.isTracked || holdingHand<0 || holdingHand>1)return UnclearTableHands();
        if(intrinsics.resolution.x<=0 || intrinsics.resolution.y<=0 ||
            Mathf.Abs((float)intrinsics.resolution.x/intrinsics.resolution.y-(float)width/height)>.03f)return UnclearTableHands();
        // A stationary card can still be held. Require the playing hand to be open,
        // not pinching the card, as well as being clear of the enlarged table area.
        var playing=holdingHand==0 ? hands.rightHand : hands.leftHand;
        if(!playing.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb))return UnclearTableHands();
        var distances=new List<float>(4);
        bool completeShape=true;
        foreach(var tip in new[]{XRHandJointID.IndexTip,XRHandJointID.MiddleTip,XRHandJointID.RingTip,XRHandJointID.LittleTip})
        {
            if(!playing.GetJoint(tip).TryGetPose(out var finger)){completeShape=false;distances.Add(float.NaN);}
            else distances.Add(Vector3.Distance(thumb.position,finger.position));
        }
        bool open=LivePlayVerifier.PlayingHandOpen(distances),pinching=false;
        foreach(float distance in distances)if(HandCropGeometry.Finite(distance) && distance<.035f)pinching=true;
        var playingJoints=new[]{XRHandJointID.Palm,XRHandJointID.Wrist,XRHandJointID.IndexTip,XRHandJointID.MiddleTip,
            XRHandJointID.RingTip,XRHandJointID.LittleTip,XRHandJointID.ThumbTip};
        var holdingJoints=new[]{XRHandJointID.Palm,XRHandJointID.Wrist};
        bool bothClear=true,playingNear=false;
        string overlap=null;
        var trackedHands=new[]{hands.leftHand,hands.rightHand};
        for(int index=0;index<trackedHands.Length;index++)
            foreach(var joint in index==holdingHand ? holdingJoints : playingJoints)
            {
                if(!trackedHands[index].GetJoint(joint).TryGetPose(out var pose))
                {
                    if(index!=holdingHand && joint!=XRHandJointID.Palm && joint!=XRHandJointID.Wrist &&
                        joint!=XRHandJointID.IndexTip && joint!=XRHandJointID.ThumbTip){completeShape=false;continue;}
                    return UnclearTableHands();
                }
                if(!ToHead(pose.position,head,out var local) || !Project(local,intrinsics,width,height,out var pixel))return UnclearTableHands();
                float pad=index==holdingHand ? Mathf.Max(32,Mathf.Min(width,height)*.025f) :
                    Mathf.Max(Mathf.Min(width,height)*.12f,Mathf.Max(region.Width,region.Height)*.6f);
                if(pixel.x>=region.X-pad && pixel.x<=region.X+region.Width+pad &&
                    pixel.y>=region.Y-pad && pixel.y<=region.Y+region.Height+pad)
                {bothClear=false;overlap=index==holdingHand ? "Move holding hand away from the table zone." : "Move playing hand away after placing the card.";
                    if(index!=holdingHand)playingNear=true;}
            }
        release.Update(holdingHand,Time.unscaledTime,true,playingNear,pinching,open,bothClear);
        TableHandsIssue=!bothClear ? overlap : !completeShape ? "Playing fingers partly hidden; show the empty playing hand." : "Table view clear.";
        // Table observation and release are different states. A relaxed hand away from
        // the pile must not prevent learning the old/new top. A play still needs release evidence.
        return bothClear && completeShape;
    }
    bool UnclearTableHands(){release.Reset();TableHandsIssue="Both hands must be tracked; show holding and playing hand.";return false;}
}
