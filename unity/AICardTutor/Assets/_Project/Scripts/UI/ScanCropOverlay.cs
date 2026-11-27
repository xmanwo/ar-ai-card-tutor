using UnityEngine;
using UnityEngine.UI;

/// <summary>Draws the actual uploaded rectangle on the complete scan photo.</summary>
public class ScanCropOverlay : MonoBehaviour
{
    public static readonly Color Focused = new Color(.20f, 1f, .5f, 1f);
    public static readonly Color FullFrame = new Color(1f, .73f, .20f, 1f);
    RectTransform border;
    readonly Image[] edges = new Image[4];

    public void Show(ScanPreviewFrame frame, RawImage photo)
    {
        bool visible = frame != null && photo != null && photo.enabled &&
            photo.texture != null && photo.texture == frame.Original;
        ShowRegion(frame,photo,frame!=null ? frame.Crop : default,visible,frame!=null && frame.Cropped ? Focused : FullFrame);
    }
    public void ShowRegion(ScanPreviewFrame frame,RawImage photo,HandCropGeometry.Crop crop,bool visible,Color color)
    {
        visible=visible && frame!=null && photo!=null && photo.enabled && photo.texture==frame.Original;
        if (border == null && visible) Build();
        if (border == null) return;
        border.gameObject.SetActive(visible);
        if (!visible) return;
        float width = photo.texture.width, height = photo.texture.height;
        border.anchorMin = new Vector2(crop.X / width, 1f - (crop.Y + crop.Height) / height);
        border.anchorMax = new Vector2((crop.X + crop.Width) / width, 1f - crop.Y / height);
        border.offsetMin = border.offsetMax = Vector2.zero;
        foreach (var edge in edges) edge.color = color;
    }

    void Build()
    {
        border = new GameObject("RecognitionCrop", typeof(RectTransform)).GetComponent<RectTransform>();
        border.SetParent(transform, false);
        edges[0] = Edge("Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 2));
        edges[1] = Edge("Bottom", Vector2.zero, new Vector2(1, 0), new Vector2(0, 2));
        edges[2] = Edge("Left", Vector2.zero, new Vector2(0, 1), new Vector2(2, 0));
        edges[3] = Edge("Right", new Vector2(1, 0), Vector2.one, new Vector2(2, 0));
    }

    Image Edge(string name, Vector2 min, Vector2 max, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(border, false);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }
}
