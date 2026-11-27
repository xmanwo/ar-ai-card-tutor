using System;
using System.Collections.Generic;

/// <summary>Learn the hand automatically from independent raw frames. Never remove on absence.</summary>
public sealed class LiveHandInventory
{
    readonly StableHandEvidence evidence=new StableHandEvidence();
    readonly HashSet<int> excluded=new HashSet<int>();
    LiveCardTracker.Box anchor;
    public IReadOnlyList<FreeCardPlayVerifier.Observation> Visible { get; private set; }=Array.Empty<FreeCardPlayVerifier.Observation>();
    public string Message { get; private set; }="Show your hand; Live learns it automatically.";
    public void Reset(bool forgetPosition=false)
    {evidence.Reset(null);if(forgetPosition){anchor=default;excluded.Clear();}Visible=Array.Empty<FreeCardPlayVerifier.Observation>();}
    public void Exclude(int id){excluded.Add(id);if(excluded.Count>256)excluded.Clear();evidence.Reset(null);}
    public void Allow(int id){excluded.Remove(id);evidence.Reset(null);}
    public void Interrupt(){evidence.Reset(null);}
    public void SeedFocus(IReadOnlyList<FreeCardPlayVerifier.Seed> seeds)
    {
        if(anchor.Valid || seeds==null)return;
        var points=new List<FreeCardPlayVerifier.Observation>();
        foreach(var seed in seeds)if(seed.Box.Valid)points.Add(new FreeCardPlayVerifier.Observation(1,seed.Key,1,seed.Box));
        if(points.Count>0)anchor=Bounds(points);
    }
    public bool Observe(int frame,float time,IReadOnlyList<FreeCardPlayVerifier.Observation> input,
        FreeCardPlayVerifier.Point? palm,IReadOnlyList<string> saved,out IReadOnlyList<FreeCardPlayVerifier.Observation> accepted)
    {
        accepted=null;var valid=new List<FreeCardPlayVerifier.Observation>();var ids=new HashSet<int>();
        if(saved!=null && saved.Count==0 && !palm.HasValue)
        {Visible=Array.Empty<FreeCardPlayVerifier.Observation>();evidence.Reset(null);return false;}
        if(input!=null)foreach(var o in input)
            if(o.TrackId>0 && !excluded.Contains(o.TrackId) && ids.Add(o.TrackId) && o.Box.Valid &&
                StableHandEvidence.ValidKey(o.Key) && o.Confidence>=.55f && o.Confidence<=1)
            {valid.Add(o);if(valid.Count==64)break;}
        var groups=new List<List<FreeCardPlayVerifier.Observation>>();var used=new HashSet<int>();
        foreach(var o in valid)
        {
            if(!used.Add(o.TrackId))continue;
            var group=new List<FreeCardPlayVerifier.Observation>{o};
            for(int i=0;i<group.Count;i++)foreach(var other in valid)
                if(!used.Contains(other.TrackId) && Distance(group[i].Box,other.Box)<.06f)
                {used.Add(other.TrackId);group.Add(other);}
            groups.Add(group);
        }
        List<FreeCardPlayVerifier.Observation> best=null;float bestScore=float.MinValue;
        foreach(var group in groups)
        {
            var bounds=Bounds(group);if(Distance(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom)>.44f)continue;
            float gap=palm.HasValue ? Gap(bounds,palm.Value.X,palm.Value.Y) : anchor.Valid ? Gap(bounds,anchor.X,anchor.Y) : 0;
            if(gap>.18f || group.Count==1 && !palm.HasValue && !anchor.Valid)continue;
            float score=group.Count-gap*12;
            if(score>bestScore){best=group;bestScore=score;}
        }
        Visible=best!=null ? best.AsReadOnly() : Array.Empty<FreeCardPlayVerifier.Observation>();
        if(best!=null)anchor=Bounds(best);
        var samples=new List<StableHandEvidence.Sample>();foreach(var o in Visible)samples.Add(new StableHandEvidence.Sample(o.Key,o.Confidence));
        if(!evidence.Update(frame,time,samples))return false;
        var current=evidence.Current;
        Message=$"Learning hand: {Visible.Count} visible · {current.AgreeingFrames}/4 agreeing frames";
        if(!current.HasConsensus || best==null || !SameCounts(best,current.Candidate))return false;
        var available=new Dictionary<string,int>();foreach(var o in best){available.TryGetValue(o.Key,out var n);available[o.Key]=n+1;}
        if(saved!=null)foreach(var key in saved)
        {if(!available.TryGetValue(key,out int n) || n==0)return false;available[key]=n-1;}
        if(saved!=null && saved.Count==best.Count)return false;
        accepted=best.AsReadOnly();Message=$"Live hand established: {best.Count} cards";return true;
    }
    static bool SameCounts(List<FreeCardPlayVerifier.Observation> a,IReadOnlyList<StableHandEvidence.CardCount> b)
    {
        var counts=new Dictionary<string,int>();foreach(var o in a){counts.TryGetValue(o.Key,out int n);counts[o.Key]=n+1;}
        if(counts.Count!=b.Count)return false;foreach(var o in b)if(!counts.TryGetValue(o.Key,out int n) || n!=o.Count)return false;return true;
    }
    public static LiveCardTracker.Box Bounds(IReadOnlyList<FreeCardPlayVerifier.Observation> items)
    {
        float l=float.MaxValue,t=float.MaxValue,r=float.MinValue,b=float.MinValue;
        foreach(var o in items){l=Math.Min(l,o.Box.Left);t=Math.Min(t,o.Box.Top);r=Math.Max(r,o.Box.Right);b=Math.Max(b,o.Box.Bottom);}
        return items.Count>0 ? new LiveCardTracker.Box(l,t,r,b) : default;
    }
    static float Distance(LiveCardTracker.Box a,LiveCardTracker.Box b)=>Distance(a.X,a.Y,b.X,b.Y);
    static float Distance(float x,float y,float a,float b)=>(float)Math.Sqrt((x-a)*(x-a)+(y-b)*(y-b));
    static float Gap(LiveCardTracker.Box b,float x,float y)=>Distance(x,y,Math.Max(b.Left,Math.Min(b.Right,x)),Math.Max(b.Top,Math.Min(b.Bottom,y)));
}

