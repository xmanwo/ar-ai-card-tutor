using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI.BodyUI;

/// <summary>
/// Controller-free controls. The HUD and ScanPanel buttons share one
/// index-finger ray and dwell timer.
///
/// This deliberately does NOT recognise gestures. It only asks "where is the fingertip pointing",
/// which is a continuous quantity, so degraded hand tracking while holding cards costs accuracy
/// instead of correctness. A 0.13 m button 0.30 m from the fingertip subtends ~25 deg; a single
/// card at table distance subtends ~10 deg. That factor of 2.5 is the whole reason this is viable.
///
/// Setup: drop this on an empty GameObject in the scene. It builds its own canvas, button, ray and
/// dwell ring at runtime, so there is no prefab to maintain. Nothing else to wire.
/// </summary>
[DisallowMultipleComponent]
public class HandRescanHud : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Leave empty to find the CardScanClient automatically.")]
    public CardScanClient client;

    [Tooltip("Leave empty to find the scan panel. Its buttons use the same finger ray as this HUD.")]
    public ScanPanel scanPanel;

    [Header("Placement (camera local space, metres)")]
    [Tooltip("X right, Y up, Z forward, relative to the head camera. -0.22 sits about 22 deg left.")]
    public Vector3 localOffset = new Vector3(-0.22f, 0.02f, 0.55f);

    [Tooltip("Edge length of the button in metres. 0.13 is generous on purpose - it buys tracking error budget.")]
    public float buttonSize = 0.13f;

    [Header("Aiming")]
    [Tooltip("Seconds the ray must rest on the button before the click fires.")]
    public float dwellSeconds = 0.5f;

    [Tooltip("Ray reach in metres. A hand hanging at your side can never trigger the button.")]
    public float maxRayDistance = 1.2f;

    [Tooltip("Seconds of smoothing on the fingertip ray. 0 = raw 30 Hz jitter, 0.06 = light, 0.2 = heavy.")]
    public float smoothingTau = 0.06f;

    [Tooltip("Ignore the ray when the fingertip is closer than this to the palm (a closed fist is not a point).")]
    public float minFingerReach = 0.035f;

    [Tooltip("Which hand to aim with first. Falls back to the other hand when this one is not tracked.")]
    public bool preferRightHand = true;

    [Tooltip("Draw the aiming line. Turn off if the line material renders magenta in your URP build.")]
    public bool showRayLine = true;

    [Header("Diagnostics")]
    [Tooltip("Log hand / controller state every 2 s. This is also the tool for the OpenXR mode question.")]
    public bool logDiagnostics = true;

    // Canvas is laid out in generic units and scaled down to metres, so all the UI numbers below
    // stay readable integers. 200 units == buttonSize metres.
    const float CanvasUnits = 200f;

    static readonly Color Navy = new Color(0.071f, 0.227f, 0.420f, 0.82f);
    static readonly Color Amber = new Color(0.851f, 0.467f, 0.024f, 1f);
    static readonly Color Paper = new Color(0.96f, 0.94f, 0.90f, 1f);

    static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

    XRHandSubsystem handSubsystem;
    RectTransform canvasRect;
    Button button;
    Button placementButton;
    TMP_Text placementLabel;
    Image buttonImage;
    Image ringImage;
    Image cursorImage;
    LineRenderer rayLine;

    float dwell;
    Button[] panelButtons = new Button[0];
    Button hoveredButton;
    internal Button AimedButton { get; private set; }
    HandScanProgress hoveredHandProgress;
    bool firedForHover;
    bool hasSmoothed;
    Vector3 smoothedOrigin;
    Vector3 smoothedDirection;
    float nextDiagnosticAt;
    bool lastHandTracked;
    Transform handMenuRoot;
    readonly List<Transform> handVisualizerRoots = new List<Transform>();
    Renderer[] handRenderers = new Renderer[0];
    readonly List<LineRenderer> mutedLines = new List<LineRenderer>();
    readonly List<XRInteractorLineVisual> mutedLineVisuals = new List<XRInteractorLineVisual>();
    float nextVisualSweepAt;
    float visualSweepInterval = 2f;
    int lastMutedTotal = -1;
    int handVisualSearches;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        BuildHud();
    }

    void Start()
    {
        if (client == null)
            client = FindFirstObjectByType<CardScanClient>();

        if (client == null)
            Debug.LogError("[HandHud] no CardScanClient found - the button will do nothing.");

        RefreshPanelButtons();
        SuppressOtherHandVisuals();
        StartCoroutine(SuppressHandVisualsDelayed());
        StartCoroutine(KeepHandMenuHidden());

        var cam = ScanPanel.FindHeadCamera();
        if (!PlaceAbovePhotoPanel())
            AttachToCamera(cam);
        if (cam != null && scanPanel != null && scanPanel.attachToCamera)
            BuildPlacementButton(cam);

        FindHandSubsystem();
        LogSubsystemReport();
    }

    /// <summary>Call after adding controls at runtime; Start order must not decide which buttons work.</summary>
    public void RefreshPanelButtons()
    {
        if (scanPanel == null)
            scanPanel = FindFirstObjectByType<ScanPanel>();
        panelButtons = scanPanel != null
            ? scanPanel.GetComponentsInChildren<Button>(true) : new Button[0];
    }

    /// <summary>
    /// The orange HUD line is the only hand ray this app uses. Disable the XRI near/far line
    /// visuals, any other line renderers, and the palm-up hand menu from the MR template.
    /// </summary>
    void SuppressOtherHandVisuals()
    {
        foreach (var menu in FindObjectsByType<HandMenu>(FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            menu.enabled = false;
            if (menu.handMenuUIGameObject != null)
                menu.handMenuUIGameObject.SetActive(false);
        }

        // The MR template names its palm menu instance in the scene, not through XRI HandMenu.
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (t.name == "Hand Menu Setup MR Template Variant")
            {
                handMenuRoot = t;
                t.gameObject.SetActive(false);
                break;
            }
            if (t.name == "AXR Settings")
                t.gameObject.SetActive(false);
        }

        // The XR Hands visualiser draws a translucent copy of the hand on top of the real one.
        // In passthrough that reads as a cloud of white shards, so the whole visual is hidden
        // and the user only sees their real hand plus the orange aim line.
        // Only search once: the visualiser objects are not recreated after they are switched off.
        if (handVisualizerRoots.Count == 0 && handVisualSearches < 10)
        {
            handVisualSearches++;
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                if (behaviour.GetType().Name != "HandVisualizer") continue;
                // Public property on the XR Hands sample: turns off the per-joint line renderers.
                var debugJoints = behaviour.GetType().GetProperty("debugDrawJoints",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (debugJoints != null && debugJoints.CanWrite &&
                    debugJoints.PropertyType == typeof(bool))
                    debugJoints.SetValue(behaviour, false);
                behaviour.enabled = false;
                handVisualizerRoots.Add(behaviour.transform);
            }
        }

        if (handVisualizerRoots.Count > 0)
        {
            // Keep the objects alive: only their renderers are unwanted, and switching a whole
            // object off risks taking the joint drivers the aim ray depends on with it.
            var renderers = new List<Renderer>();
            foreach (var root in handVisualizerRoots)
            {
                if (root == null) continue;
                renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
            }
            handRenderers = renderers.ToArray();
            foreach (var r in handRenderers)
                r.enabled = false;
        }

        foreach (var visual in FindObjectsByType<XRInteractorLineVisual>(FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            visual.enabled = false;
            if (!mutedLineVisuals.Contains(visual)) mutedLineVisuals.Add(visual);
            var line = visual.GetComponent<LineRenderer>();
            if (line != null) line.enabled = false;
            visual.gameObject.SetActive(false);
        }

        foreach (var line in FindObjectsByType<LineRenderer>(FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (line.GetComponentInParent<HandRescanHud>() != null) continue;
            line.enabled = false;
            if (!mutedLines.Contains(line)) mutedLines.Add(line);
        }

        int mutedTotal = mutedLines.Count + mutedLineVisuals.Count;
        if (mutedTotal != lastMutedTotal)
        {
            lastMutedTotal = mutedTotal;
            Debug.Log($"[HandHud] hid hand visuals (hand meshes={handVisualizerRoots.Count}, " +
                      $"lines={mutedLines.Count}, line visuals={mutedLineVisuals.Count}).");
        }
    }

    /// <summary>Anything re-enabled after the first sweep is turned off again on the next frame.</summary>
    void MuteHandVisuals()
    {
        for (int i = 0; i < mutedLines.Count; i++)
        {
            var line = mutedLines[i];
            if (line != null && line.enabled) line.enabled = false;
        }

        for (int i = 0; i < mutedLineVisuals.Count; i++)
        {
            var visual = mutedLineVisuals[i];
            if (visual != null && visual.enabled) visual.enabled = false;
        }

        for (int i = 0; i < handRenderers.Length; i++)
        {
            var r = handRenderers[i];
            if (r != null && r.enabled) r.enabled = false;
        }
    }

    System.Collections.IEnumerator SuppressHandVisualsDelayed()
    {
        yield return null;
        yield return null;
        SuppressOtherHandVisuals();

        // The visualiser builds its hand meshes after hand tracking starts, so sweep again.
        float[] waits = { 0.5f, 1f, 1.5f, 2f };
        foreach (float wait in waits)
        {
            yield return new WaitForSeconds(wait);
            SuppressOtherHandVisuals();
        }
    }

    System.Collections.IEnumerator KeepHandMenuHidden()
    {
        while (true)
        {
            if (handMenuRoot != null && handMenuRoot.gameObject.activeSelf)
                handMenuRoot.gameObject.SetActive(false);
            yield return null;
        }
    }

    void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(OnClick);
        if (placementButton != null)
            Destroy(placementButton.transform.parent.gameObject);
    }

    void OnDisable()
    {
        SetHandScanHover(null);
        hoveredButton = null;
        firedForHover = false;
        dwell = 0f;
        ShowAim(false);
    }

    // ------------------------------------------------------------------ build

    void BuildHud()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 200; // above ScanPanel (100) so the two can never fight

        canvasRect = transform as RectTransform;
        if (canvasRect == null)
        {
            Debug.LogError("[HandHud] the Canvas did not get a RectTransform; the HUD cannot be built. " +
                           "Put this component on an empty GameObject and let Awake add the Canvas.");
            enabled = false;
            return;
        }

        canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
        canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        canvasRect.sizeDelta = new Vector2(CanvasUnits, CanvasUnits);
        canvasRect.anchoredPosition = Vector2.zero;

        // Sensible size even if AttachToCamera never finds a camera (otherwise a 200-unit canvas
        // would sit in the world at 200 metres). AttachToCamera overwrites this with rig compensation.
        canvasRect.localScale = Vector3.one * (buttonSize / CanvasUnits);

        // Button body: a child that exactly fills the canvas. Keeping the graphic off the Canvas
        // object itself is the conventional layout and avoids any batching edge case.
        var buttonRect = NewRect("Button", canvasRect, CanvasUnits);

        buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = Navy;
        // A disc instead of a square, so the button reads as a round target like the dwell ring.
        buttonImage.sprite = MakeDiscSprite(128, 0f);

        var outline = buttonRect.gameObject.AddComponent<Outline>();
        outline.effectColor = Amber;
        outline.effectDistance = new Vector2(2.5f, -2.5f);
        outline.useGraphicAlpha = true;

        button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.35f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.onClick.AddListener(OnClick);

        var labelRect = NewRect("Label", buttonRect, CanvasUnits);
        var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = "RESCAN";
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 38;
        label.characterSpacing = 4f;
        label.color = Paper;
        label.raycastTarget = false;

        // Dwell ring: radial fill, sits under the cursor, follows the fingertip.
        ringImage = NewImage("DwellRing", canvasRect, CanvasUnits * 0.92f, Amber);
        ringImage.sprite = MakeDiscSprite(128, 0.80f);
        ringImage.type = Image.Type.Filled;
        ringImage.fillMethod = Image.FillMethod.Radial360;
        ringImage.fillOrigin = (int)Image.Origin360.Top;
        ringImage.fillClockwise = true;
        ringImage.fillAmount = 0f;
        ringImage.enabled = false;

        cursorImage = NewImage("Cursor", canvasRect, CanvasUnits * 0.10f, Paper);
        cursorImage.sprite = MakeDiscSprite(64, 0f);
        cursorImage.enabled = false;

        if (showRayLine)
            BuildRayLine();
    }

    void BuildPlacementButton(Camera cam)
    {
        const float unitsToMetres = 0.0007f;
        var root = new GameObject("UI Placement", typeof(RectTransform), typeof(Canvas));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvas.sortingOrder = 201;
        var rect = (RectTransform)root.transform;
        rect.SetParent(cam.transform, false);
        rect.localPosition = new Vector3(0f, -0.24f, 0.8f);
        var rigScale = cam.transform.lossyScale;
        rect.localScale = new Vector3(unitsToMetres / Safe(rigScale.x),
            unitsToMetres / Safe(rigScale.y), unitsToMetres / Safe(rigScale.z));
        rect.sizeDelta = new Vector2(220f, 70f);

        var target = NewRect("PlacementToggle", rect, 220f);
        target.sizeDelta = rect.sizeDelta;
        var background = target.gameObject.AddComponent<Image>();
        background.color = new Color32(64, 88, 108, 255);
        var border = target.gameObject.AddComponent<Outline>();
        border.effectColor = new Color32(157, 178, 192, 255);
        border.effectDistance = new Vector2(2f, -2f);
        placementButton = target.gameObject.AddComponent<Button>();
        placementButton.targetGraphic = background;
        placementButton.onClick.AddListener(TogglePanelPlacement);

        var label = NewRect("Label", target, 208f);
        label.sizeDelta = new Vector2(208f, 64f);
        placementLabel = label.gameObject.AddComponent<TextMeshProUGUI>();
        placementLabel.text = scanPanel.PlacementLocked ? "Move UI" : "Lock UI";
        placementLabel.fontSize = 34f;
        placementLabel.fontStyle = FontStyles.Bold;
        placementLabel.color = Paper;
        placementLabel.alignment = TextAlignmentOptions.Center;
        placementLabel.overflowMode = TextOverflowModes.Ellipsis;
        placementLabel.raycastTarget = false;
    }

    void TogglePanelPlacement()
    {
        if (scanPanel != null && scanPanel.TryTogglePlacementLock())
            placementLabel.text = scanPanel.PlacementLocked ? "Move UI" : "Lock UI";
    }

    void BuildRayLine()
    {
        var lineGo = new GameObject("AimRay");
        lineGo.transform.SetParent(transform, false);

        rayLine = lineGo.AddComponent<LineRenderer>();
        rayLine.useWorldSpace = true;
        rayLine.positionCount = 2;
        rayLine.startWidth = 0.0025f;
        rayLine.endWidth = 0.0012f;
        rayLine.numCapVertices = 4;
        rayLine.startColor = new Color(Amber.r, Amber.g, Amber.b, 0.85f);
        rayLine.endColor = new Color(Amber.r, Amber.g, Amber.b, 0.15f);
        rayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rayLine.receiveShadows = false;

        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("UI/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            Debug.LogWarning("[HandHud] no usable shader for the aim line; hiding it.");
            rayLine.enabled = false;
            rayLine = null;
            return;
        }

        var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        rayLine.sharedMaterial = mat;
        rayLine.enabled = false;
    }

    bool PlaceAbovePhotoPanel()
    {
        // CardHandView reparents the preview into the left photo panel during Awake.
        if (scanPanel == null || scanPanel.frameImage == null ||
            scanPanel.frameImage.rectTransform.parent is not RectTransform photoPanel ||
            photoPanel.parent != scanPanel.transform || !photoPanel.gameObject.activeInHierarchy)
            return false;

        // Half the original projected diameter after moving from the HUD depth to the panel.
        const float photoGap = 64f;
        float diameter = buttonSize * 0.5f * scanPanel.localOffset.z / (localOffset.z * scanPanel.panelScale);
        canvasRect.SetParent(photoPanel, false);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * (diameter / CanvasUnits);
        canvasRect.anchorMin = canvasRect.anchorMax = new Vector2(0.5f, 1f);
        canvasRect.anchoredPosition3D = new Vector3(18f, photoGap + diameter * 0.5f, 0f);
        Debug.Log($"[HandHud] Rescan aligned above {photoPanel.name}: diameter {diameter:F1}, gap {photoGap:F0}, depth offset 0.");
        return true;
    }

    void AttachToCamera(Camera cam)
    {
        if (cam == null)
        {
            Debug.LogWarning("[HandHud] no head camera found; leaving the HUD where it is.");
            return;
        }

        // With the photo panel tucked away, keep the hand target just below the left rail.
        if (scanPanel != null)
        {
            localOffset = new Vector3(scanPanel.localOffset.x,
                scanPanel.localOffset.y - 0.30f, scanPanel.localOffset.z);
        }

        transform.SetParent(cam.transform, false);
        transform.localPosition = localOffset;
        transform.localRotation = Quaternion.identity;

        // Metres -> canvas units, with any rig scale cancelled out.
        float metresPerUnit = buttonSize / CanvasUnits;
        var rigScale = cam.transform.lossyScale;
        transform.localScale = new Vector3(
            metresPerUnit / Safe(rigScale.x),
            metresPerUnit / Safe(rigScale.y),
            metresPerUnit / Safe(rigScale.z));

        Debug.Log($"[HandHud] attached to {cam.name}, offset {localOffset}, size {buttonSize} m.");
    }

    static float Safe(float value) => Mathf.Approximately(value, 0f) ? 1f : value;

    static RectTransform NewRect(string name, Transform parent, float size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = Vector2.zero;
        return rect;
    }

    static Image NewImage(string name, Transform parent, float size, Color color)
    {
        var rect = NewRect(name, parent, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>Annulus (or a filled dot when innerRatio is 0) as an anti-aliased sprite, built once.</summary>
    static Sprite MakeDiscSprite(int size, float innerRatio)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[size * size];
        float centre = (size - 1) * 0.5f;
        float outer = centre;
        float inner = outer * Mathf.Clamp01(innerRatio);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - centre;
                float dy = y - centre;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float outerEdge = Mathf.Clamp01(outer - distance);
                float innerEdge = inner <= 0f ? 1f : Mathf.Clamp01(distance - inner);
                byte alpha = (byte)(Mathf.Min(outerEdge, innerEdge) * 255f);

                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // ------------------------------------------------------------------ per frame

    void Update()
    {
        // Hand visuals can be rebuilt at runtime; keep them off without a per-frame scene scan.
        MuteHandVisuals();
        if (Time.unscaledTime >= nextVisualSweepAt)
        {
            nextVisualSweepAt = Time.unscaledTime + visualSweepInterval;
            visualSweepInterval = Mathf.Min(visualSweepInterval + 1f, 10f);
            SuppressOtherHandVisuals();
        }

        if (canvasRect == null)
            return;

        UpdateButtonAvailability();
        RunDiagnostics();

        if (!TryGetAimRay(out Vector3 origin, out Vector3 direction))
        {
            SetHandScanHover(null);
            ShowAim(false);
            // Lost tracking cancels progress, but does not re-arm a button already clicked.
            dwell = 0f;
            return;
        }

        UpdateAim(origin, direction, Time.unscaledDeltaTime);
    }

    void UpdateAim(Vector3 origin, Vector3 direction, float deltaTime)
    {
        var target = FindAimedButton(origin, direction, out Vector3 worldPoint);
        if (target != hoveredButton)
        {
            hoveredButton = target;
            dwell = 0f;
            firedForHover = false;
        }

        var progress = scanPanel != null && scanPanel.automaticHandScan && target != null
            ? target.GetComponent<HandScanProgress>() : null;
        SetHandScanHover(progress);
        if (target == null || progress != null || !target.IsInteractable() || firedForHover)
            dwell = 0f;
        else
            dwell += Mathf.Max(0f, deltaTime);

        ShowAim(target != null, target, origin, direction, worldPoint);
        if (target != null && dwell >= Mathf.Max(0.05f, dwellSeconds))
            Fire();
    }

    void SetHandScanHover(HandScanProgress progress)
    {
        if (progress == hoveredHandProgress) return;
        if (hoveredHandProgress != null) hoveredHandProgress.SetHandHover(false);
        hoveredHandProgress = progress;
        if (progress != null) progress.SetHandHover(true);
    }

    void UpdateButtonAvailability()
    {
        if (button == null || client == null)
            return;

        bool blocked = client.State == ScanState.Scanning || !client.CanRescanCurrent;
        if (button.interactable == blocked)
            button.interactable = !blocked;
    }

    /// <summary>
    /// Ray from the index fingertip along (fingertip - palm). Deliberately not the joint's own
    /// rotation: Meta does not guarantee a usable forward axis on the tip joint, and the palm
    /// difference is stable across hand orientations.
    /// </summary>
    bool TryGetAimRay(out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;

        if (handSubsystem == null || !handSubsystem.running)
        {
            hasSmoothed = false;
            return false;
        }

        bool gotHand = TryPickHand(out XRHand hand);
        if (!gotHand)
        {
            hasSmoothed = false;
            return false;
        }

        if (!hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose tipPose))
            return false;

        if (!hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palmPose))
            return false;

        Vector3 rawDirection = tipPose.position - palmPose.position;
        float reach = rawDirection.magnitude;

        // A fist has no meaningful direction; treat it as "not aiming" rather than guessing.
        if (reach < minFingerReach)
        {
            hasSmoothed = false;
            return false;
        }

        rawDirection /= reach;

        if (!hasSmoothed)
        {
            smoothedOrigin = tipPose.position;
            smoothedDirection = rawDirection;
            hasSmoothed = true;
        }
        else
        {
            float k = smoothingTau <= 0.0001f
                ? 1f
                : 1f - Mathf.Exp(-Time.deltaTime / smoothingTau);

            smoothedOrigin = Vector3.Lerp(smoothedOrigin, tipPose.position, k);

            // Slerp keeps the ray sweep at constant angular speed; a dot product near -1 would make
            // the interpolation ambiguous, so snap in that case.
            smoothedDirection = Vector3.Dot(smoothedDirection, rawDirection) < -0.98f
                ? rawDirection
                : Vector3.Slerp(smoothedDirection, rawDirection, k).normalized;
        }

        origin = smoothedOrigin;
        direction = smoothedDirection;
        return true;
    }

    bool TryPickHand(out XRHand hand)
    {
        bool rightTracked = handSubsystem.rightHand.isTracked;
        bool leftTracked = handSubsystem.leftHand.isTracked;

        if (preferRightHand)
        {
            if (rightTracked) { hand = handSubsystem.rightHand; return true; }
            if (leftTracked) { hand = handSubsystem.leftHand; return true; }
        }
        else
        {
            if (leftTracked) { hand = handSubsystem.leftHand; return true; }
            if (rightTracked) { hand = handSubsystem.rightHand; return true; }
        }

        hand = default;
        return false;
    }

    /// <summary>
    /// Hit-test the actual button rectangles, including inactive colour buttons once shown.
    /// Disabled buttons still count as the hovered target so a completed dwell cannot re-fire
    /// when scanning finishes and the same button becomes available again.
    /// </summary>
    Button FindAimedButton(Vector3 origin, Vector3 direction, out Vector3 worldPoint)
    {
        Button closest = null;
        float nearest = maxRayDistance;
        worldPoint = default;
        CheckButton(button, origin, direction, ref closest, ref nearest, ref worldPoint);
        CheckButton(placementButton, origin, direction, ref closest, ref nearest, ref worldPoint);
        foreach (var candidate in panelButtons)
            if (candidate != button)
                CheckButton(candidate, origin, direction, ref closest, ref nearest, ref worldPoint);
        return closest;
    }

    void CheckButton(Button candidate, Vector3 origin, Vector3 direction,
        ref Button closest, ref float nearest, ref Vector3 worldPoint)
    {
        if (candidate == null || !candidate.isActiveAndEnabled)
            return;

        // A colour-wheel slice covers the same rectangle as the other three, so the rectangular
        // test below would always report the first one. Let the slice answer by angle instead.
        var sector = candidate.GetComponent<WildColorSector>();

        if (sector != null)
        {
            if (!sector.TryHit(origin, direction, nearest, out Vector3 sectorPoint))
                return;

            closest = candidate;
            nearest = Vector3.Distance(origin, sectorPoint);
            worldPoint = sectorPoint;
            return;
        }

        var rect = (RectTransform)candidate.transform;
        Vector3 normal = rect.forward;
        float denominator = Vector3.Dot(direction, normal);
        if (Mathf.Abs(denominator) < 1e-4f)
            return;

        float distance = Vector3.Dot(rect.position - origin, normal) / denominator;
        if (distance <= 0f || distance > nearest)
            return;

        Vector3 point = origin + direction * distance;
        if (!rect.rect.Contains((Vector2)rect.InverseTransformPoint(point)))
            return;

        closest = candidate;
        nearest = distance;
        worldPoint = point;
    }

    void Fire()
    {
        dwell = 0f;
        firedForHover = true;

        if (ringImage != null)
            ringImage.fillAmount = 0f;

        Debug.Log($"[HandHud] dwell click {hoveredButton.name} at {Time.unscaledTime:F1}s");
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(hoveredButton.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }

    void OnClick()
    {
        if (client != null)
            client.Rescan();
    }

    void ShowAim(bool visible, Button target = null, Vector3 origin = default, Vector3 direction = default,
                 Vector3 worldPoint = default)
    {
        AimedButton = visible ? target : null;
        if (visible && target != null)
        {
            var rect = (RectTransform)target.transform;
            PlaceFeedback(cursorImage, rect, worldPoint, target == button ? 20f : 10f);
            PlaceFeedback(ringImage, rect, worldPoint, target == button ? 184f : 44f);
        }

        if (cursorImage != null)
        {
            cursorImage.enabled = visible;
        }

        // The ring only appears once the dwell actually starts, so an idle hand leaves no clutter.
        if (ringImage != null)
        {
            bool ringVisible = visible && dwell > 0.02f;
            ringImage.enabled = ringVisible;
            ringImage.fillAmount = Mathf.Clamp01(dwell / Mathf.Max(0.05f, dwellSeconds));
        }

        if (rayLine == null)
            return;

        if (!visible && direction == Vector3.zero)
        {
            rayLine.enabled = false;
            return;
        }

        Vector3 end = visible ? worldPoint : origin + direction * maxRayDistance;
        rayLine.enabled = true;
        rayLine.SetPosition(0, origin);
        rayLine.SetPosition(1, end);
    }

    void PlaceFeedback(Image image, RectTransform target, Vector3 point, float size)
    {
        if (image == null)
            return;

        // Keep feedback owned by this HUD, but draw it at the hit button's position and scale.
        var rect = image.rectTransform;
        rect.sizeDelta = Vector2.one * size;
        rect.SetPositionAndRotation(point - target.forward * 0.001f, target.rotation);
        rect.localScale = new Vector3(target.lossyScale.x / Safe(canvasRect.lossyScale.x),
            target.lossyScale.y / Safe(canvasRect.lossyScale.y), target.lossyScale.z / Safe(canvasRect.lossyScale.z));
    }

    // ------------------------------------------------------------------ diagnostics

    /// <summary>
    /// The one thing that has to be measured before any of this matters: with a controller in the
    /// room, does OpenXR still report hand joints? Read these lines through logcat.
    /// </summary>
    void RunDiagnostics()
    {
        if (!logDiagnostics || Time.unscaledTime < nextDiagnosticAt)
            return;

        nextDiagnosticAt = Time.unscaledTime + 2f;
        LogSubsystemReport();
    }

    void LogSubsystemReport()
    {
        FindHandSubsystem();

        bool rightTracked = handSubsystem != null && handSubsystem.rightHand.isTracked;
        bool leftTracked = handSubsystem != null && handSubsystem.leftHand.isTracked;

        if (rightTracked || leftTracked)
            lastHandTracked = true;

        var controller = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool controllerValid = controller.isValid;
        bool primaryReadable = controllerValid
            && controller.TryGetFeatureValue(CommonUsages.primaryButton, out bool _primaryPressed);

        Debug.Log($"[HandHud] hands={(handSubsystem != null ? (handSubsystem.running ? "running" : "stopped") : "none")} " +
                  $"left={leftTracked} right={rightTracked} | controller@RightHand={controllerValid} " +
                  $"primaryButton={primaryReadable} | seenHand={(lastHandTracked ? "yes" : "no")}");
    }

    void FindHandSubsystem()
    {
        if (handSubsystem != null && handSubsystem.running)
            return;

        s_Subsystems.Clear();
        SubsystemManager.GetSubsystems(s_Subsystems);

        handSubsystem = s_Subsystems.Count > 0 ? s_Subsystems[0] : null;

        // Prefer a running instance if the platform created several.
        for (int i = 0; i < s_Subsystems.Count; i++)
        {
            if (s_Subsystems[i].running)
            {
                handSubsystem = s_Subsystems[i];
                break;
            }
        }
    }
}
