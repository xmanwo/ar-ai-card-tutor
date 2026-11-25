using System.IO;
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
    const string ArtFolder = "Assets/_Project/Art";
    const string DiscPath = ArtFolder + "/WildDisc.png";

    /// <summary>Diameter of the Wild colour wheel, in canvas units.</summary>
    const float WheelDiameter = 120f;

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
        rootRect.sizeDelta = new Vector2(ScanPanel.PanelWidth, ScanPanel.PanelHeight);
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
        ConfigureTutorPanel(panel);

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
        if (panel.wildColorControls == null)
            panel.wildColorControls = CreateChild("WildColorControls", panel.transform, typeof(RectTransform));

        var row = (RectTransform)panel.wildColorControls.transform;

        if (panel.wildColorText == null)
        {
            var title = CreateChild("SelectionText", row, typeof(TextMeshProUGUI));
            panel.wildColorText = title.GetComponent<TMP_Text>();
        }

        ApplyDefaultFont(panel.wildColorText);
        panel.wildColorText.text = "Choose colour";
        panel.wildColorText.fontSize = 18f;
        panel.wildColorText.color = Color.white;
        panel.wildColorText.raycastTarget = false;

        // Four quarter arcs of one disc. The slices deliberately overlap: all four bounding boxes
        // are identical, so the hand HUD tells them apart by angle (WildColorSector), not by rect.
        string[] names = { "Red", "Yellow", "Green", "Blue" };
        // TryHit measures the angle counter-clockwise from +X, the way Mathf.Atan2 does. Image
        // fills run clockwise from the chosen origin, so each origin sits one quarter ahead of its
        // slice start:
        //   Red     0.. 90  -> origin Top    (starts at 90,  clockwise reaches 0)
        //   Yellow 90..180  -> origin Left   (starts at 180, clockwise reaches 90)
        //   Green 180..270  -> origin Bottom (starts at 270, clockwise reaches 180)
        //   Blue  270..360  -> origin Right  (starts at 0,   clockwise reaches 270)
        float[] starts = { 0f, 90f, 180f, 270f };
        int[] origins =
        {
            (int)Image.Origin360.Top,
            (int)Image.Origin360.Left,
            (int)Image.Origin360.Bottom,
            (int)Image.Origin360.Right
        };
        Color[] colours =
        {
            new Color(.72f, .18f, .16f),
            new Color(.9f, .75f, .12f),
            new Color(.12f, .5f, .28f),
            new Color(.19f, .4f, .8f)
        };

        for (int i = 0; i < names.Length; i++)
        {
            var existing = row.Find(names[i]);
            var go = existing != null ? existing.gameObject : CreateChild(names[i], row, typeof(Image));
            var rect = (RectTransform)go.transform;

            rect.sizeDelta = new Vector2(WheelDiameter, WheelDiameter);
            rect.anchoredPosition = Vector2.zero;

            // Slices carry no text label. Drop anything the square-button version left behind.
            var stale = go.transform.Find("Label");

            if (stale != null)
                Object.DestroyImmediate(stale.gameObject);

            var image = go.GetComponent<Image>();

            if (image == null)
                image = go.AddComponent<Image>();

            image.color = colours[i];
            // A real asset, not a runtime Sprite: the prefab has to store something that still
            // resolves after a domain reload, otherwise the wheel disappears in the editor.
            image.sprite = EnsureDiscSprite();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = origins[i];
            image.fillAmount = 0.25f;
            image.fillClockwise = true;
            // Raycast off on purpose: four overlapping targets would answer with the same slice
            // everywhere. The hand HUD reads the transform directly.
            image.raycastTarget = false;

            var sector = go.GetComponent<WildColorSector>();

            if (sector == null)
                sector = go.AddComponent<WildColorSector>();

            sector.color = names[i];
            sector.startAngle = starts[i];
            sector.spanDegrees = 90f;

            var button = go.GetComponent<Button>();

            if (button == null)
                button = go.AddComponent<Button>();

            button.targetGraphic = image;

            while (button.onClick.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(button.onClick, 0);

            UnityEventTools.AddStringPersistentListener(button.onClick, panel.SelectWildColor, names[i]);
        }

        panel.wildColorControls.SetActive(false);
    }

    /// <summary>
    /// Adds the tutor block to the existing prefab without rebuilding it, so object IDs and the
    /// scene overrides that point at them survive. Prefer this over CreatePrefab once the panel is
    /// already placed in SampleScene.
    /// </summary>
    [MenuItem("Tools/AI Card Tutor/Upgrade Scan Panel With Tutor")]
    public static void UpgradeWithTutor()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            Debug.LogError($"[ScanPanel] {PrefabPath} not found. Run Create Scan Panel Prefab first.");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);

        try
        {
            var panel = root.GetComponent<ScanPanel>();

            if (panel == null)
            {
                Debug.LogError($"[ScanPanel] {PrefabPath} has no ScanPanel component.");
                return;
            }

            ConfigureWildColorControls(panel);
            ConfigureTutorPanel(panel);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[ScanPanel] tutor block added to {PrefabPath}. " +
                  "Add a TutorService to the scene, then confirm a hand to fill it.");
    }

    /// <summary>
    /// Grows the canvas so three tutor lines fit under the colour picker, then wires the component.
    /// The button row is anchored to the bottom, so it follows the taller canvas on its own.
    /// </summary>
    public static void ConfigureTutorPanel(ScanPanel panel)
    {
        var rootRect = (RectTransform)panel.transform;
        rootRect.sizeDelta = new Vector2(ScanPanel.PanelWidth, ScanPanel.PanelHeight);
        var offset = panel.localOffset;
        offset.y = ScanPanel.FloatingOffsetY;
        panel.localOffset = offset;

        // ScanPanel.ApplyLayout reasserts the geometry over existing scene overrides.
        if (panel.frameImage != null)
        {
            panel.frameImage.rectTransform.sizeDelta = new Vector2(180f, 180f);
            panel.frameImage.rectTransform.anchoredPosition = new Vector2(-240f, 158f + ScanPanel.CardRowOffset);
        }

        if (panel.listText != null)
        {
            panel.listText.rectTransform.sizeDelta = new Vector2(280f, 180f);
            panel.listText.rectTransform.anchoredPosition = new Vector2(190f, 158f + ScanPanel.CardRowOffset);
            // Seven cards at 28pt is exactly the height of the box; clip rather than grow.
            panel.listText.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (panel.statusText != null)
        {
            panel.statusText.rectTransform.sizeDelta = new Vector2(-40f, 72f);
            panel.statusText.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            panel.statusText.fontSize = 28f;
            // A long failure message must not push into the frame row underneath.
            panel.statusText.overflowMode = TextOverflowModes.Ellipsis;
        }

        if (panel.savedStateText != null)
        {
            panel.savedStateText.rectTransform.sizeDelta = new Vector2(660f, 84f);
            panel.savedStateText.rectTransform.anchoredPosition = new Vector2(0f, -176f);
            panel.savedStateText.overflowMode = TextOverflowModes.Ellipsis;
            panel.savedStateText.enabled = false;
        }

        panel.ApplyScanControlsLayout();
        var scanProgressLabel = panel.rescanButton != null
            ? panel.rescanButton.GetComponentInChildren<TMP_Text>() : null;
        if (scanProgressLabel != null)
        {
            scanProgressLabel.text = new TextRef(panel.automaticHandScan ? "scan.waitingHand" : "scan.rescan").Resolve();
            scanProgressLabel.fontSize = panel.automaticHandScan ? 15f : 22f;
        }

        var tutor = panel.GetComponent<TutorPanel>();

        if (tutor == null)
            tutor = panel.gameObject.AddComponent<TutorPanel>();

        // The prefab stores its own copy of this field, so a new default in code alone would leave
        // the old value of 3 in place - and three reasons is what used to spill into the row below.
        tutor.maxReasonLines = 2;

        // Reserve preview space for the two explanation blocks. Runtime fits their actual text.
        tutor.legalText = EnsureTutorLine(panel.transform, "TutorLegalText",
            -18f, 36f, 22f, Color.white);
        tutor.reasonText = EnsureTutorLine(panel.transform, "TutorReasonText",
            -76f, 50f, 20f, new Color(0.86f, 0.86f, 0.86f));
        tutor.blockedText = EnsureTutorLine(panel.transform, "TutorBlockedText",
            -132f, 50f, 20f, new Color32(209, 213, 219, 255));
        panel.ApplyTutorReadability(tutor);
    }

    static TMP_Text EnsureTutorLine(Transform parent, string name, float y, float height,
                                    float fontSize, Color color)
    {
        var existing = parent.Find(name);
        var go = existing != null
            ? existing.gameObject
            : CreateChild(name, parent, typeof(TextMeshProUGUI));

        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(660f, height);
        rect.anchoredPosition = new Vector2(0f, y);

        var text = go.GetComponent<TextMeshProUGUI>();
        ApplyDefaultFont(text);
        text.text = string.Empty;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.TopLeft;
        // Clip instead of spilling: a long sentence must never cover the row underneath.
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        return text;
    }

    /// <summary>
    /// A white disc as a project asset, so the wheel can be shown inside the prefab editor.
    /// Generated once; later runs just load it.
    /// </summary>
    static Sprite EnsureDiscSprite()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(DiscPath);

        if (existing != null)
            return existing;

        if (!AssetDatabase.IsValidFolder(ArtFolder))
            AssetDatabase.CreateFolder("Assets/_Project", "Art");

        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        float centre = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - centre;
                float dy = y - centre;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                byte alpha = (byte)(Mathf.Clamp01(centre - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        File.WriteAllBytes(DiscPath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(DiscPath, ImportAssetOptions.ForceUpdate);

        if (AssetImporter.GetAtPath(DiscPath) is TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(DiscPath);
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
