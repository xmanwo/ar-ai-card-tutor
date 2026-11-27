using System;
using System.Collections.Generic;
using System.Text;

/// <summary>Read-only evidence against a manually confirmed hand. Never edits game state.</summary>
public sealed class StableHandEvidence
{
    public enum Phase { NeedsReference, Checking, Matches, MissingCards, ExtraCards, ChangedCards, Occluded, Uncertain, Unavailable }
    public readonly struct Sample
    {
        public readonly string Key;
        public readonly float Confidence;
        public Sample(string key,float confidence) { Key=key; Confidence=confidence; }
    }
    public readonly struct CardCount
    {
        public readonly string Key;
        public readonly int Count;
        public CardCount(string key,int count) { Key=key; Count=count; }
    }
    public sealed class Snapshot
    {
        public readonly IReadOnlyList<CardCount> Reference, Candidate;
        public readonly bool HasReference, HasConsensus;
        public readonly int ReferenceCount, ObservedCount, CandidateCount, AgreeingFrames, WindowFrames;
        public readonly Phase State;
        public Snapshot(IReadOnlyList<CardCount> reference,bool hasReference,IReadOnlyList<CardCount> candidate,
            bool consensus,int observed,int agreements,int frames,Phase state)
        {
            Reference=reference; HasReference=hasReference; Candidate=candidate; HasConsensus=consensus;
            ReferenceCount=Total(reference); CandidateCount=Total(candidate); ObservedCount=observed;
            AgreeingFrames=agreements; WindowFrames=frames; State=state;
        }
    }
    sealed class Frame
    {
        public float Time;
        public string Signature;
        public bool Eligible;
        public IReadOnlyList<CardCount> Counts;
    }
    public const int WindowSize=5, RequiredAgreements=4, Capacity=64;
    public const float MinimumSpan=.6f, MaximumWindowSeconds=2f, MinimumConfidence=.45f;
    static readonly IReadOnlyList<CardCount> Empty=Array.Empty<CardCount>();
    readonly List<Frame> frames=new List<Frame>();
    IReadOnlyList<CardCount> reference=Empty;
    bool hasReference;
    string referenceSignature="";
    int lastFrame=-1;
    float lastTime=-1;
    public Snapshot Current { get; private set; }

    public StableHandEvidence() { Reset(null); }

    /// <param name="confirmedKeys">Canonical UnoCardKey keys; null means no confirmed reference.</param>
    public void Reset(IEnumerable<string> confirmedKeys)
    {
        frames.Clear(); lastFrame=-1; lastTime=-1;
        var counts=new SortedDictionary<string,int>(StringComparer.Ordinal);
        hasReference=confirmedKeys!=null;
        if (confirmedKeys!=null)
            foreach (string key in confirmedKeys)
            {
                if (!ValidKey(key)) { hasReference=false; continue; }
                counts.TryGetValue(key,out int count); counts[key]=count+1;
            }
        reference=hasReference ? Freeze(counts) : Empty;
        referenceSignature=Signature(reference);
        Current=new Snapshot(reference,hasReference,Empty,false,-1,0,0,
            hasReference ? Phase.Checking : Phase.NeedsReference);
    }

