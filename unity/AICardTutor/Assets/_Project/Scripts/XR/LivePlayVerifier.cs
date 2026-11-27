using System;
using System.Collections.Generic;

/// <summary>Evidence gate, not a rules engine. Missing cards alone can never issue a proof.</summary>
public sealed class LivePlayVerifier
{
    public sealed class Proof
    {
        public readonly string Key;
        public readonly int ReferenceCount,HandFrame,TableFrame;
        public readonly float CapturedAt;
        internal Proof(string key,int count,int hand,int table,float time)
        { Key=key;ReferenceCount=count;HandFrame=hand;TableFrame=table;CapturedAt=time; }
    }
    readonly List<TableSample> table=new List<TableSample>();
    struct TableSample { public string Key;public float Time,X,Y; }
    IReadOnlyList<StableHandEvidence.CardCount> reference=Array.Empty<StableHandEvidence.CardCount>();
    StableHandEvidence.Snapshot hand;
    string topKey;
    int handFrame=-1,tableFrame=-1,referenceCount;
    float handTime=-1,tableTime=-1,baselineTime=-1,handBaselineTime=-1;
    float releaseTime=-1;
    bool handWasSeen,tableWasSeen,handSafe,lastTableSafe;
    bool tableGeometryClear;
    bool tableObservationKnown;
    public string Message { get; private set; }="Scan and confirm the hand and table first.";
    public bool HasSeenReferenceHand=>handWasSeen;
    public bool TableHandsBlocked=>tableObservationKnown && !tableGeometryClear;

