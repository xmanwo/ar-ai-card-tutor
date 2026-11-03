using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

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
            typeof(GraphicRaycaster));

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(700f, 500f);
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
        statusRect.sizeDelta = new Vector2(-40f, 96f);
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
        frameRect.sizeDelta = new Vector2(280f, 280f);
        frameRect.anchoredPosition = new Vector2(-170f, 0f);
        frame.GetComponent<RawImage>().color = Color.white;

        // Card list -------------------------------------------------------
        var list = CreateChild("ListText", rootRect, typeof(TextMeshProUGUI));
        var listRect = list.GetComponent<RectTransform>();
        listRect.sizeDelta = new Vector2(340f, 300f);
        listRect.anchoredPosition = new Vector2(160f, 0f);

        var listText = list.GetComponent<TextMeshProUGUI>();
        ApplyDefaultFont(listText);
        listText.text = string.Empty;
        listText.fontSize = 28f;
        listText.color = Color.white;
        listText.alignment = TextAlignmentOptions.TopLeft;

        // Buttons ---------------------------------------------------------
        var buttonRow = CreateChild("ButtonRow", rootRect, typeof(RectTransform));
        var rowRect = buttonRow.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.5f, 0f);
        rowRect.anchorMax = new Vector2(0.5f, 0f);
        rowRect.pivot = new Vector2(0.5f, 0f);
        rowRect.sizeDelta = new Vector2(660f, 72f);
        rowRect.anchoredPosition = new Vector2(0f, 18f);

        var scanButton = CreateButton("Scan", rowRect, new Vector2(-220f, 0f));
        var rescanButton = CreateButton("Rescan", rowRect, new Vector2(0f, 0f));
        var confirmButton = CreateButton("Confirm", rowRect, new Vector2(220f, 0f));

        // Component wiring -------------------------------------------------
        var panel = root.AddComponent<ScanPanel>();
        panel.frameImage = frame.GetComponent<RawImage>();
        panel.statusText = statusText;
        panel.listText = listText;
        panel.scanButton = scanButton.GetComponent<Button>();
        panel.rescanButton = rescanButton.GetComponent<Button>();
        panel.confirmButton = confirmButton.GetComponent<Button>();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);

        Debug.Log($"[ScanPanel] prefab written to {PrefabPath}. " +
                  "Drag it under Main Camera in SampleScene and set Pos (0, -0.15, 1.1).");
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
        rect.sizeDelta = new Vector2(200f, 64f);
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
        tmp.fontSize = 28f;
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
