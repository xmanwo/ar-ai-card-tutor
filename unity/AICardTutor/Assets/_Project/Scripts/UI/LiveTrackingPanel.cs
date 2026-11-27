using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A larger live inspection view; the confirmed hand remains independently saved.</summary>
public sealed class LiveTrackingPanel
{
    readonly RectTransform root;
    readonly RawImage photo;
    readonly TrackingBoxesOverlay boxes;
    readonly TMP_Text title,status,legendTitle,modeText,handStatus;
    readonly TMP_Text[] rows=new TMP_Text[12];
    readonly TMP_FontAsset font;
    readonly RectTransform debugRoot;
    readonly LiveHandCardsView cardFaces;
    readonly TMP_Text viewText;
    CardScanClient liveClient;
    bool detections,debug,wasActive;
    int version=-1;

    public LiveTrackingPanel(Transform parent,TMP_FontAsset font)
    {
        this.font=font;
        root=Rect("LiveTrackingPanel",parent,new Vector2(680,500),new Vector2(0,-10));
        var background=root.gameObject.AddComponent<Image>();
        background.color=new Color(.025f,.045f,.065f,1f);
        background.raycastTarget=true;
        title=Text("LiveTitle",root,new Vector2(450,30),new Vector2(-90,225),20);
        var viewRect=Rect("LiveDisplayMode",root,new Vector2(130,28),new Vector2(255,225));
        var viewImage=viewRect.gameObject.AddComponent<Image>();viewImage.color=new Color(.12f,.28f,.4f);
        var viewButton=viewRect.gameObject.AddComponent<Button>();viewButton.targetGraphic=viewImage;
        viewButton.onClick.AddListener(()=>{debug=!debug;version=-1;});
        viewText=Text("ViewLabel",viewRect,new Vector2(126,26),Vector2.zero,16);viewText.alignment=TextAlignmentOptions.Center;
        debugRoot=Rect("TrackingDebugView",root,new Vector2(680,500),Vector2.zero);
        var modeRect=Rect("TrackDisplayMode",debugRoot,new Vector2(130,28),new Vector2(255,195));
        var modeImage=modeRect.gameObject.AddComponent<Image>(); modeImage.color=new Color(.12f,.28f,.4f);
        var button=modeRect.gameObject.AddComponent<Button>(); button.targetGraphic=modeImage;
        button.onClick.AddListener(()=>{ detections=!detections; version=-1; });
        modeText=Text("Mode",modeRect,new Vector2(126,26),Vector2.zero,16);
        modeText.alignment=TextAlignmentOptions.Center;
        photo=Rect("LivePhoto",debugRoot,new Vector2(470,330),new Vector2(-90,30)).gameObject.AddComponent<RawImage>();
        photo.raycastTarget=false;
        boxes=photo.gameObject.AddComponent<TrackingBoxesOverlay>(); boxes.Initialize(font);
        legendTitle=Text("LegendTitle",debugRoot,new Vector2(170,24),new Vector2(245,163),13);
        for (int i=0;i<rows.Length;i++)
            rows[i]=Text("TrackRow"+i,debugRoot,new Vector2(170,23),new Vector2(245,140-i*24),14);
        cardFaces=new LiveHandCardsView(root,font,()=>{if(liveClient!=null)liveClient.UndoLivePlay();});
        debugRoot.gameObject.SetActive(false);
        status=Text("LiveStatus",root,new Vector2(640,34),new Vector2(0,-166),13);
        handStatus=Text("StableHandStatus",root,new Vector2(640,52),new Vector2(0,-220),14);
        root.gameObject.SetActive(false);
    }