    public void Reset(StableHandEvidence.Snapshot evidence,string confirmedTop)
    {
        reference=evidence.Reference;referenceCount=evidence.ReferenceCount;topKey=confirmedTop;
        hand=null;handFrame=tableFrame=-1;handTime=tableTime=-1;
        handBaselineTime=baselineTime=-1;handWasSeen=tableWasSeen=handSafe=lastTableSafe=false;table.Clear();
        releaseTime=-1;
        tableGeometryClear=false;
        tableObservationKnown=false;
        Message="Waiting to see the confirmed hand and original table card.";
    }
    public void Interrupt(string reason)
    {
        table.Clear();hand=null;handWasSeen=tableWasSeen=handSafe=lastTableSafe=false;
        handBaselineTime=baselineTime=-1;Message=reason;
        releaseTime=-1;
        tableGeometryClear=false;
        tableObservationKnown=false;
    }
    public void ObserveRelease(float time)
    {if(!ValidTime(time)){releaseTime=-1;return;}if(time>releaseTime)releaseTime=time;}
    public void ObserveHand(int frame,float time,StableHandEvidence.Snapshot evidence,bool spatiallySeparate)
    {
        if(frame<=handFrame || !ValidTime(time) || time<handTime)return;
        handFrame=frame;handTime=time;hand=evidence;handSafe=spatiallySeparate;
        if(spatiallySeparate && evidence.HasReference && evidence.HasConsensus &&
            evidence.State==StableHandEvidence.Phase.Matches)
        { handWasSeen=true;handBaselineTime=time; }
    }
    /// <param name="x">Table detection centre, normalised to the dedicated table region.</param>
    /// <param name="clear">Both hands tracked clear of the table, disjoint regions and a valid camera anchor.</param>
    public void ObserveTable(int frame,float time,string key,float confidence,float x,float y,bool clear)
    {
        if(frame<=tableFrame || !ValidTime(time) || time<tableTime)return;
        if(tableTime>=0 && time-tableTime>3f){table.Clear();tableWasSeen=false;}
        tableFrame=frame;tableTime=time;
        tableGeometryClear=clear;
        tableObservationKnown=true;
        lastTableSafe=clear && StableHandEvidence.ValidKey(key) && ValidTime(confidence) && confidence>=.6f && confidence<=1 &&
            ValidTime(x) && ValidTime(y) && x<=1 && y<=1;
        if(!lastTableSafe){table.Clear();Message="Table unclear or hands overlap it; no play recorded.";return;}
        table.Add(new TableSample{Key=key,Time=time,X=x,Y=y});
        while(table.Count>5 || table.Count>0 && time-table[0].Time>5f)table.RemoveAt(0);
        int baseline=0;float first=time;
        foreach(var item in table)if(item.Key==topKey){baseline++;first=Math.Min(first,item.Time);}
        if(baseline>=2 && time-first>=.4f && key==topKey)
        {tableWasSeen=true;baselineTime=time;}
    }
    public bool TryProve(float now,out Proof proof)
    {
        proof=null;
        if(referenceCount<1 || !StableHandEvidence.ValidKey(topKey))
        {Message="Confirm Hand and Discard before automatic play verification.";return false;}
        if(!handWasSeen)
        {Message=hand!=null && !handSafe ? "Waiting: show holding hand away from the blue table zone." :
            "Waiting: hold the complete confirmed hand steady for repeated recognition.";return false;}
        if(!tableWasSeen)
        {Message="Waiting: original table card must be seen twice in the table zone.";return false;}
        if(!handSafe){Message="Hand crop overlaps table or is unavailable; no play recorded.";return false;}
        if(!lastTableSafe){Message="Open playing hand away from table; keep both hands tracked.";return false;}
        if(hand==null || hand.ObservedCount!=hand.CandidateCount || now-handTime>2.5f || now-tableTime>2.5f ||
            now<handTime || now<tableTime || !hand.HasConsensus ||
            (hand.State!=StableHandEvidence.Phase.MissingCards && hand.State!=StableHandEvidence.Phase.Occluded))
        {Message="Monitoring; missing detections alone do not count as plays.";return false;}
        string missing=null;int removed=0;
        foreach(var old in reference)
        {
            int count=Count(hand.Candidate,old.Key),delta=old.Count-count;
            if(delta<0){Message="Hand composition uncertain; retaining cards.";return false;}
            if(delta>0){removed+=delta;missing=old.Key;}
        }
        foreach(var item in hand.Candidate)
            if(Count(reference,item.Key)==0){Message="Hand labels changed; retaining cards.";return false;}
        if(removed!=1 || hand.CandidateCount!=referenceCount-1)
        {Message="Need exactly one stable missing card; retaining cards.";return false;}
        if(missing==topKey)
        {Message="Same-face play cannot be distinguished from the old top; confirm manually.";return false;}
        if(releaseTime<=baselineTime || now<releaseTime || now-releaseTime>6f)
        {Message="Missing card: waiting for a tracked place-and-release gesture.";return false;}
        var latest=table.Count>0 ? table[table.Count-1] : default;
        if(latest.Key!=missing){Message="Missing card not confirmed on the table; retaining cards.";return false;}
        int support=0;float firstSeen=tableTime,minX=1,minY=1,maxX=0,maxY=0;
        foreach(var item in table)
            if(item.Key==missing && item.Time>baselineTime)
            {support++;firstSeen=Math.Min(firstSeen,item.Time);minX=Math.Min(minX,item.X);maxX=Math.Max(maxX,item.X);
                minY=Math.Min(minY,item.Y);maxY=Math.Max(maxY,item.Y);}
        if(support<4 || tableTime-firstSeen<1f || maxX-minX>.1f || maxY-minY>.1f ||
            firstSeen<handBaselineTime-5f || Math.Abs(handTime-tableTime)>2.5f)
        {Message="Verifying a settled new table card over multiple frames.";return false;}
        proof=new Proof(missing,referenceCount,handFrame,tableFrame,Math.Min(handTime,tableTime));
        Message="Visual evidence verified; checking UNO rules.";return true;
    }
    static int Count(IReadOnlyList<StableHandEvidence.CardCount> cards,string key)
    {foreach(var card in cards)if(card.Key==key)return card.Count;return 0;}
    static bool ValidTime(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value)&&value>=0;
    public static bool PlayingHandOpen(IReadOnlyList<float> thumbToFingertipDistances)
    {
        if(thumbToFingertipDistances==null || thumbToFingertipDistances.Count!=4)return false;
        foreach(float distance in thumbToFingertipDistances)
            if(!ValidTime(distance) || distance<.045f)return false;
        return true;
    }
    public string Diagnostics(float now)=>$"Hand baseline={handWasSeen}, table baseline={tableWasSeen}; "+
        $"hand={hand?.ObservedCount ?? -1}/{referenceCount}, votes={hand?.AgreeingFrames ?? 0}/{hand?.WindowFrames ?? 0}; "+
        $"table={((table.Count>0) ? table[table.Count-1].Key : "none")}, frames={table.Count}, clear={lastTableSafe}; "+
        $"release age={(releaseTime>=0 ? now-releaseTime : -1):F1}s";
}

/// <summary>One observed near-table pinch followed by open/clear hands. Never extends an old release.</summary>
public sealed class LiveReleaseTracker
{
    int hand=-1;
    float pinchAt=-1,lastTime=-1;
    public float ReleasedAt { get; private set; }=-1;
    public void Reset(){hand=-1;pinchAt=lastTime=-1;ReleasedAt=-1;}
    public void Update(int holdingHand,float now,bool valid,bool playingNearTable,bool pinching,bool open,bool bothClear)
    {
        if(!valid || holdingHand<0 || holdingHand>1 || float.IsNaN(now) || float.IsInfinity(now) || now<0 || now<lastTime)
        {Reset();return;}
        if(hand!=holdingHand){Reset();hand=holdingHand;}
        lastTime=now;
        if(pinching && playingNearTable){pinchAt=now;ReleasedAt=-1;return;}
        if(open && bothClear && pinchAt>=0 && now-pinchAt<=4f)
        {ReleasedAt=now;pinchAt=-1;}
    }
}