/// <summary>One persistent view window. No periodic crop/full-frame alternation.</summary>
public sealed class LiveFocusWindow
{
    LiveCardTracker.Box bounds;
    float lastSeen=-999;
    public bool HasFocus=>bounds.Valid;
    public void Reset(){bounds=default;lastSeen=-999;}
    public bool NeedsRecovery(float now)=>!bounds.Valid || now-lastSeen>3;
    public void Observe(LiveCardTracker.Box observed,float time)
    {
        if(!observed.Valid)return;
        var target=new LiveCardTracker.Box(observed.Left-.10f,observed.Top-.10f,observed.Right+.10f,observed.Bottom+.10f);
        if(!bounds.Valid)bounds=target;
        else
        {
            // Ignore tiny boundary changes and ease in larger changes instead of resizing every reply.
            float L(float old,float next)=>Math.Abs(old-next)<.012f ? old : old+(next-old)*.35f;
            bounds=new LiveCardTracker.Box(L(bounds.Left,target.Left),L(bounds.Top,target.Top),L(bounds.Right,target.Right),L(bounds.Bottom,target.Bottom));
        }
        lastSeen=time;
    }
    public bool TryCrop(int width,int height,out HandCropGeometry.Crop crop)
    {
        float diagonal=(float)Math.Sqrt((double)width*width+(double)height*height);
        crop=default;return bounds.Valid && HandCropGeometry.TryCrop(bounds.Left*diagonal,bounds.Top*diagonal,bounds.Right*diagonal,bounds.Bottom*diagonal,width,height,out crop);
    }
}
