using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds a neighbouring AI panel at runtime, preserving existing prefab overrides.</summary>
public class AiTutorPanel : MonoBehaviour
{
    AiTutorClient client;
    TMP_Text heading, status, answer, explainLabel, hintLabel;
    Button explain, hint;
    Language previousLanguage;
    AiTutorClient.Availability previousState;

    void Start()
    {
        var tutorPanel = GetComponent<TutorPanel>();
        if (tutorPanel == null || tutorPanel.legalText == null) return;
        client = GetComponent<AiTutorClient>();
        if (client == null) client = gameObject.AddComponent<AiTutorClient>();
        client.tutor = tutorPanel.tutor;

        var root = new GameObject("AiTutor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(transform, false);
        Place((RectTransform)root.transform, new Vector2(700f, 430f), new Vector2(720f, 110f));
        var background = root.GetComponent<Image>();
        background.color = new Color(0.045f, 0.07f, 0.10f, 0.96f);
        background.raycastTarget = false;
        heading = Text(root.transform, "Heading", tutorPanel.legalText, 28f,
            new Vector2(650f, 45f), new Vector2(0f, 175f));
        status = Text(root.transform, "Readiness", tutorPanel.legalText, 22f,
            new Vector2(650f, 36f), new Vector2(0f, 127f));
        answer = Text(root.transform, "Answer", tutorPanel.legalText, 28f,
            new Vector2(650f, 200f), Vector2.zero);
        answer.enableAutoSizing = true;
        answer.fontSizeMin = 22f;
        answer.fontSizeMax = 28f;
        explain = MakeButton(root.transform, "Explain", -170f, tutorPanel.legalText, out explainLabel);
        hint = MakeButton(root.transform, "Hint", 170f, tutorPanel.legalText, out hintLabel);
        explain.onClick.AddListener(() => client.Ask("explain"));
        hint.onClick.AddListener(() => client.Ask("hint"));
        client.Changed += Render;
        previousLanguage = Loc.Current;
        previousState = client.State;
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
        heading.text = AiTutorClient.Local("AI tutor · DeepSeek", "AI 助手 · DeepSeek");
        explainLabel.text = AiTutorClient.Local("Explain", "解释牌局");
        hintLabel.text = AiTutorClient.Local("Give a hint", "给我提示");
        status.text = client.ReadinessMessage;
        answer.text = string.IsNullOrEmpty(client.Answer)
            ? AiTutorClient.Local("Confirm your cards, then ask for an explanation or a hint.",
                "确认桌顶和手牌后，点击按钮获取解释或提示。") : client.Answer;
        bool ready = client.State == AiTutorClient.Availability.Ready;
        explain.interactable = hint.interactable = ready && !client.Busy;
    }

    void LateUpdate()
    {
        if (client == null || explain == null) return;
        bool ready = client.State == AiTutorClient.Availability.Ready;
        explain.interactable = hint.interactable = ready && !client.Busy;
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
        Place((RectTransform)go.transform, new Vector2(310f, 64f), new Vector2(x, -165f));
        var image = go.GetComponent<Image>();
        image.color = new Color(0.12f, 0.32f, 0.48f, 1f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        label = Text(go.transform, "Label", template, 28f, new Vector2(300f, 60f), Vector2.zero);
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
