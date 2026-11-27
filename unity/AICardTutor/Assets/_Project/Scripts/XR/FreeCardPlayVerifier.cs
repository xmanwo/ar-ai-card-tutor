using System;
using System.Collections.Generic;

/// <summary>User-selected policy: sustained absence from the observed hand counts as played.
/// No trajectory, outward steps, table reacquisition, hand joints or release gesture is required.</summary>
public sealed class FreeCardPlayVerifier
{
    public readonly struct Observation
    {
        public readonly int TrackId;public readonly string Key;public readonly float Confidence;public readonly LiveCardTracker.Box Box;
        public Observation(int id,string key,float confidence,LiveCardTracker.Box box){TrackId=id;Key=key;Confidence=confidence;Box=box;}
    }
    public readonly struct Seed
    {
        public readonly string Key;public readonly LiveCardTracker.Box Box;
        public Seed(string key,LiveCardTracker.Box box){Key=key;Box=box;}
    }
    public readonly struct Point
    {
        public readonly float X,Y;public Point(float x,float y){X=x;Y=y;}
    }
    sealed class Member
    {
        public string Key;public int Track=-1;
        public LiveCardTracker.Box Box;
        public float LastSeen=-1,MissingSince=-1;
        public bool Present;
        public readonly List<bool> Recent=new List<bool>();
    }
    readonly List<Member> members=new List<Member>();
    int frame=-1,visible;float lastTime=-1;
    public const float MissingSeconds=2f;
    public const int MissingFrames=4;
    public string Message { get; private set; }="Show your hand; Live learns it automatically.";
    public bool HasSeenReferenceHand { get; private set; }
    public int VisibleCount=>visible;
    public int CandidateTrackId { get; private set; }
    public bool IsMemberTrack(int id){foreach(var m in members)if(m.Track==id)return true;return false;}
    public bool IsHeldTrack(int id){foreach(var m in members)if(m.Track==id)return m.Present;return false;}
    public void Reset(StableHandEvidence.Snapshot hand,string unusedTop,IReadOnlyList<Seed> seeds=null)
    {
        members.Clear();frame=-1;lastTime=-1;visible=0;CandidateTrackId=0;HasSeenReferenceHand=false;
        var used=new HashSet<int>();
        foreach(var entry in hand.Reference)for(int count=0;count<entry.Count;count++)
        {
            var m=new Member{Key=entry.Key};
            if(seeds!=null)for(int i=0;i<seeds.Count;i++)if(!used.Contains(i) && seeds[i].Key==m.Key)
            {m.Box=seeds[i].Box;used.Add(i);break;}
            members.Add(m);
        }
        Message=$"Live hand ready; a card missing for {MissingSeconds:0.#}s is moved to Discard.";
    }
    public void Interrupt(string reason)
    {
        foreach(var m in members){m.Recent.Clear();m.MissingSince=-1;}Message=reason;
    }
    public bool Observe(int id,float time,IReadOnlyList<Observation> input,Point? unusedPalm=null)
    {
        if(id<=frame || !Finite(time) || time<0 || time<lastTime)return false;
        if(lastTime>=0 && time-lastTime>3)Interrupt("Waiting for fresh hand observations.");
        frame=id;lastTime=time;CandidateTrackId=0;visible=0;
        var valid=new List<Observation>();var ids=new HashSet<int>();
        if(input!=null)foreach(var o in input)
            if(o.TrackId>0 && ids.Add(o.TrackId) && o.Box.Valid && StableHandEvidence.ValidKey(o.Key) &&
                Finite(o.Confidence) && o.Confidence>=.45f && o.Confidence<=1)
            {valid.Add(o);if(valid.Count>=64)break;}
        var matched=new Dictionary<Member,Observation>();var used=new HashSet<int>();
        // Reserve known IDs first so an identical-looking card cannot steal its neighbour's observation.
        foreach(var m in members)foreach(var o in valid)
            if(o.TrackId==m.Track && o.Key==m.Key && used.Add(o.TrackId)){matched[m]=o;break;}
        foreach(var m in members)
        {
            if(matched.ContainsKey(m))continue;
            float best=float.MaxValue;Observation chosen=default;bool found=false;
            foreach(var o in valid)
            {
                if(o.Key!=m.Key || used.Contains(o.TrackId))continue;
                float distance=m.Box.Valid ? Distance(m.Box.X,m.Box.Y,o.Box.X,o.Box.Y) : 0;
                if(distance>=best)continue;chosen=o;best=distance;found=true;
            }
            if(found){matched[m]=chosen;used.Add(chosen.TrackId);}
        }
        foreach(var m in members)
        {
            m.Present=matched.TryGetValue(m,out var o);
            if(m.Present){m.Track=o.TrackId;m.Box=o.Box;m.LastSeen=time;m.MissingSince=-1;visible++;}
            else if(m.MissingSince<0)m.MissingSince=time;
            m.Recent.Add(m.Present);while(m.Recent.Count>5)m.Recent.RemoveAt(0);
        }
        if(visible==members.Count && members.Count>0)HasSeenReferenceHand=true;
        return true;
    }
    bool Missing(Member m,float now)
    {
        if(m.Present || m.MissingSince<0 || now-m.MissingSince+0.0001f<MissingSeconds)return false;
        int absent=0;foreach(bool seen in m.Recent)if(!seen)absent++;
        return absent>=MissingFrames;
    }
    public bool TryProve(float now,out LivePlayVerifier.Proof proof)
    {
        proof=null;if(!Finite(now) || now<lastTime || lastTime<0 || now-lastTime>3)return false;
        if(members.Count==0){Message="Show your hand; Live learns it automatically.";return false;}
        Member selected=null;
        foreach(var m in members)if(Missing(m,now) && (selected==null || m.MissingSince<selected.MissingSince))selected=m;
        if(selected==null)
        {
            Message=visible<members.Count ? $"Hand {visible}/{members.Count} visible; checking absence for {MissingSeconds:0.#}s." : "Live hand stable; missing cards will move to Discard.";
            return false;
        }
        CandidateTrackId=selected.Track;
        proof=new LivePlayVerifier.Proof(selected.Key,members.Count,frame,frame,lastTime);
        Message="Sustained hand-card absence verified.";return true;
    }
    public string MissingCandidate(float now)
    {foreach(var m in members)if(!m.Present && m.MissingSince>=0 && now-m.MissingSince>=.2f)return m.Key;return null;}
    public List<Seed> SeedsAfterPlay(string removedKey,bool restore=false)
    {
        var result=new List<Seed>();bool removed=false;
        foreach(var m in members)
        {
            if(!restore && !removed && m.Key==removedKey && !m.Present){removed=true;continue;}
            result.Add(new Seed(m.Key,m.Box));
        }
        return result;
    }
    public bool TrySearchExtent(out LiveCardTracker.Box extent)
    {
        float l=1,t=1,r=0,b=0;bool any=false;
        foreach(var m in members)if(m.Present && m.Box.Valid)
        {any=true;l=Math.Min(l,m.Box.Left);t=Math.Min(t,m.Box.Top);r=Math.Max(r,m.Box.Right);b=Math.Max(b,m.Box.Bottom);}
        extent=any ? new LiveCardTracker.Box(l-.1f,t-.1f,r+.1f,b+.1f) : default;return any;
    }
    public string Diagnostics(float now)
    {
        string text=$"absence policy; visible={visible}/{members.Count}; frame={frame}; reply age={Math.Max(0,now-lastTime):F1}s";
        foreach(var m in members){int misses=0;foreach(bool seen in m.Recent)if(!seen)misses++;
            text+=$" {m.Key}#{m.Track}:present={m.Present},missing={Math.Max(0,m.MissingSince<0 ? 0 : now-m.MissingSince):F1}s,votes={misses}/{m.Recent.Count}";}
        return text;
    }
    static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    static float Distance(float x,float y,float a,float b)=>(float)Math.Sqrt((x-a)*(x-a)+(y-b)*(y-b));
}
