using System;
using System.Collections.Generic;

public static class LiveAbsenceChecks
{
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    static List<FreeCardPlayVerifier.Observation> Hand(bool missing=false,bool wrong=false)
    {
        var list=new List<FreeCardPlayVerifier.Observation>();var keys=new[]{"Y7","G4","G4","R9","B8"};
        for(int i=0;i<keys.Length;i++)if(!missing || i!=0)
            list.Add(new FreeCardPlayVerifier.Observation(i+1,wrong && i==0 ? "R7" : keys[i],.9f,new LiveCardTracker.Box(.20f+i*.04f,.25f,.22f+i*.04f,.28f)));
        return list;
    }
    static FreeCardPlayVerifier Rig()
    {
        var e=new StableHandEvidence();e.Reset(new[]{"Y7","G4","G4","R9","B8"});
        var v=new FreeCardPlayVerifier();v.Reset(e.Current,null);v.Observe(1,0,Hand());return v;
    }
    static GameSessionState Session(string first="Yellow 7")
    {
        var s=new GameSessionState();s.TryMergeLiveHand(new[]{CardEntry.FromLabel(first,.9f,null),CardEntry.FromLabel("Green 4",.9f,null),CardEntry.FromLabel("Green 4",.9f,null),CardEntry.FromLabel("Red 9",.9f,null),CardEntry.FromLabel("Blue 8",.9f,null)},out _);return s;
    }
    public static int Main()
    {
        try
        {
            var v=Rig();
            for(int i=1;i<=10;i++){v.Observe(i+1,i*.2f,Hand(true));Check(!v.TryProve(i*.2f,out _),"A cover shorter than two seconds counted as played.");}
            v.Observe(12,2.2f,Hand(true));
            Check(v.TryProve(2.2f,out var proof) && proof.Key=="Y7" && proof.ReferenceCount==5,"The user's sustained-disappearance policy still required a trajectory/table/palm.");
            Check(v.CandidateTrackId==1 && v.SeedsAfterPlay("Y7").Count==4 && v.SeedsAfterPlay("Y7",true).Count==5,"Missing-card seed removal/Undo lost a second card.");
            var shortCover=Rig();shortCover.Observe(2,.2f,Hand(true));shortCover.Observe(3,.4f,Hand(true));shortCover.Observe(4,.6f,Hand());
            Check(!shortCover.TryProve(.6f,out _),"Recovery from a short cover still removed the card.");
            var oneError=Rig();oneError.Observe(2,.2f,Hand(false,true));oneError.Observe(3,.4f,Hand());
            Check(!oneError.TryProve(.4f,out _),"One wrong class changed the hand.");
            var ids=Rig();var changed=Hand();changed[0]=new FreeCardPlayVerifier.Observation(22,"Y7",.9f,changed[0].Box);
            for(int i=1;i<8;i++){ids.Observe(i+1,i*.2f,changed);Check(!ids.TryProve(i*.2f,out _),"A new detector ID was mistaken for card absence.");}
            var interrupted=Rig();for(int i=1;i<=4;i++)interrupted.Observe(i+1,i*.2f,Hand(true));
            interrupted.Interrupt("Offline");interrupted.Observe(6,1,Hand(true));Check(!interrupted.TryProve(1,out _),"Interrupted frames cast absence votes.");
            Check(!v.Observe(12,2.2f,Hand(true)) && !v.Observe(13,float.NaN,Hand(true)),"Duplicate or invalid replies cast votes.");
            Check(!v.TryProve(6,out _) && !v.TryProve(float.NaN,out _),"Stale observations were committed.");
            var duplicated=Rig();var onlyOne=Hand();onlyOne.RemoveAt(2);
            for(int i=1;i<=11;i++)duplicated.Observe(i+1,i*.2f,onlyOne);
            Check(duplicated.TryProve(2.2f,out var repeated) && repeated.Key=="G4","One of two identical cards could not disappear independently.");
            var session=Session();string id=session.Hand[0].id;int rev=session.Revision;
            session.TrySave(ScanTarget.Discard,new[]{CardEntry.FromLabel("Blue 2",.9f,null)},null,out _);rev=session.Revision;
            Check(!session.TryApplyVerifiedPlay(rev,session.Discard.id,proof,null,null,out _),"The default strict rules transaction changed behaviour.");
            Check(session.TryApplyVerifiedPlay(rev,session.Discard.id,proof,null,null,out _,observationOnly:true),"Rule conflict blocked the explicitly selected observation-only policy.");
            Check(session.Hand.Count==4 && session.Played.Count==1 && session.Played[0].id==id && session.Discard.id==id,"Sustained absence did not move exactly one original card into Discard.");
            Check(!session.TryApplyVerifiedPlay(rev,session.Discard.id,proof,null,null,out _,observationOnly:true),"The same reply committed twice.");
            Check(session.TryUndoPlay() && session.Hand.Count==5 && session.Hand[0].id==id && session.Played.Count==0,"Undo did not restore the exact absent card.");
            var first=Session();Check(first.TryApplyVerifiedPlay(first.Revision,null,proof,null,null,out _,allowUnknownTop:true,observationOnly:true),"No initial top still blocked absence-based play.");
            var wild=Session("Wild");var wp=new LivePlayVerifier.Proof("WC",5,6,6,1);
            Check(wild.TryApplyVerifiedPlay(wild.Revision,null,wp,null,null,out _,allowUnknownTop:true,observationOnly:true) && wild.Hand.Count==4 && wild.ActiveColor==null,"Wild disappearance did not immediately move to Discard.");
            Check(wild.TrySetLiveWildColor("Green") && wild.ActiveColor=="Green" && wild.CanUndoPlay,"Post-play colour selection lost Undo.");
            Check(wild.TryUndoPlay() && wild.Discard==null && wild.Hand.Count==5,"Wild Undo did not restore the earlier state.");
            Console.WriteLine("Live disappearance policy checks passed: "+checks);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
