using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>World-space card faces for the review draft and the separately confirmed piles.</summary>
public class CardHandView : MonoBehaviour
{
    public bool IsAvailable { get; private set; }
    ScanPanel owner;
    RectTransform root, handRow, discardSlot;
    TMP_Text handTitle, discardTitle, emptyHand;
    TMP_Text photoTitle, photoPlaceholder, cropStatus, cropModeText;
    Button cropMode;
    ScanCropOverlay cropOverlay;
    RawImage focusedPhoto;
    TMP_Text originalLabel, focusedLabel, focusedPlaceholder;
    Button previous, next;
    TMP_FontAsset font;
    TutorService boundTutor;
    ScanState lastState;
    ScanTarget lastTarget;
    IReadOnlyList<CardEntry> lastDraft;
    int page;
    string signature;

    sealed class Face
    {
        public string Id, Color, Value;
        public bool Uncertain, Legal;
    }

    public void Initialize(ScanPanel panel)
    {
        if (root != null) return;
        owner = panel;
        lastTarget = panel.ActiveTarget;
        IsAvailable = UnoCardArt.Get("Blue", "5") != null;
        if (!IsAvailable)
        {
            Debug.LogWarning("[CardHandView] Card textures unavailable; keeping the text view.");
            return;
        }
        font = owner.statusText != null ? owner.statusText.font : TMP_Settings.defaultFontAsset;
        root = NewRect("CardFaces", transform, new Vector2(700, 670), Vector2.zero);
        handRow = NewRect("HandRow", root, new Vector2(530, 150), new Vector2(65, 145));
        discardSlot = NewRect("DiscardSlot", root, new Vector2(92, 138), new Vector2(-280, 140));
        handTitle = Text(root, "HandTitle", new Vector2(370, 28), new Vector2(-15, 236), 21);
        discardTitle = Text(root, "TopTitle", new Vector2(112, 42), new Vector2(-280, 232), 18);
        discardTitle.alignment = TextAlignmentOptions.Center;
        emptyHand = Text(root, "EmptyHand", new Vector2(480, 110), new Vector2(65, 145), 23);
        emptyHand.alignment = TextAlignmentOptions.Center;
        previous = PageButton("PreviousCards", "<", 260, () => { page--; Draw(); });
        next = PageButton("NextCards", ">", 310, () => { page++; Draw(); });
        BuildPhotoPanel();
        if (owner.wildColorControls != null)
            owner.wildColorControls.transform.SetAsLastSibling();
        boundTutor = owner.tutor;
        if (boundTutor != null) boundTutor.Changed += AdviceChanged;
        foreach (var hud in FindObjectsByType<HandRescanHud>(FindObjectsSortMode.None))
            hud.RefreshPanelButtons();
        Draw();
    }

    public void Render(ScanState state, ScanTarget target, IReadOnlyList<CardEntry> draft)
    {
        lastState = state;
        lastTarget = target;
        lastDraft = draft;
        if (IsAvailable) Draw();
    }

    void AdviceChanged(TutorAdvice advice) { if (IsAvailable) Draw(); }

    List<Face> HandFaces(bool reviewing)
    {
        var result = new List<Face>();
        if (reviewing && lastDraft != null)
        {
            for (int i = 0; i < lastDraft.Count; i++)
            {
                var card = lastDraft[i];
                if (card != null) result.Add(new Face {
                    Id = "draft-" + i, Color = card.color, Value = card.value, Uncertain = card.uncertain
                });
            }
        }
        else if (owner.session != null)
        {
            var advice = owner.tutor != null ? owner.tutor.Current : null;
            foreach (var card in owner.session.Hand)
            {
                bool legal = false;
                if (advice != null && advice.Ready)
                    foreach (var candidate in advice.Legal)
                        if (candidate.id == card.id) { legal = true; break; }
                result.Add(new Face { Id = card.id, Color = card.color, Value = card.value,
                    Uncertain = card.uncertain, Legal = legal });
            }
        }
        return result;
    }

