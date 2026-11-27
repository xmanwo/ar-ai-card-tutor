using System;
using System.Collections.Generic;

public static class StableHandEvidenceChecks
{
    // User-supplied physical hand, 2026-11-25. A test fixture, never an inference input.
    static readonly string[] Hand={"YR","W4","YA2","WC","GS","G3","G4","R3","B5"};
    static int checks;
    static void Check(bool value,string message) { if (!value) throw new Exception(message); checks++; }
    static List<StableHandEvidence.Sample> Samples(string[] keys=null,float confidence=.9f)
    {
        var result=new List<StableHandEvidence.Sample>();
        foreach (string key in keys ?? Hand) result.Add(new StableHandEvidence.Sample(key,confidence));
        return result;
    }
    static int Count(IReadOnlyList<StableHandEvidence.CardCount> values,string key)
    { foreach (var item in values) if (item.Key==key) return item.Count; return 0; }
    static void Feed(StableHandEvidence evidence,IReadOnlyList<StableHandEvidence.Sample> observations,int start=1,int count=5,float time=0)
    { for (int i=0;i<count;i++) evidence.Update(start+i,time+i*.3f,observations); }
    public static int Main()
    {
        try { Run(); Console.WriteLine("Stable hand/reconnect checks passed: "+checks); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    static void Run()
    {
        var evidence=new StableHandEvidence();
        Feed(evidence,Samples());
        Check(evidence.Current.HasConsensus,"A repeated high-confidence hand should produce consensus.");
        Check(!evidence.Current.HasReference && evidence.Current.ReferenceCount==0,"Detection consensus must not invent a confirmed reference.");
        Check(evidence.Current.State==StableHandEvidence.Phase.NeedsReference,"Initial confirmation must be requested.");
        evidence.Reset(Hand);
        Check(evidence.Current.ReferenceCount==9 && evidence.Current.ObservedCount==-1,"Reference must be available before any new observation.");
        evidence.Update(1,0,Samples());
        Check(!evidence.Current.HasConsensus,"One observation cannot be stable evidence.");
        evidence.Update(2,.3f,Samples());evidence.Update(3,.6f,Samples());
        Check(!evidence.Current.HasConsensus,"Three observations cannot pass the four-agreement threshold.");
        evidence.Update(4,.9f,Samples());
        Check(evidence.Current.State==StableHandEvidence.Phase.Matches,"Four sufficiently separated agreeing observations should match.");
        var saved=evidence.Current;
        var referenceCopy=new List<string>(Hand);evidence.Reset(referenceCopy);referenceCopy[0]="R9";
        Check(Count(evidence.Current.Reference,"YR")==1 && Count(evidence.Current.Reference,"R9")==0,"Reference retained a mutable input alias.");
        Check(saved.HasConsensus && saved.ReferenceCount==9,"Published snapshots must remain immutable.");

        var wrong=Samples();wrong[6]=new StableHandEvidence.Sample("G9",.99f);
        var missing=Samples();missing.RemoveAt(6);
        var extra=Samples();extra.Add(new StableHandEvidence.Sample("B8",.99f));
        var weak=Samples();weak[6]=new StableHandEvidence.Sample("G9",.3f);
        var invalid=Samples();invalid[6]=new StableHandEvidence.Sample("Unknown",float.NaN);
        var errors=new List<IReadOnlyList<StableHandEvidence.Sample>>{wrong,missing,extra,weak,invalid,new List<StableHandEvidence.Sample>()};
        // Inject exactly one bad reply at every position in the five-reply window.
        foreach (var error in errors)
            for (int position=0;position<5;position++)
            {
                evidence.Reset(Hand);
                for (int frame=0;frame<5;frame++)
                {
                    evidence.Update(frame+1,frame*.3f,frame==position ? error : Samples());
                    Check(evidence.Current.ReferenceCount==9 && Count(evidence.Current.Reference,"G4")==1 &&
                        Count(evidence.Current.Reference,"G9")==0,"An isolated error changed the confirmed reference.");
                    Check(evidence.Current.State!=StableHandEvidence.Phase.ChangedCards &&
                        evidence.Current.State!=StableHandEvidence.Phase.MissingCards &&
                        evidence.Current.State!=StableHandEvidence.Phase.ExtraCards,"A single error was promoted to a persistent hand change.");
                }
                Check(evidence.Current.HasConsensus,"One bad frame should not suppress four agreeing good frames.");
                Check(evidence.Current.CandidateCount==9 && Count(evidence.Current.Candidate,"G4")==1,"Consensus accepted an isolated false label/count.");
            }
        evidence.Reset(Hand);Feed(evidence,wrong);
        Check(evidence.Current.State==StableHandEvidence.Phase.ChangedCards,"A repeated label conflict should request review.");
        Check(Count(evidence.Current.Reference,"G4")==1 && Count(evidence.Current.Candidate,"G9")==1,"Conflict must preserve both reference and observed evidence.");
        evidence.Reset(Hand);Feed(evidence,missing);
        Check(evidence.Current.State==StableHandEvidence.Phase.MissingCards && evidence.Current.ReferenceCount==9,"Persistent occlusion must not delete a card.");
        evidence.Reset(Hand);Feed(evidence,extra);
        Check(evidence.Current.State==StableHandEvidence.Phase.ExtraCards && evidence.Current.ReferenceCount==9,"A persistent extra detection must not add a confirmed card.");
        evidence.Reset(Hand);Feed(evidence,new List<StableHandEvidence.Sample>());
        Check(evidence.Current.State==StableHandEvidence.Phase.Occluded && !evidence.Current.HasConsensus,"Empty observations cannot confirm an empty hand.");
        Check(evidence.Current.ReferenceCount==9,"Full hand occlusion erased the reference.");
        evidence.Reset(Hand);Feed(evidence,Samples(confidence:.3f));
        Check(!evidence.Current.HasConsensus && evidence.Current.State==StableHandEvidence.Phase.Uncertain,"Low-confidence repetitions must not count as reliable agreement.");
        evidence.Reset(Hand);Feed(evidence,null);
        Check(evidence.Current.ObservedCount==-1 && !evidence.Current.HasConsensus,"Null results must mean unknown, never zero cards.");
        evidence.Reset(new[]{"Y7","Y7","B5"});Feed(evidence,Samples(new[]{"Y7","B5","Y7"}));
        Check(evidence.Current.State==StableHandEvidence.Phase.Matches && Count(evidence.Current.Reference,"Y7")==2,"Repeated identical cards were merged, or result ordering affected consensus.");
        evidence.Reset(Hand);Feed(evidence,Samples());
        var before=evidence.Current;
        Check(!evidence.Update(5,2,wrong) && !evidence.Update(4,2,wrong),"Repeated or older replies must be ignored.");
        Check(!evidence.Update(6,.2f,wrong) && !evidence.Update(6,float.NaN,wrong),"Invalid/backward capture times must be ignored.");
        Check(ReferenceEquals(before,evidence.Current),"Rejected replies mutated evidence.");
        evidence.Update(6,10,Samples());
        Check(evidence.Current.WindowFrames==1 && !evidence.Current.HasConsensus,"Stale pre-gap frames voted after a long interruption.");
        evidence.Reset(Hand);
        for (int i=1;i<=5;i++) evidence.Update(i,i*.01f,Samples());
        Check(!evidence.Current.HasConsensus,"A burst of near-identical timestamps bypassed the minimum evidence span.");
        evidence.Reset(Hand);Feed(evidence,Samples());evidence.MarkUnavailable();
        Check(evidence.Current.State==StableHandEvidence.Phase.Unavailable && evidence.Current.ReferenceCount==9 &&
            evidence.Current.ObservedCount==-1 && !evidence.Current.HasConsensus,"A disconnect was treated as an empty/new hand.");
        evidence.Update(6,1.5f,Samples());
        Check(evidence.Current.WindowFrames==1 && !evidence.Current.HasConsensus,"Pre-disconnect agreement was reused after reconnecting.");
        Feed(evidence,Samples(),7,3,1.8f);
        Check(evidence.Current.State==StableHandEvidence.Phase.Matches,"Fresh evidence did not recover after reconnecting.");
        evidence.Reset(new[]{"G4","invalid"});
        Check(!evidence.Current.HasReference,"A partly invalid reference must not be silently truncated.");
        var tooMany=Samples();for(int i=0;i<65;i++)tooMany.Add(new StableHandEvidence.Sample("R1",.9f));
        evidence.Reset(Hand);Feed(evidence,tooMany);
        Check(!evidence.Current.HasConsensus && evidence.Current.State==StableHandEvidence.Phase.Uncertain,"Over-capacity results must not be promoted.");
        foreach(string key in Hand) Check(StableHandEvidence.ValidKey(key),"Valid physical UNO card rejected: "+key);
        Check(!StableHandEvidence.ValidKey("Z3") && !StableHandEvidence.ValidKey("R99") && !StableHandEvidence.ValidKey(null),"Invalid canonical key accepted.");

        var retry=new LiveRetryPolicy();float now=10;
        Check(retry.CanAttempt(now),"Initial request cannot run.");
        foreach(float delay in new[]{1f,2f,4f,5f,5f})
        {
            retry.Fail(now);
            Check(Math.Abs(retry.Remaining(now)-delay)<.001f,"Retry backoff is incorrect.");
            Check(!retry.CanAttempt(now+.1f) && retry.CanAttempt(now+delay),"Retry ignored its deadline.");
            now+=delay;
        }
        Check(retry.Failures==4,"Retry exponent/counter should remain bounded.");
        retry.Reset();Check(retry.Failures==0 && retry.CanAttempt(now),"Successful reply must reset the reconnect delay.");
    }
}
