using System;
using System.Collections.Generic;
using UnityEngine;

public enum ScanTarget { Hand, Discard }

// Runtime snapshot. It does not reference a mutable CardEntry or a camera texture.
public sealed class ConfirmedCard
{
    public readonly string id;
    public readonly string color;
    public readonly string value;
    public readonly CardSource source;
    public readonly float confidence;
    public readonly bool uncertain;

    public ConfirmedCard(CardEntry card)
    {
        id = Guid.NewGuid().ToString("N");
        color = card.color;
        value = card.value;
        source = card.source;
        confidence = card.confidence;
        uncertain = card.uncertain;
    }

    public string DisplayName =>
        string.IsNullOrEmpty(color) ? value : $"{color} {value}";
}

public class GameSessionState : MonoBehaviour
{
    public IReadOnlyList<ConfirmedCard> Hand { get; private set; } =
        Array.AsReadOnly(new ConfirmedCard[0]);

    public ConfirmedCard Discard { get; private set; }
    public string ActiveColor { get; private set; }
    public bool HasHand { get; private set; }
    public int Revision { get; private set; }
    public IReadOnlyList<ConfirmedCard> Played { get; private set; }=Array.Empty<ConfirmedCard>();
    sealed class UndoState
    {
        public IReadOnlyList<ConfirmedCard> Hand,Played;
        public ConfirmedCard Top;public string Color;public int AfterRevision;
    }
    UndoState undo;
    public bool CanUndoPlay=>undo!=null && undo.AfterRevision==Revision;

    public bool TryApplyVerifiedPlay(int expectedRevision,string expectedTopId,LivePlayVerifier.Proof proof,
        string wildColor,RuleSet rules,out string error,bool allowUnknownTop=false,bool observationOnly=false)
    {
        error=null;
        if(proof==null || Revision!=expectedRevision || (Discard==null ? !allowUnknownTop || expectedTopId!=null : Discard.id!=expectedTopId) ||
            proof.ReferenceCount!=Hand.Count || !HasHand)
        {error="Session changed; verify again.";return false;}
        int index=-1;
        for(int i=0;i<Hand.Count;i++)if(UnoCardKey.For(Hand[i].color,Hand[i].value)==proof.Key){index=i;break;}
        if(index<0){error="Verified card is not in the hand.";return false;}
        var card=Hand[index];
        if(Discard!=null && !observationOnly)
        {var check=new StandardUnoRules(rules).Check(card,Hand,Discard,ActiveColor);
        if(!check.Legal){error="Rules conflict: "+check.Reason;return false;}}
        bool wild=CardFaces.IsWild(CardFaces.ParseValue(card.value));
        if(wild && !IsValidColor(wildColor) && !observationOnly){error="Choose the Wild active colour.";return false;}
        var before=new UndoState{Hand=Hand,Top=Discard,Color=ActiveColor,Played=Played};
        var remaining=new List<ConfirmedCard>(Hand);remaining.RemoveAt(index);
        var history=new List<ConfirmedCard>(Played);history.Add(card);if(history.Count>32)history.RemoveAt(0);
        Hand=remaining.AsReadOnly();Discard=card;ActiveColor=wild ? IsValidColor(wildColor) ? wildColor : null : card.color;
        Played=history.AsReadOnly();Revision++;before.AfterRevision=Revision;undo=before;
        return true;
    }
    public bool TryUndoPlay()
    {
        if(!CanUndoPlay)return false;
        Hand=undo.Hand;Discard=undo.Top;ActiveColor=undo.Color;Played=undo.Played;
        undo=null;Revision++;return true;
    }
    public bool TrySetLiveWildColor(string color)
    {
        if(Discard==null || !CardFaces.IsWild(CardFaces.ParseValue(Discard.value)) || !IsValidColor(color))return false;
        ActiveColor=color;Revision++;if(undo!=null)undo.AfterRevision=Revision;return true;
    }

    // Automatic acquisition/draws only add cards. Absence is never allowed to remove a card here.
    public bool TryMergeLiveHand(IReadOnlyList<CardEntry> observed,out string error)
    {
        error=null;if(observed==null || observed.Count==0){error="Hand not visible.";return false;}
        var unmatched=new List<CardEntry>();
        foreach(var card in observed)
        {
            if(card==null || !StableHandEvidence.ValidKey(UnoCardKey.For(card.color,card.value)))
            {error="Unknown live card.";return false;}unmatched.Add(card);
        }
        foreach(var card in Hand)
        {
            int index=unmatched.FindIndex(o=>UnoCardKey.For(o.color,o.value)==UnoCardKey.For(card.color,card.value));
            if(index<0){error="Incomplete observation; hand retained.";return false;}unmatched.RemoveAt(index);
        }
        if(HasHand && unmatched.Count==0)return false;
        var next=new List<ConfirmedCard>(Hand);foreach(var card in unmatched)next.Add(new ConfirmedCard(card));
        Hand=next.AsReadOnly();HasHand=true;Revision++;undo=null;return true;
    }
    public bool TryAddLiveDraws(IReadOnlyList<CardEntry> cards,out string error)
    {
        error=null;if(cards==null || cards.Count==0)return false;
        var next=new List<ConfirmedCard>(Hand);
        foreach(var card in cards)
        {
            if(card==null || !StableHandEvidence.ValidKey(UnoCardKey.For(card.color,card.value)))
            {error="Unknown drawn card.";return false;}
            next.Add(new ConfirmedCard(card));
        }
        Hand=next.AsReadOnly();HasHand=true;Revision++;undo=null;return true;
    }

    public bool ReadyForTutor =>
        HasHand && Discard != null && !string.IsNullOrEmpty(ActiveColor);

    public static bool IsWild(CardEntry card)
    {
        return card != null &&
            (string.Equals(card.color, "Wild", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(card.value, "Wild", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(card.DisplayName, "Draw 4", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsValidColor(string color)
    {
        return color == "Red" || color == "Yellow" || color == "Green" || color == "Blue";
    }

    public bool TrySave(ScanTarget target, IReadOnlyList<CardEntry> cards,
        string wildColor, out string error)
    {
        error = null;

        if (cards == null || cards.Count == 0)
        {
            error = "No cards to save.";
            return false;
        }

        foreach (var card in cards)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.value) ||
                string.Equals(card.value, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                error = "Resolve unknown cards before confirming.";
                return false;
            }

            if (!IsWild(card) && !IsValidColor(card.color))
            {
                error = "Resolve unknown card colours before confirming.";
                return false;
            }
        }

        if (target == ScanTarget.Hand)
        {
            var snapshot = new ConfirmedCard[cards.Count];
            for (int i = 0; i < cards.Count; i++)
                snapshot[i] = new ConfirmedCard(cards[i]);

            Hand = Array.AsReadOnly(snapshot);
            HasHand = true;
            Revision++;undo=null;
            return true;
        }

        if (cards.Count != 1)
        {
            error = "Scan exactly one discard card.";
            return false;
        }

        var top = cards[0];
        string activeColor = IsWild(top) ? wildColor : top.color;
        if (!IsValidColor(activeColor))
        {
            error = "Choose a valid active colour before confirming.";
            return false;
        }

        Discard = new ConfirmedCard(top);
        ActiveColor = activeColor;
        Revision++;undo=null;
        return true;
    }
}
