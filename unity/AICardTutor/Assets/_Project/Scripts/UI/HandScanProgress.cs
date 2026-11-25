using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Shared half-second dwell for the tracked finger and Unity UI pointers.</summary>
public sealed class HandScanProgress : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    const int HandPointer = int.MinValue;
    internal const float HoldSeconds = .5f;
    static readonly Color Cyan = new Color32(26, 155, 175, 255);
    static readonly Color Green = new Color32(21, 123, 82, 255);
    static Sprite whiteSprite;

    readonly HashSet<int> pointers = new HashSet<int>();
    ScanPanel panel;
    Image fill;
    TMP_Text label;
    float hold, holdStartedAt = -1f;
    bool fired;

    internal bool Holding => pointers.Count > 0 && !fired && panel.CanHoldHandScan;

    internal void Initialize(ScanPanel owner)
    {
        panel = owner;
        label = GetComponentInChildren<TMP_Text>();
        var bar = new GameObject("HandScanFill", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)bar.transform;
        rect.SetParent(transform, false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(3, 3);
        rect.offsetMax = new Vector2(-3, -3);
        if (whiteSprite == null)
        {
            var texture = Texture2D.whiteTexture;
            whiteSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f));
        }
        fill = bar.GetComponent<Image>();
        fill.sprite = whiteSprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.raycastTarget = false;
    }

    public void OnPointerEnter(PointerEventData eventData) => SetHover(eventData.pointerId, true);
    public void OnPointerExit(PointerEventData eventData) => SetHover(eventData.pointerId, false);
    public void SetHandHover(bool active) => SetHover(HandPointer, active);

    void SetHover(int pointer, bool entered)
    {
        if (entered)
        {
            if (pointers.Add(pointer) && Holding)
                panel.FreezeHandScanForHover();
        }
        else if (pointers.Remove(pointer) && pointers.Count == 0)
        {
            hold = 0f;
            holdStartedAt = -1f;
            fired = false;
        }
    }

    internal void Tick(float now)
    {
        if (!Holding)
        {
            hold = 0f;
            holdStartedAt = -1f;
            return;
        }

        if (holdStartedAt < 0f) holdStartedAt = now;
        hold = now - holdStartedAt;
        if (hold < HoldSeconds) return;
        fired = true;
        panel.ToggleHandScanLock();
    }

    internal void Display(bool automatic, bool locked, bool ready, bool scanning, float remaining, float period)
    {
        fill.enabled = automatic;
        if (!automatic)
        {
            label.fontSize = 22f;
            label.text = new TextRef("scan.rescan").Resolve();
            return;
        }

        label.fontSize = 15f;
        if (Holding)
            Show("scan.holding", hold / HoldSeconds, Cyan, HoldSeconds - hold);
        else if (locked && panel.HandScanCanRun)
            Show("scan.locked", 1f, Green);
        else if (scanning)
            Show("scan.handScanning", 1f, Cyan);
        else if (ready)
            Show("scan.autoHand", 1f - remaining / period, Cyan, remaining);
        else
            Show("scan.waitingHand", 0f, Cyan);
    }

    void Show(string key, float progress, Color colour, float seconds = 0f)
    {
        fill.color = colour;
        fill.fillAmount = Mathf.Clamp01(progress);
        label.text = new TextRef(key, new TextVar("seconds",
            seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))).Resolve();
    }

    void OnDisable()
    {
        pointers.Clear();
        hold = 0f;
        holdStartedAt = -1f;
        fired = false;
    }
}