    void Draw()
    {
        if (root == null) return;
        RefreshPhotoPanel();
        bool reviewingHand = lastTarget == ScanTarget.Hand && lastState == ScanState.Reviewing;
        bool pickingWildColour = owner.wildColorControls != null && owner.wildColorControls.activeSelf;
        // Reserve the right edge for the full-size wheel and its label while picking.
        const float handLeft = -200f;
        float rowWidth = pickingWildColour ? 380f : 530f;
        float titleWidth = pickingWildColour ? 260f : 370f;
        handRow.sizeDelta = new Vector2(rowWidth, 150f);
        handRow.anchoredPosition = new Vector2(handLeft + rowWidth * 0.5f, 145f);
        handTitle.rectTransform.sizeDelta = new Vector2(titleWidth, 28f);
        handTitle.rectTransform.anchoredPosition = new Vector2(handLeft + titleWidth * 0.5f, 236f);
        ((RectTransform)previous.transform).anchoredPosition = new Vector2(pickingWildColour ? 95f : 260f, 236f);
        ((RectTransform)next.transform).anchoredPosition = new Vector2(pickingWildColour ? 145f : 310f, 236f);
        var cards = HandFaces(reviewingHand);
        var stamp = new StringBuilder(reviewingHand ? "review:" : "saved:");
        foreach (var card in cards) stamp.Append(card.Id).Append('/').Append(card.Color).Append('/').Append(card.Value).Append(';');
        string current = stamp.ToString();
        if (current != signature) { page = 0; signature = current; }
        int pages = HandCardLayout.PageCount(cards.Count);
        page = Mathf.Clamp(page, 0, pages - 1);
        Clear(handRow);
        Clear(discardSlot);
        string phase = reviewingHand ? Local("Review", "待确认") : owner.session != null && owner.session.HasHand
            ? Local("Saved", "已保存") : Local("Not saved", "未保存");
        handTitle.text = Local("Hand", "手牌") + $" ({cards.Count}) · {phase}" + (pages > 1 ? $"  {page+1}/{pages}" : "");
        previous.gameObject.SetActive(pages > 1);
        next.gameObject.SetActive(pages > 1);
        previous.interactable = page > 0;
        next.interactable = page < pages - 1;
        emptyHand.gameObject.SetActive(cards.Count == 0 && !pickingWildColour);
        emptyHand.text = Local("Scan Hand to show your cards here.", "扫描手牌后在这里显示牌面。");
        int start = page * HandCardLayout.PageSize;
        int visible = Mathf.Min(HandCardLayout.PageSize, cards.Count - start);
        for (int i = 0; i < visible; i++)
        {
            Face card = cards[start+i];
            float x = HandCardLayout.PositionX(i, visible, rowWidth, 96f);
            MakeCard(handRow, card, new Vector2(x, card.Legal ? 4 : 0), new Vector2(96, 144));
        }

        bool reviewingTop = lastTarget == ScanTarget.Discard && lastState == ScanState.Reviewing;
        Face top = null;
        if (reviewingTop && lastDraft != null && lastDraft.Count == 1)
        {
            var card = lastDraft[0];
            if (card != null) top = new Face { Color = card.color, Value = card.value, Uncertain = card.uncertain };
        }
        else if (!reviewingTop && owner.session != null && owner.session.Discard != null)
        {
            var card = owner.session.Discard;
            top = new Face { Color = card.color, Value = card.value, Uncertain = card.uncertain };
        }
        discardTitle.text = Local("Top card", "桌顶牌") + "\n" +
            (reviewingTop ? Local("Review", "待确认") : top != null ? Local("Saved", "已保存") : Local("Not saved", "未保存"));
        if (top != null)
        {
            CardColor activeColour = !reviewingTop && CardFaces.IsWild(CardFaces.ParseValue(top.Value))
                ? CardFaces.ParseColor(owner.session.ActiveColor)
                : CardColor.Unknown;
            Color? activeTint = ActiveWildTint(activeColour);
            MakeCard(discardSlot, top, Vector2.zero, new Vector2(92, 138), activeTint);
            if (activeTint.HasValue)
                ShowActiveWildColour(activeColour, activeTint.Value);
        }
        else
        {
            var message = Text(discardSlot, "NoTop", new Vector2(100, 100), Vector2.zero, 20);
            message.alignment = TextAlignmentOptions.Center;
            message.text = reviewingTop && lastDraft != null && lastDraft.Count > 1
                ? lastDraft.Count + Local(" cards\nNeed 1", " 张牌\n需要 1 张")
                : Local("Scan\nDiscard", "扫描\n桌顶");
        }
    }

