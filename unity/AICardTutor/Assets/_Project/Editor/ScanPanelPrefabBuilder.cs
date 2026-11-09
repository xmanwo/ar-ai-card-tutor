using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Generates the three-page scan panel as a prefab so the layout starts from a known-good
/// state. Run it from Tools > AI Card Tutor, then tweak the result by hand.
/// </summary>
public static class ScanPanelPrefabBuilder
{
    const string PrefabFolder = "Assets/_Project/Prefabs/UI";
    const string PrefabPath = PrefabFolder + "/ScanPanel.prefab";

    [MenuItem("Tools/AI Card Tutor/Create Scan Panel Prefab")]
    public static void CreatePrefab()
    {
        EnsureFolders();

        var root = new GameObject(
            "ScanPanel",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster),
            typeof(TrackedDeviceGraphicRaycaster));

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(700f, 660f);
        rootRect.localScale = new Vector3(0.001f, 0.001f, 0.001f);

        // Background ------------------------------------------------------
        var background = CreateChild("Background", rootRect, typeof(Image));
        Stretch(background);
        background.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.09f, 0.92f);

        // Status line -----------------------------------------------------
        var status = CreateChild("StatusText", rootRect, typeof(TextMeshProUGUI));
        var statusRect = status.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.sizeDelta = new Vector2(-40f, 76f);
        statusRect.anchoredPosition = new Vector2(0f, -16f);

        var statusText = status.GetComponent<TextMeshProUGUI>();
        ApplyDefaultFont(statusText);
        statusText.text = "Ready. Look at the cards, then tap Scan.";
        statusText.fontSize = 32f;
        statusText.color = Color.white;
        statusText.alignment = TextAlignmentOptions.Center;

        // Captured frame --------------------------------------------------
        var frame = CreateChild("FrameImage", rootRect, typeof(RawImage));
        var frameRect = frame.GetComponent<RectTransform>();
        frameRect.sizeDelta = new Vector2(260f, 260f);
        frameRect.anchoredPosition = new Vector2(-175f, 90f);
        frame.GetComponent<RawImage>().color = Color.white;

        // Card list -------------------------------------------------------
        var list = CreateChild("ListText", rootRect, typeof(TextMeshProUGUI));
        var listRect = list.GetComponent<RectTransform>();
        listRect.sizeDelta = new Vector2(340f, 260f);
        listRect.anchoredPosition = new Vector2(155f, 90f);

        var listText = list.GetComponent<TextMeshProUGUI>();
        ApplyDefaultFont(listText);
        listText.text = string.Empty;
        listText.fontSize = 28f;
        listText.color = Color.white;
        listText.alignment = TextAlignmentOptions.TopLeft;

        var saved = CreateChild("SavedStateText", rootRect, typeof(TextMeshProUGUI));
        var savedRect = saved.GetComponent<RectTransform>();
        savedRect.sizeDelta = new Vector2(660f, 100f);
        savedRect.anchoredPosition = new Vector2(0f, -183f);
        var savedText = saved.GetComponent<TextMeshProUGUI>();
        ApplyDefaultFont(savedText);
        savedText.text = "Saved hand: not confirmed\nSaved discard: not confirmed\nActive colour: not set";
        savedText.fontSize = 22f;
        savedText.color = Color.white;
        savedText.alignment = TextAlignmentOptions.TopLeft;
        savedText.raycastTarget = false;

        // Buttons ---------------------------------------------------------
        var buttonRow = CreateChild("ButtonRow", rootRect, typeof(RectTransform));
        var rowRect = buttonRow.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0f);
        rowRect.anchorMax = new Vector2(0.5f, 0f);
        rowRect.pivot = new Vector2(0.5f, 0f);
        rowRect.sizeDelta = new Vector2(660f, 72f);
        rowRect.anchoredPosition = new Vector2(0f, 18f);

        var scanButton = CreateButton("Scan Hand", rowRect, new Vector2(-247.5f, 0f));
        var discardButton = CreateButton("Scan Discard", rowRect, new Vector2(-82.5f, 0f));
        var rescanButton = CreateButton("Rescan", rowRect, new Vector2(82.5f, 0f));
        var confirmButton = CreateButton("Confirm", rowRect, new Vector2(247.5f, 0f));

        // Component wiring -------------------------------------------------
        var panel = root.AddComponent<ScanPanel>();
        panel.frameImage = frame.GetComponent<RawImage>();
        panel.statusText = statusText;
        panel.listText = listText;
        panel.savedStateText = savedText;
        panel.scanButton = scanButton.GetComponent<Button>();
        panel.scanDiscardButton = discardButton.GetComponent<Button>();
        panel.rescanButton = rescanButton.GetComponent<Button>();
        panel.confirmButton = confirmButton.GetComponent<Button>();

        ConfigureWildColorControls(panel);

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);

        Debug.Log($"[ScanPanel] prefab written to {PrefabPath}. " +
                  "Drag it under Main Camera in SampleScene and set Pos (0, -0.15, 1.1).");
    }

    // Also upgrades the existing prefab while retaining its object IDs and scene overrides.
    public static void ConfigureWildColorControls(ScanPanel panel)
    {
        panel.frameImage.rectTransform.anchoredPosition = new Vector2(-175f, 90f);
        panel.listText.rectTransform.anchoredPosition = new Vector2(155f, 90f);
        panel.savedStateText.rectTransform.sizeDelta = new Vector2(660f, 100f);
        panel.savedStateText.rectTransform.anchoredPosition = new Vector2(0f, -183f);

        if (panel.wildColorControls == null)
            panel.wildColorControls = CreateChild("WildColorControls", panel.transform, typeof(RectTransform));

        var row = (RectTransform)panel.wildColorControls.transform;
        row.sizeDelta = new Vector2(660f, 64f);
        row.anchoredPosition = new Vector2(0f, -85f);

        if (panel.wildColorText == null)
        {
            var title = CreateChild("SelectionText", row, typeof(TextMeshProUGUI));
            panel.wildColorText = title.GetComponent<TMP_Text>();
        }
        panel.wildColorText.rectTransform.sizeDelta = new Vector2(150f, 64f);
        panel.wildColorText.rectTransform.anchoredPosition = new Vector2(-250f, 0f);
        ApplyDefaultFont(panel.wildColorText);
        panel.wildColorText.text = "Choose\ncolour";
        panel.wildColorText.fontSize = 20f;
        panel.wildColorText.color = Color.white;
        panel.wildColorText.alignment = TextAlignmentOptions.MidlineLeft;
        panel.wildColorText.raycastTarget = false;

        string[] names = { "Red", "Yellow", "Green", "Blue" };
        Color[] colours = {
            new Color(.72f, .18f, .16f), new Color(.9f, .75f, .12f),
            new Color(.12f, .5f, .28f), new Color(.19f, .4f, .8f)
        };
        for (int i = 0; i < names.Length; i++)
        {
            var existing = row.Find(names[i]);
            var go = existing != null ? existing.gameObject : CreateButton(names[i], row, Vector2.zero);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(100f, 64f);
            rect.anchoredPosition = new Vector2(-112.5f + i * 110f, 0f);
            go.GetComponent<Image>().color = colours[i];
            var label = go.GetComponentInChildren<TMP_Text>();
            label.fontSize = 22f;
            label.color = names[i] == "Yellow" ? Color.black : Color.white;
            var button = go.GetComponent<Button>();
            while (button.onClick.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            UnityEventTools.AddStringPersistentListener(button.onClick, panel.SelectWildColor, names[i]);
        }

        panel.wildColorControls.SetActive(false);
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
            AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "UI");
    }

    static GameObject CreateChild(string name, Transform parent, params System.Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);

        if (go.GetComponent<RectTransform>() == null)
            go.AddComponent<RectTransform>();

        return go;
    }

    static GameObject CreateButton(string label, Transform parent, Vector2 position)
    {
        var go = CreateChild(label, parent, typeof(Image), typeof(Button));

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(160f, 64f);
        rect.anchoredPosition = position;

        var image = go.GetComponent<Image>();
        image.color = new Color(0.24f, 0.44f, 0.85f, 1f);

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        var text = CreateChild("Label", rect, typeof(TextMeshProUGUI));
        Stretch(text);

        var tmp = text.GetComponent<TextMeshProUGUI>();
        ApplyDefaultFont(tmp);
        tmp.text = label;
        tmp.fontSize = 24f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return go;
    }

    static void Stretch(GameObject go)
    {
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void ApplyDefaultFont(TMP_Text text)
    {
        if (text.font == null && TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
    }
}