    /// <summary>Only independent raw replies vote. Tracker predictions and smoothed labels never vote.</summary>
    public bool Update(int frameId,float time,IReadOnlyList<Sample> observations,bool allowEmpty=false)
    {
        if (frameId<=lastFrame || !Finite(time) || time<0 || time<lastTime) return false;
        if (lastTime>=0 && time-lastTime>MaximumWindowSeconds) frames.Clear();
        lastFrame=frameId; lastTime=time;
        var counts=new SortedDictionary<string,int>(StringComparer.Ordinal);
        bool eligible=observations!=null && observations.Count<=Capacity;
        if (observations!=null)
            for (int i=0;i<Math.Min(observations.Count,Capacity);i++)
            {
                var sample=observations[i];
                if (!ValidKey(sample.Key) || !Finite(sample.Confidence) || sample.Confidence<0 || sample.Confidence>1)
                { eligible=false; continue; }
                counts.TryGetValue(sample.Key,out int count); counts[sample.Key]=count+1;
                if (sample.Confidence<MinimumConfidence) eligible=false;
            }
        var current=new Frame { Time=time, Counts=Freeze(counts), Eligible=eligible };
        current.Signature=Signature(current.Counts);
        frames.Add(current);
        while (frames.Count>WindowSize || frames.Count>0 && time-frames[0].Time>MaximumWindowSeconds)
            frames.RemoveAt(0);
        Frame winner=null; int agreements=0; bool consensus=false;
        foreach (var candidate in frames)
        {
            if (!candidate.Eligible || candidate.Counts.Count==0 && !allowEmpty) continue;
            int support=0; float first=time,last=0;
            foreach (var item in frames)
                if (item.Eligible && item.Signature==candidate.Signature)
                { support++; first=Math.Min(first,item.Time); last=Math.Max(last,item.Time); }
            if (support<=agreements) continue;
            winner=candidate; agreements=support;
            consensus=support>=RequiredAgreements && last-first>=MinimumSpan;
        }
        var stable=consensus ? winner.Counts : Empty;
        int observed=observations==null ? -1 : Total(current.Counts);
        Phase phase;
        if (!hasReference) phase=Phase.NeedsReference;
        else if (observed==0 && eligible) phase=Phase.Occluded;
        else if (!eligible) phase=Phase.Uncertain;
        else if (!consensus || winner.Signature!=current.Signature) phase=Phase.Checking;
        else if (winner.Signature==referenceSignature) phase=Phase.Matches;
        else if (Total(stable)<Total(reference)) phase=Phase.MissingCards;
        else if (Total(stable)>Total(reference)) phase=Phase.ExtraCards;
        else phase=Phase.ChangedCards;
        Current=new Snapshot(reference,hasReference,stable,consensus,observed,agreements,frames.Count,phase);
        return true;
    }

    public void MarkUnavailable()
    {
        // A failure is unknown evidence, never an observation of an empty hand.
        frames.Clear();
        Current=new Snapshot(reference,hasReference,Empty,false,-1,0,0,Phase.Unavailable);
    }

    static IReadOnlyList<CardCount> Freeze(SortedDictionary<string,int> counts)
    {
        var list=new List<CardCount>();
        foreach (var pair in counts) list.Add(new CardCount(pair.Key,pair.Value));
        return list.AsReadOnly();
    }
    static string Signature(IReadOnlyList<CardCount> counts)
    {
        var text=new StringBuilder();
        foreach (var card in counts) text.Append(card.Key).Append(':').Append(card.Count).Append(';');
        return text.ToString();
    }
    static int Total(IReadOnlyList<CardCount> counts)
    { int total=0; foreach (var card in counts) total+=card.Count; return total; }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool ValidKey(string key)
    {
        if (key=="WC" || key=="W4") return true;
        if (string.IsNullOrEmpty(key) || key.Length<2 || "RGBY".IndexOf(key[0])<0) return false;
        return key.Length==2 && (key[1]>='0' && key[1]<='9' || key[1]=='S' || key[1]=='R') ||
            key.Length==3 && key.Substring(1)=="A2";
    }
    public static string ShortName(string key) => key=="WC" ? "Wild" : key=="W4" ? "Wild+4" : key.Replace("A2","+2");
}

/// <summary>Bounded reconnect delays; one outstanding capture/request is still enforced by the client.</summary>
public sealed class LiveRetryPolicy
{
    public int Failures { get; private set; }
    public float NextAttemptAt { get; private set; }
    public void Reset() { Failures=0; NextAttemptAt=0; }
    public bool CanAttempt(float now) => now>=NextAttemptAt;
    public float Remaining(float now) => Math.Max(0,NextAttemptAt-now);
    public void Fail(float now)
    {
        Failures=Math.Min(4,Failures+1);
        NextAttemptAt=now+Math.Min(5,1<<(Failures-1));
    }
}
