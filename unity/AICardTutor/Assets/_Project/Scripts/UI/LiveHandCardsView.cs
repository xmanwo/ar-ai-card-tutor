using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Only confirmed or visually/rule-verified session cards render here, never a raw prediction.</summary>
public sealed class LiveHandCardsView
{
    public readonly RectTransform Root;
    readonly RectTransform handRow,topSlot,playedRow;
    readonly TMP_Text handTitle,playedTitle,topTitle,empty,playHint;
    readonly TMP_FontAsset font;
    readonly Button previous,next,undo;
    GameSessionState previousSession;
    int revision=-1,page;
    string handSignature;
    public LiveHandCardsView(Transform parent,TMP_FontAsset font,Action undoAction)
    {
        this.font=font;
        Root=Rect("LiveCardFaces",parent,new Vector2(670,360),new Vector2(0,30));
        handRow=Rect("LiveHandRow",Root,new Vector2(530,144),new Vector2(45,70));
        topSlot=Rect("LiveTopCard",Root,new Vector2(78,117),new Vector2(-275,75));
        playedRow=Rect("PlayedCards",Root,new Vector2(530,108),new Vector2(45,-88));
        handTitle=Text("LiveHandTitle",Root,new Vector2(370,30),new Vector2(10,160),20);
        topTitle=Text("LiveTopTitle",Root,new Vector2(112,36),new Vector2(-275,160),16);topTitle.alignment=TextAlignmentOptions.Center;
        playedTitle=Text("PlayedTitle",Root,new Vector2(570,28),new Vector2(0,-18),18);
        empty=Text("LiveEmptyHand",Root,new Vector2(480,110),new Vector2(45,75),22);empty.alignment=TextAlignmentOptions.Center;
        playHint=Text("PlayVerificationStatus",Root,new Vector2(520,100),new Vector2(45,-88),19);
        playHint.alignment=TextAlignmentOptions.Center;playHint.textWrappingMode=TextWrappingModes.Normal;
        playHint.color=new Color(1,.88f,.3f);
        previous=Button("LivePrevious","<",Root,new Vector2(40,28),new Vector2(250,160),()=>{page--;revision=-1;});
        next=Button("LiveNext",">",Root,new Vector2(40,28),new Vector2(300,160),()=>{page++;revision=-1;});
        undo=Button("UndoLivePlay","Undo last play",Root,new Vector2(145,28),new Vector2(245,-158),undoAction);
    }
    public void Refresh(GameSessionState session,string verificationStatus=null,IReadOnlyList<CardEntry> latestScan=null)
    {
        undo.interactable=session!=null && session.CanUndoPlay;
        playHint.gameObject.SetActive(session!=null && session.HasHand && session.Played.Count==0 && session.Hand.Count>0);
        playHint.text=verificationStatus ?? "Waiting for a verified play.";
        var stamp=new StringBuilder(latestScan!=null ? "scan:" : "session:");
        if(latestScan!=null)foreach(var card in latestScan)stamp.Append(UnoCardKey.For(card.color,card.value)).Append(';');
        string nextSignature=stamp.ToString();
        if(previousSession==session && revision==(session!=null ? session.Revision : 0) && handSignature==nextSignature)return;
        handSignature=nextSignature;
        previousSession=session;revision=session!=null ? session.Revision : 0;
        Clear(handRow);Clear(topSlot);Clear(playedRow);
        int count=latestScan!=null ? latestScan.Count : session!=null && session.HasHand ? session.Hand.Count : 0;
        int pages=HandCardLayout.PageCount(count);page=Mathf.Clamp(page,0,pages-1);
        handTitle.text=(latestScan!=null ? $"Latest Scan Hand ({count})" : $"Hand ({count})")+(pages>1 ? $" · {page+1}/{pages}" : "");
        previous.gameObject.SetActive(pages>1);next.gameObject.SetActive(pages>1);
        previous.interactable=page>0;next.interactable=page<pages-1;
        empty.gameObject.SetActive(count==0);
        empty.text=latestScan!=null ? "No cards found in the latest Scan Hand\nScanning again..." : "Running Scan Hand...\nCards appear when this scan finishes";
        int start=page*HandCardLayout.PageSize,visible=Mathf.Min(HandCardLayout.PageSize,count-start);
        for(int i=0;i<visible;i++)
        {
            string key=latestScan!=null ? UnoCardKey.For(latestScan[start+i].color,latestScan[start+i].value) : UnoCardKey.For(session.Hand[start+i].color,session.Hand[start+i].value);
            Face(handRow,key,new Vector2(HandCardLayout.PositionX(i,visible,530,96),0),new Vector2(96,144));
        }
        topTitle.text="Top card\n"+(session?.Discard!=null ? session.ActiveColor : "Awaiting play");
        if(session?.Discard!=null)Face(topSlot,UnoCardKey.For(session.Discard.color,session.Discard.value),Vector2.zero,new Vector2(78,117));
        int played=session!=null ? session.Played.Count : 0;
        playedTitle.text=$"Discard pile · Played ({played})";
        int shown=Mathf.Min(8,played);
        for(int i=0;i<shown;i++)
        {
            var card=session.Played[played-shown+i];
            Face(playedRow,UnoCardKey.For(card.color,card.value),new Vector2(HandCardLayout.PositionX(i,shown,530,72),0),new Vector2(72,108));
        }
    }
    void Face(Transform parent,string key,Vector2 position,Vector2 size)
    {
        var rect=Rect("Face_"+key,parent,size,position);
        var shadow=Rect("Shadow",rect,size,new Vector2(3,-4)).gameObject.AddComponent<Image>();
        shadow.sprite=UnoCardArt.GetByKey(key);shadow.color=new Color(0,0,0,.4f);shadow.raycastTarget=false;
        var image=Rect("Card",rect,size,Vector2.zero).gameObject.AddComponent<Image>();
        image.sprite=UnoCardArt.GetByKey(key);image.preserveAspect=true;image.raycastTarget=false;
        if(image.sprite==null){var text=Text("Unknown",rect,size,Vector2.zero,24);text.text="?";text.alignment=TextAlignmentOptions.Center;}
    }
    Button Button(string name,string label,Transform parent,Vector2 size,Vector2 position,Action action)
    {
        var rect=Rect(name,parent,size,position);var image=rect.gameObject.AddComponent<Image>();image.color=new Color(.12f,.28f,.4f);
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>action());
        var text=Text("Label",rect,size,Vector2.zero,16);text.text=label;text.alignment=TextAlignmentOptions.Center;return button;
    }
    TMP_Text Text(string name,Transform parent,Vector2 size,Vector2 position,float fontSize)
    {
        var text=Rect(name,parent,size,position).gameObject.AddComponent<TextMeshProUGUI>();
        text.font=font;text.fontSize=fontSize;text.color=Color.white;text.raycastTarget=false;text.richText=false;
        text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;return text;
    }
    static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=size;rect.anchoredPosition=position;return rect;
    }
    static void Clear(Transform parent)
    {
        for(int i=parent.childCount-1;i>=0;i--){var child=parent.GetChild(i).gameObject;child.SetActive(false);
            if(Application.isPlaying)UnityEngine.Object.Destroy(child);else UnityEngine.Object.DestroyImmediate(child);}
    }
}
