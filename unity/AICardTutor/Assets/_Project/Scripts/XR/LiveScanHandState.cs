using System;
using System.Collections.Generic;

/// <summary>Game events inferred only from completed, ordinary Scan Hand rounds.</summary>
public sealed class LiveScanHandState
{
    readonly Dictionary<string,List<int>> missing=new Dictionary<string,List<int>>();
    readonly Dictionary<string,int> focusReference=new Dictionary<string,int>();
    IReadOnlyList<CardEntry> latest;
    int lastRound=-1,pendingRevision;
    string pendingTop,pendingWild;
    public const int MissingRounds=2;
    public int CompletedRounds { get; private set; }
    public IReadOnlyList<CardEntry> Latest=>latest;
    public bool NeedsWildColor=>pendingWild!=null;
    public int LastDrawCount { get; private set; }
    public int MissingOrChangedCards { get; private set; }
    public bool FocusQualityPassed { get; private set; }
    public bool RequiresFullRescan=>MissingOrChangedCards>=2;
    public string Message { get; private set; }="Running Scan Hand automatically...";
    public void Reset(){latest=null;CompletedRounds=0;lastRound=-1;focusReference.Clear();MissingOrChangedCards=0;FocusQualityPassed=false;ResetEvidence();Message="Running Scan Hand automatically...";}
    public void ResetEvidence(){missing.Clear();pendingWild=null;LastDrawCount=0;}
    public void Interrupt(string message){ResetEvidence();Message=message;}
    public bool ObserveRound(int round,IReadOnlyList<CardEntry> cards,GameSessionState session,RuleSet rules=null,bool stableRound=true)
    {
        if(round<=lastRound || cards==null || session==null)return false;
        int previousObservedCount=latest!=null ? latest.Count : session.Hand.Count;
        var copy=new List<CardEntry>();var observed=new Dictionary<string,int>();bool valid=true;int certain=0;
        foreach(var card in cards)
        {
            if(card==null)return false;
            copy.Add(new CardEntry{color=card.color,value=card.value,confidence=card.confidence,uncertain=card.uncertain,source=card.source,
                bbox=card.bbox==null ? null : (float[])card.bbox.Clone()});
            string key=UnoCardKey.For(card.color,card.value);
            if(!StableHandEvidence.ValidKey(key)){valid=false;continue;}
            if(!card.uncertain && card.confidence>=.45f)certain++;
            observed.TryGetValue(key,out int n);observed[key]=n+1;
        }
        lastRound=round;CompletedRounds++;latest=copy.AsReadOnly();LastDrawCount=0;pendingWild=null;
        // Only a reliable scan establishes this reference. A poor initial scan cannot prevent
        // a later good scan from acquiring focus. Additions never cancel an old-card loss.
        MissingOrChangedCards=0;
        foreach(var old in focusReference)
        {observed.TryGetValue(old.Key,out int seen);MissingOrChangedCards+=Math.Max(0,old.Value-seen);}
        FocusQualityPassed=stableRound && valid && copy.Count>0 && certain*5>=copy.Count*4 && !RequiresFullRescan;
        if(RequiresFullRescan)focusReference.Clear();
        else if(FocusQualityPassed)foreach(var item in observed)
        {focusReference.TryGetValue(item.Key,out int old);focusReference[item.Key]=Math.Max(old,item.Value);}
        if(!valid){ResetEvidence();Message="Unknown card in Scan Hand result; waiting for the next scan.";return false;}
        if(!session.HasHand)
        {
            if(copy.Count==0){Message="No hand cards found; scanning again.";return true;}
            if(!session.TryMergeLiveHand(copy,out var error)){Message=error;return false;}
            Message=$"Scan Hand complete: {copy.Count} cards. Scanning again.";return true;
        }
        var before=Counts(session.Hand);var extras=new List<CardEntry>();var remaining=new Dictionary<string,int>(before);
        foreach(var card in copy)
        {
            string key=UnoCardKey.For(card.color,card.value);
            if(remaining.TryGetValue(key,out int n) && n>0)remaining[key]=n-1;
            else extras.Add(card);
        }
        int increase=Math.Max(0,copy.Count-previousObservedCount);
        if(extras.Count>increase)extras.RemoveRange(increase,extras.Count-increase);
        if(extras.Count>0)
        {
            if(!session.TryAddLiveDraws(extras,out var error)){Message=error;return false;}
            LastDrawCount=extras.Count;
        }
        if(copy.Count>=session.Hand.Count)
        {
            missing.Clear();
            Message=(LastDrawCount>0 ? $"Drew {LastDrawCount} card(s). " : "")+
                (extras.Count==0 && !SameCounts(observed,Counts(session.Hand)) ? "Card count unchanged; label differences are not a play. Scanning again." : $"Scan Hand complete: {copy.Count} cards. Scanning again.");
            return true;
        }
        var current=Counts(session.Hand);var keep=new HashSet<string>();int absent=0;
        foreach(var pair in current)
        {
            observed.TryGetValue(pair.Key,out int seen);int count=Math.Max(0,pair.Value-seen);
            if(count==0)continue;keep.Add(pair.Key);absent+=count;
            if(!missing.TryGetValue(pair.Key,out var streak))missing[pair.Key]=streak=new List<int>();
            int previous=streak.Count;
            if(streak.Count>count)streak.RemoveRange(count,streak.Count-count);
            for(int i=0;i<streak.Count;i++)streak[i]++;
            for(int i=previous;i<count;i++)streak.Add(1);
        }
        foreach(var key in new List<string>(missing.Keys))if(!keep.Contains(key))missing.Remove(key);
        string draw=LastDrawCount>0 ? $"Drew {LastDrawCount} card(s). " : "";
        if(absent==0){Message=draw+$"Scan Hand complete: {copy.Count} cards. Scanning again.";return true;}
        string blocked=null;bool twice=false;
        var engine=new StandardUnoRules(rules);
        foreach(var card in session.Hand)
        {
            string key=UnoCardKey.For(card.color,card.value);
            if(!missing.TryGetValue(key,out var streak) || streak.Count==0 || streak[0]<MissingRounds)continue;
            twice=true;
            if(session.Discard==null || !GameSessionState.IsValidColor(session.ActiveColor))
            {blocked="Top card / active colour needed to check UNO rules.";continue;}
            var check=engine.Check(card,session.Hand,session.Discard,session.ActiveColor);
            if(!check.Legal){blocked="Missing card is not playable: "+card.DisplayName+" ("+check.Reason+").";continue;}
            if(CardFaces.IsWild(CardFaces.ParseValue(card.value)))
            {
                pendingWild=key;pendingRevision=session.Revision;pendingTop=session.Discard.id;
                Message=draw+"Absent in two Scan Hand rounds: "+card.DisplayName+". Choose Wild colour.";return true;
            }
            if(Commit(session,key,null,rules,out var error))
            {Message=draw+"Played: "+card.DisplayName+" · absent in two scans and UNO rules passed.";return true;}
            blocked=error;
        }
        Message=draw+(twice ? blocked ?? "Waiting for a legal missing card." : $"Scan Hand found {copy.Count} cards; missing {absent}, checking the next complete scan.");
        return true;
    }
    public bool TryChooseWild(GameSessionState session,string color,RuleSet rules=null)
    {
        if(pendingWild==null || !GameSessionState.IsValidColor(color) || session==null || session.Revision!=pendingRevision || session.Discard?.id!=pendingTop)return false;
        string key=pendingWild;
        if(!Commit(session,key,color,rules,out var error)){Message=error;return false;}
        pendingWild=null;Message="Played Wild · active colour: "+color;return true;
    }
    bool Commit(GameSessionState session,string key,string color,RuleSet rules,out string error)
    {
        var proof=new LivePlayVerifier.Proof(key,session.Hand.Count,lastRound,lastRound,0);
        if(!session.TryApplyVerifiedPlay(session.Revision,session.Discard?.id,proof,color,rules,out error))return false;
        if(missing.TryGetValue(key,out var streak) && streak.Count>0)streak.RemoveAt(0);
        if(focusReference.TryGetValue(key,out int count))
        {if(count<=1)focusReference.Remove(key);else focusReference[key]=count-1;}
        return true;
    }
    public void RestoreDisplay(GameSessionState session)
    {
        ResetEvidence();MissingOrChangedCards=0;FocusQualityPassed=false;var copy=new List<CardEntry>();
        focusReference.Clear();foreach(var pair in Counts(session.Hand))focusReference[pair.Key]=pair.Value;
        foreach(var card in session.Hand)copy.Add(new CardEntry{color=card.color,value=card.value,confidence=card.confidence,source=card.source,uncertain=card.uncertain});
        latest=copy.AsReadOnly();Message="Last play undone. Continuing Scan Hand.";
    }
    public string Diagnostics()
    {
        string text=$"completed Scan Hand rounds={CompletedRounds}; latest={latest?.Count.ToString() ?? "none"}; drew={LastDrawCount}; lost/changed={MissingOrChangedCards}; focus quality={FocusQualityPassed};";
        foreach(var pair in missing){text+=" "+pair.Key+" missing rounds=";foreach(int n in pair.Value)text+=n+",";}
        return text;
    }
    static Dictionary<string,int> Counts(IReadOnlyList<ConfirmedCard> cards)
    {
        var counts=new Dictionary<string,int>();foreach(var card in cards){string key=UnoCardKey.For(card.color,card.value);counts.TryGetValue(key,out int n);counts[key]=n+1;}return counts;
    }
    static bool SameCounts(Dictionary<string,int> a,Dictionary<string,int> b)
    {if(a.Count!=b.Count)return false;foreach(var pair in a)if(!b.TryGetValue(pair.Key,out int n) || n!=pair.Value)return false;return true;}
}