    void BuildPhotoPanel()
    {
        if (owner.frameImage == null) return;
        // One column under the Rescan HUD: original context above, actual recognition input below.
        var photoRoot = NewRect("CapturedPhotoPanel", transform, new Vector2(400, 520), new Vector2(-560, -60));
        var background = photoRoot.gameObject.AddComponent<Image>();
        background.color = new Color(.045f, .07f, .10f, .96f);
        background.raycastTarget = false;
        photoTitle = Text(photoRoot, "PhotoTitle", new Vector2(190, 34), new Vector2(-75, 234), 21);
        originalLabel = Text(photoRoot, "OriginalLabel", new Vector2(350, 22), new Vector2(0, 200), 16);
        originalLabel.alignment = TextAlignmentOptions.Center;
        originalLabel.text = Local("Original photo", "原始照片");
        focusedLabel = Text(photoRoot, "FocusedLabel", new Vector2(350, 24), new Vector2(0, 20), 18);
        focusedLabel.alignment = TextAlignmentOptions.Center;
        var focusedRect = NewRect("RecognitionPhoto", photoRoot, new Vector2(350, 210), new Vector2(0, -100));
        focusedPhoto = focusedRect.gameObject.AddComponent<RawImage>();
        focusedPhoto.raycastTarget = false;
        focusedPhoto.enabled = false;
        focusedPlaceholder = Text(photoRoot, "FocusPlaceholder", new Vector2(340, 205), new Vector2(0, -100), 20);
        focusedPlaceholder.alignment = TextAlignmentOptions.Center;
        cropStatus = Text(photoRoot, "CropStatus", new Vector2(370, 44), new Vector2(0, -235), 15);
        cropStatus.alignment = TextAlignmentOptions.Center;
        cropStatus.overflowMode = TextOverflowModes.Ellipsis;
        var modeRect = NewRect("HoldingHandMode", photoRoot, new Vector2(140, 28), new Vector2(100, 234));
        var modeImage = modeRect.gameObject.AddComponent<Image>();
        modeImage.color = new Color(.12f, .28f, .40f, 1);
        cropMode = modeRect.gameObject.AddComponent<Button>();
        cropMode.targetGraphic = modeImage;
        cropMode.onClick.AddListener(() => {
            var provider = owner.client != null && owner.client.capture != null ? owner.client.capture.handRoi : null;
            if (provider == null) return;
            provider.CycleMode();
            RefreshPhotoPanel();
        });
        cropModeText = Text(modeRect, "Mode", new Vector2(136, 26), Vector2.zero, 17);
        cropModeText.alignment = TextAlignmentOptions.Center;
        photoPlaceholder = Text(photoRoot, "PhotoPlaceholder", new Vector2(340, 140), new Vector2(0, 115), 20);
        photoPlaceholder.alignment = TextAlignmentOptions.Center;
        var rect = owner.frameImage.rectTransform;
        rect.SetParent(photoRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(0, 115);
        owner.frameImage.raycastTarget = false;
        owner.frameImage.enabled = owner.frameImage.texture != null;
        cropOverlay = owner.frameImage.GetComponent<ScanCropOverlay>();
        if (cropOverlay == null) cropOverlay = owner.frameImage.gameObject.AddComponent<ScanCropOverlay>();
    }

    public void RefreshPhotoPanel()
    {
        if (photoTitle == null || owner.frameImage == null) return;
        bool visible = owner.frameImage.enabled && owner.frameImage.texture != null;
        string pile = lastTarget == ScanTarget.Hand ? Local("Hand", "手牌") : Local("Discard", "桌顶");
        photoTitle.text = pile + (lastState == ScanState.Scanning ? Local(" · Live", " · 实时") : Local(" · Photo", " · 照片"));
        var capture = owner.client != null ? owner.client.capture : null;
        bool focused = visible && lastTarget == ScanTarget.Hand && capture != null &&
            capture.LastScanCropped && capture.LastRecognitionTexture != null &&
            owner.frameImage.texture == capture.LastTexture;
        if (focusedPhoto != null)
        {
            focusedPhoto.enabled = focused;
            focusedPhoto.texture = focused ? capture.LastRecognitionTexture : null;
            if (focused) FitPhoto(focusedPhoto, 350, 210);
        }
        if (focusedLabel != null)
        {
            focusedLabel.text = lastTarget == ScanTarget.Discard ? Local("Full-frame recognition", "全图识别") :
                focused ? Local("Hand crop · Recognition input", "手牌裁剪 · 实际识别区域") : Local("Hand focus · Waiting", "手牌放大 · 等待定位");
            focusedLabel.color = focused ? ScanCropOverlay.Focused : ScanCropOverlay.FullFrame;
        }
        if (focusedPlaceholder != null)
        {
            focusedPlaceholder.gameObject.SetActive(!focused);
            focusedPlaceholder.text = lastTarget == ScanTarget.Discard
                ? Local("Discard scanning uses the original photo.", "桌顶扫描使用原始照片。")
                : visible ? Local("No focused crop in this frame.\nRecognition used the original photo.", "本帧尚未裁剪。\n识别使用上方原始照片。")
                : Local("The focused hand image appears here when a crop is available.", "定位到手牌后，在这里显示放大识别图。");
        }
        if (cropModeText != null)
            cropModeText.text = capture != null && capture.handRoi != null ? capture.handRoi.ModeLabel : "Hand: Auto";
        if (cropMode != null)
            cropMode.interactable = lastState != ScanState.Scanning && capture != null && capture.handRoi != null;
        if (cropStatus != null)
        {
            cropStatus.text = visible && capture != null ? capture.LastCropNote : Local("Green box = recognised area", "绿色框为识别区域");
            cropStatus.color = capture != null && capture.LastScanCropped ? ScanCropOverlay.Focused : ScanCropOverlay.FullFrame;
        }
        if (cropOverlay != null) cropOverlay.Show(capture, owner.frameImage);
        photoPlaceholder.gameObject.SetActive(!visible);
        photoPlaceholder.text = lastState == ScanState.Scanning
            ? Local("Recognising...", "正在识别……")
            : Local("The captured photo appears here after a scan.", "扫描结束后在这里显示识别照片。");
        if (visible) FitPhoto(owner.frameImage, 350, 140);
    }

    static void FitPhoto(RawImage image, float width, float height)
    {
        var texture = image.texture;
        float fit = Mathf.Min(width / texture.width, height / texture.height);
        image.rectTransform.sizeDelta = new Vector2(texture.width * fit, texture.height * fit);
    }

    void ShowActiveWildColour(CardColor colour, Color tint)
    {
        var size = new Vector2(120f, 24f);
        var rect = NewRect("ActiveWildColour", discardSlot, size, new Vector2(0f, -88f));
        var background = rect.gameObject.AddComponent<Image>();
        background.color = tint;
        background.raycastTarget = false;
        var label = Text(rect, "Label", size, Vector2.zero, 15f);
        string colourName = Loc.T(CardFaces.ColorKey(colour)).ToUpperInvariant();
        label.text = new TextRef("tutor.activeColourLabel", new TextVar("colour", colourName)).Resolve();
        label.alignment = TextAlignmentOptions.Center;
        label.color = colour == CardColor.Yellow ? new Color32(16, 16, 16, 255) : Color.white;
    }

    static Color? ActiveWildTint(CardColor colour) => colour switch
    {
        CardColor.Red => new Color32(165, 39, 49, 255),
        CardColor.Green => new Color32(24, 109, 64, 255),
        CardColor.Blue => new Color32(32, 84, 175, 255),
        CardColor.Yellow => new Color32(240, 195, 43, 255),
        _ => null
    };

    void MakeCard(Transform parent, Face card, Vector2 position, Vector2 size, Color? activeTint = null)
    {
        var rect = NewRect("Card_" + card.Color + "_" + card.Value, parent, size, position);
        Sprite sprite = UnoCardArt.Get(card.Color, card.Value);
        var shadow = NewRect("Shadow", rect, size, new Vector2(3, -4)).gameObject.AddComponent<Image>();
        shadow.sprite = sprite;
        shadow.color = new Color(0, 0, 0, .38f);
        shadow.raycastTarget = false;
        var image = NewRect("Face", rect, size, Vector2.zero).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        if (sprite == null)
        {
            image.color = new Color(.2f, .24f, .28f, 1);
            var label = Text(rect, "UnknownFace", size, Vector2.zero, 22);
            label.text = "?";
            label.alignment = TextAlignmentOptions.Center;
        }
        if (activeTint.HasValue || card.Legal || card.Uncertain)
        {
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = activeTint ?? (card.Uncertain ? new Color(1, .65f, .08f) : new Color(.2f, 1, .5f));
            outline.effectDistance = new Vector2(3, -3);
        }
        if (card.Uncertain)
        {
            var badge = NewRect("Uncertain", rect, new Vector2(22, 24), new Vector2(-size.x/2+13, -size.y/2+15));
            var background = badge.gameObject.AddComponent<Image>();
            background.color = new Color(.65f, .27f, 0, 1);
            background.raycastTarget = false;
            var label = Text(badge, "Question", new Vector2(22, 24), Vector2.zero, 20);
            label.alignment = TextAlignmentOptions.Center;
            label.text = "?";
        }
    }

    Button PageButton(string name, string label, float x, UnityEngine.Events.UnityAction action)
    {
        var rect = NewRect(name, root, new Vector2(42, 30), new Vector2(x, 236));
        var background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(.12f, .28f, .40f, 1);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(action);
        var text = Text(rect, "Label", new Vector2(40, 28), Vector2.zero, 22);
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    TMP_Text Text(Transform parent, string name, Vector2 size, Vector2 position, float fontSize)
    {
        var rect = NewRect(name, parent, size, position);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            child.SetActive(false);
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }
    }

    static string Local(string en, string zh) => Loc.Current == Language.Zh ? zh : en;

    void OnDestroy()
    {
        if (boundTutor != null) boundTutor.Changed -= AdviceChanged;
    }
}
