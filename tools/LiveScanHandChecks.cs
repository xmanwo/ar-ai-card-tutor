using System;
using System.Collections.Generic;

public static class LiveScanHandChecks
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static List<CardEntry> Cards(params string[] labels)
    {var cards=new List<CardEntry>();foreach(string label in labels)cards.Add(CardEntry.FromLabel(label,.95f,new float[]{10,10,20,30}));return cards;}
    sealed class Rig
    {
        public readonly GameSessionState Session=new GameSessionState();public readonly LiveScanHandState Loop=new LiveScanHandState();public int Round;
        public Rig(string top="Yellow 2",params string[] hand)
        {
            if(top!=null)Session.TrySave(ScanTarget.Discard,Cards(top),null,out _);
            Observe(hand.Length>0 ? Cards(hand) : Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8"));
        }
        public void Observe(List<CardEntry> cards,RuleSet rules=null)=>Check(Loop.ObserveRound(++Round,cards,Session,rules),"Completed round rejected.");
    }
    public static int Main()
    {
        try
        {
            var r=new Rig();string yellow=r.Session.Hand[0].id,green=r.Session.Hand[1].id;
            Check(r.Session.HasHand && r.Session.Hand.Count==5 && r.Loop.Latest.Count==5,"First Scan Hand did not establish the hand automatically.");
            var reduced=Cards("Green 4","Green 4","Red 9","Blue 8");r.Observe(reduced);
            Check(r.Session.Played.Count==0 && r.Loop.Latest.Count==4,"First missing scan counted as played or failed to publish the latest hand.");
            r.Observe(reduced);
            Check(r.Session.Hand.Count==4 && r.Session.Played.Count==1 && r.Session.Discard.id==yellow && r.Session.Hand[0].id==green,"Second missing scan failed to move exactly one legal original card.");
            Check(!r.Loop.ObserveRound(r.Round,reduced,r.Session) && r.Session.Played.Count==1,"Duplicate completed round counted twice.");
            var returnCard=new Rig();returnCard.Observe(reduced);returnCard.Observe(Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8"));returnCard.Observe(reduced);
            Check(returnCard.Session.Played.Count==0,"An intervening sighting failed to reset missing rounds.");
            returnCard.Observe(reduced);Check(returnCard.Session.Played.Count==1,"A new pair of consecutive missing rounds did not qualify.");
            var illegal=new Rig();var noGreen=Cards("Yellow 7","Green 4","Red 9","Blue 8");illegal.Observe(noGreen);illegal.Observe(noGreen);
            Check(illegal.Session.Hand.Count==5 && illegal.Session.Played.Count==0 && illegal.Loop.Latest.Count==4,"An illegal missing Green 4 counted as played or latest scan was hidden.");
            var duplicate=new Rig("Green 2");duplicate.Observe(noGreen);duplicate.Observe(noGreen);
            Check(duplicate.Session.Hand.Count==4 && duplicate.Session.Played.Count==1 && duplicate.Session.Played[0].value=="4","Repeated faces could not be removed one copy at a time.");
            var draw=new Rig();string original=draw.Session.Hand[0].id;draw.Observe(Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8","Blue 5"));
            Check(draw.Loop.LastDrawCount==1 && draw.Session.Hand.Count==6 && draw.Loop.Latest.Count==6 && draw.Session.Hand[0].id==original,"Additional scan card did not count as drawing or regenerated existing IDs.");
            draw.Observe(Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8","Blue 5"));
            Check(draw.Loop.LastDrawCount==0 && draw.Session.Hand.Count==6,"Same observed draw was added twice.");
            var sameSize=new Rig();var relabelled=Cards("Yellow 8","Green 4","Green 4","Red 9","Blue 8");sameSize.Observe(relabelled);sameSize.Observe(relabelled);
            Check(sameSize.Session.Hand.Count==5 && sameSize.Session.Played.Count==0 && sameSize.Loop.LastDrawCount==0,"Same-sized label correction became a play or a draw.");
            var mixed=new Rig();mixed.Observe(reduced);mixed.Observe(Cards("Green 4","Green 4","Red 9","Blue 8","Red 3"));
            Check(mixed.Loop.LastDrawCount==1 && mixed.Session.Played.Count==1 && mixed.Session.Hand.Count==5,"A draw between missing rounds lost the pending legal play or the newly drawn card.");
            var unknownTop=new Rig(null);unknownTop.Observe(reduced);unknownTop.Observe(reduced);
            Check(unknownTop.Session.Played.Count==0 && unknownTop.Loop.Message.Contains("Top card"),"Unknown top fabricated UNO legality.");
            var interrupted=new Rig();interrupted.Observe(reduced);interrupted.Loop.Interrupt("Offline");interrupted.Observe(reduced);
            Check(interrupted.Session.Played.Count==0,"Network failure was counted as a missing scan.");
            var immutable=new Rig();var source=Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8");immutable.Observe(source);source[0].value="0";source[0].bbox[0]=900;
            Check(immutable.Loop.Latest[0].value=="7" && immutable.Loop.Latest[0].bbox[0]==10,"Published result retained mutable raw card/image-box references.");
            var wild=new Rig("Yellow 2","Wild","Green 4");wild.Observe(Cards("Green 4"));wild.Observe(Cards("Green 4"));
            Check(wild.Loop.NeedsWildColor && wild.Session.Played.Count==0,"Wild did not await active colour after strict rule validation.");
            Check(wild.Loop.TryChooseWild(wild.Session,"Blue") && wild.Session.Played.Count==1 && wild.Session.ActiveColor=="Blue","Wild colour did not complete the legal play.");
            var plus4=new Rig("Yellow 2","Wild Draw 4","Yellow 7","Green 4");plus4.Observe(Cards("Yellow 7","Green 4"));plus4.Observe(Cards("Yellow 7","Green 4"));
            Check(plus4.Session.Played.Count==0 && !plus4.Loop.NeedsWildColor,"Official +4 restriction was bypassed.");
            var houseRules=new RuleSet{wildDrawFourNeedsNoActiveColour=false};plus4.Observe(Cards("Yellow 7","Green 4"),houseRules);
            Check(plus4.Loop.NeedsWildColor && plus4.Loop.TryChooseWild(plus4.Session,"Green",houseRules),"Configured +4 variant was ignored.");
            Check(r.Session.TryUndoPlay(),"Undo failed.");r.Loop.RestoreDisplay(r.Session);
            Check(r.Session.Hand.Count==5 && r.Loop.Latest.Count==5 && r.Session.Hand[0].id==yellow && r.Session.Played.Count==0,"Undo did not restore card fan and ledger identities.");
            var last=new Rig("Red 2","Red 9");last.Observe(Cards());last.Observe(Cards());
            Check(last.Session.Hand.Count==0 && last.Session.Played.Count==1 && last.Loop.Latest.Count==0,"Two successful empty scans could not register the last legal card.");
            var keepFocus=new Rig();
            Check(keepFocus.Loop.FocusQualityPassed && !keepFocus.Loop.RequiresFullRescan,"A stable reliable first scan could not establish tracking.");
            keepFocus.Observe(reduced);
            Check(keepFocus.Loop.MissingOrChangedCards==1 && !keepFocus.Loop.RequiresFullRescan,"One absent card unnecessarily forced full acquisition.");
            var changed=new Rig();changed.Observe(Cards("Yellow 8","Green 5","Green 4","Red 9","Blue 8"));
            Check(changed.Loop.MissingOrChangedCards==2 && changed.Loop.RequiresFullRescan && !changed.Loop.FocusQualityPassed,"Two changed old faces failed to trigger full acquisition.");
            changed.Observe(Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8"));
            Check(changed.Loop.FocusQualityPassed && !changed.Loop.RequiresFullRescan,"Successful full reacquisition could not resume following.");
            var additions=new Rig();additions.Observe(Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8","Red 3","Blue 5"));
            Check(additions.Loop.MissingOrChangedCards==0 && additions.Loop.FocusQualityPassed,"Drawing multiple cards was treated as tracking failure.");
            var hideWithAdds=new Rig();hideWithAdds.Observe(Cards("Green 4","Red 9","Blue 8","Red 3","Blue 5","Blue 6"));
            Check(hideWithAdds.Loop.MissingOrChangedCards==2 && hideWithAdds.Loop.RequiresFullRescan,"Added cards masked two missing old cards.");
            var identicalLost=new Rig();identicalLost.Observe(Cards("Yellow 7","Red 9","Blue 8"));
            Check(identicalLost.Loop.MissingOrChangedCards==2 && identicalLost.Loop.RequiresFullRescan,"Two identical missing cards counted as only one loss.");
            var lateQuality=new Rig();lateQuality.Loop.Reset();
            var bad=Cards("Yellow 0","Green 0","Green 1","Red 9","Blue 8");foreach(var card in bad)card.uncertain=true;
            lateQuality.Observe(bad);Check(!lateQuality.Loop.FocusQualityPassed,"Low-quality initial recognition was allowed to establish carried focus.");
            lateQuality.Observe(Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8"));
            Check(lateQuality.Loop.FocusQualityPassed && !lateQuality.Loop.RequiresFullRescan,"A poor initial scan prevented a later good scan from establishing focus.");
            var timedOut=new Rig();timedOut.Loop.ObserveRound(++timedOut.Round,Cards("Yellow 7","Green 4","Green 4","Red 9","Blue 8"),timedOut.Session,null,false);
            Check(!timedOut.Loop.FocusQualityPassed,"A timed-out unsettled scan replaced trusted focus.");
            Console.WriteLine("Repeated Scan Hand / draw / two-round rules checks passed: "+checks);return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
