// Standalone checks of production sources. LIVE_PLAY_CHECKS supplies only unused Unity base types/attributes.
using System;
using System.Collections.Generic;
#if LIVE_PLAY_CHECKS
namespace UnityEngine
{
    public class MonoBehaviour {}
    public class ScriptableObject {}
    public class HeaderAttribute:Attribute { public HeaderAttribute(string value){} }
    public class TooltipAttribute:Attribute { public TooltipAttribute(string value){} }
    public class CreateAssetMenuAttribute:Attribute { public string fileName,menuName; }
}
public class Detection { public string label;public float confidence;public float[] bbox_xyxy; }
#endif

public static class LivePlayChecks
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static string Key(ConfirmedCard card)=>UnoCardKey.For(card.color,card.value);
    static GameSessionState Session(string[] hand,string top)
    {
        var session=new GameSessionState();var entries=new List<CardEntry>();
        foreach(string label in hand)entries.Add(CardEntry.FromLabel(label,.9f,new float[]{10,10,40,50}));
        Check(session.TrySave(ScanTarget.Hand,entries,null,out var error),"Hand fixture: "+error);
        Check(session.TrySave(ScanTarget.Discard,new[]{CardEntry.FromLabel(top,.9f,null)},"Red",out error),"Top fixture: "+error);
        return session;
    }
    sealed class Rig
    {
        public StableHandEvidence Evidence=new StableHandEvidence();
        public LivePlayVerifier Verifier=new LivePlayVerifier();
        public List<StableHandEvidence.Sample> Full=new List<StableHandEvidence.Sample>(),Reduced;
        public string Missing,Top;
        public Rig(GameSessionState session,string missing,bool handBaseline=true,bool tableBaseline=true,bool released=true)
        {
            Missing=missing;Top=Key(session.Discard);var keys=new List<string>();
            foreach(var card in session.Hand){keys.Add(Key(card));Full.Add(new StableHandEvidence.Sample(Key(card),.9f));}
            Evidence.Reset(keys);Verifier.Reset(Evidence.Current,Top);Reduced=new List<StableHandEvidence.Sample>(Full);
            int index=Reduced.FindIndex(item=>item.Key==missing);if(index>=0)Reduced.RemoveAt(index);
            if(handBaseline)for(int i=0;i<4;i++)Hand(i+1,i*.3f,Full,true);
            if(tableBaseline){Table(101,0,Top);Table(102,.5f,Top);}
            if(released)Verifier.ObserveRelease(1.1f);
        }
        public void Hand(int id,float time,IReadOnlyList<StableHandEvidence.Sample> samples,bool safe=true)
        {Evidence.Update(id,time,samples,true);Verifier.ObserveHand(id,time,Evidence.Current,safe);}
        public void Table(int id,float time,string key,float confidence=.9f,bool safe=true,float x=.5f)
        {Verifier.ObserveTable(id,time,key,confidence,x,.5f,safe);}
        public void Reduction(bool safe=true)
        {for(int i=0;i<4;i++)Hand(i+5,1.2f+i*.3f,Reduced,safe);}
        public void NewTop(string key=null,bool safe=true,float confidence=.9f,bool moving=false)
        {for(int i=0;i<4;i++)Table(103+i,1.2f+i*.4f,key ?? Missing,confidence,safe,moving && i==3 ? .8f : .5f);}
        public LivePlayVerifier.Proof Proof(float now=2.4f)
        {Verifier.TryProve(now,out var proof);return proof;}
    }
    static void Run()
    {
        Check(LivePlayVerifier.PlayingHandOpen(new[]{.06f,.08f,.09f,.1f}),"An open playing hand was rejected.");
        Check(!LivePlayVerifier.PlayingHandOpen(new[]{.02f,.08f,.09f,.1f}),"An index/thumb pinch was considered released.");
        Check(!LivePlayVerifier.PlayingHandOpen(new[]{.06f,.02f,.09f,.1f}),"A middle/thumb grip was considered released.");
        Check(!LivePlayVerifier.PlayingHandOpen(new[]{.06f,.08f,.02f,.1f}),"A ring/thumb grip was considered released.");
        Check(!LivePlayVerifier.PlayingHandOpen(new[]{.06f,.08f,.09f,.02f}),"A little-finger/thumb grip was considered released.");
        Check(!LivePlayVerifier.PlayingHandOpen(new[]{float.NaN,.08f,.09f,.1f}),"Unknown finger separation was considered released.");
        Check(!LivePlayVerifier.PlayingHandOpen(new[]{float.PositiveInfinity,.08f,.09f,.1f}),"Invalid finger separation was considered released.");
        Check(!LivePlayVerifier.PlayingHandOpen(null) && !LivePlayVerifier.PlayingHandOpen(new[]{.1f,.1f}),"Missing hand joints were considered released.");
        var release=new LiveReleaseTracker();
        release.Update(0,0,true,false,false,true,true);
        Check(release.ReleasedAt<0,"An already-open hand fabricated a release gesture.");
        release.Update(0,.1f,true,true,true,false,false);release.Update(0,.3f,true,false,false,true,true);
        Check(release.ReleasedAt==.3f,"A near-table pinch followed by open/clear was not recorded.");
        release.Update(0,1,true,false,false,true,true);
        Check(release.ReleasedAt==.3f,"Remaining open renewed an old release timestamp.");
        release.Update(0,1.1f,true,true,true,false,false);
        Check(release.ReleasedAt<0,"A new pinch retained an older release.");
        release.Update(0,1.2f,true,true,false,true,false);
        Check(release.ReleasedAt<0,"Opening over the table counted as clearing the table.");
        release.Update(0,1.3f,false,false,false,true,true);release.Update(0,1.4f,true,false,false,true,true);
        Check(release.ReleasedAt<0,"Untracked joints bridged a pinch/release gesture.");
        release.Reset();release.Update(0,0,true,true,true,false,false);release.Update(0,5,true,false,false,true,true);
        Check(release.ReleasedAt<0,"A stale pinch was reused as a current release.");
        release.Reset();release.Update(0,0,true,true,true,false,false);release.Update(1,.3f,true,false,false,true,true);
        Check(release.ReleasedAt<0,"Switching holding hands reused the other hand's gesture.");
        var session=Session(new[]{"Blue 5","Green 4","Green 4"},"Blue 3");
        var rig=new Rig(session,"B5");rig.Reduction();
        Check(rig.Proof()==null,"A legal missing card without a new table card counted as played.");
        rig.NewTop();var proof=rig.Proof();Check(proof!=null && proof.Key=="B5","Independent hand/table evidence failed to verify a valid play.");
        string originalTop=session.Discard.id,remainingId=session.Hand[1].id,playedId=session.Hand[0].id;
        int revision=session.Revision;
        Check(session.TryApplyVerifiedPlay(revision,originalTop,proof,null,null,out var error),"Valid play failed: "+error);
        Check(session.Hand.Count==2 && session.Hand[0].id==remainingId && session.Discard.id==playedId,"A play regenerated identities or removed the wrong card.");
        Check(session.Played.Count==1 && session.Played[0].id==playedId && session.ActiveColor=="Blue","Played history/top/active colour were not committed together.");
        Check(!session.TryApplyVerifiedPlay(revision,originalTop,proof,null,null,out error),"Duplicate result committed the play twice.");
        Check(!session.TryApplyVerifiedPlay(session.Revision,session.Discard.id,proof,null,null,out error),"Old proof was applied against a different hand.");
        Check(session.TryUndoPlay() && session.Hand.Count==3 && session.Hand[0].id==playedId && session.Discard.id==originalTop && session.Played.Count==0,"Undo failed to restore the exact session.");
        Check(!session.TryUndoPlay(),"An undo was applied twice.");

        for(int bad=0;bad<4;bad++)
        {
            session=Session(new[]{"Blue 5","Green 4"},"Blue 3");rig=new Rig(session,"B5");rig.Reduction();
            for(int i=0;i<4;i++)rig.Table(103+i,1.2f+i*.4f,i==bad ? "B5" : "G4");
            Check(rig.Proof()==null,"One matching table hallucination confirmed a play.");
        }
        session=Session(new[]{"Blue 5","Green 4"},"Blue 3");
        rig=new Rig(session,"B5");rig.NewTop();rig.Hand(5,1.2f,rig.Reduced);
        Check(rig.Proof()==null,"One missing-hand observation confirmed a play.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop("G4");Check(rig.Proof()==null,"Different top card falsely verified the missing card.");
        rig=new Rig(session,"B5");rig.Reduction(false);rig.NewTop();Check(rig.Proof()==null,"Overlapping/unknown hand region counted as a play.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop(safe:false);Check(rig.Proof()==null,"A hand over the table counted as a settled played card.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop(confidence:.59f);Check(rig.Proof()==null,"Weak table observations confirmed a play.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop(moving:true);Check(rig.Proof()==null,"A moving/held card counted as settled.");
        rig=new Rig(session,"B5",handBaseline:false);rig.Reduction();rig.NewTop();Check(rig.Proof()==null,"No original hand was seen before a play was inferred.");
        rig=new Rig(session,"B5",tableBaseline:false);rig.Reduction();rig.NewTop();Check(rig.Proof()==null,"No old top was seen before a table transition was inferred.");
        rig=new Rig(session,"B5",released:false);rig.Reduction();rig.NewTop();
        Check(rig.Proof()==null,"Another player's table change plus a hand miss counted as our play without a release gesture.");
        rig.Verifier.ObserveRelease(.1f);Check(rig.Proof()==null,"A gesture predating the old table observation confirmed a play.");
        rig.Verifier.ObserveRelease(5f);Check(rig.Proof()==null,"A future gesture confirmed a current play.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop();rig.Verifier.ObserveRelease(-1);
        Check(rig.Proof()==null,"A gesture invalidated by unknown joints remained actionable.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop();Check(rig.Proof(20)==null,"Stale evidence confirmed a current play.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop();rig.Hand(9,2.45f,Array.Empty<StableHandEvidence.Sample>());
        Check(rig.Proof(2.5f)==null,"A newly occluded hand reused a previous smaller consensus as current evidence.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop();rig.Verifier.Interrupt("Camera moved");
        Check(rig.Proof()==null,"Interrupted evidence retained an actionable proof.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop();
        rig.Table(200,10,"B5");rig.Hand(10,10,rig.Reduced);Check(rig.Proof(10)==null,"Old table baseline survived a large gap.");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop();
        rig.Table(106,2.4f,"G4");rig.Hand(8,2.1f,rig.Full);Check(rig.Proof()!=null,"Duplicate frame replies altered acknowledged evidence.");

        session=Session(new[]{"Blue 5","Blue 5","Green 4"},"Blue 5");
        rig=new Rig(session,"B5");rig.Reduction();rig.NewTop();Check(rig.Proof()==null,"An identical old/new top was treated as a proven transition.");
        session=Session(new[]{"Blue 5","Green 4","Red 3"},"Blue 3");
        rig=new Rig(session,"B5");rig.Reduced.RemoveAt(0);rig.Reduction();rig.NewTop();Check(rig.Proof()==null,"A two-card reduction counted as a single play.");
        rig=new Rig(session,"B5");rig.Reduced.Add(new StableHandEvidence.Sample("R9",.9f));rig.Reduction();rig.NewTop();
        Check(rig.Proof()==null,"A label replacement counted as a missing-card play.");

        session=Session(new[]{"Green 4","Blue 5"},"Blue 3");rig=new Rig(session,"G4");rig.Reduction();rig.NewTop();proof=rig.Proof();
        Check(proof!=null,"Visual evidence should remain independent of legality.");
        revision=session.Revision;originalTop=session.Discard.id;
        Check(!session.TryApplyVerifiedPlay(revision,originalTop,proof,null,null,out error) && error.Contains("NoMatch"),"UNO colour/value check did not block an illegal auto update.");
        Check(session.Revision==revision && session.Hand.Count==2 && session.Discard.id==originalTop && session.Played.Count==0,"Rejected play partly mutated the session.");
        session=Session(new[]{"Green 4","Green 4","Blue 5"},"Blue 4");rig=new Rig(session,"G4");rig.Reduction();rig.NewTop();proof=rig.Proof();
        string secondCopy=session.Hand[1].id;
        Check(session.TryApplyVerifiedPlay(session.Revision,session.Discard.id,proof,null,null,out error),"Number-match play failed.");
        Check(session.Hand.Count==2 && session.Hand[0].id==secondCopy && Key(session.Hand[0])=="G4","A duplicate-label play removed both copies.");

        session=Session(new[]{"Wild","Red 3"},"Blue 5");rig=new Rig(session,"WC");rig.Reduction();rig.NewTop();proof=rig.Proof();
        revision=session.Revision;
        Check(!session.TryApplyVerifiedPlay(revision,session.Discard.id,proof,null,null,out error) && session.Revision==revision,"A Wild auto-chose an active colour.");
        Check(session.TryApplyVerifiedPlay(revision,session.Discard.id,proof,"Green",null,out error) && session.ActiveColor=="Green","Explicit Wild colour was not applied.");
        session=Session(new[]{"Wild Draw 4","Blue 5"},"Blue 3");rig=new Rig(session,"W4");rig.Reduction();rig.NewTop();proof=rig.Proof();
        Check(!session.TryApplyVerifiedPlay(session.Revision,session.Discard.id,proof,"Green",null,out error) && error.Contains("WildDrawFourBlocked"),"Official +4 restriction was bypassed.");
        Check(session.TryApplyVerifiedPlay(session.Revision,session.Discard.id,proof,"Green",new RuleSet{wildDrawFourNeedsNoActiveColour=false},out error),"Configured +4 house rule was ignored.");
        session=Session(new[]{"Wild Draw 4","Red 3"},"Blue 5");rig=new Rig(session,"W4");rig.Reduction();rig.NewTop();proof=rig.Proof();
        Check(session.TryApplyVerifiedPlay(session.Revision,session.Discard.id,proof,"Yellow",null,out error) && session.ActiveColor=="Yellow","Legal Wild+4 was rejected.");

        session=Session(new[]{"Blue 5"},"Blue 3");rig=new Rig(session,"B5");rig.Reduction();rig.NewTop();proof=rig.Proof();
        Check(proof!=null && session.TryApplyVerifiedPlay(session.Revision,session.Discard.id,proof,null,null,out error),"Final card could not be verified.");
        Check(session.HasHand && session.Hand.Count==0 && session.Played.Count==1,"Playing the last card did not produce an empty confirmed hand.");
        Check(session.TryUndoPlay() && session.Hand.Count==1,"Undo did not restore the last card.");
        session=Session(new[]{"Blue 5","Green 4"},"Blue 3");rig=new Rig(session,"B5");rig.Reduction();rig.NewTop();proof=rig.Proof();
        revision=session.Revision;originalTop=session.Discard.id;
        session.TrySave(ScanTarget.Discard,new[]{CardEntry.FromLabel("Blue 7",.9f,null)},null,out error);
        Check(!session.TryApplyVerifiedPlay(revision,originalTop,proof,null,null,out error),"A manual confirmation failed to invalidate an older play proof.");
        Check(!session.CanUndoPlay,"Manual confirmation left an unrelated undo active.");
    }
    public static int Main()
    {try{Run();Console.WriteLine("Live play/production rules/session checks passed: "+checks);return 0;}
     catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}}
}
