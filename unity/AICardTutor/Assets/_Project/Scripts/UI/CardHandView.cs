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
    RectTransform root, handRow, discardSlot, photoRoot;
    GameObject unoPill;
    TMP_Text handTitle, discardTitle, emptyHand;
    TMP_Text tableLabel, discardLabel, discardMeta;
    TMP_Text photoTitle, photoPlaceholder, cropStatus, cropModeText;
    Button cropMode, photoToggle, photoClose;
    TMP_Text debugLabel, photoToggleLabel, cropChip, photoChip, photoCloseLabel;
    CanvasGroup originalPhotoVisibility;
    bool photoDebug;
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
        root = NewRect("CardFaces", transform, new Vector2(ScanPanel.PanelWidth, ScanPanel.PanelHeight),
            Vector2.zero);
        handRow = NewRect("HandRow", root, new Vector2(470, 150), new Vector2(0, -24));
        discardSlot = NewRect("DiscardSlot", root, new Vector2(96, 146), new Vector2(-180, 210));
        handTitle = Text(root, "HandTitle", new Vector2(470, 28), new Vector2(0, 92), 18);
        discardTitle = Text(root, "TopTitle", new Vector2(170, 40), new Vector2(-180, 282), 16);
        discardTitle.alignment = TextAlignmentOptions.Center;
        discardTitle.gameObject.SetActive(false);
        emptyHand = Text(root, "EmptyHand", new Vector2(460, 50), new Vector2(0, -142), 18);
        emptyHand.alignment = TextAlignmentOptions.Center;

        tableLabel = Text(root, "TableLabel", new Vector2(200, 20), new Vector2(-235, 344), 11);
        tableLabel.characterSpacing = 2f;
        tableLabel.color = new Color32(142, 152, 173, 255);
        tableLabel.text = Local("TABLE", "牌桌");

        discardLabel = Text(root, "DiscardLabel", new Vector2(240, 18), new Vector2(-235, 278), 11);
        discardLabel.characterSpacing = 1.5f;
        discardLabel.color = new Color32(127, 138, 159, 255);
        discardLabel.text = Local("DISCARD PILE", "弃牌堆");

        discardMeta = Text(root, "DiscardMeta", new Vector2(205, 90), new Vector2(-25, 242), 17);
        discardMeta.alignment = TextAlignmentOptions.TopLeft;
        discardMeta.color = new Color32(223, 229, 239, 255);

        var dividerRect = NewRect("Divider", root, new Vector2(470, 1), new Vector2(0, 112));
        var dividerImage = dividerRect.gameObject.AddComponent<Image>();
        dividerImage.color = new Color(1f, 1f, 1f, .09f);
        dividerImage.raycastTarget = false;

        previous = PageButton("PreviousCards", "<", 185, -142, () => { page--; Draw(); });
        next = PageButton("NextCards", ">", 232, -142, () => { page++; Draw(); });
        var pill = NewRect("CallUno", root, new Vector2(120f, 28f), new Vector2(175f, 344f));
        var pillBackground = pill.gameObject.AddComponent<Image>();
        pillBackground.raycastTarget = false;
        ScanPanel.StyleUnoPill(pillBackground);
        var unoLabel = Text(pill, "Label", new Vector2(114f, 26f), Vector2.zero, 16f);
        unoLabel.alignment = TextAlignmentOptions.Center;
        unoLabel.fontStyle = FontStyles.Bold;
        unoLabel.color = new Color32(18, 20, 24, 255);
        unoLabel.text = new TextRef("tutor.callUno").Resolve();
        unoPill = pill.gameObject;
        unoPill.SetActive(false);
        BuildPhotoPanel();
        BuildDebugRow();
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
        bool reviewingHand = lastTarget == ScanTarget.Hand && (lastState == ScanState.Reviewing ||
            (owner.client != null && owner.client.RetainingHandDraft));
        bool pickingWildColour = owner.wildColorControls != null && owner.wildColorControls.activeSelf;
        // Stacked table rail: discard on top, the hand below, wheel on the free right while picking.
        const float handLeft = -235f;
        float rowWidth = pickingWildColour ? 280f : 470f;
        float titleWidth = pickingWildColour ? 240f : 470f;
        handRow.sizeDelta = new Vector2(rowWidth, 150f);
        handRow.anchoredPosition = new Vector2(0f, -24f);
        handTitle.rectTransform.sizeDelta = new Vector2(titleWidth, 28f);
        handTitle.rectTransform.anchoredPosition = new Vector2(handLeft + titleWidth * 0.5f, 92f);
        ((RectTransform)previous.transform).anchoredPosition = new Vector2(185f, -142f);
        ((RectTransform)next.transform).anchoredPosition = new Vector2(232f, -142f);
        var cards = HandFaces(reviewingHand);
        var advice = owner.tutor != null ? owner.tutor.Current : null;
        unoPill.SetActive(lastState != ScanState.Scanning && lastState != ScanState.Reviewing &&
            advice != null && advice.UnoAlert);
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
        if (tableLabel != null) tableLabel.text = Local("TABLE", "牌桌");
        if (discardLabel != null) discardLabel.text = Local("DISCARD PILE", "弃牌堆");
        handTitle.text = Local("YOUR HAND", "你的手牌") + $" ({cards.Count}) · {phase}" +
            (pages > 1 ? $"  {page+1}/{pages}" : "");
        previous.gameObject.SetActive(pages > 1 && !pickingWildColour);
        next.gameObject.SetActive(pages > 1 && !pickingWildColour);
        previous.interactable = page > 0;
        next.interactable = page < pages - 1;
        emptyHand.gameObject.SetActive(cards.Count == 0 && !pickingWildColour);
        emptyHand.text = Local("Show your cards, then press Scan hand.",
            "把牌展示给摄像头，然后按扫描手牌。");
        int start = page * HandCardLayout.PageSize;
        int visible = Mathf.Min(HandCardLayout.PageSize, cards.Count - start);
        for (int i = 0; i < visible; i++)
        {
            Face card = cards[start+i];
            float x = HandCardLayout.PositionX(i, visible, rowWidth, 96f);
            MakeCard(handRow, card, new Vector2(x, card.Legal ? 4 : 0), new Vector2(96, 144));
        }
        if (cards.Count == 0 && !pickingWildColour)
        {
            for (int i = 0; i < 5; i++)
            {
                var slot = NewRect("EmptySlot", handRow, new Vector2(58f, 84f),
                    new Vector2((i - 2) * 66f, 0f));
                var image = slot.gameObject.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, .035f);
                image.raycastTarget = false;
                var outline = slot.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(1f, 1f, 1f, .10f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
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
        if (discardMeta != null)
        {
            var meta = new StringBuilder();
            meta.Append(top == null ? Local("No card", "无牌") :
                string.Equals(top.Color, "Wild", System.StringComparison.OrdinalIgnoreCase)
                    ? top.Value : top.Color + " " + top.Value);
            if (!reviewingTop && owner.session != null && !string.IsNullOrEmpty(owner.session.ActiveColor))
                meta.Append('\n').Append(Local("Active colour: ", "生效颜色：")).Append(owner.session.ActiveColor);
            meta.Append('\n').Append(reviewingTop ? Local("Review", "待确认") :
                top != null ? Local("Confirmed", "已确认") : Local("Not scanned", "未扫描"));
            discardMeta.text = meta.ToString();
        }
        if (top != null)
        {
            CardColor activeColour = !reviewingTop && CardFaces.IsWild(CardFaces.ParseValue(top.Value))
                ? CardFaces.ParseColor(owner.session.ActiveColor)
                : CardColor.Unknown;
            Color? activeTint = ActiveWildTint(activeColour);
            MakeCard(discardSlot, top, Vector2.zero, new Vector2(100, 150), activeTint);
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
        photoRoot = NewRect("CapturedPhotoPanel", transform, Vector2.zero, Vector2.zero);
        var background = photoRoot.gameObject.AddComponent<Image>();
        ScanPanel.StyleFloatingPanel(background);
        background.raycastTarget = false;
        photoTitle = Text(photoRoot, "PhotoTitle", Vector2.zero, Vector2.zero, 21);
        var debug = PhotoButton("PhotoDebug", new Vector2(96f, 30f), new Color32(69, 75, 84, 255));
        debugLabel = Text(debug.transform, "Label", new Vector2(92f, 28f), Vector2.zero, 16f);
        debugLabel.alignment = TextAlignmentOptions.Center;
        debug.onClick.AddListener(() => {
            photoDebug = !photoDebug;
            ApplyPhotoLayout();
            RefreshPhotoPanel();
        });
        photoClose = PhotoButton("PhotoClose", new Vector2(58f, 30f), new Color32(69, 75, 84, 255));
        photoCloseLabel = Text(photoClose.transform, "Label", new Vector2(54f, 28f), Vector2.zero, 14f);
        photoCloseLabel.alignment = TextAlignmentOptions.Center;
        photoCloseLabel.text = Local("Close", "关闭");
        photoClose.onClick.AddListener(() => SetPhotoPanelVisible(false));
        originalLabel = Text(photoRoot, "OriginalLabel", new Vector2(165, 22), new Vector2(-60, 169), 16);
        originalLabel.alignment = TextAlignmentOptions.Center;
        originalLabel.text = Local("Original photo", "原始照片");
        focusedLabel = Text(photoRoot, "FocusedLabel", Vector2.zero, Vector2.zero, 18);
        focusedLabel.alignment = TextAlignmentOptions.Center;
        var focusedRect = NewRect("RecognitionPhoto", photoRoot, Vector2.zero, Vector2.zero);
        focusedPhoto = focusedRect.gameObject.AddComponent<RawImage>();
        focusedPhoto.raycastTarget = false;
        focusedPhoto.enabled = false;
        focusedPlaceholder = Text(photoRoot, "FocusPlaceholder", new Vector2(340, 140), new Vector2(0, -77), 20);
        focusedPlaceholder.alignment = TextAlignmentOptions.Center;
        cropStatus = Text(photoRoot, "CropStatus", new Vector2(370, 44), new Vector2(0, -190), 15);
        cropStatus.alignment = TextAlignmentOptions.Center;
        cropMode = PhotoButton("HoldingHandMode", new Vector2(140, 28), new Color(.12f, .28f, .40f, 1));
        ((RectTransform)cropMode.transform).anchoredPosition = new Vector2(112, 169);
        cropMode.onClick.AddListener(() => {
            var provider = owner.client != null && owner.client.capture != null ? owner.client.capture.handRoi : null;
            if (provider == null) return;
            provider.CycleMode();
            RefreshPhotoPanel();
        });
        cropModeText = Text(cropMode.transform, "Mode", new Vector2(136, 26), Vector2.zero, 17);
        cropModeText.alignment = TextAlignmentOptions.Center;
        photoPlaceholder = Text(photoRoot, "PhotoPlaceholder", Vector2.zero, Vector2.zero, 20);
        photoPlaceholder.alignment = TextAlignmentOptions.Center;
        var rect = owner.frameImage.rectTransform;
        rect.SetParent(photoRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        owner.frameImage.raycastTarget = false;
        owner.frameImage.enabled = owner.frameImage.texture != null;
        originalPhotoVisibility = rect.GetComponent<CanvasGroup>();
        if (originalPhotoVisibility == null) originalPhotoVisibility = rect.gameObject.AddComponent<CanvasGroup>();
        cropOverlay = owner.frameImage.GetComponent<ScanCropOverlay>();
        if (cropOverlay == null) cropOverlay = owner.frameImage.gameObject.AddComponent<ScanCropOverlay>();
        ApplyPhotoLayout();
        photoRoot.gameObject.SetActive(false);
    }

    /// <summary>The learner-facing panel; photo/debug pages stay one tap away.</summary>
    void BuildDebugRow()
    {
        photoToggle = SmallButton("PhotoToggle", new Vector2(84f, 28f), new Vector2(-210f, -330f));
        photoToggleLabel = Text(photoToggle.transform, "Label", new Vector2(80f, 26f), Vector2.zero, 13f);
        photoToggleLabel.alignment = TextAlignmentOptions.Center;
        photoToggleLabel.text = Local("Debug", "调试");
        photoToggle.onClick.AddListener(() => SetPhotoPanelVisible(!photoRoot.gameObject.activeSelf));

        cropChip = Text(root, "CropChip", new Vector2(170f, 28f), new Vector2(-95f, -330f), 13f);
        cropChip.alignment = TextAlignmentOptions.Left;
        cropChip.color = new Color32(154, 165, 185, 255);
        cropChip.text = "Crop: Auto";

        photoChip = Text(root, "PhotoChip", new Vector2(80f, 28f), new Vector2(45f, -330f), 13f);
        photoChip.alignment = TextAlignmentOptions.Left;
        photoChip.color = new Color32(154, 165, 185, 255);
        photoChip.text = Local("Photo", "照片");
    }

    Button SmallButton(string name, Vector2 size, Vector2 position)
    {
        var rect = NewRect(name, root, size, position);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color32(45, 52, 64, 255);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    internal void SetPhotoPanelVisible(bool visible)
    {
        if (photoRoot == null) return;
        photoRoot.gameObject.SetActive(visible);
        if (visible)
        {
            photoRoot.transform.SetAsLastSibling();
            RefreshPhotoPanel();
        }
        if (photoToggleLabel != null)
            photoToggleLabel.text = visible ? Local("Close", "关闭") : Local("Debug", "调试");
    }

    Button PhotoButton(string name, Vector2 size, Color colour)
    {
        var rect = NewRect(name, photoRoot, size, Vector2.zero);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    void ApplyPhotoLayout()
    {
        ScanPanel.ApplyPhotoLayout(photoRoot, photoDebug);
        float halfWidth = photoRoot.sizeDelta.x * .5f;
        float halfHeight = photoRoot.sizeDelta.y * .5f;
        photoTitle.rectTransform.sizeDelta = new Vector2(photoRoot.sizeDelta.x - 240f, 32f);
        photoTitle.rectTransform.anchoredPosition = new Vector2(-70f, halfHeight - 24f);
        ((RectTransform)debugLabel.transform.parent).anchoredPosition =
            new Vector2(halfWidth - 120f, halfHeight - 24f);
        if (photoClose != null)
            ((RectTransform)photoClose.transform).anchoredPosition =
                new Vector2(halfWidth - 48f, halfHeight - 24f);
        owner.frameImage.rectTransform.anchoredPosition =
            new Vector2(0f, photoDebug ? 70f : 0f);
        photoPlaceholder.rectTransform.anchoredPosition = owner.frameImage.rectTransform.anchoredPosition;
        photoPlaceholder.rectTransform.sizeDelta = new Vector2(280f, 120f);
        focusedPhoto.rectTransform.anchoredPosition =
            new Vector2(0f, photoDebug ? -85f : 0f);
        focusedLabel.rectTransform.anchoredPosition =
            new Vector2(0f, photoDebug ? -24f : -88f);
        focusedLabel.rectTransform.sizeDelta = new Vector2(photoRoot.sizeDelta.x - 24f, 26f);
        originalLabel.rectTransform.anchoredPosition = new Vector2(-halfWidth + 80f, halfHeight - 52f);
        cropStatus.rectTransform.anchoredPosition = new Vector2(0f, -halfHeight + 44f);
        cropStatus.rectTransform.sizeDelta = new Vector2(photoRoot.sizeDelta.x - 40f, 32f);
        ((RectTransform)cropMode.transform).anchoredPosition =
            new Vector2(-halfWidth + 88f, -halfHeight + 22f);
        originalLabel.gameObject.SetActive(photoDebug);
        cropStatus.gameObject.SetActive(photoDebug);
        cropMode.gameObject.SetActive(photoDebug);
    }

    public void RefreshPhotoPanel()
    {
        if (photoTitle == null || owner.frameImage == null) return;
        bool visible = owner.frameImage.enabled && owner.frameImage.texture != null;
        string pile = lastTarget == ScanTarget.Hand ? Local("Hand", "手牌") : Local("Discard", "桌顶");
        var capture = owner.client != null ? owner.client.capture : null;
        bool focused = visible && lastTarget == ScanTarget.Hand && capture != null &&
            capture.LastScanCropped && capture.LastRecognitionTexture != null &&
            owner.frameImage.texture == capture.LastTexture;
        photoTitle.text = focused && !photoDebug ? Local("Hand crop", "手牌裁剪") : pile + Local(" · Photo", " · 照片");
        debugLabel.text = photoDebug ? Local("Debug on", "调试开启") : Local("Debug", "调试");
        originalPhotoVisibility.alpha = photoDebug || !focused ? 1f : 0f;
        focusedPhoto.enabled = focused;
        focusedPhoto.texture = focused ? capture.LastRecognitionTexture : null;
        if (focused) FitPhoto(focusedPhoto, photoDebug ? 350 : 272, photoDebug ? 154 : 128);
        focusedLabel.text = focused ? (photoDebug ? Local("Recognition crop", "识别裁剪") :
            capture.LastCropNote.Split('\n')[0]) : visible ? Local("Full frame", "全图") : Local("Waiting for a scan", "等待扫描");
        focusedLabel.color = focused ? ScanCropOverlay.Focused : new Color32(209, 213, 219, 255);
        focusedPlaceholder.gameObject.SetActive(photoDebug && !focused);
        focusedPlaceholder.text = lastTarget == ScanTarget.Discard
            ? Local("Discard uses the original photo.", "桌顶扫描使用原始照片。")
            : Local("No crop available.", "暂无裁剪图。");
        cropModeText.text = capture != null && capture.handRoi != null ? capture.handRoi.ModeLabel : "Hand: Auto";
        if (cropChip != null)
            cropChip.text = capture != null && capture.handRoi != null
                ? capture.handRoi.ModeLabel : "Crop: Auto";
        cropMode.interactable = lastState != ScanState.Scanning && capture != null && capture.handRoi != null;
        cropStatus.text = visible && capture != null ? capture.LastCropNote : string.Empty;
        cropStatus.color = focused ? ScanCropOverlay.Focused : ScanCropOverlay.FullFrame;
        cropOverlay.Show(capture, owner.frameImage);
        photoPlaceholder.gameObject.SetActive(!visible);
        photoPlaceholder.text = lastState == ScanState.Scanning
            ? Local("Recognising...", "正在识别……")
            : Local("Photo preview", "照片预览");
        if (visible) FitPhoto(owner.frameImage, photoDebug ? 350 : 272, 128);
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

    Button PageButton(string name, string label, float x, float y,
        UnityEngine.Events.UnityAction action)
    {
        var rect = NewRect(name, root, new Vector2(42, 30), new Vector2(x, y));
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
