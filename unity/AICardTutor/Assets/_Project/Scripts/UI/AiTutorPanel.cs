using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds a neighbouring AI panel at runtime, preserving existing prefab overrides.</summary>
public class AiTutorPanel : MonoBehaviour
{
    AiTutorClient client;
    TutorPanel tutorView;
    TMP_Text heading, stepText, title, status, answer, foot, explainLabel, hintLabel;
    Button explain, hint;
    Language previousLanguage;
    AiTutorClient.Availability previousState;

    void Start()
    {
        var tutorPanel = GetComponent<TutorPanel>();
        if (tutorPanel == null || tutorPanel.legalText == null) return;
        tutorView = tutorPanel;
        client = GetComponent<AiTutorClient>();
        if (client == null) client = gameObject.AddComponent<AiTutorClient>();
        client.tutor = tutorPanel.tutor;

        var root = new GameObject("AiTutor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(transform, false);
        ScanPanel.ApplyAiLayout((RectTransform)root.transform);
        var background = root.GetComponent<Image>();
        ScanPanel.StyleFloatingPanel(background);
        background.raycastTarget = false;
        float top = ScanPanel.PanelHeight * .5f;
        heading = Text(root.transform, "Heading", tutorPanel.legalText, 11f,
            new Vector2(410f, 20f), new Vector2(0f, top - 24f));
        heading.characterSpacing = 1.8f;
        heading.color = new Color32(142, 152, 173, 255);
        stepText = Text(root.transform, "Steps", tutorPanel.legalText, 11f,
            new Vector2(410f, 20f), new Vector2(0f, top - 48f));
        stepText.color = new Color32(142, 152, 173, 255);
        title = Text(root.transform, "Title", tutorPanel.legalText, 24f,
            new Vector2(410f, 32f), new Vector2(0f, top - 90f));
        status = Text(root.transform, "Readiness", tutorPanel.legalText, 14f,
            new Vector2(410f, 40f), new Vector2(0f, top - 128f));
        answer = Text(root.transform, "Answer", tutorPanel.legalText, 18f,
            new Vector2(410f, 210f), new Vector2(0f, -120f));
        answer.enableAutoSizing = true;
        answer.fontSizeMin = 15f;
        answer.fontSizeMax = 21f;
        foot = Text(root.transform, "Foot", tutorPanel.legalText, 11f,
            new Vector2(410f, 20f), new Vector2(0f, -262f));
        foot.color = new Color32(127, 138, 159, 255);
        explain = MakeButton(root.transform, "Explain", -100f, tutorPanel.legalText, out explainLabel);
        hint = MakeButton(root.transform, "Hint", 100f, tutorPanel.legalText, out hintLabel);
        explain.onClick.AddListener(() => client.Ask("explain"));
        hint.onClick.AddListener(() => client.Ask("hint"));
        client.Changed += Render;
        previousLanguage = Loc.Current;
        previousState = client.State;

        // The local rule block is a sibling of this panel; keep it above the coach background.
        if (tutorPanel.legalText != null) tutorPanel.legalText.transform.SetAsLastSibling();
        if (tutorPanel.reasonText != null) tutorPanel.reasonText.transform.SetAsLastSibling();
        if (tutorPanel.blockedText != null) tutorPanel.blockedText.transform.SetAsLastSibling();
        // The hand dwell system may already have cached the old ScanPanel buttons in Start.
        // Also safe when its Start runs later: it will collect these controls itself.
        foreach (var hud in FindObjectsByType<HandRescanHud>(FindObjectsSortMode.None))
            hud.RefreshPanelButtons();
        Render();
    }

    void Update()
    {
        if (client != null && (previousLanguage != Loc.Current || previousState != client.State))
        {
            previousLanguage = Loc.Current;
            previousState = client.State;
            Render();
        }
    }

    void Render()
    {
        heading.text = AiTutorClient.Local("COACH · DEEPSEEK", "教练 · DEEPSEEK");
        stepText.text = AiTutorClient.Local("1  Discard   ›   2  Hand   ›   3  Play",
            "1  桌顶   ›   2  手牌   ›   3  出牌");
        var service = tutorView != null && tutorView.tutor != null ? tutorView.tutor : client.tutor;
        var session = service != null ? service.session : null;
        title.text = session == null || session.Discard == null
            ? AiTutorClient.Local("Scan the discard", "扫描桌顶牌")
            : !session.HasHand
                ? AiTutorClient.Local("Scan your hand", "扫描手牌")
                : AiTutorClient.Local("Choose a card", "选择要出的牌");
        explainLabel.text = AiTutorClient.Local("Explain", "解释牌局");
        hintLabel.text = AiTutorClient.Local("Give a hint", "给我提示");
        status.text = client.ReadinessMessage;
        status.color = client.State == AiTutorClient.Availability.Offline
            ? new Color32(245, 185, 66, 255) : new Color32(170, 179, 197, 255);
        answer.text = client.CanAsk ? client.Answer : string.Empty;
        foot.text = AiTutorClient.Local("Local rules ready · AI optional",
            "本地规则可用 · AI 可选");
        explain.interactable = hint.interactable = client.CanAsk;
    }

    static TMP_Text Text(Transform parent, string name, TMP_Text template, float size, Vector2 dimensions, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = template.font;
        text.fontSharedMaterial = template.fontSharedMaterial;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.richText = false;
        text.raycastTarget = false;
        Place(text.rectTransform, dimensions, position);
        return text;
    }

    static Button MakeButton(Transform parent, string name, float x, TMP_Text template, out TMP_Text label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, new Vector2(190f, 54f), new Vector2(x, ScanPanel.FooterCentreY));
        var image = go.GetComponent<Image>();
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        var colours = button.colors;
        colours.normalColor = new Color32(31, 82, 122, 255);
        colours.highlightedColor = new Color32(48, 110, 154, 255);
        colours.pressedColor = new Color32(23, 62, 92, 255);
        colours.selectedColor = colours.highlightedColor;
        colours.disabledColor = new Color32(60, 67, 74, 255);
        button.colors = colours;
        label = Text(go.transform, "Label", template, 18f, new Vector2(182f, 50f), Vector2.zero);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    static void Place(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    void OnDestroy()
    {
        if (client != null) client.Changed -= Render;
    }
}