    public void Refresh(CardScanClient client)
    {
        bool active=client!=null && client.LiveTracking;
        root.gameObject.SetActive(active);
        if(active && !wasActive){debug=false;version=-1;}
        wasActive=active;
        if (!active) { version=-1; return; }
        var frame=client.DisplayPreview;
        liveClient=client;
        title.text="Live · Repeating Scan Hand";
        viewText.text=debug ? "Card view" : "Debug view";
        debugRoot.gameObject.SetActive(debug);cardFaces.Root.gameObject.SetActive(!debug);
        cardFaces.Refresh(client.LiveSession,client.LivePlayStatus,client.LiveLatestHand);
        modeText.text=detections ? "Detections" : "Tracks";
        legendTitle.text=detections ? "After server pairing" : "ID · Card · Confidence";
        if (frame!=null)
        {
            photo.enabled=true; photo.texture=frame.Recognition;
            float scale=Mathf.Min(470f/photo.texture.width,330f/photo.texture.height);
            photo.rectTransform.sizeDelta=new Vector2(photo.texture.width*scale,photo.texture.height*scale);
        }
        else { photo.enabled=false; photo.texture=null; }
        var viewport=frame==null ? default : frame.Cropped ? frame.Crop :
            new HandCropGeometry.Crop(0,0,frame.Width,frame.Height);
        boxes.Show(client.DisplayTracks,client.DisplayDetections,detections,viewport,
            debug && frame!=null && client.CanShowTrackingBoxes);
        if (version!=client.PreviewVersion)
        {
            version=client.PreviewVersion;
            int count=detections ? client.DisplayDetections.Count : client.DisplayTracks.Count;
            for (int i=0;i<rows.Length;i++)
            {
                rows[i].text="";
                if (i>=count) continue;
                if (i==rows.Length-1 && count>rows.Length) { rows[i].text=$"+{count-i} more boxes"; continue; }
                if (detections)
                {
                    var item=client.DisplayDetections[i];
                    rows[i].text=$"D{i+1} {Short(item.Label)} {item.Confidence:P0}";
                    rows[i].color=TrackingBoxesOverlay.DetectionColor;
                }
                else
                {
                    var item=client.DisplayTracks[i];
                    rows[i].text=item.State==LiveCardTracker.Status.Missing
                        ? $"#{item.Id} {Short(item.Label)} lost"
                        : $"#{item.Id} {Short(item.Label)} {item.Confidence:P0}";
                    rows[i].color=TrackingBoxesOverlay.ColorFor(item.State);
                }
            }
        }
        handStatus.text=$"Completed Scan Hand rounds: {client.LiveCompletedRounds} · Latest hand: "+
            (client.LiveLatestHand!=null ? client.LiveLatestHand.Count.ToString() : "scanning...")+"\n"+client.LivePlayStatus;
        handStatus.color=Color.white;
        if (!string.IsNullOrEmpty(client.LiveError))
        {
            status.color=new Color(1,.55f,.4f);
            status.text=(client.IsBusy ? "Reconnecting..." : $"Automatic retry in {client.LiveRetrySeconds:F1}s")+
                " · Confirmed hand retained\n"+client.LiveError;
        }
        else
        {
            status.color=Color.white;
            status.text=frame==null ? "Waiting for the first recognition reply..." :
                $"Frame {frame.FrameId} · {client.PreviewFramesPerSecond:F1} Hz · {client.LastLatencyMs:F0} ms · age {Mathf.Max(0,Time.unscaledTime-frame.CapturedAt):F1}s"+
                (client.CanShowTrackingBoxes ? "\nGreen: stable · Yellow: new/uncertain · Orange: temporarily lost" : "\nWaiting for a fresh frame; stale boxes hidden.");
        }
    }

    static string Short(string label) => label.Replace("Draw Two","+2").Replace("Draw two","+2")
        .Replace("Draw 2","+2").Replace("Draw 4","+4").Replace("Reverse","Rev");
    static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        rect.sizeDelta=size; rect.anchoredPosition=position; return rect;
    }
    TMP_Text Text(string name,Transform parent,Vector2 size,Vector2 position,float fontSize)
    {
        var text=Rect(name,parent,size,position).gameObject.AddComponent<TextMeshProUGUI>();
        text.font=font; text.fontSize=fontSize; text.color=Color.white;
        text.raycastTarget=false; text.richText=false; text.textWrappingMode=TextWrappingModes.NoWrap;
        text.overflowMode=TextOverflowModes.Ellipsis; return text;
    }
}
