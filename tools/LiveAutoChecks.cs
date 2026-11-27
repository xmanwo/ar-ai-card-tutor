using System;
using System.Collections.Generic;

public static class LiveAutoChecks
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static List<FreeCardPlayVerifier.Observation> Hand(bool extra=false)
    {
        var list=new List<FreeCardPlayVerifier.Observation>();var keys=extra ? new[]{"B5","G4","G4","Y7"} : new[]{"B5","G4","G4"};
        for(int i=0;i<keys.Length;i++){float x=.22f+i*.04f;list.Add(new FreeCardPlayVerifier.Observation(i+1,keys[i],.9f,new LiveCardTracker.Box(x-.01f,.25f,x+.01f,.28f)));}
        list.Add(new FreeCardPlayVerifier.Observation(99,"R5",.95f,new LiveCardTracker.Box(.55f,.5f,.58f,.55f)));return list;
    }
    public static int Main()
    {
        try
        {
            var inventory=new LiveHandInventory();IReadOnlyList<FreeCardPlayVerifier.Observation> accepted=null;
            Check(!inventory.Observe(1,0,Hand(),null,null,out accepted),"One raw frame became a saved hand.");
            for(int i=2;i<=5;i++)inventory.Observe(i,(i-1)*.25f,Hand(),null,null,out accepted);
            Check(accepted!=null && accepted.Count==3,"Live did not automatically acquire the compact hand or included the table card.");
            Check(accepted[1].Key=="G4" && accepted[2].Key=="G4" && accepted[1].TrackId!=accepted[2].TrackId,"Repeated card faces were merged.");
            var session=new GameSessionState();var entries=new[]{CardEntry.FromLabel("Blue 5",.9f,null),CardEntry.FromLabel("Green 4",.9f,null),CardEntry.FromLabel("Green 4",.9f,null)};
            Check(session.TryMergeLiveHand(entries,out var error) && session.HasHand && session.Hand.Count==3,"Automatic initial session hand failed: "+error);
            string id=session.Hand[0].id;int rev=session.Revision;
            Check(!session.TryMergeLiveHand(entries,out error) && session.Revision==rev,"Identical observations rebuilt the hand.");
            Check(!session.TryMergeLiveHand(new[]{entries[1],entries[2]},out error) && session.Hand[0].id==id && session.Hand.Count==3,"A missing observation removed a card.");
            var saved=new[]{"B5","G4","G4"};inventory.Reset();
            var partial=Hand();partial.RemoveAt(0);
            for(int i=6;i<=13;i++)Check(!inventory.Observe(i,i*.25f,partial,null,saved,out accepted),"Prolonged occlusion replaced the saved hand.");
            inventory.Reset();
            for(int i=14;i<=18;i++)inventory.Observe(i,i*.25f,Hand(true),null,saved,out accepted);
            Check(accepted!=null && accepted.Count==4,"A repeatedly observed added card was not acquired.");
            var added=new List<CardEntry>(entries){CardEntry.FromLabel("Yellow 7",.9f,null)};
            Check(session.TryMergeLiveHand(added,out error) && session.Hand.Count==4 && session.Hand[0].id==id,"Automatic draw changed existing card identities.");
            inventory.Exclude(4);inventory.Reset();
            for(int i=19;i<=23;i++)Check(!inventory.Observe(i,i*.25f,Hand(true),null,saved,out accepted),"Played-card track was added back into the hand.");
            var fresh=new LiveHandInventory();var noisy=Hand();noisy[0]=new FreeCardPlayVerifier.Observation(1,"R9",.9f,noisy[0].Box);
            for(int i=0;i<5;i++)fresh.Observe(i,i*.25f,i==2 ? noisy : Hand(),null,null,out accepted);
            Check(accepted!=null && accepted[0].Key=="B5","One wrong frame contaminated auto acquisition.");
            fresh.Interrupt();Check(!fresh.Observe(20,10,Hand(),null,null,out accepted),"A reconnect reused old auto-acquisition votes.");
            Check(!fresh.Observe(20,10,Hand(),null,null,out accepted),"A duplicate reply cast another acquisition vote.");
            var focus=new LiveFocusWindow();focus.Observe(new LiveCardTracker.Box(.25f,.25f,.4f,.3f),0);
            Check(focus.TryCrop(1728,1728,out var first),"Live focus did not create a crop.");
            for(int i=1;i<12;i++)
            {
                Check(!focus.NeedsRecovery(i*.2f),"Live focus periodically switched to full-image search.");
                focus.TryCrop(1728,1728,out var next);Check(first.X==next.X && first.Y==next.Y && first.Width==next.Width,"Occlusion changed the presentation window.");
            }
            Check(focus.NeedsRecovery(3.1f),"Persistent loss never entered recovery.");
            focus.Observe(new LiveCardTracker.Box(.251f,.251f,.401f,.301f),3.2f);focus.TryCrop(1728,1728,out var stable);
            Check(stable.X==first.X && stable.Width==first.Width,"Tiny detection noise resized the focus window.");
            var emptyTop=new GameSessionState();emptyTop.TryMergeLiveHand(entries,out error);int initial=emptyTop.Revision;
            var proof=new LivePlayVerifier.Proof("B5",3,10,10,2);
            Check(!emptyTop.TryApplyVerifiedPlay(initial,null,proof,null,null,out error),"Default transaction silently skipped top-card rules.");
            Check(emptyTop.TryApplyVerifiedPlay(initial,null,proof,null,null,out error,allowUnknownTop:true) && emptyTop.Hand.Count==2 && emptyTop.Played.Count==1 && emptyTop.Discard.value=="5","First visible Live play did not establish the discard without a manual scan.");
            Check(emptyTop.TryUndoPlay() && emptyTop.Discard==null && emptyTop.Hand.Count==3,"First-play Undo failed to restore the unknown-top state.");
            Console.WriteLine("Automatic Live hand and stable focus checks passed: "+checks);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
