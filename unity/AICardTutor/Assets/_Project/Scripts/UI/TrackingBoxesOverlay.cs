using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Pooled, non-interactive ID boxes over an acknowledged photo, never the next capture.</summary>
public class TrackingBoxesOverlay : MonoBehaviour
{
    public static readonly Color NewColor=new Color(1f,.9f,.18f);
    public static readonly Color StableColor=new Color(.20f,1f,.5f);
    public static readonly Color MissingColor=new Color(1f,.51f,.15f);
    public static readonly Color DetectionColor=new Color(.2f,.8f,1f);
    sealed class Item { public RectTransform Rect; public Image[] Edges; public TMP_Text Label; }
    readonly List<Item> pool=new List<Item>();
    TMP_FontAsset font;

    public void Initialize(TMP_FontAsset source) { font=source; }

    public void Show(IReadOnlyList<LiveCardTracker.Snapshot> tracks,
        IReadOnlyList<LiveCardTracker.Detection> detections,bool showDetections,
        HandCropGeometry.Crop viewport,bool visible)
    {
        int shown=0;
        if (visible && showDetections && detections!=null)
            for (int i=0;i<detections.Count && shown<64;i++)
                Draw(detections[i].Bounds,viewport,"D"+(i+1),DetectionColor,ref shown);
        else if (visible && tracks!=null)
            foreach (var track in tracks)
            {
                if (shown>=64) break;
                Draw(track.Bounds,viewport,"#"+track.Id,ColorFor(track.State),ref shown);
            }
        for (int i=shown;i<pool.Count;i++) pool[i].Rect.gameObject.SetActive(false);
    }

    public static Color ColorFor(LiveCardTracker.Status state) => state==LiveCardTracker.Status.Stable
        ? StableColor : state==LiveCardTracker.Status.Missing ? MissingColor : NewColor;

    void Draw(LiveCardTracker.Box box,HandCropGeometry.Crop viewport,string label,Color color,ref int count)
    {
        if (!TrackingOverlayGeometry.Project(box,viewport,out var bounds)) return;
        if (count==pool.Count) pool.Add(Build());
        Item item=pool[count++]; item.Rect.gameObject.SetActive(true);
        item.Rect.anchorMin=new Vector2(bounds.Left,bounds.Bottom);
        item.Rect.anchorMax=new Vector2(bounds.Right,bounds.Top);
        item.Rect.offsetMin=item.Rect.offsetMax=Vector2.zero;
        foreach (var edge in item.Edges) edge.color=color;
        item.Label.text=label; item.Label.color=color;
    }

    Item Build()
    {
        var rect=Rect("TrackBox",transform);
        var item=new Item { Rect=rect, Edges=new Image[4] };
        item.Edges[0]=Edge(rect,"Top",new Vector2(0,1),Vector2.one,new Vector2(0,2));
        item.Edges[1]=Edge(rect,"Bottom",Vector2.zero,new Vector2(1,0),new Vector2(0,2));
        item.Edges[2]=Edge(rect,"Left",Vector2.zero,new Vector2(0,1),new Vector2(2,0));
        item.Edges[3]=Edge(rect,"Right",new Vector2(1,0),Vector2.one,new Vector2(2,0));
        var backing=Rect("IDBackground",rect);
        backing.anchorMin=backing.anchorMax=new Vector2(0,1);
        backing.pivot=new Vector2(0,1); backing.anchoredPosition=new Vector2(1,-1);
        backing.sizeDelta=new Vector2(42,18);
        var background=backing.gameObject.AddComponent<Image>();
        background.color=new Color(0,0,0,.72f); background.raycastTarget=false;
        var textRect=Rect("TrackID",rect);
        textRect.anchorMin=textRect.anchorMax=new Vector2(0,1);
        textRect.pivot=new Vector2(0,1); textRect.anchoredPosition=new Vector2(1,-1);
        textRect.sizeDelta=new Vector2(46,18);
        item.Label=textRect.gameObject.AddComponent<TextMeshProUGUI>();
        item.Label.font=font!=null ? font : TMP_Settings.defaultFontAsset;
        item.Label.fontSize=13; item.Label.alignment=TextAlignmentOptions.TopLeft;
        item.Label.richText=false; item.Label.raycastTarget=false;
        item.Label.textWrappingMode=TextWrappingModes.NoWrap;
        return item;
    }

    static RectTransform Rect(string name,Transform parent)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false); return rect;
    }
    static Image Edge(Transform parent,string name,Vector2 min,Vector2 max,Vector2 size)
    {
        var rect=Rect(name,parent); rect.anchorMin=min; rect.anchorMax=max;
        rect.sizeDelta=size; rect.anchoredPosition=Vector2.zero;
        var image=rect.gameObject.AddComponent<Image>(); image.raycastTarget=false; return image;
    }
}
